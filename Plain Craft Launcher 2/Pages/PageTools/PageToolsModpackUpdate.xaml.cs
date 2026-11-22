using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.UI;

namespace PCL;

/// <summary>整合包三方更新页面：对比新整合包与实例现状，按差异增量更新。</summary>
public partial class PageToolsModpackUpdate
{
    private ModpackUpdatePlan _plan;
    private bool _busy;

    public PageToolsModpackUpdate()
    {
        InitializeComponent();
        PageEnter += RefreshInstances;
        Loaded += (_, _) => RefreshInstances();
        TextCfKey.Text = SymbioTools.Config.CurseForgeApiKey ?? "";
    }

    /// <summary>读取页面上的 CurseForge Key，变更时顺手持久化。</summary>
    private string GetCurseForgeKey()
    {
        var key = TextCfKey.Text?.Trim() ?? "";
        if (key == (SymbioTools.Config.CurseForgeApiKey ?? "")) return key;
        SymbioTools.Config.CurseForgeApiKey = key;
        SymbioTools.SaveConfig();
        return key;
    }

    private void BtnSaveKey_Click(object sender, MouseButtonEventArgs e)
    {
        GetCurseForgeKey();
        HintService.Hint(string.IsNullOrWhiteSpace(SymbioTools.Config.CurseForgeApiKey)
            ? "已清空 CurseForge API Key。"
            : "CurseForge API Key 已保存。", HintType.Success);
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

    private void BtnPickPack_Click(object sender, MouseButtonEventArgs e)
    {
        var file = SystemDialogs.SelectFile("整合包 (*.zip;*.mrpack)|*.zip;*.mrpack|所有文件|*.*", "选择新版本整合包");
        if (string.IsNullOrWhiteSpace(file)) return;
        TextPackPath.Text = file;
        _plan = null;
        BtnApply.IsEnabled = false;
        PanChanges.Children.Clear();
        LabSummary.Text = "已选择整合包，点「对比差异」开始比对。";
    }

    private void BtnCompare_Click(object sender, MouseButtonEventArgs e)
    {
        if (_busy) return;
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        var pack = TextPackPath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pack) || !System.IO.File.Exists(pack))
        {
            HintService.Hint("请先选择一个整合包文件。", HintType.Error);
            return;
        }

        _busy = true;
        BtnCompare.IsEnabled = false;
        BtnApply.IsEnabled = false;
        BtnCompare.Text = "对比中……";
        LabSummary.Text = "正在计算本地文件哈希，文件较多时需要一会儿……";
        PanChanges.Children.Clear();

        var curseForgeKey = GetCurseForgeKey();

