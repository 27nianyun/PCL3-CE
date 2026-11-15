using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     Symbio Edition「问题板块」核心逻辑：
///     1. 优化记录（含撤销）——记录每次一键优化执行的动作，支持一键撤销（反向操作）。
///     2. 报错记录快捷查看 + 自动总结——扫描实例的 crash-reports / logs，本地规则总结原因。
///     3. 备份——选择实例与备份地址，完整复制实例文件夹（带时间戳）。
///     全部为纯新增，不依赖 PCL 内部私有 API，配置独立存放于 Paths.Data。
/// </summary>
public static class SymbioIssues
{
    #region 配置与持久化

    private static readonly object FileLock = new();
    private static string ConfigPath => Path.Combine(Paths.Data, "symbio.issues.json");

    private static SymbioIssuesConfig? _config;
    private static SymbioIssuesConfig Config
    {
        get
        {
            if (_config is not null)
                return _config;
            lock (FileLock)
            {
                if (_config is not null)
                    return _config;
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        var json = File.ReadAllText(ConfigPath);
                        _config = JsonSerializer.Deserialize<SymbioIssuesConfig>(json) ?? new SymbioIssuesConfig();
                    }
                    else
                    {
                        _config = new SymbioIssuesConfig();
                    }
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "读取问题板块配置失败");
                    _config = new SymbioIssuesConfig();
                }

