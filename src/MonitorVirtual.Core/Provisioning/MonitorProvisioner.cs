using System.Diagnostics;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Devices;
using MonitorVirtual.Core.Display;
using MonitorVirtual.Core.Logging;

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

        var dev = _driver.GetStatus();
        if (!dev.Present)
        {
            if (!cfg.Enabled) return ProvisionStatus.NotInstalled("Driver não instalado (monitor desligado).");
            if (!EnsureDriverInstalled(out _)) return ProvisionStatus.NotInstalled("Falha ao instalar o driver.");
            dev = _driver.GetStatus();
            WaitForVirtualAdapter(TimeSpan.FromSeconds(10));
        }

        // O XML só é lido quando o dispositivo inicia; se mudou, reinicia o dispositivo.
        var settingsChanged = VddSettings.Write(1, cfg.Width, cfg.Height, cfg.RefreshRate);

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
        if (cfg.ForceExtend && !_display.IsExtended())
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
        var primary = _display.FindPrimary();
        var primaryGeo = primary is null ? null : _display.GetGeometry(primary.DeviceName);
        var (x, y) = ComputePosition(cfg, primaryGeo);

        var virtName = (_display.FindVirtual() ?? virtualAdapter).DeviceName;
        _display.ApplyMode(virtName, cfg.Width, cfg.Height, cfg.RefreshRate, x, y);

        return GetStatus();
    }

    private static (int X, int Y) ComputePosition(AppConfig cfg, DisplayGeometry? primary)
    {
        if (primary is null) return (0, 0);

        return cfg.Side == MonitorSide.Direita
            ? (primary.X + primary.Width, primary.Y)
            : (primary.X - cfg.Width, primary.Y);
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
