using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft;
using PCL.Core.Minecraft.ResourceProject;
using PCL.Core.UI;
using PCL.Network;
using PCL.Network.Loaders;

using CompFile = PCL.ModComp.CompFile;
using CompLoaderType = PCL.ModComp.CompLoaderType;
using LocalCompFile = PCL.ModLocalComp.LocalCompFile;

namespace PCL;

/// <summary>
///     Symbio Edition 扩展功能：
///     1. 自动获取前置模组（基于既有 ModDependencyResolver + Modrinth/CurseForge）。
///     2. 自动分析报错原因并执行优化（本地规则引擎 + 可选 LLM 深度分析）。
///     全部复用 PCL 现有 API，不改动既有功能与配色。
/// </summary>
public static class SymbioAutoDependency
{
    /// <summary>
    ///     为指定实例构建一次性的「全量依赖解析」请求。
    ///     汇总该实例 mods 文件夹内所有已装模组的必需/可选依赖，作为待解析项。
    /// </summary>
    public static ModDependencyRequest BuildDependencyRequest(McInstance instance, string modsFolder)
    {
        var installed = ModCompDependency.ScanInstalledMods(modsFolder);
        var references = new List<ModDependencyReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(modsFolder))
        {
            foreach (var path in Directory.GetFiles(modsFolder))
            {
                if (!LocalCompFile.IsModFile(path))
                    continue;

                var lf = new LocalCompFile(path);
                lf.Load();
                var compFile = lf.compFile;
                var source = lf.Comp?.FromCurseForge == true ? "CurseForge" : "Modrinth";

                if (compFile?.Dependencies != null)
                    foreach (var dep in compFile.Dependencies)
                        if (seen.Add(dep))
                            references.Add(new ModDependencyReference { ProjectId = dep, Source = source, IsRequired = true });

                if (compFile?.OptionalDependencies != null)
                    foreach (var dep in compFile.OptionalDependencies)
                        if (seen.Add(dep))
                            references.Add(new ModDependencyReference { ProjectId = dep, Source = source, IsRequired = false });
            }
        }

        var info = instance.Info;
        if (info is null)
            return new ModDependencyRequest
            {
                RequiredDependencies = references,
                InstalledMods = installed,
                ProjectResolver = ModCompDependency.ResolveProjectFiles
            };

        var targetLoaders = new List<string>();
        if (info.HasForge) targetLoaders.Add(nameof(CompLoaderType.Forge));
        if (info.HasFabric) targetLoaders.Add(nameof(CompLoaderType.Fabric));
        if (info.HasQuilt) targetLoaders.Add(nameof(CompLoaderType.Quilt));
        if (info.HasNeoForge) targetLoaders.Add(nameof(CompLoaderType.NeoForge));
        if (info.HasLiteLoader) targetLoaders.Add(nameof(CompLoaderType.LiteLoader));

