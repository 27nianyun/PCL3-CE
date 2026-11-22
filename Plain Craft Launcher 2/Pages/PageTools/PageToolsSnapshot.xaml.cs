using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>环境还原点页面：为实例创建 / 还原 / 删除模组与配置快照。</summary>
public partial class PageToolsSnapshot
{
    private bool _busy;

    public PageToolsSnapshot()
    {
        InitializeComponent();
        PageEnter += RefreshAll;
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshInstances();
        RefreshSnapshots();
    }

    #region 实例与列表

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

    private void RefreshSnapshots()
    {
        var inst = GetSelectedInstance();
        PanSnapshotList.Children.Clear();
        if (inst is null)
        {
            LabSnapshotSummary.Text = "还没有可用的实例。";
            return;
        }

        var list = ModEnvironmentSnapshot.List(inst);
        LabSnapshotSummary.Text = list.Count == 0
            ? $"「{inst.Name}」还没有还原点。"
            : $"「{inst.Name}」共有 {list.Count} 个还原点，占用约 {ModMemoryOptimize.FormatBytes(list.Sum(i => i.SizeBytes))}。";

        if (list.Count == 0) return;
        foreach (var snapshot in list) PanSnapshotList.Children.Add(BuildSnapshotCard(snapshot));
    }

    private MyCard BuildSnapshotCard(EnvSnapshotInfo snapshot)
    {
        var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = snapshot.DisplayName };
        var panel = new StackPanel { Margin = new Thickness(25, 36, 25, 16) };

        var loader = string.IsNullOrWhiteSpace(snapshot.Loader) ? "原版" : snapshot.Loader;
        if (!string.IsNullOrWhiteSpace(snapshot.LoaderVersion)) loader += " " + snapshot.LoaderVersion;
        var detail = $"{snapshot.CreatedAt:yyyy-MM-dd HH:mm}　MC {snapshot.McVersion}　{loader}　" +
                     $"{snapshot.ModCount} 个模组　{ModMemoryOptimize.FormatBytes(snapshot.SizeBytes)}" +
                     (snapshot.HasConfig ? "　含配置" : "");
        if (!string.IsNullOrWhiteSpace(snapshot.Note)) detail += $"\n备注：{snapshot.Note}";

        panel.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3")
        });

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var btnRestore = new MyButton
        {
            Text = "还原到此环境",
            MinWidth = 140,
            ColorType = MyButton.ColorState.Highlight,
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnRestore.Click += (_, _) => RestoreSnapshot(snapshot);
        buttons.Children.Add(btnRestore);

        var btnDelete = new MyButton
        {
            Text = "删除",
            MinWidth = 90,
            ColorType = MyButton.ColorState.Red,
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnDelete.Click += (_, _) => DeleteSnapshot(snapshot);
        buttons.Children.Add(btnDelete);

        var btnOpen = new MyButton { Text = "打开位置", MinWidth = 110 };
        btnOpen.Click += (_, _) =>
        {
            try
            {
                ModBase.OpenExplorer(System.IO.Path.GetDirectoryName(snapshot.FilePath));
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "打开快照文件夹失败", ModBase.LogLevel.Hint);
            }
        };
        buttons.Children.Add(btnOpen);

        panel.Children.Add(buttons);
        card.Children.Add(panel);
        return card;
    }

    #endregion

    #region 操作

    private void BtnCreateSnapshot_Click(object sender, MouseButtonEventArgs e)
    {
        if (_busy) return;
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        var name = TextSnapshotName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var note = TextSnapshotNote.Text?.Trim() ?? "";
        var includeConfig = CheckIncludeConfig.Checked == true;

        _busy = true;
        BtnCreateSnapshot.IsEnabled = false;
        BtnCreateSnapshot.Text = "正在创建……";

        ModBase.RunInNewThread(() =>
        {
            try
            {
                var info = ModEnvironmentSnapshot.Create(inst, name, note, includeConfig);
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    BtnCreateSnapshot.IsEnabled = true;
                    BtnCreateSnapshot.Text = "创建当前环境还原点";
                    TextSnapshotName.Text = "";
                    TextSnapshotNote.Text = "";
                    HintService.Hint($"已创建还原点「{info.DisplayName}」，包含 {info.ModCount} 个模组。",
                        HintType.Success);
                    RefreshSnapshots();
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "创建环境还原点失败", ModBase.LogLevel.Hint, userSummary: "创建还原点失败，请查看日志。");
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    BtnCreateSnapshot.IsEnabled = true;
                    BtnCreateSnapshot.Text = "创建当前环境还原点";
                });
            }
        }, "Snapshot Create");
    }

    private void RestoreSnapshot(EnvSnapshotInfo snapshot)
    {
        if (_busy) return;
        var inst = GetSelectedInstance();
        if (inst is null) return;
        if (ModWatcher.hasRunningMinecraft)
        {
            HintService.Hint("请先关闭正在运行的游戏再还原环境。", HintType.Error);
            return;
        }

        if (ModMain.MyMsgBox(
                $"将把「{inst.Name}」的模组与配置恢复到 {snapshot.CreatedAt:yyyy-MM-dd HH:mm} 的状态。\n" +
                "当前的 mods 与 config 会改名保留为 *.restore-old-时间戳，不会直接删除；存档不受影响。\n\n确定继续吗？",
                "还原环境", "开始还原", "取消") == 2) return;

        var restoreConfig = snapshot.HasConfig && ModMain.MyMsgBox(
            "这个还原点包含 config 配置。\n是否一并恢复配置？（选择「仅恢复模组」会保留当前配置）",
            "恢复配置", "一并恢复", "仅恢复模组") == 1;

        _busy = true;
        ModBase.RunInNewThread(() =>
        {
            try
            {
                ModEnvironmentSnapshot.Restore(inst, snapshot, restoreConfig);
                ModBase.RunInUi(() =>
                {
                    _busy = false;
                    HintService.Hint("环境已恢复到所选还原点。", HintType.Success);
                    RefreshSnapshots();
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "还原环境失败", ModBase.LogLevel.Hint, userSummary: "还原环境失败，请查看日志。");
                ModBase.RunInUi(() => _busy = false);
            }
        }, "Snapshot Restore");
    }

    private void DeleteSnapshot(EnvSnapshotInfo snapshot)
    {
        if (ModMain.MyMsgBox($"确定删除还原点「{snapshot.DisplayName}」吗？此操作不可撤销。", "删除还原点",
                "删除", "取消") == 2) return;
        try
        {
            ModEnvironmentSnapshot.Delete(snapshot);
            RefreshSnapshots();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "删除还原点失败", ModBase.LogLevel.Hint, userSummary: "删除还原点失败，请查看日志。");
        }
    }

    private void BtnRefreshSnapshots_Click(object sender, MouseButtonEventArgs e) => RefreshSnapshots();

    private void BtnOpenSnapshotFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = GetSelectedInstance();
        if (inst is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        var folder = ModEnvironmentSnapshot.GetSnapshotFolder(inst);
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            ModBase.OpenExplorer(folder);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开快照文件夹失败", ModBase.LogLevel.Hint);
        }
    }

    #endregion
}
