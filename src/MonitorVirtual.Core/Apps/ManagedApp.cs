using System.Text.Json.Serialization;

namespace MonitorVirtual.Core.Apps;

/// <summary>
/// Um programa que consome o monitor virtual (Holyrics, Resolume Arena, OBS...).
/// Todos eles têm o mesmo problema: montam a lista de telas quando abrem, então
/// precisam subir *depois* do monitor virtual estar pronto.
/// </summary>
public sealed class ManagedApp
{
    public string Name { get; set; } = string.Empty;

    public string ExePath { get; set; } = string.Empty;

    /// <summary>Nome do processo sem extensão. Se vazio, é derivado do ExePath.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Inicia este programa assim que o monitor virtual ficar ativo.</summary>
    public bool LaunchAfterMonitor { get; set; } = true;

    /// <summary>
    /// Reinicia sozinho quando o programa estava aberto antes do monitor aparecer.
    /// Desligado por padrão: fechar um programa de projeção durante o culto é destrutivo.
    /// </summary>
    public bool AutoRestartIfEarly { get; set; }

    [JsonIgnore]
    public string EffectiveProcessName =>
        string.IsNullOrWhiteSpace(ProcessName)
            ? Path.GetFileNameWithoutExtension(ExePath)
            : ProcessName!;

    public ManagedApp Clone() => new()
    {
        Name = Name,
        ExePath = ExePath,
        ProcessName = ProcessName,
        LaunchAfterMonitor = LaunchAfterMonitor,
        AutoRestartIfEarly = AutoRestartIfEarly,
    };
}
