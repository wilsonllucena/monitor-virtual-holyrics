using System.Text;
using System.Text.Json;
using MonitorVirtual.Core.Apps;
using MonitorVirtual.Core.Config;

namespace MonitorVirtual.Core.Holyrics;

public sealed record HolyricsStatus(bool ProcessRunning, bool ApiReachable, string? Detail);

/// <summary>
/// Integração específica do Holyrics: consulta a API local (Configurações → API Server,
/// porta padrão 8091) só para exibir status. A API não expõe qual monitor é a tela
/// pública — esse vínculo é feito uma vez no assistente do Holyrics.
/// O ciclo de vida do processo é tratado pelo <see cref="AppLauncher"/>.
/// </summary>
public sealed class HolyricsClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

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
            var url = $"http://localhost:{cfg.HolyricsApiPort}/api/GetDisplaySettings?token={cfg.HolyricsApiToken}";
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return new HolyricsStatus(running, false, $"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            return new HolyricsStatus(running, status == "ok", status ?? "resposta inesperada");
        }
        catch (Exception ex)
        {
            return new HolyricsStatus(running, false, ex.Message);
        }
    }
}
