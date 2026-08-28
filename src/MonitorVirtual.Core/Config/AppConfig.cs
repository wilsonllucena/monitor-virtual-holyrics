using System.Text.Json;
using System.Text.Json.Serialization;
using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Config;

public enum MonitorSide
{
    Direita = 0,
    Esquerda = 1,
}

/// <summary>Estado desejado do monitor virtual. Persistido em %ProgramData%\MonitorVirtual\config.json.</summary>
public sealed class AppConfig
{
    /// <summary>Se true, o monitor virtual deve estar presente e ativo.</summary>
    public bool Enabled { get; set; } = true;

    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int RefreshRate { get; set; } = 60;

    /// <summary>Onde o monitor virtual fica no desktop estendido, em relação ao primário.</summary>
    public MonitorSide Side { get; set; } = MonitorSide.Direita;

    /// <summary>Força topologia "Estender" (Win+P) sempre que reconciliar.</summary>
    public bool ForceExtend { get; set; } = true;

    /// <summary>
    /// Dois projetores viram um telão só: o Holyrics projeta num canvas único
    /// (ex.: 3840×1080) e o app recorta com soft-edge blend na junta.
    /// Desligado não altera quem tem 1 monitor ou operador + 1 projetor.
    /// </summary>
    public bool SurroundEnabled { get; set; }

    /// <summary>Pixels de overposição na junta. 0 = corte seco; 128–256 para projetores.</summary>
    public int SurroundBlendOverlap { get; set; } = 192;

    /// <summary>
    /// Gama da compensação do fade. 1.0 = cosseno linear (bom no monitor);
    /// 2.2 clareia a junta na parede (dois projetores somam luz). Maior = junta mais clara.
    /// </summary>
    public double SurroundBlendGamma { get; set; } = 2.2;

    /// <summary>Multiplica o fade na overposição. &gt; 1 clareia a faixa preta no telão.</summary>
    public double SurroundBlendGain { get; set; } = 1.0;

    /// <summary>
    /// Com surround ligado, aponta a Tela pública do Holyrics para o monitor virtual
    /// e oculta telas extras que caem nos projetores (evita a imagem dividida).
    /// </summary>
    public bool SurroundSteerHolyrics { get; set; } = true;

    /// <summary>Taxa da saída nos projetores físicos.</summary>
    public int SurroundOutputFps { get; set; } = 24;

    /// <summary>Ajusta a resolução do monitor virtual para o tamanho do canvas surround.</summary>
    public bool SurroundSyncResolution { get; set; } = true;

    /// <summary>
    /// Com 3+ telas, ignora o primário (mesa do operador) e usa o resto no telão.
    /// Com só 2, as duas entram — um deles é o primário do Windows.
    /// </summary>
    public bool SurroundPreferNonPrimary { get; set; } = true;

    /// <summary>Troca esquerda/direita se o Windows numerou os projetores ao contrário.</summary>
    public bool SurroundSwap { get; set; }

    /// <summary>
    /// Desloca a fatia no canvas (px). Positivo = conteúdo anda para a esquerda
    /// no projetor. Serve para coincidir a letra da parede com o monitor virtual.
    /// </summary>
    public int SurroundAlignLeftX { get; set; }

    public int SurroundAlignRightX { get; set; }

    /// <summary>DeviceName dos monitores do telão. Vazio = detecção automática.</summary>
    public List<string> SurroundDeviceNames { get; set; } = new();

    /// <summary>Garante que o monitor virtual nunca seja o primário.</summary>
    public bool NeverPrimary { get; set; } = true;

    /// <summary>Watchdog: intervalo de reconciliação em segundos (0 desliga).</summary>
    public int WatchdogSeconds { get; set; } = 5;

    /// <summary>
    /// Programas que consomem o monitor virtual (Holyrics, Resolume Arena, OBS...).
    /// Todos precisam abrir depois que o monitor está pronto.
    /// </summary>
    public List<ManagedApp> ManagedApps { get; set; } = new();

    // --- campos legados, mantidos só para migrar config.json antigo ---
    public bool LaunchHolyrics { get; set; }
    public string? HolyricsPath { get; set; }
    public bool AutoRestartHolyrics { get; set; }

    /// <summary>Taxa de atualização da janela de visualização do monitor virtual.</summary>
    public int PreviewFps { get; set; } = 15;

    /// <summary>API local do Holyrics (Configurações → API Server).</summary>
    public int HolyricsApiPort { get; set; } = 8091;

    public string? HolyricsApiToken { get; set; }

    /// <summary>
    /// O NDI do Holyrics (v2.29+) sai com fundo transparente por padrão — só a letra.
    /// No Resolume isso vira xadrez no preview e preto na composition. Quando true,
    /// desligamos <c>transparent_background</c> via API para o papel de fundo ir junto.
    /// </summary>
    public bool HolyricsIncludeNdiBackground { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    [JsonIgnore]
    public string ResolutionText => $"{Width}x{Height} @ {RefreshRate}Hz";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile), JsonOpts);
                if (cfg is not null) return cfg.Normalized();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao ler config.json, usando padrões", ex);
        }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureDataDirs();
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(Normalized(), JsonOpts));
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error("Sem permissão para gravar config.json — rode o Monitor Virtual " +
                      "(ou este comando) como Administrador uma vez para corrigir as permissões", ex);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao gravar config.json", ex);
        }
    }

    private AppConfig Normalized()
    {
        if (Width < 640) Width = 640;
        if (Height < 480) Height = 480;
        if (RefreshRate < 24) RefreshRate = 60;
        if (WatchdogSeconds is not 0 and < 2) WatchdogSeconds = 2;
        SurroundBlendOverlap = Math.Clamp(SurroundBlendOverlap, 0, 960);
        SurroundBlendGamma = Math.Clamp(SurroundBlendGamma, 0.4, 3);
        SurroundBlendGain = Math.Clamp(SurroundBlendGain, 0.25, 2.5);
        SurroundAlignLeftX = Math.Clamp(SurroundAlignLeftX, -480, 480);
        SurroundAlignRightX = Math.Clamp(SurroundAlignRightX, -480, 480);
        SurroundOutputFps = Math.Clamp(SurroundOutputFps, 5, 60);
        SurroundDeviceNames ??= new();

        MigrateHolyricsFields();
        return this;
    }

    /// <summary>Converte a configuração antiga (só Holyrics) na lista de programas.</summary>
    private void MigrateHolyricsFields()
    {
        if (string.IsNullOrWhiteSpace(HolyricsPath)) return;
        if (ManagedApps.Any(a => string.Equals(a.ExePath, HolyricsPath, StringComparison.OrdinalIgnoreCase)))
        {
            HolyricsPath = null;
            return;
        }

        ManagedApps.Add(new ManagedApp
        {
            Name = "Holyrics",
            ExePath = HolyricsPath!,
            ProcessName = "Holyrics",
            LaunchAfterMonitor = LaunchHolyrics,
            AutoRestartIfEarly = AutoRestartHolyrics,
        });

        HolyricsPath = null;
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts)!;
}
