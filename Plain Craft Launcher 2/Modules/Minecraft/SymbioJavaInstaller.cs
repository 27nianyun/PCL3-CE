using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft;
using PCL.Core.Utils.OS;

namespace PCL;

/// <summary>
///     PCL₃ 自动问题处理 —— Java 运行环境自动匹配与安装。
///     1. 按游戏版本 + 加载器自动映射所需 Java 大版本。
///     2. 本地内置的 Oracle JDK/JRE 安装包优先，静默安装到默认目录（C:\Program Files\Java\）。
///     3. 本地未覆盖的 Java 版本，复用 PCL 现有的 Mojang Java 下载能力自动下载。
///     全部在后台线程执行，UI 反馈通过 Hint 完成。
/// </summary>
public static class SymbioJavaInstaller
{
    /// <summary>
    ///     本地内置的 Oracle JDK/JRE 安装包清单。
    ///     Key 为 Java 主版本号，Value 为安装包绝对路径。
    ///     路径缺失时自动跳过，转由网络下载兜底。
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> LocalInstallerMap = BuildLocalInstallerMap();

    /// <summary>
    ///     本地安装包的候选路径列表（按用户提供顺序 + 常见默认位置）。
    ///     实际运行时以「文件存在」为准。
    /// </summary>
    private static readonly (int major, string path)[] LocalInstallerCandidates =
    {
        (21, @"G:\java\jdk-21_windows-x64_bin.exe"),
        (22, @"G:\java\jdk-22.0.2_windows-x64_bin.exe"),
        (23, @"G:\java\jdk-23.0.2_windows-x64_bin.exe"),
        (23, @"G:\java\jdk-23_windows-x64_bin.exe"),
        (24, @"G:\java\jdk-24.0.2_windows-x64_bin.exe"),
        (25, @"G:\java\jdk-25.0.5.0.2_windows-x64_bin (1).exe"),
        (8,  @"G:\java\jre-8u441-windows-x64.exe")
    };

    private static Dictionary<int, string> BuildLocalInstallerMap()
    {
        var map = new Dictionary<int, string>();
        foreach (var (major, path) in LocalInstallerCandidates)
        {
            // 同一主版本若存在多个包，优先选文件名含最新小版本的（简单启发式：取更长的、含 .0. 的更精确版本）
            if (map.TryGetValue(major, out var existing))
            {
                if (File.Exists(path) && !File.Exists(existing))
                {
                    map[major] = path;
                }
                else if (File.Exists(path) && File.Exists(existing))
                {
                    // 两个都存在时，优先选文件名带三段式版本号的（如 jdk-23.0.2 优先于 jdk-23）
                    if (path.Contains($"{major}.0") && !existing.Contains($"{major}.0"))
                        map[major] = path;
                }
            }
            else
            {
                map[major] = path;
            }
        }
        return map;
    }

    /// <summary>
    ///     根据 Minecraft 版本与加载器，映射出该实例所需的最低 Java 主版本号。
    ///     遵循 Minecraft 官方的 Java 版本要求：
    ///     1.6-1.11 → Java 8；1.12-1.16.5 → Java 8；
    ///     1.17 → Java 16；1.18-1.20.4 → Java 17；1.20.5+ → Java 21；
    ///     NeoForge 1.20.2+ 部分需要 Java 21；Fabric/Quilt 高版本通常要求 Java 21。
    /// </summary>
    public static int MapGameVersionToJavaMajor(McInstance instance)
    {
        var info = instance?.Info;
        var vanilla = info?.VanillaName ?? string.Empty;

        // 优先按加载器最高要求判断
        var loaderMajor = 0;
        if (info is not null)
        {
            // NeoForge 21.1+ 需要 Java 21
            if (info.HasNeoForge)
                loaderMajor = Math.Max(loaderMajor, 21);
            // Fabric/Quilt 较新版本通常要求 Java 21（保守按 17，加载器会自行报错）
            if (info.HasFabric || info.HasQuilt)
                loaderMajor = Math.Max(loaderMajor, 17);
            // Forge 高版本（1.20.5+）需要 Java 21，中版本需要 Java 17
            if (info.HasForge)
                loaderMajor = Math.Max(loaderMajor, 17);
            if (info.HasLiteLoader)
                loaderMajor = Math.Max(loaderMajor, 8);
        }

        // 按原版版本号解析
        var major = 0;
        if (!string.IsNullOrWhiteSpace(vanilla))
        {
            major = ParseVanillaJavaMajor(vanilla);
        }

        return Math.Max(major, loaderMajor);
    }

    /// <summary>
    ///     解析原版版本号对应的最低 Java 主版本。
    /// </summary>
    private static int ParseVanillaJavaMajor(string vanilla)
    {
        // 规范化：去除快照/前缀，取 "1.X.Y" 中的 X 与 Y
        var cleaned = vanilla.Trim();
        // 形如 "1.20.4" 或 "1.17" 或 "1.16.5"
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return 8;

        if (int.TryParse(parts[1], out var minor))
        {
            // minor 是第二个数字（1.X 中的 X）
            if (minor >= 21) return 21;               // 1.21+（1.20.5 也归这里）
            if (minor == 20)
            {
                // 1.20.5+ 需要 Java 21，1.20.4 及以下需要 Java 17
                if (parts.Length >= 3 && int.TryParse(parts[2], out var patch20) && patch20 >= 5)
                    return 21;
                return 17;
            }
            if (minor >= 18) return 17;               // 1.18 - 1.20.4 → Java 17
            if (minor == 17) return 16;               // 1.17 → Java 16
            return 8;                                  // 1.16.5 及以下 → Java 8
        }

        return 8;
    }

