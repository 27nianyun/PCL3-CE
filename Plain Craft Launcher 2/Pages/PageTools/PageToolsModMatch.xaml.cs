using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;

namespace PCL;

public partial class PageToolsModMatch
{
    private List<ModVersionMatch> _lastResult = new();
    private bool _onlyMismatch;

    public PageToolsModMatch()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshInstances();
    }

    private void Page_Entered() => RefreshInstances();

    private void RefreshInstances()
    {
        var current = ((MyComboBoxItem?)ComboInstance.SelectedItem)?.Tag?.ToString();
        ComboInstance.Items.Clear();
        foreach (var inst in SymbioTools.EnumerateAllInstances())
        {
            ComboInstance.Items.Add(new MyComboBoxItem { Content = inst.Name, Tag = inst.PathInstance });
        }

        if (!string.IsNullOrEmpty(current))
        {
            foreach (MyComboBoxItem item in ComboInstance.Items)
            {
                if (item.Tag?.ToString() == current)
                {
                    ComboInstance.SelectedItem = item;
                    break;
                }
            }
        }

        if (ComboInstance.SelectedItem is null && ComboInstance.Items.Count > 0)
            ComboInstance.SelectedIndex = 0;
    }

    private McInstance? GetSelectedInstance()
    {
        var item = (MyComboBoxItem?)ComboInstance.SelectedItem;
        if (item is null)
            return null;
        return SymbioTools.EnumerateAllInstances()
            .FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());
    }

    private void BtnScan_Click(object sender, MouseButtonEventArgs e) => Scan();

    private void Scan()
    {
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        if (!inst.Modable)
        {
            HintService.Hint("该实例不支持模组（非模组加载器）。", HintType.Error);
            return;
        }

        var mcVersion = inst.Info.VanillaName;
        var loader = SymbioTools.GetLoaderType(inst);

        _lastResult = SymbioTools.MatchModVersions(inst);

        var matched = _lastResult.Count(m => m.Status == ModMatchStatus.Matched);
        var mismatched = _lastResult.Count(m => m.Status == ModMatchStatus.Mismatched);
        var noConstraint = _lastResult.Count(m => m.Status == ModMatchStatus.NoConstraint);
        var error = _lastResult.Count(m => m.Status == ModMatchStatus.Error || m.Status == ModMatchStatus.Unknown);

        LabSummary.Text =
            $"实例版本：{mcVersion}　加载器：{(string.IsNullOrEmpty(loader) ? "未知" : loader)}　" +
            $"共 {_lastResult.Count} 个模组　✅ 匹配 {matched}　❌ 不匹配 {mismatched}　· 未声明 {noConstraint}　⚠ 异常 {error}";

        RenderResult();
    }

    private void RenderResult()
    {
        PanResult.Children.Clear();

        IEnumerable<ModVersionMatch> list = _onlyMismatch
            ? _lastResult.Where(m => m.Status == ModMatchStatus.Mismatched)
            : _lastResult;

        foreach (var m in list)
        {
            PanResult.Children.Add(BuildItemCard(m));
        }

        if (!list.Any())
            PanResult.Children.Add(new TextBlock
            {
                Text = _onlyMismatch ? "没有不匹配的模组。" : "没有扫描到模组。",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 4)
            });
    }

    private MyCard BuildItemCard(ModVersionMatch m)
    {
        var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = DisplayName(m) };

        var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };

        // 状态行
        var statusText = m.Status switch
        {
            ModMatchStatus.Matched => "✅ 版本匹配",
            ModMatchStatus.Mismatched => "❌ 版本不匹配",
            ModMatchStatus.NoConstraint => "· 未声明版本要求",
            ModMatchStatus.Error => "⚠ 解析出错",
            _ => "？ 无法判断",
        };
        var statusColor = m.Status switch
        {
            ModMatchStatus.Matched => Color.FromRgb(0x2E, 0x9E, 0x44),
            ModMatchStatus.Mismatched => Color.FromRgb(0xE5, 0x4A, 0x4A),
            ModMatchStatus.Error => Color.FromRgb(0xE0, 0x9B, 0x2E),
            _ => Color.FromRgb(0x8A, 0x8A, 0x8A),
        };
        stack.Children.Add(new TextBlock
        {
            Text = statusText,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusColor),
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (!string.IsNullOrEmpty(m.ModId))
            stack.Children.Add(InfoLine("Mod ID", m.ModId));
        if (!string.IsNullOrEmpty(m.ModVersion))
            stack.Children.Add(InfoLine("模组版本", m.ModVersion));
        if (!string.IsNullOrEmpty(m.Constraint))
            stack.Children.Add(InfoLine("版本要求", m.Constraint));
        stack.Children.Add(InfoLine("文件", m.FileName));
        if (!string.IsNullOrEmpty(m.Message))
            stack.Children.Add(new TextBlock
            {
                Text = m.Message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                Margin = new Thickness(0, 6, 0, 0)
            });

        card.Children.Add(stack);
        return card;
    }

    private static string DisplayName(ModVersionMatch m) =>
        string.IsNullOrEmpty(m.ModName) ? m.FileName : m.ModName;

    private static TextBlock InfoLine(string label, string value)
    {
        var sp = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 3),
            TextWrapping = TextWrapping.Wrap
        };
        sp.Inlines.Add(new System.Windows.Documents.Run(label + "：")
        {
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3")
        });
        sp.Inlines.Add(new System.Windows.Documents.Run(value));
        return sp;
    }

    private void BtnOpenMods_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        var folder = SymbioTools.GetSubFolder(inst, "mods");
        if (!System.IO.Directory.Exists(folder))
        {
            HintService.Hint("mods 目录不存在。", HintType.Error);
            return;
        }

        SymbioTools.OpenFolder(folder);
    }

    private void BtnOnlyMismatch_Click(object sender, MouseButtonEventArgs e)
    {
        _onlyMismatch = !_onlyMismatch;
        (sender as MyButton)!.Text = _onlyMismatch ? "显示全部" : "只看不匹配";
        RenderResult();
    }
}
