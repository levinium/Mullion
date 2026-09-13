using Avalonia;
// Core has an Orientation too - Landscape/Portrait, a different question from
// which way a seam runs.
using Orientation = Avalonia.Layout.Orientation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Controls;
using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;

namespace Mullion.App.ViewModels;

/// <summary>
/// One drawable cell of a display.
/// <para>
/// A cell is not simply a zone. Where the key surface has rows to spare, a
/// column holds a whole-column zone AND its two halves - three zones occupying
/// overlapping space. Drawing all three as rectangles stacks them and the labels
/// collide. So the biggest zone becomes the cell's rectangle and the subzones
/// are drawn as chips inside it, which states "A is the whole column, Q one half
/// and Z the other" without overlap.
///
/// Which way those halves cut the column is not fixed - see TierAxis. The cell
/// reads it off the rectangles rather than being told, so the drawing cannot
/// disagree with the geometry.
/// </para>
/// </summary>
public sealed partial class ZoneCellViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCapturing;

    public required string Name { get; init; }
    public required string KeyLabel { get; init; }

    private Rect _area;

    /// <summary>
    /// The cell's rectangle, as a fraction of its display.
    /// <para>
    /// Settable rather than init-only so a seam drag can redraw the zones under
    /// the cursor. Committing first and redrawing after would mean saving a
    /// layout the user has not seen, and dragging against a static picture is
    /// guessing.
    /// </para>
    /// </summary>
    public required Rect Area
    {
        get => _area;
        set => SetProperty(ref _area, value);
    }

    public required string SizeLabel { get; init; }
    public required bool SpansDisplays { get; init; }

    /// <summary>Where this cell sits on the key surface, so a click can rebind it.</summary>
    public GridPos Position { get; init; }

    /// <summary>Set only when the diagram is interactive; null elsewhere.</summary>
    public Action<GridPos>? Activated { get; set; }

    public bool IsInteractive => Activated is not null;

    [RelayCommand]
    private void Activate() => Activated?.Invoke(Position);

    /// <summary>
    /// Which way this zone's two subzones cut it.
    /// <para>
    /// Stacked halves are a letterbox pair on anything wide, so the axis is
    /// chosen per zone from the same aspect band the engine already holds zones
    /// to. The diagram has to follow it: subzones drawn the wrong way round are
    /// a picture that contradicts the key it is labelled with.
    /// </para>
    /// </summary>
    public Axis TierAxis { get; init; } = Axis.Vertical;

    /// <summary>The same question as TierAxis, in the form a binding can ask.</summary>
    public bool IsSideBySide => TierAxis == Axis.Horizontal;

    /// <summary>
    /// Turning this zone's subzones through a right angle, while editing.
    /// <para>
    /// Addressed by desk slot and zone index rather than by grid position,
    /// matching how the override is stored: the choice belongs to a place on the
    /// desk, so it survives a monitor being swapped for an identical one, and it
    /// must not move when the zone is rebound to a different key.
    /// </para>
    /// </summary>
    public string? Slot { get; init; }

    public int ZoneIndex { get; init; }

    public Action<string, int>? AxisFlipRequested { get; set; }

    /// <summary>
    /// Offered only where there is something to turn. A zone with no subzones -
    /// the top or bottom row of the surface, where the second key falls off the
    /// edge - has no axis to speak of.
    /// </summary>
    public bool CanFlipAxis =>
        AxisFlipRequested is not null && Slot is not null && (HasFirst || HasSecond);

    /// <summary>What the button will do, since it is an icon and says nothing itself.</summary>
    public string FlipHint => TierAxis == Axis.Vertical
        ? "Split this zone side by side instead"
        : "Split this zone top and bottom instead";

    [RelayCommand]
    private void FlipAxis()
    {
        if (Slot is not null) AxisFlipRequested?.Invoke(Slot, ZoneIndex);
    }

    /// <summary>
    /// Where the two subzones sit in a fixed 2x2 grid, spanning the axis they do
    /// not divide.
    /// <para>
    /// A fixed grid with bound placement rather than a UniformGrid that reshapes
    /// itself: a UniformGrid packs by child order, so a zone with only one half
    /// - the top or bottom row of the surface, where the other key falls off the
    /// edge - would have the remaining half slide into the first cell and draw
    /// the right half on the left.
    /// </para>
    /// </summary>
    public int FirstRow => 0;

    public int FirstColumn => 0;

    public int SecondRow => TierAxis == Axis.Vertical ? 1 : 0;

    public int SecondColumn => TierAxis == Axis.Vertical ? 0 : 1;

    /// <summary>Each half spans the whole of the axis it does not cut.</summary>
    public int TierRowSpan => TierAxis == Axis.Vertical ? 1 : 2;

    public int TierColumnSpan => TierAxis == Axis.Vertical ? 2 : 1;

    /// <summary>
    /// Which way a subzone's chip and its size label are laid out.
    /// <para>
    /// Along whichever axis the half actually has room in, which is always the
    /// one it was NOT cut along. Stacked halves are short and wide, so the label
    /// goes beside the chip; side-by-side halves are tall and narrow, so it goes
    /// underneath. Laying it beside the chip in a narrow half spends the one
    /// dimension there is least of and pushes the pair towards the seam.
    /// </para>
    /// <para>
    /// It also squares the chip with its own half. A chip-beside-label group
    /// centres the GROUP, which leaves the chip itself sitting left of centre by
    /// half the label's width - visible as the two chips of a side-by-side pair
    /// being lopsided about the middle. Stacked, the chip is centred because it
    /// is the thing being centred.
    /// </para>
    /// </summary>
    public Orientation ChipContentOrientation =>
        TierAxis == Axis.Vertical ? Orientation.Horizontal : Orientation.Vertical;

    /// <summary>
    /// Room for the chip, plus a second line under it where there is one. Capping
    /// both at one line's worth would shrink a stacked pair to half size rather
    /// than let it use the height its half has going spare.
    /// </summary>
    public double TierChipMaxHeight => TierAxis == Axis.Vertical ? 20 : 40;

    /// <summary>
    /// Where the two subzone CHIPS sit, which is not where their hit targets sit.
    /// <para>
    /// A target is the whole half, because pointing at a region has to mean that
    /// region. A chip is a label, and labels have to keep out of each other's
    /// way. Stacked, the two coincide - a chip centred in the upper half is
    /// nowhere near the zone's own block on the middle seam.
    /// </para>
    /// <para>
    /// Side by side they cannot. Centring each chip in its half is the obvious
    /// arrangement and it does not fit: the whole-zone block owns the middle
    /// line, and on a 508px tile with "Ctrl+Shift+" chords the block is 176px
    /// wide and each subzone chip 146px, so their centres are 127px apart when
    /// they need 161px. The tile would have to be about 644px wide - and that
    /// figure moves with the length of the chord, so no fixed threshold settles
    /// it either. So a side-by-side chip takes the TOP half of its own column,
    /// which also puts it on the same line as the chips of any stacked zone
    /// beside it, while its target still covers the full height.
    /// </para>
    /// </summary>
    public int ChipSecondRow => TierAxis == Axis.Vertical ? 1 : 0;

    public int ChipSecondColumn => TierAxis == Axis.Vertical ? 0 : 1;

    public int ChipColumnSpan => TierAxis == Axis.Vertical ? 2 : 1;

    /// <summary>
    /// A stacked chip stretches across the tile, since its row is the full width
    /// and the Viewbox needs that width to know what to shrink to. A side-by-side
    /// chip has to centre in its own column instead: stretched, it sits against
    /// the column's left edge, which puts the pair at the far left and the middle
    /// rather than either side of the middle.
    /// </summary>
    public Avalonia.Layout.HorizontalAlignment ChipHorizontalAlignment =>
        TierAxis == Axis.Vertical
            ? Avalonia.Layout.HorizontalAlignment.Stretch
            : Avalonia.Layout.HorizontalAlignment.Center;

    /// <summary>
    /// Where the two subzones sit on the key surface - the key above home takes
    /// the first, the key below takes the second.
    /// <para>
    /// A subzone is a zone in its own right, drawn as a chip on the tile rather
    /// than as a rectangle of its own because all three overlap in space.
    /// Carrying its position means the chip can be clicked to rebind that zone,
    /// instead of the tile being the only thing on the diagram that answers to a
    /// click and always meaning the whole column.
    /// </para>
    /// </summary>
    public GridPos? FirstPosition { get; init; }

    /// <summary>Names for the tooltips, so each target says which zone it is.</summary>
    public string? FirstName { get; init; }

    public string? SecondName { get; init; }

    public GridPos? SecondPosition { get; init; }

    [ObservableProperty]
    private bool _isFirstCapturing;

    [ObservableProperty]
    private bool _isSecondCapturing;

    public bool IsFirstInteractive => IsInteractive && FirstPosition is not null;

    /// <summary>
    /// What repeated presses of this key walk through, e.g. "Left, then Left +
    /// Center, then the whole display".
    /// <para>
    /// Nothing on screen said this happened. The zones are drawn, so a first
    /// press is obvious; that holding the modifier and pressing again widens the
    /// window is invisible until someone does it by accident.
    /// </para>
    /// </summary>
    public string? Cycle { get; init; }

    /// <summary>How a zone reads when the diagram is only being looked at.</summary>
    public string ViewHint =>
        Cycle is null ? $"{ModifierPrefix}{KeyLabel} - {Name}" : $"{ModifierPrefix}{KeyLabel} - {Cycle}";

    /// <summary>
    /// What pointing here would change. Each target names its OWN zone: the
    /// halves had no tooltip of their own, so they inherited the tile's, which
    /// names the whole column - the pointer said one thing and the click did
    /// another.
    /// </summary>
    public string WholeHint => $"{Name} - click to change its key";

    /// <summary>
    /// The fallback names follow the axis. A side-by-side subzone described as
    /// the "upper half" is the pointer telling you the opposite of what the
    /// rectangle under it shows.
    /// </summary>
    private string FirstWord => TierAxis == Axis.Vertical ? "Upper" : "Left";

    private string SecondWord => TierAxis == Axis.Vertical ? "Lower" : "Right";

    public string FirstHint => $"{FirstName ?? $"{FirstWord} half"} - click to change its key";

    public string SecondHint => $"{SecondName ?? $"{SecondWord} half"} - click to change its key";

    public bool IsSecondInteractive => IsInteractive && SecondPosition is not null;

    [RelayCommand]
    private void ActivateFirst()
    {
        if (FirstPosition is not null) Activated?.Invoke(FirstPosition.Value);
    }

    [RelayCommand]
    private void ActivateSecond()
    {
        if (SecondPosition is not null) Activated?.Invoke(SecondPosition.Value);
    }

    /// <summary>
    /// This zone's own chord prefix, e.g. "Win+" or "Ctrl+Alt+".
    /// <para>
    /// Per zone rather than one prefix for the diagram, because a zone bound by
    /// hand may be taken with any modifier. Drawn from the control's single
    /// prefix, every chip claimed the default and the picture lied about half
    /// the bindings on it.
    /// </para>
    /// </summary>
    public string ModifierPrefix { get; init; } = "Win+";

    private readonly string? _upperPrefix;
    private readonly string? _lowerPrefix;

    /// <summary>
    /// A tier's chord, falling back to this zone's own rather than to a fixed
    /// "Win+". A hardcoded default is a lie waiting for the one path that
    /// forgets to set it - and it would claim the wrong modifier on screen
    /// while everything around it was right.
    /// </summary>
    public string FirstModifierPrefix
    {
        get => _upperPrefix ?? ModifierPrefix;
        init => _upperPrefix = value;
    }

    public string SecondModifierPrefix
    {
        get => _lowerPrefix ?? ModifierPrefix;
        init => _lowerPrefix = value;
    }

    /// <summary>The prefix split where a long chord may be broken across lines.</summary>
    public IReadOnlyList<string> ModifierSegments => ChordSegments(ModifierPrefix);

    internal static IReadOnlyList<string> ChordSegments(string prefix) =>
        [.. prefix.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(part => $"{part}+")];

    public string? FirstKey { get; init; }
    public string? FirstSize { get; init; }
    public string? SecondKey { get; init; }
    public string? SecondSize { get; init; }

    public bool HasFirst => FirstKey is not null;
    public bool HasSecond => SecondKey is not null;

    private bool HasTiers => HasFirst || HasSecond;

    /// <summary>
    /// How tall this zone must be drawn before its name will fit, in device
    /// pixels.
    /// <para>
    /// Tier chips sit at the quarter marks, so a zone carrying them has only
    /// the middle band free; the key chip alone fits there but the key plus a
    /// name does not until the zone is much taller. Without tiers the whole
    /// height is available and the name fits almost immediately.
    /// </para>
    /// </summary>
    public double NameNeedsHeight => (HasTiers ? 112 : 44) + BlockChrome;

    /// <summary>As <see cref="NameNeedsHeight"/>, with a line for the size too.</summary>
    public double SizeNeedsHeight => (HasTiers ? 142 : 62) + BlockChrome;

    /// <summary>
    /// How tall this zone must be drawn before its tier chips are worth having.
    /// <para>
    /// Below this the band they leave in the middle is narrower than the zone's
    /// own key needs, and all three end up printed over one another. Something
    /// has to give, and it is the tiers: they are the halves of the column, and
    /// the home-row key is what the column IS.
    /// </para>
    /// <para>
    /// Twice what the middle has to hold, since the tiers take an equal bite
    /// above and below it: a legible chip, the grid's inset, a tier chip, and
    /// clear air between the two.
    /// </para>
    /// </summary>
    public double TiersNeedHeight => 52;

    /// <summary>
    /// The padding and border of the outline drawn round the chip, name and
    /// size. It is part of what has to fit, so a tile just tall enough for the
    /// text alone is not tall enough once the box is round it - which is how
    /// the name ended up hanging out of the bottom of a short tile.
    /// </summary>
    private const double BlockChrome = 10;

    /// <summary>
    /// How tall this zone's key chip may grow, in device pixels.
    /// <para>
    /// A long chord wraps onto a second line rather than shrinking to nothing,
    /// which costs height. A zone with tier chips at its quarter marks has only
    /// the middle band to spend, so a wrapped chip there grows straight into
    /// them; it shrinks instead. Without tiers the whole height is free and
    /// wrapping is the better trade.
    /// </para>
    /// </summary>
    public double ChipMaxHeight => HasTiers ? 24 : 52;
}

