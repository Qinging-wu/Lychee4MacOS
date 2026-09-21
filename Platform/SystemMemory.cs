using System.Runtime.InteropServices;
using Lychee.Core;

namespace Lychee.Platform;

/// <summary>
/// Physical memory stats.
/// - macOS: hw.memsize via sysctl + host_statistics(HOST_VM_INFO) page
///   counters. HOST_VM_INFO belongs to the host_statistics() flavor namespace
///   (= 2 in XNU osfmk/mach/host_info.h) — NOT the host_info() namespace.
///   vm_statistics_data_t is 15 natural_t fields on current XNU (REV0=12),
///   so the buffer must carry at least 15 ints or the kernel rejects the call.
///   "Available" approximates free + inactive pages (speculative pages are
///   already counted inside free_count). macOS has no page file stats — the
///   Detail line omits them (unlike the Windows build).
/// - Windows: kernel32 GlobalMemoryStatusEx (matches the WPF build).
/// </summary>
public static class SystemMemory
{
    public static bool TryRead(out ulong totalBytes, out ulong usedBytes, out ulong availableBytes)
    {
        if (OperatingSystem.IsMacOS()) return TryReadMac(out totalBytes, out usedBytes, out availableBytes);
        if (OperatingSystem.IsWindows()) return TryReadWindows(out totalBytes, out usedBytes, out availableBytes);
        totalBytes = usedBytes = availableBytes = 0;
        return false;
    }

    private static bool TryReadWindows(out ulong totalBytes, out ulong usedBytes, out ulong availableBytes)
    {
        totalBytes = usedBytes = availableBytes = 0;
        try
        {
            var status = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };
            if (!GlobalMemoryStatusEx(ref status))
                return false;

            totalBytes = status.ullTotalPhys;
            availableBytes = status.ullAvailPhys;
            usedBytes = totalBytes - availableBytes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadMac(out ulong totalBytes, out ulong usedBytes, out ulong availableBytes)
    {
        totalBytes = usedBytes = availableBytes = 0;
        try
        {
            var total = SysCtlUInt64("hw.memsize");
            if (total == 0) return false;

            var pageSize = SysCtlUInt64("hw.pagesize");
            if (pageSize == 0) pageSize = 4096;

            var host = mach_host_self();
            if (host == 0) return false;

            // vm_statistics_data_t: 15 natural_t fields on current XNU.
            // Index 0 = free_count, 1 = active_count, 2 = inactive_count, 3 = wire_count.
            var stats = new int[VmStatisticsIntCount];
            var count = stats.Length;
            var kr = host_statistics(host, HostVmInfo, stats, ref count);
            if (kr != 0)
            {
                AppLog.Error("memory",
                    $"host_statistics(HOST_VM_INFO) failed: kern_return={kr}, count={count}");
                return false;
            }

            var freePages = (ulong)(uint)stats[0];
            var inactivePages = (ulong)(uint)stats[2];

            availableBytes = (freePages + inactivePages) * pageSize;
            if (availableBytes > total) availableBytes = total;
            usedBytes = total - availableBytes;
            totalBytes = total;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("memory", ex);
            return false;
        }
    }

    private static ulong SysCtlUInt64(string name)
    {
        try
        {
            var len = IntPtr.Zero;
            if (sysctlbyname(name, IntPtr.Zero, ref len, IntPtr.Zero, IntPtr.Zero) != 0 || len == IntPtr.Zero)
                return 0;

            var size = (int)len;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var len2 = new IntPtr(size);
                if (sysctlbyname(name, buffer, ref len2, IntPtr.Zero, IntPtr.Zero) != 0)
                    return 0;

                return size >= 8
                    ? (ulong)Marshal.ReadInt64(buffer)
                    : (ulong)(uint)Marshal.ReadInt32(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return 0;
        }
    }

    // XNU osfmk/mach/host_info.h — host_statistics() flavors
    private const int HostVmInfo = 2;

    // sizeof(vm_statistics_data_t) / sizeof(integer_t) = 15 on current XNU
    private const int VmStatisticsIntCount = 15;

    [DllImport("libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics(uint host, int flavor, int[] info, ref int count);

    [DllImport("libSystem.dylib")]
    private static extern int sysctlbyname(
        string name, IntPtr oldp, ref IntPtr oldlen, IntPtr newp, IntPtr newlen);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