        ModBase.RunInNewThread(() =>
        {
            ModpackUpdatePlan plan;
            try
            {
                plan = ModModpackUpdater.BuildPlan(pack, inst, curseForgeKey);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "整合包对比失败", ModBase.LogLevel.Hint, userSummary: "对比失败，请查看日志。");
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    BtnCompare.IsEnabled = true;
                    BtnCompare.Text = "对比差异";
                    LabSummary.Text = "对比失败：" + ex.Message;
                });
                return;
            }

            ModBase.RunInUi(() =>
            {
                _busy = false;
                BtnCompare.IsEnabled = true;
                BtnCompare.Text = "对比差异";
                _plan = plan;
                RenderPlan(inst, plan);
            });
        }, "Modpack Compare");
    }

    private void RenderPlan(McInstance inst, ModpackUpdatePlan plan)
    {
        PanChanges.Children.Clear();

        var packTitle = string.IsNullOrWhiteSpace(plan.PackVersion)
            ? plan.PackName
            : $"{plan.PackName} {plan.PackVersion}";
        LabSummary.Text =
            $"整合包：{packTitle}（MC {plan.McVersion}" +
            (string.IsNullOrWhiteSpace(plan.LoaderInfo) ? "" : " / " + plan.LoaderInfo) + "）\n" +
            $"新增 {plan.AddedCount}　更新 {plan.UpdatedCount}　移除 {plan.RemovedCount}　已一致 {plan.UnchangedCount}　" +
            $"需要下载约 {ModMemoryOptimize.FormatBytes(plan.DownloadBytes)}" +
            (ModModpackUpdater.HasBaseline(inst) ? "" : "\n还没有基线清单，本次暂不能判断「移除」；更新一次后即可。");

        AddGroup("新增文件", plan.Files.Where(f => f.Change == ModpackFileChange.Added),
            Color.FromRgb(0x2E, 0x9E, 0x44));
        AddGroup("需要更新", plan.Files.Where(f => f.Change == ModpackFileChange.Updated),
            Color.FromRgb(0x2A, 0x6F, 0xC9));
        AddGroup("整合包已移除", plan.Files.Where(f => f.Change == ModpackFileChange.Removed),
            Color.FromRgb(0xE5, 0x4A, 0x4A));
        AddGroup("内容已一致", plan.Files.Where(f => f.Change == ModpackFileChange.Unchanged),
            Color.FromRgb(0x8A, 0x8A, 0x8A), 10);

        BtnApply.IsEnabled = plan.AddedCount + plan.UpdatedCount + plan.RemovedCount > 0;
    }

    private void AddGroup(string title, IEnumerable<ModpackFilePlanItem> items, Color color, int limit = 40)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = $"{title}（{list.Count}）",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var item in list.Take(limit))
            panel.Children.Add(new TextBlock
            {
                Text = "· " + item.Path + (item.Size > 0 ? $"　{ModMemoryOptimize.FormatBytes(item.Size)}" : ""),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                Margin = new Thickness(8, 1, 0, 1)
            });

        if (list.Count > limit)
            panel.Children.Add(new TextBlock
            {
                Text = $"……还有 {list.Count - limit} 个文件未显示",
                FontSize = 12,
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                Margin = new Thickness(8, 1, 0, 1)
            });

        PanChanges.Children.Add(panel);
    }

    private void BtnApply_Click(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _plan is null) return;
        var inst = GetSelectedInstance();
        if (inst is null) return;
        if (ModWatcher.hasRunningMinecraft)
        {
            HintService.Hint("请先关闭正在运行的游戏再更新整合包。", HintType.Error);
            return;
        }

        var removeOld = CheckRemoveOld.Checked == true;
        var message = $"将下载 {_plan.AddedCount + _plan.UpdatedCount} 个文件（约 {ModMemoryOptimize.FormatBytes(_plan.DownloadBytes)}）" +
                      (removeOld && _plan.RemovedCount > 0 ? $"，并删除 {_plan.RemovedCount} 个已从整合包移除的文件" : "") +
                      "。\n\n更新前建议先创建一个环境还原点，方便回退。确定继续吗？";
        if (ModMain.MyMsgBox(message, "开始更新整合包", "开始更新", "取消") == 2) return;

        _busy = true;
        BtnApply.IsEnabled = false;
        BtnCompare.IsEnabled = false;
        BtnApply.Text = "更新中……";

        ModBase.RunInNewThread(() =>
        {
            var progress = new Progress<(int Done, int Total, string Current)>(p =>
            {
                if (p.Total == 0) return;
                LabSummary.Text = $"正在处理 {p.Done}/{p.Total}：{p.Current}";
            });

            try
            {
                var (success, failed, bytes) = ModModpackUpdater.Apply(_plan, inst, removeOld, progress);
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    BtnCompare.IsEnabled = true;
                    BtnApply.Text = "开始更新";
                    BtnApply.IsEnabled = false;
                    LabSummary.Text = failed == 0
                        ? $"更新完成：处理 {success} 个文件，共 {ModMemoryOptimize.FormatBytes(bytes)}。"
                        : $"更新完成：成功 {success} 个，失败 {failed} 个，处理 {ModMemoryOptimize.FormatBytes(bytes)}。失败项请查看日志。";
                    HintService.Hint(failed == 0 ? "整合包更新完成。" : "部分文件更新失败，请查看日志。",
                        failed == 0 ? HintType.Success : HintType.Warning);
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "整合包更新失败", ModBase.LogLevel.Hint, userSummary: "更新失败，请查看日志。");
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    BtnCompare.IsEnabled = true;
                    BtnApply.IsEnabled = true;
                    BtnApply.Text = "开始更新";
                });
            }
        }, "Modpack Update");
    }
}