                return _config;
            }
        }
    }

    private static void SaveConfig()
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(Paths.Data);
                var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "保存问题板块配置失败");
            }
        }
    }

    /// <summary>
    ///     供 UI 读取的默认备份根目录（可能为空）。
    /// </summary>
    public static string ConfigForUi => Config.DefaultBackupRoot;

    /// <summary>
    ///     设置默认备份根目录。
    /// </summary>
    public static void SetDefaultBackupRoot(string path)
    {
        Config.DefaultBackupRoot = path;
        SaveConfig();
    }

    #endregion

    #region 一、优化记录与撤销

    /// <summary>
    ///     记录一条优化动作（供撤销）。返回记录 ID。
    /// </summary>
    public static int RecordOptimization(string description, string kind, string detail)
    {
        var rec = new OptimizationRecord
        {
            Id = Config.NextOptimizationId++,
            Time = DateTime.Now,
            Description = description,
            Kind = kind,
            Detail = detail,
            Undone = false
        };
        Config.OptimizationRecords.Insert(0, rec);
        // 限制最多保留 200 条，避免无限增长
        if (Config.OptimizationRecords.Count > 200)
            Config.OptimizationRecords.RemoveRange(200, Config.OptimizationRecords.Count - 200);
        SaveConfig();
        return rec.Id;
    }

    /// <summary>
    ///     获取所有优化记录（新的在前）。
    /// </summary>
    public static List<OptimizationRecord> GetOptimizationRecords() => Config.OptimizationRecords;

    /// <summary>
    ///     撤销一条优化记录。由于记录本身只含描述信息，真正的撤销需要反向操作；
    ///     这里把可撤销的动作集中处理：禁用模组→恢复、已记录的信息→标记已撤销。
    ///     若记录携带可恢复的文件信息（Kind=DisableMod），执行改名恢复。
    /// </summary>
    public static bool UndoOptimization(int id, out string message)
    {
        var rec = Config.OptimizationRecords.FirstOrDefault(r => r.Id == id);
        if (rec is null)
        {
            message = "未找到该优化记录。";
            return false;
        }

        if (rec.Undone)
        {
            message = "该记录已撤销过。";
            return false;
        }

        var ok = true;
        var sb = new StringBuilder();

        switch (rec.Kind)
        {
            case "DisableMod":
                // 恢复被禁用的模组：把 .disabled 后缀去掉
                var target = rec.Detail; // Detail 存完整文件路径
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    try
                    {
                        var original = Path.ChangeExtension(target, null);
                        // 去掉 .jar.disabled -> .jar
                        var restored = target.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                            ? target[..^".disabled".Length]
                            : target;
                        if (File.Exists(restored))
                        {
                            sb.AppendLine($"跳过（目标已存在）：{Path.GetFileName(restored)}");
                        }
                        else
                        {
                            File.Move(target, restored);
                            sb.AppendLine($"已恢复模组：{Path.GetFileName(restored)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        sb.AppendLine($"恢复失败：{ex.Message}");
                    }
                }
                else
                {
                    ok = false;
                    sb.AppendLine("记录的文件已不存在，无法恢复。");
                }
                break;

            default:
                // 其它类型记录仅做标记撤销，不涉及文件操作
                sb.AppendLine("已标记为撤销（该动作不涉及文件回滚）。");
                break;
        }

        if (ok)
            rec.Undone = true;

        SaveConfig();
        message = sb.ToString().Trim();
        return ok;
    }

    /// <summary>
    ///     清空优化记录。
    /// </summary>
    public static void ClearOptimizationRecords()
    {
        Config.OptimizationRecords.Clear();
        SaveConfig();
    }

    #endregion

    #region 二、报错记录快捷查看与自动总结

    /// <summary>
    ///     收集指定实例（可空=全部实例）的崩溃报告与日志路径，并给出本地规则总结。
    /// </summary>
    public static List<ErrorRecord> CollectErrorRecords(McInstance? instance = null)
    {
        var result = new List<ErrorRecord>();

        IEnumerable<McInstance> instances;
        if (instance is not null)
            instances = new[] { instance };
        else
            instances = EnumerateAllInstances();

        foreach (var inst in instances)
        {
            if (inst is null || string.IsNullOrEmpty(inst.PathIndie))
                continue;

            // crash-reports 目录
            var crashDir = Path.Combine(inst.PathIndie, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                foreach (var f in Directory.GetFiles(crashDir, "crash-*.txt")
                             .OrderByDescending(File.GetLastWriteTime).Take(20))
                {
                    result.Add(BuildErrorRecord(inst, f, true));
                }
            }

            // logs/latest.log
            var latestLog = Path.Combine(inst.PathIndie, "logs", "latest.log");
            if (File.Exists(latestLog))
            {
                result.Add(BuildErrorRecord(inst, latestLog, false));
            }
        }

        return result;
    }

    private static ErrorRecord BuildErrorRecord(McInstance inst, string path, bool isCrash)
    {
        var info = new FileInfo(path);
        string summary;
        var content = "";
        try
        {
            content = File.ReadAllText(path);
            summary = SummarizeLog(content, isCrash);
        }
        catch
        {
            summary = "无法读取该文件。";
        }

        return new ErrorRecord
        {
            InstanceName = inst.Name,
            FilePath = path,
            FileName = Path.GetFileName(path),
            IsCrashReport = isCrash,
            LastWriteTime = info.LastWriteTime,
            Size = info.Length,
            Summary = summary
        };
    }

    /// <summary>
    ///     本地规则引擎：对崩溃/日志文本做自动总结。
    /// </summary>
    public static string SummarizeLog(string log, bool isCrash)
    {
        if (string.IsNullOrWhiteSpace(log))
            return "（空文件）";

        var causes = new List<string>();

        if (log.Contains("OutOfMemoryError") || log.Contains("Java heap space"))
            causes.Add("内存不足 (OOM)");
        if (log.Contains("UnsupportedClassVersionError"))
            causes.Add("Java 版本不匹配");
        if (log.Contains("ClassNotFoundException") || log.Contains("NoClassDefFoundError"))
            causes.Add("缺失类 / 前置模组");
        if (log.Contains("NoSuchMethodError") || log.Contains("NoSuchFieldError"))
            causes.Add("模组 API 版本冲突");
        if (log.Contains("Duplicate Mod") || log.Contains("Found a duplicate mod"))
            causes.Add("重复模组");
        if (log.Contains("mixin") && log.Contains("failed", StringComparison.OrdinalIgnoreCase))
            causes.Add("Mixin 注入失败（模组冲突）");
        if (log.Contains("Could not find or load main class"))
            causes.Add("主类缺失（核心文件损坏）");
        if (log.Contains("UnsupportedOperationException"))
            causes.Add("不支持的 API 调用");
        if (log.Contains("NullPointerException"))
            causes.Add("空指针异常（NPE）");
        if (log.Contains("Connection refused") || log.Contains("Connection timed out"))
            causes.Add("网络连接失败");

        if (causes.Count == 0)
        {
            // 提取崩溃描述行
            var descLine = log.Split('\n')
                .FirstOrDefault(l => l.Contains("Description:") || l.Contains("java.lang.") ||
                                     l.Contains("Exception") || l.Contains("Error"));
            if (descLine is not null)
                return "疑似：" + descLine.Trim().Substring(0, Math.Min(120, descLine.Trim().Length));
            return isCrash ? "崩溃原因待分析（未匹配到常见特征）" : "日志未发现明显错误";
        }

        return string.Join("；", causes.Distinct());
    }

    /// <summary>
    ///     打开报错记录所在文件/文件夹。
    /// </summary>
    public static void OpenErrorRecord(ErrorRecord rec)
    {
        if (rec is null)
            return;
        try
        {
            if (File.Exists(rec.FilePath))
                ProcessTools.OpenFile(rec.FilePath);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开报错记录失败");
        }
    }

    #endregion

    #region 三、备份

    /// <summary>
    ///     备份一个实例到指定目录（带时间戳子文件夹）。返回最终备份路径；取消返回 null。
    /// </summary>
    public static string? BackupInstance(McInstance instance, string backupRoot, bool askOverwrite)
    {
        if (instance is null || string.IsNullOrEmpty(instance.PathInstance))
            return null;
        if (!Directory.Exists(instance.PathInstance))
            return null;

        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var destDir = Path.Combine(backupRoot, $"{instance.Name}_{stamp}");

        // 若目标已存在（几乎不可能），追加序号
        var finalDest = destDir;
        var i = 2;
        while (Directory.Exists(finalDest))
            finalDest = destDir + $"_{i++}";

        try
        {
            CopyDirectory(instance.PathInstance, finalDest, askOverwrite);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "备份实例失败");
            throw;
        }

        // 记录备份历史
        Config.BackupRecords.Insert(0, new BackupRecord
        {
            Time = DateTime.Now,
            InstanceName = instance.Name,
            SourcePath = instance.PathInstance,
            DestPath = finalDest
        });
        if (Config.BackupRecords.Count > 100)
            Config.BackupRecords.RemoveRange(100, Config.BackupRecords.Count - 100);
        SaveConfig();

        return finalDest;
    }

    public static List<BackupRecord> GetBackupRecords() => Config.BackupRecords;

    /// <summary>
    ///     复制目录。askOverwrite=true 时同名文件由调用方预先确认（此处默认跳过已有文件）。
    ///     安全策略：绝不删除任何已有文件。
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir, bool askOverwrite)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (File.Exists(destFile))
                continue; // 跳过已存在，绝不覆盖
            File.Copy(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)), askOverwrite);
        }
    }

    #endregion

    #region 工具

    /// <summary>
    ///     枚举所有已加载的实例（摊平 mcInstanceList）。
    /// </summary>
    public static List<McInstance> EnumerateAllInstances()
    {
        var list = new List<McInstance>();
        try
        {
            foreach (var kv in ModInstanceList.mcInstanceList)
            {
                foreach (var inst in kv.Value)
                {
                    if (inst is not null)
                        list.Add(inst);
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "枚举实例失败");
        }

        return list.Distinct().ToList();
    }

    /// <summary>
    ///     打开某个路径。
    /// </summary>
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (Directory.Exists(path))
                ProcessTools.OpenFolder(path);
            else if (File.Exists(path))
                ProcessTools.OpenFolder(Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开文件夹失败");
        }
    }

    #endregion
}

