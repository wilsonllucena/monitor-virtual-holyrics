using System.Runtime.InteropServices;
using System.Text;
using MonitorVirtual.Core.Logging;

namespace MonitorVirtual.Core.Interop;

/// <summary>
/// Carga dinâmica de nvapi64.dll. Sem a DLL (GPU não-NVIDIA) tudo devolve falha
/// silenciosa — o telão cai no canvas virtual, sem quebrar 1 monitor.
/// </summary>
internal static class NvApi
{
    internal const int Ok = 0;
    internal const int NotSupported = -3;
    internal const int NoImplementation = -4;
    internal const int IncompatibleStructVersion = -9;
    internal const int InvalidArgument = -5;
    internal const int NvidiaDeviceNotFound = -6;
    internal const int ExpectedPhysicalGpuHandle = -7;
    internal const int ModeChangeFailed = -149;
    internal const int TopoNotPossible = -221;
    internal const int NoActiveSliTopology = -136;

    internal const int MaxDisplays = 64;
    internal const int MaxPhysicalGpus = 64;
    internal const int ShortStringMax = 64;

    internal const uint MosaicSetTopoCurrentGpu = 1;
    internal const uint MosaicSetTopoNoDriverReload = 2;
    internal const uint MosaicSetTopoAllowInvalid = 8;

    internal const uint MosaicTopoNone = 0;
    internal const uint MosaicTopo1x2Basic = 1;
    internal const uint MosaicTopo2x1Basic = 2;

    internal const uint MosaicTopoTypeAll = 0;
    internal const uint MosaicTopoTypeBasic = 1;

    internal const uint GridFlagBezel = 1;
    internal const uint GridFlagImmersiveGaming = 2;
    internal const uint GridFlagBaseMosaic = 4;
    internal const uint GridFlagDriverReload = 8;

    private const uint IdInitialize = 0x0150E828;
    private const uint IdGetErrorMessage = 0x6C2D048C;
    private const uint IdGetDisplayIdByName = 0xAE457190;
    private const uint IdEnumDisplayGrids = 0xDF2887AF;
    private const uint IdSetDisplayGrids = 0x4D959A89;
    private const uint IdValidateDisplayGrids = 0xCF43903D;
    private const uint IdGetCurrentTopo = 0xEC32944E;
    private const uint IdSetCurrentTopo = 0x9B542831;
    private const uint IdEnableCurrentTopo = 0x5F1AA66C;
    private const uint IdSetScanoutIntensity = 0xA57457A4;

    private static readonly object Gate = new();
    private static IntPtr _lib;
    private static bool _tried;
    private static bool _ready;

    private static InitializeFn? _initialize;
    private static GetErrorMessageFn? _getError;
    private static GetDisplayIdByNameFn? _getDisplayId;
    private static EnumDisplayGridsFn? _enumGrids;
    private static SetDisplayGridsFn? _setGrids;
    private static GetCurrentTopoFn? _getCurrentTopo;
    private static SetCurrentTopoFn? _setCurrentTopo;
    private static EnableCurrentTopoFn? _enableCurrentTopo;
    private static SetScanoutIntensityFn? _setScanout;

    internal static bool IsAvailable
    {
        get
        {
            Ensure();
            return _ready;
        }
    }

    internal static void Ensure()
    {
        lock (Gate)
        {
            if (_tried) return;
            _tried = true;

            try
            {
                if (!NativeLibrary.TryLoad("nvapi64.dll", out _lib))
                {
                    var system = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll");
                    if (!NativeLibrary.TryLoad(system, out _lib))
                        return;
                }

                if (!NativeLibrary.TryGetExport(_lib, "nvapi_QueryInterface", out var qi))
                    return;

                var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(qi);
                _initialize = Bind<InitializeFn>(query, IdInitialize);
                if (_initialize is null || _initialize() != Ok)
                    return;

                _getError = Bind<GetErrorMessageFn>(query, IdGetErrorMessage);
                _getDisplayId = Bind<GetDisplayIdByNameFn>(query, IdGetDisplayIdByName);
                _enumGrids = Bind<EnumDisplayGridsFn>(query, IdEnumDisplayGrids);
                _setGrids = Bind<SetDisplayGridsFn>(query, IdSetDisplayGrids);
                _getCurrentTopo = Bind<GetCurrentTopoFn>(query, IdGetCurrentTopo);
                _setCurrentTopo = Bind<SetCurrentTopoFn>(query, IdSetCurrentTopo);
                _enableCurrentTopo = Bind<EnableCurrentTopoFn>(query, IdEnableCurrentTopo);
                _setScanout = Bind<SetScanoutIntensityFn>(query, IdSetScanoutIntensity);
                _ready = true;
            }
            catch (Exception ex)
            {
                Log.Warn($"NVAPI não carregou: {ex.Message}");
                _ready = false;
            }
        }
    }

