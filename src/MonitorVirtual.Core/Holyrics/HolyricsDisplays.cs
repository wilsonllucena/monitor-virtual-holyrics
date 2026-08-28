using System.Globalization;
using System.Text;
using System.Text.Json;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.Core.Holyrics;

public sealed record HolyricsDisplayScreen(
    string Id,
    string? Name,
    string? Screen,
    bool Hide,
    int? AreaX,
    int? AreaY,
    int? AreaW,
    int? AreaH)
{
    public bool IsPhysicalOutput =>
        Id.Equals("public", StringComparison.OrdinalIgnoreCase) ||
        Id.StartsWith("screen_", StringComparison.OrdinalIgnoreCase);

    public int OriginX => AreaX ?? ParseScreen(Screen).X;
    public int OriginY => AreaY ?? ParseScreen(Screen).Y;

    public static (int X, int Y) ParseScreen(string? screen)
    {
        if (string.IsNullOrWhiteSpace(screen)) return (int.MinValue, int.MinValue);
        var parts = screen.Split(',');
        if (parts.Length < 2) return (int.MinValue, int.MinValue);
        return int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
               int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            ? (x, y)
            : (int.MinValue, int.MinValue);
    }
}

public sealed record HolyricsDisplayFixResult(
    bool PublicRetargeted,
    int HiddenScreens,
    string? Error,
    string? Detail)
{
    public bool Ok => Error is null;
    public bool Changed => PublicRetargeted || HiddenScreens > 0;
}

public sealed partial class HolyricsClient
{
    public async Task<IReadOnlyList<HolyricsDisplayScreen>> ListDisplaysAsync(
        AppConfig cfg, CancellationToken ct = default)
    {
        using var doc = await PostAsync(cfg, "GetDisplaySettings", "{}", ct).ConfigureAwait(false);
        return doc is null ? Array.Empty<HolyricsDisplayScreen>() : ParseDisplays(doc.RootElement);
    }

