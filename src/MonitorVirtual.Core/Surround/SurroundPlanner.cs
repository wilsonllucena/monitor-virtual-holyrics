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
/// <paramref name="PinOutput"/> trava a janela em OutputX/Y (metades de um
/// span NVIDIA); senão o app segue o Screen.Bounds do DeviceName.
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
    int BlendPixels,
    bool PinOutput = false);

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

    public static SurroundPlan? TryCreate(
        IReadOnlyList<SurroundMonitor> selected,
        int overlap,
        bool swap = false,
        int alignLeftX = 0,
        int alignRightX = 0,
        bool pinOutput = false)
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

        var slices = BuildSlices(ordered, canvasHeight, overlap, alignLeftX, alignRightX, pinOutput);
        return new SurroundPlan(canvasWidth, canvasHeight, overlap, ordered, slices);
    }

    public static SurroundPlan? TryCreate(IReadOnlyList<SurroundMonitor> physical, AppConfig cfg)
    {
        var selected = SelectMonitors(physical, cfg);
        if (selected.Count >= 2)
            return TryCreate(selected, cfg.SurroundBlendOverlap, cfg.SurroundSwap,
                cfg.SurroundAlignLeftX, cfg.SurroundAlignRightX);

        // Mosaic juntou os HDMI num monitor só: as duas saídas físicas somem da
        // lista do Windows. Recortamos o span em duas metades nativas (1920+1920)
        // para o overlay continuar cobrindo cada projetor.
        if (physical.Count == 1 && IsLogicalSpan(physical[0]))
        {
            var (leftW, rightW) = InferHalfWidths(physical[0]);
            return TryCreateFromLogicalSpan(
                physical[0], cfg.SurroundBlendOverlap, cfg.SurroundSwap,
                leftW, rightW, cfg.SurroundAlignLeftX, cfg.SurroundAlignRightX);
        }

        return null;
    }

    /// <summary>
    /// NVIDIA Surround/Mosaic no GeForce junta 3840×1080 <b>sem</b> overlap de
    /// pixels. Tratamos o span como dois projetores de largura nativa lado a
    /// lado — o overlay aplica a overposição de verdade.
    /// </summary>
    public static SurroundPlan? TryCreateFromLogicalSpan(
        SurroundMonitor span,
        int overlap,
        bool swap = false,
        int leftWidth = 0,
        int rightWidth = 0,
        int alignLeftX = 0,
        int alignRightX = 0)
    {
        if (!IsLogicalSpan(span)) return null;
        if (leftWidth <= 0 || rightWidth <= 0)
            (leftWidth, rightWidth) = InferHalfWidths(span);

        if (leftWidth < 640 || rightWidth < 640) return null;

        var left = new SurroundMonitor(
            span.DeviceName + "#L", "Esq", span.Primary,
            span.X, span.Y, leftWidth, span.Height);
        var right = new SurroundMonitor(
            span.DeviceName + "#R", "Dir", false,
            span.X + leftWidth, span.Y, rightWidth, span.Height);

        return TryCreate(new[] { left, right }, overlap, swap, alignLeftX, alignRightX, pinOutput: true);
    }

    public static bool IsLogicalSpan(SurroundMonitor monitor) =>
        monitor.Width >= 2560 && monitor.Height >= 480;

    public static (int Left, int Right) InferHalfWidths(SurroundMonitor span)
    {
        // Dois Full HD é o caso da igreja (3840 sem overlap, 3648 com 192 px).
        if (span.Width >= 3200 && span.Width <= 4000 && span.Height <= 1440)
            return (1920, 1920);

        var half = Math.Max(640, span.Width / 2);
        return (half, span.Width - half);
    }

    /// <summary>
    /// Recoloca as fatias em 1:1 no canvas capturado. A letra no projetor é o
    /// mesmo pixel do monitor virtual — sem esticar, sem pular na junta.
    /// Overlap geométrico = Σ saídas − largura do canvas.
    /// </summary>
    public static IReadOnlyList<SurroundSlice> MapSlicesToCanvas(
        IReadOnlyList<SurroundSlice> slices,
        int canvasWidth,
        int canvasHeight,
        int fadePixels,
        int alignLeftX = 0,
        int alignRightX = 0)
    {
        if (slices.Count == 0 || canvasWidth <= 0) return slices;

        var totalOut = slices.Sum(s => s.OutputWidth);
        var geoOverlap = Math.Max(0, totalOut - canvasWidth);
        var fade = Math.Clamp(fadePixels, 0, slices.Min(s => Math.Max(1, s.OutputWidth / 2)));

        var list = new List<SurroundSlice>(slices.Count);
        for (var i = 0; i < slices.Count; i++)
        {
            var s = slices[i];
            var align = i == 0 ? alignLeftX : i == slices.Count - 1 ? alignRightX : 0;
            var edge = BlendEdge.None;
            if (fade > 0)
            {
                if (i > 0) edge |= BlendEdge.Left;
                if (i < slices.Count - 1) edge |= BlendEdge.Right;
            }

            // 1:1 com o canvas: esquerda cola em X=0, direita cola na borda
            // direita do Holyrics. O overlap é o pedaço que as duas copiam.
            int srcX;
            if (i == 0)
                srcX = align;
            else if (i == slices.Count - 1)
                srcX = canvasWidth - s.OutputWidth + align;
            else
                srcX = slices.Take(i).Sum(x => x.OutputWidth) - geoOverlap * i + align;

            list.Add(s with
            {
                SourceX = srcX,
                SourceY = s.SourceY,
                SourceWidth = s.OutputWidth,
                SourceHeight = canvasHeight > 0 ? canvasHeight : s.SourceHeight,
                BlendEdge = edge,
                BlendPixels = fade,
            });
        }

        return list;
    }

    /// <summary>
    /// Blit 1:1: origem recortada ao canvas e destino sem esticar. Esticar a
    /// fatia (DrawImage em retângulo cheio) deslocava a letra na junta.
    /// </summary>
    public static (int DestX, int DestY, int SrcX, int SrcY, int SrcW, int SrcH)? BlitRect(
        int sourceX, int sourceY, int sourceW, int sourceH,
        int canvasW, int canvasH)
    {
        var srcX = sourceX;
        var srcY = sourceY;
        var srcW = sourceW;
        var srcH = sourceH;
        var destX = 0;
        var destY = 0;

        if (srcX < 0)
        {
            destX -= srcX;
            srcW += srcX;
            srcX = 0;
        }

        if (srcY < 0)
        {
            destY -= srcY;
            srcH += srcY;
            srcY = 0;
        }

        if (srcX + srcW > canvasW) srcW = canvasW - srcX;
        if (srcY + srcH > canvasH) srcH = canvasH - srcY;
        if (srcW <= 0 || srcH <= 0) return null;
        return (destX, destY, srcX, srcY, srcW, srcH);
    }

    private static List<SurroundSlice> BuildSlices(
        IReadOnlyList<SurroundMonitor> ordered,
        int canvasHeight,
        int overlap,
        int alignLeftX,
        int alignRightX,
        bool pinOutput)
    {
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

            var align = i == 0 ? alignLeftX : i == ordered.Count - 1 ? alignRightX : 0;
            slices.Add(new SurroundSlice(
                m.DeviceName,
                m.X, m.Y, m.Width, m.Height,
                sourceX + align, 0, m.Width, canvasHeight,
                edge, overlap, pinOutput));

            sourceX += m.Width - overlap;
        }

        return slices;
    }

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

        var span = new SurroundMonitor(@"\\.\DISPLAY1", "Telão", true, 0, 0, 3840, 1080);
        if (TryCreate(new[] { span }, 192) is not null)
            return "1 monitor físico não deveria gerar plano";
        var halves = TryCreateFromLogicalSpan(span, 192);
        if (halves is null) return "span 3840 deveria virar 2 fatias";
        if (halves.CanvasWidth != 3648) return $"span canvas {halves.CanvasWidth}, esperado 3648";
        if (!halves.Slices[0].PinOutput || !halves.Slices[1].PinOutput)
            return "metades do span precisam de PinOutput (senão as duas janelas viram 3840)";
        if (halves.Slices[0].OutputWidth != 1920 || halves.Slices[1].OutputX != 1920)
            return "metades do span não são 1920+1920";
        if (halves.Slices[1].SourceX != 1728)
            return $"span fatia direita src={halves.Slices[1].SourceX}, esperado 1728";

        var mapped = MapSlicesToCanvas(halves.Slices, 3648, 1080, 192);
        if (mapped[0].SourceX != 0 || mapped[1].SourceX != 1728)
            return $"MapSlicesToCanvas 3648 desalinhado ({mapped[0].SourceX},{mapped[1].SourceX})";
        if (mapped[0].SourceX + mapped[0].SourceWidth - mapped[1].SourceX != 192)
            return "MapSlicesToCanvas não compartilhou 192 px";

        var mosaic = MapSlicesToCanvas(halves.Slices, 3840, 1080, 64);
        if (mosaic[1].SourceX != 1920)
            return "canvas 3840 (Mosaic sem overlap) deve cortar em 1920";
        if (mosaic[0].BlendPixels != 64 || !mosaic[0].BlendEdge.HasFlag(BlendEdge.Right))
            return "faixa branca: o fade tem que existir mesmo sem pixels compartilhados";
        if (mosaic[1].BlendPixels != 64 || !mosaic[1].BlendEdge.HasFlag(BlendEdge.Left))
            return "fade da direita na junta";

        var aligned = MapSlicesToCanvas(halves.Slices, 3648, 1080, 192, alignLeftX: 0, alignRightX: -16);
        if (aligned[1].SourceX != 1712)
            return $"alinhamento direito {aligned[1].SourceX}, esperado 1712";

        var blit = BlitRect(1728, 0, 1920, 1080, 3648, 1080);
        if (blit is not { DestX: 0, SrcX: 1728, SrcW: 1920 })
            return $"BlitRect 1:1 falhou: {blit}";
        var clipped = BlitRect(-10, 0, 1920, 1080, 3648, 1080);
        if (clipped is not { DestX: 10, SrcX: 0, SrcW: 1910 })
            return "BlitRect deveria deslocar destino quando SourceX é negativo, sem esticar";

        if (IsLogicalSpan(left)) return "1920×1080 não é span";
        if (!IsLogicalSpan(span)) return "3840×1080 deveria ser span";

        return null;
    }
}
