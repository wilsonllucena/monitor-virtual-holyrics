namespace MonitorVirtual.Core;

/// <summary>Locais fixos usados pelo app, serviço e instalador.</summary>
public static class AppPaths
{
    /// <summary>Pasta onde o executável está instalado.</summary>
    public static string InstallDir => AppContext.BaseDirectory.TrimEnd('\\');

    /// <summary>Payload do driver (MttVDD.inf/.cat/.dll) que acompanha o instalador.</summary>
    public static string DriverPayloadDir => Path.Combine(InstallDir, "driver");

    public static string DriverInfPath => Path.Combine(DriverPayloadDir, "MttVDD.inf");

    /// <summary>Dados por máquina (config, logs, settings do driver).</summary>
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MonitorVirtual");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    /// <summary>Pasta apontada por HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver\VDDPATH.</summary>
    public static string DriverConfigDir => Path.Combine(DataDir, "driver-config");

    public static string VddSettingsFile => Path.Combine(DriverConfigDir, "vdd_settings.xml");

    public static string LogDir => Path.Combine(DataDir, "logs");

    public static void EnsureDataDirs()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(DriverConfigDir);
        Directory.CreateDirectory(LogDir);
        EnsureUsersCanWrite();
    }

    /// <summary>
    /// %ProgramData% dá só leitura para usuários comuns, e o app roda elevado — sem isto,
    /// a CLI e os diagnósticos rodados sem elevação não conseguem gravar config nem log.
    /// Concede "Modificar" ao grupo Usuários (SID S-1-5-32-545, independente de idioma).
    /// Seguro: os programas configurados aqui são iniciados sem elevação, via explorer.exe.
    /// </summary>
    private static void EnsureUsersCanWrite()
    {
        var marker = Path.Combine(DataDir, ".permissions");
        if (File.Exists(marker)) return;
        if (!Elevation.IsElevated()) return;

        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = $"\"{DataDir}\" /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            p?.WaitForExit(20000);
            File.WriteAllText(marker, DateTime.Now.ToString("O"));
        }
        catch
        {
            // sem permissão de escrita o app ainda funciona elevado; não vale derrubar nada
        }
    }
}
