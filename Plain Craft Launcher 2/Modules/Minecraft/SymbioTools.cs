using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using fNbt;
using PCL.Core.App;
using PCL.Core.Minecraft;

namespace PCL;

/// <summary>
///     Symbio 工具集：地图导入、光影/数据包批量导入、预设、服务器收录、推荐、攻略。
///     所有逻辑纯本地，不改动既有功能；配置持久化到独立的 symbio.tools.json。
/// </summary>
public static class SymbioTools
{
    #region 配置与持久化

    private static readonly object FileLock = new();
    private static string ConfigPath => Path.Combine(Paths.Data, "symbio.tools.json");

    private static SymbioToolsConfig? _config;
    public static SymbioToolsConfig Config
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
                    _config = File.Exists(ConfigPath)
                        ? JsonSerializer.Deserialize<SymbioToolsConfig>(File.ReadAllText(ConfigPath)) ?? new SymbioToolsConfig()
                        : new SymbioToolsConfig();
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "读取 Symbio 工具集配置失败");
                    _config = new SymbioToolsConfig();
                }

                return _config;
            }
        }
    }

    public static void SaveConfig()
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "保存 Symbio 工具集配置失败");
            }
        }
    }

    #endregion

    #region 通用工具

    /// <summary>枚举所有实例（复用 SymbioIssues 的实现，避免重复）。</summary>
    public static List<McInstance> EnumerateAllInstances() => SymbioIssues.EnumerateAllInstances();

    /// <summary>获取实例的有效游戏目录（版本隔离则实例目录，否则全局 .minecraft）。</summary>
    public static string GetGameDir(McInstance inst) => inst.PathIndie;

    /// <summary>获取实例的指定子文件夹（mods / shaderpacks / resourcepacks / saves / datapacks）。</summary>
    public static string GetSubFolder(McInstance inst, string sub) => Path.Combine(inst.PathIndie, sub);

    public static void EnsureFolder(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    /// <summary>递归复制目录（覆盖已有文件）。</summary>
    public static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, dest), true);
    }

    /// <summary>判断一个目录是否是 Minecraft 存档（含 level.dat）。</summary>
    public static bool IsMinecraftSave(string path)
    {
        if (!Directory.Exists(path))
            return false;
        return File.Exists(Path.Combine(path, "level.dat"));
    }

    /// <summary>判断是否是光影包（含 shaders 目录或 .zip 里含 shaders）。</summary>
    public static bool IsShaderPack(string path)
    {
        if (Directory.Exists(path))
            return Directory.Exists(Path.Combine(path, "shaders"));
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return ZipContainsEntry(path, "shaders/");
        return false;
    }

    /// <summary>判断是否是数据包（含 pack.mcmeta 或 data 目录）。</summary>
    public static bool IsDataPack(string path)
    {
        if (Directory.Exists(path))
            return File.Exists(Path.Combine(path, "pack.mcmeta"));
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return ZipContainsEntry(path, "pack.mcmeta");
        return false;
    }

    private static bool ZipContainsEntry(string zipPath, string entryPrefix)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            return archive.Entries.Any(e => e.FullName.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开文件夹失败：" + path);
        }
    }

    #endregion

    #region 地图导入

    /// <summary>
    ///     将地图文件夹（含 level.dat）导入到目标实例的 saves 目录。
    ///     返回导入后的存档文件夹路径；失败抛异常。
    /// </summary>
    public static string ImportMap(McInstance instance, string sourceFolder, bool overwrite)
    {
        if (!IsMinecraftSave(sourceFolder))
            throw new InvalidOperationException("所选文件夹不是有效的 Minecraft 存档（缺少 level.dat）。");

        var savesFolder = GetSubFolder(instance, "saves");
        EnsureFolder(savesFolder);

        var targetName = new DirectoryInfo(sourceFolder).Name;
        var dest = Path.Combine(savesFolder, targetName);

        if (Directory.Exists(dest))
        {
            if (!overwrite)
                throw new InvalidOperationException($"存档「{targetName}」已存在。");
            Directory.Delete(dest, true);
        }

        CopyDirectory(sourceFolder, dest);
        return dest;
    }

    /// <summary>根据存档内的 level.dat 读取版本信息（尽力而为）。</summary>
    public static string? DetectSaveVersion(string saveFolder)
    {
        try
        {
            var levelDat = Path.Combine(saveFolder, "level.dat");
            if (!File.Exists(levelDat))
                return null;
            // level.dat 是 gzip 压缩的 NBT，这里仅做启发式：检查文件名和大小，完整解析由 NBT 库处理
            // 简化实现：返回 null 表示无法自动确定，由用户选择
            return null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 光影 / 数据包 批量导入

    /// <summary>将单个光影包导入到目标实例的 shaderpacks 目录。</summary>
    public static string ImportShader(McInstance instance, string source, bool overwrite)
    {
        var shaderFolder = GetSubFolder(instance, "shaderpacks");
        EnsureFolder(shaderFolder);
        return CopyResourceItem(source, shaderFolder, overwrite);
    }

    /// <summary>将单个数据包导入到目标实例。数据包可放全局 datapacks 或世界内，这里放全局 datapacks 目录。</summary>
    public static string ImportDataPack(McInstance instance, string source, bool overwrite)
    {
        var dpFolder = GetSubFolder(instance, "datapacks");
        EnsureFolder(dpFolder);
        return CopyResourceItem(source, dpFolder, overwrite);
    }

    private static string CopyResourceItem(string source, string destFolder, bool overwrite)
    {
        var name = Path.GetFileName(source.TrimEnd('\\', '/'));
        var dest = Path.Combine(destFolder, name);

        if (File.Exists(source))
        {
            if (File.Exists(dest))
            {
                if (!overwrite)
                    throw new InvalidOperationException($"「{name}」已存在。");
                File.Delete(dest);
            }

            File.Copy(source, dest);
        }
        else if (Directory.Exists(source))
        {
            if (Directory.Exists(dest))
            {
                if (!overwrite)
                    throw new InvalidOperationException($"「{name}」已存在。");
                Directory.Delete(dest, true);
            }

            CopyDirectory(source, dest);
        }
        else
        {
            throw new InvalidOperationException("找不到要导入的文件或文件夹。");
        }

        return dest;
    }

    /// <summary>启用/禁用光影（重命名 shaderpacks 里的 zip 加 .disabled 后缀，与模组禁用策略一致）。</summary>
    public static void SetShaderEnabled(McInstance instance, string shaderName, bool enabled)
    {
        var folder = GetSubFolder(instance, "shaderpacks");
        var normal = Path.Combine(folder, shaderName);
        var disabled = normal + ".disabled";

        if (enabled)
        {
            if (File.Exists(disabled) && !File.Exists(normal))
                File.Move(disabled, normal);
            else if (Directory.Exists(disabled) && !Directory.Exists(normal))
                Directory.Move(disabled, normal);
        }
        else
        {
            if (File.Exists(normal))
                File.Move(normal, disabled);
            else if (Directory.Exists(normal))
                Directory.Move(normal, disabled);
        }
    }

    #endregion

    #region 预设

    /// <summary>记录一条预设到配置，返回是否新增。</summary>
    public static bool AddPreset(PresetEntry preset)
    {
        var existing = Config.Presets.FirstOrDefault(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.ModFiles = preset.ModFiles;
            existing.Description = preset.Description;
            SaveConfig();
            return false;
        }

        Config.Presets.Add(preset);
        SaveConfig();
        return true;
    }

    public static bool RemovePreset(string name)
    {
        var removed = Config.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            SaveConfig();
        return removed > 0;
    }

    /// <summary>将预设应用到目标实例的 mods 目录（从记录的文件列表复制）。</summary>
    public static int ApplyPreset(PresetEntry preset, McInstance instance, bool overwrite)
    {
        var modsFolder = GetSubFolder(instance, "mods");
        EnsureFolder(modsFolder);
        int count = 0;

        foreach (var src in preset.ModFiles)
        {
            if (!File.Exists(src))
                continue;

            var dest = Path.Combine(modsFolder, Path.GetFileName(src));
            if (File.Exists(dest) && !overwrite)
                continue;

            File.Copy(src, dest, true);
            count++;
        }

        return count;
    }

    /// <summary>从某个实例的 mods 目录收集所有 .jar 文件，生成一条预设。</summary>
    public static PresetEntry BuildPresetFromInstance(string presetName, McInstance sourceInstance, string description = "")
    {
        var modsFolder = GetSubFolder(sourceInstance, "mods");
        var files = Directory.Exists(modsFolder)
            ? Directory.GetFiles(modsFolder, "*.jar", SearchOption.TopDirectoryOnly).ToList()
            : new List<string>();

        return new PresetEntry
        {
            Name = presetName,
            Description = description,
            ModFiles = files
        };
    }

    #endregion

    #region 服务器收录

    /// <summary>内置收录的知名服务器（名称 + 地址 + 默认端口）。</summary>
    public static readonly List<PresetServerInfo> FeaturedServers = new()
    {
        new PresetServerInfo { Name = "Hypixel", Address = "mc.hypixel.net", Port = 25565, Category = "小游戏 / PvP" },
        new PresetServerInfo { Name = "Mineplex", Address = "us.mineplex.com", Port = 25565, Category = "小游戏" },
        new PresetServerInfo { Name = "2b2t", Address = "2b2t.org", Port = 25565, Category = "无政府生存" },
        new PresetServerInfo { Name = "CubeCraft", Address = "play.cubecraft.net", Port = 19132, Category = "小游戏（基岩）" },
        new PresetServerInfo { Name = "Wynncraft", Address = "play.wynncraft.com", Port = 25565, Category = "MMORPG" },
        new PresetServerInfo { Name = "Pixelmon Realms", Address = "play.pixelmonrealms.com", Port = 25565, Category = "宝可梦" },
        new PresetServerInfo { Name = "The Hive", Address = "play.hivemc.com", Port = 19132, Category = "小游戏（基岩）" },
        new PresetServerInfo { Name = "PVP Legacy", Address = "play.pvplegacy.net", Port = 25565, Category = "PvP 竞技" },
    };

    /// <summary>用户自定义的服务器列表。</summary>
    public static List<CustomServerInfo> CustomServers => Config.CustomServers;

    public static void AddCustomServer(string name, string address, int port = 25565, string category = "")
    {
        Config.CustomServers.RemoveAll(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase));
        Config.CustomServers.Add(new CustomServerInfo { Name = name, Address = address, Port = port, Category = category });
        SaveConfig();
    }

    public static void RemoveCustomServer(string address)
    {
        Config.CustomServers.RemoveAll(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase));
        SaveConfig();
    }

    /// <summary>将服务器写入指定实例的 servers.dat（NBT 格式）。</summary>
    public static async Task<bool> AddServerToInstanceAsync(McInstance instance, string name, string address, int port = 25565)
    {
        try
        {
            var serversDatPath = Path.Combine(instance.PathIndie, "servers.dat");
            var ip = port == 25565 ? address : $"{address}:{port}";

            NbtList nbtData;
            if (!File.Exists(serversDatPath))
            {
                nbtData = new NbtList("servers", NbtTagType.Compound);
            }
            else
            {
                nbtData = await NbtFileHandler.ReadTagInNbtFileAsync<NbtList>(serversDatPath, "servers") ?? new NbtList("servers", NbtTagType.Compound);
            }

            var server = new NbtCompound();
            server["name"] = new NbtString("name", name);
            server["ip"] = new NbtString("ip", ip);
            if (nbtData.Count == 0)
                nbtData.ListType = NbtTagType.Compound;
            nbtData.Add(server);

            var cloned = (NbtList)nbtData.Clone();
            await NbtFileHandler.WriteTagInNbtFileAsync(cloned, serversDatPath);
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"写入服务器 {name} 到实例失败");
            return false;
        }
    }

    #endregion

    #region 推荐

    /// <summary>根据内存大小推荐分配内存（GB）。</summary>
    public static int RecommendMemoryGb(long totalRamBytes)
    {
        double gb = totalRamBytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 32) return 8;
        if (gb >= 16) return 6;
        if (gb >= 8) return 4;
        if (gb >= 6) return 3;
        return 2;
    }

    /// <summary>根据 CPU 核心数与内存推荐光影档位。</summary>
    public static string RecommendShaderTier(int cpuCores, long totalRamBytes)
    {
        double gb = totalRamBytes / 1024.0 / 1024.0 / 1024.0;
        if (cpuCores >= 6 && gb >= 16) return "高（光追 / 体积云）";
        if (cpuCores >= 4 && gb >= 8) return "中（Sildur's Vibrant 中档 / Complementary）";
        return "低（BSL Low / 无光影，优化帧率优先）";
    }

    /// <summary>根据配置推荐优化模组列表（返回模组名 + 说明）。</summary>
    public static List<(string Name, string Desc)> RecommendMods(int cpuCores, long totalRamBytes, bool lowSpec)
    {
        var result = new List<(string, string)>
        {
            ("Sodium", "渲染优化，显著提升帧率"),
            ("Lithium", "游戏逻辑优化，降低卡顿"),
            ("Phosphor / Starlight", "光照引擎优化"),
            ("Iris Shaders", "光影加载（配合 Sodium）")
        };

        if (lowSpec)
        {
            result.Add(("FerriteCore", "内存占用优化，适合低配"));
            result.Add(("EntityCulling", "实体剔除，提升渲染性能"));
        }

        return result;
    }

    #endregion

    #region 攻略搜索

    /// <summary>根据关键词生成定向攻略搜索建议（内置 URL 模板，由 UI 展示/跳转）。</summary>
    public static List<(string Title, string Url)> BuildGuideLinks(string keyword)
    {
        var k = Uri.EscapeDataString(keyword);
        return new List<(string, string)>
        {
            ("B 站搜索（视频攻略）", $"https://search.bilibili.com/all?keyword={k}"),
            ("MC 百科搜索（模组/整合包资料）", $"https://www.mcmod.cn/search.html?key={keyword}"),
            ("CurseForge 整合包", $"https://www.curseforge.com/minecraft/search?search={k}"),
            ("Modrinth 搜索", $"https://modrinth.com/search?q={k}"),
            ("中文 Minecraft Wiki", $"https://zh.minecraft.wiki/w/{k}"),
        };
    }

    /// <summary>投影搜索建议（B 站 / 投影社区 / MCSchematic）。</summary>
    public static List<(string Title, string Url)> BuildSchematicLinks(string keyword)
    {
        var k = Uri.EscapeDataString(keyword);
        return new List<(string, string)>
        {
            ("MCSchematic 搜索", $"https://www.mcschematic.com/search?q={keyword}"),
            ("Planet Minecraft 地图", $"https://www.planetminecraft.com/resources/projects/?keywords={k}"),
            ("B 站投影教程/下载", $"https://search.bilibili.com/all?keyword={Uri.EscapeDataString(keyword + " 投影 litematic")}"),
        };
    }

    #endregion

    #region 转盘（热门整合包 / 光影包 / 数据包）

    /// <summary>转盘上的项目种类。</summary>
    public enum WheelItemKind
    {
        /// <summary>整合包（Modpack）。</summary>
        Modpack,
        /// <summary>光影包（Shader）。</summary>
        Shader,
        /// <summary>数据包（Datapack）。</summary>
        Datapack,
    }

    /// <summary>转盘内置的热门项目列表（名称 + 类型 + 官方下载页）。</summary>
    public static readonly List<WheelItem> FeaturedWheelItems = new()
    {
        // —— 整合包 ——
        new WheelItem { Name = "RLCraft", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/rlcraft" },
        new WheelItem { Name = "All the Mods 9", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/all-the-mods-9" },
        new WheelItem { Name = "Sky Factory 4", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/skyfactory-4" },
        new WheelItem { Name = "Enigmatica 9", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/enigmatica9" },
        new WheelItem { Name = "Pixelmon Modpack", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/the-pixelmon-modpack" },
        new WheelItem { Name = "GregTech New Horizons", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/gt-new-horizons" },
        new WheelItem { Name = "Create: Above and Beyond", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/create-above-and-beyond" },
        new WheelItem { Name = "Better MC", Kind = WheelItemKind.Modpack, Url = "https://www.curseforge.com/minecraft/modpacks/better-mc-forge" },

        // —— 光影包 ——
        new WheelItem { Name = "BSL Shaders", Kind = WheelItemKind.Shader, Url = "https://www.curseforge.com/minecraft/shaders/bsl-shaders" },
        new WheelItem { Name = "Complementary Shaders", Kind = WheelItemKind.Shader, Url = "https://www.curseforge.com/minecraft/shaders/complementary-reimagined" },
        new WheelItem { Name = "Sildur's Vibrant Shaders", Kind = WheelItemKind.Shader, Url = "https://www.curseforge.com/minecraft/shaders/sildurs-vibrant-shaders" },
        new WheelItem { Name = "SEUS PTGI", Kind = WheelItemKind.Shader, Url = "https://www.sonicether.com/seus/" },
        new WheelItem { Name = "Chocapic13' Shaders", Kind = WheelItemKind.Shader, Url = "https://www.curseforge.com/minecraft/shaders/chocapic13-shaders" },
        new WheelItem { Name = "Sodium (优化，非光影)", Kind = WheelItemKind.Shader, Url = "https://modrinth.com/mod/sodium" },

        // —— 数据包 ——
        new WheelItem { Name = "Terralith 地形生成", Kind = WheelItemKind.Datapack, Url = "https://modrinth.com/datapack/terralith" },
        new WheelItem { Name = "Incendium 下界拓展", Kind = WheelItemKind.Datapack, Url = "https://modrinth.com/datapack/incendium" },
        new WheelItem { Name = "Nullscape 末地拓展", Kind = WheelItemKind.Datapack, Url = "https://modrinth.com/datapack/nullscape" },
        new WheelItem { Name = "Wandering Trades", Kind = WheelItemKind.Datapack, Url = "https://modrinth.com/datapack/wandering-trades" },
    };

    /// <summary>根据项目类型返回中文类别标签。</summary>
    public static string KindLabel(WheelItemKind kind) => kind switch
    {
        WheelItemKind.Modpack => "整合包",
        WheelItemKind.Shader => "光影包",
        WheelItemKind.Datapack => "数据包",
        _ => "其他",
    };

    #endregion

    #region 模组版本匹配

    /// <summary>实例的加载器类型标识（用于 McConstraintMatcher 判定版本约束方言）。</summary>
    public static string GetLoaderType(McInstance inst)
    {
        var info = inst.Info;
        if (info.HasForge) return "Forge";
        if (info.HasNeoForge) return "NeoForge";
        if (info.HasQuilt) return "Quilt";
        if (info.HasFabric) return "Fabric";
        if (info.HasLegacyFabric) return "Fabric";
        return "";
    }

    /// <summary>扫描指定实例 mods 目录，逐一对每个模组判定其游戏版本是否匹配当前实例。</summary>
    public static List<ModVersionMatch> MatchModVersions(McInstance inst)
    {
        var result = new List<ModVersionMatch>();
        var mcVersion = inst.Info.VanillaName;
        var loaderType = GetLoaderType(inst);

        var modsFolder = GetSubFolder(inst, "mods");
        if (!Directory.Exists(modsFolder))
            return result;

        foreach (var file in Directory.GetFiles(modsFolder, "*.jar", SearchOption.TopDirectoryOnly))
        {
            result.Add(MatchSingleMod(file, mcVersion, loaderType));
        }

        return result;
    }

    /// <summary>判定单个 mod 文件（jar）的游戏版本约束是否匹配给定 MC 版本。</summary>
    public static ModVersionMatch MatchSingleMod(string jarPath, string mcVersion, string loaderType)
    {
        var item = new ModVersionMatch { FilePath = jarPath, FileName = Path.GetFileName(jarPath) };

        try
        {
            var comp = new ModLocalComp.LocalCompFile(jarPath);
            comp.Load();

            item.ModName = comp.Name ?? comp.ModId ?? "";
            item.ModId = comp.ModId ?? "";
            item.ModVersion = comp.Version ?? "";

            // 从依赖里取 minecraft 的版本约束（原始约束更适合 McConstraintMatcher）
            string? constraint = null;
            if (comp.DependencyRaw is not null && comp.DependencyRaw.TryGetValue("minecraft", out var raw))
                constraint = raw;
            if (string.IsNullOrWhiteSpace(constraint) && comp.Dependencies is not null &&
                comp.Dependencies.TryGetValue("minecraft", out var norm))
                constraint = norm;

            item.Constraint = constraint;

            if (string.IsNullOrWhiteSpace(mcVersion))
            {
                item.Status = ModMatchStatus.Unknown;
                item.Message = "无法确定实例游戏版本";
            }
            else if (string.IsNullOrWhiteSpace(constraint))
            {
                item.Status = ModMatchStatus.NoConstraint;
                item.Message = "未声明 Minecraft 版本要求（任意版本均可）";
            }
            else
            {
                var ok = McConstraintMatcher.Satisfies(constraint, loaderType, mcVersion);
                item.Status = ok ? ModMatchStatus.Matched : ModMatchStatus.Mismatched;
                item.Message = ok
                    ? $"版本约束 {constraint} 包含当前版本 {mcVersion}"
                    : $"版本约束 {constraint} 不包含当前版本 {mcVersion}";
            }
        }
        catch (Exception ex)
        {
            item.Status = ModMatchStatus.Error;
            item.Message = "解析失败：" + ex.Message;
        }

        return item;
    }

    #endregion
}

#region 数据模型

public class SymbioToolsConfig
{
    public List<PresetEntry> Presets { get; set; } = new();
    public List<CustomServerInfo> CustomServers { get; set; } = new();

    /// <summary>导入资源（地图/光影/数据包）时是否覆盖同名项。</summary>
    public bool OverwriteOnImport { get; set; } = true;

    /// <summary>联机 Portal 程序路径（空则自动检测）。</summary>
    public string PortalPath { get; set; } = "";
}

public class PresetEntry
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> ModFiles { get; set; } = new();
}

public class CustomServerInfo
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; } = 25565;
    public string Category { get; set; } = "";
}

public class PresetServerInfo
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; } = 25565;
    public string Category { get; set; } = "";
}

/// <summary>转盘上的一个项目。</summary>
public class WheelItem
{
    public string Name { get; set; } = "";
    public SymbioTools.WheelItemKind Kind { get; set; } = SymbioTools.WheelItemKind.Modpack;
    public string Url { get; set; } = "";
}

/// <summary>模组版本匹配状态。</summary>
public enum ModMatchStatus
{
    /// <summary>版本约束包含当前实例版本。</summary>
    Matched,
    /// <summary>版本约束不包含当前实例版本。</summary>
    Mismatched,
    /// <summary>未声明版本要求。</summary>
    NoConstraint,
    /// <summary>无法判断（缺少实例版本等）。</summary>
    Unknown,
    /// <summary>解析出错。</summary>
    Error,
}

/// <summary>单个模组的版本匹配结果。</summary>
public class ModVersionMatch
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ModName { get; set; } = "";
    public string ModId { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public string Constraint { get; set; } = "";
    public ModMatchStatus Status { get; set; } = ModMatchStatus.Unknown;
    public string Message { get; set; } = "";
}

#endregion
