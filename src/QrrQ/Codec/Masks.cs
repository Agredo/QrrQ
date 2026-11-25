namespace QrrQ.Codec;

/// <summary>The eight data masks from ISO/IEC 18004.</summary>
internal static class Masks
{
    public static bool IsDark(int mask, int row, int column) => mask switch
    {
        0 => (row + column) % 2 == 0,
        1 => row % 2 == 0,
        2 => column % 3 == 0,
        3 => (row + column) % 3 == 0,
        4 => (row / 2 + column / 3) % 2 == 0,
        5 => row * column % 2 + row * column % 3 == 0,
        6 => (row * column % 2 + row * column % 3) % 2 == 0,
        7 => ((row + column) % 2 + row * column % 3) % 2 == 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mask)),
    };
}
