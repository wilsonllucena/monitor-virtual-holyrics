using MonitorVirtual.Core;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.App;

internal static class Program
{
    internal const string MutexName = @"Global\MonitorVirtualHolyrics";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // asInvoker no manifesto: quem chama via CreateProcess (Inno Setup,
        // scripts) não toma erro 740. Pedimos UAC aqui, antes do mutex, para
        // a instância elevada ser a que fica residente.
        if (!Elevation.IsElevated())
        {
            var forwarded = string.Join(" ", args.Select(QuoteArg));
            if (!Elevation.RelaunchElevated(forwarded))
            {
                MessageBox.Show(
                    "O Monitor Virtual precisa de permissão de administrador para criar a tela virtual." +
                    Environment.NewLine + Environment.NewLine +
                    "Aceite o aviso do Controle de Conta de Usuário (UAC) ou clique com o botão direito no atalho e escolha Executar como administrador.",
                    "Monitor Virtual", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "O Monitor Virtual já está em execução (veja o ícone perto do relógio).",
                "Monitor Virtual", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AppPaths.EnsureDataDirs();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Exceção não tratada", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) =>
            Log.Error("Exceção na UI", e.Exception);

        var background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        var preview = args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        Log.Info($"Monitor Virtual iniciado (background={background}, elevado={Elevation.IsElevated()}).");

        Application.Run(new TrayApp(background, preview));
    }

    private static string QuoteArg(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (!arg.Contains(' ') && !arg.Contains('"')) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}
