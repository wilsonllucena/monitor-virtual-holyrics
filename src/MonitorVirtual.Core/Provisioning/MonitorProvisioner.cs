using System.Diagnostics;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Devices;
using MonitorVirtual.Core.Display;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.Core.Provisioning;

public sealed record ProvisionStatus(
    bool DriverInstalled,
    bool DeviceEnabled,
    bool MonitorActive,
    bool Extended,
    string? AdapterDeviceName,
    string? MonitorName,
    DisplayGeometry? Geometry,
    string Summary,
    SurroundSurface? Surround = null)
{
    public static ProvisionStatus NotInstalled(string reason) =>
        new(false, false, false, false, null, null, null, reason);
}

/// <summary>
/// Reconcilia o estado real do Windows com o estado desejado da configuração.
/// É idempotente: pode (e deve) rodar a cada poucos segundos.
/// </summary>
public sealed class MonitorProvisioner
{
    private readonly DriverManager _driver = new();
    private readonly DisplayService _display = new();
    private readonly object _gate = new();

    public DriverManager Driver => _driver;
    public DisplayService Display => _display;

    /// <summary>Instala o driver (uma vez, exige elevação). Idempotente.</summary>
    public bool EnsureDriverInstalled(out bool rebootRequired)
    {
        rebootRequired = false;
        AppPaths.EnsureDataDirs();
        VddSettings.EnsureRegistryPath();

        if (_driver.GetStatus().Present) return true;

        if (!File.Exists(AppPaths.DriverInfPath))
        {
            Log.Error($"Payload do driver ausente: {AppPaths.DriverInfPath}. " +
                      "Rode tools/fetch-driver.ps1 ou reinstale o aplicativo.");
            return false;
        }

        return _driver.Install(AppPaths.DriverInfPath, out rebootRequired);
    }

    /// <summary>Lê o estado atual sem alterar nada.</summary>
    public ProvisionStatus GetStatus()
    {
        var nvidia = NvidiaSpan.DetectActive();
        var dev = _driver.GetStatus();
        if (!dev.Present && nvidia is null)
            return ProvisionStatus.NotInstalled("Driver não instalado.");

        var adapter = _display.FindVirtual();
        var geometry = adapter is null ? null : _display.GetGeometry(adapter.DeviceName);
        var active = adapter is { Attached: true };

        string summary;
        if (nvidia is not null)
            summary = nvidia.Summary;
        else if (!dev.Present)
            summary = "Driver não instalado.";
        else if (!dev.Enabled)
            summary = "Dispositivo desabilitado.";
        else if (active)
            summary = $"Monitor virtual ativo em {geometry?.Width}x{geometry?.Height}@{geometry?.RefreshRate}Hz " +
                      $"na posição ({geometry?.X},{geometry?.Y}).";
        else
            summary = "Dispositivo habilitado, aguardando o monitor aparecer.";

        return new ProvisionStatus(
            dev.Present, dev.Enabled, active, _display.IsExtended(),
            nvidia?.AdapterDeviceName ?? adapter?.DeviceName,
            adapter?.MonitorName, geometry, summary, nvidia);
    }

    /// <summary>Leva o sistema ao estado descrito na configuração.</summary>
    public ProvisionStatus Reconcile(AppConfig cfg)
    {
        lock (_gate)
        {
            try
            {
                return ReconcileCore(cfg);
            }
            catch (Exception ex)
            {
                Log.Error("Falha na reconciliação", ex);
                return ProvisionStatus.NotInstalled($"Erro: {ex.Message}");
            }
        }
    }

