using System.Runtime.InteropServices;
using System.Text;

namespace MonitorVirtual.Core.Interop;

/// <summary>
/// P/Invoke de setupapi/newdev/cfgmgr32 — criação e controle do nó de dispositivo
/// root-enumerated do driver de vídeo indireto (o mesmo que o nefconw faz).
/// </summary>
internal static class SetupApi
{
    internal static readonly Guid GUID_DEVCLASS_DISPLAY = new("4D36E968-E325-11CE-BFC1-08002BE10318");

    internal const uint DIGCF_PRESENT = 0x02;
    internal const uint DICD_GENERATE_ID = 0x01;

    internal const uint DIF_REMOVE = 0x05;
    internal const uint DIF_PROPERTYCHANGE = 0x12;
    internal const uint DIF_REGISTERDEVICE = 0x19;

    internal const uint DICS_ENABLE = 0x01;
    internal const uint DICS_DISABLE = 0x02;
    internal const uint DICS_FLAG_GLOBAL = 0x01;

    internal const uint SPDRP_DEVICEDESC = 0x00;
    internal const uint SPDRP_HARDWAREID = 0x01;
    internal const uint SPDRP_FRIENDLYNAME = 0x0C;

    internal const uint INSTALLFLAG_FORCE = 0x01;

    internal const uint SPOST_NONE = 0;
    internal const uint SP_COPY_NEWER_ONLY = 0x0004;

    // cfgmgr32 status
    internal const uint DN_HAS_PROBLEM = 0x00000400;
    internal const uint CM_PROB_DISABLED = 22;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_CLASSINSTALL_HEADER
    {
        public uint cbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_REMOVEDEVICE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiCreateDeviceInfoW(
        IntPtr DeviceInfoSet,
        string DeviceName,
        ref Guid ClassGuid,
        string? DeviceDescription,
        IntPtr hwndParent,
        uint CreationFlags,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiSetDeviceRegistryPropertyW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        byte[] PropertyBuffer,
        uint PropertyBufferSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiCallClassInstaller(
        uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiSetClassInstallParamsW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        ref SP_PROPCHANGE_PARAMS ClassInstallParams,
        int ClassInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiSetClassInstallParamsW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        ref SP_REMOVEDEVICE_PARAMS ClassInstallParams,
        int ClassInstallParamsSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupCopyOEMInfW(
        string SourceInfFileName,
        string? OEMSourceMediaLocation,
        uint OEMSourceMediaType,
        uint CopyStyle,
        StringBuilder? DestinationInfFileName,
        uint DestinationInfFileNameSize,
        out uint RequiredSize,
        IntPtr DestinationInfFileNameComponent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupUninstallOEMInfW(string InfFileName, uint Flags, IntPtr Reserved);

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool UpdateDriverForPlugAndPlayDevicesW(
        IntPtr hwndParent,
        string HardwareId,
        string FullInfPath,
        uint InstallFlags,
        out bool bRebootRequired);

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool DiUninstallDevice(
        IntPtr hwndParent,
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Flags,
        out bool NeedReboot);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    internal static extern int CM_Get_DevNode_Status(
        out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

    internal static byte[] ToMultiSz(params string[] values)
    {
        var sb = new StringBuilder();
        foreach (var v in values)
        {
            sb.Append(v);
            sb.Append('\0');
        }

        sb.Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }

    internal static string[] FromMultiSz(byte[] buffer, int byteCount)
    {
        var raw = Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0');
        return raw.Length == 0
            ? Array.Empty<string>()
            : raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
