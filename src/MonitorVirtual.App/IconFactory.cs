using System.Drawing.Drawing2D;

namespace MonitorVirtual.App;

/// <summary>Desenha os ícones da bandeja e das janelas em runtime (evita assets binários no repositório).</summary>
internal static class IconFactory
{
    private static Icon? _app;

    /// <summary>Ícone verde do programa (janela, barra de tarefas, Alt+Tab).</summary>
    public static Icon AppIcon => _app ??= Create(true);

    public static Icon Create(bool active)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var body = new Rectangle(3, 5, 26, 17);
            using var frame = new Pen(active ? Color.FromArgb(60, 200, 120) : Color.FromArgb(150, 150, 150), 2.5f);
            using var fill = new SolidBrush(active ? Color.FromArgb(30, 90, 60) : Color.FromArgb(60, 60, 60));

            g.FillRectangle(fill, body);
            g.DrawRectangle(frame, body);

            using var standBrush = new SolidBrush(frame.Color);
            g.FillRectangle(standBrush, 13, 22, 6, 4);
            g.FillRectangle(standBrush, 9, 26, 14, 3);

            if (active)
            {
                using var glow = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
                g.FillRectangle(glow, 6, 8, 20, 4);
            }
        }

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