    private ProvisionStatus ReconcileCore(AppConfig cfg)
    {
        AppPaths.EnsureDataDirs();
        VddSettings.EnsureRegistryPath();

        var width = cfg.Width;
        var height = cfg.Height;
        SurroundSurface? surround = null;

        if (cfg.SurroundEnabled)
            (width, height, surround) = ApplySurroundTopology(cfg, width, height);

        if (surround is { Kind: SurroundSurfaceKind.NvidiaLogical })
            return FinishNvidiaSpan(surround);

        if (!cfg.SurroundEnabled)
            NvidiaSpan.TryDisable();

        var dev = _driver.GetStatus();
        if (!dev.Present)
        {
            if (!cfg.Enabled) return ProvisionStatus.NotInstalled("Driver não instalado (monitor desligado).");
            if (!EnsureDriverInstalled(out _)) return ProvisionStatus.NotInstalled("Falha ao instalar o driver.");
            dev = _driver.GetStatus();
            WaitForVirtualAdapter(TimeSpan.FromSeconds(10));
        }

        // O XML só é lido quando o dispositivo inicia; se mudou, reinicia o dispositivo.
        var settingsChanged = VddSettings.Write(1, width, height, cfg.RefreshRate);

        if (!cfg.Enabled)
        {
            if (dev.Enabled) _driver.SetEnabled(false);
            return GetStatus();
        }

        if (!dev.Enabled)
        {
            _driver.SetEnabled(true);
            WaitForVirtualAdapter(TimeSpan.FromSeconds(10));
        }
        else if (settingsChanged)
        {
            Log.Info("Configuração do driver mudou; reiniciando o dispositivo.");
            _driver.Restart();
            WaitForVirtualAdapter(TimeSpan.FromSeconds(10));
        }

        var virtualAdapter = WaitForVirtualAdapter(TimeSpan.FromSeconds(5));
        if (virtualAdapter is null)
            return GetStatus() with { Summary = "Monitor virtual não apareceu na lista de vídeo do Windows." };

        // 1) topologia estendida — causa nº 1 de "o Holyrics não projeta"
        //    surround exige Estender: clone/espelho manda o mesmo slide nos dois projetores.
        if ((cfg.ForceExtend || cfg.SurroundEnabled) && !_display.IsExtended())
        {
            Log.Info("Topologia não está estendida; aplicando Estender.");
            _display.ApplyExtendTopology();
            Thread.Sleep(500);
            virtualAdapter = _display.FindVirtual() ?? virtualAdapter;
        }

        var virtName = (_display.FindVirtual() ?? virtualAdapter).DeviceName;
        var overlayPrimary = surround is { Kind: SurroundSurfaceKind.VirtualOverlay };
        var selectedProjectors = overlayPrimary
            ? SurroundPlanner.SelectMonitors(_display.ListPhysical(), cfg)
            : Array.Empty<SurroundMonitor>();
        var operatorDesk = overlayPrimary && _display.ListPhysical().Count > selectedProjectors.Count;

        // 2 projetores = o canvas virtual é o desktop (taskbar no telão).
        // Mesa + 2 projetores = o operador fica com o primário.
        if (overlayPrimary && !operatorDesk)
        {
            var virt = _display.ListAdapters().FirstOrDefault(a => a.IsVirtual);
            if (virt is { Primary: false, Attached: true })
            {
                Log.Info($"Surround overlay: canvas virtual vira primário ({virt.DeviceName}) para a taskbar atravessar o telão.");
                _display.MakePrimary(virt.DeviceName);
            }

            virtName = (_display.FindVirtual() ?? virtualAdapter).DeviceName;
            _display.ApplyMode(virtName, width, height, cfg.RefreshRate, 0, 0);
            _display.ArrangeInRow(selectedProjectors, width, 0);
            Thread.Sleep(200);

            return GetStatus() with
            {
                Surround = OverlaySurface(width, height, virtName),
                Summary = OverlaySurface(width, height, virtName).Summary,
            };
        }

        if (overlayPrimary && operatorDesk)
        {
            virtName = (_display.FindVirtual() ?? virtualAdapter).DeviceName;
            var (ox, oy) = ComputePosition(cfg, width);
            _display.ApplyMode(virtName, width, height, cfg.RefreshRate, ox, oy);
            return GetStatus() with
            {
                Surround = OverlaySurface(width, height, virtName) with
                {
                    X = ox,
                    Y = oy,
                    Summary = $"{width}x{height} canvas (mesa do operador intacta) + blend nos projetores",
                },
            };
        }

        if (cfg.NeverPrimary)
        {
            var current = _display.ListAdapters();
            var virt = current.FirstOrDefault(a => a.IsVirtual);
            if (virt is { Primary: true })
            {
                var other = current.FirstOrDefault(a => !a.IsVirtual && a.Attached);
                if (other is not null)
                {
                    Log.Info($"Monitor virtual estava como primário; passando para {other.DeviceName}.");
                    _display.MakePrimary(other.DeviceName);
                }
            }
        }

        // 3) resolução e posição estáveis (o Holyrics guarda a tela pela posição/índice)
        virtName = (_display.FindVirtual() ?? virtualAdapter).DeviceName;
        var (x, y) = ComputePosition(cfg, width);
        _display.ApplyMode(virtName, width, height, cfg.RefreshRate, x, y);

        var status = GetStatus();
        return surround is null ? status : status with { Surround = surround };
    }

