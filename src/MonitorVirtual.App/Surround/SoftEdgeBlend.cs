using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App.Surround;

/// <summary>
/// Aplica o fade nas fatias enviadas aos projetores físicos (não no preview).
/// Sem compensação de gama a junta soma menos que 1.0 na parede e fica preta.
/// </summary>
internal static class SoftEdgeBlend
{
    public static void Apply(Bitmap bmp, BlendEdge edge, int pixels, double gamma, double gain)
    {
        if (bmp is null || pixels <= 0 || edge == BlendEdge.None) return;

        pixels = Math.Min(pixels, Math.Max(1, bmp.Width / 2));
        var lut = SoftEdgeCurve.BuildLut(pixels, gamma, gain);
        if (lut.Length == 0) return;

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

    private static void Multiply(byte[] bytes, int i, float f)
    {
        bytes[i] = (byte)(bytes[i] * f);
        bytes[i + 1] = (byte)(bytes[i + 1] * f);
        bytes[i + 2] = (byte)(bytes[i + 2] * f);
    }
}
