using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL;

/// <summary>整合包差异类型。</summary>
public enum ModpackFileChange
{
    Added,
    Updated,
    Unchanged,
    Removed
}

/// <summary>整合包差异中的单个文件。</summary>
public sealed class ModpackFilePlanItem
{
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
    public ModpackFileChange Change { get; set; }

    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>一次整合包对比的结果。</summary>
public sealed class ModpackUpdatePlan
{
    public string PackName { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string McVersion { get; set; } = "";
    public string LoaderInfo { get; set; } = "";
    public List<ModpackFilePlanItem> Files { get; } = [];

    public int AddedCount => Files.Count(f => f.Change == ModpackFileChange.Added);
    public int UpdatedCount => Files.Count(f => f.Change == ModpackFileChange.Updated);
    public int RemovedCount => Files.Count(f => f.Change == ModpackFileChange.Removed);
    public int UnchangedCount => Files.Count(f => f.Change == ModpackFileChange.Unchanged);

    public long DownloadBytes => Files
        .Where(f => f.Change is ModpackFileChange.Added or ModpackFileChange.Updated)
        .Sum(f => f.Size);
}

/// <summary>
///     整合包三方更新：把新整合包清单与实例现状对比，分类为新增 / 更新 / 移除 / 一致后再按需下载。
/// </summary>
/// <remarks>
///     PCL₃-CE 新增功能（迁移自 PCL Living Fork「整合包三方更新」的构思，独立实现）。
///     支持 Modrinth 格式（modrinth.index.json，自带直链与 SHA1）与 CurseForge 清单：
///     清单内自带 downloadUrl 时直接使用；没有直链时用 CurseForge 官方 API（需用户自备 API Key）解析文件名与下载地址。
///     「移除」只在有基线清单时判定，且默认不删除。
/// </remarks>
public static class ModModpackUpdater
{
    private const string BaselineFileName = "ModpackBaseline.json";
    private const string CurseForgeFilesApi = "https://api.curseforge.com/v1/mods/{0}/files";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL3-CE/1.0 (ModpackUpdater)");
        return client;
    }

    /// <summary>基线文件路径：&lt;实例目录&gt;\PCL\ModpackBaseline.json。</summary>
    public static string GetBaselinePath(McInstance instance) =>
        System.IO.Path.Combine(instance.PathIndie, "PCL", BaselineFileName);

    /// <summary>是否已有基线（决定「移除」能否判定）。</summary>
    public static bool HasBaseline(McInstance instance) => File.Exists(GetBaselinePath(instance));

    /// <summary>读取整合包清单（不落盘）。<paramref name="curseForgeApiKey" /> 用于解析 CurseForge 无直链文件。</summary>
    public static ModpackUpdatePlan ReadManifest(string archivePath, string curseForgeApiKey = null)
    {
        var plan = new ModpackUpdatePlan();
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = FindEntry(archive, "modrinth.index.json");
        if (entry is null)
        {
            var curseEntry = FindEntry(archive, "manifest.json");
            if (curseEntry is null)
                throw new InvalidOperationException("这个压缩包不是可识别的整合包（缺少 modrinth.index.json / manifest.json）。");
            ReadCurseForgeManifest(curseEntry, plan, curseForgeApiKey);
            return plan;
        }

        using var stream = entry.Open();
        var json = JsonNode.Parse(stream) as JsonObject
                   ?? throw new InvalidOperationException("modrinth.index.json 格式不正确。");

        plan.PackName = json["name"]?.GetValue<string>() ?? "";
        plan.PackVersion = json["versionId"]?.GetValue<string>() ?? "";

        if (json["dependencies"] is JsonObject dependencies)
        {
            plan.McVersion = dependencies["minecraft"]?.GetValue<string>() ?? "";
            var loaders = new List<string>();
            foreach (var key in new[] { "forge", "neoforge", "fabric-loader", "quilt-loader" })
                if (dependencies[key] is JsonNode node)
                    loaders.Add($"{key} {node}");
            plan.LoaderInfo = string.Join(" / ", loaders);
        }

        foreach (var file in json["files"]?.AsArray() ?? [])
        {
            if (file is not JsonObject item) continue;
            var clientEnv = item["env"]?["client"]?.GetValue<string>();
            if (string.Equals(clientEnv, "unsupported", StringComparison.OrdinalIgnoreCase)) continue;

            var path = item["path"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(path)) continue;

            var downloads = item["downloads"]?.AsArray();
            var url = downloads?.Select(d => d?.GetValue<string>())
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? "";
            var sha1 = item["hashes"]?["sha1"]?.GetValue<string>() ?? "";

            plan.Files.Add(new ModpackFilePlanItem
            {
                Path = NormalizeRelativePath(path),
                Url = url,
                Sha1 = sha1.ToLowerInvariant(),
                Size = item["fileSize"] is JsonNode sizeNode && long.TryParse(sizeNode.ToString(), out var size)
                    ? size
                    : 0
            });
        }

        return plan;
    }

