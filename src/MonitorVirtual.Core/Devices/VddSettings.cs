using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Devices;

/// <summary>
/// Gera o vdd_settings.xml consumido pelo driver e aponta o driver para a nossa pasta
/// através de HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver\VDDPATH — assim não mexemos
/// em C:\VirtualDisplayDriver e não brigamos com outra instalação do VDD.
/// </summary>
public static class VddSettings
{
    private const string RegistryKey = @"SOFTWARE\MikeTheTech\VirtualDisplayDriver";
    private const string RegistryValue = "VDDPATH";

    /// <summary>Taxas de atualização sempre publicadas, além da escolhida pelo usuário.</summary>
    private static readonly int[] BaseRefreshRates = { 30, 60 };

    public static void EnsureRegistryPath()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryKey, writable: true);
            key?.SetValue(RegistryValue, AppPaths.DriverConfigDir, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao gravar VDDPATH no registro (o driver usará C:\\VirtualDisplayDriver)", ex);
        }
    }

    /// <summary>Escreve o XML. Retorna true se o conteúdo mudou (exige reinício do dispositivo).</summary>
    public static bool Write(int monitorCount, int width, int height, int refreshRate, bool logging = false)
    {
        AppPaths.EnsureDataDirs();

        var xml = Build(monitorCount, width, height, refreshRate, logging);
        var previous = File.Exists(AppPaths.VddSettingsFile)
            ? File.ReadAllText(AppPaths.VddSettingsFile, Encoding.UTF8)
            : null;

        if (string.Equals(previous?.Replace("\r\n", "\n"), xml.Replace("\r\n", "\n"), StringComparison.Ordinal))
            return false;

        File.WriteAllText(AppPaths.VddSettingsFile, xml, new UTF8Encoding(false));
        Log.Info($"vdd_settings.xml atualizado ({monitorCount} monitor(es), {width}x{height}@{refreshRate}).");
        return true;
    }

    private static string Build(int monitorCount, int width, int height, int refreshRate, bool logging)
    {
        var rates = BaseRefreshRates.Append(refreshRate).Distinct().OrderBy(r => r).ToArray();

        // Além da resolução alvo, publicamos algumas comuns: se o operador trocar a resolução
        // pelas Configurações do Windows, o modo continua disponível.
        var resolutions = new (int W, int H)[]
        {
            (1280, 720),
            (1366, 768),
            (1600, 900),
            (1920, 1080),
            (2560, 1440),
            (3456, 1080),
            (3584, 1080),
            (3648, 1080),
            (3840, 1080),
            (3840, 2160),
            (width, height),
        }.Distinct().OrderBy(r => r.W).ThenBy(r => r.H).ToArray();

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("vdd_settings",
                new XElement("monitors", new XElement("count", monitorCount)),
                new XElement("gpu", new XElement("friendlyname", "default")),
                new XElement("global", rates.Select(r => new XElement("g_refresh_rate", r))),
                new XElement("resolutions",
                    resolutions.Select(r => new XElement("resolution",
                        new XElement("width", r.W),
                        new XElement("height", r.H),
                        new XElement("refresh_rate", refreshRate)))),
                new XElement("options",
                    new XElement("CustomEdid", false),
                    new XElement("PreventSpoof", false),
                    new XElement("EdidCeaOverride", false),
                    new XElement("HardwareCursor", true),
                    new XElement("SDR10bit", false),
                    new XElement("HDRPlus", false),
                    new XElement("logging", logging),
                    new XElement("debuglogging", false))));

        return doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.None) + Environment.NewLine;
    }
}
