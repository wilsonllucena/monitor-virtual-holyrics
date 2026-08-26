using System.Drawing.Drawing2D;

namespace MonitorVirtual.App;

/// <summary>
/// Tela de teste em cima do monitor virtual — confirma visualmente que a saída certa foi
/// escolhida antes de configurar a projeção no Holyrics.
/// </summary>
internal sealed class TestScreenForm : Form
{
    private readonly System.Windows.Forms.Timer _autoClose = new() { Interval = 20000 };
    private readonly Screen _screen;

    public TestScreenForm(Screen screen)
    {
        _screen = screen;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        _autoClose.Tick += (_, _) => Close();
        _autoClose.Start();

        Click += (_, _) => Close();
        KeyDown += (_, _) => Close();
        KeyPreview = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var colors = new[]
        {
            Color.White, Color.Yellow, Color.Cyan, Color.Lime,
            Color.Magenta, Color.Red, Color.Blue, Color.Black,
        };

        var barWidth = ClientSize.Width / (float)colors.Length;
        var barHeight = ClientSize.Height * 0.35f;
        for (var i = 0; i < colors.Length; i++)
        {
            using var brush = new SolidBrush(colors[i]);
            g.FillRectangle(brush, i * barWidth, 0, barWidth + 1, barHeight);
        }

        var titleSize = Math.Max(28f, ClientSize.Height / 14f);
        using var title = new Font("Segoe UI", titleSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var body = new Font("Segoe UI", titleSize * 0.45f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);
        using var gray = new SolidBrush(Color.FromArgb(200, 200, 200));

        var format = new StringFormat { Alignment = StringAlignment.Center };
        var centerX = ClientSize.Width / 2f;
        var top = barHeight + ClientSize.Height * 0.08f;

        g.DrawString("MONITOR VIRTUAL", title, white, centerX, top, format);
        g.DrawString(
            $"{_screen.DeviceName}  ·  {ClientSize.Width} x {ClientSize.Height}  ·  posição ({_screen.Bounds.X}, {_screen.Bounds.Y})",
            body, gray, centerX, top + titleSize * 1.3f, format);
        g.DrawString(
            "Esta é a tela que você deve escolher no Holyrics em Configurações → Projeção.",
            body, gray, centerX, top + titleSize * 2.2f, format);
        g.DrawString(
            "Clique ou pressione qualquer tecla para fechar (fecha sozinha em 20 s).",
            body, gray, centerX, top + titleSize * 3.1f, format);

        using var border = new Pen(Color.FromArgb(90, 200, 140), 6);
        g.DrawRectangle(border, 3, 3, ClientSize.Width - 6, ClientSize.Height - 6);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _autoClose.Stop();
        _autoClose.Dispose();
        base.OnFormClosed(e);
    }
}
