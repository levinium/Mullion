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

public sealed record ZoneRecord(
    string Name,
    int Row,
    int Col,
    IReadOnlyList<ZonePartRecord> Parts,
    string Kind);

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

    /// <summary>Go straight to the tray even when launched by hand, not just at sign-in.</summary>
    public bool StartInTray { get; init; }
    public string DragModifier { get; init; } = nameof(Model.DragModifier.Shift);
    public int UndoDepth { get; init; } = 20;
    public string Theme { get; init; } = "system";
    public ShapeTuningRecord Shape { get; init; } = new();
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
}

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
