using System.Runtime.InteropServices;
using System.Text.Json;
using MonitorVirtual.Core.Display;
using MonitorVirtual.Core.Interop;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Surround;

public enum SurroundSurfaceKind
{
    None = 0,
    /// <summary>NVIDIA Surround/Mosaic: o Windows vê um monitor só (taskbar contínua).</summary>
    NvidiaLogical = 1,
    /// <summary>
    /// Canvas IddCx como primário + fatias nos projetores. A taskbar mora no canvas
    /// (atravessa o telão na parede); o Windows ainda lista as saídas físicas.
    /// </summary>
    VirtualOverlay = 2,
}

public sealed record SurroundSurface(
    SurroundSurfaceKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    string? AdapterDeviceName,
    string Summary);

/// <summary>Resultado de tentar unir os projetores num monitor lógico NVIDIA.</summary>
public sealed record NvidiaSpanResult(
    bool Ok,
    string Detail,
    SurroundSurface? Surface,
    IReadOnlyList<uint> DisplayIds)
{
    public static NvidiaSpanResult Fail(string detail) =>
        new(false, detail, null, Array.Empty<uint>());
}

/// <summary>
/// Liga NVIDIA Surround/Mosaic nos projetores: 2 HDMI viram UM desktop
/// (ex. 3648×1080), com overposição nativa. É o único caminho em que o
/// Windows — e a taskbar — enxergam um monitor só. IddCx não consegue
/// fundir saídas físicas; CCD só estende ou clona.
/// </summary>
public static class NvidiaSpan
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static bool IsAvailable => NvApi.IsAvailable;

    public static string RestoreFile => Path.Combine(AppPaths.DataDir, "surround-span.json");

    public static SurroundSurface? DetectActive()
    {
        if (!NvApi.IsAvailable) return null;

        var count = (uint)8;
        var grids = Enumerable.Range(0, 8).Select(_ => NvMosaicGridTopo.New()).ToArray();
        var rc = NvApi.EnumGrids(grids, ref count);
        if (rc == NvApi.Ok)
        {
            for (var i = 0; i < count && i < grids.Length; i++)
            {
                var g = grids[i];
                if (g.Displays is null || g.DisplayCount < 2 || g.Columns < 2) continue;
                var overlap = Math.Abs(g.Displays[0].OverlapX);
                var canvasW = (int)(g.DisplaySettings.Width * g.Columns) - overlap * ((int)g.Columns - 1);
                if (canvasW < 640) canvasW = (int)(g.DisplaySettings.Width * Math.Max(1, g.Columns));
                var canvasH = (int)Math.Max(1, g.DisplaySettings.Height);
                return ReadLogicalSurface(canvasW, canvasH, @"\\.\DISPLAY1");
            }
        }

        rc = NvApi.GetCurrentTopo(out var brief, out var setting, out var ox, out _);
        if (rc == NvApi.Ok && brief.Enabled != 0 && setting.Width > 0)
        {
            var canvasW = (int)setting.Width * 2 - Math.Abs(ox);
            return ReadLogicalSurface(Math.Max(640, canvasW), (int)setting.Height, @"\\.\DISPLAY1");
        }

        return null;
    }

    public static NvidiaSpanResult TryEnable(IReadOnlyList<SurroundMonitor> monitors, int overlap, int refreshRate)
    {
        if (monitors.Count < 2)
            return NvidiaSpanResult.Fail("precisa de 2 projetores");

        if (!NvApi.IsAvailable)
            return NvidiaSpanResult.Fail("NVAPI ausente (sem GPU NVIDIA ou driver antigo)");

        var mapped = MapDisplays(monitors);
        if (mapped.Count < 2)
            return NvidiaSpanResult.Fail(
                mapped.Count == 0
                    ? "os projetores não são saídas NVIDIA (IddCx não entra no Surround)"
                    : "só um projetor tem ID NVIDIA; Surround precisa dos dois no mesmo GPU");

        var left = mapped[0];
        var right = mapped[1];
        var width = (uint)Math.Max(8, left.Monitor.Width);
        var height = (uint)Math.Max(8, Math.Max(left.Monitor.Height, right.Monitor.Height));
        var freq = (uint)Math.Clamp(refreshRate, 24, 240);
        overlap = Math.Clamp(overlap, 0, (int)width - 8);

        // GeForce Surround junta as bordas SEM compartilhar pixels. Overlap nativo
        // do Mosaic (3648) só existe em Quadro+Sync; no PC da igreja vira uma
        // linha no meio. O span fica 3840 (corte seco) e o overlay aplica o blend.
        var mosaicOverlap = 0;

        if (IsMatchingSpan(mapped, mosaicOverlap, out var current) && current.Surface is { Width: >= 3200 })
            return current;

        SaveRestorePoint(mapped, (int)width, (int)height);

        var flagsToTry = new uint[]
        {
            NvApi.GridFlagDriverReload,
            NvApi.GridFlagBaseMosaic | NvApi.GridFlagDriverReload,
            0,
            NvApi.GridFlagBaseMosaic,
            NvApi.GridFlagImmersiveGaming | NvApi.GridFlagDriverReload,
            NvApi.GridFlagBezel | NvApi.GridFlagDriverReload,
        };

        foreach (var flags in flagsToTry)
        {
            var grids = BuildGrids(mapped, width, height, freq, mosaicOverlap, flags);
            var rc = NvApi.SetGrids(grids, NvApi.MosaicSetTopoCurrentGpu | NvApi.MosaicSetTopoAllowInvalid);
            if (rc == NvApi.Ok)
            {
                Thread.Sleep(800);
                if (IsMatchingSpan(mapped, mosaicOverlap, out var surface))
                {
                    MarkEnabledByUs(mapped, overlap);
                    Log.Info($"NVIDIA Surround ativo: {surface.Surface!.Summary} (flags=0x{flags:X}, span seco; blend no overlay).");
                    TryApplyScanoutBlend(mapped, overlap, SoftEdgeCurve.DefaultGamma, SoftEdgeCurve.DefaultGain);
                    return surface;
                }

                Log.Warn("SetDisplayGrids OK, mas o Windows ainda não listou o monitor único.");
            }
            else
            {
                Log.Warn($"SetDisplayGrids flags=0x{flags:X} → {NvApi.Error(rc)}");
            }
        }

        var topoRc = TryLegacy1x2(width, height, freq, mosaicOverlap);
        if (topoRc == NvApi.Ok)
        {
            Thread.Sleep(800);
            if (IsMatchingSpan(mapped, mosaicOverlap, out var surface))
            {
                MarkEnabledByUs(mapped, overlap);
                Log.Info($"NVIDIA Surround (topo 1x2) ativo: {surface.Surface!.Summary} (span seco; blend no overlay).");
                return surface;
            }
        }
        else
        {
            Log.Warn($"SetCurrentTopo 1x2 → {NvApi.Error(topoRc)}");
        }

        return NvidiaSpanResult.Fail("o driver NVIDIA recusou o Surround (Mosaic/Surround indisponível neste GPU/driver)");
    }

    public static bool TryDisable()
    {
        if (!NvApi.IsAvailable) return true;
        var state = LoadState();
        if (state is not { EnabledByUs: true })
        {
            Log.Info("Surround NVIDIA não foi ligado por este app; não desfaço a topologia.");
            return true;
        }

        var count = (uint)8;
        var grids = Enumerable.Range(0, 8).Select(_ => NvMosaicGridTopo.New()).ToArray();
        var rc = NvApi.EnumGrids(grids, ref count);
        if (rc != NvApi.Ok)
        {
            rc = NvApi.EnableCurrentTopo(0);
            ClearState();
            if (rc != NvApi.Ok)
            {
                Log.Warn($"EnableCurrentTopo(0) → {NvApi.Error(rc)}");
                return false;
            }

            Log.Info("NVIDIA Surround desligado (topo atual).");
            return true;
        }

        var split = new List<NvMosaicGridTopo>();
        for (var i = 0; i < count && i < grids.Length; i++)
        {
            var g = grids[i];
            if (g.DisplayCount <= 1)
            {
                if (g.DisplayCount == 1) split.Add(g);
                continue;
            }

            var w = g.DisplaySettings.Width;
            var h = g.DisplaySettings.Height;
            var hz = g.DisplaySettings.Freq;
            for (var d = 0; d < g.DisplayCount && d < NvApi.MaxDisplays; d++)
            {
                var id = g.Displays[d].DisplayId;
                if (id == 0) continue;
                split.Add(NvMosaicGridTopo.OneByOne(id, w, h, hz, NvApi.GridFlagDriverReload));
            }
        }

        if (split.Count == 0)
        {
            rc = NvApi.EnableCurrentTopo(0);
            ClearState();
            return rc == NvApi.Ok;
        }

        rc = NvApi.SetGrids(split.ToArray(), NvApi.MosaicSetTopoCurrentGpu | NvApi.MosaicSetTopoAllowInvalid);
        if (rc != NvApi.Ok)
        {
            Log.Warn($"Desligar Surround (1x1) → {NvApi.Error(rc)}");
            rc = NvApi.EnableCurrentTopo(0);
        }

        ClearState();
        if (rc == NvApi.Ok) Log.Info("NVIDIA Surround desligado: projetores voltaram a ser monitores separados.");
        return rc == NvApi.Ok;
    }

    public static bool TryApplyScanoutBlend(
        IReadOnlyList<MappedDisplay> mapped, int overlap, double gamma, double gain)
    {
        if (!NvApi.IsAvailable || mapped.Count < 2 || overlap <= 0) return false;

        var ok = true;
        for (var i = 0; i < mapped.Count; i++)
        {
            var edge = BlendEdge.None;
            if (i > 0) edge |= BlendEdge.Left;
            if (i < mapped.Count - 1) edge |= BlendEdge.Right;
            if (!SetIntensity(mapped[i].DisplayId, mapped[i].Monitor.Width, overlap, edge, gamma, gain))
                ok = false;
        }

        return ok;
    }

    public static (int Left, int Right, int Height) NativeHalves()
    {
        var state = LoadState();
        if (state is { LeftWidth: > 0, RightWidth: > 0 })
            return (state.LeftWidth, state.RightWidth, Math.Max(480, state.Height));
        return (1920, 1920, 1080);
    }

    public static bool Probe() => NvApi.IsAvailable;

    public static string? SelfTest()
    {
        var nv = NvApi.SelfTest();
        if (nv is not null) return nv;

        var surface = new SurroundSurface(
            SurroundSurfaceKind.NvidiaLogical, 0, 0, 3648, 1080, @"\\.\DISPLAY1",
            "3648x1080 NVIDIA");
        if (surface.Width != 3648 || surface.Kind != SurroundSurfaceKind.NvidiaLogical)
            return "surface lógica com tamanho errado";
        if (NvidiaSpanResult.Fail("x").Ok) return "Fail não pode ser Ok";
        return null;
    }

    /// <summary>Dois Full HD com overlap 192 → canvas 3648; 1 monitor não gera span.</summary>
    public static string? SelfTestPlannerContract()
    {
        var left = new SurroundMonitor(@"\\.\DISPLAY1", "Esq", true, 0, 0, 1920, 1080);
        var right = new SurroundMonitor(@"\\.\DISPLAY2", "Dir", false, 1920, 0, 1920, 1080);
        var plan = SurroundPlanner.TryCreate(new[] { left, right }, 192);
        if (plan is null || plan.CanvasWidth != 3648) return "planner 3648";
        if (SurroundPlanner.TryCreate(new[] { left }, 192) is not null) return "1 monitor não vira span";
        return null;
    }

    public static IReadOnlyList<MappedDisplay> MapDisplays(IReadOnlyList<SurroundMonitor> monitors)
    {
        var list = new List<MappedDisplay>();
        foreach (var m in monitors)
        {
            var id = NvApi.DisplayIdFromGdiName(m.DeviceName);
            if (id is null or 0)
            {
                Log.Warn($"NVAPI não reconheceu {m.DeviceName} (provável IddCx ou outro GPU).");
                continue;
            }

            list.Add(new MappedDisplay(m, id.Value));
        }

        return list;
    }

    public sealed record MappedDisplay(SurroundMonitor Monitor, uint DisplayId);

    private static NvMosaicGridTopo[] BuildGrids(
        IReadOnlyList<MappedDisplay> mapped, uint width, uint height, uint freq, int overlap, uint flags)
    {
        var grids = new List<NvMosaicGridTopo>
        {
            NvMosaicGridTopo.OneByTwo(
                mapped[0].DisplayId, mapped[1].DisplayId, width, height, freq, overlap, flags),
        };

        for (var i = 2; i < mapped.Count; i++)
            grids.Add(NvMosaicGridTopo.OneByOne(mapped[i].DisplayId, width, height, freq, flags));

        return grids.ToArray();
    }

    private static bool IsMatchingSpan(
        IReadOnlyList<MappedDisplay> mapped, int overlap, out NvidiaSpanResult result)
    {
        result = NvidiaSpanResult.Fail("ainda não é um monitor só");
        var count = (uint)8;
        var grids = Enumerable.Range(0, 8).Select(_ => NvMosaicGridTopo.New()).ToArray();
        var rc = NvApi.EnumGrids(grids, ref count);
        if (rc != NvApi.Ok) return false;

        for (var i = 0; i < count && i < grids.Length; i++)
        {
            var g = grids[i];
            if (g.Displays is null || g.DisplayCount < 2 || g.Columns < 2) continue;

            var ids = new HashSet<uint>();
            for (var d = 0; d < g.DisplayCount && d < NvApi.MaxDisplays; d++)
                if (g.Displays[d].DisplayId != 0) ids.Add(g.Displays[d].DisplayId);

            if (!ids.Contains(mapped[0].DisplayId) || !ids.Contains(mapped[1].DisplayId))
                continue;

            var canvasW = (int)(g.DisplaySettings.Width * g.Columns) - overlap * ((int)g.Columns - 1);
            if (g.Displays[0].OverlapX != 0)
            {
                // Pedimos span seco (overlap 0) e o driver ainda tem bezel/overlap:
                // não é o alvo — o overlay de 1920+1920 não cabe num desktop 3648.
                if (overlap == 0 && Math.Abs(g.Displays[0].OverlapX) > 16)
                    continue;
                canvasW = (int)(g.DisplaySettings.Width * g.Columns) - Math.Abs(g.Displays[0].OverlapX);
            }
            var canvasH = (int)g.DisplaySettings.Height;
            if (canvasW < 640) canvasW = (int)(g.DisplaySettings.Width * g.Columns);

            var surface = ReadLogicalSurface(canvasW, canvasH, mapped[0].Monitor.DeviceName);
            result = new NvidiaSpanResult(true, "já estava em Surround", surface,
                mapped.Select(m => m.DisplayId).ToList());
            return true;
        }

        var topoRc = NvApi.GetCurrentTopo(out var brief, out var setting, out var ox, out _);
        if (topoRc == NvApi.Ok && brief.Enabled != 0 && setting.Width > 0)
        {
            if (overlap == 0 && Math.Abs(ox) > 16)
                return false;

            var canvasW = (int)setting.Width * 2 - Math.Max(overlap, Math.Abs(ox));
            var surface = ReadLogicalSurface(canvasW, (int)setting.Height, mapped[0].Monitor.DeviceName);
            result = new NvidiaSpanResult(true, "topo Mosaic ativo", surface,
                mapped.Select(m => m.DisplayId).ToList());
            return true;
        }

        return false;
    }

    private static SurroundSurface ReadLogicalSurface(int fallbackW, int fallbackH, string hintName)
    {
        var display = new DisplayService();
        var attached = display.ListAdapters().Where(a => a.Attached && !a.IsVirtual).ToList();
        DisplayAdapter? best = null;
        DisplayGeometry? bestGeo = null;
        foreach (var a in attached)
        {
            var geo = display.GetGeometry(a.DeviceName);
            if (geo is null) continue;
            if (bestGeo is null || geo.Width > bestGeo.Width)
            {
                best = a;
                bestGeo = geo;
            }
        }

        if (best is not null && bestGeo is not null && bestGeo.Width >= fallbackW - 64)
        {
            return new SurroundSurface(
                SurroundSurfaceKind.NvidiaLogical,
                bestGeo.X, bestGeo.Y, bestGeo.Width, bestGeo.Height,
                best.DeviceName,
                $"{bestGeo.Width}x{bestGeo.Height} em 1 monitor NVIDIA (taskbar contínua)");
        }

        return new SurroundSurface(
            SurroundSurfaceKind.NvidiaLogical,
            0, 0, fallbackW, fallbackH,
            hintName,
            $"{fallbackW}x{fallbackH} em 1 monitor NVIDIA (taskbar contínua)");
    }

    private static int TryLegacy1x2(uint width, uint height, uint freq, int overlap)
    {
        var brief = NvMosaicTopoBrief.New();
        brief.Topo = NvApi.MosaicTopo1x2Basic;
        brief.IsPossible = 1;
        var setting = NvMosaicDisplaySettingV1.New();
        setting.Width = width;
        setting.Height = height;
        setting.Freq = freq;
        return NvApi.SetCurrentTopo(brief, setting, overlap, 0, 1);
    }

    private static bool SetIntensity(
        uint displayId, int outputWidth, int overlap, BlendEdge edge, double gamma, double gain)
    {
        var width = Math.Max(8, outputWidth);
        var lut = SoftEdgeCurve.BuildLut(overlap, gamma, gain);
        var pixels = width;
        var floats = new float[pixels * 3];
        for (var x = 0; x < pixels; x++)
        {
            var f = 1f;
            if (edge.HasFlag(BlendEdge.Left) && x < overlap)
                f = lut[x];
            else if (edge.HasFlag(BlendEdge.Right) && x >= pixels - overlap)
                f = lut[pixels - 1 - x];
            floats[x * 3] = f;
            floats[x * 3 + 1] = f;
            floats[x * 3 + 2] = f;
        }

        var data = new NvScanoutIntensityDataV1
        {
            Version = NvApi.MakeVersion(MarshalSizeIntensity(), 1),
            Width = (uint)pixels,
            Height = 1,
        };

        var tex = Marshal.AllocHGlobal(floats.Length * sizeof(float));
        var block = Marshal.AllocHGlobal(Marshal.SizeOf<NvScanoutIntensityDataV1>());
        try
        {
            Marshal.Copy(floats, 0, tex, floats.Length);
            data.BlendingTexture = tex;
            Marshal.StructureToPtr(data, block, false);
            var rc = NvApi.SetScanoutIntensity(displayId, block, out _);
            if (rc != NvApi.Ok)
            {
                Log.Warn($"SetScanoutIntensity({displayId}) → {NvApi.Error(rc)} (blend fica só na overposição nativa).");
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(block);
            Marshal.FreeHGlobal(tex);
        }
    }

    private static int MarshalSizeIntensity() => Marshal.SizeOf<NvScanoutIntensityDataV1>();

    private sealed record SpanState(
        bool EnabledByUs,
        uint[] DisplayIds,
        int Overlap,
        int LeftWidth = 1920,
        int RightWidth = 1920,
        int Height = 1080);

    private static void SaveRestorePoint(IReadOnlyList<MappedDisplay> mapped, int leftWidth, int height) =>
        WriteState(new SpanState(false, mapped.Select(m => m.DisplayId).ToArray(), 0,
            leftWidth, mapped.Count > 1 ? mapped[1].Monitor.Width : leftWidth, height));

    private static void MarkEnabledByUs(IReadOnlyList<MappedDisplay> mapped, int overlap) =>
        WriteState(new SpanState(
            true,
            mapped.Select(m => m.DisplayId).ToArray(),
            overlap,
            mapped[0].Monitor.Width,
            mapped.Count > 1 ? mapped[1].Monitor.Width : mapped[0].Monitor.Width,
            mapped[0].Monitor.Height));

    private static void WriteState(SpanState state)
    {
        try
        {
            AppPaths.EnsureDataDirs();
            File.WriteAllText(RestoreFile, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn($"Não gravei o estado do Surround NVIDIA: {ex.Message}");
        }
    }

    private static SpanState? LoadState()
    {
        try
        {
            if (!File.Exists(RestoreFile)) return null;
            return JsonSerializer.Deserialize<SpanState>(File.ReadAllText(RestoreFile), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static void ClearState()
    {
        try
        {
            if (File.Exists(RestoreFile)) File.Delete(RestoreFile);
        }
        catch
        {
            // o próximo ligar/desligar regrava
        }
    }
}
