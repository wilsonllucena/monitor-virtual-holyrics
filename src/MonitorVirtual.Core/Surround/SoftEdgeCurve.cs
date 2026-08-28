namespace MonitorVirtual.Core.Surround;

/// <summary>
/// Curva de soft-edge para dois projetores somando luz na parede.
/// Cosseno linear soma 1.0 em valor de pixel; projetor aplica ~gama 2.2, então
/// <c>pow(cosseno, 1/gama)</c> clareia o meio (senão a junta fica preta).
/// </summary>
public static class SoftEdgeCurve
{
    public const double MinGamma = 0.4;
    public const double MaxGamma = 3.0;
    public const double DefaultGamma = 2.2;

    public const double MinGain = 0.25;
    public const double MaxGain = 2.5;
    public const double DefaultGain = 1.0;

    /// <summary>
    /// Índice 0 = borda externa (preto), último = interior (cheio).
    /// Gama maior (ex.: 2.2) clareia a junta no projetor; ganho &gt; 1 reforça.
    /// </summary>
    public static float[] BuildLut(int pixels, double gamma, double gain)
    {
        if (pixels <= 0) return Array.Empty<float>();

        var lut = new float[pixels];
        var last = Math.Max(1, pixels - 1);
        var g = (float)Math.Clamp(gamma, MinGamma, MaxGamma);
        var k = (float)Math.Clamp(gain, MinGain, MaxGain);
        var exp = 1f / g;

        for (var i = 0; i < pixels; i++)
        {
            var t = i / (float)last;
            var s = 0.5f * (1f - MathF.Cos(t * MathF.PI));
            lut[i] = Math.Clamp(MathF.Pow(s, exp) * k, 0f, 1f);
        }

        return lut;
    }

    /// <summary>Sanidade da curva: gama 2.2 clareia o meio; cosseno linear soma ~1.0.</summary>
    public static string? SelfTest()
    {
        var linear = BuildLut(11, 1.0, 1.0);
        if (linear.Length != 11) return "LUT com tamanho errado";
        if (linear[0] > 0.02f) return $"borda externa deveria ser ~0, veio {linear[0]}";
        if (linear[^1] < 0.98f) return $"interior deveria ser ~1, veio {linear[^1]}";
        if (Math.Abs(linear[5] - 0.5f) > 0.03f)
            return $"gama 1: meio {linear[5]}, esperado ~0.5";

        for (var i = 0; i < linear.Length; i++)
        {
            var sum = linear[i] + linear[linear.Length - 1 - i];
            if (Math.Abs(sum - 1f) > 0.03f)
                return $"cosseno linear não soma 1 em i={i} (soma {sum})";
        }

        var projector = BuildLut(11, 2.2, 1.0);
        if (projector[5] <= linear[5] + 0.05f)
            return $"gama 2.2 deveria clarear o meio ({projector[5]} vs {linear[5]})";

        var darkOld = MathF.Pow(0.5f, 2.2f);
        if (projector[5] <= darkOld + 0.1f)
            return $"compensação falhou: meio {projector[5]} ainda perto de pow(0.5,2.2)={darkOld}";

        var boosted = BuildLut(11, 2.2, 1.5);
        if (boosted[5] <= projector[5])
            return "ganho 1.5 deveria aumentar o meio da junta";

        // Luz na parede: pixel^(gama) soma ~1.0 no overlap — senão faixa BRANCA (2.0) ou PRETA.
        for (var i = 0; i < projector.Length; i++)
        {
            var light = MathF.Pow(projector[i], 2.2f) + MathF.Pow(projector[projector.Length - 1 - i], 2.2f);
            if (Math.Abs(light - 1f) > 0.08f)
                return $"gama 2.2 em luz deveria somar 1 em i={i} (soma {light})";
        }

        var clamped = BuildLut(8, 2.2, 2.5);
        if (clamped.Any(v => v is < 0f or > 1f))
            return "LUT saiu do intervalo 0–1";

        return null;
    }
}
