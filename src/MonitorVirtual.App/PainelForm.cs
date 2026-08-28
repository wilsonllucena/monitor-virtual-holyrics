namespace MonitorVirtual.App;

/// <summary>
/// Janela do operador: presença na barra de tarefas, com botões óbvios para
/// Configurações e Ajustar blend. Não depende do menu da bandeja ficar aberto.
/// </summary>
internal sealed class PainelForm : Form
{
    private readonly Action _showBlend;
    private readonly Action _showSettings;
    private readonly Action _showPreview;
    private readonly Action _showTest;
    private readonly Button _blend = new();
    private readonly Label _status = new();
    private bool _allowClose;
    private bool _silentShow;

    public PainelForm(
        Action showBlend,
        Action showSettings,
        Action showPreview,
        Action showTest)
    {
        _showBlend = showBlend;
        _showSettings = showSettings;
        _showPreview = showPreview;
        _showTest = showTest;

        Text = "Monitor Virtual";
        Icon = IconFactory.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 268);
        Font = SystemFonts.MessageBoxFont;

        var tip = new ToolTip { ShowAlways = true };

        var iconBox = new PictureBox
        {
            Image = IconFactory.Paint(48, trayActive: false, branded: true),
            SizeMode = PictureBoxSizeMode.Zoom,
            Left = 16,
            Top = 12,
            Size = new Size(48, 48),
            Cursor = Cursors.Hand,
        };
        iconBox.Click += (_, _) => _showBlend();
        tip.SetToolTip(iconBox, "Ajustar blend do telão");

        var title = new Label
        {
            Text = "Monitor Virtual para Holyrics",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Left = 72,
            Top = 14,
        };

        var subtitle = new Label
        {
            Text = "Clique no ícone ou no botão para ajustar o blend — olhe o telão, não o preview.",
            AutoSize = true,
            MaximumSize = new Size(330, 0),
            ForeColor = SystemColors.GrayText,
            Left = 72,
            Top = 34,
        };

        _status.Left = 16;
        _status.Top = 72;
        _status.AutoSize = true;
        _status.MaximumSize = new Size(388, 0);
        _status.Text = "Verificando...";

        _blend.Text = "Ajustar blend do telão";
        _blend.Left = 16;
        _blend.Top = 118;
        _blend.Size = new Size(388, 44);
        _blend.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        _blend.FlatStyle = FlatStyle.Flat;
        _blend.Image = IconFactory.UiBitmap;
        _blend.TextImageRelation = TextImageRelation.ImageBeforeText;
        _blend.ImageAlign = ContentAlignment.MiddleLeft;
        _blend.Padding = new Padding(10, 0, 8, 0);
        _blend.Cursor = Cursors.Hand;
        _blend.Click += (_, _) => _showBlend();
        tip.SetToolTip(_blend, "Ajustar blend do telão");

        var settings = NewButton("Configurações...", 16, 174, _showSettings);
        var preview = NewButton("Ver o monitor", 216, 174, _showPreview);
        var test = NewButton("Testar tela...", 16, 216, _showTest);
        var hide = NewButton("Minimizar", 216, 216, MinimizeQuiet);

        Controls.Add(iconBox);
        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(_status);
        Controls.Add(_blend);
        Controls.Add(settings);
        Controls.Add(preview);
        Controls.Add(test);
        Controls.Add(hide);
    }

    public void Atualizar(string status, bool blendEnabled)
    {
        _status.Text = status;
        _blend.Enabled = blendEnabled;
        _blend.UseVisualStyleBackColor = !blendEnabled;
        _blend.BackColor = blendEnabled ? Color.FromArgb(60, 160, 100) : SystemColors.Control;
        _blend.ForeColor = blendEnabled ? Color.White : SystemColors.GrayText;
        _blend.FlatAppearance.BorderColor = blendEnabled ? Color.FromArgb(40, 120, 75) : SystemColors.ControlDark;
        _blend.Text = blendEnabled
            ? "Ajustar blend do telão"
            : "Ajustar blend do telão (ligue o surround)";
    }

    public void PermitirFechar() => _allowClose = true;

    protected override bool ShowWithoutActivation => _silentShow;

    public void MostrarMinimizado()
    {
        _silentShow = true;
        WindowState = FormWindowState.Minimized;
        Show();
        _silentShow = false;
    }

    public void MostrarNaFrente()
    {
        _silentShow = false;
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Show();
        Activate();
        BringToFront();
    }

    private void MinimizeQuiet()
    {
        WindowState = FormWindowState.Minimized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
            return;
        }

        base.OnFormClosing(e);
    }

    private static Button NewButton(string text, int left, int top, Action click)
    {
        var button = new Button
        {
            Text = text,
            Left = left,
            Top = top,
            Size = new Size(188, 36),
        };
        button.Click += (_, _) => click();
        return button;
    }
}
