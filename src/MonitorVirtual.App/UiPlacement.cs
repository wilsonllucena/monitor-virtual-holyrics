namespace MonitorVirtual.App;

/// <summary>
/// Coloca janelas do operador na tela que não é projetor — senão o overlay do
/// telão cobre o painel. Com só 2 projetores, usa o canto da primária.
/// </summary>
internal static class UiPlacement
{
    public static Screen OperatorScreen(IReadOnlyCollection<string>? projectorDeviceNames)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0) return Screen.PrimaryScreen ?? throw new InvalidOperationException();

        if (projectorDeviceNames is { Count: > 0 })
        {
            var covered = new HashSet<string>(projectorDeviceNames, StringComparer.OrdinalIgnoreCase);
            var free = screens.FirstOrDefault(s => !covered.Contains(s.DeviceName));
            if (free is not null) return free;
        }

        return Screen.PrimaryScreen ?? screens[0];
    }

    public static void Place(Form form, IReadOnlyCollection<string>? projectorDeviceNames, bool cornerIfCovered = true)
    {
        var screen = OperatorScreen(projectorDeviceNames);
        var area = screen.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;

        var covered = projectorDeviceNames is { Count: > 0 }
            && projectorDeviceNames.Contains(screen.DeviceName, StringComparer.OrdinalIgnoreCase);

        if (cornerIfCovered && covered)
        {
            form.Location = new Point(
                area.Right - form.Width - 24,
                area.Bottom - form.Height - 24);
        }
        else
        {
            form.Location = new Point(
                area.Left + Math.Max(0, (area.Width - form.Width) / 2),
                area.Top + Math.Max(0, (area.Height - form.Height) / 2));
        }
    }

    public static void RaiseAboveOverlays(Form? form)
    {
        if (form is not { IsHandleCreated: true, Visible: true, IsDisposed: false }) return;
        if (form.WindowState == FormWindowState.Minimized) return;

        NativeMethods.SetWindowPos(
            form.Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }
}
