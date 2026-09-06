using Mullion.Core.Config;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class TopologyFingerprintTests
{
    [Fact]
    public void IsInvariantUnderEnumerationOrder()
    {
        var displays = TestDisplays.ThreeAcross();
        var shuffled = displays.AsEnumerable().Reverse().ToList();

        TopologyFingerprint.Compute(shuffled).ShouldBe(TopologyFingerprint.Compute(displays));
    }

    [Fact]
    public void HardwareSurvivesRearrangementButArrangementDoesNot()
    {
        var before = TestDisplays.TwoAcross();

        // Same two monitors, sides swapped.
        var after = new List<DisplayInfo>
        {
            before[1] with { Bounds = before[0].Bounds, WorkArea = before[0].WorkArea },
            before[0] with { Bounds = before[1].Bounds, WorkArea = before[1].WorkArea },
        };

        var a = TopologyFingerprint.Compute(before);
        var b = TopologyFingerprint.Compute(after);

        a.Hardware.ShouldBe(b.Hardware, "the same monitors are still connected");
        a.Arrangement.ShouldNotBe(b.Arrangement, "but they are positioned differently");
    }

    [Fact]
    public void ChangesWhenResolutionOrDpiOrPrimaryChanges()
    {
        var baseline = TopologyFingerprint.Compute(TestDisplays.SuperUltrawideAlone());

        var resized = TestDisplays.SuperUltrawideAlone();
        resized[0] = resized[0] with { Bounds = new Mullion.Core.Geometry.PxRect(0, 0, 3840, 1080) };
        TopologyFingerprint.Compute(resized).Arrangement.ShouldNotBe(baseline.Arrangement);

        var scaled = TestDisplays.SuperUltrawideAlone();
        scaled[0] = scaled[0] with { Dpi = 144 };
        TopologyFingerprint.Compute(scaled).Arrangement.ShouldNotBe(baseline.Arrangement);
    }

    /// <summary>
    /// GDI names are renumbered by Windows; identity must not depend on them or
    /// profiles would thrash after an unrelated reboot.
    /// </summary>
    [Fact]
    public void IgnoresGdiDeviceNameChanges()
    {
        var a = TestDisplays.TwoAcross();
        var b = TestDisplays.TwoAcross();
        b[0] = b[0] with { GdiDeviceName = @"\\.\DISPLAY7" };
        b[1] = b[1] with { GdiDeviceName = @"\\.\DISPLAY3" };

        TopologyFingerprint.Compute(a).ShouldBe(TopologyFingerprint.Compute(b));
    }
}

public class ProfileResolverTests
{
    private static AppConfig ConfigWith(params ProfileRecord[] profiles) =>
        new() { Profiles = profiles };

