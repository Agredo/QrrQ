namespace QrrQ.Playground;

/// <summary>Grid of modules that the browser interop produced from an image.</summary>
/// <param name="Ok">The image could be reduced to a module grid.</param>
/// <param name="Error">Reason when <paramref name="Ok"/> is <c>false</c>.</param>
/// <param name="Width">Pixel width of the image.</param>
/// <param name="Height">Pixel height of the image.</param>
/// <param name="ModuleSize">Detected module size in pixels.</param>
/// <param name="Size">Edge length in modules.</param>
/// <param name="Matrix">Rows of the grid, <c>'1'</c> = dark.</param>
public sealed record ImageDto(bool Ok, string? Error, int Width, int Height, double ModuleSize, int Size, string[] Matrix);

/// <summary>Result of reading one view of a module grid.</summary>
public sealed record ReadView(string Text, int Version, char Ecc, int Mask, int FormatDistance, int CorrectedCodewords);

/// <summary>One reading direction, optionally compared against the text that was requested.</summary>
public sealed record Reading(string? Expected, ReadView? Actual, string? Problem = null)
{
    /// <summary>Something was read and it matches the expectation (or there is nothing to compare against).</summary>
    public bool Matches => Actual is not null &&
        (Expected is null || string.Equals(Actual.Text, Expected, StringComparison.Ordinal));

    /// <summary>CSS class for the table cell.</summary>
    public string CssClass => Actual is null ? "bad" : Matches ? "ok" : "warn";
}

/// <summary>Both reading directions of one image.</summary>
public sealed record ImageScan(bool Detected, string? Error, int Width, int Height, double ModuleSize, int Size, Reading Direct, Reading Mirrored);

/// <summary>Everything the result sections of the page display.</summary>
public sealed record PlaygroundResult(
    QrrQCode Code,
    string ArtworkPng,
    string MirrorPng,
    string ArtworkSvg,
    string MirrorSvg,
    ImageScan Artwork,
    ImageScan Mirror,
    string ArtworkPreview,
    string MirrorPreview);
