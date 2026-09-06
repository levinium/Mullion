using Mullion.Core.Geometry;
using Mullion.Core.Layout;

namespace Mullion.Core.Model;

/// <summary>Physical rotation, informational only - the maths uses resulting bounds.</summary>
public enum Rotation
{
    None,
    Cw90,
    Cw180,
    Cw270,
}

/// <summary>
/// A connected display. Deliberately carries no display-class enum: shape is
/// continuous and derived from <see cref="Bounds"/> via <see cref="ShapeAnalyzer"/>.
/// </summary>
public sealed record DisplayInfo
{
    /// <summary>
    /// EDID-derived, stable across replugging and GDI renumbering, e.g. "DEL-A0FD".
    /// Duplicates of the same model are disambiguated deterministically ("DEL-A0FD#2").
    /// </summary>
    public required string StableKey { get; init; }

    /// <summary>Volatile GDI name (\\.\DISPLAY1). Diagnostics only - never persisted as identity.</summary>
    public required string GdiDeviceName { get; init; }

    public required string FriendlyName { get; init; }

    /// <summary>Full bounds in virtual-desktop physical pixels. May have negative origin.</summary>
    public required PxRect Bounds { get; init; }

    /// <summary>Bounds minus taskbar and other appbars. Zones are relative to THIS.</summary>
    public required PxRect WorkArea { get; init; }

    /// <summary>Effective DPI; 96 == 100% scaling.</summary>
    public required uint Dpi { get; init; }

    public required bool IsPrimary { get; init; }

    public Rotation Rotation { get; init; } = Rotation.None;

    /// <summary>Opaque platform handle (HMONITOR). Volatile, never serialized.</summary>
    public nint Handle { get; init; }

    public double Scale => Dpi / 96.0;

    public Orientation Orientation => ShapeAnalyzer.OrientationOf(Bounds);

    /// <summary>Axis this display subdivides along: columns if landscape, rows if portrait.</summary>
    public Axis SplitAxis => ShapeAnalyzer.SplitAxis(Bounds);

    /// <summary>Long-over-short, so identical whether the panel is rotated or not.</summary>
    public double Elongation => Bounds.Elongation;

    public string Label =>
        $"{FriendlyName} · {Bounds.Width}×{Bounds.Height}" + (Dpi != 96 ? $" @{Scale:P0}" : string.Empty);

    public override string ToString() => $"{StableKey} {Bounds} dpi={Dpi}{(IsPrimary ? " primary" : "")}";
}
