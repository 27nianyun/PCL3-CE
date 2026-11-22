using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCL;

/// <summary>一个环境还原点的概要信息。</summary>
public sealed class EnvSnapshotInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string McVersion { get; set; } = "";
    public string Loader { get; set; } = "";
    public string LoaderVersion { get; set; } = "";
    public string Note { get; set; } = "";
    public int ModCount { get; set; }
    public bool HasConfig { get; set; }
    public long SizeBytes { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") : Name;
}

/// <summary>快照 zip 内的清单文件结构。</summary>
internal sealed class EnvSnapshotManifest
{
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string McVersion { get; set; } = "";
    public string Loader { get; set; } = "";
    public string LoaderVersion { get; set; } = "";
    public string Note { get; set; } = "";
    public bool HasConfig { get; set; }
    public string VersionJsonName { get; set; } = "";
    public List<EnvSnapshotFile> Mods { get; set; } = [];
}

/// <summary>快照中记录的单个模组文件。</summary>
public sealed class EnvSnapshotFile
{
    public string Path { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>
///     环境还原点：把实例的版本 JSON、加载器信息、模组清单与配置打包成快照，可整套恢复。
/// </summary>
/// <remarks>
///     PCL₃-CE 新增功能（迁移自 PCL Living Fork 的「环境还原点」构思，独立实现）。
///     快照**不包含存档**，因此恢复环境不会让存档进度回退。
///     还原前会把当前 mods / config 改名保留为 <c>*.restore-old-时间戳</c>，不会直接删除。
/// </remarks>
public static class ModEnvironmentSnapshot
{
    private const string ManifestEntry = "snapshot.json";
    private const string ModsPrefix = "mods/";
    private const string ConfigPrefix = "config/";
    private const string VersionJsonEntry = "version.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>快照存放目录：&lt;实例目录&gt;PCL\Snapshots。</summary>
    public static string GetSnapshotFolder(McInstance instance)
    {
        return Path.Combine(instance.PathIndie, "PCL", "Snapshots");
    }

    /// <summary>创建快照，返回其概要。耗时较长，请放在后台线程调用。</summary>
    public static EnvSnapshotInfo Create(McInstance instance, string name, string note = "",
        bool includeConfig = true, IProgress<double> progress = null)
    {
        var folder = GetSnapshotFolder(instance);
        Directory.CreateDirectory(folder);

        var createdAt = DateTime.Now;
        var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? createdAt.ToString("yyyy-MM-dd_HH-mm-ss") : name);
        var filePath = Path.Combine(folder, $"{createdAt:yyyy-MM-dd_HH-mm-ss}_{safeName}.zip");

        var modsFolder = Path.Combine(instance.PathIndie, "mods");
        var configFolder = Path.Combine(instance.PathIndie, "config");
        var versionJson = Path.Combine(instance.PathInstance, instance.Name + ".json");

        var mods = new List<EnvSnapshotFile>();
        if (Directory.Exists(modsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(modsFolder, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                mods.Add(new EnvSnapshotFile
                {
                    Path = Path.GetRelativePath(modsFolder, file).Replace('\\', '/'),
                    Sha1 = ComputeSha1(file),
                    Size = info.Length
                });
            }
        }

        var manifest = new EnvSnapshotManifest
        {
            Name = name ?? "",
            CreatedAt = createdAt,
            McVersion = instance.Info.VanillaName ?? "",
            Loader = SymbioTools.GetLoaderType(instance),
            LoaderVersion = GetLoaderVersion(instance),
            Note = note ?? "",
            HasConfig = includeConfig && Directory.Exists(configFolder),
            VersionJsonName = File.Exists(versionJson) ? instance.Name + ".json" : "",
            Mods = mods
        };

        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

            var total = Math.Max(1, mods.Count + (manifest.HasConfig ? 1 : 0) + (manifest.VersionJsonName.Length > 0 ? 1 : 0));
            var done = 0;

            if (Directory.Exists(modsFolder))
                foreach (var file in Directory.EnumerateFiles(modsFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(modsFolder, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, ModsPrefix + relative, CompressionLevel.Optimal);
                    progress?.Report(++done / (double)total);
                }

            if (manifest.HasConfig)
            {
                foreach (var file in Directory.EnumerateFiles(configFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(configFolder, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, ConfigPrefix + relative, CompressionLevel.Optimal);
                }

                progress?.Report(++done / (double)total);
            }

            if (manifest.VersionJsonName.Length > 0)
            {
                archive.CreateEntryFromFile(versionJson, VersionJsonEntry, CompressionLevel.Optimal);
                progress?.Report(++done / (double)total);
            }
        }

        progress?.Report(1d);

        return new EnvSnapshotInfo
        {
            Name = manifest.Name,
            FilePath = filePath,
            CreatedAt = createdAt,
            McVersion = manifest.McVersion,
            Loader = manifest.Loader,
            LoaderVersion = manifest.LoaderVersion,
            Note = manifest.Note,
            ModCount = manifest.Mods.Count,
            HasConfig = manifest.HasConfig,
            SizeBytes = new FileInfo(filePath).Length
        };
    }

    /// <summary>列出实例的全部快照，按时间倒序。只读取 zip 内清单，速度很快。</summary>
    public static List<EnvSnapshotInfo> List(McInstance instance)
    {
        var folder = GetSnapshotFolder(instance);
        var list = new List<EnvSnapshotInfo>();
        if (!Directory.Exists(folder)) return list;

        foreach (var file in Directory.EnumerateFiles(folder, "*.zip"))
        {
            try
            {
                using var archive = ZipFile.OpenRead(file);
                var entry = archive.GetEntry(ManifestEntry);
                if (entry is null) continue;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var manifest = JsonSerializer.Deserialize<EnvSnapshotManifest>(reader.ReadToEnd());
                if (manifest is null) continue;

                list.Add(new EnvSnapshotInfo
                {
                    Name = manifest.Name,
                    FilePath = file,
                    CreatedAt = manifest.CreatedAt == default
                        ? File.GetCreationTime(file)
                        : manifest.CreatedAt,
                    McVersion = manifest.McVersion,
                    Loader = manifest.Loader,
                    LoaderVersion = manifest.LoaderVersion,
                    Note = manifest.Note,
                    ModCount = manifest.Mods.Count,
                    HasConfig = manifest.HasConfig,
                    SizeBytes = new FileInfo(file).Length
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"读取环境还原点失败（{Path.GetFileName(file)}）", ModBase.LogLevel.Hint);
            }
        }

        return list.OrderByDescending(i => i.CreatedAt).ToList();
    }

    /// <summary>把实例环境恢复到指定快照。耗时较长，请放在后台线程调用。</summary>
    public static void Restore(McInstance instance, EnvSnapshotInfo snapshot, bool restoreConfig = true)
    {
        if (!File.Exists(snapshot.FilePath)) throw new FileNotFoundException("快照文件不存在", snapshot.FilePath);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var modsFolder = Path.Combine(instance.PathIndie, "mods");
        var configFolder = Path.Combine(instance.PathIndie, "config");
        var versionJson = Path.Combine(instance.PathInstance, instance.Name + ".json");

        using var archive = ZipFile.OpenRead(snapshot.FilePath);

        // 1. 当前 mods 改名保留
        if (Directory.Exists(modsFolder))
        {
            var backup = UniquePath(modsFolder + $".restore-old-{stamp}");
            Directory.Move(modsFolder, backup);
        }

        Directory.CreateDirectory(modsFolder);

        // 2. 解压快照中的 mods
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(ModsPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = entry.FullName[ModsPrefix.Length..];
            if (string.IsNullOrEmpty(relative) || relative.EndsWith('/')) continue;
            var target = Path.Combine(modsFolder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }

        // 3. 配置
        if (restoreConfig && snapshot.HasConfig)
        {
            if (Directory.Exists(configFolder))
            {
                var backup = UniquePath(configFolder + $".restore-old-{stamp}");
                Directory.Move(configFolder, backup);
            }

            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = entry.FullName[ConfigPrefix.Length..];
                if (string.IsNullOrEmpty(relative) || relative.EndsWith('/')) continue;
                var target = Path.Combine(configFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }

        // 4. 版本 JSON（加载器与依赖声明）
        var versionEntry = archive.GetEntry(VersionJsonEntry);
        if (versionEntry is not null && Directory.Exists(instance.PathInstance))
        {
            if (File.Exists(versionJson))
            {
                var backup = UniquePath(versionJson + $".restore-old-{stamp}");
                File.Copy(versionJson, backup, true);
            }

            versionEntry.ExtractToFile(versionJson, true);
        }

        ModBase.Log($"[Snapshot] 已恢复环境还原点：{snapshot.DisplayName}", ModBase.LogLevel.Normal);
    }

    /// <summary>删除一个快照文件。</summary>
    public static void Delete(EnvSnapshotInfo snapshot)
    {
        if (File.Exists(snapshot.FilePath)) File.Delete(snapshot.FilePath);
    }

    /// <summary>获取加载器版本号（用于快照展示）。</summary>
    private static string GetLoaderVersion(McInstance instance)
    {
        try
        {
            var info = instance.Info;
            if (info.HasForge) return info.Forge ?? "";
            if (info.HasNeoForge) return info.NeoForge ?? "";
            if (info.HasFabric) return info.Fabric ?? "";
            if (info.HasQuilt) return info.Quilt ?? "";
        }
        catch
        {
            // 忽略
        }

        return "";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length > 60 ? name[..60] : name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{path}-{i}";
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        return path + "-" + Guid.NewGuid().ToString("N")[..6];
    }

    private static string ComputeSha1(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }
}
