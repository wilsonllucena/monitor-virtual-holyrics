using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App.Surround;

/// <summary>Janela sem borda em cima de um projetor, mostrando a fatia do canvas.</summary>
internal sealed class SurroundOutputForm : Form
{
    private readonly PictureBox _canvas = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.StretchImage,
        BackColor = Color.Black,
    };

    public string DeviceName { get; }
    private bool _allowClose;

    public SurroundOutputForm(SurroundSlice slice)
    {
        DeviceName = slice.DeviceName;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        ShowIcon = false;
        KeyPreview = true;
        SetBounds(slice);

        Controls.Add(_canvas);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — fora do Alt+Tab
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE — não rouba o Holyrics
            return cp;
        }
    }

    public void SetBounds(SurroundSlice slice)
    {
        var screen = Screen.AllScreens.FirstOrDefault(s =>
            string.Equals(s.DeviceName, slice.DeviceName, StringComparison.OrdinalIgnoreCase));

        var next = screen?.Bounds
                   ?? new Rectangle(slice.OutputX, slice.OutputY, slice.OutputWidth, slice.OutputHeight);
        if (Bounds != next) Bounds = next;
    }

    public void Present(Bitmap? frame)
    {
        _canvas.Image = frame;
        if (frame is not null) _canvas.Invalidate();
    }

    public void Shutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
        else
            base.OnFormClosing(e);
    }
}
