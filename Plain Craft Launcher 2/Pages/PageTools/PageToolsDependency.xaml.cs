using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>模组依赖图谱页面：展示前置关系、缺失前置与冲突声明。</summary>
public partial class PageToolsDependency
{
    private DependencyGraphResult _result;
    private bool _onlyMissing;
    private bool _busy;

    public PageToolsDependency()
    {
        InitializeComponent();
        PageEnter += RefreshInstances;
        Loaded += (_, _) => RefreshInstances();
    }

    private void RefreshInstances()
    {
        var current = (ComboInstance.SelectedItem as MyComboBoxItem)?.Tag?.ToString();
        ComboInstance.Items.Clear();
        foreach (var inst in SymbioTools.EnumerateAllInstances())
            ComboInstance.Items.Add(new MyComboBoxItem { Content = inst.Name, Tag = inst.PathInstance });

        if (!string.IsNullOrEmpty(current))
            foreach (MyComboBoxItem item in ComboInstance.Items)
                if (item.Tag?.ToString() == current)
                {
                    ComboInstance.SelectedItem = item;
                    break;
                }

        if (ComboInstance.SelectedItem is null && ComboInstance.Items.Count > 0)
            ComboInstance.SelectedIndex = 0;
    }

    private McInstance GetSelectedInstance()
    {
        var item = ComboInstance.SelectedItem as MyComboBoxItem;
        if (item is null) return null;
        return SymbioTools.EnumerateAllInstances()
            .FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());
    }

    private void BtnAnalyze_Click(object sender, MouseButtonEventArgs e) => Analyze();

    private void BtnOnlyMissing_Click(object sender, MouseButtonEventArgs e)
    {
        _onlyMissing = !_onlyMissing;
        BtnOnlyMissing.Text = _onlyMissing ? "显示全部模组" : "只看缺失前置";
        if (_result is not null) RenderResult();
    }

    private void Analyze()
    {
        if (_busy) return;
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        if (!inst.Modable)
        {
            HintService.Hint("该实例没有安装模组加载器，无法分析依赖。", HintType.Error);
            return;
        }

        _busy = true;
        BtnAnalyze.IsEnabled = false;
        BtnAnalyze.Text = "分析中……";
        LabSummary.Text = "正在解析 mods 目录……";

        ModBase.RunInNewThread(() =>
        {
            DependencyGraphResult result;
            try
            {
                result = ModDependencyGraph.Build(inst);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "分析模组依赖失败", ModBase.LogLevel.Hint, userSummary: "分析依赖失败，请查看日志。");
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    BtnAnalyze.IsEnabled = true;
                    BtnAnalyze.Text = "分析依赖关系";
                    LabSummary.Text = "分析失败，请查看日志。";
                });
                return;
            }

            ModBase.RunInUi(() =>
            {
                _busy = false;
                BtnAnalyze.IsEnabled = true;
                BtnAnalyze.Text = "分析依赖关系";
                _result = result;
                RenderResult();
            });
        }, "Dependency Analyze");
    }

    private void RenderResult()
    {
        PanResult.Children.Clear();
        PanMissing.Children.Clear();

        var loader = string.IsNullOrWhiteSpace(_result.Loader) ? "原版" : _result.Loader;
        var totalEdges = _result.Nodes.Sum(n => n.Edges.Count);
        LabSummary.Text = $"实例 {_result.McVersion}（{loader}）　模组 {_result.Nodes.Count} 个　" +
                          $"依赖关系 {totalEdges} 条　❌ 缺失必选前置 {_result.Missing.Count} 个　" +
                          $"○ 可选前置缺失 {_result.OptionalMissingCount} 个　⚠ 冲突声明 {_result.Conflicts.Count} 条";

        BorderMissing.Visibility = _result.Missing.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var missing in _result.Missing)
        {
            var line = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"{missing.Id}" + (string.IsNullOrWhiteSpace(missing.Constraint) ? "" : $" {missing.Constraint}") +
                       $"　← {missing.RequiredBy} 依赖"
            };
            line.Children.Add(text);

            var btn = new MyButton
            {
                Text = "搜索",
                MinWidth = 80,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var id = missing.Id;
            btn.Click += (_, _) => ModBase.OpenWebsite("https://modrinth.com/mods?q=" + Uri.EscapeDataString(id));
            Grid.SetColumn(btn, 1);
            line.Children.Add(btn);
            PanMissing.Children.Add(line);
        }

        IEnumerable<ModDepNode> nodes = _onlyMissing
            ? _result.Nodes.Where(n => n.MissingCount > 0 || !string.IsNullOrEmpty(n.LoadError))
            : _result.Nodes;

        if (!nodes.Any())
        {
            PanResult.Children.Add(new TextBlock
            {
                Text = _onlyMissing ? "所有模组的必选前置都齐全。" : "没有扫描到模组。",
                FontSize = 13,
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        foreach (var node in nodes) PanResult.Children.Add(BuildNodeCard(node));
    }

    private MyCard BuildNodeCard(ModDepNode node)
    {
        var status = !string.IsNullOrEmpty(node.LoadError)
            ? "⚠ 解析失败"
            : node.MissingCount > 0
                ? $"❌ 缺少 {node.MissingCount} 个前置"
                : "✅ 依赖完整";
        var statusColor = !string.IsNullOrEmpty(node.LoadError)
            ? Color.FromRgb(0xE0, 0x9B, 0x2E)
            : node.MissingCount > 0
                ? Color.FromRgb(0xE5, 0x4A, 0x4A)
                : Color.FromRgb(0x2E, 0x9E, 0x44);

        var card = new MyCard
        {
            Margin = new Thickness(0, 0, 0, 8),
            Title = string.IsNullOrWhiteSpace(node.Version) ? node.Name : $"{node.Name} {node.Version}"
        };
        var stack = new StackPanel { Margin = new Thickness(25, 34, 25, 14) };

        stack.Children.Add(new TextBlock
        {
            Text = $"{status}　{node.FileName}" + (string.IsNullOrEmpty(node.ModId) ? "" : $"　[{node.ModId}]"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (!string.IsNullOrEmpty(node.LoadError))
        {
            stack.Children.Add(new TextBlock
            {
                Text = node.LoadError,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3")
            });
            card.Children.Add(stack);
            return card;
        }

        if (node.Edges.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "没有声明依赖。",
                FontSize = 12,
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3")
            });
        }
        else
        {
            foreach (var edge in node.Edges.OrderBy(e => e.Satisfied).ThenBy(e => e.Optional))
                stack.Children.Add(BuildEdgeLine(edge));
        }

        card.Children.Add(stack);
        return card;
    }

    private static TextBlock BuildEdgeLine(ModDepEdge edge)
    {
        var mark = edge.Satisfied ? "✔" : edge.Optional ? "○" : "✘";
        var suffix = edge.Optional ? "（可选）" : "";
        var provider = edge.Satisfied
            ? string.IsNullOrWhiteSpace(edge.ProviderVersion)
                ? $"　→ {edge.ProviderName}"
                : $"　→ {edge.ProviderName} {edge.ProviderVersion}"
            : edge.Optional
                ? "　→ 未安装"
                : "　→ 未安装，需要补充";

        var block = new TextBlock
        {
            FontSize = 12.5,
            Margin = new Thickness(0, 2, 0, 2),
            TextWrapping = TextWrapping.Wrap
        };
        var brush = edge.Satisfied
            ? (Brush)Application.Current.TryFindResource("ColorBrushGray3")
            : new SolidColorBrush(edge.Optional ? Color.FromRgb(0xE0, 0x9B, 0x2E) : Color.FromRgb(0xE5, 0x4A, 0x4A));
        block.Foreground = brush;
        block.Text = $"{mark} {edge.TargetId}" +
                     (string.IsNullOrWhiteSpace(edge.Constraint) ? "" : $" {edge.Constraint}") +
                     suffix + provider;
        return block;
    }

    private void BtnOpenMods_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        var folder = System.IO.Path.Combine(inst.PathIndie, "mods");
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            ModBase.OpenExplorer(folder);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开 mods 目录失败", ModBase.LogLevel.Hint);
        }
    }
}