/// <summary>Anything the diagram places at a real position on the virtual desktop.</summary>
public abstract partial class DiagramNodeViewModel : ObservableObject
{
    public required Rect Bounds { get; init; }

    /// <summary>Which gutter this sits in, or the desk itself.</summary>
    public virtual DiagramLane Lane => DiagramLane.Desk;

    /// <summary>Virtual X at which this child's vertical gutter opens.</summary>
    public virtual double LaneAnchor => 0;

    /// <summary>Order among gutters sharing a position; 0 sits nearest the desk.</summary>
    public int LaneSlot { get; set; }

    /// <summary>Thickness of this child's slot, in device pixels.</summary>
    public virtual double LaneThickness => 0;
}

/// <summary>
/// A zone that is not drawable inside a single monitor rectangle: the whole of a
/// display that is already split, or a region crossing a bezel.
/// <para>
/// Drawn as a dimension line alongside the monitors it covers rather than as a
/// rectangle on the desk - a rectangle would sit on top of the zones it contains
/// and hide them, which is exactly what happened while unions had no
/// representation at all and simply vanished from the picture. It is deliberately
/// not panel-shaped: it measures the monitors, it is not one of them.
/// </para>
/// </summary>
public abstract partial class SpanMeasureViewModel : DiagramNodeViewModel
{
    [ObservableProperty]
    private bool _isCapturing;