#region 数据模型

public sealed class OptimizationRecord
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Undone { get; set; }
}

public sealed class ErrorRecord
{
    public string InstanceName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsCrashReport { get; set; }
    public DateTime LastWriteTime { get; set; }
    public long Size { get; set; }
    public string Summary { get; set; } = "";
}

public sealed class BackupRecord
{
    public DateTime Time { get; set; }
    public string InstanceName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string DestPath { get; set; } = "";
}

public sealed class SymbioIssuesConfig
{
    [JsonPropertyName("nextOptimizationId")]
    public int NextOptimizationId { get; set; } = 1;

    [JsonPropertyName("optimizationRecords")]
    public List<OptimizationRecord> OptimizationRecords { get; set; } = new();

    [JsonPropertyName("backupRecords")]
    public List<BackupRecord> BackupRecords { get; set; } = new();

    /// <summary>默认备份根目录（用户自定义，空=每次选择）。</summary>
    [JsonPropertyName("defaultBackupRoot")]
    public string DefaultBackupRoot { get; set; } = "";

    /// <summary>是否在优化后自动记录（供撤销）。</summary>
    [JsonPropertyName("autoRecordOptimization")]
    public bool AutoRecordOptimization { get; set; } = true;

    /// <summary>报错记录自动总结的最大条数。</summary>
    [JsonPropertyName("maxErrorRecords")]
    public int MaxErrorRecords { get; set; } = 50;
}

#endregion

#region 进程操作辅助（避免直接依赖不确定的 API）

internal static class ProcessTools
{
    public static void OpenFile(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 忽略
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 忽略
        }
    }
}

#endregion
