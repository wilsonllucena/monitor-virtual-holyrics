using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App.Surround;

/// <summary>
/// Fade em cosseno + gama na borda da fatia. Os dois projetores somam ~1.0
/// na overposição; sem isso a junta fica uma faixa clara (dois feixes no mesmo ponto).
/// </summary>
internal static class SoftEdgeBlend
{
    public static void Apply(Bitmap bmp, BlendEdge edge, int pixels, double gamma)
    {
        if (bmp is null || pixels <= 0 || edge == BlendEdge.None) return;

        pixels = Math.Min(pixels, Math.Max(1, bmp.Width / 2));
        var lut = BuildLut(pixels, gamma);

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            var left = edge.HasFlag(BlendEdge.Left);
            var right = edge.HasFlag(BlendEdge.Right);

            for (var y = 0; y < bmp.Height; y++)
            {
                var row = y * stride;
                if (left)
                {
                    for (var x = 0; x < pixels; x++)
                        Multiply(bytes, row + x * 4, lut[x]);
                }

                if (right)
                {
                    for (var i = 0; i < pixels; i++)
                    {
                        var x = bmp.Width - pixels + i;
                        Multiply(bytes, row + x * 4, lut[pixels - 1 - i]);
                    }
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>Índice 0 = borda externa (preto), último = interior (cheio).</summary>
    private static float[] BuildLut(int pixels, double gamma)
    {
        var lut = new float[pixels];
        var last = Math.Max(1, pixels - 1);
        var g = (float)Math.Clamp(gamma, 1, 3);
        for (var i = 0; i < pixels; i++)
        {
            var t = i / (float)last;
            var s = 0.5f * (1f - MathF.Cos(t * MathF.PI));
            lut[i] = MathF.Pow(s, g);
        }

        return lut;
    }

    private static void Multiply(byte[] bytes, int i, float f)
    {
        bytes[i] = (byte)(bytes[i] * f);
        bytes[i + 1] = (byte)(bytes[i + 1] * f);
        bytes[i + 2] = (byte)(bytes[i + 2] * f);
    }
}
