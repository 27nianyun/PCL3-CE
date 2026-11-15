using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class PageToolsIssues
{
    public PageToolsIssues()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshAll();
    }

    private void Page_Entered()
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshBackupInstances();
        RefreshErrorRecords();
        RefreshOptimizationRecords();
    }

    #region 备份

    private void RefreshBackupInstances()
    {
        var current = ((MyComboBoxItem?)ComboBackupInstance.SelectedItem)?.Tag?.ToString();
        ComboBackupInstance.Items.Clear();
        foreach (var inst in SymbioIssues.EnumerateAllInstances())
        {
            ComboBackupInstance.Items.Add(new MyComboBoxItem { Content = inst.Name, Tag = inst.PathInstance });
        }

        // 恢复之前的选择
        if (!string.IsNullOrEmpty(current))
        {
            foreach (MyComboBoxItem item in ComboBackupInstance.Items)
            {
                if (item.Tag?.ToString() == current)
                {
                    ComboBackupInstance.SelectedItem = item;
                    break;
                }
            }
        }

        if (ComboBackupInstance.SelectedItem is null && ComboBackupInstance.Items.Count > 0)
            ComboBackupInstance.SelectedIndex = 0;

        // 默认备份目录
        if (string.IsNullOrEmpty(TextBackupRoot.Text))
        {
            var cfg = SymbioIssues.ConfigForUi;
            if (!string.IsNullOrEmpty(cfg))
                TextBackupRoot.Text = cfg;
        }
    }

    private void BtnBackupBrowse_Click(object sender, MouseButtonEventArgs e)
    {
        var folder = SystemDialogs.SelectFolder("选择备份地址", TextBackupRoot.Text);
        if (!string.IsNullOrEmpty(folder))
        {
            TextBackupRoot.Text = folder;
            SymbioIssues.SetDefaultBackupRoot(folder);
        }
    }

    private void BtnBackupStart_Click(object sender, MouseButtonEventArgs e)
    {
        var item = (MyComboBoxItem?)ComboBackupInstance.SelectedItem;
        if (item is null)
        {
            HintService.Hint("请先选择一个实例。", HintType.Error);
            return;
        }

        var inst = SymbioIssues.EnumerateAllInstances()
            .FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());
        if (inst is null)
        {
            HintService.Hint("找不到所选实例。", HintType.Error);
            return;
        }

        var root = TextBackupRoot.Text?.Trim();
        if (string.IsNullOrEmpty(root))
            root = SystemDialogs.SelectFolder("选择备份地址");
        if (string.IsNullOrEmpty(root))
            return;

        SymbioIssues.SetDefaultBackupRoot(root);

        HintService.Hint($"正在备份 {inst.Name} …", HintType.Info);
        ModBase.RunInNewThread(() =>
        {
            try
            {
                var dest = SymbioIssues.BackupInstance(inst, root, true);
                ModBase.RunInUi(() =>
                {
                    HintService.Hint($"备份完成：{dest}", HintType.Success);
                    var r = ModMain.MyMsgBox($"备份已完成。\n\n{Path.GetFileName(dest)}\n\n是否打开备份所在文件夹？",
                        "备份完成", button2: "打开文件夹");
                    if (r == 2)
                        SymbioIssues.OpenFolder(dest);
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "备份实例失败");
                ModBase.RunInUi(() => HintService.Hint("备份失败：" + ex.Message, HintType.Error));
            }
        });
    }

    #endregion

    #region 报错记录

    private void BtnRefreshErrors_Click(object sender, MouseButtonEventArgs e) => RefreshErrorRecords();

    private void BtnOpenErrorFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var item = (MyComboBoxItem?)ComboBackupInstance.SelectedItem;
        var inst = item is null
            ? SymbioIssues.EnumerateAllInstances().FirstOrDefault()
            : SymbioIssues.EnumerateAllInstances().FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());

        if (inst is null)
        {
            HintService.Hint("请先选择实例。", HintType.Error);
            return;
        }

        var crashDir = Path.Combine(inst.PathIndie, "crash-reports");
        if (Directory.Exists(crashDir))
            SymbioIssues.OpenFolder(crashDir);
        else
            SymbioIssues.OpenFolder(Path.Combine(inst.PathIndie, "logs"));
    }

    private void RefreshErrorRecords()
    {
        PanErrorList.Children.Clear();

        // 针对当前选中实例收集（若未选则全部）
        McInstance? inst = null;
        var item = (MyComboBoxItem?)ComboBackupInstance.SelectedItem;
        if (item is not null)
            inst = SymbioIssues.EnumerateAllInstances()
                .FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());

        var records = SymbioIssues.CollectErrorRecords(inst);

        if (records.Count == 0)
        {
            PanErrorList.Children.Add(new TextBlock
            {
                Text = "未发现报错记录。启动崩溃后此处会列出崩溃报告与日志。",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var rec in records.Take(30))
        {
            var card = new MyCard
            {
                Margin = new Thickness(0, 0, 0, 10),
                Title = $"[{rec.InstanceName}] {rec.FileName}"
            };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 20) };

            stack.Children.Add(new TextBlock
            {
                Text = rec.Summary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"时间：{rec.LastWriteTime:yyyy-MM-dd HH:mm:ss}　大小：{FormatSize(rec.Size)}",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var btn = new MyButton
            {
                Text = "打开文件",
                MinWidth = 100,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 0)
            };
            var captured = rec;
            btn.Click += (_, _) => SymbioIssues.OpenErrorRecord(captured);
            stack.Children.Add(btn);

            card.Children.Add(stack);
            PanErrorList.Children.Add(card);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        return (bytes / 1024.0 / 1024.0).ToString("F2") + " MB";
    }

    #endregion

    #region 优化记录与撤销

    private void BtnRefreshOpt_Click(object sender, MouseButtonEventArgs e) => RefreshOptimizationRecords();

    private void BtnClearOpt_Click(object sender, MouseButtonEventArgs e)
    {
        var r = ModMain.MyMsgBox("确定要清空所有优化记录吗？此操作不可撤销。", "清空记录",
            button2: "清空", button3: "取消");
        if (r == 2)
        {
            SymbioIssues.ClearOptimizationRecords();
            RefreshOptimizationRecords();
            HintService.Hint("优化记录已清空。", HintType.Success);
        }
    }

    private void RefreshOptimizationRecords()
    {
        PanOptList.Children.Clear();
        var records = SymbioIssues.GetOptimizationRecords();

        if (records.Count == 0)
        {
            PanOptList.Children.Add(new TextBlock
            {
                Text = "暂无优化记录。执行「一键解决」后，这里会列出每次优化的动作，并支持撤销。",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            return;
        }

        foreach (var rec in records.Take(50))
        {
            var card = new MyCard
            {
                Margin = new Thickness(0, 0, 0, 10),
                Title = rec.Undone ? $"[已撤销] {rec.Description}" : rec.Description
            };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 20) };

            stack.Children.Add(new TextBlock
            {
                Text = $"类型：{KindText(rec.Kind)}　时间：{rec.Time:yyyy-MM-dd HH:mm:ss}",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            if (!string.IsNullOrEmpty(rec.Detail))
                stack.Children.Add(new TextBlock
                {
                    Text = rec.Detail,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8)
                });

            if (!rec.Undone)
            {
                var btn = new MyButton
                {
                    Text = "撤销",
                    MinWidth = 100,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ColorType = MyButton.ColorState.Red
                };
                var capturedId = rec.Id;
                btn.Click += (_, _) => UndoRecord(capturedId);
                stack.Children.Add(btn);
            }

            card.Children.Add(stack);
            PanOptList.Children.Add(card);
        }
    }

    private static string KindText(string kind) => kind switch
    {
        "DisableMod" => "禁用模组",
        "FetchDeps" => "补充前置模组",
        "OptimizeMemory" => "优化内存",
        _ => kind
    };

    private void UndoRecord(int id)
    {
        var ok = SymbioIssues.UndoOptimization(id, out var msg);
        HintService.Hint(msg, ok ? HintType.Success : HintType.Error);
        RefreshOptimizationRecords();
    }

    #endregion
}
