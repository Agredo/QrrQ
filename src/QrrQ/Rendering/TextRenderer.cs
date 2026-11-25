using System.Text;

namespace QrrQ.Rendering;

/// <summary>Console preview using half-block characters (two module rows per text line).</summary>
internal static class TextRenderer
{
    public static string Render(QrMatrix matrix, int quietZone, bool darkBackground)
    {
        var side = matrix.Size + 2 * quietZone;
        var builder = new StringBuilder();
        for (var row = 0; row < side; row += 2)
        {
            for (var column = 0; column < side; column++)
            {
                // On a dark terminal the light modules are the ones being "printed".
                var upper = IsDark(matrix, row - quietZone, column - quietZone) != darkBackground;
                var lower = IsDark(matrix, row + 1 - quietZone, column - quietZone) != darkBackground;
                builder.Append((upper, lower) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    (false, false) => ' ',
                });
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static bool IsDark(QrMatrix matrix, int row, int column) =>
        row >= 0 && row < matrix.Size && column >= 0 && column < matrix.Size && matrix[row, column];
}