    public required string Name { get; init; }
    public required string KeyLabel { get; init; }
    public required string SizeLabel { get; init; }

    /// <summary>The chord this span answers to, which may not be the default.</summary>
    public string ModifierPrefix { get; init; } = "Win+";

    public IReadOnlyList<string> ModifierSegments => ZoneCellViewModel.ChordSegments(ModifierPrefix);

    public GridPos Position { get; init; }
    public Action<GridPos>? Activated { get; set; }
    public bool IsInteractive => Activated is not null;

    /// <summary>What pointing here would change, for the floating hint.</summary>
    public string WholeHint => $"{Name} - click to change its key";

    [RelayCommand]
    private void Activate() => Activated?.Invoke(Position);
}

/// <summary>A span across displays sitting side by side; measured underneath them.</summary>
public sealed partial class HorizontalSpanViewModel : SpanMeasureViewModel
{
    public override DiagramLane Lane => DiagramLane.Bottom;

    /// <summary>
    /// The upper and lower halves of the span, drawn as chips above and below
    /// the rule - the same "upper / whole / lower" the key rows mean, stated in
    /// the same vertical order. They are separate zones, but three rules stacked
    /// under the desk would all look alike and say nothing about which is which.
    /// </summary>
    public string? FirstKey { get; init; }

    public string? FirstSize { get; init; }
    public string? SecondKey { get; init; }
    public string? SecondSize { get; init; }

    private readonly string? _upperPrefix;
    private readonly string? _lowerPrefix;

    /// <summary>As on a zone cell: a tier follows the span's own chord.</summary>
    public string FirstModifierPrefix
    {
        get => _upperPrefix ?? ModifierPrefix;
        init => _upperPrefix = value;
    }

    public string SecondModifierPrefix
    {
        get => _lowerPrefix ?? ModifierPrefix;
        init => _lowerPrefix = value;
    }

    public bool HasFirst => FirstKey is not null;
    public bool HasSecond => SecondKey is not null;

    // The label and the chip riding the rule, plus a row for each tier chip.
    // Asymmetric because the span chip is taller than its own row and is raised
    // within it: it reaches up under the text, so the upper tier needs clearing
    // above, while below it already ends short of its row.
    public override double LaneThickness =>
        40 + (HasFirst ? 30 : 0) + (HasSecond ? 18 : 0);
}

/// <summary>
/// A span up a stack of displays; measured beside them, not underneath - drawn
/// below, it would appear to measure their combined width, which is not what the
/// key does. The gutter it opens is against its own displays, on whichever of
/// their sides faces the nearer edge of the desk.
/// </summary>
public sealed partial class VerticalSpanViewModel : SpanMeasureViewModel
{
    public override DiagramLane Lane => DiagramLane.VerticalGutter;

    /// <summary>
    /// The gutter opens against this measure's own displays - at their left edge
    /// or their right - so it always sits beside what it describes. Sent to the
    /// far edge of the desk instead, a measure for a middle column appears to
    /// belong to whichever monitor it ends up next to.
    /// </summary>
    public override double LaneAnchor => OnLeftOfDisplays ? Bounds.Left : Bounds.Right;

    /// <summary>True when the measure sits to the left of the displays it covers.</summary>
    public required bool OnLeftOfDisplays { get; init; }

    /// <summary>
    /// The rule sits between the displays and the label, so a measure on their
    /// left has the label outermost and one on their right has the rule.
    /// </summary>
    public int RuleColumn => OnLeftOfDisplays ? 1 : 0;

    public int LabelColumn => OnLeftOfDisplays ? 0 : 1;

    /// <summary>
    /// A measure on the left reads bottom-to-top, one on the right reads
    /// top-to-bottom - so in both cases the text turns away from the displays
    /// and the pair reads outward rather than both leaning the same way.
    /// </summary>
    public bool ReadsUpward => OnLeftOfDisplays;

    public bool ReadsDownward => !ReadsUpward;