    /// <summary>
    ///     判断实例所需 Java 是否已经本地可用（存在版本 >= 要求的已启用 Java）。
    /// </summary>
    public static bool IsRequiredJavaAvailable(McInstance instance, out int requiredMajor)
    {
        requiredMajor = MapGameVersionToJavaMajor(instance);
        var minVersion = new Version(requiredMajor, 0, 0);
        var entries = ModJava.Javas.SelectSuitableJavaAsync(minVersion, null).GetAwaiter().GetResult();
        return entries.Length > 0;
    }

    /// <summary>
    ///     确保实例所需的 Java 已安装。若缺失，优先用本地安装包静默安装，
    ///     本地无对应版本则复用 PCL 的 Mojang 下载能力。
    ///     返回是否已成功确保（或无需处理）。
    /// </summary>
    public static void EnsureJavaInstalled(McInstance instance)
    {
        try
        {
            var requiredMajor = MapGameVersionToJavaMajor(instance);
            if (IsRequiredJavaAvailable(instance, out _))
            {
                HintService.Hint($"已检测到符合要求的 Java {requiredMajor}，无需安装。", HintType.Success);
                return;
            }

            HintService.Hint($"正在为实例「{instance.Name}」准备 Java {requiredMajor}…", HintType.Info);

            ModBase.RunInNewThread(() =>
            {
                try
                {
                    var installed = TryInstallFromLocalPackage(requiredMajor);
                    if (installed)
                    {
                        // 静默安装成功后重新扫描，让 JavaManager 识别新安装的 Java
                        ModJava.Javas.ScanJavaAsync(true).GetAwaiter().GetResult();
                        ModBase.RunInUi(() =>
                            HintService.Hint($"Java {requiredMajor} 已从本地安装包安装完成。", HintType.Success));
                        return;
                    }

                    // 本地无对应安装包，转由网络下载（复用 PCL 的 Mojang Java 下载）
                    DownloadJavaFromNetwork(requiredMajor);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, $"[PCL₃] 自动安装 Java {requiredMajor} 失败");
                    ModBase.RunInUi(() =>
                        HintService.Hint($"自动安装 Java {requiredMajor} 失败：" + ex.Message, HintType.Error));
                }
            });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PCL₃] 确保 Java 安装失败");
            ModBase.RunInUi(() => HintService.Hint("确保 Java 安装失败：" + ex.Message, HintType.Error));
        }
    }

    /// <summary>
    ///     尝试用本地 Oracle 安装包静默安装指定主版本的 Java。
    ///     返回是否成功触发安装（注意：静默安装是异步的，这里只负责启动安装进程并等待）。
    /// </summary>
    private static bool TryInstallFromLocalPackage(int requiredMajor)
    {
        if (!LocalInstallerMap.TryGetValue(requiredMajor, out var installerPath))
        {
            ModBase.Log($"[PCL₃] 本地无 Java {requiredMajor} 安装包，转由网络下载");
            return false;
        }

        if (!File.Exists(installerPath))
        {
            ModBase.Log($"[PCL₃] 本地安装包不存在：{installerPath}，转由网络下载");
            return false;
        }

        ModBase.Log($"[PCL₃] 使用本地安装包安装 Java {requiredMajor}：{installerPath}");
        try
        {
            // Oracle JDK/JRE 安装器支持 /s 静默安装到默认目录（C:\Program Files\Java\）。
            var psi = new ProcessStartInfo(installerPath, "/s")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;
            // 等待安装完成（Oracle 安装器 /s 模式下进程会阻塞到安装结束）
            proc.WaitForExit();
            var exitCode = proc.ExitCode;
            ModBase.Log($"[PCL₃] 本地 Java 安装进程退出码：{exitCode}");
            // 即使退出码非 0，也可能已部分安装；交由后续扫描判定
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[PCL₃] 运行本地安装包失败：{installerPath}");
            return false;
        }
    }

    /// <summary>
    ///     复用 PCL 的 Mojang Java 下载能力，从网络下载指定主版本的 Java runtime。
    /// </summary>
    private static void DownloadJavaFromNetwork(int requiredMajor)
    {
        ModBase.Log($"[PCL₃] 从网络下载 Java {requiredMajor}");
        if (!ModJava.JavaDownloadConfirm($"Java {requiredMajor}"))
        {
            ModBase.RunInUi(() => HintService.Hint("已取消 Java 下载。", HintType.Info));
            return;
        }

        var javaLoader = ModJava.GetJavaDownloadLoader();
        javaLoader.Start(requiredMajor.ToString(), true);

        // 下载完成后重新扫描
        ModJava.Javas.ScanJavaAsync(true).GetAwaiter().GetResult();
        ModBase.RunInUi(() => HintService.Hint($"Java {requiredMajor} 下载安装完成。", HintType.Success));
    }

    /// <summary>
    ///     获取本地可用的安装包版本清单（用于诊断展示/日志）。
    /// </summary>
    public static IReadOnlyList<int> GetLocalAvailableMajors() =>
        LocalInstallerMap
            .Where(kv => File.Exists(kv.Value))
            .Select(kv => kv.Key)
            .OrderBy(x => x)
            .ToList();
}