    internal static string Error(int status)
    {
        if (_getError is null) return $"NVAPI {status}";
        var buf = new byte[ShortStringMax];
        _ = _getError(status, buf);
        var end = Array.IndexOf(buf, (byte)0);
        var text = Encoding.ASCII.GetString(buf, 0, end < 0 ? buf.Length : end).Trim();
        return string.IsNullOrWhiteSpace(text) ? $"NVAPI {status}" : text;
    }

    internal static uint? DisplayIdFromGdiName(string deviceName)
    {
        if (_getDisplayId is null) return null;
        foreach (var candidate in GdiNameCandidates(deviceName))
        {
            var rc = _getDisplayId(candidate, out var id);
            if (rc == Ok && id != 0) return id;
        }

        return null;
    }

    /// <summary>
    /// O NVAPI documenta "\\DISPLAY1"; o GDI devolve "\\.\DISPLAY1". Tentamos os dois.
    /// </summary>
    internal static IReadOnlyList<string> GdiNameCandidates(string deviceName)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(deviceName)) return list;

        void Add(string value)
        {
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
                list.Add(value);
        }

        Add(deviceName);
        if (deviceName.StartsWith(@"\\.\", StringComparison.Ordinal))
            Add(@"\\" + deviceName[4..]);
        var leaf = deviceName.Replace(@"\\.\", "", StringComparison.Ordinal)
            .Replace(@"\\", "", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(leaf))
        {
            Add(@"\\" + leaf);
            Add(@"\\.\" + leaf);
            Add(leaf);
        }

        return list;
    }

    internal static int EnumGrids(NvMosaicGridTopo[] grids, ref uint count)
    {
        if (_enumGrids is null) return NoImplementation;
        return _enumGrids(grids, ref count);
    }

    internal static int SetGrids(NvMosaicGridTopo[] grids, uint flags)
    {
        if (_setGrids is null) return NoImplementation;
        return _setGrids(grids, (uint)grids.Length, flags);
    }

    internal static int GetCurrentTopo(out NvMosaicTopoBrief brief, out NvMosaicDisplaySettingV1 setting,
        out int overlapX, out int overlapY)
    {
        brief = NvMosaicTopoBrief.New();
        setting = NvMosaicDisplaySettingV1.New();
        overlapX = overlapY = 0;
        if (_getCurrentTopo is null) return NoImplementation;
        return _getCurrentTopo(ref brief, ref setting, out overlapX, out overlapY);
    }

    internal static int SetCurrentTopo(NvMosaicTopoBrief brief, NvMosaicDisplaySettingV1 setting,
        int overlapX, int overlapY, uint enable)
    {
        if (_setCurrentTopo is null) return NoImplementation;
        return _setCurrentTopo(ref brief, ref setting, overlapX, overlapY, enable);
    }

    internal static int EnableCurrentTopo(uint enable)
    {
        if (_enableCurrentTopo is null) return NoImplementation;
        return _enableCurrentTopo(enable);
    }

    internal static int SetScanoutIntensity(uint displayId, IntPtr data, out int sticky)
    {
        sticky = 0;
        if (_setScanout is null) return NoImplementation;
        return _setScanout(displayId, data, out sticky);
    }

    internal static uint MakeVersion(int size, uint version) => (uint)size | (version << 16);

    internal static string? SelfTest()
    {
        var size = Marshal.SizeOf<NvMosaicGridTopo>();
        var disp = Marshal.SizeOf<NvMosaicGridTopoDisplayV2>();
        if (disp != 28) return $"sizeof DISPLAY_V2={disp}, esperado 28";
        if (size < 1800 || size > 1900)
            return $"sizeof GRID_TOPO={size}, fora da faixa do nvapi.h V2 (~1832)";

        var ver = MakeVersion(size, 2);
        if ((ver & 0xFFFF) != (uint)size) return "MakeVersion não gravou o sizeof nos 16 bits baixos";

        var names = GdiNameCandidates(@"\\.\DISPLAY1");
        if (!names.Contains(@"\\DISPLAY1", StringComparer.OrdinalIgnoreCase))
            return "candidato NVAPI \\DISPLAY1 ausente";
        if (!names.Contains(@"\\.\DISPLAY1", StringComparer.OrdinalIgnoreCase))
            return "candidato GDI \\.\\DISPLAY1 ausente";

        var one = GdiNameCandidates(@"\\.\DISPLAY2");
        if (one[0] != @"\\.\DISPLAY2") return "a ordem dos candidatos deve preservar o nome GDI";

        var empty = GdiNameCandidates("");
        if (empty.Count != 0) return "nome vazio não gera candidato";

        return null;
    }

    private static T? Bind<T>(QueryInterfaceFn query, uint id) where T : class
    {
        var ptr = query(id);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryInterfaceFn(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetErrorMessageFn(int status, byte[] message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDisplayIdByNameFn(
        [MarshalAs(UnmanagedType.LPStr)] string name, out uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumDisplayGridsFn(
        [In, Out] NvMosaicGridTopo[]? grids, ref uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetDisplayGridsFn(
        [In] NvMosaicGridTopo[] grids, uint count, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCurrentTopoFn(
        ref NvMosaicTopoBrief brief, ref NvMosaicDisplaySettingV1 setting,
        out int overlapX, out int overlapY);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetCurrentTopoFn(
        ref NvMosaicTopoBrief brief, ref NvMosaicDisplaySettingV1 setting,
        int overlapX, int overlapY, uint enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnableCurrentTopoFn(uint enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetScanoutIntensityFn(uint displayId, IntPtr data, out int sticky);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvMosaicTopoBrief
{
    public uint Version;
    public uint Topo;
    public uint Enabled;
    public uint IsPossible;

    public static NvMosaicTopoBrief New() => new()
    {
        Version = NvApi.MakeVersion(Marshal.SizeOf<NvMosaicTopoBrief>(), 1),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvMosaicDisplaySettingV1
{
    public uint Version;
    public uint Width;
    public uint Height;
    public uint Bpp;
    public uint Freq;

    public static NvMosaicDisplaySettingV1 New() => new()
    {
        Version = NvApi.MakeVersion(Marshal.SizeOf<NvMosaicDisplaySettingV1>(), 1),
        Bpp = 32,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvMosaicGridTopoDisplayV2
{
    public uint Version;
    public uint DisplayId;
    public int OverlapX;
    public int OverlapY;
    public uint Rotation;
    public uint CloneGroup;
    public uint PixelShiftType;

    public static NvMosaicGridTopoDisplayV2 For(uint displayId, int overlapX = 0) => new()
    {
        Version = NvApi.MakeVersion(Marshal.SizeOf<NvMosaicGridTopoDisplayV2>(), 2),
        DisplayId = displayId,
        OverlapX = overlapX,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvMosaicGridTopo
{
    public uint Version;
    public uint Rows;
    public uint Columns;
    public uint DisplayCount;
    public uint Flags;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = NvApi.MaxDisplays)]
    public NvMosaicGridTopoDisplayV2[] Displays;

    public NvMosaicDisplaySettingV1 DisplaySettings;

    public static NvMosaicGridTopo New()
    {
        var topo = new NvMosaicGridTopo
        {
            Version = NvApi.MakeVersion(Marshal.SizeOf<NvMosaicGridTopo>(), 2),
            Displays = new NvMosaicGridTopoDisplayV2[NvApi.MaxDisplays],
            DisplaySettings = NvMosaicDisplaySettingV1.New(),
        };
        for (var i = 0; i < topo.Displays.Length; i++)
            topo.Displays[i].Version = NvApi.MakeVersion(Marshal.SizeOf<NvMosaicGridTopoDisplayV2>(), 2);
        return topo;
    }

    public static NvMosaicGridTopo OneByOne(uint displayId, uint width, uint height, uint freq, uint flags = 0)
    {
        var topo = New();
        topo.Rows = 1;
        topo.Columns = 1;
        topo.DisplayCount = 1;
        topo.Flags = flags;
        topo.Displays[0] = NvMosaicGridTopoDisplayV2.For(displayId);
        topo.DisplaySettings.Width = width;
        topo.DisplaySettings.Height = height;
        topo.DisplaySettings.Freq = freq;
        return topo;
    }

    public static NvMosaicGridTopo OneByTwo(
        uint leftId, uint rightId, uint width, uint height, uint freq, int overlapX, uint flags)
    {
        var topo = New();
        topo.Rows = 1;
        topo.Columns = 2;
        topo.DisplayCount = 2;
        topo.Flags = flags;
        topo.Displays[0] = NvMosaicGridTopoDisplayV2.For(leftId, overlapX);
        topo.Displays[1] = NvMosaicGridTopoDisplayV2.For(rightId);
        topo.DisplaySettings.Width = width;
        topo.DisplaySettings.Height = height;
        topo.DisplaySettings.Freq = freq;
        return topo;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvScanoutIntensityDataV1
{
    public uint Version;
    public uint Width;
    public uint Height;
    public IntPtr BlendingTexture;
}
