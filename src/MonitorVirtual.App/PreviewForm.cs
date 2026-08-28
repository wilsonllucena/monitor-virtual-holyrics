using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.App;

/// <summary>
/// Espelha o conteúdo do monitor virtual dentro de uma janela — permite conferir a projeção
/// do Holyrics sem projetor ligado. A captura é feita com BitBlt do desktop (CopyFromScreen).
/// </summary>
internal sealed class PreviewForm : Form
{
    private readonly Func<Rectangle?> _getSourceBounds;
    private readonly Action? _showSettings;
    private readonly Action? _showBlend;
    private readonly Func<bool>? _blendEnabled;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly PictureBox _canvas = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Black,
    };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true };
    private ToolStripButton? _blendButton;

    private Bitmap? _frame;
    private Rectangle _source;
    private int _fps;
    private bool _captureFailing;

    public PreviewForm(
        Func<Rectangle?> getSourceBounds,
        int fps,
        Action? showSettings = null,
        Action? showBlend = null,
        Func<bool>? blendEnabled = null)
    {
        _getSourceBounds = getSourceBounds;
        _showSettings = showSettings;
        _showBlend = showBlend;
        _blendEnabled = blendEnabled;
        _fps = Math.Clamp(fps, 1, 60);

        Text = "Monitor virtual — visualização";
        Icon = IconFactory.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 580);
        MinimumSize = new Size(360, 260);
        BackColor = Color.Black;
        KeyPreview = true;
        ShowInTaskbar = true;

        BuildMenu();

        _status.Items.Add(_statusLabel);
        _status.SizingGrip = true;

        Controls.Add(_canvas);
        Controls.Add(_status);

        _timer.Interval = Math.Max(16, 1000 / _fps);
        _timer.Tick += (_, _) => CaptureFrame();
        _timer.Start();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };

        CaptureFrame();
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip();

        var view = new ToolStripMenuItem("Exibição");
        var topMost = new ToolStripMenuItem("Sempre visível", null, (s, _) =>
        {
            TopMost = !TopMost;
            ((ToolStripMenuItem)s!).Checked = TopMost;
        });
        view.DropDownItems.Add(topMost);

        var fit = new ToolStripMenuItem("Ajustar janela à proporção do monitor", null, (_, _) => FitToAspect());
        view.DropDownItems.Add(fit);

        var rate = new ToolStripMenuItem("Taxa de atualização");
        foreach (var option in new[] { 5, 10, 15, 24, 30 })
        {
            var fps = option;
            var item = new ToolStripMenuItem($"{fps} fps", null, (_, _) => SetFps(fps))
            {
                Checked = fps == _fps,
            };
            rate.DropDownItems.Add(item);
        }

        view.DropDownItems.Add(rate);
        menu.Items.Add(view);

        var programa = new ToolStripMenuItem("Programa");
        var settings = new ToolStripMenuItem("Configurações...", null, (_, _) => _showSettings?.Invoke());
        var blend = new ToolStripMenuItem("Ajustar blend do telão...", IconFactory.UiBitmap, (_, _) => _showBlend?.Invoke())
        {
            Enabled = _blendEnabled?.Invoke() ?? false,
        };
        programa.DropDownOpening += (_, _) => blend.Enabled = _blendEnabled?.Invoke() ?? false;
        programa.DropDownItems.Add(settings);
        programa.DropDownItems.Add(blend);
        menu.Items.Add(programa);

        if (_showSettings is not null || _showBlend is not null)
        {
            var tools = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(24, 24),
            };
            var blendBtn = new ToolStripButton("Ajustar blend do telão")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Image = IconFactory.UiBitmap,
                Font = new Font(SystemFonts.MenuFont ?? Font, FontStyle.Bold),
                Enabled = _blendEnabled?.Invoke() ?? false,
            };
            blendBtn.Click += (_, _) => _showBlend?.Invoke();
            var cfgBtn = new ToolStripButton("Configurações");
            cfgBtn.Click += (_, _) => _showSettings?.Invoke();
            tools.Items.Add(blendBtn);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(cfgBtn);
            _blendButton = blendBtn;
            Controls.Add(tools);
        }

        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void SetFps(int fps)
    {
        _fps = Math.Clamp(fps, 1, 60);
        _timer.Interval = Math.Max(16, 1000 / _fps);

        foreach (ToolStripMenuItem top in MainMenuStrip!.Items)
        {
            foreach (var child in top.DropDownItems)
            {
                if (child is ToolStripMenuItem { DropDownItems.Count: > 0 } group)
                {
                    foreach (ToolStripMenuItem item in group.DropDownItems)
                        item.Checked = item.Text == $"{_fps} fps";
                }
            }
        }
    }

    private void FitToAspect()
    {
        if (_source.Width <= 0 || _source.Height <= 0) return;

        var chrome = Height - ClientSize.Height;
        var reserved = (MainMenuStrip?.Height ?? 0) + _status.Height + 28;
        var width = Math.Min(_source.Width, Screen.FromControl(this).WorkingArea.Width - 80);
        var height = (int)(width * (_source.Height / (double)_source.Width));

        ClientSize = new Size(width, height + reserved);
        Height += 0; // mantém a moldura calculada pelo WinForms
        _ = chrome;
    }

    private void CaptureFrame()
    {
        var bounds = _getSourceBounds();
        if (bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
        {
            _statusLabel.Text = "Monitor virtual não está ativo.";
            _canvas.Image = null;
            return;
        }

        _source = bounds.Value;

        try
        {
            if (_frame is null || _frame.Width != _source.Width || _frame.Height != _source.Height)
            {
                _frame?.Dispose();
                _frame = new Bitmap(_source.Width, _source.Height, PixelFormat.Format32bppPArgb);
            }

            using (var g = Graphics.FromImage(_frame))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.CopyFromScreen(_source.X, _source.Y, 0, 0, _source.Size, CopyPixelOperation.SourceCopy);
            }

            _canvas.Image = _frame;
            _canvas.Invalidate();
            _statusLabel.Text = $"{_source.Width}x{_source.Height} em ({_source.X},{_source.Y}) · {_fps} fps";
            _captureFailing = false;
            if (_blendButton is not null)
                _blendButton.Enabled = _blendEnabled?.Invoke() ?? false;
        }
        catch (Exception ex)
        {
            // a captura falha momentaneamente quando o dispositivo reinicia ou a sessão troca;
            // registra só a primeira falha da sequência para não inundar o log a cada quadro
            _statusLabel.Text = $"Falha na captura: {ex.Message}";
            if (!_captureFailing)
            {
                _captureFailing = true;
                Log.Error("Falha ao capturar o monitor virtual", ex);
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _canvas.Image = null;
        _frame?.Dispose();
        base.OnFormClosed(e);
    }
}
