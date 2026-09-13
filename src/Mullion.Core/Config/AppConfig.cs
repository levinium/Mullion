using System.Text.Json;
using System.Text.Json.Serialization;
using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;

namespace Mullion.Core.Config;

public sealed record DisplaySnapshot(
    string StableKey,
    string FriendlyName,
    PxRect Bounds,
    PxRect WorkArea,
    uint Dpi,
    bool IsPrimary);

public sealed record ZonePartRecord(string DisplayKey, NormRect Area);

/// <param name="Modifier">
/// The chord this zone is taken with, when it is not the configured default -
/// "Ctrl+Shift", and so on. Null means "whatever the default is", so a zone
/// nobody has rebound follows that setting when it changes.
/// <para>
/// Optional, and absent from files written before it existed: those zones read
/// back as null, which is exactly what they were. No migration needed.
/// </para>
/// </param>
public sealed record ZoneRecord(
    string Name,
    int Row,
    int Col,
    IReadOnlyList<ZonePartRecord> Parts,
    string Kind,
    string? Modifier = null,

    /// <param name="Key">
    /// The key this zone was put on by hand, when it is not the one its place on
    /// the surface implies. Written as the scan code, with "e" in front for the
    /// E0-prefixed keys - the arrows and the navigation cluster, which share
    /// codes with the numpad and are otherwise indistinguishable from it.
    /// Absent for every zone nobody has moved, which is nearly all of them.
    /// </param>
    string? Key = null);

public sealed record ProfileRecord
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string HardwareFingerprint { get; init; }
    public required string ArrangementFingerprint { get; init; }
    public required IReadOnlyList<DisplaySnapshot> Displays { get; init; }
    public required IReadOnlyList<ZoneRecord> Zones { get; init; }

    public string SurfaceId { get; init; } = KeySurface.LeftHandBlock.Id;
    public string Modifiers { get; init; } = nameof(ChordModifiers.Win);
    public DateTimeOffset LastUsedUtc { get; init; }

    /// <summary>True once the user has edited zones, which stops regeneration overwriting them.</summary>
    public bool Customized { get; init; }
}

public sealed record GeneralSettings
{
    public string AutoStart { get; init; } = nameof(Core.Abstractions.AutoStartMode.Disabled);
    public bool ShowZoneFlash { get; init; } = true;
    public bool PauseWhenFullscreen { get; init; } = true;
    public string WinKeySuppression { get; init; } = nameof(Hotkeys.WinKeySuppression.DummyKey);
    public bool IgnoreInjectedInput { get; init; }
    public bool AllowSpanningUnions { get; init; } = true;
    /// <summary>Modifier every zone hotkey is taken with, e.g. "Win" or "Ctrl+Alt".</summary>
    public string HotkeyModifier { get; init; } = Hotkeys.ModifierChoice.Default;

    public bool DragToSnap { get; init; } = true;

    /// <summary>Snap a dragged split to the grid and to exact-aspect positions.</summary>
    public bool SnapSplits { get; init; } = true;

    /// <summary>Go straight to the tray even when launched by hand, not just at sign-in.</summary>
    public bool StartInTray { get; init; }
    public string DragModifier { get; init; } = nameof(Model.DragModifier.Shift);
    public int UndoDepth { get; init; } = 20;
    public string Theme { get; init; } = "system";
    public ShapeTuningRecord Shape { get; init; } = new();

    /// <summary>
    /// Look once a day for a newer release.
    /// <para>
    /// On by default, which is a decision worth writing down rather than
    /// leaving as a default nobody chose. The app is one file that people copy
    /// wherever they like: there is no installer to tell them anything, no
    /// package manager watching, and no reason they would ever revisit the
    /// page they got it from. Off by default means a fix ships and the people
    /// it was written for never hear about it.
    /// </para>
    /// <para>
    /// What it costs them is one HTTP GET a day for a public file, carrying no
    /// identifier of any kind. It is stated plainly in Settings next to the
    /// switch, and the switch genuinely stops it - nothing else in the app
    /// makes a network request, so turning this off makes Mullion silent.
    /// </para>
    /// </summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>
    /// When the last check finished, so a machine that is signed into ten times
    /// a day still asks once. Null until the first one completes.
    /// </summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }
}

/// <summary>The four tuning constants, surfaced so an unusual panel is retuned rather than special-cased.</summary>
public sealed record ShapeTuningRecord
{
    public double ZoneAspectMax { get; init; } = 2.20;
    public double ZoneAspectMin { get; init; } = 0.62;
    public double MinZoneLogicalPx { get; init; } = 560;
    public double PreferredZoneAspect { get; init; } = 1.15;

    public ShapeTuning ToTuning() => new()
    {
        ZoneAspectMax = ZoneAspectMax,
        ZoneAspectMin = ZoneAspectMin,
        MinZoneLogicalPx = MinZoneLogicalPx,
        PreferredZoneAspect = PreferredZoneAspect,
    };
}

public sealed record AppConfig
{
    /// <summary>
    /// 1 - initial.
    /// 2 - zone names switched to American spelling ("centre" -> "center").
    /// 3 - zone names dropped their display-name prefix ("C49RG9x left" -> "Left").
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool WizardCompleted { get; init; }
    public GeneralSettings General { get; init; } = new();
    public IReadOnlyList<ProfileRecord> Profiles { get; init; } = [];
    public Guid? ActiveProfileId { get; init; }

    /// <summary>
    /// Hand-made splits, keyed by the place a display occupies rather than by
    /// which monitor it is. Kept outside Profiles on purpose: a profile is a
    /// generated answer for one arrangement, while these are the parts the user
    /// decided for themselves and expects to survive.
    /// </summary>
    public IReadOnlyList<DisplayOverride> Overrides { get; init; } = [];

    /// <summary>
    /// The hotkeys that are not zones - undo, minimize - where they have been
    /// changed from what Mullion ships with.
    /// <para>
    /// Only the changed ones. An empty list means "the defaults", so a new
    /// action added in a later version reaches everybody instead of only the
    /// people who have never opened the settings screen.
    /// </para>
    /// </summary>
    public IReadOnlyList<ActionRecord> Actions { get; init; } = [];
}

/// <param name="Command">Which action: see <see cref="Hotkeys.GlobalAction"/>.</param>
/// <param name="Key">The physical key, written as <see cref="Hotkeys.KeyText"/> does.</param>
/// <param name="Modifier">
/// Its own chord, or null to follow the configured default - the same rule zones
/// follow, so changing the default modifier moves everything nobody has pinned.
/// </param>
public sealed record ActionRecord(string Command, string Key, string? Modifier = null);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
public partial class AppJsonContext : JsonSerializerContext;

/// <summary>Compact array forms for the two geometry types, so config stays readable.</summary>
public sealed class PxRectJsonConverter : JsonConverter<PxRect>
{
    public override PxRect Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<int[]>(ref reader, options) ?? [];
        return values.Length == 4 ? new PxRect(values[0], values[1], values[2], values[3]) : default;
    }

    public override void Write(Utf8JsonWriter writer, PxRect value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}

public sealed class NormRectJsonConverter : JsonConverter<NormRect>
{
    public override NormRect Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<double[]>(ref reader, options) ?? [];
        return values.Length == 4 ? new NormRect(values[0], values[1], values[2], values[3]) : default;
    }

    public override void Write(Utf8JsonWriter writer, NormRect value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 6));
        writer.WriteNumberValue(Math.Round(value.Y, 6));
        writer.WriteNumberValue(Math.Round(value.W, 6));
        writer.WriteNumberValue(Math.Round(value.H, 6));
        writer.WriteEndArray();
    }
}
