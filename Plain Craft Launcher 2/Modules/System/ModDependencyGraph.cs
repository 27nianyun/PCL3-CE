using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCL;

/// <summary>依赖图中的一条边：某模组对另一个模组的依赖。</summary>
public sealed class ModDepEdge
{
    public string TargetId { get; set; } = "";
    public string Constraint { get; set; } = "";
    public bool Optional { get; set; }
    public bool Satisfied { get; set; }
    public string ProviderName { get; set; } = "";
    public string ProviderVersion { get; set; } = "";
}

/// <summary>依赖图中的一个模组节点。</summary>
public sealed class ModDepNode
{
    public string ModId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string LoadError { get; set; } = "";
    public List<ModDepEdge> Edges { get; } = [];

    public int MissingCount => Edges.Count(e => !e.Satisfied && !e.Optional);
    public int OptionalMissingCount => Edges.Count(e => !e.Satisfied && e.Optional);
}

/// <summary>一条冲突声明。</summary>
public sealed class ModConflictRow
{
    public string DeclarerName { get; set; } = "";
    public string Target { get; set; } = "";
    public string Range { get; set; } = "";
    public bool Hard { get; set; }
    public bool TargetInstalled { get; set; }
}

/// <summary>一次依赖分析的结果。</summary>
public sealed class DependencyGraphResult
{
    public List<ModDepNode> Nodes { get; } = [];
    public List<ModConflictRow> Conflicts { get; } = [];

    /// <summary>缺失的必选前置：ModId / 版本要求 / 依赖方名称。</summary>
    public List<(string Id, string Constraint, string RequiredBy)> Missing { get; } = [];

    public int OptionalMissingCount { get; set; }
    public string McVersion { get; set; } = "";
    public string Loader { get; set; } = "";
}

/// <summary>
///     本地模组依赖图谱：读取 mods 目录中每个 jar 的依赖声明，建立前置关系并检测缺失。
/// </summary>
/// <remarks>
///     PCL₃-CE 新增功能（迁移自 PCL Living Fork 的「模组依赖图谱与预检」构思，独立实现）。
///     复用 PCL 已有的 <see cref="ModLocalComp.LocalCompFile" /> 元数据解析与
///     <see cref="McConstraintMatcher" /> 版本求值，不额外引入依赖。
/// </remarks>
public static class ModDependencyGraph
{
    /// <summary>分析指定实例的模组依赖关系。请放在后台线程调用。</summary>
    public static DependencyGraphResult Build(McInstance instance)
    {
        var result = new DependencyGraphResult
        {
            McVersion = instance.Info.VanillaName ?? "",
            Loader = SymbioTools.GetLoaderType(instance)
        };

        var modsFolder = Path.Combine(instance.PathIndie, "mods");
        if (!Directory.Exists(modsFolder)) return result;

        // 1. 解析所有模组
        var comps = new List<ModLocalComp.LocalCompFile>();
        foreach (var file in Directory.EnumerateFiles(modsFolder, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var comp = new ModLocalComp.LocalCompFile(file);
            try
            {
                comp.Load();
            }
            catch (Exception ex)
            {
                result.Nodes.Add(new ModDepNode
                {
                    FileName = Path.GetFileName(file),
                    FilePath = file,
                    LoadError = ex.Message
                });
                continue;
            }

            comps.Add(comp);
        }

        // 2. 建立 provider 索引（主 ModId + 别名，供依赖匹配）
        var providers = new Dictionary<string, (string Name, string Version, string File)>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in comps)
        {
            var name = comp.Name ?? comp.ModId ?? Path.GetFileNameWithoutExtension(comp.RawPath);
            var version = comp.Version ?? "";

            void Register(string id, string ver)
            {
                if (string.IsNullOrWhiteSpace(id)) return;
                if (!providers.ContainsKey(id)) providers[id] = (name, ver, comp.RawPath);
            }

            Register(comp.ModId, version);
            foreach (var alias in comp.ProvidedIds)
            {
                var aliasVersion = comp.ProvidedVersions.TryGetValue(alias, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v
                    : version;
                Register(alias, aliasVersion);
            }
        }

        // 3. 逐模组比对依赖
        foreach (var comp in comps)
        {
            var node = new ModDepNode
            {
                ModId = comp.ModId ?? "",
                Name = comp.Name ?? comp.ModId ?? Path.GetFileNameWithoutExtension(comp.RawPath),
                Version = comp.Version ?? "",
                FileName = Path.GetFileName(comp.RawPath),
                FilePath = comp.RawPath
            };

            foreach (var declaration in comp.DependencyDeclarations)
            {
                if (string.IsNullOrWhiteSpace(declaration.Id)) continue;
                if (ModDependencyIds.IsPlatform(declaration.Id)) continue; // minecraft / 加载器 / java

                var edge = new ModDepEdge
                {
                    TargetId = declaration.Id,
                    Constraint = declaration.Raw ?? "",
                    Optional = declaration.Optional
                };

                if (providers.TryGetValue(declaration.Id, out var provider))
                {
                    edge.ProviderName = provider.Name;
                    edge.ProviderVersion = provider.Version;
                    edge.Satisfied = VersionSatisfied(declaration.Raw, result.Loader, provider.Version);
                }

                node.Edges.Add(edge);

                if (!edge.Satisfied)
                {
                    if (declaration.Optional)
                    {
                        result.OptionalMissingCount++;
                    }
                    else if (!result.Missing.Any(m => string.Equals(m.Id, declaration.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Missing.Add((declaration.Id, declaration.Raw ?? "", node.Name));
                    }
                }
            }

            // 冲突声明：只展示，不自动处理
            foreach (var conflict in comp.ConflictDeclarations)
            {
                if (string.IsNullOrWhiteSpace(conflict.Target)) continue;
                result.Conflicts.Add(new ModConflictRow
                {
                    DeclarerName = node.Name,
                    Target = conflict.Target,
                    Range = conflict.Raw ?? "",
                    Hard = conflict.Hard,
                    TargetInstalled = providers.ContainsKey(conflict.Target)
                });
            }

            result.Nodes.Add(node);
        }

        result.Nodes.Sort((a, b) =>
        {
            var cmp = b.MissingCount.CompareTo(a.MissingCount);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        ModBase.Log($"[Dependency] {instance.Name}：{result.Nodes.Count} 个模组，缺失前置 {result.Missing.Count} 个，冲突声明 {result.Conflicts.Count} 条",
            ModBase.LogLevel.Normal);

        return result;
    }

    /// <summary>判断 provider 版本是否满足依赖的原始约束；无法判定时视为满足，避免误报。</summary>
    private static bool VersionSatisfied(string constraint, string loader, string providerVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return true;
        if (string.IsNullOrWhiteSpace(providerVersion)) return true;
        try
        {
            return McConstraintMatcher.Satisfies(constraint, loader, providerVersion);
        }
        catch
        {
            return true;
        }
    }
}
