using System.Runtime.InteropServices;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App.Surround;

/// <summary>Janela sem borda em cima de um projetor, mostrando a fatia do canvas.</summary>
internal sealed class SurroundOutputForm : Form
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

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

    /// <summary>
    /// O Holyrics abre janelas full-screen nos projetores e cobre o overlay.
    /// Reafirma TOPMOST sem ativar, para a fatia blendada continuar na parede.
    /// </summary>
    public void KeepOnTop()
    {
        if (!IsHandleCreated || IsDisposed) return;
        TopMost = true;
        if (!Visible) return;
        SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
