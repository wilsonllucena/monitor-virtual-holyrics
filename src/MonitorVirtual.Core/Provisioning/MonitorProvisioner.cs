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
    string Summary)
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
        var dev = _driver.GetStatus();
        if (!dev.Present) return ProvisionStatus.NotInstalled("Driver não instalado.");

        var adapter = _display.FindVirtual();
        var geometry = adapter is null ? null : _display.GetGeometry(adapter.DeviceName);
        var active = adapter is { Attached: true };

        var summary = !dev.Enabled
            ? "Dispositivo desabilitado."
            : active
                ? $"Monitor virtual ativo em {geometry?.Width}x{geometry?.Height}@{geometry?.RefreshRate}Hz " +
                  $"na posição ({geometry?.X},{geometry?.Y})."
                : "Dispositivo habilitado, aguardando o monitor aparecer.";

        return new ProvisionStatus(
            true, dev.Enabled, active, _display.IsExtended(),
            adapter?.DeviceName, adapter?.MonitorName, geometry, summary);
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

        if (cfg.Enabled && cfg.SurroundEnabled)
            (width, height) = ApplySurroundTopology(cfg, width, height);

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

        // 2) o monitor virtual nunca deve ser o primário
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
        var virtName = (_display.FindVirtual() ?? virtualAdapter).DeviceName;
        var (x, y) = ComputePosition(cfg, width);
        _display.ApplyMode(virtName, width, height, cfg.RefreshRate, x, y);

        return GetStatus();
    }

    /// <summary>
    /// Sai do clone, alinha os projetores lado a lado e devolve o tamanho do canvas
    /// único para o monitor virtual.
    /// </summary>
    private (int Width, int Height) ApplySurroundTopology(AppConfig cfg, int width, int height)
    {
        if (!_display.IsExtended())
        {
            Log.Info("Surround: Windows estava em clone/espelho; aplicando Estender.");
            _display.ApplyExtendTopology();
            Thread.Sleep(500);
        }

        var physical = _display.ListPhysical();
        var selected = SurroundPlanner.SelectMonitors(physical, cfg);
        if (selected.Count < 2)
        {
            Log.Info($"Surround ligado, mas há {physical.Count} monitor(es) físico(s); canvas inalterado.");
            return (width, height);
        }

        _display.ArrangeSideBySide(selected);
        Thread.Sleep(200);

        physical = _display.ListPhysical();
        var plan = SurroundPlanner.TryCreate(physical, cfg);
        if (plan is null) return (width, height);

        Log.Info($"Surround: {plan.Summary}.");
        return cfg.SurroundSyncResolution
            ? (plan.CanvasWidth, plan.CanvasHeight)
            : (width, height);
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
