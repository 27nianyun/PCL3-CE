using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PCL.Core.Utils.OS;

namespace PCL;

/// <summary>
/// 内存优化：回收托管内存并把进程已占用的物理内存归还给系统。
/// </summary>
/// <remarks>
/// 本模块为 PCL₃-CE 新增功能（迁移自 PCL Living Fork「工具百宝箱 · 内存优化」的功能构思，
/// 按照 PCL₃-CE 的代码风格重新实现，未复制其源代码）。
/// 实现上只使用 Windows 公开 API：GC 强制回收 + EmptyWorkingSet 清空工作集，
/// 不修改任何游戏进程的内存，也不会结束其他程序。
/// </remarks>
public static class ModMemoryOptimize
{
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr minWorkingSetSize,
        IntPtr maxWorkingSetSize, uint flags);

    /// <summary>某一时刻的内存快照（单位：字节）。</summary>
    public readonly record struct MemorySnapshot(long ProcessBytes, long TotalBytes, long AvailableBytes);

    /// <summary>一次优化的结果。</summary>
    public readonly record struct MemoryResult(MemorySnapshot Before, MemorySnapshot After, bool WorkingSetTrimmed)
    {
        /// <summary>启动器自身释放的物理内存（可能为负，表示回收期间又有新分配）。</summary>
        public long ProcessReleasedBytes => Before.ProcessBytes - After.ProcessBytes;

        /// <summary>系统可用物理内存的增量。</summary>
        public long AvailableGainedBytes => After.AvailableBytes - Before.AvailableBytes;
    }

    /// <summary>读取当前启动器进程与系统的内存占用。</summary>
    public static MemorySnapshot GetSnapshot()
    {
        long processBytes = 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            processBytes = process.WorkingSet64;
        }
        catch
        {
            // 拿不到进程信息时保持 0，不影响后续展示
        }

        long total = 0;
        long available = 0;
        try
        {
            var (totalBytes, availableBytes) = KernelInterop.GetPhysicalMemoryBytes();
            total = (long)totalBytes;
            available = (long)availableBytes;
        }
        catch
        {
            // 同上
        }

        return new MemorySnapshot(processBytes, total, available);
    }

    /// <summary>
    /// 执行一次内存优化。该操作可能耗时数十毫秒，请在后台线程调用。
    /// </summary>
    public static MemoryResult Optimize()
    {
        var before = GetSnapshot();

        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        }
        catch
        {
            // GC 失败不影响工作集回收
        }

        var trimmed = false;
        try
        {
            using var process = Process.GetCurrentProcess();
            var handle = process.Handle;
            if (EmptyWorkingSet(handle)) trimmed = true;
            // 传 -1 表示由系统重新计算工作集上下限，效果与任务管理器的“精简工作集”一致
            if (SetProcessWorkingSetSizeEx(handle, new IntPtr(-1), new IntPtr(-1), 0)) trimmed = true;
        }
        catch
        {
            // 权限不足等情况直接跳过，仍然返回 GC 之后的统计
        }

        return new MemoryResult(before, GetSnapshot(), trimmed);
    }

    /// <summary>把字节数格式化为便于阅读的字符串。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