    // The turned label, the rule, and the chip - which is wider than the rule
    // it rides and overhangs it on both sides. Trim this and the chip is clipped
    // where the gutter meets the desk.
    public override double LaneThickness => 50;
}

/// <summary>
/// A draggable seam between two of a display's zones.
/// <para>
/// One per boundary, so a display split into three has two. The display's outer
/// edges are not seams: there is nothing beyond them to trade width with.
/// </para>
/// </summary>
public sealed partial class SplitHandleViewModel : ObservableObject
{
    /// <summary>Which boundary this is, counting from the display's near edge.</summary>
    public required int Index { get; init; }

    [ObservableProperty]
    private double _position;

    /// <summary>
    /// The seam's extent across the other axis. Zones stop at the taskbar, so a
    /// seam drawn the full height of the display would overhang them.
    /// </summary>
    public required double From { get; init; }

    public required double To { get; init; }

    public Action<SeamDrag>? Dragged { get; set; }
    public Action? Released { get; set; }
}

public sealed partial class DisplayNodeViewModel : DiagramNodeViewModel
{
    public required string Key { get; init; }

    /// <summary>
    /// Geometry identity - size, position and orientation. Custom splits are
    /// saved against this rather than the monitor, so swapping in a different
    /// panel of the same shape in the same place keeps them.
    /// </summary>
    public required string Slot { get; init; }
    public required string Title { get; init; }

    /// <summary>
    /// Resolution, scaling and primary flag. Shown as a tooltip rather than a
    /// second label line: inside a narrow column it collided with the tier key
    /// above it, and stacking it would only make the label taller and the
    /// collision worse. The window header already states the same thing.
    /// </summary>
    public required string Detail { get; init; }
    public required bool IsPrimary { get; init; }

    /// <summary>Taskbar strip as a fraction of the display, so the diagram matches reality.</summary>
    public required Rect TaskbarArea { get; init; }

    public required bool HasTaskbar { get; init; }
    public required IReadOnlyList<ZoneCellViewModel> Cells { get; init; }

    /// <summary>
    /// Whether the name tab has room to sit above this display, outside it.
    /// <para>
    /// Outside is where it belongs: inside, it lands on the zone occupying the
    /// top of the display, and on a monitor split into several stacked zones
    /// those cells are short enough that it covers their key chips. It goes
    /// inside only when another display sits directly above - in a stacked pair
    /// there is no room, and a tab drawn there would appear to label the display
    /// above it.
    /// </para>
    /// </summary>
    public required bool LabelAbove { get; init; }

    /// <summary>Everything the tab shows, for the hover text when it is trimmed.</summary>
    public string TitleAndDetail => $"{Title}  ·  {Detail}";

    public bool LabelInside => !LabelAbove;

    // ---- Zone count ---------------------------------------------------------

    /// <summary>
    /// Add or drop a zone on this display, from the diagram itself.
    /// <para>
    /// Beside the display it applies to, rather than only in a list further up
    /// the page: with more than one monitor a bare pair of buttons cannot say
    /// which one it would change, and the picture is where the answer is
    /// obvious.
    /// </para>
    /// </summary>
    public Action<string, int>? ZoneCountChanged { get; set; }

    public bool CanEditZoneCount => ZoneCountChanged is not null;

    /// <summary>Bounds from the shape analyzer, so a step cannot make a split it would reject.</summary>
    public int MinZones { get; set; } = 1;

    public int MaxZones { get; set; } = 1;

    public int ZoneCount => Cells.Count;

    public bool CanAddZone => CanEditZoneCount && ZoneCount < MaxZones;

    public bool CanRemoveZone => CanEditZoneCount && ZoneCount > MinZones;

    [RelayCommand]
    private void AddZone()
    {
        if (CanAddZone) ZoneCountChanged?.Invoke(Slot, ZoneCount + 1);
    }

    [RelayCommand]
    private void RemoveZone()
    {
        if (CanRemoveZone) ZoneCountChanged?.Invoke(Slot, ZoneCount - 1);
    }

    // ---- Draggable splits ---------------------------------------------------

    /// <summary>
    /// The seams between this display's zones. Empty unless the diagram was
    /// built with a commit callback - the wizard and the main window draw the
    /// same picture but are not places to edit it.
    /// </summary>
    public IReadOnlyList<SplitHandleViewModel> Handles { get; private set; } = [];

    /// <summary>Horizontal for a left-to-right split; the seams run down it.</summary>
    public Orientation SeamOrientation { get; private set; }

    public bool HasHandles => Handles.Count > 0;

    private double[] _weights = [];
    private double _spanStart;
    private double _spanLength;
    private double _minFraction = 0.05;
    private Action<string, IReadOnlyList<double>>? _commit;

    /// <summary>Positions a drag prefers to land on; empty when snapping is off.</summary>
    private IReadOnlyList<double> _snapTo = [];

    /// <summary>
    /// Turn this display's zones into something draggable.
    /// <para>
    /// The weights come from the cells rather than being passed in: the cells
    /// are what is on screen, so deriving from them means the seams cannot start
    /// out disagreeing with the zones they sit between.
    /// </para>
    /// </summary>
    /// <param name="minFraction">
    /// How small a zone may be dragged, as a fraction of the split span. The
    /// layout engine already refuses to generate a zone below a pixel floor;
    /// dragging should not be a way around it.
    /// </param>
    public void EnableSplitDragging(
        double minFraction,
        Action<string, IReadOnlyList<double>> commit,
        PxRect workArea = default,
        ShapeTuning? tuning = null,
        bool snap = false)
    {
        if (Cells.Count < 2) return;

        var horizontal = IsSplitHorizontally();
        SeamOrientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        _commit = commit;
        _minFraction = minFraction;

        // After the axis is known, not before: the exact-aspect positions are
        // measured across the OTHER axis, so a candidate list built ahead of
        // this would be the right numbers for the wrong direction.
        _snapTo = snap ? SnapPositions(workArea, horizontal, tuning) : [];

        var ordered = Ordered(horizontal);

        _spanStart = horizontal ? ordered[0].Area.X : ordered[0].Area.Y;
        var end = horizontal ? ordered[^1].Area.Right : ordered[^1].Area.Bottom;
        _spanLength = end - _spanStart;

        if (_spanLength <= 0) return;

        _weights = [.. ordered.Select(c => (horizontal ? c.Area.Width : c.Area.Height) / _spanLength)];

        // Across the seam: the extent the zones themselves occupy, so a seam
        // stops where they stop rather than running into the taskbar.
        var from = horizontal ? ordered.Min(c => c.Area.Y) : ordered.Min(c => c.Area.X);
        var to = horizontal ? ordered.Max(c => c.Area.Bottom) : ordered.Max(c => c.Area.Right);

        var positions = SplitBoundaries.Of(_weights);

        Handles = [.. positions.Select((p, i) => new SplitHandleViewModel
        {
            Index = i,
            Position = _spanStart + p * _spanLength,
            From = from,
            To = to,
            Dragged = DragSeam,
            Released = CommitSeams,
        })];

        OnPropertyChanged(nameof(Handles));
        OnPropertyChanged(nameof(HasHandles));
    }

