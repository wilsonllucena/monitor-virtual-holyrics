using MonitorVirtual.Core;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.App;

internal static class Program
{
    internal const string MutexName = @"Global\MonitorVirtualHolyrics";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "O Monitor Virtual já está em execução (veja o ícone perto do relógio).",
                "Monitor Virtual", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AppPaths.EnsureDataDirs();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Exceção não tratada", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) =>
            Log.Error("Exceção na UI", e.Exception);

        var background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        var preview = args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        Log.Info($"Monitor Virtual iniciado (background={background}, elevado={Elevation.IsElevated()}).");

        Application.Run(new TrayApp(background, preview));
    }
}
