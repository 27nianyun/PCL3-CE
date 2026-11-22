using System.IO;
using System.IO.Compression;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     Java 版存档备份与还原。
///     备份文件位置：&lt;实例目录&gt;PCL\SaveBackup\&lt;存档名&gt;\&lt;时间戳&gt;.zip。
///     本模块由 PCL₃-CE 独立实现（功能思路参考 PCL Living Fork 的存档快照，不含其源代码）。
/// </summary>
public static class ModSaveBackup
{
    /// <summary>单个存档最多保留的备份份数下限。</summary>
    private const int MinKeep = 1;

    /// <summary>单个存档最多保留的备份份数上限。</summary>
    private const int MaxKeep = 100;

    /// <summary>取实例的存档目录（不含结尾斜杠）。</summary>
    public static string GetSavesFolder(McInstance instance)
    {
        return Path.Combine(instance.PathIndie, "saves");
    }

    /// <summary>取实例的备份根目录（不含结尾斜杠）。</summary>
    public static string GetBackupRoot(McInstance instance)
    {
        return Path.Combine(instance.PathInstance, "PCL", "SaveBackup");
    }

    /// <summary>取某个存档的备份目录（不含结尾斜杠）。</summary>
    public static string GetBackupFolder(McInstance instance, string worldName)
    {
        return Path.Combine(GetBackupRoot(instance), worldName);
    }

    /// <summary>当前生效的保留份数。</summary>
    public static int KeepCount => Math.Clamp(Config.Launch.SaveBackupKeep, MinKeep, MaxKeep);

    /// <summary>
    ///     备份单个存档。返回生成的 zip 路径，失败或跳过时返回 null。
    /// </summary>
    /// <param name="instance">实例</param>
    /// <param name="worldFolder">存档文件夹的完整路径</param>
    /// <param name="onlyIfChanged">若该存档自上次备份后未修改（以 level.dat 判断），则跳过</param>
    public static string? BackupWorld(McInstance instance, string worldFolder, bool onlyIfChanged = false)
    {
        try
        {
            if (!Directory.Exists(worldFolder))
                return null;
            var savesFolder = Path.GetFullPath(GetSavesFolder(instance));
            var fullWorld = Path.GetFullPath(worldFolder);
            // 安全校验：只允许备份 saves 目录下的存档，避免路径穿越
            if (!fullWorld.StartsWithF(savesFolder + Path.DirectorySeparatorChar, true))
                return null;

            var worldName = Path.GetFileName(fullWorld.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(worldName))
                return null;

            var targetFolder = GetBackupFolder(instance, worldName);
            // 未修改则跳过
            if (onlyIfChanged && !HasWorldChanged(fullWorld, targetFolder))
            {
                ModBase.Log("[Backup] 存档 " + worldName + " 自上次备份后无变化，跳过");
                return null;
            }

            Directory.CreateDirectory(targetFolder);
            var zipPath = Path.Combine(targetFolder,
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip");
            if (File.Exists(zipPath))
                zipPath = Path.Combine(targetFolder,
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".zip");

            ZipFile.CreateFromDirectory(fullWorld, zipPath, CompressionLevel.Optimal, false);
            ModBase.Log("[Backup] 已备份存档 " + worldName + " → " + zipPath);

            PruneBackups(targetFolder);
            return zipPath;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Backup] 备份存档失败：" + worldFolder);
            // 清理可能残留的半成品文件
            TryDeleteBrokenLatest(instance, worldFolder);
            return null;
        }
    }

    /// <summary>
    ///     备份实例下的全部存档。返回成功备份的数量。
    /// </summary>
    /// <param name="instance">实例</param>
    /// <param name="onlyIfChanged">仅备份自上次备份后有变化的存档</param>
    /// <param name="showHint">是否在结束时弹出提示</param>
    public static int BackupAllSaves(McInstance instance, bool onlyIfChanged = true, bool showHint = false)
    {
        var count = 0;
        var savesFolder = GetSavesFolder(instance);
        if (!Directory.Exists(savesFolder))
        {
            if (showHint) HintService.Hint(Lang.Text("Instance.Saves.Backup.NoSaves"), HintType.Info);
            return 0;
        }

        foreach (var world in Directory.GetDirectories(savesFolder))
            if (BackupWorld(instance, world, onlyIfChanged) is not null)
                count++;

        if (showHint)
            HintService.Hint(count > 0
                ? Lang.Text("Instance.Saves.Backup.Done", count)
                : Lang.Text("Instance.Saves.Backup.NothingToDo"), HintType.Info);
        return count;
    }

    /// <summary>枚举某个存档的全部备份（按时间倒序）。</summary>
    public static List<string> ListBackups(McInstance instance, string worldName)
    {
        var folder = GetBackupFolder(instance, worldName);
        if (!Directory.Exists(folder))
            return new List<string>();
        return Directory.GetFiles(folder, "*.zip")
            .OrderByDescending(f => Path.GetFileName(f))
            .ToList();
    }

