using System.Diagnostics;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Startup;

/// <summary>
/// Auto-início via Agendador de Tarefas com "executar com privilégios mais altos":
/// no logon o app sobe elevado sem prompt de UAC — é o que permite ligar/desligar
/// o monitor no domingo de manhã sem ninguém clicar em "Sim".
/// </summary>
public static class StartupTask
{
    public const string TaskName = "MonitorVirtualHolyrics";

    public static bool Exists()
    {
        var (code, _) = Run($"/Query /TN \"{TaskName}\"");
        return code == 0;
    }

    public static bool Enable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            Log.Error($"Início automático não configurado: executável inválido ('{exePath}').");
            return false;
        }

        var args = $"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --background\" /SC ONLOGON /RL HIGHEST";
        var (code, output) = Run(args);
        if (code != 0) Log.Error($"Falha ao criar tarefa de logon ({code}): {output}");
        else Log.Info("Tarefa de início automático criada.");
        return code == 0;
    }

    public static bool Disable()
    {
        if (!Exists()) return true;
        var (code, output) = Run($"/Delete /F /TN \"{TaskName}\"");
        if (code != 0) Log.Error($"Falha ao remover tarefa de logon ({code}): {output}");
        return code == 0;
    }

    private static (int Code, string Output) Run(string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            return (p.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            Log.Error("schtasks falhou", ex);
            return (-1, ex.Message);
        }
    }
}
