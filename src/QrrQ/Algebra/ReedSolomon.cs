namespace QrrQ.Algebra;

/// <summary>Reed-Solomon-Prüfung und -Korrektur für QR-Blöcke (Nullstellen α⁰ … αᵉ⁻¹).</summary>
internal static class ReedSolomon
{
    /// <summary>Syndrom Sᵢ = c(αⁱ) mit c₀ als höchstem Koeffizienten.</summary>
    public static byte Syndrome(ReadOnlySpan<byte> codeword, int index)
    {
        var x = Gf256.Exp(index);
        byte value = 0;
        foreach (var coefficient in codeword)
        {
            value = (byte)(Gf256.Multiply(value, x) ^ coefficient);
        }

        return value;
    }

    /// <summary>
    /// Korrigiert <paramref name="codeword"/> in-place (Berlekamp-Massey, Chien, Forney).
    /// </summary>
    /// <returns>Anzahl korrigierter Codewörter, oder -1 wenn der Block nicht korrigierbar ist.</returns>
    public static int Correct(Span<byte> codeword, int eccCount)
    {
        var syndromes = new byte[eccCount];
        var clean = true;
        for (var i = 0; i < eccCount; i++)
        {
            syndromes[i] = Syndrome(codeword, i);
            clean &= syndromes[i] == 0;
        }

        if (clean)
        {
            return 0;
        }

        // Berlekamp-Massey: Fehlerortungspolynom Λ (Koeffizienten aufsteigend, Λ₀ = 1).
        var locator = new byte[eccCount + 1];
        var previous = new byte[eccCount + 1];
        var scratch = new byte[eccCount + 1];
        locator[0] = previous[0] = 1;
        var degree = 0;
        var shift = 1;
        byte lastDiscrepancy = 1;

        for (var n = 0; n < eccCount; n++)
        {
            var discrepancy = syndromes[n];
            for (var i = 1; i <= degree; i++)
            {
                discrepancy ^= Gf256.Multiply(locator[i], syndromes[n - i]);
            }

            if (discrepancy == 0)
            {
                shift++;
                continue;
            }

            var factor = Gf256.Divide(discrepancy, lastDiscrepancy);
            if (2 * degree <= n)
            {
                locator.CopyTo(scratch, 0);
                for (var i = 0; i + shift <= eccCount; i++)
                {
                    locator[i + shift] ^= Gf256.Multiply(factor, previous[i]);
                }

                degree = n + 1 - degree;
                scratch.CopyTo(previous, 0);
                lastDiscrepancy = discrepancy;
                shift = 1;
            }
            else
            {
                for (var i = 0; i + shift <= eccCount; i++)
                {
                    locator[i + shift] ^= Gf256.Multiply(factor, previous[i]);
                }

                shift++;
            }
        }

        if (2 * degree > eccCount)
        {
            return -1;
        }

        // Chien-Suche: Fehler an Grad p ⇔ Λ(α⁻ᵖ) = 0.
        List<int> errorDegrees = [with(capacity: degree)];
        for (var p = 0; p < codeword.Length; p++)
        {
            if (Evaluate(locator.AsSpan(0, degree + 1), Gf256.Exp(-p)) == 0)
            {
                errorDegrees.Add(p);
            }
        }

        if (errorDegrees.Count != degree)
        {
            return -1;
        }

        // Ω(x) = S(x)·Λ(x) mod xᵉ
        var evaluator = new byte[eccCount];
        for (var i = 0; i < eccCount; i++)
        {
            for (var j = 0; j <= Math.Min(i, degree); j++)
            {
                evaluator[i] ^= Gf256.Multiply(locator[j], syndromes[i - j]);
            }
        }

        // Forney (erste Nullstelle α⁰): eₖ = Xₖ · Ω(Xₖ⁻¹) / Λ'(Xₖ⁻¹)
        foreach (var p in errorDegrees)
        {
            var inverse = Gf256.Exp(-p);
            byte derivative = 0;
            for (var i = 1; i <= degree; i += 2)
            {
                derivative ^= Gf256.Multiply(locator[i], Gf256.Exp(-p * (i - 1)));
            }

            if (derivative == 0)
            {
                return -1;
            }

            var magnitude = Gf256.Multiply(Gf256.Exp(p), Gf256.Divide(Evaluate(evaluator, inverse), derivative));
            codeword[codeword.Length - 1 - p] ^= magnitude;
        }

        for (var i = 0; i < eccCount; i++)
        {
            if (Syndrome(codeword, i) != 0)
            {
                return -1;
            }
        }

        return degree;
    }

    /// <summary>Wertet ein Polynom mit aufsteigenden Koeffizienten aus (Horner).</summary>
    private static byte Evaluate(ReadOnlySpan<byte> coefficients, byte x)
    {
        byte value = 0;
        for (var i = coefficients.Length - 1; i >= 0; i--)
        {
            value = (byte)(Gf256.Multiply(value, x) ^ coefficients[i]);
        }

        return value;
    }
}