    /// <summary>把清单与实例现状对比生成差异计划。需计算本地哈希，请放在后台线程调用。</summary>
    public static ModpackUpdatePlan BuildPlan(string archivePath, McInstance instance, string curseForgeApiKey = null)
    {
        var plan = ReadManifest(archivePath, curseForgeApiKey);
        var root = instance.PathIndie;
        var baseline = LoadBaseline(instance);

        foreach (var item in plan.Files)
        {
            var target = SafeCombine(root, item.Path);
            if (target is null || !File.Exists(target))
            {
                item.Change = ModpackFileChange.Added;
                continue;
            }

            var localHash = ComputeSha1(target);
            item.Change = !string.IsNullOrEmpty(item.Sha1) &&
                          string.Equals(localHash, item.Sha1, StringComparison.OrdinalIgnoreCase)
                ? ModpackFileChange.Unchanged
                : ModpackFileChange.Updated;
        }

        if (baseline.Count > 0)
        {
            var newPaths = new HashSet<string>(plan.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var oldPath in baseline.Keys)
            {
                if (newPaths.Contains(oldPath)) continue;
                var target = SafeCombine(root, oldPath);
                if (target is null || !File.Exists(target)) continue;
                plan.Files.Add(new ModpackFilePlanItem
                {
                    Path = oldPath,
                    Change = ModpackFileChange.Removed,
                    Sha1 = baseline[oldPath]
                });
            }
        }

        return plan;
    }

    /// <summary>执行更新，返回（成功数, 失败数, 处理字节数）。</summary>
    public static (int Success, int Failed, long Bytes) Apply(ModpackUpdatePlan plan, McInstance instance,
        bool removeOldFiles, IProgress<(int Done, int Total, string Current)> progress = null)
    {
        var root = instance.PathIndie;
        var success = 0;
        var failed = 0;
        long bytes = 0;

        var pending = plan.Files
            .Where(f => f.Change is ModpackFileChange.Added or ModpackFileChange.Updated)
            .ToList();
        var removals = removeOldFiles ? plan.Files.Where(f => f.Change == ModpackFileChange.Removed).ToList() : [];
        var total = pending.Count + removals.Count;
        var done = 0;

        foreach (var item in pending)
        {
            progress?.Report((done, total, item.FileName));
            var target = SafeCombine(root, item.Path);
            if (target is null)
            {
                failed++;
                done++;
                continue;
            }

            var temp = target + ".pcltmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                var size = DownloadToFile(item.Url, temp);
                if (!string.IsNullOrEmpty(item.Sha1))
                {
                    var actual = ComputeSha1(temp);
                    if (!string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA1 校验不通过");
                }

                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                bytes += size;
                success++;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"整合包更新下载失败：{item.Path}", ModBase.LogLevel.Hint);
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // 忽略清理失败
                }

                failed++;
            }

            done++;
        }

        foreach (var item in removals)
        {
            progress?.Report((done, total, item.FileName));
            var target = SafeCombine(root, item.Path);
            try
            {
                if (target is not null && File.Exists(target))
                {
                    var size = new FileInfo(target).Length;
                    File.Delete(target);
                    bytes += size;
                    success++;
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"整合包更新删除失败：{item.Path}", ModBase.LogLevel.Hint);
                failed++;
            }

            done++;
        }