    /// <summary>
    /// NVIDIA Surround: o Windows já vê um monitor só. Desconectamos o IddCx do
    /// desktop para ele não aparecer como tela extra (Holyrics/taskbar no telão).
    /// </summary>
    private ProvisionStatus FinishNvidiaSpan(SurroundSurface surround)
    {
        var virt = _display.FindVirtual();
        if (virt is { Attached: true })
        {
            Log.Info($"Surround NVIDIA: desconectando {virt.DeviceName} do desktop (o telão já é o monitor único).");
            _display.Detach(virt.DeviceName);
        }

        var largest = _display.FindLargestPhysical();
        if (largest is not null)
        {
            surround = surround with
            {
                X = largest.Value.Geometry.X,
                Y = largest.Value.Geometry.Y,
                Width = largest.Value.Geometry.Width,
                Height = largest.Value.Geometry.Height,
                AdapterDeviceName = largest.Value.Adapter.DeviceName,
            };

            var others = _display.ListPhysical()
                .Where(m => !string.Equals(m.DeviceName, largest.Value.Adapter.DeviceName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            // Sem mesa extra, o telão único é o primário (taskbar de ponta a ponta).
            // Com operador, o primário fica na mesa.
            if (others.Count == 0 && !largest.Value.Adapter.Primary)
                _display.MakePrimary(largest.Value.Adapter.DeviceName);
        }

        var st = GetStatus();
        return st with
        {
            Surround = surround,
            Summary = surround.Summary,
        };
    }

    private static SurroundSurface OverlaySurface(int width, int height, string virtName) =>
        new(SurroundSurfaceKind.VirtualOverlay, 0, 0, width, height, virtName,
            $"{width}x{height} canvas primário (taskbar no telão) + blend nas saídas físicas");

    /// <summary>
    /// Sai do clone, tenta NVIDIA Surround (1 monitor lógico) e, se o driver recusar,
    /// devolve o canvas IddCx para o overlay. 1 monitor físico não altera nada.
    /// </summary>
    private (int Width, int Height, SurroundSurface? Surface) ApplySurroundTopology(
        AppConfig cfg, int width, int height)
    {
        if (!_display.IsExtended())
        {
            Log.Info("Surround: Windows estava em clone/espelho; aplicando Estender.");
            _display.ApplyExtendTopology();
            Thread.Sleep(500);
        }

        var already = NvidiaSpan.DetectActive();
        if (already is not null)
        {
            Log.Info($"Surround NVIDIA já ativo: {already.Summary}.");
            return (already.Width, already.Height, already);
        }

        var physical = _display.ListPhysical();
        var selected = SurroundPlanner.SelectMonitors(physical, cfg);
        if (selected.Count < 2)
        {
            Log.Info($"Surround ligado, mas há {physical.Count} monitor(es) físico(s); canvas inalterado.");
            return (width, height, null);
        }

        _display.ArrangeSideBySide(selected);
        Thread.Sleep(200);

        physical = _display.ListPhysical();
        var plan = SurroundPlanner.TryCreate(physical, cfg);
        if (plan is null) return (width, height, null);

        var nvidia = NvidiaSpan.TryEnable(plan.Monitors, plan.Overlap, cfg.RefreshRate);
        if (nvidia.Ok && nvidia.Surface is not null)
        {
            Log.Info($"Surround: {nvidia.Surface.Summary}.");
            return (nvidia.Surface.Width, nvidia.Surface.Height, nvidia.Surface);
        }

        Log.Info($"Surround NVIDIA indisponível ({nvidia.Detail}); canvas virtual primário + blend nas saídas.");

        var canvasW = cfg.SurroundSyncResolution ? plan.CanvasWidth : width;
        var canvasH = cfg.SurroundSyncResolution ? plan.CanvasHeight : height;
        var overlay = new SurroundSurface(
            SurroundSurfaceKind.VirtualOverlay, 0, 0, canvasW, canvasH, null,
            $"{canvasW}x{canvasH} canvas primário (taskbar no telão) + blend nas saídas físicas");
        return (canvasW, canvasH, overlay);
    }

    private (int X, int Y) ComputePosition(AppConfig cfg, int virtWidth)
    {
        var physical = _display.ListPhysical();
        if (physical.Count == 0)
        {
            var primary = _display.FindPrimary();
            var geo = primary is null ? null : _display.GetGeometry(primary.DeviceName);
            if (geo is null) return (0, 0);
            return cfg.Side == MonitorSide.Direita
                ? (geo.X + geo.Width, geo.Y)
                : (geo.X - virtWidth, geo.Y);
        }

        var minX = physical.Min(m => m.X);
        var maxRight = physical.Max(m => m.X + m.Width);
        var y = physical.FirstOrDefault(m => m.Primary)?.Y ?? physical[0].Y;

        return cfg.Side == MonitorSide.Direita
            ? (maxRight, y)
            : (minX - virtWidth, y);
    }

    private DisplayAdapter? WaitForVirtualAdapter(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var adapter = _display.FindVirtual();
            if (adapter is { Attached: true }) return adapter;
            Thread.Sleep(300);
        }

        return _display.FindVirtual();
    }
}
