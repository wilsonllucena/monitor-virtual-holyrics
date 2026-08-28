using MonitorVirtual.Core.Config;

namespace MonitorVirtual.Core.Surround;

[Flags]
public enum BlendEdge
{
    None = 0,
    Left = 1,
    Right = 2,
}

/// <summary>Monitor físico (não virtual) candidato ao telão surround.</summary>
public sealed record SurroundMonitor(
    string DeviceName,
    string Label,
    bool Primary,
    int X,
    int Y,
    int Width,
    int Height);

/// <summary>
/// Fatia do canvas que um projetor mostra. A zona de overlap é o mesmo pedaço
/// do canvas nas duas fatias vizinhas — sem isso o slide aparece duplicado
/// (clone) ou cortado no meio.
/// </summary>
public sealed record SurroundSlice(
    string DeviceName,
    int OutputX,
    int OutputY,
    int OutputWidth,
    int OutputHeight,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    BlendEdge BlendEdge,
    int BlendPixels);

public sealed record SurroundPlan(
    int CanvasWidth,
    int CanvasHeight,
    int Overlap,
    IReadOnlyList<SurroundMonitor> Monitors,
    IReadOnlyList<SurroundSlice> Slices)
{
    public string Summary =>
        $"{CanvasWidth}x{CanvasHeight} em {Slices.Count} projetores" +
        (Overlap > 0 ? $", blend {Overlap} px" : ", sem overposição");

    public string Key =>
        $"{CanvasWidth}x{CanvasHeight}:{Overlap}:" +
        string.Join(",", Slices.Select(s =>
            $"{s.DeviceName}@{s.OutputX},{s.OutputY},{s.OutputWidth}x{s.OutputHeight}:{s.SourceX}+{s.SourceWidth}"));
}

/// <summary>
/// Monta o canvas único a partir dos monitores físicos. Dois Full HD lado a lado
/// com blend de 192 px viram 3648×1080 — o Holyrics enxerga uma tela só.
/// </summary>
public static class SurroundPlanner
{
    public static IReadOnlyList<SurroundMonitor> SelectMonitors(
        IReadOnlyList<SurroundMonitor> physical,
        AppConfig cfg)
    {
        var attached = physical.Where(m => m.Width > 0 && m.Height > 0).ToList();
        if (attached.Count == 0) return attached;

        if (cfg.SurroundDeviceNames is { Count: > 0 })
        {
            var wanted = new HashSet<string>(cfg.SurroundDeviceNames, StringComparer.OrdinalIgnoreCase);
            var picked = attached.Where(m => wanted.Contains(m.DeviceName)).ToList();
            if (picked.Count >= 2) return picked;
        }

        // 3+ telas: o primário fica com o operador; os outros são o telão.
        // 2 telas: as duas entram (os dois projetores, um deles é o "primário" do Windows).
        if (cfg.SurroundPreferNonPrimary)
        {
            var others = attached.Where(m => !m.Primary).ToList();
            if (others.Count >= 2) return others;
        }

        return attached;
    }

    public static SurroundPlan? TryCreate(IReadOnlyList<SurroundMonitor> selected, int overlap, bool swap = false)
    {
        if (selected.Count < 2) return null;

        var ordered = selected
            .OrderBy(m => m.X)
            .ThenBy(m => m.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (swap) ordered.Reverse();

        var maxOverlap = ordered.Min(m => m.Width) - 8;
        if (maxOverlap < 0) maxOverlap = 0;
        overlap = Math.Clamp(overlap, 0, maxOverlap);

        var canvasWidth = ordered.Sum(m => m.Width) - overlap * (ordered.Count - 1);
        var canvasHeight = ordered.Max(m => m.Height);
        if (canvasWidth < 640 || canvasHeight < 480) return null;

        var slices = new List<SurroundSlice>(ordered.Count);
        var sourceX = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            var edge = BlendEdge.None;
            if (overlap > 0)
            {
                if (i > 0) edge |= BlendEdge.Left;
                if (i < ordered.Count - 1) edge |= BlendEdge.Right;
            }

            slices.Add(new SurroundSlice(
                m.DeviceName,
                m.X, m.Y, m.Width, m.Height,
                sourceX, 0, m.Width, canvasHeight,
                edge, overlap));

            sourceX += m.Width - overlap;
        }

        return new SurroundPlan(canvasWidth, canvasHeight, overlap, ordered, slices);
    }

    public static SurroundPlan? TryCreate(IReadOnlyList<SurroundMonitor> physical, AppConfig cfg) =>
        TryCreate(SelectMonitors(physical, cfg), cfg.SurroundBlendOverlap, cfg.SurroundSwap);

    /// <summary>Sanidade do recorte: dois Full HD com blend 192 → canvas 3648 e overlap compartilhado.</summary>
    public static string? SelfTest()
    {
        var left = new SurroundMonitor(@"\\.\DISPLAY1", "Esq", true, 0, 0, 1920, 1080);
        var right = new SurroundMonitor(@"\\.\DISPLAY2", "Dir", false, 1920, 0, 1920, 1080);

        var none = TryCreate(new[] { left }, 192);
        if (none is not null) return "1 monitor não deveria gerar plano";

        var hard = TryCreate(new[] { left, right }, 0);
        if (hard is null || hard.CanvasWidth != 3840 || hard.Slices[1].SourceX != 1920)
            return $"corte seco inesperado: {hard?.CanvasWidth} / {hard?.Slices[1].SourceX}";

        var blend = TryCreate(new[] { left, right }, 192);
        if (blend is null) return "plano com blend veio vazio";
        if (blend.CanvasWidth != 3648) return $"canvas {blend.CanvasWidth}, esperado 3648";
        if (blend.Slices[0].SourceX != 0 || blend.Slices[0].SourceWidth != 1920)
            return "fatia esquerda errada";
        if (blend.Slices[1].SourceX != 1728 || blend.Slices[1].SourceWidth != 1920)
            return $"fatia direita src={blend.Slices[1].SourceX}";
        if (!blend.Slices[0].BlendEdge.HasFlag(BlendEdge.Right) ||
            !blend.Slices[1].BlendEdge.HasFlag(BlendEdge.Left))
            return "bordas de blend invertidas";
        if (blend.Slices[0].BlendPixels != 192)
            return "BlendPixels da fatia não segue a overposição";

        var swapped = TryCreate(new[] { left, right }, 192, swap: true);
        if (swapped is null || swapped.Slices[0].DeviceName != right.DeviceName)
            return "inverter esquerda/direita falhou";

        return null;
    }
}
