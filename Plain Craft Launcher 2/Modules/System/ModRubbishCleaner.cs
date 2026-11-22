using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCL.Core.Utils.OS;

namespace PCL;

/// <summary>可清理垃圾的分类。</summary>
public enum RubbishKind
{
    /// <summary>崩溃报告目录（crash-reports）。</summary>
    CrashReport,

    /// <summary>日志文件与日志目录（logs、*.log、hs_err_pid*）。</summary>
    Log,

    /// <summary>解压出来的 Natives 库文件。</summary>
    Natives,

    /// <summary>启动器本次运行的临时目录。</summary>
    LauncherTemp,

    /// <summary>启动器安装目录下的缓存数据。</summary>
    LauncherCache
}

/// <summary>一条待清理记录。</summary>
public sealed class RubbishItem
{
    public RubbishKind Kind { get; init; }
    public string Path { get; init; } = "";
    public long Bytes { get; init; }
    public int FileCount { get; init; }
    public bool IsDirectory { get; init; }
}

/// <summary>垃圾清理的扫描与执行结果。</summary>
public readonly record struct RubbishResult(int FileCount, long Bytes, int FailedCount);

/// <summary>
/// 扫描并清理 Minecraft 实例与启动器产生的垃圾文件。
/// </summary>
/// <remarks>
/// PCL₃-CE 在原有「清理垃圾」的基础上拆分为「扫描 → 明细 → 清理」两步：
/// 扫描只统计、不删除，让用户先看清要删什么；普通清理只动 Minecraft 侧文件，
/// 不需要重启启动器（原实现因为一并删除启动器缓存而必须重启）。
/// </remarks>
public static class ModRubbishCleaner
{
    /// <summary>扫描所有可清理项。仅统计，不删除任何文件，可安全在后台线程调用。</summary>
    public static List<RubbishItem> Scan()
    {
        var items = new List<RubbishItem>();

        if (!ModFolder.mcFolderList.Any()) ModFolder.mcFolderListLoader.Start();
        ModFolder.mcFolderListLoader.WaitForExit();

        var targets = new List<DirectoryInfo>();
        foreach (var mcFolder in ModFolder.mcFolderList.ToList())
        {
            if (string.IsNullOrWhiteSpace(mcFolder.Location)) continue;
            var root = new DirectoryInfo(mcFolder.Location);
            if (!root.Exists) continue;
            targets.Add(root);
            var versions = new DirectoryInfo(System.IO.Path.Combine(root.FullName, "versions"));
            if (!versions.Exists) continue;
            try
            {
                targets.AddRange(versions.EnumerateDirectories());
            }
            catch
            {
                // 无权限读取的实例跳过
            }
        }

        foreach (var dir in targets)
        {
            var crashReports = System.IO.Path.Combine(dir.FullName, "crash-reports");
            AddDirectory(items, crashReports, RubbishKind.CrashReport);

            var logs = System.IO.Path.Combine(dir.FullName, "logs");
            AddDirectory(items, logs, RubbishKind.Log);

            // 根目录散落的日志文件
            try
            {
                foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    if (!file.Name.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase) &&
                        !file.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                        !file.Name.Equals("WailaErrorOutput.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    items.Add(new RubbishItem
                    {
                        Kind = RubbishKind.Log,
                        Path = file.FullName,
                        Bytes = SafeLength(file),
                        FileCount = 1
                    });
                }
            }
            catch
            {
                // 忽略
            }

            // Natives 解压目录
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    if (!sub.Name.EndsWith("-natives", StringComparison.OrdinalIgnoreCase) &&
                        !sub.Name.Equals("natives-windows-x86_64", StringComparison.OrdinalIgnoreCase)) continue;
                    AddDirectory(items, sub.FullName, RubbishKind.Natives);
                }
            }
            catch
            {
                // 忽略
            }
        }

        AddDirectory(items, ModBase.pathTemp, RubbishKind.LauncherTemp);
        try
        {
            AddDirectory(items, System.IO.Path.Combine(SystemPaths.DriveLetter, "ProgramData", "PCL"),
                RubbishKind.LauncherCache);
        }
        catch
        {
            // 忽略
        }

        return items.Where(i => i.FileCount > 0 || i.Bytes > 0).ToList();
    }

    /// <summary>按扫描结果执行删除，返回实际删除的文件数与释放空间。</summary>
    public static RubbishResult Clean(IEnumerable<RubbishItem> items)
    {
        var fileCount = 0;
        long bytes = 0;
        var failed = 0;

        foreach (var item in items)
        {
            try
            {
                if (item.IsDirectory)
                {
                    if (!Directory.Exists(item.Path)) continue;
                    var (count, size) = MeasureDirectory(item.Path);
                    Directory.Delete(item.Path, true);
                    fileCount += count;
                    bytes += size;
                }
                else
                {
                    if (!File.Exists(item.Path)) continue;
                    var size = SafeLength(new FileInfo(item.Path));
                    File.Delete(item.Path);
                    fileCount++;
                    bytes += size;
                }
            }
            catch
            {
                // 文件被占用等情况：跳过并计数，不中断整体清理
                failed++;
            }
        }

        return new RubbishResult(fileCount, bytes, failed);
    }

    private static void AddDirectory(ICollection<RubbishItem> items, string path, RubbishKind kind)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            var (count, size) = MeasureDirectory(path);
            if (count == 0 && size == 0)
            {
                // 空目录也值得清理，但只在确实存在时记录
                items.Add(new RubbishItem { Kind = kind, Path = path, IsDirectory = true });
                return;
            }

            items.Add(new RubbishItem
            {
                Kind = kind,
                Path = path,
                Bytes = size,
                FileCount = count,
                IsDirectory = true
            });
        }
        catch
        {
            // 忽略
        }
    }

    private static (int Count, long Bytes) MeasureDirectory(string path)
    {
        var count = 0;
        long bytes = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                count++;
                bytes += SafeLength(file);
            }
        }
        catch
        {
            // 部分子目录无权限时保留已统计的部分
        }

        return (count, bytes);
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch
        {
            return 0;
        }
    }
}