    /// <summary>
    /// Tela pública no canvas virtual; oculta screen_2/3… que estão em cima dos projetores.
    /// Sem isto o Holyrics abre em 2 telas físicas e o telão fica dividido.
    /// </summary>
    public async Task<HolyricsDisplayFixResult> EnsureSinglePublicScreenAsync(
        AppConfig cfg,
        int virtX, int virtY, int virtW, int virtH,
        IReadOnlyList<SurroundMonitor> projectors,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfg.HolyricsApiToken))
            return new HolyricsDisplayFixResult(false, 0, "Token da API não configurado.", null);

        IReadOnlyList<HolyricsDisplayScreen> screens;
        try
        {
            screens = await ListDisplaysAsync(cfg, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new HolyricsDisplayFixResult(false, 0, ex.Message, null);
        }

        if (screens.Count == 0)
            return new HolyricsDisplayFixResult(false, 0, "Holyrics não devolveu telas (API Server ligada?).", null);

        var publicScreen = screens.FirstOrDefault(s =>
            s.Id.Equals("public", StringComparison.OrdinalIgnoreCase));
        if (publicScreen is null)
            return new HolyricsDisplayFixResult(false, 0, "Tela pública não encontrada no Holyrics.", null);

        var retargeted = false;
        if (!MatchesOrigin(publicScreen, virtX, virtY) || publicScreen.Hide)
        {
            var payload = BuildPublicPayload(virtX, virtY, virtW, virtH);
            var error = await PostSettingsAsync(cfg, payload, "pública", ct).ConfigureAwait(false);
            if (error is not null)
            {
                var minimal = $"{{\"id\":\"public\",\"hide\":false,\"screen\":\"{virtX},{virtY}\"}}";
                error = await PostSettingsAsync(cfg, minimal, "pública", ct).ConfigureAwait(false);
            }
            if (error is not null)
                return new HolyricsDisplayFixResult(false, 0, error, Describe(screens));
            retargeted = true;
            Log.Info($"Holyrics: Tela pública apontada para o canvas virtual ({virtX},{virtY} {virtW}x{virtH}).");
        }

        var hidden = 0;
        foreach (var screen in screens)
        {
            if (!screen.Id.StartsWith("screen_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (screen.Hide) continue;
            if (!OverlapsProjector(screen, projectors)) continue;

            var payload = $"{{\"id\":{JsonString(screen.Id)},\"hide\":true}}";
            var error = await PostSettingsAsync(cfg, payload, screen.Id, ct).ConfigureAwait(false);
            if (error is not null)
                return new HolyricsDisplayFixResult(retargeted, hidden, error, Describe(screens));
            hidden++;
            Log.Info($"Holyrics: '{screen.Name ?? screen.Id}' ocultada (caía no projetor).");
        }

        var detail = retargeted || hidden > 0
            ? $"pública={virtX},{virtY} {virtW}x{virtH}; ocultas={hidden}"
            : "já estava no canvas único";
        return new HolyricsDisplayFixResult(retargeted, hidden, null, detail);
    }

    internal static IReadOnlyList<HolyricsDisplayScreen> ParseDisplays(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<HolyricsDisplayScreen>();

        var list = new List<HolyricsDisplayScreen>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            ReadRect(item, "total_area", out var tx, out var ty, out var tw, out var th);
            if (tx is null)
                ReadRect(item, "area", out tx, out ty, out tw, out th);

            list.Add(new HolyricsDisplayScreen(
                id,
                ReadString(item, "name"),
                ReadString(item, "screen"),
                ReadBool(item, "hide", defaultValue: false),
                tx, ty, tw, th));
        }

        return list;
    }

    public static string? SelfTestParse()
    {
        const string json = """
            {"status":"ok","data":[
              {"id":"public","name":"Público","screen":"3840,0","hide":false,
               "area":{"x":3840,"y":0,"width":3648,"height":1080},
               "total_area":{"x":3840,"y":0,"width":3648,"height":1080}},
              {"id":"screen_2","name":"Tela 2","hide":false,
               "area":{"x":0,"y":0,"width":1920,"height":1080}},
              {"id":"stream_image","name":"Stream"}
            ]}
            """;
        using var doc = JsonDocument.Parse(json);
        var list = ParseDisplays(doc.RootElement);
        if (list.Count != 3) return $"parseou {list.Count} telas, esperado 3";
        if (list[0].OriginX != 3840 || list[0].AreaW != 3648) return "pública com área errada";
        if (!list[1].IsPhysicalOutput || list[2].IsPhysicalOutput) return "classificação stream/tela";

        var projector = new SurroundMonitor(@"\\.\DISPLAY1", "Proj", true, 0, 0, 1920, 1080);
        if (!OverlapsProjector(list[1], new[] { projector }))
            return "screen_2 deveria coincidir com o projetor";
        if (OverlapsProjector(list[0], new[] { projector }))
            return "pública no virtual não deveria coincidir com o projetor";
        return null;
    }

    private async Task<string?> PostSettingsAsync(
        AppConfig cfg, string payload, string label, CancellationToken ct)
    {
        using var doc = await PostAsync(cfg, "SetDisplaySettings", payload, ct).ConfigureAwait(false);
        if (doc is null) return $"Falha ao atualizar a tela {label}.";
        var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        return status == "ok" ? null : $"Holyrics recusou a tela {label}: {status}.";
    }

    private static string BuildPublicPayload(int x, int y, int w, int h) =>
        "{\"id\":\"public\",\"hide\":false," +
        $"\"screen\":\"{x},{y}\"," +
        $"\"total_area\":{{\"x\":{x},\"y\":{y},\"width\":{w},\"height\":{h}}}}}";

    internal static bool MatchesOrigin(HolyricsDisplayScreen screen, int x, int y, int slop = 24)
    {
        if (screen.Hide) return false;
        var (sx, sy) = HolyricsDisplayScreen.ParseScreen(screen.Screen);
        if (sx != int.MinValue && Math.Abs(sx - x) <= slop && Math.Abs(sy - y) <= slop)
            return true;
        if (screen.AreaX is int ax && screen.AreaY is int ay)
            return Math.Abs(ax - x) <= slop && Math.Abs(ay - y) <= slop;
        return false;
    }

    internal static bool OverlapsProjector(
        HolyricsDisplayScreen screen, IReadOnlyList<SurroundMonitor> projectors, int slop = 48)
    {
        if (projectors.Count == 0) return false;
        var x = screen.OriginX;
        var y = screen.OriginY;
        if (x == int.MinValue) return false;

        var w = screen.AreaW ?? 0;
        var h = screen.AreaH ?? 0;

        foreach (var p in projectors)
        {
            if (Math.Abs(x - p.X) <= slop && Math.Abs(y - p.Y) <= slop)
                return true;
            if (w > 0 && h > 0 && RectsOverlap(x, y, w, h, p.X, p.Y, p.Width, p.Height))
                return true;
        }

        return false;
    }

    private static bool RectsOverlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
        ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    private static string Describe(IReadOnlyList<HolyricsDisplayScreen> screens) =>
        string.Join("; ", screens.Select(s =>
            $"{s.Id}@{(s.AreaX is int x ? $"{x},{s.AreaY}" : s.Screen ?? "?")} hide={s.Hide}"));

    private static string JsonString(string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            writer.WriteStringValue(value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)value.GetDouble(),
            _ => null,
        };
    }

    private static void ReadRect(
        JsonElement item, string name, out int? x, out int? y, out int? w, out int? h)
    {
        x = y = w = h = null;
        if (!item.TryGetProperty(name, out var rect) || rect.ValueKind != JsonValueKind.Object)
            return;
        x = ReadInt(rect, "x");
        y = ReadInt(rect, "y");
        w = ReadInt(rect, "width");
        h = ReadInt(rect, "height");
    }
}