        SaveBaseline(instance, plan.Files
            .Where(f => f.Change != ModpackFileChange.Removed)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Sha1, StringComparer.OrdinalIgnoreCase));

        progress?.Report((total, total, ""));
        return (success, failed, bytes);
    }

    private static Dictionary<string, string> LoadBaseline(McInstance instance)
    {
        var path = GetBaselinePath(instance);
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return json is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(json, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveBaseline(McInstance instance, Dictionary<string, string> files)
    {
        try
        {
            var path = GetBaselinePath(instance);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(files, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "保存整合包基线失败", ModBase.LogLevel.Hint);
        }
    }

    private static void ReadCurseForgeManifest(ZipArchiveEntry entry, ModpackUpdatePlan plan, string apiKey)
    {
        using var stream = entry.Open();
        var json = JsonNode.Parse(stream) as JsonObject
                   ?? throw new InvalidOperationException("manifest.json 格式不正确。");

        plan.PackName = json["name"]?.GetValue<string>() ?? "";
        plan.PackVersion = json["version"]?.GetValue<string>() ?? "";
        plan.McVersion = json["minecraft"]?["version"]?.GetValue<string>() ?? "";

        var files = json["files"]?.AsArray();
        if (files is null || files.Count == 0)
            throw new InvalidOperationException("整合包清单里没有文件列表。");

        var unresolved = new List<ModpackFilePlanItem>();
        var ids = new List<(string ProjectId, string FileId)>();

        foreach (var node in files)
        {
            if (node is not JsonObject item) continue;
            var projectId = item["projectID"]?.ToString() ?? "";
            var fileId = item["fileID"]?.ToString() ?? "";
            var url = item["downloadUrl"]?.GetValue<string>() ?? "";

            var planItem = new ModpackFilePlanItem
            {
                Path = $"mods/{projectId}-{fileId}.jar",
                Url = url,
                Change = ModpackFileChange.Added
            };
            plan.Files.Add(planItem);

            if (string.IsNullOrWhiteSpace(url))
            {
                unresolved.Add(planItem);
                ids.Add((projectId, fileId));
            }
        }

        if (unresolved.Count == 0) return;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"这个 CurseForge 整合包有 {unresolved.Count} 个文件没有直链（downloadUrl），需要 CurseForge 官方 API 才能自动更新。\n" +
                "请在本页填写 CurseForge API Key（在 console.curseforge.com 免费申请）后重新对比；\n" +
                "也可以改用 Modrinth 格式的整合包。");

        ResolveCurseForgeFiles(apiKey, ids, unresolved);
    }

    /// <summary>用 CurseForge 官方 API 批量解析 fileID 对应的下载地址、文件名与 SHA1。</summary>
    private static void ResolveCurseForgeFiles(string apiKey, List<(string ProjectId, string FileId)> ids,
        List<ModpackFilePlanItem> targets)
    {
        var byProject = new Dictionary<string, List<(string FileId, ModpackFilePlanItem Item)>>();
        for (var i = 0; i < ids.Count; i++)
        {
            var projectId = ids[i].ProjectId;
            if (string.IsNullOrWhiteSpace(projectId)) continue;
            if (!byProject.TryGetValue(projectId, out var list)) byProject[projectId] = list = [];
            list.Add((ids[i].FileId, targets[i]));
        }

        var resolved = 0;
        foreach (var pair in byProject)
        {
            var projectId = pair.Key;
            var wanted = pair.Value;
            try
            {
                var fileIds = new JsonArray();
                foreach (var entry in wanted)
                    if (long.TryParse(entry.FileId, out var fileId))
                        fileIds.Add(fileId);

                var body = new JsonObject { ["fileIds"] = fileIds };

                using var request = new HttpRequestMessage(HttpMethod.Post,
                    string.Format(CurseForgeFilesApi, projectId));
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Accept.ParseAdd("application/json");
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

                using var response = Http.Send(request, HttpCompletionOption.ResponseContentRead);
                if (!response.IsSuccessStatusCode)
                {
                    ModBase.Log($"[ModpackUpdater] CurseForge API 返回 {(int)response.StatusCode}（project {projectId}）",
                        ModBase.LogLevel.Hint);
                    continue;
                }

                using var content = response.Content.ReadAsStream();
                var data = JsonNode.Parse(content)?["data"]?.AsArray();
                if (data is null) continue;

                foreach (var node in data)
                {
                    if (node is not JsonObject file) continue;
                    var id = file["id"]?.ToString() ?? "";
                    var match = wanted.FirstOrDefault(x => x.FileId == id);
                    if (match.Item is null) continue;

                    var url = file["downloadUrl"]?.GetValue<string>() ?? "";
                    if (string.IsNullOrWhiteSpace(url)) continue; // 作者禁止第三方下载

                    var fileName = file["fileName"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrWhiteSpace(fileName))
                        match.Item.Path = "mods/" + fileName;

                    var sha1 = "";
                    foreach (var hash in file["hashes"]?.AsArray() ?? [])
                        if (hash?["algo"]?.ToString() == "1")
                            sha1 = hash["value"]?.GetValue<string>() ?? "";

                    match.Item.Url = url;
                    match.Item.Sha1 = sha1.ToLowerInvariant();
                    match.Item.Size = file["fileLength"] is JsonNode lengthNode &&
                                      long.TryParse(lengthNode.ToString(), out var length)
                        ? length
                        : 0;
                    resolved++;
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[ModpackUpdater] 解析 CurseForge 文件失败（project {projectId}）", ModBase.LogLevel.Hint);
            }
        }

        var left = targets.Count - resolved;
        if (left > 0)
            throw new InvalidOperationException(
                $"还有 {left} 个文件无法获取下载地址（可能是作者禁止第三方下载，或 API Key 无效）。\n" +
                "建议改用 Modrinth 格式整合包，或在 CurseForge 客户端更新。");
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName)
    {
        return archive.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>把清单相对路径安全拼接，越界返回 null。</summary>
    private static string SafeCombine(string root, string relative)
    {
        try
        {
            var fullRoot = System.IO.Path.GetFullPath(root);
            var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot,
                relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            return combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? combined : null;
        }
        catch
        {
            return null;
        }
    }

    private static long DownloadToFile(string url, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("缺少下载地址");

        using var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var source = response.Content.ReadAsStream();
        using var target = File.Create(targetPath);
        source.CopyTo(target);
        return target.Length;
    }

    private static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }
}