    /// <summary>
    /// A display splits along its long axis, but the reliable signal is where
    /// the cells actually are: two cells sharing a left edge are stacked, and
    /// nothing about the display's shape has to be consulted to see it.
    /// </summary>
    private bool IsSplitHorizontally()
    {
        var xs = Cells.Select(c => Math.Round(c.Area.X, 4)).Distinct().Count();
        return xs > 1;
    }

    private List<ZoneCellViewModel> Ordered(bool horizontal) =>
        [.. horizontal ? Cells.OrderBy(c => c.Area.X) : Cells.OrderBy(c => c.Area.Y)];

    private void DragSeam(SeamDrag drag)
    {
        if (_spanLength <= 0) return;

        // Snapped before the conversion, because the candidates are positions
        // on the display and that is what the cursor reports.
        var position = drag.Fine ? drag.Position : SplitSnapping.Snap(drag.Position, _snapTo);

        // The handle speaks in fractions of the display; the weights are
        // fractions of the split, which starts wherever the zones start.
        var local = (position - _spanStart) / _spanLength;

        _weights = [.. SplitBoundaries.Move(_weights, drag.Index, local, _minFraction)];

        Redraw();
    }

    /// <summary>Push the current weights back onto the cells and the seams.</summary>
    private void Redraw()
    {
        var horizontal = SeamOrientation == Orientation.Horizontal;
        var ordered = Ordered(horizontal);

        var at = _spanStart;

        for (var i = 0; i < ordered.Count; i++)
        {
            var length = _weights[i] * _spanLength;
            var area = ordered[i].Area;

            // Only the split axis moves. The other one is the display's own
            // extent less its taskbar, which a seam has no say over.
            ordered[i].Area = horizontal
                ? new Rect(at, area.Y, length, area.Height)
                : new Rect(area.X, at, area.Width, length);

            at += length;
        }

        var positions = SplitBoundaries.Of(_weights);

        for (var i = 0; i < Handles.Count && i < positions.Count; i++)
            Handles[i].Position = _spanStart + positions[i] * _spanLength;
    }

    private void CommitSeams() => _commit?.Invoke(Slot, _weights);

    /// <summary>
    /// Where a seam on this display prefers to land.
    /// <para>
    /// Per display rather than one shared grid: the exact-aspect positions
    /// depend on the monitor's own proportions, so a 32:9 and a portrait panel
    /// do not offer the same stops even at the same percentage.
    /// </para>
    /// </summary>
    private static IReadOnlyList<double> SnapPositions(
        PxRect workArea, bool horizontal, ShapeTuning? tuning)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return [];

        var (along, across) = SplitSnapping.Extents(workArea, horizontal);

        // Over the whole display: a seam is clamped to its own pair anyway, so
        // candidates outside that range simply never win.
        return SplitSnapping.Candidates(0, 1, along, across, tuning: tuning);
    }

}

public sealed partial class MonitorDiagramViewModel : ObservableObject
{
    [ObservableProperty]
    private Rect _virtualBounds;

    [ObservableProperty]
    private IReadOnlyList<DisplayNodeViewModel> _displays = [];

    [ObservableProperty]
    private IReadOnlyList<SpanMeasureViewModel> _spans = [];

    /// <summary>
    /// Shown before every key, e.g. "Win+". It lives here rather than on the
    /// window so the control can bind it from its own DataContext: the diagram
    /// is handed to the control AS the DataContext, so anything bound beside it
    /// would resolve against the diagram anyway.
    /// </summary>
    [ObservableProperty]
    private string _modifierPrefix = "Win+";

    /// <summary>
    /// What the pointer is over, named. Shown in the editor's own line rather
    /// than as a tooltip: a tooltip is a window of its own, so it takes the
    /// pointer from whatever it is describing - the region under it loses its
    /// highlight and a click lands on the popup instead of the zone.
    /// </summary>
    [ObservableProperty]
    private string? _hoverHint;

    /// <summary>Where that label sits, kept beside the pointer and inside the diagram.</summary>
    [ObservableProperty]
    private double _hintX;

    [ObservableProperty]
    private double _hintY;

    /// <summary>Everything the panel places, monitors and span measures alike.</summary>
    public IEnumerable<DiagramNodeViewModel> Nodes => Displays.Cast<DiagramNodeViewModel>().Concat(Spans);

    /// <param name="onZoneActivated">
    /// When supplied the diagram becomes clickable and each zone reports its
    /// grid position - which is how settings turns the picture into the
    /// rebinding control rather than duplicating it as a list.
    /// </param>
    /// <param name="onSplitChanged">
    /// When supplied the seams between zones become draggable and report the new
    /// weights for a display slot. Left null the diagram is a picture: the
    /// wizard and the main window show the same layout but are not places to
    /// reshape it.
    /// </param>
    public static MonitorDiagramViewModel Build(
        IReadOnlyList<DisplayInfo> displays,
        LayoutResult? layout = null,
        Action<GridPos>? onZoneActivated = null,
        Action<string, IReadOnlyList<double>>? onSplitChanged = null,
        Action<string, int>? onZoneCountChanged = null,
        Action<string, int>? onSubzoneAxisFlipped = null,
        ShapeTuning? tuning = null,
        bool snapSplits = true,
        ChordModifiers defaultModifier = ChordModifiers.Win)
    {
        var vm = new MonitorDiagramViewModel
        {
            ModifierPrefix = $"{ModifierChoice.Format(defaultModifier)}+",
        };
        if (displays.Count == 0) return vm;

        var desk = PxRect.Union(displays.Select(d => d.Bounds));

        // The desk alone: the gutters holding the span measures are reserved by
        // the panel in device pixels, since only it knows the scale.
        vm.VirtualBounds = new Rect(desk.X, desk.Y, desk.Width, desk.Height);

        vm.Displays =
        [
            .. displays.Select(d =>
                BuildNode(d, layout, HasClearanceAbove(d, displays), defaultModifier, displays)),
        ];
        vm.Spans = BuildSpans(displays, layout, desk, defaultModifier);

        if (onZoneActivated is not null)
        {
            foreach (var cell in vm.Displays.SelectMany(d => d.Cells))
                cell.Activated = onZoneActivated;

            foreach (var span in vm.Spans)
                span.Activated = onZoneActivated;
        }

        if (onSubzoneAxisFlipped is not null)
        {
            // Slot and index are set on every cell regardless, so the button is
            // decided by whether anything is listening rather than by whether the
            // cell happens to know where it lives.
            foreach (var node in vm.Displays)
            foreach (var cell in node.Cells)
                cell.AxisFlipRequested = onSubzoneAxisFlipped;
        }

        if (onSplitChanged is not null || onZoneCountChanged is not null)
        {
            var byKey = displays.ToDictionary(d => d.StableKey);
            var shape = tuning ?? ShapeTuning.Default;
            var hasOthers = displays.Count > 1;

            foreach (var node in vm.Displays)
            {
                if (!byKey.TryGetValue(node.Key, out var display)) continue;

                if (onSplitChanged is not null)
                    node.EnableSplitDragging(
                        MinSplitFraction(display, node, shape),
                        onSplitChanged,
                        display.WorkArea,
                        shape,
                        snapSplits);

                if (onZoneCountChanged is null) continue;

                var counts = ShapeAnalyzer.ZoneCounts(display.Bounds, display.Dpi, hasOthers, shape);

                // The analyzer's own bounds, matched to the Splits list above:
                // stepping must not produce a split it would itself reject.
                // One is always allowed - "leave this display whole" is a
                // legitimate answer even where the analyzer would rather split.
                node.MinZones = Math.Min(1, counts.Min);
                node.MaxZones = Math.Max(counts.Max, node.Cells.Count);
                node.ZoneCountChanged = onZoneCountChanged;
            }
        }

        return vm;
    }

