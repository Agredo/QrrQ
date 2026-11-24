namespace QrrQ.Tests;

public class QrMatrixTests
{
    [Fact]
    public void Transformations_are_their_own_inverse_or_periodic()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var matrix = code.Matrix;

        Assert.Equal(matrix, matrix.Transpose().Transpose());
        Assert.Equal(matrix, matrix.FlipHorizontal().FlipHorizontal());
        Assert.Equal(matrix, matrix.FlipVertical().FlipVertical());
        Assert.Equal(matrix, matrix.RotateClockwise().RotateClockwise().RotateClockwise().RotateClockwise());
        Assert.Equal(matrix.Transpose().FlipHorizontal(), matrix.RotateClockwise());
    }

    [Fact]
    public void FlipHorizontal_mirrors_columns()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var mirrored = code.Matrix.FlipHorizontal();

        for (var row = 0; row < code.Size; row++)
        {
            for (var column = 0; column < code.Size; column++)
            {
                Assert.Equal(code.Matrix[row, column], mirrored[row, code.Size - 1 - column]);
            }
        }
    }

    [Fact]
    public void ToArray_matches_indexer()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var array = code.Matrix.ToArray();

        Assert.Equal(code.Size, array.GetLength(0));
        Assert.Equal(code.Size, array.GetLength(1));
        Assert.Equal(code.Matrix[3, 7], array[3, 7]);
    }

    [Fact]
    public void Non_square_and_mismatched_raster_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new QrMatrix(new bool[3, 4]));
        Assert.Throws<ArgumentNullException>(() => new QrMatrix(null!));
    }

    [Fact]
    public void Equality_compares_content()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var copy = new QrMatrix(code.Matrix.ToArray());

        Assert.Equal(code.Matrix, copy);
        Assert.Equal(code.Matrix.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(code.Matrix, code.Matrix.FlipHorizontal());
    }
}
