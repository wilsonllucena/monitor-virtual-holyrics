using System.Runtime.InteropServices;
using MonitorVirtual.Core.Interop;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.Core.Display;

public sealed record DisplayAdapter(
    string DeviceName,
    string DeviceString,
    string DeviceId,
    bool Attached,
    bool Primary,
    string? MonitorName)
{
    public bool IsVirtual =>
        DeviceId.Contains("MTTVDD", StringComparison.OrdinalIgnoreCase) ||
        DeviceString.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase);
}

public sealed record DisplayGeometry(int X, int Y, int Width, int Height, int RefreshRate);

/// <summary>Enumeração e ajuste da topologia de vídeo (posição, modo, primário, estender).</summary>
public sealed class DisplayService
{
    public IReadOnlyList<DisplayAdapter> ListAdapters()
    {
        var result = new List<DisplayAdapter>();
        var dd = new User32Display.DISPLAY_DEVICE
        {
            cb = (uint)Marshal.SizeOf<User32Display.DISPLAY_DEVICE>(),
        };

        for (uint i = 0; User32Display.EnumDisplayDevicesW(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & User32Display.DISPLAY_DEVICE_MIRRORING_DRIVER) == 0)
            {
                result.Add(new DisplayAdapter(
                    dd.DeviceName,
                    dd.DeviceString,
                    dd.DeviceID,
                    (dd.StateFlags & User32Display.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0,
                    (dd.StateFlags & User32Display.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    GetMonitorName(dd.DeviceName)));
            }

            dd = new User32Display.DISPLAY_DEVICE
            {
                cb = (uint)Marshal.SizeOf<User32Display.DISPLAY_DEVICE>(),
            };
        }

        return result;
    }

    public DisplayAdapter? FindVirtual() => ListAdapters().FirstOrDefault(a => a.IsVirtual);

    public DisplayAdapter? FindPrimary() => ListAdapters().FirstOrDefault(a => a.Primary);

    /// <summary>Monitores físicos ligados, sem o virtual e sem driver de espelho.</summary>
    public IReadOnlyList<SurroundMonitor> ListPhysical()
    {
        var result = new List<SurroundMonitor>();
        foreach (var adapter in ListAdapters().Where(a => a.Attached && !a.IsVirtual))
        {
            var geo = GetGeometry(adapter.DeviceName);
            if (geo is null) continue;

            var label = string.IsNullOrWhiteSpace(adapter.MonitorName)
                ? adapter.DeviceString
                : adapter.MonitorName;

            result.Add(new SurroundMonitor(
                adapter.DeviceName,
                label,
                adapter.Primary,
                geo.X, geo.Y, geo.Width, geo.Height));
        }

        return result;
    }

    private static string? GetMonitorName(string adapterDeviceName)
    {
        var mon = new User32Display.DISPLAY_DEVICE
        {
            cb = (uint)Marshal.SizeOf<User32Display.DISPLAY_DEVICE>(),
        };

        return User32Display.EnumDisplayDevicesW(adapterDeviceName, 0, ref mon, 0)
            ? mon.DeviceString
            : null;
    }

    public DisplayGeometry? GetGeometry(string adapterDeviceName)
    {
        var dm = User32Display.NewDevMode();
        if (!User32Display.EnumDisplaySettingsExW(
                adapterDeviceName, User32Display.ENUM_CURRENT_SETTINGS, ref dm, 0))
            return null;

        return new DisplayGeometry(
            dm.dmPositionX, dm.dmPositionY, (int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency);
    }

    /// <summary>Aplica a topologia "Estender" (equivalente a Win+P → Estender).</summary>
    public bool ApplyExtendTopology()
    {
        var rc = User32Display.SetDisplayConfig(
            0, IntPtr.Zero, 0, IntPtr.Zero,
            User32Display.SDC_TOPOLOGY_EXTEND | User32Display.SDC_APPLY |
            User32Display.SDC_ALLOW_PATH_ORDER_CHANGES);

        if (rc != 0) Log.Warn($"SetDisplayConfig(EXTEND) retornou {rc}.");
        return rc == 0;
    }

    /// <summary>Verifica se todos os monitores ligados estão em posições distintas (ou seja, estendido).</summary>
    public bool IsExtended()
    {
        var attached = ListAdapters().Where(a => a.Attached).ToList();
        if (attached.Count < 2) return false;

        var positions = attached
            .Select(a => GetGeometry(a.DeviceName))
            .Where(g => g is not null)
            .Select(g => (g!.X, g.Y))
            .ToList();

        return positions.Distinct().Count() == positions.Count;
    }

    /// <summary>
    /// Se dois monitores físicos estão no mesmo ponto (clone/espelho), coloca-os
    /// lado a lado. Sem isso o Windows manda o mesmo quadro nos dois projetores.
    /// </summary>
    public bool ArrangeSideBySide(IReadOnlyList<SurroundMonitor> selected)
    {
        if (selected.Count < 2) return true;

        var current = selected
            .Select(m => (Monitor: m, Geo: GetGeometry(m.DeviceName)))
            .Where(t => t.Geo is not null)
            .ToList();

        if (current.Count < 2) return false;

        var positions = current.Select(t => (t.Geo!.X, t.Geo.Y)).Distinct().Count();
        if (positions == current.Count)
            return true; // já estão em coordenadas distintas (estendido)

        var ordered = current
            .OrderBy(t => t.Monitor.Primary ? 0 : 1)
            .ThenBy(t => t.Monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var anchor = ordered[0].Geo!;
        var x = anchor.X;
        var y = anchor.Y;
        var ok = true;

        foreach (var item in ordered)
        {
            var geo = item.Geo!;
            if (geo.X != x || geo.Y != y)
            {
                if (!ApplyPosition(item.Monitor.DeviceName, x, y))
                    ok = false;
            }

            x += geo.Width;
        }

        return ok && Commit();
    }

    private bool ApplyPosition(string adapterDeviceName, int x, int y)
    {
        var dm = User32Display.NewDevMode();
        if (!User32Display.EnumDisplaySettingsExW(
                adapterDeviceName, User32Display.ENUM_CURRENT_SETTINGS, ref dm, 0))
            return false;

        if (dm.dmPositionX == x && dm.dmPositionY == y) return true;

        dm.dmPositionX = x;
        dm.dmPositionY = y;
        dm.dmFields = User32Display.DM_POSITION;

        var rc = User32Display.ChangeDisplaySettingsExW(
            adapterDeviceName, ref dm, IntPtr.Zero,
            User32Display.CDS_UPDATEREGISTRY | User32Display.CDS_NORESET, IntPtr.Zero);

        if (rc != User32Display.DISP_CHANGE_SUCCESSFUL)
        {
            Log.Warn($"ChangeDisplaySettingsEx(posição {adapterDeviceName} → {x},{y}) = {rc}.");
            return false;
        }

        return true;
    }

    /// <summary>Define resolução, taxa e posição do monitor virtual (aplicado em lote).</summary>
    public bool ApplyMode(string adapterDeviceName, int width, int height, int refreshRate, int x, int y)
    {
        var dm = User32Display.NewDevMode();
        if (!User32Display.EnumDisplaySettingsExW(
                adapterDeviceName, User32Display.ENUM_CURRENT_SETTINGS, ref dm, 0))
        {
            Log.Warn($"EnumDisplaySettings falhou para {adapterDeviceName}.");
            return false;
        }

        if (dm.dmPelsWidth == (uint)width && dm.dmPelsHeight == (uint)height &&
            dm.dmDisplayFrequency == (uint)refreshRate && dm.dmPositionX == x && dm.dmPositionY == y)
            return true; // já está como queremos

        dm.dmPelsWidth = (uint)width;
        dm.dmPelsHeight = (uint)height;
        dm.dmDisplayFrequency = (uint)refreshRate;
        dm.dmPositionX = x;
        dm.dmPositionY = y;
        dm.dmFields = User32Display.DM_PELSWIDTH | User32Display.DM_PELSHEIGHT |
                      User32Display.DM_DISPLAYFREQUENCY | User32Display.DM_POSITION;

        var rc = User32Display.ChangeDisplaySettingsExW(
            adapterDeviceName, ref dm, IntPtr.Zero,
            User32Display.CDS_UPDATEREGISTRY | User32Display.CDS_NORESET, IntPtr.Zero);

        if (rc != User32Display.DISP_CHANGE_SUCCESSFUL)
        {
            Log.Warn($"ChangeDisplaySettingsEx({adapterDeviceName}, {width}x{height}@{refreshRate}) = {rc}.");
            return false;
        }

        return Commit();
    }

    /// <summary>
    /// Tira o adaptador do desktop (DEVMODE 0×0). Usado no Surround NVIDIA para o
    /// IddCx não aparecer como segundo monitor ao lado do telão único.
    /// </summary>
    public bool Detach(string adapterDeviceName)
    {
        var dm = User32Display.NewDevMode();
        if (!User32Display.EnumDisplaySettingsExW(
                adapterDeviceName, User32Display.ENUM_CURRENT_SETTINGS, ref dm, 0))
        {
            // já desconectado
            return true;
        }

        dm.dmPelsWidth = 0;
        dm.dmPelsHeight = 0;
        dm.dmPositionX = 0;
        dm.dmPositionY = 0;
        dm.dmFields = User32Display.DM_POSITION | User32Display.DM_PELSWIDTH | User32Display.DM_PELSHEIGHT;

        var rc = User32Display.ChangeDisplaySettingsExW(
            adapterDeviceName, ref dm, IntPtr.Zero,
            User32Display.CDS_UPDATEREGISTRY | User32Display.CDS_NORESET, IntPtr.Zero);

        if (rc != User32Display.DISP_CHANGE_SUCCESSFUL)
        {
            Log.Warn($"ChangeDisplaySettingsEx(detach {adapterDeviceName}) = {rc}.");
            return false;
        }

        return Commit();
    }

    /// <summary>
    /// Coloca os monitores em fila a partir de (originX, originY), sem sobrepor o canvas.
    /// No fallback de overlay o virtual fica em (0,0) e os projetores ao lado — as janelas
    /// de fatia continuam achando cada um pelo DeviceName.
    /// </summary>
    public bool ArrangeInRow(IReadOnlyList<SurroundMonitor> monitors, int originX, int originY)
    {
        if (monitors.Count == 0) return true;

        var ordered = monitors
            .OrderBy(t => t.X)
            .ThenBy(t => t.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var x = originX;
        var already = true;
        foreach (var m in ordered)
        {
            var geo = GetGeometry(m.DeviceName);
            if (geo is null || geo.X != x || geo.Y != originY) already = false;
            x += geo?.Width ?? m.Width;
        }

        if (already) return true;

        x = originX;
        var ok = true;
        foreach (var m in ordered)
        {
            if (!ApplyPosition(m.DeviceName, x, originY))
                ok = false;
            var geo = GetGeometry(m.DeviceName);
            x += geo?.Width ?? m.Width;
        }

        return ok && Commit();
    }

    /// <summary>O maior monitor físico ligado — no Surround NVIDIA é o telão único.</summary>
    public (DisplayAdapter Adapter, DisplayGeometry Geometry)? FindLargestPhysical()
    {
        (DisplayAdapter Adapter, DisplayGeometry Geometry)? best = null;
        foreach (var adapter in ListAdapters().Where(a => a.Attached && !a.IsVirtual))
        {
            var geo = GetGeometry(adapter.DeviceName);
            if (geo is null) continue;
            if (best is null || geo.Width * geo.Height > best.Value.Geometry.Width * best.Value.Geometry.Height)
                best = (adapter, geo);
        }

        return best;
    }

    /// <summary>Torna outro monitor o primário, mantendo-o na origem (0,0).</summary>
    public bool MakePrimary(string adapterDeviceName)
    {
        var adapters = ListAdapters();
        var current = adapters.FirstOrDefault(a =>
            string.Equals(a.DeviceName, adapterDeviceName, StringComparison.OrdinalIgnoreCase));
        if (current is { Primary: true })
        {
            var geo = GetGeometry(adapterDeviceName);
            if (geo is { X: 0, Y: 0 }) return true;
        }

        var dm = User32Display.NewDevMode();
        if (!User32Display.EnumDisplaySettingsExW(
                adapterDeviceName, User32Display.ENUM_CURRENT_SETTINGS, ref dm, 0))
            return false;

        dm.dmPositionX = 0;
        dm.dmPositionY = 0;
        dm.dmFields = User32Display.DM_POSITION;

        var rc = User32Display.ChangeDisplaySettingsExW(
            adapterDeviceName, ref dm, IntPtr.Zero,
            User32Display.CDS_UPDATEREGISTRY | User32Display.CDS_NORESET | User32Display.CDS_SET_PRIMARY,
            IntPtr.Zero);

        if (rc != User32Display.DISP_CHANGE_SUCCESSFUL)
        {
            Log.Warn($"CDS_SET_PRIMARY em {adapterDeviceName} = {rc}.");
            return false;
        }

        return Commit();
    }

    /// <summary>Aplica as mudanças acumuladas com CDS_NORESET.</summary>
    public bool Commit()
    {
        var rc = User32Display.ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (rc != User32Display.DISP_CHANGE_SUCCESSFUL && rc != User32Display.DISP_CHANGE_RESTART)
        {
            Log.Warn($"Commit de display retornou {rc}.");
            return false;
        }

        return true;
    }
}
