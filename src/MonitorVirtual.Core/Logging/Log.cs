using System.Text;

namespace MonitorVirtual.Core.Logging;

/// <summary>Log de arquivo simples com rotação diária (sem dependências externas).</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly List<Action<string>> Sinks = new();

    /// <summary>Permite que a UI acompanhe o log em tempo real.</summary>
    public static void AddSink(Action<string> sink)
    {
        lock (Gate) Sinks.Add(sink);
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERRO", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                var file = Path.Combine(AppPaths.LogDir, $"monitorvirtual-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                Cleanup();
            }
            catch
            {
                // log nunca derruba o app
            }

            foreach (var sink in Sinks)
            {
                try { sink(line); } catch { /* ignore */ }
            }
        }
    }

    private static void Cleanup()
    {
        var files = new DirectoryInfo(AppPaths.LogDir).GetFiles("monitorvirtual-*.log");
        foreach (var f in files.Where(f => f.LastWriteTime < DateTime.Now.AddDays(-30)))
        {
            try { f.Delete(); } catch { /* ignore */ }
        }
    }
}
