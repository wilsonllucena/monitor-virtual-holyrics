using System.Diagnostics;
using System.Security.Principal;

namespace MonitorVirtual.Core;

public static class Elevation
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Relança o processo pedindo elevação. Retorna false se o usuário recusar.</summary>
    public static bool RelaunchElevated(string? arguments = null)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
