using System.Text;
using System.Text.Json;
using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Holyrics;

public sealed record HolyricsStatus(bool ProcessRunning, bool ApiReachable, string? Detail);

public sealed record HolyricsNdiOutput(string? Id, string? Name, bool Enabled, bool TransparentBackground);

public sealed record HolyricsNdiFixResult(
    int Changed,
    int AlreadyOpaque,
    string? Error,
    IReadOnlyList<HolyricsNdiOutput> Outputs)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Integração com a API local do Holyrics (Configurações → API Server, porta 8091).
/// Além do status, ajusta a saída NDI: o Holyrics 2.29+ publica só a camada de texto
/// com <c>transparent_background=true</c>, e o Resolume mostra xadrez/preto no lugar
/// do papel de fundo.
/// </summary>
public sealed class HolyricsClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly ManagedApp Probe = new() { Name = "Holyrics", ProcessName = "Holyrics" };

    public static bool IsRunning() => AppLauncher.IsRunning(Probe);

    public static string? Autodetect() => AppLauncher.FindExecutable("Holyrics.exe");

    public async Task<HolyricsStatus> GetStatusAsync(AppConfig cfg, CancellationToken ct = default)
    {
        var running = IsRunning();
        if (string.IsNullOrWhiteSpace(cfg.HolyricsApiToken))
            return new HolyricsStatus(running, false, "Token da API não configurado.");

        try
        {
            using var doc = await PostAsync(cfg, "GetDisplaySettings", "{}", ct).ConfigureAwait(false);
            if (doc is null)
                return new HolyricsStatus(running, false, "API não respondeu.");

            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            return new HolyricsStatus(running, status == "ok", status ?? "resposta inesperada");
        }
        catch (Exception ex)
        {
            return new HolyricsStatus(running, false, ex.Message);
        }
    }

    /// <summary>Lista as saídas NDI do Holyrics (v2.29+).</summary>
    public async Task<IReadOnlyList<HolyricsNdiOutput>> ListNdiAsync(AppConfig cfg, CancellationToken ct = default)
    {
        using var doc = await PostAsync(cfg, "GetNDISettingsList", "{}", ct).ConfigureAwait(false);
        if (doc is null) return Array.Empty<HolyricsNdiOutput>();

        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "ok")
            return Array.Empty<HolyricsNdiOutput>();

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<HolyricsNdiOutput>();

        var list = new List<HolyricsNdiOutput>();
        foreach (var item in data.EnumerateArray())
        {
            list.Add(new HolyricsNdiOutput(
                item.TryGetProperty("id", out var id) ? id.GetString() : null,
                item.TryGetProperty("name", out var name) ? name.GetString() : null,
                ReadBool(item, "enabled", defaultValue: true),
                ReadBool(item, "transparent_background", defaultValue: false)));
        }

        return list;
    }

    /// <summary>
    /// Desliga o fundo transparente nas saídas NDI ativas. Sem isto o Resolume recebe
    /// só a letra com alpha (preview em xadrez, composition preta).
    /// </summary>
    public async Task<HolyricsNdiFixResult> EnsureOpaqueNdiBackgroundAsync(
        AppConfig cfg, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfg.HolyricsApiToken))
            return new HolyricsNdiFixResult(0, 0, "Token da API não configurado.", Array.Empty<HolyricsNdiOutput>());

        IReadOnlyList<HolyricsNdiOutput> outputs;
        try
        {
            outputs = await ListNdiAsync(cfg, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new HolyricsNdiFixResult(0, 0, ex.Message, Array.Empty<HolyricsNdiOutput>());
        }

        if (outputs.Count == 0)
            return new HolyricsNdiFixResult(0, 0, "Nenhuma saída NDI encontrada no Holyrics (exige v2.29+).", outputs);

        var changed = 0;
        var already = 0;

        foreach (var output in outputs)
        {
            if (!output.Enabled)
                continue;

            if (!output.TransparentBackground)
            {
                already++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(output.Id) && string.IsNullOrWhiteSpace(output.Name))
                continue;

            try
            {
                var payload = BuildSetNdiPayload(output, transparentBackground: false);
                using var doc = await PostAsync(cfg, "SetNDISettings", payload, ct).ConfigureAwait(false);
                if (doc is null)
                    return new HolyricsNdiFixResult(changed, already,
                        $"Falha ao atualizar '{output.Name ?? output.Id}'.", outputs);

                var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (status != "ok")
                    return new HolyricsNdiFixResult(changed, already,
                        $"Holyrics recusou o ajuste de '{output.Name ?? output.Id}': {status}.", outputs);

                changed++;
                Log.Info($"NDI do Holyrics '{output.Name ?? output.Id}': fundo transparente desligado.");
            }
            catch (Exception ex)
            {
                return new HolyricsNdiFixResult(changed, already, ex.Message, outputs);
            }
        }

        return new HolyricsNdiFixResult(changed, already, null, outputs);
    }

    private static string BuildSetNdiPayload(HolyricsNdiOutput output, bool transparentBackground)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(output.Id))
                writer.WriteString("id", output.Id);
            else
                writer.WriteString("name", output.Name);
            writer.WriteBoolean("transparent_background", transparentBackground);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<JsonDocument?> PostAsync(
        AppConfig cfg, string method, string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.HolyricsApiToken))
            return null;

        var url = $"http://localhost:{cfg.HolyricsApiPort}/api/{method}?token={cfg.HolyricsApiToken}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} em {method}: {TrimBody(body)}");

        return JsonDocument.Parse(body);
    }

    private static bool ReadBool(JsonElement item, string name, bool defaultValue)
    {
        if (!item.TryGetProperty(name, out var value)) return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue,
        };
    }

    private static string TrimBody(string body)
    {
        var t = body.Replace('\n', ' ').Trim();
        return t.Length <= 160 ? t : t[..160];
    }
}
