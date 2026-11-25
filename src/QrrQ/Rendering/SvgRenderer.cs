using System.Text;

namespace QrrQ.Rendering;

/// <summary>SVG output: a single path with horizontal runs merged.</summary>
internal static class SvgRenderer
{
    public static string Render(QrMatrix matrix, int moduleSize, int quietZone, string darkColor, string lightColor)
    {
        var side = matrix.Size + 2 * quietZone;
        var path = new StringBuilder();
        for (var row = 0; row < matrix.Size; row++)
        {
            for (var column = 0; column < matrix.Size; column++)
            {
                if (!matrix[row, column])
                {
                    continue;
                }

                var start = column;
                while (column + 1 < matrix.Size && matrix[row, column + 1])
                {
                    column++;
                }

                var length = column - start + 1;
                path.Append($"M{start + quietZone} {row + quietZone}h{length}v1h-{length}z");
            }
        }

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {side} {side}" width="{side * moduleSize}" height="{side * moduleSize}" shape-rendering="crispEdges">
              <rect width="{side}" height="{side}" fill="{lightColor}"/>
              <path fill="{darkColor}" d="{path}"/>
            </svg>
            """;
    }
}
