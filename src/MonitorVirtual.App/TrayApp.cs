using Microsoft.Win32;
using MonitorVirtual.Core;
using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Holyrics;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Provisioning;
using MonitorVirtual.Core.Startup;
using MonitorVirtual.Core.Surround;
using MonitorVirtual.App.Surround;

namespace MonitorVirtual.App;

/// <summary>
/// Ícone na bandeja, janela na barra de tarefas e watchdog. Processo residente.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Control _marshal = new();
    private readonly System.Windows.Forms.Timer _watchdog = new();
    private readonly MonitorProvisioner _provisioner = new();

    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly ToolStripMenuItem _surroundItem;
    private readonly ToolStripMenuItem _blendItem;

    private AppConfig _config;
    private ProvisionStatus? _last;
    private PreviewForm? _preview;
    private SurroundOutputHost? _surround;
    private bool _busy;
    private bool _surroundHintShown;
    private bool _holyricsSteerApplied;
    private bool _holyricsSteerInFlight;
    private int _holyricsSteerFailures;
    private bool _holyricsSteerHintShown;
    private BlendAdjustForm? _blendAdjust;
    private TestScreenForm? _juntaTest;
    private PainelForm? _painel;
    private ContextMenuStrip? _menu;
    private DateTime _menuHoldUntil;

    private readonly HashSet<string> _launched = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _needsRestart = new(StringComparer.OrdinalIgnoreCase);
    private bool _ndiBackgroundApplied;
    private bool _ndiBackgroundInFlight;
    private int _ndiBackgroundFailures;

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

        _surroundItem = new ToolStripMenuItem("Ligar telão surround (2 projetores = 1 tela)", null,
            (_, _) => ToggleSurround());
        _blendItem = new ToolStripMenuItem("Ajustar blend do telão...", null, (_, _) => ShowBlendAdjust())
        {
            Enabled = false,
        };

        var menu = new ContextMenuStrip();
        _menu = menu;
        menu.Opening += OnTrayMenuOpening;
        menu.Closing += OnTrayMenuClosing;
        menu.Closed += (_, _) => _surround?.HoldZOrder(false);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_surroundItem);
        menu.Items.Add(_blendItem);
        menu.Items.Add(new ToolStripMenuItem("Abrir janela do Monitor Virtual", null, (_, _) => ShowPainel()));
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
        _tray.MouseUp += OnTrayMouseUp;
        _tray.MouseClick += OnTrayMouseClick;
        _tray.DoubleClick += OnTrayDoubleClick;

        _watchdog.Interval = Math.Max(2, _config.WatchdogSeconds) * 1000;
        _watchdog.Tick += (_, _) => RunReconcile();
        if (_config.WatchdogSeconds > 0) _watchdog.Start();

        SystemEvents.DisplaySettingsChanged += OnSystemChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RunReconcile(firstRun: true, silent: background);

        EnsurePainel();
        if (background) _painel!.MostrarMinimizado();
        else ShowPainel();

        if (openPreview) _marshal.BeginInvoke(ShowPreview);
    }

    private void OnTrayMenuOpening(object? sender, EventArgs e)
    {
        _surround?.HoldZOrder(true);
        _menuHoldUntil = DateTime.UtcNow.AddMilliseconds(1500);
        ForegroundForMenu();
    }

    private void OnTrayMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        // Overflow da bandeja e overlays TOPMOST disparam AppFocusChange/AppClicked
        // no instante em que o menu abre. Depois da janela de graça, o usuário
        // clica fora e o menu fecha (ItemClicked / Keyboard sempre fecham).
        if (DateTime.UtcNow >= _menuHoldUntil) return;
        if (e.CloseReason is ToolStripDropDownCloseReason.AppFocusChange
            or ToolStripDropDownCloseReason.AppClicked)
            e.Cancel = true;
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        ForegroundForMenu();

        if (_menu is { Visible: true }) return;

        // Ícone no overflow ("mostrar ícones ocultos"): o flyout fecha e leva o
        // menu junto. Reabre depois que o overflow sumiu.
        _marshal.BeginInvoke(new Action(() =>
        {
            Task.Delay(90).ContinueWith(_ =>
                _marshal.BeginInvoke(ReabrirMenuDaBandeja));
        }));
    }

    private void ReabrirMenuDaBandeja()
    {
        if (_menu is null || _menu.Visible) return;
        _surround?.HoldZOrder(true);
        _menuHoldUntil = DateTime.UtcNow.AddMilliseconds(1500);
        ForegroundForMenu();
        _menu.Show(Cursor.Position);
        ForegroundForMenu();
    }

    private IntPtr MenuOwnerHandle =>
        _painel is { IsHandleCreated: true } ? _painel.Handle
        : _marshal.IsHandleCreated ? _marshal.Handle
        : IntPtr.Zero;

    private void ForegroundForMenu()
    {
        var hwnd = MenuOwnerHandle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetForegroundWindow(hwnd);
        NativeMethods.PostMessage(hwnd, NativeMethods.WmNull, IntPtr.Zero, IntPtr.Zero);
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowPainel();
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e)
    {
        if (_config.SurroundEnabled &&
            (_surround is { IsRunning: true } ||
             _last?.Surround is { Kind: SurroundSurfaceKind.NvidiaLogical }))
            ShowBlendAdjust();
        else
            ShowPainel();
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
        _tray.Icon = IconFactory.Create(
            status.MonitorActive || status.Surround is { Kind: SurroundSurfaceKind.NvidiaLogical });
        _tray.Text = Truncate($"Monitor Virtual — {status.Summary}", 63);
        _statusItem.Text = status.Summary;
        _toggleItem.Text = _config.Enabled ? "Desligar monitor virtual" : "Ligar monitor virtual";

        SyncSurround(status, firstRun, silent);
        _painel?.Atualizar(_statusItem.Text, _blendItem.Enabled);

        if (firstRun && !silent && !status.DriverInstalled)
            Notify("Driver do monitor virtual não está instalado. Use \"Reparar / reinstalar driver\".",
                ToolTipIcon.Warning);

        MaybeLaunchApps(status);
        CheckAppOrdering(status, wasActive, silent);
        MaybeFixHolyricsNdi(silent);
    }

    private void ToggleSurround()
    {
        _config.SurroundEnabled = !_config.SurroundEnabled;
        _config.Save();
        _holyricsSteerApplied = false;
        _holyricsSteerFailures = 0;
        RunReconcile();
    }

    /// <summary>
    /// Depois do monitor virtual pronto, cobre os projetores com as fatias do canvas
    /// (antes de abrir o Holyrics). Clone/espelho fica para trás: cada lado mostra
    /// metade do slide, com blend na junta.
    /// Com NVIDIA Surround o Windows já vê um monitor só — sem overlay.
    /// </summary>
    private void SyncSurround(ProvisionStatus status, bool firstRun, bool silent)
    {
        _surroundItem.Text = _config.SurroundEnabled
            ? "Desligar telão surround"
            : "Ligar telão surround (2 projetores = 1 tela)";
        _surroundItem.Checked = _config.SurroundEnabled;
        _blendItem.Enabled = _config.SurroundEnabled;

        var physical = _provisioner.Display.ListPhysical();
        var nvidia = status.Surround is { Kind: SurroundSurfaceKind.NvidiaLogical };

        if (!_config.SurroundEnabled)
        {
            _surround?.Stop();
            _holyricsSteerApplied = false;

            if (firstRun && !silent && physical.Count >= 2 && !_surroundHintShown)
            {
                _surroundHintShown = true;
                Notify("Dois monitores detectados. Se o telão está repetindo o mesmo slide, " +
                       "ligue Telão surround no menu.", ToolTipIcon.Info);
            }

            return;
        }

        if (nvidia)
        {
            _surround?.Stop();
            _blendItem.Enabled = true;
            _statusItem.Text = status.Summary;
            MaybeSteerHolyrics(silent);

            if (firstRun && !silent)
            {
                Notify($"{status.Summary}. A barra de tarefas atravessa o telão como um monitor só. " +
                       "Abra a janela do Monitor Virtual (clique no ícone da bandeja) e use «Ajustar blend do telão».",
                    ToolTipIcon.Info);
                ShowPainel();
            }
            return;
        }

        if (!status.MonitorActive && status.Surround is null)
        {
            _surround?.Stop();
            _holyricsSteerApplied = false;

            var planProbe = SurroundPlanner.TryCreate(physical, _config);
            if (firstRun && !silent && planProbe is null)
                Notify("Surround ligado, mas precisa de 2 projetores físicos. Nada foi alterado.",
                    ToolTipIcon.Warning);
            return;
        }

        var plan = SurroundPlanner.TryCreate(physical, _config);
        if (plan is null)
        {
            _surround?.Stop();
            _holyricsSteerApplied = false;
            _blendItem.Enabled = false;
            if (firstRun && !silent)
                Notify("Surround ligado, mas precisa de 2 projetores físicos. Nada foi alterado.",
                    ToolTipIcon.Warning);
            return;
        }

        _surround ??= new SurroundOutputHost(GetCanvasBounds)
        {
            RaiseOperatorUi = RaiseOperatorWindows,
        };
        _surround.Start(plan, _config.SurroundOutputFps, _config.SurroundBlendGamma, _config.SurroundBlendGain);
        _statusItem.Text = status.Surround?.Summary ?? $"{status.Summary} · {plan.Summary}";
        _blendItem.Enabled = true;

        MaybeSteerHolyrics(silent);

        if (firstRun && !silent)
        {
            Notify(
                (status.Surround?.Summary ?? plan.Summary) +
                ". Canvas virtual é o primário: a taskbar atravessa o telão. " +
                "Abra a janela do Monitor Virtual (clique no ícone da bandeja) e use «Ajustar blend do telão».",
                ToolTipIcon.Info);
            ShowPainel();
        }
    }

    /// <summary>
    /// Holyrics lista todos os monitores na abertura e costuma projetar nos dois
    /// projetores (tela dividida). Apontamos a Tela pública para o canvas virtual
    /// e ocultamos screen_2/3 que caem nas saídas físicas do telão.
    /// </summary>
    private void MaybeSteerHolyrics(bool silent)
    {
        if (!_config.SurroundEnabled || !_config.SurroundSteerHolyrics) return;
        if (_holyricsSteerApplied || _holyricsSteerInFlight) return;
        if (_surround is not { IsRunning: true } &&
            _last?.Surround is not { Kind: SurroundSurfaceKind.NvidiaLogical })
            return;

        if (string.IsNullOrWhiteSpace(_config.HolyricsApiToken))
        {
            if (!silent && !_holyricsSteerHintShown && HolyricsClient.IsRunning())
            {
                _holyricsSteerHintShown = true;
                Notify("Holyrics aberto com surround: cole o token da API em Configurações " +
                       "para ele projetar só no monitor virtual (senão a tela divide nos dois projetores).",
                    ToolTipIcon.Warning);
            }
            return;
        }

        if (!HolyricsClient.IsRunning()) return;

        var canvas = GetCanvasBounds();
        if (canvas is null) return;

        var projectors = _last?.Surround is { Kind: SurroundSurfaceKind.NvidiaLogical }
            ? Array.Empty<SurroundMonitor>()
            : SurroundPlanner.SelectMonitors(_provisioner.Display.ListPhysical(), _config).ToList();
        _holyricsSteerInFlight = true;
        var cfg = _config.Clone();
        var rect = canvas.Value;
        var nvidiaSpan = _last?.Surround is { Kind: SurroundSurfaceKind.NvidiaLogical };

        Task.Run(async () =>
        {
            HolyricsDisplayFixResult result;
            try
            {
                result = await new HolyricsClient().EnsureSinglePublicScreenAsync(
                    cfg, rect.X, rect.Y, rect.Width, rect.Height, projectors);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao apontar a Tela pública do Holyrics", ex);
                result = new HolyricsDisplayFixResult(false, 0, ex.Message, null);
            }

            _marshal.BeginInvoke(() =>
            {
                _holyricsSteerInFlight = false;
                if (!result.Ok)
                {
                    _holyricsSteerFailures++;
                    Log.Warn($"Holyrics não apontado para o canvas único: {result.Error}");
                    if (_holyricsSteerFailures >= 5)
                        _holyricsSteerApplied = true;
                    return;
                }

                _holyricsSteerApplied = true;
                _holyricsSteerFailures = 0;
                if (result.Changed && !silent)
                    Notify(
                        nvidiaSpan
                            ? "Holyrics: Tela pública no telão único (um monitor, taskbar contínua)."
                            : "Holyrics: Tela pública no monitor virtual (canvas único). " +
                              (result.HiddenScreens > 0
                                  ? $"{result.HiddenScreens} tela(s) extra(s) nos projetores foram ocultadas."
                                  : "As duas saídas físicas não recebem mais o slide direto."),
                        ToolTipIcon.Info);
            });
        });
    }

    /// <summary>
    /// O NDI nativo do Holyrics (v2.29+) manda só a camada de texto com alpha.
    /// O Resolume pinta o resto de preto. Pedimos à API para incluir o fundo.
    /// </summary>
    private void MaybeFixHolyricsNdi(bool silent)
    {
        if (!_config.HolyricsIncludeNdiBackground || _ndiBackgroundApplied || _ndiBackgroundInFlight)
            return;
        if (string.IsNullOrWhiteSpace(_config.HolyricsApiToken))
            return;
        if (!HolyricsClient.IsRunning())
            return;

        _ndiBackgroundInFlight = true;
        var cfg = _config.Clone();

        Task.Run(async () =>
        {
            HolyricsNdiFixResult result;
            try
            {
                result = await new HolyricsClient().EnsureOpaqueNdiBackgroundAsync(cfg);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ajustar o NDI do Holyrics", ex);
                result = new HolyricsNdiFixResult(0, 0, ex.Message, Array.Empty<HolyricsNdiOutput>());
            }

            _marshal.BeginInvoke(() =>
            {
                _ndiBackgroundInFlight = false;
                if (!result.Ok)
                {
                    _ndiBackgroundFailures++;
                    Log.Warn($"NDI do Holyrics não ajustado: {result.Error}");
                    if (_ndiBackgroundFailures >= 3)
                        _ndiBackgroundApplied = true;
                    return;
                }

                _ndiBackgroundApplied = true;
                if (result.Changed > 0 && !silent)
                    Notify(
                        result.Changed == 1
                            ? "NDI do Holyrics: papel de fundo ligado (Resolume não fica mais só com a letra)."
                            : $"NDI do Holyrics: papel de fundo ligado em {result.Changed} saídas.",
                        ToolTipIcon.Info);
            });
        });
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

    private void EnsurePainel()
    {
        if (_painel is { IsDisposed: false }) return;

        _painel = new PainelForm(ShowBlendAdjust, ShowSettings, ShowPreview, ShowTestScreen);
        _painel.Atualizar(_statusItem.Text, _blendItem.Enabled);
        UiPlacement.Place(_painel, _surround?.ProjectorDeviceNames);
        _ = _painel.Handle;
    }

    private void ShowPainel()
    {
        EnsurePainel();
        var hidden = !_painel!.Visible || _painel.WindowState == FormWindowState.Minimized;
        if (hidden)
            UiPlacement.Place(_painel, _surround?.ProjectorDeviceNames);
        _painel.Atualizar(_statusItem.Text, _blendItem.Enabled);
        _painel.MostrarNaFrente();
        UiPlacement.RaiseAboveOverlays(_painel);
    }

    private void RaiseOperatorWindows()
    {
        UiPlacement.RaiseAboveOverlays(_blendAdjust);
        if (_painel is { Visible: true, WindowState: not FormWindowState.Minimized }
            && FormEstaNoProjetor(_painel))
            UiPlacement.RaiseAboveOverlays(_painel);
    }

    private bool FormEstaNoProjetor(Form form)
    {
        var names = _surround?.ProjectorDeviceNames;
        if (names is null || names.Count == 0) return false;
        var screen = Screen.FromControl(form);
        return names.Contains(screen.DeviceName, StringComparer.OrdinalIgnoreCase);
    }

    private void ShowPreview()
    {
        if (_preview is { IsDisposed: false })
        {
            _preview.WindowState = FormWindowState.Normal;
            _preview.Activate();
            return;
        }

        _preview = new PreviewForm(GetCanvasBounds, _config.PreviewFps,
            ShowSettings, ShowBlendAdjust, () => _blendItem.Enabled);
        _preview.FormClosed += (_, _) => _preview = null;
        _preview.Icon = IconFactory.AppIcon;
        UiPlacement.Place(_preview, _surround?.ProjectorDeviceNames, cornerIfCovered: false);
        _preview.Show();
    }

    private Rectangle? GetCanvasBounds()
    {
        var status = _last ?? _provisioner.GetStatus();
        if (status.Surround is { } surface)
        {
            if (!string.IsNullOrWhiteSpace(surface.AdapterDeviceName))
            {
                var named = Screen.AllScreens.FirstOrDefault(s =>
                    string.Equals(s.DeviceName, surface.AdapterDeviceName, StringComparison.OrdinalIgnoreCase));
                if (named is not null) return named.Bounds;
            }

            var bySize = Screen.AllScreens
                .OrderByDescending(s => s.Bounds.Width)
                .FirstOrDefault(s => s.Bounds.Width >= surface.Width - 64);
            if (bySize is not null) return bySize.Bounds;

            return new Rectangle(surface.X, surface.Y, surface.Width, surface.Height);
        }

        var name = status.AdapterDeviceName;
        if (name is null) return null;

        var screen = Screen.AllScreens.FirstOrDefault(s =>
            string.Equals(s.DeviceName, name, StringComparison.OrdinalIgnoreCase));

        return screen?.Bounds;
    }

    /// <summary>Start ordenado: só abre os programas depois que o monitor (e o surround) estão ativos.</summary>
    private void MaybeLaunchApps(ProvisionStatus status)
    {
        if (!status.MonitorActive && status.Surround is not { Kind: SurroundSurfaceKind.NvidiaLogical })
            return;

        if (_config.SurroundEnabled)
        {
            var physical = _provisioner.Display.ListPhysical();
            var plan = SurroundPlanner.TryCreate(physical, _config);
            var surface = status.Surround;
            if (plan is not null &&
                surface is null &&
                _surround is not { IsRunning: true })
            {
                Log.Info("Aguardando o telão surround ficar estável antes de abrir os programas.");
                return;
            }
        }

        var started = new List<string>();
        foreach (var app in _config.ManagedApps.Where(a => a.LaunchAfterMonitor))
        {
            if (!_launched.Add(app.Name)) continue; // uma tentativa por sessão
            if (AppLauncher.IsRunning(app)) continue;
            if (AppLauncher.Launch(app)) started.Add(app.Name);
        }

        if (started.Count > 0)
        {
            Notify($"Monitor virtual pronto — {string.Join(" e ", started)} iniciado(s).", ToolTipIcon.Info);
            if (_config.SurroundEnabled)
            {
                _holyricsSteerApplied = false;
                _marshal.BeginInvoke(() =>
                {
                    Task.Delay(4000).ContinueWith(_ =>
                        _marshal.BeginInvoke(() => MaybeSteerHolyrics(silent: false)));
                });
            }
        }
    }

    private void Toggle()
    {
        _config.Enabled = !_config.Enabled;
        _config.Save();
        RunReconcile();
    }

    private void ShowSettings()
    {
        var snapshot = _config.Clone();
        using var form = new SettingsForm(_config.Clone(), _provisioner, ApplyBlendLive);
        form.Icon = IconFactory.AppIcon;
        UiPlacement.Place(form, _surround?.ProjectorDeviceNames, cornerIfCovered: false);
        if (form.ShowDialog() != DialogResult.OK)
        {
            ApplyBlendLive(snapshot.SurroundBlendOverlap, snapshot.SurroundBlendGamma, snapshot.SurroundBlendGain);
            return;
        }

        _config = form.Result;
        _config.Save();
        _ndiBackgroundApplied = false;
        _ndiBackgroundFailures = 0;
        _holyricsSteerApplied = false;
        _holyricsSteerFailures = 0;

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

    private void ShowBlendAdjust()
    {
        if (_blendAdjust is { IsDisposed: false })
        {
            _blendAdjust.WindowState = FormWindowState.Normal;
            UiPlacement.Place(_blendAdjust, _surround?.ProjectorDeviceNames);
            _blendAdjust.Activate();
            UiPlacement.RaiseAboveOverlays(_blendAdjust);
            return;
        }

        if (_surround is not { IsRunning: true } &&
            _last?.Surround is not { Kind: SurroundSurfaceKind.NvidiaLogical })
        {
            MessageBox.Show(
                "Ligue o telão surround com 2 projetores para ajustar o blend na parede.",
                "Monitor Virtual", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _blendAdjust = new BlendAdjustForm(
            _config,
            ApplyBlendLive,
            showTest => ToggleJuntaTest(showTest),
            () =>
            {
                _holyricsSteerApplied = false;
                _config.Save();
                RunReconcile();
            });
        _blendAdjust.FormClosed += (_, _) => _blendAdjust = null;
        _blendAdjust.Icon = IconFactory.AppIcon;
        UiPlacement.Place(_blendAdjust, _surround?.ProjectorDeviceNames);
        _blendAdjust.Show();
        _blendAdjust.Activate();
        UiPlacement.RaiseAboveOverlays(_blendAdjust);
    }

    /// <summary>Gama/ganho/largura do fade no próximo quadro, nas fatias dos projetores.</summary>
    private void ApplyBlendLive(int overlap, double gamma, double gain)
    {
        _config.SurroundBlendOverlap = overlap;
        _config.SurroundBlendGamma = gamma;
        _config.SurroundBlendGain = gain;
        _surround?.ApplyBlend(gamma, gain, overlap);

        if (_last?.Surround is { Kind: SurroundSurfaceKind.NvidiaLogical })
        {
            var physical = SurroundPlanner.SelectMonitors(_provisioner.Display.ListPhysical(), _config);
            var mapped = NvidiaSpan.MapDisplays(physical.Count >= 2 ? physical : _provisioner.Display.ListPhysical());
            NvidiaSpan.TryApplyScanoutBlend(mapped, overlap, gamma, gain);
        }
    }

    private void ToggleJuntaTest(bool show)
    {
        if (!show)
        {
            if (_juntaTest is { IsDisposed: false })
            {
                _juntaTest.Close();
                _juntaTest = null;
            }
            return;
        }

        ShowTestScreen(juntaWhite: true, stayOpen: true);
    }

    private void ShowTestScreen() => ShowTestScreen(juntaWhite: false, stayOpen: false);

    private void ShowTestScreen(bool juntaWhite, bool stayOpen)
    {
        var status = _last ?? _provisioner.GetStatus();
        if (!status.MonitorActive && status.Surround is null)
        {
            MessageBox.Show("O telão / monitor virtual não está ativo.", "Monitor Virtual",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var canvas = GetCanvasBounds();
        Screen? screen = null;
        if (canvas is not null)
        {
            screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds == canvas.Value)
                     ?? Screen.AllScreens.FirstOrDefault(s =>
                         Math.Abs(s.Bounds.Width - canvas.Value.Width) < 64 &&
                         Math.Abs(s.Bounds.Height - canvas.Value.Height) < 64)
                     ?? Screen.FromPoint(new Point(canvas.Value.X + 8, canvas.Value.Y + 8));
        }

        if (screen is null && status.AdapterDeviceName is not null)
        {
            screen = Screen.AllScreens.FirstOrDefault(s =>
                string.Equals(s.DeviceName, status.AdapterDeviceName, StringComparison.OrdinalIgnoreCase));
        }

        if (screen is null)
        {
            MessageBox.Show("Não foi possível localizar a área do telão.", "Monitor Virtual",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_juntaTest is { IsDisposed: false })
        {
            _juntaTest.Close();
            _juntaTest = null;
        }

        var form = new TestScreenForm(
            screen,
            _config.SurroundEnabled ? _config.SurroundBlendOverlap : 0,
            stayOpen,
            juntaWhite);
        if (stayOpen)
        {
            _juntaTest = form;
            form.FormClosed += (_, _) => { if (ReferenceEquals(_juntaTest, form)) _juntaTest = null; };
        }

        form.Show();
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
        _blendAdjust?.Close();
        _juntaTest?.Close();
        _painel?.PermitirFechar();
        _painel?.Close();
        _painel = null;
        _surround?.Dispose();
        _surround = null;
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
