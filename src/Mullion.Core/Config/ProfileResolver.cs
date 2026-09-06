using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;

namespace Mullion.Core.Config;

public enum ProfileMatch
{
    /// <summary>Same monitors, same positions. Use as-is.</summary>
    Exact,

    /// <summary>Same monitors, moved or resized. Reproject zones onto the new geometry.</summary>
    SameHardware,

    /// <summary>Some monitors in common. Reproject what matches, patch the rest.</summary>
    Partial,

    /// <summary>Nothing recognizable. Generate a fresh profile.</summary>
    None,
}

public sealed record ProfileResolution(
    ProfileMatch Match,
    ProfileRecord? Profile,
    string Explanation);

/// <summary>
/// Chooses which stored profile applies to the current displays.
/// <para>
/// Tiered rather than exact-only, because zones are stored as fractions of a
/// work area: a resolution change, a taskbar move or a monitor being dragged to
/// the other side of the desk should reuse the existing layout rather than
/// silently discard the user's configuration and start over.
/// </para>
/// </summary>
public static class ProfileResolver
{
    public static ProfileResolution Resolve(AppConfig config, IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return new(ProfileMatch.None, null, "No displays detected.");

        var fingerprint = TopologyFingerprint.Compute(displays);
        var currentKeys = displays.Select(d => d.StableKey).ToHashSet(StringComparer.Ordinal);

        var exact = config.Profiles.FirstOrDefault(p =>
            p.ArrangementFingerprint == fingerprint.Arrangement);

        if (exact is not null)
            return new(ProfileMatch.Exact, exact, $"Matched profile \"{exact.Name}\" exactly.");

        var sameHardware = config.Profiles.FirstOrDefault(p =>
            p.HardwareFingerprint == fingerprint.Hardware);

        if (sameHardware is not null)
        {
            return new(ProfileMatch.SameHardware, sameHardware,
                $"Same displays as \"{sameHardware.Name}\" but rearranged or resized; zones reprojected.");
        }

        // Best partial overlap: at least one shared display, covering at least
        // half of what is currently connected. Below that the layout would be
        // mostly guesswork and a fresh profile is more honest.
        var best = config.Profiles
            .Select(p => (Profile: p, Shared: p.Displays.Count(d => currentKeys.Contains(d.StableKey))))
            .Where(x => x.Shared > 0 && x.Shared * 2 >= currentKeys.Count)
            .OrderByDescending(x => x.Shared)
            .ThenByDescending(x => x.Profile.LastUsedUtc)
            .FirstOrDefault();

        if (best.Profile is not null)
        {
            return new(ProfileMatch.Partial, best.Profile,
                $"Partially matched \"{best.Profile.Name}\" ({best.Shared} of {currentKeys.Count} displays); " +
                "missing displays dropped and new ones added.");
        }

        return new(ProfileMatch.None, null, "New display setup; generating a layout.");
    }

    /// <summary>Build a profile record from a generated layout.</summary>
    public static ProfileRecord CreateProfile(
        IReadOnlyList<DisplayInfo> displays,
        LayoutResult layout,
        string? name = null)
    {
        var fingerprint = TopologyFingerprint.Compute(displays);

        return new ProfileRecord
        {
            Id = Guid.NewGuid(),
            Name = name ?? SuggestName(displays),
            HardwareFingerprint = fingerprint.Hardware,
            ArrangementFingerprint = fingerprint.Arrangement,
            SurfaceId = layout.Surface.Id,
            LastUsedUtc = DateTimeOffset.UtcNow,
            Displays = [.. displays.Select(d => new DisplaySnapshot(
                d.StableKey, d.FriendlyName, d.Bounds, d.WorkArea, d.Dpi, d.IsPrimary))],
            Zones = [.. layout.Zones.Select(z => new ZoneRecord(
                z.Name,
                z.Position.Row,
                z.Position.Col,
                [.. z.Parts.Select(p => new ZonePartRecord(p.DisplayKey, p.Area))],
                z.Kind.ToString()))],
        };
    }

    /// <summary>Rebuild a layout from a stored profile, dropping zones whose display is gone.</summary>
    public static LayoutResult ToLayout(ProfileRecord profile, IReadOnlyList<DisplayInfo> displays)
    {
        var surface = KeySurface.All.FirstOrDefault(s => s.Id == profile.SurfaceId) ?? KeySurface.LeftHandBlock;
        var present = displays.Select(d => d.StableKey).ToHashSet(StringComparer.Ordinal);
        var notes = new List<string>();

        var zones = new List<Zone>();

        foreach (var record in profile.Zones)
        {
            var parts = record.Parts.Where(p => present.Contains(p.DisplayKey)).ToList();

            if (parts.Count == 0)
            {
                notes.Add($"Zone \"{record.Name}\" was dropped: its display is not connected.");
                continue;
            }

            if (parts.Count < record.Parts.Count)
                notes.Add($"Zone \"{record.Name}\" lost part of its span: a display is not connected.");

            zones.Add(new Zone
            {
                Id = Guid.NewGuid(),
                Name = record.Name,
                Parts = [.. parts.Select(p => new ZonePart(p.DisplayKey, p.Area))],
                Position = new GridPos(record.Row, record.Col),
                Kind = Enum.TryParse<ZoneKind>(record.Kind, out var kind) ? kind : ZoneKind.Region,
            });
        }

        return new LayoutResult(zones, surface, notes);
    }

    private static string SuggestName(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 1)
        {
            var only = displays[0];
            return $"{only.FriendlyName} — {only.Bounds.Width}×{only.Bounds.Height}";
        }

        var largest = displays.MaxBy(d => d.Bounds.Area)!;
        return $"{displays.Count} displays — {largest.Bounds.Width}×{largest.Bounds.Height}";
    }
}