    /// <summary>取最近一次的备份路径，没有则返回 null。</summary>
    public static string? GetLatestBackup(McInstance instance, string worldName)
    {
        return ListBackups(instance, worldName).FirstOrDefault();
    }

    /// <summary>
    ///     将指定备份还原到存档目录。还原前会把当前存档改名保存为 .restore-old-时间戳，避免数据彻底丢失。
    /// </summary>
    public static bool RestoreBackup(McInstance instance, string worldName, string zipPath,
        bool showHint = true)
    {
        try
        {
            if (!File.Exists(zipPath))
                return false;
            var savesFolder = GetSavesFolder(instance);
            Directory.CreateDirectory(savesFolder);
            var worldFolder = Path.Combine(savesFolder, worldName);

            if (Directory.Exists(worldFolder))
            {
                var oldFolder = worldFolder + ".restore-old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try
                {
                    Directory.Move(worldFolder, oldFolder);
                    ModBase.Log("[Backup] 原存档已改名保存：" + oldFolder);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "[Backup] 无法改名原存档，将直接覆盖");
                }
            }

            ZipFile.ExtractToDirectory(zipPath, worldFolder, true);
            ModBase.Log("[Backup] 已还原存档 " + worldName + " ← " + zipPath);
            if (showHint)
                HintService.Hint(Lang.Text("Instance.Saves.Backup.RestoreDone", worldName), HintType.Success);
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Backup] 还原存档失败：" + zipPath);
            if (showHint)
                HintService.Hint(Lang.Text("Instance.Saves.Backup.RestoreFailed"), HintType.Error);
            return false;
        }
    }

    /// <summary>删除某个备份文件。</summary>
    public static void DeleteBackup(string zipPath)
    {
        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Backup] 删除备份失败：" + zipPath);
        }
    }

    /// <summary>备份占用的总字节数。</summary>
    public static long GetBackupSize(McInstance instance)
    {
        try
        {
            var root = GetBackupRoot(instance);
            if (!Directory.Exists(root))
                return 0;
            return Directory.GetFiles(root, "*.zip", SearchOption.AllDirectories).Sum(f =>
            {
                try
                {
                    return new FileInfo(f).Length;
                }
                catch (Exception)
                {
                    return 0L;
                }
            });
        }
        catch (Exception)
        {
            return 0L;
        }
    }

    /// <summary>
    ///     取实例下最近修改过的存档（以 level.dat 的写入时间为准）。没有存档时返回 null。
    /// </summary>
    public static string? GetLatestWorld(McInstance instance)
    {
        try
        {
            var savesFolder = GetSavesFolder(instance);
            if (!Directory.Exists(savesFolder))
                return null;
            return Directory.GetDirectories(savesFolder)
                .Select(dir => new
                {
                    Dir = dir,
                    Time = GetWorldTime(dir)
                })
                .OrderByDescending(x => x.Time)
                .Select(x => x.Dir)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Backup] 查找最近存档失败");
            return null;
        }
    }

    /// <summary>取存档的最后修改时间（优先 level.dat，其次目录本身）。</summary>
    public static DateTime GetWorldTime(string worldFolder)
    {
        try
        {
            var levelDat = Path.Combine(worldFolder, "level.dat");
            if (File.Exists(levelDat))
                return File.GetLastWriteTimeUtc(levelDat);
            return Directory.GetLastWriteTimeUtc(worldFolder);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>判断存档自上次备份后是否发生了修改。</summary>
    private static bool HasWorldChanged(string worldFolder, string backupFolder)
    {
        if (!Directory.Exists(backupFolder))
            return true;
        var latest = Directory.GetFiles(backupFolder, "*.zip")
            .Select(f =>
            {
                try
                {
                    return File.GetLastWriteTimeUtc(f);
                }
                catch (Exception)
                {
                    return DateTime.MinValue;
                }
            })
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return latest == DateTime.MinValue || GetWorldTime(worldFolder) > latest;
    }

    /// <summary>按保留份数清理旧备份。</summary>
    private static void PruneBackups(string backupFolder)
    {
        try
        {
            var keep = KeepCount;
            var files = Directory.GetFiles(backupFolder, "*.zip")
                .OrderByDescending(f => Path.GetFileName(f))
                .Skip(keep)
                .ToList();
            foreach (var file in files)
                try
                {
                    File.Delete(file);
                    ModBase.Log("[Backup] 已清理旧备份：" + file);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "[Backup] 清理旧备份失败：" + file);
                }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Backup] 清理旧备份失败");
        }
    }

    /// <summary>备份失败时尝试删掉刚生成的残缺 zip。</summary>
    private static void TryDeleteBrokenLatest(McInstance instance, string worldFolder)
    {
        try
        {
            var worldName = Path.GetFileName(worldFolder.TrimEnd(Path.DirectorySeparatorChar));
            var latest = GetLatestBackup(instance, worldName);
            if (latest is null)
                return;
            // 只删除 3 分钟内产生的文件，避免误删历史备份
            if (DateTime.Now - File.GetLastWriteTime(latest) < TimeSpan.FromMinutes(3))
                File.Delete(latest);
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
