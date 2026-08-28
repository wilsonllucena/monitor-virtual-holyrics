using System.Drawing.Drawing2D;

namespace MonitorVirtual.App;

/// <summary>
/// Ícone do produto: dois projetores (cinza) e a junta azul no meio — o visual
/// do atalho / da barra de tarefas. Bandeja fica verde quando o monitor está ativo.
/// </summary>
internal static class IconFactory
{
    private static Icon? _app;
    private static Icon? _trayOn;
    private static Icon? _trayOff;
    private static Bitmap? _ui;

    /// <summary>Ícone da janela, barra de tarefas, Alt+Tab e atalho do .exe.</summary>
    public static Icon AppIcon => _app ??= FromBitmap(Paint(32, trayActive: false, branded: true));

    public static Icon Create(bool active) =>
        active
            ? _trayOn ??= FromBitmap(Paint(32, trayActive: true, branded: true))
            : _trayOff ??= FromBitmap(Paint(32, trayActive: false, branded: true));

    /// <summary>Bitmap para botões da UI (não descartar: a PictureBox/Button segura a referência).</summary>
    public static Bitmap UiBitmap => _ui ??= Paint(32, trayActive: false, branded: true);

    public static Bitmap Paint(int size, bool trayActive, bool branded)
    {
        size = Math.Clamp(size, 16, 256);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var s = size / 32f;
        var frame = trayActive
            ? Color.FromArgb(50, 170, 100)
            : Color.FromArgb(228, 220, 204); // moldura bege do atalho
        var inner = Color.FromArgb(40, 44, 50);
        var screen = Color.FromArgb(188, 190, 196);
        var junta = branded
            ? Color.FromArgb(40, 118, 214)
            : Color.FromArgb(230, 230, 235);
        var stand = trayActive ? Color.FromArgb(40, 130, 80) : Color.FromArgb(120, 118, 112);

        using (var brush = new SolidBrush(frame))
            Round(g, brush, R(1.5f * s, 1.5f * s, 29f * s, 29f * s), 3.2f * s);

        using (var brush = new SolidBrush(inner))
            Round(g, brush, R(4f * s, 4.5f * s, 24f * s, 17.5f * s), 1.6f * s);

        // dois projetores + junta vertical (o “blend” da taskbar)
        using (var brush = new SolidBrush(screen))
        {
            g.FillRectangle(brush, R(5.2f * s, 5.8f * s, 8.6f * s, 14.2f * s));
            g.FillRectangle(brush, R(18.2f * s, 5.8f * s, 8.6f * s, 14.2f * s));
        }

        using (var brush = new SolidBrush(junta))
            g.FillRectangle(brush, R(14.2f * s, 5.4f * s, 3.6f * s, 15f * s));

        if (trayActive)
        {
            using var glow = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
            g.FillRectangle(glow, R(5.6f * s, 6.2f * s, 7.8f * s, 3.2f * s));
            g.FillRectangle(glow, R(18.6f * s, 6.2f * s, 7.8f * s, 3.2f * s));
        }

        using (var brush = new SolidBrush(stand))
        {
            g.FillRectangle(brush, R(8.4f * s, 22.6f * s, 2.4f * s, 2.2f * s));
            g.FillRectangle(brush, R(6.6f * s, 24.8f * s, 6f * s, 1.8f * s));
            g.FillRectangle(brush, R(21.2f * s, 22.6f * s, 2.4f * s, 2.2f * s));
            g.FillRectangle(brush, R(19.4f * s, 24.8f * s, 6f * s, 1.8f * s));
        }

        return bmp;
    }

    private static RectangleF R(float x, float y, float w, float h) => new(x, y, w, h);

    private static void Round(Graphics g, Brush brush, RectangleF r, float radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private static Icon FromBitmap(Bitmap bmp)
    {
        using (bmp)
        {
            var handle = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(handle);
                return (Icon)tmp.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
    }
}
