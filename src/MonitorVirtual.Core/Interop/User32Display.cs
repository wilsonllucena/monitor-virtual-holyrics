using System.Runtime.InteropServices;

namespace MonitorVirtual.Core.Interop;

/// <summary>P/Invoke de user32 para enumerar, posicionar e aplicar topologia de monitores.</summary>
internal static class User32Display
{
    internal const int ENUM_CURRENT_SETTINGS = -1;

    // DEVMODE.dmFields
    internal const uint DM_POSITION = 0x00000020;
    internal const uint DM_BITSPERPEL = 0x00040000;
    internal const uint DM_PELSWIDTH = 0x00080000;
    internal const uint DM_PELSHEIGHT = 0x00100000;
    internal const uint DM_DISPLAYFREQUENCY = 0x00400000;

    // ChangeDisplaySettingsEx flags
    internal const uint CDS_UPDATEREGISTRY = 0x00000001;
    internal const uint CDS_SET_PRIMARY = 0x00000010;
    internal const uint CDS_NORESET = 0x10000000;

    // retorno de ChangeDisplaySettingsEx
    internal const int DISP_CHANGE_SUCCESSFUL = 0;
    internal const int DISP_CHANGE_RESTART = 1;

    // DISPLAY_DEVICE.StateFlags
    internal const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    internal const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    internal const uint DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008;

    // SetDisplayConfig flags
    internal const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    internal const uint SDC_TOPOLOGY_CLONE = 0x00000002;
    internal const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    internal const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
    internal const uint SDC_APPLY = 0x00000080;
    internal const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000;

    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    internal const uint QDC_DATABASE_CURRENT = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public uint cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevicesW(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplaySettingsExW(
        string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsExW(
        string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsExW(
        string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern int SetDisplayConfig(
        uint numPathArrayElements,
        IntPtr pathArray,
        uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    internal static DEVMODE NewDevMode() => new()
    {
        dmDeviceName = string.Empty,
        dmFormName = string.Empty,
        dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
    };
}
