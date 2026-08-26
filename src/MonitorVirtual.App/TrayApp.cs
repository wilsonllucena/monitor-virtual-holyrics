using Microsoft.Win32;
using MonitorVirtual.Core;
using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Provisioning;
using MonitorVirtual.Core.Startup;

namespace MonitorVirtual.App;

/// <summary>Ícone na bandeja + watchdog. É o processo residente do produto.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Control _marshal = new();
    private readonly System.Windows.Forms.Timer _watchdog = new();
    private readonly MonitorProvisioner _provisioner = new();

    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _restartItem;

    private AppConfig _config;
    private ProvisionStatus? _last;
    private PreviewForm? _preview;
    private bool _busy;

    private readonly HashSet<string> _launched = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _needsRestart = new(StringComparer.OrdinalIgnoreCase);

    public TrayApp(bool background, bool openPreview = false)
    {
        _marshal.CreateControl();
        _config = AppConfig.Load();

        _statusItem = new ToolStripMenuItem("Verificando...") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Monitor virtual ligado", null, (_, _) => Toggle())
        {
            CheckOnClick = false,
        };

        _restartItem = new ToolStripMenuItem("Reiniciar programa (para ver o monitor)")
        {
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripMenuItem("Ver o monitor em uma janela", null, (_, _) => ShowPreview()));
        menu.Items.Add(new ToolStripMenuItem("Testar tela...", null, (_, _) => ShowTestScreen()));
        menu.Items.Add(_restartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Configurações...", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripMenuItem("Reparar / reinstalar driver", null, (_, _) => Repair()));
        menu.Items.Add(new ToolStripMenuItem("Abrir pasta de logs", null, (_, _) => OpenLogs()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon
        {
            Icon = IconFactory.Create(false),
            Text = "Monitor Virtual",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _watchdog.Interval = Math.Max(2, _config.WatchdogSeconds) * 1000;
        _watchdog.Tick += (_, _) => RunReconcile();
        if (_config.WatchdogSeconds > 0) _watchdog.Start();

        SystemEvents.DisplaySettingsChanged += OnSystemChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RunReconcile(firstRun: true, silent: background);

        if (openPreview) _marshal.BeginInvoke(ShowPreview);
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        Log.Info("Configuração de vídeo mudou; reconciliando.");
        RunReconcile();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Log.Info("Retorno de suspensão; reconciliando.");
            RunReconcile();
        }
    }

    private void RunReconcile(bool firstRun = false, bool silent = false)
    {
        if (_busy) return;
        _busy = true;

        var cfg = _config.Clone();
        Task.Run(() =>
        {
            ProvisionStatus status;
            var wasActive = false;
            try
            {
                wasActive = _provisioner.GetStatus().MonitorActive;

                if (firstRun && !_provisioner.Driver.GetStatus().Present && cfg.Enabled)
                {
                    if (!_provisioner.EnsureDriverInstalled(out var reboot) && !silent)
                        Notify("Não foi possível instalar o driver do monitor virtual. Veja os logs.",
                            ToolTipIcon.Error);
                    else if (reboot && !silent)
                        Notify("Driver instalado. Reinicie o Windows se o monitor não aparecer.", ToolTipIcon.Info);
                }

                status = _provisioner.Reconcile(cfg);
            }
            catch (Exception ex)
            {
                Log.Error("Erro no ciclo de reconciliação", ex);
                status = ProvisionStatus.NotInstalled($"Erro: {ex.Message}");
            }

            _marshal.BeginInvoke(() =>
            {
                _busy = false;
                ApplyStatus(status, firstRun, silent, wasActive);
            });
        });
    }

    private void ApplyStatus(ProvisionStatus status, bool firstRun, bool silent, bool wasActive = true)
    {
        _last = status;
        _tray.Icon = IconFactory.Create(status.MonitorActive);
        _tray.Text = Truncate($"Monitor Virtual — {status.Summary}", 63);
        _statusItem.Text = status.Summary;
        _toggleItem.Text = _config.Enabled ? "Desligar monitor virtual" : "Ligar monitor virtual";

        if (firstRun && !silent && !status.DriverInstalled)
            Notify("Driver do monitor virtual não está instalado. Use \"Reparar / reinstalar driver\".",
                ToolTipIcon.Warning);

        MaybeLaunchApps(status);
        CheckAppOrdering(status, wasActive, silent);
    }

    /// <summary>
    /// Holyrics, Resolume Arena e OBS montam a lista de telas quando abrem: se o monitor
    /// virtual nasce depois, ele não aparece na configuração de saída. Detecta esse caso
    /// e oferece o reinício de cada programa afetado.
    /// </summary>
    private void CheckAppOrdering(ProvisionStatus status, bool wasActive, bool silent)
    {
        if (!status.MonitorActive)
        {
            _needsRestart.Clear();
            RefreshRestartMenu();
            return;
        }

        var appeared = !wasActive && status.MonitorActive;
        var newlyFlagged = new List<ManagedApp>();

        foreach (var app in _config.ManagedApps)
        {
            var running = AppLauncher.IsRunning(app);

            if (!running)
            {
                _needsRestart.Remove(app.Name);
                continue;
            }

            if (appeared && _needsRestart.Add(app.Name))
                newlyFlagged.Add(app);
        }

        RefreshRestartMenu();

        foreach (var app in newlyFlagged.Where(a => a.AutoRestartIfEarly))
            RestartApp(app, confirm: false);

        var manual = newlyFlagged.Where(a => !a.AutoRestartIfEarly).Select(a => a.Name).ToArray();
        if (manual.Length > 0 && !silent)
            Notify($"{string.Join(" e ", manual)} já estava(m) aberto(s) quando o monitor virtual " +
                   "apareceu — reinicie para a nova tela ser listada.", ToolTipIcon.Warning);
    }

    private void RefreshRestartMenu()
    {
        _restartItem.DropDownItems.Clear();

        foreach (var app in _config.ManagedApps.Where(a => _needsRestart.Contains(a.Name)))
        {
            var target = app;
            _restartItem.DropDownItems.Add(
                new ToolStripMenuItem(target.Name, null, (_, _) => RestartApp(target)));
        }

        _restartItem.Visible = _restartItem.DropDownItems.Count > 0;
    }

    private void RestartApp(ManagedApp app, bool confirm = true)
    {
        var path = string.IsNullOrWhiteSpace(app.ExePath)
            ? AppLauncher.FindExecutable(app.EffectiveProcessName + ".exe")
            : app.ExePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(
                $"Não encontrei o executável de {app.Name}. Informe o caminho em Configurações.",
                "Monitor Virtual", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (confirm)
        {
            var answer = MessageBox.Show(
                $"{app.Name} será fechado e aberto de novo para reconhecer o monitor virtual.\n\n" +
                "Não faça isso durante o culto — a projeção some por alguns segundos.\n\nContinuar?",
                $"Reiniciar {app.Name}", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;
        }

        var target = app.Clone();
        target.ExePath = path!;

        Task.Run(() =>
        {
            var ok = AppLauncher.Restart(target);
            _marshal.BeginInvoke(() =>
            {
                _needsRestart.Remove(app.Name);
                RefreshRestartMenu();
                Notify(ok
                        ? $"{app.Name} reiniciado. A tela virtual já deve aparecer na lista de saídas."
                        : $"Não consegui reiniciar o {app.Name}. Veja os logs.",
                    ok ? ToolTipIcon.Info : ToolTipIcon.Error);
            });
        });
    }

    private void ShowPreview()
    {
        if (_preview is { IsDisposed: false })
        {
            _preview.WindowState = FormWindowState.Normal;
            _preview.Activate();
            return;
        }

        _preview = new PreviewForm(GetVirtualBounds, _config.PreviewFps);
        _preview.FormClosed += (_, _) => _preview = null;
        _preview.Show();
    }

    private Rectangle? GetVirtualBounds()
    {
        var name = (_last ?? _provisioner.GetStatus()).AdapterDeviceName;
        if (name is null) return null;

        var screen = Screen.AllScreens.FirstOrDefault(s =>
            string.Equals(s.DeviceName, name, StringComparison.OrdinalIgnoreCase));

        return screen?.Bounds;
    }

    /// <summary>Start ordenado: só abre os programas depois que o monitor está ativo.</summary>
    private void MaybeLaunchApps(ProvisionStatus status)
    {
        if (!status.MonitorActive) return;

        var started = new List<string>();
        foreach (var app in _config.ManagedApps.Where(a => a.LaunchAfterMonitor))
        {
            if (!_launched.Add(app.Name)) continue; // uma tentativa por sessão
            if (AppLauncher.IsRunning(app)) continue;
            if (AppLauncher.Launch(app)) started.Add(app.Name);
        }

        if (started.Count > 0)
            Notify($"Monitor virtual pronto — {string.Join(" e ", started)} iniciado(s).", ToolTipIcon.Info);
    }

    private void Toggle()
    {
        _config.Enabled = !_config.Enabled;
        _config.Save();
        RunReconcile();
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_config.Clone(), _provisioner);
        if (form.ShowDialog() != DialogResult.OK) return;

        _config = form.Result;
        _config.Save();

        _watchdog.Stop();
        if (_config.WatchdogSeconds > 0)
        {
            _watchdog.Interval = Math.Max(2, _config.WatchdogSeconds) * 1000;
            _watchdog.Start();
        }

        if (_config.StartWithWindows) StartupTask.Enable(Environment.ProcessPath ?? "");
        else StartupTask.Disable();

        _launched.Clear();
        RunReconcile();
    }

    private void ShowTestScreen()
    {
        var status = _last ?? _provisioner.GetStatus();
        if (!status.MonitorActive || status.AdapterDeviceName is null)
        {
            MessageBox.Show("O monitor virtual não está ativo.", "Monitor Virtual",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var screen = Screen.AllScreens.FirstOrDefault(s =>
            string.Equals(s.DeviceName, status.AdapterDeviceName, StringComparison.OrdinalIgnoreCase));

        if (screen is null)
        {
            MessageBox.Show("Não foi possível localizar a área do monitor virtual.", "Monitor Virtual",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        new TestScreenForm(screen).Show();
    }

    private void Repair()
    {
        if (!Elevation.IsElevated())
        {
            MessageBox.Show("Execute o aplicativo como Administrador para reparar o driver.",
                "Monitor Virtual", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Task.Run(() =>
        {
            var ok = _provisioner.EnsureDriverInstalled(out var reboot);
            _provisioner.Driver.Restart();
            var status = _provisioner.Reconcile(_config.Clone());

            _marshal.BeginInvoke(() =>
            {
                ApplyStatus(status, firstRun: false, silent: false);
                Notify(ok
                        ? reboot ? "Driver reinstalado — reinicie o Windows se necessário." : "Driver reinstalado."
                        : "Falha ao reinstalar o driver. Veja os logs.",
                    ok ? ToolTipIcon.Info : ToolTipIcon.Error);
            });
        });
    }

    private static void OpenLogs()
    {
        AppPaths.EnsureDataDirs();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = AppPaths.LogDir,
            UseShellExecute = true,
        });
    }

    private void Notify(string message, ToolTipIcon icon)
    {
        _tray.BalloonTipTitle = "Monitor Virtual";
        _tray.BalloonTipText = message;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(5000);
    }

    private void ExitApp()
    {
        var keep = MessageBox.Show(
            "Manter o monitor virtual ligado depois de fechar?\n\n" +
            "Sim: o monitor continua disponível para o Holyrics.\n" +
            "Não: o monitor virtual é desligado agora.",
            "Monitor Virtual", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        if (keep == DialogResult.Cancel) return;

        if (keep == DialogResult.No)
        {
            _config.Enabled = false;
            _config.Save();
            _provisioner.Driver.SetEnabled(false);
        }

        _watchdog.Stop();
        SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
