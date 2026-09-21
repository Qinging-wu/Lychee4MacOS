using System.Runtime.InteropServices;
using Lychee.Core;

namespace Lychee.Platform;

/// <summary>
/// Whole-system CPU busy/idle tick counters.
/// - macOS: host_statistics(HOST_CPU_LOAD_INFO) via mach. HOST_CPU_LOAD_INFO
///   belongs to the host_statistics() flavor namespace (= 3 in XNU
///   osfmk/mach/host_info.h) — NOT the host_info() namespace, and NOT
///   host_statistics64().
/// - Windows: kernel32 GetSystemTimes (kernel time already includes idle).
/// Returns accumulated tick totals; callers compute deltas over time.
/// </summary>
public static class SystemCpuTimes
{
    public static bool TryRead(out long idle, out long total)
    {
        if (OperatingSystem.IsMacOS()) return TryReadMac(out idle, out total);
        if (OperatingSystem.IsWindows()) return TryReadWindows(out idle, out total);
        idle = 0;
        total = 0;
        return false;
    }

    private static bool TryReadWindows(out long idle, out long total)
    {
        idle = 0;
        total = 0;
        try
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return false;

            var i = (long)(((ulong)idleFt.dwHighDateTime << 32) | idleFt.dwLowDateTime);
            var k = (long)(((ulong)kernelFt.dwHighDateTime << 32) | kernelFt.dwLowDateTime);
            var u = (long)(((ulong)userFt.dwHighDateTime << 32) | userFt.dwLowDateTime);

            idle = i;
            total = k + u; // Windows kernel time includes idle
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadMac(out long idle, out long total)
    {
        idle = 0;
        total = 0;
        try
        {
            var host = mach_host_self();
            if (host == 0) return false;

            // host_cpu_load_info_data_t: cpu_ticks[CPU_STATE_MAX] = 4 integers
            // (CPU_STATE_USER=0, SYSTEM=1, IDLE=2, NICE=3 in osfmk/mach/machine.h)
            var ticks = new int[4];
            var count = ticks.Length;
            var kr = host_statistics(host, HostCpuLoadInfo, ticks, ref count);
            if (kr != 0)
            {
                AppLog.Error("cpu",
                    $"host_statistics(HOST_CPU_LOAD_INFO) failed: kern_return={kr}, count={count}");
                return false;
            }

            var user = (long)(uint)ticks[0];
            var system = (long)(uint)ticks[1];
            var i = (long)(uint)ticks[2];
            var nice = (long)(uint)ticks[3];

            idle = i;
            total = user + system + i + nice;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("cpu", ex);
            return false;
        }
    }

    // XNU osfmk/mach/host_info.h — host_statistics() flavors
    private const int HostCpuLoadInfo = 3;

    [DllImport("libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics(uint host, int flavor, int[] info, ref int count);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
}
