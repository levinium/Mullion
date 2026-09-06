namespace Mullion.Core.Hotkeys;

/// <summary>A cell in the key grid. Row 0 is the topmost row.</summary>
public readonly record struct GridPos(int Row, int Col)
{
    public override string ToString() => $"r{Row}c{Col}";
}

/// <summary>
/// A rectangular block of PHYSICAL key positions that zones are mapped onto.
/// <para>
/// Keys are identified by scan code, not virtual key. This is a correctness
/// requirement, not a preference: QWERT/ASDFG/ZXCVB is only that shape on
/// QWERTY. On AZERTY the home-row-left physical key emits 'Q'; on Dvorak its
/// neighbours are entirely different letters. Binding virtual keys would
/// scatter the spatial grid on any non-QWERTY layout, so bindings store the
/// position and the displayed letter is resolved per active layout.
/// </para>
/// </summary>
public sealed record KeySurface
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Rows { get; init; }
    public required int Cols { get; init; }

    /// <summary>Scan codes in row-major order, length == Rows * Cols.</summary>
    public required IReadOnlyList<ushort> ScanCodes { get; init; }

    /// <summary>QWERTY labels, used only as a fallback when no layout is queryable.</summary>
    public required IReadOnlyList<string> FallbackLabels { get; init; }

    /// <summary>The row that means "the whole of this column".</summary>
    public required int HomeRow { get; init; }

    public int Capacity => Rows * Cols;

    public ushort ScanCodeAt(GridPos p) => ScanCodes[p.Row * Cols + p.Col];

    public string FallbackLabelAt(GridPos p) => FallbackLabels[p.Row * Cols + p.Col];

    public bool Contains(GridPos p) => p.Row >= 0 && p.Row < Rows && p.Col >= 0 && p.Col < Cols;

    public IEnumerable<GridPos> Positions()
    {
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
            yield return new GridPos(r, c);
    }

    // ---- Built-in surfaces -------------------------------------------------

    /// <summary>
    /// The default. The left hand rests here and the mouse stays free.
    /// Home row is the middle row, so A/S/D keep the meaning they had in the
    /// original three-monitor spec while tiers occupy the rows above and below.
    /// </summary>
    public static readonly KeySurface LeftHandBlock = new()
    {
        Id = "left-hand",
        Name = "Left hand (QWERT / ASDFG / ZXCVB)",
        Rows = 3,
        Cols = 5,
        HomeRow = 1,
        ScanCodes =
        [
            0x10, 0x11, 0x12, 0x13, 0x14, // Q W E R T
            0x1E, 0x1F, 0x20, 0x21, 0x22, // A S D F G
            0x2C, 0x2D, 0x2E, 0x2F, 0x30, // Z X C V B
        ],
        FallbackLabels =
        [
            "Q", "W", "E", "R", "T",
            "A", "S", "D", "F", "G",
            "Z", "X", "C", "V", "B",
        ],
    };

    /// <summary>
    /// A literal 3x3 spatial grid, and arguably a better map for a 3x3 monitor
    /// wall than the left-hand block. Requires a full-size keyboard.
    /// </summary>
    public static readonly KeySurface Numpad = new()
    {
        Id = "numpad",
        Name = "Numpad (7 8 9 / 4 5 6 / 1 2 3)",
        Rows = 3,
        Cols = 3,
        HomeRow = 1,
        ScanCodes =
        [
            0x47, 0x48, 0x49, // 7 8 9
            0x4B, 0x4C, 0x4D, // 4 5 6
            0x4F, 0x50, 0x51, // 1 2 3
        ],
        FallbackLabels =
        [
            "Num7", "Num8", "Num9",
            "Num4", "Num5", "Num6",
            "Num1", "Num2", "Num3",
        ],
    };

    /// <summary>For left-handed mouse users, who need the left hand free.</summary>
    public static readonly KeySurface RightHandBlock = new()
    {
        Id = "right-hand",
        Name = "Right hand (YUIOP / HJKL; / NM,./)",
        Rows = 3,
        Cols = 5,
        HomeRow = 1,
        ScanCodes =
        [
            0x15, 0x16, 0x17, 0x18, 0x19, // Y U I O P
            0x23, 0x24, 0x25, 0x26, 0x27, // H J K L ;
            0x31, 0x32, 0x33, 0x34, 0x35, // N M , . /
        ],
        FallbackLabels =
        [
            "Y", "U", "I", "O", "P",
            "H", "J", "K", "L", ";",
            "N", "M", ",", ".", "/",
        ],
    };

    public static IReadOnlyList<KeySurface> All => [LeftHandBlock, Numpad, RightHandBlock];
}