    /// <summary>
    /// The smallest slice a seam may be dragged to, as a fraction of the split.
    /// <para>
    /// Taken from the same pixel floor the layout engine refuses to generate a
    /// zone below, so dragging is not a way around it: a zone too narrow to hold
    /// a window is no more useful for having been made by hand. Expressed
    /// against the work area's long axis because that is what the weights divide.
    /// </para>
    /// </summary>
    private static double MinSplitFraction(
        DisplayInfo display, DisplayNodeViewModel node, ShapeTuning tuning)
    {
        var axisPixels = node.Cells.Select(c => Math.Round(c.Area.X, 4)).Distinct().Count() > 1
            ? display.WorkArea.Width
            : display.WorkArea.Height;

        if (axisPixels <= 0) return 0.05;

        return Math.Clamp(tuning.MinZoneLogicalPx * display.Scale / axisPixels, 0.02, 0.45);
    }

    /// <summary>Mark one cell as awaiting a keypress, clearing any other.</summary>
    public void SetCapturing(GridPos? position)
    {
        foreach (var cell in Displays.SelectMany(d => d.Cells))
        {
            cell.IsCapturing = position is not null && cell.Position == position.Value;
            cell.IsFirstCapturing = position is not null && cell.FirstPosition == position;
            cell.IsSecondCapturing = position is not null && cell.SecondPosition == position;
        }

        foreach (var span in Spans)
            span.IsCapturing = position is not null && span.Position == position.Value;
    }

    /// <summary>
    /// Lay each union out as a measure alongside what it covers: a span across
    /// monitors reads underneath them, a span up a stack reads beside them - on
    /// whichever side is nearer the monitors in question, so the measure sits
    /// next to what it describes rather than in one distant column of them.
    /// </summary>
    private static IReadOnlyList<SpanMeasureViewModel> BuildSpans(
        IReadOnlyList<DisplayInfo> displays, LayoutResult? layout, PxRect desk,
        ChordModifiers defaultModifier)
    {
        if (layout is null) return [];

        var byKey = displays.ToDictionary(d => d.StableKey);
        var byKeyLookup = displays.ToLookup(d => d.StableKey);
        var deskMiddle = desk.X + desk.Width / 2.0;

        var spans = new List<SpanMeasureViewModel>();
        var home = layout.Surface.HomeRow;

        // A span column binds three keys - upper half, whole, lower half - and
        // they become ONE measure with tier chips, not three rules stacked under
        // the desk saying nothing about which is which.
        var tiers = layout.Zones
            .Where(z => z.Kind == ZoneKind.Union && z.Position.Row != home)
            .ToLookup(z => z.Position.Col);

        foreach (var zone in layout.Zones.Where(z => z.Kind == ZoneKind.Union && z.Position.Row == home))
        {
            var pixels = zone.Parts
                .Where(p => byKey.ContainsKey(p.DisplayKey))
                .Select(p => p.Area.Project(byKey[p.DisplayKey].WorkArea))
                .ToList();

            if (pixels.Count == 0) continue;

            var extent = PxRect.Union(pixels);
            var bounds = new Rect(extent.X, extent.Y, extent.Width, extent.Height);
            var label = KeyNames.Of(zone.KeyOn(layout.Surface));
            var size = $"{extent.Width} × {extent.Height}";

            if (IsVertical(zone, pixels, byKeyLookup))
            {
                // Beside its own displays either way; this only picks which of
                // their two sides, and the outward one keeps the measure away
                // from the middle of the desk. A centered column has no outward
                // side, and the right is where a reader looks by default.
                var middle = extent.X + extent.Width / 2.0;

                spans.Add(new VerticalSpanViewModel
                {
                    Bounds = bounds,
                    OnLeftOfDisplays = middle < deskMiddle,
                    Name = zone.Name,
                    KeyLabel = label,
                    SizeLabel = size,
                    Position = zone.Position,
                    ModifierPrefix = Prefix(zone, defaultModifier),
                });
            }
            else
            {
                var column = tiers[zone.Position.Col].ToList();
                var upper = column.FirstOrDefault(z => z.Position.Row < home);
                var lower = column.FirstOrDefault(z => z.Position.Row > home);

                spans.Add(new HorizontalSpanViewModel
                {
                    Bounds = bounds,
                    Name = zone.Name,
                    KeyLabel = label,
                    SizeLabel = size,
                    Position = zone.Position,
                    ModifierPrefix = Prefix(zone, defaultModifier),
                    FirstModifierPrefix = Prefix(upper, defaultModifier),
                    SecondModifierPrefix = Prefix(lower, defaultModifier),
                    FirstKey = upper is null ? null : KeyNames.Of(upper.KeyOn(layout.Surface)),
                    FirstSize = Extent(upper, byKey),
                    SecondKey = lower is null ? null : KeyNames.Of(lower.KeyOn(layout.Surface)),
                    SecondSize = Extent(lower, byKey),
                });
            }
        }

        // Slot only breaks ties: gutters are ordered by where they open, and
        // horizontal measures stack under the desk in the order they were built.
        foreach (var lane in spans.GroupBy(s => (s.Lane, s.LaneAnchor)))
        {
            var slot = 0;
            foreach (var span in lane.OrderBy(s => s.Bounds.Top)) span.LaneSlot = slot++;
        }

        return spans;
    }

