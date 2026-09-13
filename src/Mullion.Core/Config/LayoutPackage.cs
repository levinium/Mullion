using System.Text.Json;
using System.Text.Json.Serialization;
using Mullion.Core.Layout;

namespace Mullion.Core.Config;

/// <summary>
/// A hand-made layout, in a form that can be written to a file and read on
/// another machine.
/// <para>
/// Only the parts somebody chose: the splits they set, and the desk those splits
/// were made for. Not the generated profiles, which are answers Mullion can work
/// out again, and not the monitors' identities, which will not match anywhere
/// else. That is what lets a layout move between machines with the same desk
/// shape rather than only back onto the one it came from.
/// </para>
/// </summary>
public sealed record LayoutPackage
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Free text, so a file is identifiable without opening it.</summary>
    public string Name { get; init; } = "Mullion layout";

    public DateTimeOffset ExportedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The arrangement it was made for, as slots. Informational: shown before importing.</summary>
    public IReadOnlyList<string> Slots { get; init; } = [];

    public IReadOnlyList<DisplayOverride> Overrides { get; init; } = [];

    /// <summary>Which key block the splits were mapped onto, so keys land where they did.</summary>
    public string SurfaceId { get; init; } = Hotkeys.KeySurface.LeftHandBlock.Id;
}

/// <summary>What an import would do, so it can be reported before it is done.</summary>
/// <param name="Matching">Slots in the file that exist on this desk.</param>
/// <param name="Missing">Slots in the file with no display here. Kept, but inert until one appears.</param>
public sealed record ImportPreview(
    LayoutPackage Package,
    IReadOnlyList<string> Matching,
    IReadOnlyList<string> Missing)
{
    public bool AnythingApplies => Matching.Count > 0;
}

public static class LayoutPackageIo
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(LayoutPackage package) =>
        JsonSerializer.Serialize(package, Options);

    /// <summary>
    /// Returns null rather than throwing on anything unreadable: this is a file
    /// a user picked, so being handed the wrong one is ordinary, not exceptional.
    /// </summary>
    public static LayoutPackage? Deserialize(string json, out string? error)
    {
        error = null;

        try
        {
            var package = JsonSerializer.Deserialize<LayoutPackage>(json, Options);

            if (package is null)
            {
                error = "That file is empty.";
                return null;
            }

            if (package.Version > LayoutPackage.CurrentVersion)
            {
                error = $"That layout was saved by a newer version of Mullion " +
                        $"(format {package.Version}, this build reads {LayoutPackage.CurrentVersion}).";
                return null;
            }

            if (package.Overrides.Count == 0)
            {
                error = "That file has no custom layout in it.";
                return null;
            }

            return package;
        }
        catch (JsonException)
        {
            error = "That file is not a Mullion layout.";
            return null;
        }
    }

    /// <summary>Build a package from what is currently customized.</summary>
    public static LayoutPackage Export(
        IReadOnlyList<DisplayOverride> overrides, IReadOnlyList<string> slots, string surfaceId, string name) =>
        new()
        {
            Name = name,
            Slots = slots,
            Overrides = [.. overrides.Where(o => !o.IsEmpty)],
            SurfaceId = surfaceId,
        };

    /// <summary>
    /// What importing would change, without changing it. An override whose slot
    /// is not on this desk is kept rather than dropped - plugging that monitor
    /// back in should bring its layout with it.
    /// </summary>
    public static ImportPreview Preview(LayoutPackage package, IReadOnlyList<string> presentSlots)
    {
        var present = presentSlots.ToHashSet(StringComparer.Ordinal);

        return new ImportPreview(
            package,
            [.. package.Overrides.Select(o => o.Slot).Where(present.Contains).Distinct()],
            [.. package.Overrides.Select(o => o.Slot).Where(s => !present.Contains(s)).Distinct()]);
    }

    /// <summary>
    /// Merge imported overrides over the existing ones, replacing by slot.
    /// <para>
    /// Replace rather than append: two overrides for one slot is a contradiction,
    /// and the imported one is the one just asked for.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DisplayOverride> Merge(
        IReadOnlyList<DisplayOverride> existing, IReadOnlyList<DisplayOverride> imported)
    {
        var merged = existing.Where(o => !o.IsEmpty).ToDictionary(o => o.Slot, StringComparer.Ordinal);

        foreach (var o in imported.Where(o => !o.IsEmpty))
            merged[o.Slot] = o;

        return [.. merged.Values];
    }
}
