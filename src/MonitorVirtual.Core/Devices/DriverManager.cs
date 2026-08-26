using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MonitorVirtual.Core.Interop;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Devices;

public sealed record DeviceStatus(bool Present, bool Enabled, uint ProblemCode, string? Description)
{
    public static readonly DeviceStatus Absent = new(false, false, 0, null);
}

/// <summary>
/// Instala/remove/habilita o nó de dispositivo root-enumerated do driver de vídeo indireto.
/// Todas as operações exigem processo elevado.
/// </summary>
public sealed class DriverManager
{
    /// <summary>Hardware ID declarado no MttVDD.inf.</summary>
    public const string HardwareId = @"Root\MttVDD";

    private static string OemInfMarkerFile => Path.Combine(AppPaths.DriverConfigDir, "oem-inf.txt");

    public DeviceStatus GetStatus()
    {
        var set = SetupApi.SetupDiGetClassDevsW(
            ref Unsafe_DisplayClass, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return DeviceStatus.Absent;

        try
        {
            if (!TryFindDevice(set, out var devInfo)) return DeviceStatus.Absent;

            var description = GetStringProperty(set, ref devInfo, SetupApi.SPDRP_FRIENDLYNAME)
                              ?? GetStringProperty(set, ref devInfo, SetupApi.SPDRP_DEVICEDESC);

            var enabled = true;
            uint problem = 0;
            if (SetupApi.CM_Get_DevNode_Status(out var status, out problem, devInfo.DevInst, 0) == 0)
            {
                enabled = (status & SetupApi.DN_HAS_PROBLEM) == 0 || problem != SetupApi.CM_PROB_DISABLED;
            }

            return new DeviceStatus(true, enabled, problem, description);
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Instala o pacote de driver e cria o nó de dispositivo, se ainda não existir.</summary>
    public bool Install(string infPath, out bool rebootRequired)
    {
        rebootRequired = false;
        if (!File.Exists(infPath))
            throw new FileNotFoundException($"INF do driver não encontrado: {infPath}", infPath);

        AppPaths.EnsureDataDirs();
        StageInf(infPath);

        if (!GetStatus().Present)
        {
            CreateDeviceNode();
            Log.Info($"Nó de dispositivo criado para {HardwareId}.");
        }

        var ok = SetupApi.UpdateDriverForPlugAndPlayDevicesW(
            IntPtr.Zero, HardwareId, infPath, SetupApi.INSTALLFLAG_FORCE, out rebootRequired);

        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            // ERROR_NO_SUCH_DEVINST (0xE000020B) acontece quando o nó ainda não apareceu; o PnP
            // termina a instalação sozinho na sequência.
            Log.Warn($"UpdateDriverForPlugAndPlayDevices retornou erro {err} (0x{err:X}).");
            return GetStatus().Present;
        }

        Log.Info($"Driver instalado a partir de {infPath}. Reboot necessário: {rebootRequired}.");
        return true;
    }

    /// <summary>Habilita ou desabilita o dispositivo — é isto que liga/desliga o monitor virtual.</summary>
    public bool SetEnabled(bool enabled)
    {
        var set = SetupApi.SetupDiGetClassDevsW(
            ref Unsafe_DisplayClass, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return false;

        try
        {
            if (!TryFindDevice(set, out var devInfo)) return false;

            var pcp = new SetupApi.SP_PROPCHANGE_PARAMS
            {
                ClassInstallHeader = new SetupApi.SP_CLASSINSTALL_HEADER
                {
                    cbSize = (uint)Marshal.SizeOf<SetupApi.SP_CLASSINSTALL_HEADER>(),
                    InstallFunction = SetupApi.DIF_PROPERTYCHANGE,
                },
                StateChange = enabled ? SetupApi.DICS_ENABLE : SetupApi.DICS_DISABLE,
                Scope = SetupApi.DICS_FLAG_GLOBAL,
                HwProfile = 0,
            };

            if (!SetupApi.SetupDiSetClassInstallParamsW(
                    set, ref devInfo, ref pcp, Marshal.SizeOf<SetupApi.SP_PROPCHANGE_PARAMS>()))
            {
                Log.Error($"SetupDiSetClassInstallParams falhou: {new Win32Exception().Message}");
                return false;
            }

            if (!SetupApi.SetupDiCallClassInstaller(SetupApi.DIF_PROPERTYCHANGE, set, ref devInfo))
            {
                Log.Error($"DIF_PROPERTYCHANGE falhou: {new Win32Exception().Message}");
                return false;
            }

            Log.Info(enabled ? "Dispositivo habilitado." : "Dispositivo desabilitado.");
            return true;
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Reinicia o dispositivo — necessário para o driver reler o vdd_settings.xml.</summary>
    public bool Restart()
    {
        if (!SetEnabled(false)) return false;
        Thread.Sleep(700);
        return SetEnabled(true);
    }

    /// <summary>Remove o nó de dispositivo e (opcionalmente) o pacote de driver do DriverStore.</summary>
    public bool Uninstall(bool removeDriverPackage, out bool rebootRequired)
    {
        rebootRequired = false;
        var set = SetupApi.SetupDiGetClassDevsW(
            ref Unsafe_DisplayClass, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT);

        if (set != IntPtr.Zero && set != new IntPtr(-1))
        {
            try
            {
                if (TryFindDevice(set, out var devInfo))
                {
                    if (!SetupApi.DiUninstallDevice(IntPtr.Zero, set, ref devInfo, 0, out rebootRequired))
                        Log.Error($"DiUninstallDevice falhou: {new Win32Exception().Message}");
                    else
                        Log.Info("Nó de dispositivo removido.");
                }
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(set);
            }
        }

        if (removeDriverPackage && File.Exists(OemInfMarkerFile))
        {
            var oemInf = File.ReadAllText(OemInfMarkerFile).Trim();
            if (!string.IsNullOrWhiteSpace(oemInf))
            {
                if (SetupApi.SetupUninstallOEMInfW(oemInf, 0, IntPtr.Zero))
                    Log.Info($"Pacote de driver {oemInf} removido do DriverStore.");
                else
                    Log.Warn($"Não foi possível remover {oemInf}: {new Win32Exception().Message}");
            }
        }

        return true;
    }

    private static void StageInf(string infPath)
    {
        var dest = new StringBuilder(512);
        if (!SetupApi.SetupCopyOEMInfW(
                infPath,
                Path.GetDirectoryName(Path.GetFullPath(infPath)),
                SetupApi.SPOST_NONE,
                SetupApi.SP_COPY_NEWER_ONLY,
                dest,
                (uint)dest.Capacity,
                out _,
                IntPtr.Zero))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"Falha ao registrar o pacote de driver (SetupCopyOEMInf, erro {err}).");
        }

        var oemName = Path.GetFileName(dest.ToString());
        try
        {
            File.WriteAllText(OemInfMarkerFile, oemName);
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível registrar o nome do OEM INF: {ex.Message}");
        }

        Log.Info($"Pacote de driver registrado como {oemName}.");
    }

    private static void CreateDeviceNode()
    {
        var classGuid = SetupApi.GUID_DEVCLASS_DISPLAY;
        var set = SetupApi.SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiCreateDeviceInfoList falhou.");

        try
        {
            var devInfo = new SetupApi.SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>(),
            };

            if (!SetupApi.SetupDiCreateDeviceInfoW(
                    set, "Display", ref classGuid, null, IntPtr.Zero, SetupApi.DICD_GENERATE_ID, ref devInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiCreateDeviceInfo falhou.");

            var hwid = SetupApi.ToMultiSz(HardwareId);
            if (!SetupApi.SetupDiSetDeviceRegistryPropertyW(
                    set, ref devInfo, SetupApi.SPDRP_HARDWAREID, hwid, (uint)hwid.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao definir o HardwareID.");

            if (!SetupApi.SetupDiCallClassInstaller(SetupApi.DIF_REGISTERDEVICE, set, ref devInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DIF_REGISTERDEVICE falhou.");
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static Guid Unsafe_DisplayClass = SetupApi.GUID_DEVCLASS_DISPLAY;

    private static bool TryFindDevice(IntPtr set, out SetupApi.SP_DEVINFO_DATA found)
    {
        found = default;
        var devInfo = new SetupApi.SP_DEVINFO_DATA
        {
            cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>(),
        };

        for (uint i = 0; SetupApi.SetupDiEnumDeviceInfo(set, i, ref devInfo); i++)
        {
            var ids = GetMultiStringProperty(set, ref devInfo, SetupApi.SPDRP_HARDWAREID);
            if (ids.Any(id => string.Equals(id, HardwareId, StringComparison.OrdinalIgnoreCase)))
            {
                found = devInfo;
                return true;
            }

            devInfo = new SetupApi.SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>(),
            };
        }

        return false;
    }

    private static string[] GetMultiStringProperty(IntPtr set, ref SetupApi.SP_DEVINFO_DATA devInfo, uint prop)
    {
        var buffer = new byte[2048];
        if (!SetupApi.SetupDiGetDeviceRegistryPropertyW(
                set, ref devInfo, prop, out _, buffer, (uint)buffer.Length, out var required))
            return Array.Empty<string>();

        return SetupApi.FromMultiSz(buffer, (int)Math.Min(required, (uint)buffer.Length));
    }

    private static string? GetStringProperty(IntPtr set, ref SetupApi.SP_DEVINFO_DATA devInfo, uint prop)
    {
        var values = GetMultiStringProperty(set, ref devInfo, prop);
        return values.Length > 0 ? values[0] : null;
    }
}