        return new ModDependencyRequest
        {
            TargetMinecraftVersion = info.VanillaName ?? string.Empty,
            TargetLoaders = targetLoaders,
            RequiredDependencies = references,
            InstalledMods = installed,
            ProjectResolver = ModCompDependency.ResolveProjectFiles
        };
    }

    /// <summary>
    ///     一键自动获取当前实例缺失的前置模组并下载安装。
    ///     在后台线程调用（含网络请求），UI 反馈通过 Hint 与任务栏加载器完成。
    /// </summary>
    public static void AutoFetchDependencies(McInstance instance)
    {
        try
        {
            var modsFolder = instance.PathIndie + @"mods\";
            if (!Directory.Exists(modsFolder))
            {
                ModBase.RunInUi(() => HintService.Hint("该实例没有 mods 文件夹，无法获取前置模组。", HintType.Error));
                return;
            }

            HintService.Hint("正在检查前置模组依赖…", HintType.Info);

            ModBase.RunInNewThread(() =>
            {
                try
                {
                    var request = BuildDependencyRequest(instance, modsFolder);
                    var result = new ModDependencyResolver().Resolve(request);

                    ModBase.RunInUi(() =>
                    {
                        if (result.ToInstall.Count == 0 && result.Unresolved.Count == 0)
                        {
                            HintService.Hint("所有前置模组均已满足，无需下载。", HintType.Success);
                            return;
                        }

                        var choice = ModCompDependency.ConfirmDependencyInstall(result);
                        if (choice is ModComp.CompDepsInstallTypes.Cancel or ModComp.CompDepsInstallTypes.WithoutDeps)
                            return;

                        if (result.ToInstall.Count == 0)
                        {
                            HintService.Hint("未发现可自动下载的前置（存在无法解析的依赖）。", HintType.Error);
                            return;
                        }

                        var depDownloads = ModCompDependency.BuildDependencyDownloads(result, modsFolder);
                        foreach (var (depFilename, downloadFile) in depDownloads)
                        {
                            var depLoaders = new List<ModLoader.LoaderBase>
                            {
                                new LoaderDownload(Lang.Text("Download.Comp.Detail.DownloadFile"),
                                    new List<DownloadFile> { downloadFile })
                                {
                                    ProgressWeight = 6,
                                    block = true
                                }
                            };

                            var depLoader = new ModLoader.LoaderCombo<int>("自动获取前置：" + depFilename, depLoaders);
                            depLoader.OnStateChanged = ModDownloadLib.LoaderStateChangedHintOnly;
                            depLoader.Start(1);
                            ModLoader.LoaderTaskbarAdd(depLoader);
                        }

                        HintService.Hint($"已开始自动下载 {depDownloads.Count} 个前置模组。", HintType.Success);
                    });
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "解析或下载前置模组失败");
                    ModBase.RunInUi(() => HintService.Hint("获取前置模组失败：" + ex.Message, HintType.Error));
                }
            });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "自动获取前置模组失败");
            ModBase.RunInUi(() => HintService.Hint("自动获取前置模组失败：" + ex.Message, HintType.Error));
        }
    }
}

