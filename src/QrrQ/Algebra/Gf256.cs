namespace QrrQ.Algebra;

/// <summary>Arithmetik in GF(2⁸) mit dem QR-Polynom x⁸ + x⁴ + x³ + x² + 1 (0x11D), Erzeuger α = 2.</summary>
internal static class Gf256
{
    private static readonly byte[] ExpTable = new byte[510];
    private static readonly byte[] LogTable = new byte[256];

    static Gf256()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            ExpTable[i] = ExpTable[i + 255] = (byte)x;
            LogTable[x] = (byte)i;
            x <<= 1;
            if (x >= 0x100)
            {
                x ^= 0x11D;
            }
        }
    }

    /// <summary>αᵉ für beliebige (auch negative) Exponenten.</summary>
    public static byte Exp(int exponent) => ExpTable[(exponent % 255 + 255) % 255];

    public static byte Multiply(byte a, byte b) =>
        a == 0 || b == 0 ? (byte)0 : ExpTable[LogTable[a] + LogTable[b]];

    public static byte Divide(byte a, byte b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException();
        }

        return a == 0 ? (byte)0 : ExpTable[LogTable[a] + 255 - LogTable[b]];
    }
}
