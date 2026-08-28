using MonitorVirtual.Core;
using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Devices;
using MonitorVirtual.Core.Display;
using MonitorVirtual.Core.Holyrics;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Provisioning;
using MonitorVirtual.Core.Startup;
using MonitorVirtual.Core.Surround;

Log.AddSink(Console.WriteLine);
AppPaths.EnsureDataDirs();

var command = (args.FirstOrDefault() ?? "status").ToLowerInvariant();
var provisioner = new MonitorProvisioner();
var cfg = AppConfig.Load();

if (command is not ("status" or "displays" or "holyrics" or "apps" or "launch" or "surround" or "help" or "--help" or "-h")
    && !Elevation.IsElevated())
{
    Console.Error.WriteLine("Este comando precisa de um prompt como Administrador.");
    return 2;
}

switch (command)
{
    case "install":
    {
        if (!provisioner.EnsureDriverInstalled(out var reboot))
        {
            Console.Error.WriteLine("Falha ao instalar o driver. Veja o log em " + AppPaths.LogDir);
            return 1;
        }

        Console.WriteLine(reboot ? "Driver instalado (reinício recomendado)." : "Driver instalado.");
        PrintStatus(provisioner, cfg);
        return 0;
    }

    case "uninstall":
    {
        provisioner.Driver.Uninstall(removeDriverPackage: true, out var reboot);
        StartupTask.Disable();
        Console.WriteLine(reboot ? "Removido (reinício recomendado)." : "Removido.");
        return 0;
    }

    case "on":
    case "off":
    {
        cfg.Enabled = command == "on";
        cfg.Save();
        var status = provisioner.Reconcile(cfg);
        Console.WriteLine(status.Summary);
        return status.MonitorActive == cfg.Enabled ? 0 : 1;
    }

    case "apply":
    {
        ParseOverrides(args, cfg);
        cfg.Save();
        var status = provisioner.Reconcile(cfg);
        Console.WriteLine(status.Summary);
        return 0;
    }

    case "restart":
        Console.WriteLine(provisioner.Driver.Restart() ? "Dispositivo reiniciado." : "Falha ao reiniciar.");
        return 0;

    case "watch":
    {
        Console.WriteLine("Watchdog ativo (Ctrl+C para sair).");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        while (!cts.IsCancellationRequested)
        {
            var status = provisioner.Reconcile(AppConfig.Load());
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {status.Summary}");
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, cfg.WatchdogSeconds)), cts.Token); }
            catch (OperationCanceledException) { break; }
        }

        return 0;
    }

    case "displays":
    {
        var display = new DisplayService();
        foreach (var a in display.ListAdapters())
        {
            var geo = display.GetGeometry(a.DeviceName);
            var tags = string.Join(",", new[]
            {
                a.Attached ? "ligado" : "desligado",
                a.Primary ? "primário" : null,
                a.IsVirtual ? "VIRTUAL" : null,
            }.Where(t => t is not null));

            Console.WriteLine($"{a.DeviceName,-14} {tags,-28} {a.DeviceString}");
            Console.WriteLine($"{"",-14} monitor: {a.MonitorName}  id: {a.DeviceId}");
            if (geo is not null)
                Console.WriteLine($"{"",-14} {geo.Width}x{geo.Height}@{geo.RefreshRate}Hz em ({geo.X},{geo.Y})");
        }

        return 0;
    }

    case "apps":
    {
        if (args.Length > 1 && args[1].Equals("--detect", StringComparison.OrdinalIgnoreCase))
        {
            var found = AppLauncher.Autodetect();
            var added = 0;
            foreach (var app in found)
            {
                if (cfg.ManagedApps.Any(a =>
                        string.Equals(a.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                cfg.ManagedApps.Add(app.Clone());
                added++;
            }

            cfg.Save();
            Console.WriteLine($"{added} programa(s) adicionado(s) de {found.Count} encontrado(s).");
        }

        if (cfg.ManagedApps.Count == 0)
        {
            Console.WriteLine("Nenhum programa configurado. Use: mvcli apps --detect");
            return 0;
        }

        foreach (var app in cfg.ManagedApps)
        {
            var running = AppLauncher.IsRunning(app);
            Console.WriteLine($"{app.Name,-18} {(running ? "aberto  " : "fechado ")} " +
                              $"abre-depois={(app.LaunchAfterMonitor ? "sim" : "não"),-4} " +
                              $"reinicio-auto={(app.AutoRestartIfEarly ? "sim" : "não"),-4} {app.ExePath}");
        }

        return 0;
    }

    case "launch":
    {
        var status = provisioner.GetStatus();
        if (!status.MonitorActive)
        {
            Console.Error.WriteLine("O monitor virtual não está ativo; abortando para não repetir o erro de ordem.");
            return 1;
        }

        foreach (var app in cfg.ManagedApps.Where(a => a.LaunchAfterMonitor))
            Console.WriteLine($"{app.Name}: {(AppLauncher.Launch(app) ? "iniciado" : "falhou")}");

        return 0;
    }

    case "surround":
    {
        var self = SurroundPlanner.SelfTest();
        if (self is not null)
        {
            Console.Error.WriteLine("Falha interna no planner de surround: " + self);
            return 1;
        }

        var curve = SoftEdgeCurve.SelfTest();
        if (curve is not null)
        {
            Console.Error.WriteLine("Falha interna na curva de blend: " + curve);
            return 1;
        }

        var nv = NvidiaSpan.SelfTest();
        if (nv is not null)
        {
            Console.Error.WriteLine("Falha interna no span NVIDIA: " + nv);
            return 1;
        }

        var contract = NvidiaSpan.SelfTestPlannerContract();
        if (contract is not null)
        {
            Console.Error.WriteLine("Falha interna no contrato do canvas: " + contract);
            return 1;
        }

        var display = new DisplayService();
        var physical = display.ListPhysical();
        Console.WriteLine($"Monitores físicos: {physical.Count}");
        foreach (var m in physical.OrderBy(m => m.X).ThenBy(m => m.DeviceName))
        {
            Console.WriteLine(
                $"  {m.DeviceName,-14} {m.Width}x{m.Height} em ({m.X},{m.Y}) " +
                $"{(m.Primary ? "primário" : "        ")}  {m.Label}");
        }

        Console.WriteLine($"Surround         : {(cfg.SurroundEnabled ? "ligado" : "desligado")} " +
                          $"overlap={cfg.SurroundBlendOverlap}px gama={cfg.SurroundBlendGamma} " +
                          $"ganho={cfg.SurroundBlendGain} inverter={cfg.SurroundSwap}");
        Console.WriteLine($"NVIDIA NVAPI     : {(NvidiaSpan.IsAvailable ? "presente" : "ausente")}");
        var nvidiaNow = NvidiaSpan.DetectActive();
        if (nvidiaNow is not null)
            Console.WriteLine($"Span NVIDIA      : {nvidiaNow.Summary}");

        var plan = SurroundPlanner.TryCreate(physical, cfg);
        if (plan is null)
        {
            Console.WriteLine("Plano            : precisa de 2 projetores físicos.");
        }
        else
        {
            Console.WriteLine($"Plano            : {plan.Summary}");
            foreach (var s in plan.Slices)
            {
                Console.WriteLine(
                    $"  {s.DeviceName,-14} canvas [{s.SourceX}..{s.SourceX + s.SourceWidth}) " +
                    $"saída {s.OutputWidth}x{s.OutputHeight} blend={s.BlendEdge} {s.BlendPixels}px");
            }
        }

        var turnOn = args.Any(a => a.Equals("--on", StringComparison.OrdinalIgnoreCase));
        var turnOff = args.Any(a => a.Equals("--off", StringComparison.OrdinalIgnoreCase));
        if (!turnOn && !turnOff) return 0;

        if (!Elevation.IsElevated())
        {
            Console.Error.WriteLine("Ligar/desligar o surround precisa de um prompt como Administrador.");
            return 2;
        }

        cfg.SurroundEnabled = turnOn;
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--overlap", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var overlap))
                cfg.SurroundBlendOverlap = overlap;
            if (args[i].Equals("--gamma", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var gamma))
                cfg.SurroundBlendGamma = gamma;
            if (args[i].Equals("--gain", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var gain))
                cfg.SurroundBlendGain = gain;
        }

        cfg.Save();
        var status = provisioner.Reconcile(cfg);
        Console.WriteLine(status.Summary);
        var after = SurroundPlanner.TryCreate(new DisplayService().ListPhysical(), cfg);
        Console.WriteLine(after is null ? "Canvas: (inativo)" : $"Canvas: {after.Summary}");
        return 0;
    }

    case "holyrics":
    {
        var parse = HolyricsClient.SelfTestParse();
        if (parse is not null)
        {
            Console.Error.WriteLine("Falha interna no parser de telas do Holyrics: " + parse);
            return 1;
        }

        var client = new HolyricsClient();
        var st = await client.GetStatusAsync(cfg);
        Console.WriteLine($"Processo em execução: {(st.ProcessRunning ? "sim" : "não")}");
        Console.WriteLine($"API acessível: {(st.ApiReachable ? "sim" : "não")} ({st.Detail})");
        Console.WriteLine($"Caminho detectado: {HolyricsClient.Autodetect() ?? "não encontrado"}");

        if (st.ApiReachable)
        {
            var displays = await client.ListDisplaysAsync(cfg);
            foreach (var screen in displays)
            {
                Console.WriteLine(
                    $"Tela {screen.Id,-14} {screen.Name}  " +
                    $"origem={screen.Screen ?? $"{screen.AreaX},{screen.AreaY}"}  " +
                    $"{screen.AreaW}x{screen.AreaH}  hide={screen.Hide}");
            }

            if (args.Any(a => a.Equals("--tela-unica", StringComparison.OrdinalIgnoreCase)))
            {
                var video = provisioner.GetStatus();
                var geo = video.Geometry;
                if (geo is null)
                {
                    Console.Error.WriteLine("Telão/monitor virtual inativo; não dá para apontar a Tela pública.");
                    return 1;
                }

                var x = geo.X;
                var y = geo.Y;
                var w = geo.Width;
                var h = geo.Height;

                var projectors = SurroundPlanner.SelectMonitors(provisioner.Display.ListPhysical(), cfg);
                var tela = await client.EnsureSinglePublicScreenAsync(cfg, x, y, w, h, projectors);
                if (!tela.Ok)
                {
                    Console.Error.WriteLine("Não foi possível apontar a Tela pública: " + tela.Error);
                    return 1;
                }

                Console.WriteLine(tela.Changed
                    ? $"Tela pública no canvas virtual. {tela.Detail}"
                    : "Tela pública já estava no canvas único.");
            }

            var outputs = await client.ListNdiAsync(cfg);
            if (outputs.Count == 0)
            {
                Console.WriteLine("NDI: nenhuma saída (Holyrics 2.29+).");
            }
            else
            {
                foreach (var ndi in outputs)
                {
                    Console.WriteLine(
                        $"NDI {(ndi.Enabled ? "ligado" : "desligado"),-8} " +
                        $"fundo={(ndi.TransparentBackground ? "transparente" : "opaco"),-13} " +
                        $"{ndi.Name ?? ndi.Id}");
                }
            }

            if (args.Any(a => a.Equals("--ndi-fundo", StringComparison.OrdinalIgnoreCase)))
            {
                var fix = await client.EnsureOpaqueNdiBackgroundAsync(cfg);
                if (!fix.Ok)
                {
                    Console.Error.WriteLine("Não foi possível incluir o papel de fundo no NDI: " + fix.Error);
                    return 1;
                }

                Console.WriteLine(fix.Changed > 0
                    ? $"Papel de fundo ligado em {fix.Changed} saída(s) NDI."
                    : "Saídas NDI já estavam com fundo opaco.");
            }
        }

        return 0;
    }

    case "startup-on":
    {
        // a tarefa precisa subir o app de bandeja, não esta CLI — o instalador chama
        // "mvcli startup-on", então Environment.ProcessPath apontaria para o mvcli.exe
        var appExe = Path.Combine(AppContext.BaseDirectory, "MonitorVirtual.exe");
        if (!File.Exists(appExe))
        {
            Console.Error.WriteLine($"MonitorVirtual.exe não encontrado em {AppContext.BaseDirectory}.");
            return 1;
        }

        return StartupTask.Enable(appExe) ? 0 : 1;
    }

    case "startup-off":
        return StartupTask.Disable() ? 0 : 1;

    case "status":
        PrintStatus(provisioner, cfg);
        return 0;

    default:
        Console.WriteLine("""
            mvcli — controle do monitor virtual (requer Administrador para alterar estado)

              status                      estado atual do driver, dispositivo e monitor
              displays                    lista todos os adaptadores de vídeo
              install                     instala o driver e cria o dispositivo
              uninstall                   remove o dispositivo e o pacote de driver
              on | off                    liga/desliga o monitor virtual
              apply [--w 1920] [--h 1080] [--hz 60] [--side direita|esquerda]
                                          aplica resolução/posição
              restart                     reinicia o dispositivo (relê vdd_settings.xml)
              watch                       roda o watchdog em primeiro plano
              apps [--detect]             lista (e detecta) os programas que usam o monitor
              launch                      abre os programas configurados, se o monitor estiver ativo
              holyrics [--ndi-fundo] [--tela-unica]
                                          testa a API; --ndi-fundo inclui o papel de fundo no NDI;
                                          --tela-unica aponta a Tela pública ao monitor virtual
              surround [--on|--off] [--overlap 192] [--gamma 2.2] [--gain 1]
                                          detecta projetores e mostra o plano do telão único
              startup-on | startup-off    início automático elevado no logon
            """);
        return 0;
}

static void ParseOverrides(string[] args, AppConfig cfg)
{
    for (var i = 1; i < args.Length - 1; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--w" or "--width":
                if (int.TryParse(args[i + 1], out var w)) cfg.Width = w;
                break;
            case "--h" or "--height":
                if (int.TryParse(args[i + 1], out var h)) cfg.Height = h;
                break;
            case "--hz" or "--refresh":
                if (int.TryParse(args[i + 1], out var hz)) cfg.RefreshRate = hz;
                break;
            case "--side":
                cfg.Side = args[i + 1].StartsWith("esq", StringComparison.OrdinalIgnoreCase)
                    ? MonitorSide.Esquerda
                    : MonitorSide.Direita;
                break;
        }
    }
}