    /// <summary>Pixel size of a zone across every display it touches.</summary>
    private static string? Extent(Zone? zone, IReadOnlyDictionary<string, DisplayInfo> byKey)
    {
        if (zone is null) return null;

        var pixels = zone.Parts
            .Where(p => byKey.ContainsKey(p.DisplayKey))
            .Select(p => p.Area.Project(byKey[p.DisplayKey].WorkArea))
            .ToList();

        if (pixels.Count == 0) return null;

        var extent = PxRect.Union(pixels);
        return $"{extent.Width} × {extent.Height}";
    }

    /// <summary>
    /// Which way the union runs. Taken from how its pieces are laid out, not
    /// from the shape of the bounding box: two stacked ultrawides are wider than
    /// they are tall, yet the span up them is unmistakably vertical.
    /// </summary>
    private static bool IsVertical(
        Zone zone, IReadOnlyList<PxRect> pixels, ILookup<string, DisplayInfo> byKey)
    {
        if (pixels.Count > 1)
        {
            var spreadX = pixels.Max(p => p.X + p.Width / 2.0) - pixels.Min(p => p.X + p.Width / 2.0);
            var spreadY = pixels.Max(p => p.Y + p.Height / 2.0) - pixels.Min(p => p.Y + p.Height / 2.0);
            return spreadY > spreadX;
        }

        // One part means "the whole of this display", which stands in for the
        // pieces it was split into - so it runs along the display's split axis.
        var display = byKey[zone.Parts[0].DisplayKey].FirstOrDefault();
        return display is not null && display.SplitAxis == Axis.Vertical;
    }

    /// <summary>
    /// True when nothing sits immediately above this display, so its name tab
    /// can be drawn outside it. The band checked is proportional to the display
    /// rather than a fixed pixel count, since the diagram is scaled to fit.
    /// </summary>
    private static bool HasClearanceAbove(DisplayInfo display, IReadOnlyList<DisplayInfo> all)
    {
        var band = Math.Max(40, display.Bounds.Height / 8);

        foreach (var other in all)
        {
            if (ReferenceEquals(other, display)) continue;
            if (other.Bounds.HorizontalOverlap(display.Bounds) <= 0) continue;

            var gap = display.Bounds.Top - other.Bounds.Bottom;
            if (gap >= 0 && gap < band) return false;
        }

        return true;
    }

    private static DisplayNodeViewModel BuildNode(
        DisplayInfo display,
        LayoutResult? layout,
        bool labelAbove,
        ChordModifiers defaultModifier,
        IReadOnlyList<DisplayInfo> allDisplays)
    {
        var taskbarHeight = display.Bounds.Height - display.WorkArea.Height;

        return new DisplayNodeViewModel
        {
            Key = display.StableKey,
            Slot = DisplaySlot.Of(display),
            Title = display.FriendlyName,
            Detail = $"{display.Bounds.Width} × {display.Bounds.Height}" +
                     (display.Dpi != 96 ? $"  ·  {display.Scale:P0}" : string.Empty) +
                     (display.IsPrimary ? "  ·  primary" : string.Empty),
            Bounds = new Rect(display.Bounds.X, display.Bounds.Y, display.Bounds.Width, display.Bounds.Height),
            IsPrimary = display.IsPrimary,
            HasTaskbar = taskbarHeight > 0,
            TaskbarArea = taskbarHeight > 0
                ? new Rect(0, 1.0 - (double)taskbarHeight / display.Bounds.Height, 1,
                           (double)taskbarHeight / display.Bounds.Height)
                : default,
            Cells = BuildCells(display, layout, defaultModifier, allDisplays),
            LabelAbove = labelAbove,
        };
    }

