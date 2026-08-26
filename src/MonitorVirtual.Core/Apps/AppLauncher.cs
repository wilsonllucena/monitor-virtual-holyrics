using System.Diagnostics;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Apps;

/// <summary>Detecta, inicia e reinicia os programas que consomem o monitor virtual.</summary>
public static class AppLauncher
{
    /// <summary>Programas conhecidos, com o nome de processo correto de cada um.</summary>
    private static readonly (string Name, string Exe, string Process)[] Known =
    {
        ("Holyrics", "Holyrics.exe", "Holyrics"),
        ("Resolume Arena", "Arena.exe", "Arena"),
        ("Resolume Avenue", "Avenue.exe", "Avenue"),
        ("OBS Studio", "obs64.exe", "obs64"),
    };

    public static Process? GetProcess(ManagedApp app)
    {
        var name = app.EffectiveProcessName;
        return string.IsNullOrWhiteSpace(name)
            ? null
            : Process.GetProcessesByName(name).FirstOrDefault();
    }

    public static bool IsRunning(ManagedApp app) => GetProcess(app) is not null;

    public static DateTime? GetStartTime(ManagedApp app)
    {
        try { return GetProcess(app)?.StartTime; }
        catch { return null; }
    }

    /// <summary>
    /// Inicia o programa via explorer.exe: assim ele roda sem elevação, mesmo que o
    /// Monitor Virtual esteja elevado (programa elevado tem problemas com drag-and-drop,
    /// atalhos e arquivos de rede).
    /// </summary>
    public static bool Launch(ManagedApp app)
    {
        if (string.IsNullOrWhiteSpace(app.ExePath) || !File.Exists(app.ExePath))
        {
            Log.Error($"{app.Name}: executável não encontrado em '{app.ExePath}'.");
            return false;
        }

        if (IsRunning(app))
        {
            Log.Info($"{app.Name} já está em execução.");
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{app.ExePath}\"",
                UseShellExecute = false,
            });

            Log.Info($"{app.Name} iniciado: {app.ExePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao iniciar {app.Name}", ex);
            return false;
        }
    }

    /// <summary>
    /// Fecha e reabre o programa — único jeito de ele enxergar um monitor que apareceu
    /// depois que ele já estava aberto.
    /// </summary>
    public static bool Restart(ManagedApp app, TimeSpan? gracePeriod = null)
    {
        var grace = gracePeriod ?? TimeSpan.FromSeconds(15);
        var proc = GetProcess(app);

        if (proc is not null)
        {
            try
            {
                Log.Info($"Fechando {app.Name} para que ele redetecte os monitores.");
                if (proc.CloseMainWindow())
                    proc.WaitForExit((int)grace.TotalMilliseconds);

                if (!proc.HasExited)
                {
                    Log.Warn($"{app.Name} não fechou sozinho; encerrando o processo.");
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao fechar {app.Name}", ex);
                return false;
            }
        }

        Thread.Sleep(1500);
        return Launch(app);
    }

    /// <summary>Procura os programas conhecidos instalados na máquina.</summary>
    public static IReadOnlyList<ManagedApp> Autodetect()
    {
        var found = new List<ManagedApp>();

        foreach (var (name, exe, process) in Known)
        {
            var path = FindExecutable(exe);
            if (path is null) continue;

            found.Add(new ManagedApp
            {
                Name = name,
                ExePath = path,
                ProcessName = process,
                LaunchAfterMonitor = true,
            });
        }

        return found;
    }

    public static string? FindExecutable(string exeName)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"C:\",
        };

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        {
            // caminho direto: C:\Holyrics\Holyrics.exe, C:\Program Files\Resolume Arena\Arena.exe
            foreach (var candidate in EnumerateCandidateDirs(root))
            {
                var full = Path.Combine(candidate, exeName);
                if (File.Exists(full)) return full;
            }
        }

        // último recurso: já está rodando?
        try
        {
            var proc = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)).FirstOrDefault();
            return proc?.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirs(string root)
    {
        yield return root;

        string[] subdirs;
        try
        {
            // só um nível: instaladores usam "Resolume Arena", "Resolume Arena 7", "Holyrics"
            subdirs = Directory.GetDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in subdirs)
        {
            yield return dir;

            var name = Path.GetFileName(dir);
            if (!name.StartsWith("Resolume", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Holyrics", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("obs", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] nested;
            try { nested = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sub in nested) yield return sub;
        }
    }
}
