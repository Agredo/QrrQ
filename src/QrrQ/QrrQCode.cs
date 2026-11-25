using QrrQ.Codec;
using QrrQ.Reading;
using QrrQ.Rendering;

namespace QrrQ;

/// <summary>
/// A finished mirror QR code. <see cref="Matrix"/> is the artwork to print;
/// scanned normally it yields <see cref="NormalText"/>, in a mirror <see cref="MirroredText"/>.
/// </summary>
public sealed class QrrQCode
{
    internal QrrQCode(MirrorSolution solution)
    {
        Matrix = solution.Matrix;
        NormalText = solution.Normal.Text;
        MirroredText = solution.Mirrored.Text;
        Version = solution.Normal.Version;
        NormalEcc = solution.Normal.Ecc;
        MirroredEcc = solution.Mirrored.Ecc;
        NormalMask = solution.Normal.Mask;
        MirroredMask = solution.Mirrored.Mask;
        NormalCorrectedCodewords = solution.Normal.CorrectedCodewords;
        MirroredCorrectedCodewords = solution.Mirrored.CorrectedCodewords;
        NormalFormatBitErrors = solution.Format.DistanceNormal;
        MirroredFormatBitErrors = solution.Format.DistanceMirrored;
    }

    /// <summary>Payload for normal scanning.</summary>
    public string NormalText { get; }

    /// <summary>Payload for scanning the mirror image.</summary>
    public string MirroredText { get; }

    /// <summary>QR version 1-40.</summary>
    public int Version { get; }

    /// <summary>Edge length in modules.</summary>
    public int Size => Matrix.Size;

    /// <summary>Error correction level of the normal reading direction.</summary>
    public EccLevel NormalEcc { get; }

    /// <summary>Error correction level of the mirrored reading direction.</summary>
    public EccLevel MirroredEcc { get; }

    /// <summary>Data mask of the normal reading direction.</summary>
    public int NormalMask { get; }

    /// <summary>Data mask of the mirrored reading direction.</summary>
    public int MirroredMask { get; }

    /// <summary>Codewords that normal reading already repairs out of the box (0 = error-free).</summary>
    public int NormalCorrectedCodewords { get; }

    /// <summary>Codewords that mirrored reading already repairs out of the box (0 = error-free).</summary>
    public int MirroredCorrectedCodewords { get; }

    /// <summary>
    /// Bit deviation of the format information for normal reading (0 = exact; up to 3 is corrected by every scanner).
    /// A format word is never valid when mirrored - so depending on the mask pair 1 bit is "borrowed".
    /// </summary>
    public int NormalFormatBitErrors { get; }

    /// <summary>Bit deviation of the format information for mirrored reading.</summary>
    public int MirroredFormatBitErrors { get; }

    /// <summary>Both directions are error-free Reed-Solomon codewords.</summary>
    public bool IsErrorFree => NormalCorrectedCodewords == 0 && MirroredCorrectedCodewords == 0;

    /// <summary>The module grid to print.</summary>
    public QrMatrix Matrix { get; }

    /// <summary>The grid as it appears in a mirror (left/right swapped).</summary>
    public QrMatrix MirroredMatrix => field ??= Matrix.FlipHorizontal();

    /// <summary>PNG image of the code.</summary>
    /// <param name="moduleSize">Pixels per module.</param>
    /// <param name="quietZone">Quiet zone in modules (standard value per specification: 4).</param>
    /// <param name="mirrored"><c>true</c> returns the mirror image - handy for testing on screen.</param>
    public byte[] ToPng(int moduleSize = 10, int quietZone = 4, bool mirrored = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(moduleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        return PngEncoder.Encode(mirrored ? MirroredMatrix : Matrix, moduleSize, quietZone);
    }

    /// <summary>Both views as PNG: the artwork to print and its mirror image.</summary>
    public (byte[] Normal, byte[] Mirrored) ToPngPair(int moduleSize = 10, int quietZone = 4) =>
        (ToPng(moduleSize, quietZone), ToPng(moduleSize, quietZone, mirrored: true));

    /// <summary>SVG graphic of the code.</summary>
    public string ToSvg(int moduleSize = 10, int quietZone = 4, bool mirrored = false, string darkColor = "#000", string lightColor = "#fff")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(moduleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        return SvgRenderer.Render(mirrored ? MirroredMatrix : Matrix, moduleSize, quietZone, darkColor, lightColor);
    }

    /// <summary>Console preview made of Unicode half blocks.</summary>
    public string ToText(bool mirrored = false, bool darkBackground = true, int quietZone = 2) =>
        TextRenderer.Render(mirrored ? MirroredMatrix : Matrix, quietZone, darkBackground);

    /// <summary>Reads both views with the built-in decoder - like a scanner after image processing.</summary>
    public (QrReadResult Normal, QrReadResult Mirrored) Verify() =>
        (QrReader.Read(Matrix), QrReader.Read(MirroredMatrix));

    /// <inheritdoc />
    public override string ToString() =>
        $"QrrQ V{Version} ({Size}×{Size}), {NormalEcc.Letter}/{MirroredEcc.Letter}: \"{NormalText}\" ⇄ \"{MirroredText}\"";
}