    private static IReadOnlyList<ZoneCellViewModel> BuildCells(
        DisplayInfo display,
        LayoutResult? layout,
        ChordModifiers defaultModifier,
        IReadOnlyList<DisplayInfo> allDisplays)
    {
        if (layout is null) return [];

        // Unions are drawn as bars below, not as cells. Leaving them in would
        // wreck the column clustering: a zone spanning the whole display
        // overlaps every column, so the union-find pulls all of them into one
        // group and only the first survives - which is how the halves of a
        // split monitor disappeared from the picture entirely.
        var onThisDisplay = layout.Zones
            .Where(z => z.Kind != ZoneKind.Union)
            .Where(z => z.Parts.Any(p => p.DisplayKey == display.StableKey))
            .ToList();

        var cells = new List<ZoneCellViewModel>();

        // The zone index the subzone-axis override is keyed by: position on the
        // desk, left to right, which is the order GroupByHorizontalSpan yields
        // and the same order the stored weights are in.
        var slot = DisplaySlot.Of(display);
        var zoneIndex = -1;

        // Group by GEOMETRY, not by grid column. Rebinding lets a zone's key
        // move without its rectangle moving, so grid position and physical
        // position diverge - grouping by column then draws unrelated zones on
        // top of each other.
        foreach (var column in GroupByHorizontalSpan(onThisDisplay, display))
        {
            zoneIndex++;
            var entries = column
                .Select(z =>
                {
                    var part = z.Parts.First(p => p.DisplayKey == display.StableKey);
                    return (Zone: z, Part: part, Pixels: part.Area.Project(display.WorkArea));
                })
                .OrderBy(e => e.Part.Area.Y)
                .ToList();

            // The biggest zone is the one the others sit inside. By AREA, not by
            // height: with side-by-side subzones all three are the same height,
            // so height alone picks whichever happened to come first and the
            // whole column then draws itself inside one of its own halves.
            var primary = entries.MaxBy(e => e.Part.Area.W * e.Part.Area.H);

            var overlapping = entries.Count > 1 && entries
                .Where(e => e.Zone != primary.Zone)
                .All(e => e.Part.Area.Y >= primary.Part.Area.Y - 1e-6 &&
                          e.Part.Area.Bottom <= primary.Part.Area.Bottom + 1e-6);

            if (!overlapping)
            {
                // Zones genuinely tile this column (e.g. a portrait display split
                // into stacked thirds); draw each as its own rectangle.
                foreach (var e in entries)
                {
                    cells.Add(new ZoneCellViewModel
                    {
                        Name = e.Zone.Name,
                        KeyLabel = KeyNames.Of(e.Zone.KeyOn(layout.Surface)),
                        Area = ToDisplayFraction(e.Part.Area, display),
                        SizeLabel = $"{e.Pixels.Width} × {e.Pixels.Height}",
                        SpansDisplays = e.Zone.SpansDisplays,
                        Position = e.Zone.Position,
                        ModifierPrefix = Prefix(e.Zone, defaultModifier),
                        Cycle = Describe(e.Zone, layout, allDisplays),
                    });
                }

                continue;
            }

            // Which way the subzones cut this column, read off the rectangles
            // themselves rather than carried alongside them. A tier that keeps
            // the column's full width is a stacked half; one that keeps its full
            // height is a side-by-side half. Deriving it here means the drawing
            // cannot disagree with the geometry, which is the failure a stored
            // flag invites the first time one is updated without the other.
            var placed = entries
                .Where(e => e.Zone != primary.Zone)
                .Select(e => (Entry: e, Place: TierPlace(primary.Part.Area, e.Part.Area)))
                .Where(p => p.Place is not null)
                .ToList();

            var axis = placed.Count > 0 ? placed[0].Place!.Value.Axis : Axis.Vertical;
            var upper = placed.FirstOrDefault(p => p.Place!.Value.IsFirst).Entry;
            var lower = placed.FirstOrDefault(p => !p.Place!.Value.IsFirst).Entry;

            cells.Add(new ZoneCellViewModel
            {
                Name = primary.Zone.Name,
                KeyLabel = KeyNames.Of(primary.Zone.KeyOn(layout.Surface)),
                Area = ToDisplayFraction(primary.Part.Area, display),
                SizeLabel = $"{primary.Pixels.Width} × {primary.Pixels.Height}",
                SpansDisplays = primary.Zone.SpansDisplays,
                Position = primary.Zone.Position,
                ModifierPrefix = Prefix(primary.Zone, defaultModifier),
                Cycle = Describe(primary.Zone, layout, allDisplays),
                TierAxis = axis,
                Slot = slot,
                ZoneIndex = zoneIndex,
                FirstModifierPrefix = Prefix(upper.Zone, defaultModifier),
                SecondModifierPrefix = Prefix(lower.Zone, defaultModifier),
                FirstKey = upper.Zone is null ? null : KeyNames.Of(upper.Zone.KeyOn(layout.Surface)),
                FirstPosition = upper.Zone?.Position,
                FirstName = upper.Zone?.Name,
                FirstSize = upper.Zone is null ? null : $"{upper.Pixels.Width} × {upper.Pixels.Height}",
                SecondKey = lower.Zone is null ? null : KeyNames.Of(lower.Zone.KeyOn(layout.Surface)),
                SecondPosition = lower.Zone?.Position,
                SecondName = lower.Zone?.Name,
                SecondSize = lower.Zone is null ? null : $"{lower.Pixels.Width} × {lower.Pixels.Height}",
            });
        }

        return cells;
    }

    /// <summary>
    /// Where a subzone sits inside the zone it belongs to, and therefore which
    /// way that zone is cut.
    /// <para>
    /// A tier keeping the parent's full width can only be a stacked half; one
    /// keeping its full height can only be a side-by-side half. Null for a
    /// rectangle that is neither, which is not a tier at all.
    /// </para>
    /// </summary>
    private static (Axis Axis, bool IsFirst)? TierPlace(NormRect parent, NormRect tier)
    {
        const double Slack = 1e-6;

        var keepsWidth = Math.Abs(tier.W - parent.W) <= Slack;
        var keepsHeight = Math.Abs(tier.H - parent.H) <= Slack;

        // Both means the tier is the parent, which is not a tier.
        if (keepsWidth == keepsHeight) return null;

        return keepsWidth
            ? (Axis.Vertical, tier.Y <= parent.Y + Slack)
            : (Axis.Horizontal, tier.X <= parent.X + Slack);
    }

    /// <summary>
    /// The ring a key walks through, in words.
    /// <para>
    /// Read from RingBuilder - the same one the hotkey engine uses - so what the
    /// diagram promises and what a second press actually does cannot drift
    /// apart. A zone with nowhere wider to go says nothing rather than repeating
    /// its own name.
    /// </para>
    /// </summary>
    private static string? Describe(Zone zone, LayoutResult layout, IReadOnlyList<DisplayInfo> displays)
    {
        var steps = RingBuilder.Build(zone, layout, displays);

        return steps.Count < 2 ? null : string.Join(", then ", steps.Select(s => s.Name));
    }

    /// <summary>
    /// The chord prefix a zone is shown with - its own where it has been bound
    /// by hand, the default otherwise.
    /// </summary>
    private static string Prefix(Zone? zone, ChordModifiers fallback) =>
        $"{ModifierChoice.Format(zone?.ChordWith(fallback) ?? fallback)}+";

    /// <summary>
    /// Cluster zones into visual columns by how much their horizontal spans
    /// overlap, ordered left to right.
    /// </summary>
    private static IEnumerable<IReadOnlyList<Zone>> GroupByHorizontalSpan(
        IReadOnlyList<Zone> zones, DisplayInfo display)
    {
        var remaining = zones
            .Select(z => (Zone: z, Area: z.Parts.First(p => p.DisplayKey == display.StableKey).Area))
            .OrderBy(x => x.Area.X)
            .ToList();

        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            remaining.RemoveAt(0);

            var group = new List<Zone> { seed.Zone };
            var left = seed.Area.X;
            var right = seed.Area.Right;

            // Repeat until nothing new joins: a wide zone can pull in others
            // that did not overlap the original seed.
            bool added;
            do
            {
                added = false;

                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    var candidate = remaining[i];
                    var overlap = Math.Min(right, candidate.Area.Right) - Math.Max(left, candidate.Area.X);
                    var narrower = Math.Min(right - left, candidate.Area.W);

                    if (narrower > 0 && overlap > narrower * 0.5)
                    {
                        group.Add(candidate.Zone);
                        left = Math.Min(left, candidate.Area.X);
                        right = Math.Max(right, candidate.Area.Right);
                        remaining.RemoveAt(i);
                        added = true;
                    }
                }
            }
            while (added);

            yield return group;
        }
    }

    /// <summary>
    /// Rebase a work-area fraction onto the full display, since the diagram draws
    /// the taskbar strip too - otherwise zones would appear to cover the taskbar.
    /// </summary>
    private static Rect ToDisplayFraction(NormRect area, DisplayInfo display)
    {
        var work = display.WorkArea;
        var full = display.Bounds;

        return new Rect(
            (work.X - full.X + area.X * work.Width) / full.Width,
            (work.Y - full.Y + area.Y * work.Height) / full.Height,
            area.W * work.Width / full.Width,
            area.H * work.Height / full.Height);
    }
}
