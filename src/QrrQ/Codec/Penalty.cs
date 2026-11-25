namespace QrrQ.Codec;

/// <summary>
/// Penalty points according to ISO/IEC 18004 (long runs, 2×2 blocks, finder-like patterns, light/dark balance).
/// The score is transposition invariant - so it applies equally to both reading directions.
/// </summary>
internal static class Penalty
{
    private const int FinderLikeA = 0b1011101_0000;
    private const int FinderLikeB = 0b0000_1011101;

    public static int Score(ReadOnlySpan<bool> modules, int size)
    {
        var score = 0;
        for (var line = 0; line < size; line++)
        {
            score += ScoreLine(modules, size, start: line * size, step: 1);
            score += ScoreLine(modules, size, start: line, step: size);
        }

        for (var row = 0; row < size - 1; row++)
        {
            for (var column = 0; column < size - 1; column++)
            {
                var cell = modules[row * size + column];
                if (cell == modules[row * size + column + 1] &&
                    cell == modules[(row + 1) * size + column] &&
                    cell == modules[(row + 1) * size + column + 1])
                {
                    score += 3;
                }
            }
        }

        var dark = 0;
        foreach (var module in modules)
        {
            dark += module ? 1 : 0;
        }

        var total = size * size;
        var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return score + k * 10;
    }

    private static int ScoreLine(ReadOnlySpan<bool> modules, int size, int start, int step)
    {
        var score = 0;
        var run = 0;
        var previous = false;
        for (var i = 0; i < size; i++)
        {
            var module = modules[start + i * step];
            if (i > 0 && module == previous)
            {
                run++;
            }
            else
            {
                if (run >= 5)
                {
                    score += run - 2;
                }

                run = 1;
                previous = module;
            }
        }

        if (run >= 5)
        {
            score += run - 2;
        }

        // 11-module window with four light quiet-zone modules on either side.
        var window = 0;
        for (var i = -4; i < size + 4; i++)
        {
            var dark = i >= 0 && i < size && modules[start + i * step];
            window = (window << 1 | (dark ? 1 : 0)) & 0x7FF;
            if (i >= 6 && window is FinderLikeA or FinderLikeB)
            {
                score += 40;
            }
        }

        return score;
    }
}
