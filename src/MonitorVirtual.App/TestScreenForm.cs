using System.Drawing.Drawing2D;

namespace MonitorVirtual.App;

/// <summary>
/// Tela de teste em cima do monitor virtual — confirma visualmente que a saída certa foi
/// escolhida antes de configurar a projeção no Holyrics.
/// Com canvas surround (telão largo), desenha um padrão contínuo esquerda→direita para
/// ver se o blend está certo ou se ainda está em clone (o mesmo slide duas vezes).
/// O padrão de junta (branco) deixa a faixa preta óbvia na PAREDE, não no preview.
/// </summary>
internal sealed class TestScreenForm : Form
{
    private readonly System.Windows.Forms.Timer? _autoClose;
    private readonly Screen _screen;
    private readonly int _blendOverlap;
    private readonly bool _juntaWhite;

    public TestScreenForm(Screen screen, int blendOverlap = 0, bool stayOpen = false, bool juntaWhite = false)
    {
        _screen = screen;
        _blendOverlap = Math.Max(0, blendOverlap);
        _juntaWhite = juntaWhite;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        Icon = IconFactory.AppIcon;
        BackColor = juntaWhite ? Color.White : Color.Black;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        if (!stayOpen)
        {
            _autoClose = new System.Windows.Forms.Timer { Interval = 20000 };
            _autoClose.Tick += (_, _) => Close();
            _autoClose.Start();
        }

        Click += (_, _) => Close();
        KeyDown += (_, _) => Close();
        KeyPreview = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (ClientSize.Width >= ClientSize.Height * 2)
            PaintSurround(g);
        else
            PaintSingle(g);
    }

    private void PaintSingle(Graphics g)
    {
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

        DrawCaption(g,
            "MONITOR VIRTUAL",
            $"{_screen.DeviceName}  ·  {ClientSize.Width} x {ClientSize.Height}  ·  posição ({_screen.Bounds.X}, {_screen.Bounds.Y})",
            "Esta é a tela que você deve escolher no Holyrics em Configurações → Projeção.",
            "Clique ou pressione qualquer tecla para fechar (fecha sozinha em 20 s).");
    }

    private void PaintSurround(Graphics g)
    {
        var w = ClientSize.Width;
        var h = ClientSize.Height;

        if (_juntaWhite)
        {
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 0, 0, w, h);
            using var gray = new SolidBrush(Color.FromArgb(235, 235, 235));
            g.FillRectangle(gray, 0, 0, w * 0.22f, h);
            g.FillRectangle(gray, w * 0.78f, 0, w * 0.22f, h);
        }
        else
        {
            for (var x = 0; x < w; x++)
            {
                var t = x / (float)Math.Max(1, w - 1);
                using var pen = new Pen(Color.FromArgb(
                    40 + (int)(140 * t),
                    180 - (int)(80 * t),
                    90 + (int)(120 * (1 - t))));
                g.DrawLine(pen, x, 0, x, h);
            }
        }

        var mid = w / 2f;
        var overlap = Math.Min(_blendOverlap, w / 4);
        if (overlap > 0)
        {
            using var hatch = new SolidBrush(Color.FromArgb(_juntaWhite ? 40 : 50, Color.Black));
            g.FillRectangle(hatch, mid - overlap / 2f, 0, overlap, h);

            using var dash = new Pen(Color.FromArgb(220, 220, 40, 40), 3) { DashStyle = DashStyle.Dash };
            g.DrawLine(dash, mid - overlap / 2f, 0, mid - overlap / 2f, h);
            g.DrawLine(dash, mid + overlap / 2f, 0, mid + overlap / 2f, h);
        }

        using var center = new Pen(_juntaWhite ? Color.FromArgb(180, 200, 0, 0) : Color.White, 4);
        g.DrawLine(center, mid, 0, mid, h);

        var titleSize = Math.Max(36f, h / 10f);
        using var huge = new Font("Segoe UI", titleSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var body = new Font("Segoe UI", titleSize * 0.35f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(_juntaWhite ? Color.FromArgb(30, 30, 30) : Color.White);
        var format = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString("ESQUERDA", huge, ink, w * 0.22f, h * 0.28f, format);
        g.DrawString("DIREITA", huge, ink, w * 0.78f, h * 0.28f, format);
        g.DrawString("JUNTA", body, ink, mid, h * 0.42f, format);

        DrawCaption(g,
            _juntaWhite ? "PADRÃO DE JUNTA — OLHE O TELÃO" : "TELÃO SURROUND — UMA TELA SÓ",
            $"{w} x {h}  ·  blend {overlap} px  ·  se os dois lados mostram ESQUERDA e DIREITA, ainda está em clone",
            _juntaWhite
                ? "No telão o centro deve ficar tão claro quanto as laterais. Faixa preta = aumente gama ou intensidade."
                : "No projetor esquerdo deve aparecer sobretudo ESQUERDA; no direito, DIREITA. A junta some com o blend.",
            "Clique ou pressione qualquer tecla para fechar.");
    }

    private void DrawCaption(Graphics g, string title, string line1, string line2, string line3)
    {
        var titleSize = Math.Max(28f, ClientSize.Height / 14f);
        using var titleFont = new Font("Segoe UI", titleSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var body = new Font("Segoe UI", titleSize * 0.45f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(_juntaWhite ? Color.FromArgb(20, 20, 20) : Color.White);
        using var muted = new SolidBrush(_juntaWhite ? Color.FromArgb(50, 50, 50) : Color.FromArgb(230, 230, 230));
        var format = new StringFormat { Alignment = StringAlignment.Center };
        var centerX = ClientSize.Width / 2f;
        var top = ClientSize.Height * 0.58f;

        g.DrawString(title, titleFont, ink, centerX, top, format);
        g.DrawString(line1, body, muted, centerX, top + titleSize * 1.3f, format);
        g.DrawString(line2, body, muted, centerX, top + titleSize * 2.2f, format);
        g.DrawString(line3, body, muted, centerX, top + titleSize * 3.1f, format);

        using var border = new Pen(Color.FromArgb(90, 200, 140), 6);
        g.DrawRectangle(border, 3, 3, ClientSize.Width - 6, ClientSize.Height - 6);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_autoClose is not null)
        {
            _autoClose.Stop();
            _autoClose.Dispose();
        }

        base.OnFormClosed(e);
    }
}