static void PrintStatus(MonitorProvisioner provisioner, AppConfig cfg)
{
    var dev = provisioner.Driver.GetStatus();
    var st = provisioner.GetStatus();

    Console.WriteLine($"Driver instalado : {(dev.Present ? "sim" : "não")}" +
                      (dev.Present ? $" ({dev.Description})" : ""));
    Console.WriteLine($"Dispositivo      : {(dev.Enabled ? "habilitado" : "desabilitado")}" +
                      (dev.ProblemCode != 0 ? $" (problema {dev.ProblemCode})" : ""));
    Console.WriteLine($"Monitor virtual  : {(st.MonitorActive ? "ativo" : "inativo")}");
    Console.WriteLine($"Topologia        : {(st.Extended ? "estendida" : "não estendida")}");
    Console.WriteLine($"Adaptador        : {st.AdapterDeviceName ?? "-"} / {st.MonitorName ?? "-"}");
    Console.WriteLine($"Geometria        : {(st.Geometry is null ? "-" : $"{st.Geometry.Width}x{st.Geometry.Height}@{st.Geometry.RefreshRate}Hz em ({st.Geometry.X},{st.Geometry.Y})")}");
    Console.WriteLine($"Desejado         : {cfg.ResolutionText}, lado {cfg.Side}, ligado={cfg.Enabled}");
    Console.WriteLine($"Surround         : {(cfg.SurroundEnabled ? "ligado" : "desligado")} " +
                      $"(overlap {cfg.SurroundBlendOverlap}px gama {cfg.SurroundBlendGamma} ganho {cfg.SurroundBlendGain})");
    if (st.Surround is not null)
        Console.WriteLine($"Span             : {st.Surround.Kind} — {st.Surround.Summary}");
    Console.WriteLine($"NVIDIA NVAPI     : {(NvidiaSpan.IsAvailable ? "presente" : "ausente")}");
    Console.WriteLine($"Início automático: {(StartupTask.Exists() ? "configurado" : "não configurado")}");
    Console.WriteLine($"Holyrics rodando : {(HolyricsClient.IsRunning() ? "sim" : "não")}");
    Console.WriteLine($"Logs             : {AppPaths.LogDir}");
}