/// <summary>
///     故障诊断与优化：分析实例日志 / 崩溃原因，并给出可执行优化建议。
/// </summary>
public static class SymbioDiagnostics
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum DiagnosticFixKind
    {
        None,
        FetchDeps,
        OpenSettings
    }

    /// <summary>
    ///     「一键解决」可自动执行的修复动作类型。
    /// </summary>
    public enum DiagnosticActionKind
    {
        /// <summary>无可自动执行的动作（仅展示建议）。</summary>
        None,
        /// <summary>自动补充缺失的前置模组。</summary>
        FetchDeps,
        /// <summary>禁用冲突/重复模组（ActionParam 为要禁用的模组文件名，可逗号分隔多个）。</summary>
        DisableMod,
        /// <summary>优化内存分配（调大分配内存或切回自动）。</summary>
        OptimizeMemory,
        /// <summary>自动匹配并下载/安装符合游戏版本要求的 Java 运行环境。</summary>
        InstallJava,
        /// <summary>打开实例设置页由用户手动调整（Java/加载器等）。</summary>
        OpenSettings
    }

    public sealed record DiagnosticFinding(
        DiagnosticSeverity Severity,
        string Title,
        string Detail,
        DiagnosticFixKind FixKind,
        DiagnosticActionKind ActionKind = DiagnosticActionKind.None,
        string? ActionParam = null);

    public sealed record DiagnosticReport(List<DiagnosticFinding> Findings);

    public static DiagnosticReport RunFullDiagnostics(McInstance instance)
    {
        var findings = new List<DiagnosticFinding>();

        // 1. 缺失前置模组（复用依赖解析）
        try
        {
            var modsFolder = instance.PathIndie + @"mods\";
            if (Directory.Exists(modsFolder))
            {
                var request = SymbioAutoDependency.BuildDependencyRequest(instance, modsFolder);
                var result = new ModDependencyResolver().Resolve(request);
                foreach (var u in result.Unresolved)
                    findings.Add(new DiagnosticFinding(
                        DiagnosticSeverity.Error,
                        "缺少前置模组（无法解析）",
                        $"项目 {u.ProjectId}（{u.Source}）：{u.Reason}。可尝试手动安装或在模组市场搜索。",
                        DiagnosticFixKind.FetchDeps,
                        DiagnosticActionKind.FetchDeps));
                if (result.ToInstall.Count > 0)
                    findings.Add(new DiagnosticFinding(
                        DiagnosticSeverity.Warning,
                        "存在可自动安装的前置模组",
                        $"检测到 {result.ToInstall.Count} 个缺失但可解析的前置模组，可点击「自动获取缺失前置」一键安装。",
                        DiagnosticFixKind.FetchDeps,
                        DiagnosticActionKind.FetchDeps));
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "诊断前置依赖失败");
        }

        // 2. 日志扫描
        var logPath = instance.PathInstance + @"logs\latest.log";
        if (File.Exists(logPath))
        {
            string log;
            try
            {
                log = ModBase.ReadFile(logPath);
            }
            catch
            {
                log = string.Empty;
            }

            if (!string.IsNullOrEmpty(log))
            {
                AnalyzeLog(log, findings);
                TryAppendLlmAnalysis(log, findings);
            }
        }

        // 2.5 Java 运行环境缺失检测：按游戏版本映射所需 Java，本地缺失则提示可一键安装
        try
        {
            var requiredMajor = SymbioJavaInstaller.MapGameVersionToJavaMajor(instance);
            if (!SymbioJavaInstaller.IsRequiredJavaAvailable(instance, out _))
            {
                var localMajors = SymbioJavaInstaller.GetLocalAvailableMajors();
                var localHint = localMajors.Count > 0
                    ? $"本地内置安装包可覆盖 Java {string.Join("/", localMajors)}"
                    : "本地无内置安装包，将从网络下载";
                findings.Add(new DiagnosticFinding(
                    DiagnosticSeverity.Warning,
                    $"缺少 Java {requiredMajor} 运行环境",
                    $"该实例（游戏版本 {(instance.Info?.VanillaName ?? "未知")}）需要 Java {requiredMajor}，但本地未检测到可用环境。可一键自动匹配并安装（{localHint}）。",
                    DiagnosticFixKind.OpenSettings,
                    DiagnosticActionKind.InstallJava));
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "诊断 Java 运行环境失败");
        }

        if (findings.Count == 0)
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Info,
                "未发现问题",
                "当前实例未检测到明显的报错或缺失前置模组。若仍无法启动，请检查 Java 与内存设置。",
                DiagnosticFixKind.None));

        return new DiagnosticReport(findings);
    }

    /// <summary>
    ///     基于传入的日志文本（崩溃弹窗中已收集的 LogAll）直接分析，并叠加实例层面的前置模组检查。
    ///     供崩溃弹窗「自动分析并优化」按钮使用，避免重复读取磁盘日志。
    /// </summary>
    public static DiagnosticReport RunLogDiagnostics(McInstance? instance, string logText)
    {
        var findings = new List<DiagnosticFinding>();

        // 1. 前置模组检查（仅当有实例且 mods 目录存在）
        if (instance is not null)
        {
            try
            {
                var modsFolder = instance.PathIndie + @"mods\";
                if (Directory.Exists(modsFolder))
                {
                    var request = SymbioAutoDependency.BuildDependencyRequest(instance, modsFolder);
                    var result = new ModDependencyResolver().Resolve(request);
                    foreach (var u in result.Unresolved)
                        findings.Add(new DiagnosticFinding(
                            DiagnosticSeverity.Error,
                            "缺少前置模组（无法解析）",
                            $"项目 {u.ProjectId}（{u.Source}）：{u.Reason}。可尝试手动安装或在模组市场搜索。",
                            DiagnosticFixKind.FetchDeps,
                            DiagnosticActionKind.FetchDeps));
                    if (result.ToInstall.Count > 0)
                        findings.Add(new DiagnosticFinding(
                            DiagnosticSeverity.Warning,
                            "存在可自动安装的前置模组",
                            $"检测到 {result.ToInstall.Count} 个缺失但可解析的前置模组，可点击「自动获取缺失前置」一键安装。",
                            DiagnosticFixKind.FetchDeps,
                            DiagnosticActionKind.FetchDeps));
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "诊断前置依赖失败");
            }
        }

        // 2. 日志分析（复用同一套规则）
        if (!string.IsNullOrEmpty(logText))
        {
            AnalyzeLog(logText, findings);
            TryAppendLlmAnalysis(logText, findings);
        }

        if (findings.Count == 0)
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Info,
                "未发现问题",
                "未从日志中检测到明显报错模式。请结合崩溃弹窗上方给出的原因说明进一步排查。",
                DiagnosticFixKind.None));

        return new DiagnosticReport(findings);
    }

    private static void AnalyzeLog(string log, List<DiagnosticFinding> findings)
    {
        if (log.Contains("OutOfMemoryError") || log.Contains("Java heap space"))
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Warning,
                "内存不足（OutOfMemoryError）",
                "游戏分配的内存不足以运行当前模组组合。可一键调高分配内存（如 4GB 以上）。",
                DiagnosticFixKind.OpenSettings,
                DiagnosticActionKind.OptimizeMemory));

        if (log.Contains("UnsupportedClassVersionError"))
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Warning,
                "Java 版本不匹配",
                "模组需要更高版本的 Java（如 Java 17 / 21）。可一键自动匹配并下载安装符合游戏要求的 Java 运行环境。",
                DiagnosticFixKind.OpenSettings,
                DiagnosticActionKind.InstallJava));

        if (log.Contains("ClassNotFoundException") || log.Contains("NoClassDefFoundError"))
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Error,
                "缺失类 / 模组",
                "启动过程中找不到关键类，通常是缺少前置模组或模组文件损坏。可一键自动获取前置。",
                DiagnosticFixKind.FetchDeps,
                DiagnosticActionKind.FetchDeps));

        // 模组冲突：尝试从日志中提取冲突/报错的模组名，供「一键禁用」使用
        var conflictingMods = ExtractConflictingMods(log);
        if (conflictingMods.Count > 0)
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Error,
                "模组冲突（Mixin 注入失败）",
                "多个模组在修改同一游戏内容时发生冲突。检测到可能冲突的模组：" +
                string.Join("、", conflictingMods) + "，可一键禁用它们（改 .disabled 后缀，可随时恢复）。",
                DiagnosticFixKind.None,
                DiagnosticActionKind.DisableMod,
                string.Join(",", conflictingMods)));

        if ((log.Contains("Forge") && log.Contains("Fabric")) ||
            (log.Contains("neoforge") && log.Contains("fabric")))
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Error,
                "加载器冲突",
                "日志中同时出现 Forge / NeoForge 与 Fabric 相关错误，加载器不兼容。请确认实例仅使用一种加载器。",
                DiagnosticFixKind.OpenSettings,
                DiagnosticActionKind.OpenSettings));

        if (log.Contains("Could not find or load main class"))
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Error,
                "主类缺失",
                "核心文件可能损坏或未完整下载。建议使用实例「补全文件」功能修复。",
                DiagnosticFixKind.None,
                DiagnosticActionKind.OpenSettings));

        if (log.Contains("exit code") && !log.Contains("exit code 0"))
            findings.Add(new DiagnosticFinding(
                DiagnosticSeverity.Warning,
                "游戏以非零状态码退出",
                "游戏进程异常退出。结合上方其他错误判断具体原因（多为内存、Java 或模组冲突）。",
                DiagnosticFixKind.None));
    }

    /// <summary>
    ///     从日志中提取可能发生冲突的模组名（mod id / 文件名）。
    ///     覆盖 Mixin 注入失败、NoSuchMethod/Field、重复模组等常见冲突特征。
    /// </summary>
    private static List<string> ExtractConflictingMods(string log)
    {
        var result = new List<string>();
        var hasConflictSignal =
            log.Contains("mixin", StringComparison.OrdinalIgnoreCase) ||
            log.Contains("NoSuchFieldError") ||
            log.Contains("NoSuchMethodError") ||
            log.Contains("DuplicateModsFound", StringComparison.OrdinalIgnoreCase) ||
            log.Contains("Found a duplicate mod", StringComparison.OrdinalIgnoreCase) ||
            log.Contains("incompatible", StringComparison.OrdinalIgnoreCase);
        if (!hasConflictSignal)
            return result;

        // 1) 重复模组：典型的 Fabric 日志 "Found a duplicate mod <id>"
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     log, @"(?:duplicate mod|DuplicateModsFound)[^\w\-.]*([\w\-\.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!result.Contains(m.Groups[1].Value))
                result.Add(m.Groups[1].Value);

        // 2) Mixin 应用失败：常见 "Mixin apply for mod <id> failed" / "Mixin apply failed"
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     log, @"mixin\s+(?:apply|prepare)?\s*for\s+mod\s+([\w\-\.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!result.Contains(m.Groups[1].Value))
                result.Add(m.Groups[1].Value);

        // 3) 崩溃栈中的模组包名：形如 "net.minecraftforge.fml" 或 "mods.<name>" 等，此处仅提取明显带 "mods" 前缀的
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     log, @"\bmods\.([a-zA-Z0-9_\-\.]+)"))
        {
            var name = m.Groups[1].Value.Split('.')[0];
            if (!result.Contains(name))
                result.Add(name);
        }

        return result;
    }

    private static void TryAppendLlmAnalysis(string log, List<DiagnosticFinding> findings)
    {
        var analysis = AnalyzeLogWithLlm(log);
        if (string.IsNullOrWhiteSpace(analysis))
            return;

        var summary = analysis.Length > 1200 ? analysis[..1200] + "…" : analysis;
        findings.Add(new DiagnosticFinding(
            DiagnosticSeverity.Info,
            "AI 深度分析（可选）",
            summary,
            DiagnosticFixKind.None));
    }

    public static void ShowReport(McInstance? instance, DiagnosticReport report)
    {
        var sb = new StringBuilder();
        foreach (var f in report.Findings)
            sb.AppendLine($"[{SeverityText(f.Severity)}] {f.Title}\n  {f.Detail}\n");

        var text = sb.ToString();
        var buttons = new List<MsgBoxButtonInfo>
        {
            new(Lang.Text("Common.Action.Confirm"), 1)
        };

        // 「一键解决所有问题」：只要有任一可自动执行的动作就提供
        var actionable = report.Findings
            .Where(f => f.ActionKind is DiagnosticActionKind.FetchDeps
                or DiagnosticActionKind.DisableMod
                or DiagnosticActionKind.OptimizeMemory
                or DiagnosticActionKind.InstallJava)
            .ToList();
        if (actionable.Count > 0)
        {
            buttons.Add(new MsgBoxButtonInfo("一键解决所有问题", 2, () =>
            {
                if (instance is null)
                    return;
                var confirm = MsgBoxWrapper.Show(
                    BuildFixPlanSummary(actionable),
                    "一键解决",
                    MsgBoxTheme.Warning,
                    true,
                    Lang.Text("Common.Action.Confirm"),
                    Lang.Text("Common.Action.Cancel"));
                if (confirm != 1)
                    return;
                ApplyAllFixes(instance, report);
            }));
        }

        var hasFetch = report.Findings.Any(f => f.FixKind == DiagnosticFixKind.FetchDeps);
        if (hasFetch && instance is not null)
            buttons.Add(new MsgBoxButtonInfo("自动获取缺失前置", 3,
                () => SymbioAutoDependency.AutoFetchDependencies(instance)));

        if (instance is not null)
            buttons.Add(new MsgBoxButtonInfo("打开实例设置", 4, () =>
            {
                PageInstanceLeft.McInstance = instance;
                ModBase.RunInUi(() => ModMain.frmMain.PageChange(
                    FormMain.PageType.InstanceSetup,
                    FormMain.PageSubType.VersionInstall));
            }));

        MsgBoxWrapper.ShowWithCustomButtons(text, "故障诊断与优化", MsgBoxTheme.Info, true, buttons);
    }

    /// <summary>
    ///     生成「一键解决」执行前的方案摘要，供用户确认。
    /// </summary>
    private static string BuildFixPlanSummary(List<DiagnosticFinding> actionable)
    {
        var sb = new StringBuilder();
        sb.AppendLine("即将自动执行以下修复：");
        foreach (var f in actionable)
            sb.AppendLine("  · " + FixActionText(f));
        sb.AppendLine();
        sb.AppendLine("注意：禁用模组会将其文件改为 .disabled 后缀（可手动恢复）；补充前置与调内存均为可逆操作。是否继续？");
        return sb.ToString();
    }

    private static string FixActionText(DiagnosticFinding f) => f.ActionKind switch
    {
        DiagnosticActionKind.FetchDeps => "自动补充缺失的前置模组",
        DiagnosticActionKind.DisableMod => "禁用冲突模组：" + (string.IsNullOrEmpty(f.ActionParam) ? "（未识别具体模组）" : f.ActionParam),
        DiagnosticActionKind.OptimizeMemory => "调高游戏分配内存（优化 OOM）",
        DiagnosticActionKind.InstallJava => "自动匹配并下载安装符合要求的 Java 运行环境",
        _ => f.Title
    };

    /// <summary>
    ///     一键执行所有可自动修复的动作。
    ///     全部为可逆操作：补前置（新增文件）、禁用模组（改后缀）、调内存（改配置）。
    /// </summary>
    public static void ApplyAllFixes(McInstance instance, DiagnosticReport report)
    {
        var modsFolder = instance.PathIndie + @"mods\";
        var executed = new List<string>();
        var failed = new List<string>();

        ModBase.RunInNewThread(() =>
        {
            try
            {
                // 1. 禁用冲突模组（先执行，避免与补前置相互影响）
                foreach (var f in report.Findings.Where(f => f.ActionKind == DiagnosticActionKind.DisableMod))
                {
                    var names = (f.ActionParam ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var disabledPaths = DisableModsByName(modsFolder, names);
                    if (disabledPaths.Count > 0)
                    {
                        executed.Add($"已禁用 {disabledPaths.Count} 个冲突模组");
                        foreach (var p in disabledPaths)
                            SymbioIssues.RecordOptimization("禁用冲突模组", "DisableMod", p);
                    }
                    else
                    {
                        failed.Add("未能定位到要禁用的冲突模组：" + (f.ActionParam ?? "未知"));
                    }
                }

                // 2. 优化内存
                foreach (var f in report.Findings.Where(f => f.ActionKind == DiagnosticActionKind.OptimizeMemory))
                {
                    if (OptimizeMemory(instance))
                    {
                        executed.Add("已调高分配内存");
                        SymbioIssues.RecordOptimization("优化内存", "OptimizeMemory",
                            $"实例 {instance.Name} 分配内存已调高到 4GB");
                    }
                    else
                    {
                        failed.Add("内存优化未执行（当前配置可能已足够）");
                    }
                }

                // 2.5 自动安装 Java（当诊断出 Java 版本不匹配时）
                foreach (var f in report.Findings.Where(f => f.ActionKind == DiagnosticActionKind.InstallJava))
                {
                    try
                    {
                        var requiredMajor = SymbioJavaInstaller.MapGameVersionToJavaMajor(instance);
                        if (SymbioJavaInstaller.IsRequiredJavaAvailable(instance, out _))
                        {
                            executed.Add($"Java {requiredMajor} 已满足要求，无需安装");
                        }
                        else
                        {
                            SymbioJavaInstaller.EnsureJavaInstalled(instance);
                            executed.Add($"已触发 Java {requiredMajor} 自动匹配安装");
                            SymbioIssues.RecordOptimization("自动安装 Java", "InstallJava",
                                $"实例 {instance.Name} 已匹配安装 Java {requiredMajor}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(ex, "一键解决：自动安装 Java 失败");
                        failed.Add("自动安装 Java 失败：" + ex.Message);
                    }
                }

                // 3. 补充前置模组（最后，可能触发网络下载）
                foreach (var f in report.Findings.Where(f => f.ActionKind == DiagnosticActionKind.FetchDeps))
                {
                    try
                    {
                        var request = SymbioAutoDependency.BuildDependencyRequest(instance, modsFolder);
                        var result = new ModDependencyResolver().Resolve(request);
                        if (result.ToInstall.Count == 0)
                            continue;
                        var downloads = ModCompDependency.BuildDependencyDownloads(result, modsFolder);
                        foreach (var (depFilename, downloadFile) in downloads)
                        {
                            var depLoaders = new List<ModLoader.LoaderBase>
                            {
                                new LoaderDownload(Lang.Text("Download.Comp.Detail.DownloadFile"),
                                    new List<DownloadFile> { downloadFile })
                                {
                                    ProgressWeight = 6,
                                    block = true
                                }
                            };
                            var depLoader = new ModLoader.LoaderCombo<int>("一键解决：补充前置 " + depFilename, depLoaders);
                            depLoader.OnStateChanged = ModDownloadLib.LoaderStateChangedHintOnly;
                            depLoader.Start(1);
                            ModLoader.LoaderTaskbarAdd(depLoader);
                        }
                        executed.Add($"已开始补充 {downloads.Count} 个前置模组");
                        SymbioIssues.RecordOptimization("补充前置模组", "FetchDeps",
                            $"实例 {instance.Name} 补充 {downloads.Count} 个前置模组");
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(ex, "一键解决：补充前置失败");
                        failed.Add("补充前置失败：" + ex.Message);
                    }
                }

                ModBase.RunInUi(() =>
                {
                    var summary = new StringBuilder();
                    if (executed.Count > 0)
                        summary.AppendLine("已完成的修复：\n  " + string.Join("\n  ", executed));
                    if (failed.Count > 0)
                        summary.AppendLine("\n未能自动处理：\n  " + string.Join("\n  ", failed));
                    if (executed.Count == 0 && failed.Count == 0)
                        summary.AppendLine("没有可自动执行的动作，请根据诊断建议手动处理。");
                    summary.AppendLine("\n提示：本次操作已记录到「更多 → 问题板块」，可在其中查看记录并撤销。");

                    MsgBoxWrapper.Show(summary.ToString(), "一键解决结果", MsgBoxTheme.Info, true);
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "一键解决失败");
                ModBase.RunInUi(() => HintService.Hint("一键解决失败：" + ex.Message, HintType.Error));
            }
        });
    }

    /// <summary>
    ///     在 mods 目录中禁用指定的模组（按 mod id 或文件名模糊匹配），
    ///     通过给文件追加 .disabled 后缀实现（可逆）。返回被禁用的文件路径列表（含 .disabled 后缀）。
    /// </summary>
    private static List<string> DisableModsByName(string modsFolder, string[] names)
    {
        var result = new List<string>();
        if (!Directory.Exists(modsFolder) || names.Length == 0)
            return result;

        var files = Directory.GetFiles(modsFolder);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                // 跳过已禁用的
                if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                    continue;
                // 模糊匹配：文件名包含该 mod id
                if (fileName.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var target = file + ".disabled";
                if (File.Exists(target))
                    continue;
                try
                {
                    File.Move(file, target);
                    ModBase.Log($"[Symbio] 已禁用冲突模组：{fileName}");
                    result.Add(target);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, $"[Symbio] 禁用模组失败：{fileName}");
                }
            }
        }

        return result;
    }

    /// <summary>
    ///     优化实例的内存分配：若当前为手动配置且内存偏小，调高到更充裕的值；
    ///     若跟随全局，则切换到手动并设为充裕值。目标至少 4GB（受 32 位 Java 限制时放宽）。
    /// </summary>
    private static bool OptimizeMemory(McInstance instance)
    {
        try
        {
            var path = instance.PathInstance;
            var currentType = Config.Instance.MemorySolution[path];
            var currentGb = PageInstanceSetup.GetRam(instance);

            // 目标：至少 4GB；若当前已 >= 4GB，认为足够，不再调整
            const double targetGb = 4.0;
            if (currentGb >= targetGb)
                return false;

            var tick = GetTickFromRam(targetGb);

            // 切换到手动配置并写入新内存值
            Config.Instance.MemorySolution[path] = 1;
            Config.Instance.CustomMemorySize[path] = tick;
            ModBase.Log($"[Symbio] 内存优化：{currentGb}GB -> 目标 {targetGb}GB（tick={tick}，原类型={currentType}）");
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Symbio] 内存优化失败");
            return false;
        }
    }

    /// <summary>
    ///     GetRamFromTick 的逆运算：返回能达到目标 GB 的最小 tick（与 PageSetupLaunch.GetRamFromTick 对应）。
    /// </summary>
    private static int GetTickFromRam(double gb)
    {
        // 与 GetRamFromTick 的分段保持一致，暴力查找最小 tick（tick 上限约 40，对应 30GB）
        for (var tick = 0; tick <= 40; tick++)
            if (PageSetupLaunch.GetRamFromTick(tick) >= gb)
                return tick;
        return 40;
    }

    private static string SeverityText(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "错误",
        DiagnosticSeverity.Warning => "警告",
        _ => "提示"
    };

    #region 可选 LLM 深度分析（不硬编码任何密钥，需通过环境变量或 symbio.features.json 配置）

    private static (string endpoint, string apiKey, string model)? ReadLlmConfig()
    {
        var endpoint = Environment.GetEnvironmentVariable("SYMBIO_LLM_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("SYMBIO_LLM_APIKEY");
        var model = Environment.GetEnvironmentVariable("SYMBIO_LLM_MODEL") ?? "gpt-4o-mini";

        try
        {
            var jsonPath = Path.Combine(AppContext.BaseDirectory, "symbio.features.json");
            if (File.Exists(jsonPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                endpoint ??= GetString(root, "llmEndpoint");
                apiKey ??= GetString(root, "llmApiKey");
                var m = GetString(root, "llmModel");
                if (!string.IsNullOrWhiteSpace(m))
                    model = m;
            }
        }
        catch
        {
            // 忽略配置文件读取错误
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            return null;

        return (endpoint.TrimEnd('/'), apiKey, model);
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static string? AnalyzeLogWithLlm(string log)
    {
        var cfg = ReadLlmConfig();
        if (cfg is null)
            return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Value.apiKey);

            var payload = new
            {
                model = cfg.Value.model,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "你是 Minecraft 启动器故障诊断助手。根据用户提供的崩溃/运行日志，用简体中文给出报错原因分析与可操作的修复建议，分点列出，控制在 300 字以内。"
                    },
                    new { role = "user", content = "以下是日志：\n" + log }
                },
                temperature = 0.3
            };

            using var resp = http.PostAsJsonAsync(cfg.Value.endpoint + "/chat/completions", payload)
                .GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "LLM 分析失败");
            return null;
        }
    }

    #endregion
}