    private static ProfileRecord ProfileFor(List<DisplayInfo> displays, string name)
    {
        var layout = LayoutBuilder.Build(displays);
        return ProfileResolver.CreateProfile(displays, layout, name) with
        {
            LastUsedUtc = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void ExactArrangementMatchesDirectly()
    {
        var displays = TestDisplays.ThreeAcross();
        var config = ConfigWith(ProfileFor(displays, "Desk"));

        var result = ProfileResolver.Resolve(config, displays);

        result.Match.ShouldBe(ProfileMatch.Exact);
        result.Profile!.Name.ShouldBe("Desk");
    }

    /// <summary>
    /// Zones are fractions of a work area, so a resolution change should reuse
    /// the profile rather than discard the user's configuration.
    /// </summary>
    [Fact]
    public void ResolutionChangeStillMatchesTheSameHardware()
    {
        var original = TestDisplays.SuperUltrawideAlone();
        var config = ConfigWith(ProfileFor(original, "Desk"));

        var downscaled = TestDisplays.SuperUltrawideAlone();
        downscaled[0] = downscaled[0] with
        {
            Bounds = new Mullion.Core.Geometry.PxRect(0, 0, 3840, 1080),
            WorkArea = new Mullion.Core.Geometry.PxRect(0, 0, 3840, 1032),
        };

        var result = ProfileResolver.Resolve(config, downscaled);

        result.Match.ShouldBe(ProfileMatch.SameHardware);
        result.Profile!.Name.ShouldBe("Desk");
    }

    [Fact]
    public void UndockingFallsBackToAPartialMatch()
    {
        var docked = TestDisplays.ThreeAcross();
        var config = ConfigWith(ProfileFor(docked, "Docked"));

        // Two of the three displays remain.
        var partial = docked.Take(2).ToList();

        var result = ProfileResolver.Resolve(config, partial);

        result.Match.ShouldBe(ProfileMatch.Partial);
        result.Profile!.Name.ShouldBe("Docked");
    }

    [Fact]
    public void CompletelyDifferentHardwareGetsAFreshProfile()
    {
        var config = ConfigWith(ProfileFor(TestDisplays.ThreeAcross(), "Desk"));

        var result = ProfileResolver.Resolve(config, TestDisplays.SuperUltrawideAlone());

        result.Match.ShouldBe(ProfileMatch.None);
        result.Profile.ShouldBeNull();
    }

    [Fact]
    public void RoundTripsALayoutThroughAProfile()
    {
        var displays = TestDisplays.VerticalsFlankingStackedPair();
        var original = LayoutBuilder.Build(displays);
        var profile = ProfileResolver.CreateProfile(displays, original);

        var restored = ProfileResolver.ToLayout(profile, displays);

        restored.Zones.Count.ShouldBe(original.Zones.Count);
        restored.Surface.Id.ShouldBe(original.Surface.Id);

        foreach (var zone in original.Zones)
        {
            var match = restored.At(zone.Position);
            match.ShouldNotBeNull($"zone at {zone.Position} lost in round trip");
            match.Name.ShouldBe(zone.Name);
            match.Parts.Count.ShouldBe(zone.Parts.Count);
        }
    }

    [Fact]
    public void DropsZonesWhoseDisplayIsGoneRatherThanFailing()
    {
        var displays = TestDisplays.ThreeAcross();
        var profile = ProfileResolver.CreateProfile(displays, LayoutBuilder.Build(displays));

        var restored = ProfileResolver.ToLayout(profile, displays.Take(2).ToList());

        restored.Zones.ShouldAllBe(z => z.Parts.Count > 0);
        restored.Notes.ShouldContain(n => n.Contains("not connected"));
    }
}

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mullion-test-{Guid.NewGuid():N}");

    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    [Fact]
    public void RoundTripsAConfigWithProfiles()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var profile = ProfileResolver.CreateProfile(displays, LayoutBuilder.Build(displays), "Desk");

        var store = new ConfigStore(ConfigPath);
        var original = new AppConfig
        {
            WizardCompleted = true,
            Profiles = [profile],
            ActiveProfileId = profile.Id,
        };

        store.Save(original);
        var loaded = new ConfigStore(ConfigPath).Load();

        loaded.WizardCompleted.ShouldBeTrue();
        loaded.ActiveProfileId.ShouldBe(profile.Id);
        loaded.Profiles.Count.ShouldBe(1);
        loaded.Profiles[0].Zones.Count.ShouldBe(profile.Zones.Count);
        loaded.Profiles[0].Zones[0].Parts[0].Area.ShouldBe(profile.Zones[0].Parts[0].Area);
    }

    [Fact]
    public void CorruptConfigFallsBackToBackupRatherThanLosingEverything()
    {
        var store = new ConfigStore(ConfigPath);

        store.Save(new AppConfig { WizardCompleted = true });
        store.Save(new AppConfig { WizardCompleted = true });   // creates the .bak

        File.WriteAllText(ConfigPath, "{ this is not json");

        var loaded = new ConfigStore(ConfigPath).Load();

        loaded.WizardCompleted.ShouldBeTrue("should have recovered from the backup");
    }

    [Fact]
    public void MissingConfigYieldsDefaultsWithoutThrowing()
    {
        var loaded = new ConfigStore(Path.Combine(_dir, "nope.json")).Load();

        loaded.SchemaVersion.ShouldBe(AppConfig.CurrentSchemaVersion);
        loaded.Profiles.ShouldBeEmpty();
        loaded.WizardCompleted.ShouldBeFalse();
    }

    [Fact]
    public void UnreadableConfigWithNoBackupYieldsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, "@@@ not json @@@");

        var store = new ConfigStore(ConfigPath);
        var loaded = store.Load();

        loaded.Profiles.ShouldBeEmpty();
        store.LoadNotes.ShouldNotBeEmpty();
    }

    /// <summary>A config from a newer build must be preserved, not silently reset.</summary>
    [Fact]
    public void NewerSchemaIsFlaggedButKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, """{"schemaVersion": 99, "wizardCompleted": true}""");

        var store = new ConfigStore(ConfigPath);
        var loaded = store.Load();

        loaded.WizardCompleted.ShouldBeTrue();
        store.LoadNotes.ShouldContain(n => n.Contains("newer than this build"));
    }
}
