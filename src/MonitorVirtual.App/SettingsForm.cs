using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Holyrics;
using MonitorVirtual.Core.Provisioning;

namespace MonitorVirtual.App;

internal sealed class SettingsForm : Form
{
    private static readonly (string Label, int W, int H)[] Presets =
    {
        ("1280 x 720 (HD)", 1280, 720),
        ("1366 x 768", 1366, 768),
        ("1600 x 900", 1600, 900),
        ("1920 x 1080 (Full HD)", 1920, 1080),
        ("2560 x 1440 (QHD)", 2560, 1440),
        ("3840 x 2160 (4K)", 3840, 2160),
    };

    private readonly MonitorProvisioner _provisioner;
    private readonly List<ManagedApp> _apps;

    private readonly CheckBox _enabled = new() { Text = "Monitor virtual ligado", AutoSize = true };
    private readonly ComboBox _resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _refresh = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox _side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckBox _forceExtend = new()
        { Text = "Forçar modo Estender (Win+P) automaticamente", AutoSize = true };
    private readonly CheckBox _neverPrimary = new()
        { Text = "Nunca deixar o monitor virtual como principal", AutoSize = true };
    private readonly NumericUpDown _watchdog = new() { Minimum = 0, Maximum = 120, Width = 70 };

    private readonly ListView _appList = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Width = 350,
        Height = 118,
    };

    private readonly NumericUpDown _apiPort = new() { Minimum = 1, Maximum = 65535, Width = 80 };
    private readonly TextBox _apiToken = new() { Width = 200 };
    private readonly Label _apiResult = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly CheckBox _startWithWindows = new()
        { Text = "Iniciar com o Windows (elevado, sem UAC)", AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(460, 0) };

    private int _customWidth = 1920;
    private int _customHeight = 1080;

    public AppConfig Result { get; }

    public SettingsForm(AppConfig config, MonitorProvisioner provisioner)
    {
        Result = config;
        _provisioner = provisioner;
        _apps = config.ManagedApps.Select(a => a.Clone()).ToList();

        Text = "Monitor Virtual para Holyrics";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(510, 706);

        BuildLayout();
        LoadFrom(config);
        RefreshAppList();
        RefreshStatus();
    }

    private void BuildLayout()
    {
        var y = 12;

        // --- monitor virtual ---
        var monitorBox = Group("Monitor virtual", ref y, 200);
        Controls.Add(monitorBox);
        var my = 24;
        Add(monitorBox, _enabled, 14, my); my += 28;
        Add(monitorBox, new Label { Text = "Resolução:", AutoSize = true }, 14, my + 4);
        Add(monitorBox, _resolution, 110, my); my += 30;
        Add(monitorBox, new Label { Text = "Taxa (Hz):", AutoSize = true }, 14, my + 4);
        Add(monitorBox, _refresh, 110, my);
        Add(monitorBox, new Label { Text = "Posição:", AutoSize = true }, 230, my + 4);
        Add(monitorBox, _side, 300, my); my += 32;
        Add(monitorBox, _forceExtend, 14, my); my += 24;
        Add(monitorBox, _neverPrimary, 14, my); my += 28;
        Add(monitorBox, new Label { Text = "Verificar a cada (s):", AutoSize = true }, 14, my + 4);
        Add(monitorBox, _watchdog, 140, my);

        // --- programas que usam o monitor ---
        var appsBox = Group("Programas que usam o monitor (abrem depois dele)", ref y, 190);
        Controls.Add(appsBox);

        _appList.Columns.Add("Programa", 110);
        _appList.Columns.Add("Abre depois", 80);
        _appList.Columns.Add("Reinício auto.", 90);
        _appList.Columns.Add("Estado", 60);
        _appList.DoubleClick += (_, _) => EditApp();
        Add(appsBox, _appList, 14, 24);

        var bx = 372;
        var by = 24;
        foreach (var (text, action) in new (string, Action)[]
                 {
                     ("Detectar", DetectApps),
                     ("Adicionar", AddApp),
                     ("Editar", EditApp),
                     ("Remover", RemoveApp),
                 })
        {
            var button = new Button { Text = text, Width = 110, Left = bx, Top = by };
            button.Click += (_, _) => action();
            appsBox.Controls.Add(button);
            by += 30;
        }

        Add(appsBox, new Label
        {
            Text = "Holyrics, Resolume Arena e OBS só listam telas que já existiam quando abriram.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = SystemColors.GrayText,
        }, 14, 150);

        // --- API do Holyrics ---
        var apiBox = Group("Holyrics — API local (apenas status)", ref y, 84);
        Controls.Add(apiBox);
        Add(apiBox, new Label { Text = "Porta:", AutoSize = true }, 14, 28);
        Add(apiBox, _apiPort, 60, 24);
        Add(apiBox, new Label { Text = "Token:", AutoSize = true }, 155, 28);
        Add(apiBox, _apiToken, 205, 24);
        var testApi = new Button { Text = "Testar API", Width = 100, Left = 14, Top = 52 };
        testApi.Click += async (_, _) => await TestApiAsync();
        apiBox.Controls.Add(testApi);
        Add(apiBox, _apiResult, 124, 57);

        // --- sistema ---
        var sysBox = Group("Sistema", ref y, 140);
        Controls.Add(sysBox);
        var sy = 24;
        Add(sysBox, _startWithWindows, 14, sy); sy += 28;
        var install = new Button { Text = "Instalar / reparar driver", Width = 170, Left = 14, Top = sy };
        install.Click += (_, _) => InstallDriver();
        sysBox.Controls.Add(install);
        var restart = new Button { Text = "Reiniciar dispositivo", Width = 150, Left = 194, Top = sy };
        restart.Click += (_, _) => { _provisioner.Driver.Restart(); RefreshStatus(); };
        sysBox.Controls.Add(restart);
        sy += 34;
        Add(sysBox, _statusLabel, 14, sy);

        var ok = new Button
        {
            Text = "Salvar e aplicar", Width = 140, Left = ClientSize.Width - 300, Top = y + 6,
            DialogResult = DialogResult.OK,
        };
        ok.Click += (_, _) => SaveTo(Result);

        var cancel = new Button
        {
            Text = "Cancelar", Width = 120, Left = ClientSize.Width - 148, Top = y + 6,
            DialogResult = DialogResult.Cancel,
        };

        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private GroupBox Group(string title, ref int y, int height)
    {
        var box = new GroupBox
        {
            Text = title,
            Left = 12,
            Top = y,
            Width = ClientSize.Width - 24,
            Height = height,
        };
        y += height + 8;
        return box;
    }

    private static void Add(Control parent, Control child, int left, int top)
    {
        child.Left = left;
        child.Top = top;
        parent.Controls.Add(child);
    }

    // ------------------------------------------------------------------ programas

    private void RefreshAppList()
    {
        _appList.BeginUpdate();
        _appList.Items.Clear();

        foreach (var app in _apps)
        {
            var item = new ListViewItem(app.Name) { Tag = app };
            item.SubItems.Add(app.LaunchAfterMonitor ? "sim" : "não");
            item.SubItems.Add(app.AutoRestartIfEarly ? "sim" : "não");
            item.SubItems.Add(AppLauncher.IsRunning(app) ? "aberto" : "fechado");
            item.ToolTipText = app.ExePath;
            _appList.Items.Add(item);
        }

        _appList.EndUpdate();
    }

    private void DetectApps()
    {
        var found = AppLauncher.Autodetect();
        var added = 0;

        foreach (var app in found)
        {
            if (_apps.Any(a => string.Equals(a.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            _apps.Add(app.Clone());
            added++;
        }

        RefreshAppList();
        MessageBox.Show(
            added > 0
                ? $"{added} programa(s) adicionado(s)."
                : found.Count > 0
                    ? "Os programas encontrados já estavam na lista."
                    : "Nenhum programa conhecido encontrado (Holyrics, Resolume Arena/Avenue, OBS).",
            "Detectar programas", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AddApp()
    {
        using var dlg = new ManagedAppForm(new ManagedApp { LaunchAfterMonitor = true });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _apps.Add(dlg.Result);
        RefreshAppList();
    }

    private void EditApp()
    {
        if (_appList.SelectedItems.Count == 0) return;
        var current = (ManagedApp)_appList.SelectedItems[0].Tag!;

        using var dlg = new ManagedAppForm(current.Clone());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var index = _apps.IndexOf(current);
        if (index >= 0) _apps[index] = dlg.Result;
        RefreshAppList();
    }

    private void RemoveApp()
    {
        if (_appList.SelectedItems.Count == 0) return;
        var current = (ManagedApp)_appList.SelectedItems[0].Tag!;
        _apps.Remove(current);
        RefreshAppList();
    }

    // ------------------------------------------------------------------ carga/gravação

    private void LoadFrom(AppConfig cfg)
    {
        foreach (var p in Presets) _resolution.Items.Add(p.Label);
        _resolution.Items.Add("Personalizada...");
        var idx = Array.FindIndex(Presets, p => p.W == cfg.Width && p.H == cfg.Height);
        _resolution.SelectedIndex = idx >= 0 ? idx : Presets.Length;
        _resolution.SelectedIndexChanged += (_, _) => OnResolutionChanged();

        foreach (var hz in new[] { 30, 60, 75, 120, 144 }) _refresh.Items.Add(hz);
        _refresh.SelectedItem = _refresh.Items.Contains(cfg.RefreshRate) ? cfg.RefreshRate : 60;

        _side.Items.AddRange(new object[] { "À direita", "À esquerda" });
        _side.SelectedIndex = cfg.Side == MonitorSide.Direita ? 0 : 1;

        _enabled.Checked = cfg.Enabled;
        _forceExtend.Checked = cfg.ForceExtend;
        _neverPrimary.Checked = cfg.NeverPrimary;
        _watchdog.Value = Math.Clamp(cfg.WatchdogSeconds, 0, 120);

        _apiPort.Value = Math.Clamp(cfg.HolyricsApiPort, 1, 65535);
        _apiToken.Text = cfg.HolyricsApiToken ?? string.Empty;

        _startWithWindows.Checked = cfg.StartWithWindows;

        _customWidth = cfg.Width;
        _customHeight = cfg.Height;
    }

    private void OnResolutionChanged()
    {
        if (_resolution.SelectedIndex != Presets.Length) return;

        using var dlg = new CustomResolutionForm(_customWidth, _customHeight);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _customWidth = dlg.ChosenWidth;
            _customHeight = dlg.ChosenHeight;
        }
        else
        {
            _resolution.SelectedIndex = Array.FindIndex(Presets, p => p.W == 1920 && p.H == 1080);
        }
    }

    private void SaveTo(AppConfig cfg)
    {
        if (_resolution.SelectedIndex >= 0 && _resolution.SelectedIndex < Presets.Length)
        {
            cfg.Width = Presets[_resolution.SelectedIndex].W;
            cfg.Height = Presets[_resolution.SelectedIndex].H;
        }
        else
        {
            cfg.Width = _customWidth;
            cfg.Height = _customHeight;
        }

        cfg.RefreshRate = _refresh.SelectedItem is int hz ? hz : 60;
        cfg.Side = _side.SelectedIndex == 0 ? MonitorSide.Direita : MonitorSide.Esquerda;
        cfg.Enabled = _enabled.Checked;
        cfg.ForceExtend = _forceExtend.Checked;
        cfg.NeverPrimary = _neverPrimary.Checked;
        cfg.WatchdogSeconds = (int)_watchdog.Value;

        cfg.ManagedApps = _apps.Select(a => a.Clone()).ToList();

        cfg.HolyricsApiPort = (int)_apiPort.Value;
        cfg.HolyricsApiToken = string.IsNullOrWhiteSpace(_apiToken.Text) ? null : _apiToken.Text.Trim();

        cfg.StartWithWindows = _startWithWindows.Checked;
    }

    private async Task TestApiAsync()
    {
        var probe = new AppConfig
        {
            HolyricsApiPort = (int)_apiPort.Value,
            HolyricsApiToken = string.IsNullOrWhiteSpace(_apiToken.Text) ? null : _apiToken.Text.Trim(),
        };

        _apiResult.Text = "Consultando...";
        var status = await new HolyricsClient().GetStatusAsync(probe);
        _apiResult.Text = status.ApiReachable
            ? "API respondendo."
            : $"Processo: {(status.ProcessRunning ? "rodando" : "parado")} — {status.Detail}";
        _apiResult.ForeColor = status.ApiReachable ? Color.SeaGreen : SystemColors.GrayText;
    }

    private void InstallDriver()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var ok = _provisioner.EnsureDriverInstalled(out var reboot);
            MessageBox.Show(
                ok
                    ? reboot
                        ? "Driver instalado. Reinicie o Windows se o monitor não aparecer."
                        : "Driver instalado."
                    : "Falha ao instalar o driver. Veja os logs.",
                "Monitor Virtual", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        var dev = _provisioner.Driver.GetStatus();
        var st = _provisioner.GetStatus();
        _statusLabel.Text =
            $"Driver: {(dev.Present ? "instalado" : "não instalado")} · " +
            $"dispositivo: {(dev.Enabled ? "habilitado" : "desabilitado")}\n" +
            $"{st.Summary}";
    }
}

/// <summary>Cadastro de um programa que consome o monitor virtual.</summary>
internal sealed class ManagedAppForm : Form
{
    private readonly TextBox _name = new() { Width = 300 };
    private readonly TextBox _path = new() { Width = 300 };
    private readonly TextBox _process = new() { Width = 160 };
    private readonly CheckBox _launch = new()
        { Text = "Abrir este programa depois que o monitor estiver pronto", AutoSize = true };
    private readonly CheckBox _autoRestart = new()
    {
        Text = "Reiniciar sozinho se ele abrir antes do monitor (evite durante o culto)",
        AutoSize = true,
    };

    public ManagedApp Result { get; }

    public ManagedAppForm(ManagedApp app)
    {
        Result = app;

        Text = string.IsNullOrWhiteSpace(app.Name) ? "Adicionar programa" : $"Editar {app.Name}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(470, 240);

        _name.Text = app.Name;
        _path.Text = app.ExePath;
        _process.Text = app.ProcessName ?? string.Empty;
        _launch.Checked = app.LaunchAfterMonitor;
        _autoRestart.Checked = app.AutoRestartIfEarly;

        Controls.Add(new Label { Text = "Nome:", Left = 16, Top = 22, AutoSize = true });
        _name.Left = 130; _name.Top = 18; Controls.Add(_name);

        Controls.Add(new Label { Text = "Executável:", Left = 16, Top = 56, AutoSize = true });
        _path.Left = 130; _path.Top = 52; _path.Width = 262; Controls.Add(_path);
        var browse = new Button { Text = "...", Left = 398, Top = 51, Width = 40 };
        browse.Click += (_, _) => Browse();
        Controls.Add(browse);

        Controls.Add(new Label { Text = "Processo:", Left = 16, Top = 90, AutoSize = true });
        _process.Left = 130; _process.Top = 86; Controls.Add(_process);
        Controls.Add(new Label
        {
            Text = "(opcional)", Left = 298, Top = 90, AutoSize = true, ForeColor = SystemColors.GrayText,
        });

        _launch.Left = 16; _launch.Top = 122; Controls.Add(_launch);
        _autoRestart.Left = 16; _autoRestart.Top = 148; Controls.Add(_autoRestart);

        var ok = new Button { Text = "OK", Left = 250, Top = 190, Width = 90, DialogResult = DialogResult.OK };
        ok.Click += (_, _) => Apply();
        var cancel = new Button
            { Text = "Cancelar", Left = 350, Top = 190, Width = 90, DialogResult = DialogResult.Cancel };

        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Selecione o executável",
            Filter = "Executáveis (*.exe)|*.exe",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _path.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(_name.Text))
            _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    private void Apply()
    {
        Result.Name = string.IsNullOrWhiteSpace(_name.Text)
            ? Path.GetFileNameWithoutExtension(_path.Text)
            : _name.Text.Trim();
        Result.ExePath = _path.Text.Trim();
        Result.ProcessName = string.IsNullOrWhiteSpace(_process.Text) ? null : _process.Text.Trim();
        Result.LaunchAfterMonitor = _launch.Checked;
        Result.AutoRestartIfEarly = _autoRestart.Checked;
    }
}

internal sealed class CustomResolutionForm : Form
{
    private readonly NumericUpDown _w = new() { Minimum = 640, Maximum = 7680, Increment = 10, Width = 90 };
    private readonly NumericUpDown _h = new() { Minimum = 480, Maximum = 4320, Increment = 10, Width = 90 };

    public int ChosenWidth => (int)_w.Value;
    public int ChosenHeight => (int)_h.Value;

    public CustomResolutionForm(int width, int height)
    {
        Text = "Resolução personalizada";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(260, 120);

        _w.Value = Math.Clamp(width, 640, 7680);
        _h.Value = Math.Clamp(height, 480, 4320);

        Controls.Add(new Label { Text = "Largura:", Left = 16, Top = 20, AutoSize = true });
        _w.Left = 90; _w.Top = 16; Controls.Add(_w);
        Controls.Add(new Label { Text = "Altura:", Left = 16, Top = 54, AutoSize = true });
        _h.Left = 90; _h.Top = 50; Controls.Add(_h);

        var ok = new Button { Text = "OK", Left = 60, Top = 84, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button
            { Text = "Cancelar", Left = 150, Top = 84, Width = 90, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
