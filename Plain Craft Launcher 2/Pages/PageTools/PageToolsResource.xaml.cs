using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.UI;

namespace PCL;

public partial class PageToolsResource
{
    public PageToolsResource()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshAll();
    }

    private void Page_Entered() => RefreshAll();

    private void RefreshAll()
    {
        CheckOverwrite.IsChecked = SymbioTools.Config.OverwriteOnImport;
        RefreshInstances();
        RefreshShaders();
        RefreshDataPacks();
    }

    #region 实例

    private void RefreshInstances()
    {
        var current = ((MyComboBoxItem?)ComboInstance.SelectedItem)?.Tag?.ToString();
        ComboInstance.Items.Clear();
        foreach (var inst in SymbioTools.EnumerateAllInstances())
            ComboInstance.Items.Add(new MyComboBoxItem { Content = inst.Name, Tag = inst.PathInstance });

        if (!string.IsNullOrEmpty(current))
            foreach (MyComboBoxItem item in ComboInstance.Items)
                if (item.Tag?.ToString() == current) { ComboInstance.SelectedItem = item; break; }

        if (ComboInstance.SelectedItem is null && ComboInstance.Items.Count > 0)
            ComboInstance.SelectedIndex = 0;
    }

    private McInstance? SelectedInstance
    {
        get
        {
            var item = (MyComboBoxItem?)ComboInstance.SelectedItem;
            return item is null ? null : SymbioTools.EnumerateAllInstances().FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());
        }
    }

    #endregion

    #region 拖拽导入

    private void PanDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PanDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var p in paths)
            ImportPath(p);
    }

    private void BtnPickFiles_Click(object sender, ModBase.RouteEventArgs e)
    {
        var files = SystemDialogs.SelectFiles("选择地图 / 光影包 / 数据包（可多选）");
        if (files is null || files.Length == 0)
            return;
        foreach (var f in files)
            ImportPath(f);
    }

    private void ImportPath(string path)
    {
        var inst = SelectedInstance;
        if (inst is null)
        {
            HintService.Hint("请先选择目标实例。", HintType.Error);
            return;
        }

        // 自动识别类型
        if (SymbioTools.IsMinecraftSave(path))
        {
            ImportMap(path, inst);
        }
        else if (SymbioTools.IsShaderPack(path))
        {
            ImportShader(path, inst);
        }
        else if (SymbioTools.IsDataPack(path))
        {
            ImportDataPack(path, inst);
        }
        else if (Directory.Exists(path))
        {
            // 可能是地图文件夹但缺 level.dat，或普通文件夹
            HintService.Hint($"无法识别「{Path.GetFileName(path)}」：不是有效的地图/光影包/数据包。", HintType.Error);
        }
        else
        {
            HintService.Hint($"无法识别「{Path.GetFileName(path)}」的文件类型。", HintType.Error);
        }
    }

    private void ImportMap(string src, McInstance inst)
    {
        try
        {
            var dest = SymbioTools.ImportMap(inst, src, CheckOverwrite.IsChecked == true);
            SymbioTools.Config.OverwriteOnImport = CheckOverwrite.IsChecked == true;
            SymbioTools.SaveConfig();
            HintService.Hint($"地图「{Path.GetFileName(dest)}」已导入到 {inst.Name}。", HintType.Success);
        }
        catch (Exception ex)
        {
            HintService.Hint(ex.Message, HintType.Error);
        }
    }

    private void ImportShader(string src, McInstance inst)
    {
        try
        {
            var dest = SymbioTools.ImportShader(inst, src, CheckOverwrite.IsChecked == true);
            SymbioTools.Config.OverwriteOnImport = CheckOverwrite.IsChecked == true;
            SymbioTools.SaveConfig();
            HintService.Hint($"光影包「{Path.GetFileName(dest)}」已导入到 {inst.Name}。", HintType.Success);
            RefreshShaders();
        }
        catch (Exception ex)
        {
            HintService.Hint(ex.Message, HintType.Error);
        }
    }

    private void ImportDataPack(string src, McInstance inst)
    {
        try
        {
            var dest = SymbioTools.ImportDataPack(inst, src, CheckOverwrite.IsChecked == true);
            SymbioTools.Config.OverwriteOnImport = CheckOverwrite.IsChecked == true;
            SymbioTools.SaveConfig();
            HintService.Hint($"数据包「{Path.GetFileName(dest)}」已导入到 {inst.Name}。", HintType.Success);
            RefreshDataPacks();
        }
        catch (Exception ex)
        {
            HintService.Hint(ex.Message, HintType.Error);
        }
    }

    #endregion

    #region 光影管理

    private void BtnRefreshShaders_Click(object sender, MouseButtonEventArgs e) => RefreshShaders();
    private void BtnOpenShaderFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = SelectedInstance;
        if (inst is null) { HintService.Hint("请先选择实例。", HintType.Error); return; }
        var folder = SymbioTools.GetSubFolder(inst, "shaderpacks");
        SymbioTools.EnsureFolder(folder);
        SymbioTools.OpenFolder(folder);
    }

    private void RefreshShaders()
    {
        PanShaderList.Children.Clear();
        var inst = SelectedInstance;
        if (inst is null) { AddEmptyHint(PanShaderList, "请选择实例后查看光影包。"); return; }

        var folder = SymbioTools.GetSubFolder(inst, "shaderpacks");
        if (!Directory.Exists(folder)) { AddEmptyHint(PanShaderList, "该实例尚无光影包，拖拽 zip 到上方即可导入。"); return; }

        var items = Directory.GetFileSystemEntries(folder)
            .Where(p => !p.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p)).ToList();

        if (items.Count == 0) { AddEmptyHint(PanShaderList, "该实例尚无光影包。"); return; }

        foreach (var p in items)
        {
            var name = Path.GetFileName(p);
            var enabled = !File.Exists(p + ".disabled") && !Directory.Exists(p + ".disabled");
            var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = name };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };

            var toggle = new MyButton
            {
                Text = enabled ? "已启用（点击禁用）" : "已禁用（点击启用）",
                MinWidth = 170,
                HorizontalAlignment = HorizontalAlignment.Left,
                ColorType = enabled ? MyButton.ColorState.Highlight : MyButton.ColorState.Normal
            };
            var capturedInst = inst;
            var capturedName = name;
            var capturedEnabled = enabled;
            toggle.Click += (_, _) =>
            {
                SymbioTools.SetShaderEnabled(capturedInst, capturedName, !capturedEnabled);
                RefreshShaders();
            };
            stack.Children.Add(toggle);
            card.Children.Add(stack);
            PanShaderList.Children.Add(card);
        }
    }

    #endregion

    #region 数据包管理

    private void BtnRefreshDataPacks_Click(object sender, MouseButtonEventArgs e) => RefreshDataPacks();
    private void BtnOpenDataPackFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = SelectedInstance;
        if (inst is null) { HintService.Hint("请先选择实例。", HintType.Error); return; }
        var folder = SymbioTools.GetSubFolder(inst, "datapacks");
        SymbioTools.EnsureFolder(folder);
        SymbioTools.OpenFolder(folder);
    }

    private void RefreshDataPacks()
    {
        PanDataPackList.Children.Clear();
        var inst = SelectedInstance;
        if (inst is null) { AddEmptyHint(PanDataPackList, "请选择实例后查看数据包。"); return; }

        var folder = SymbioTools.GetSubFolder(inst, "datapacks");
        if (!Directory.Exists(folder)) { AddEmptyHint(PanDataPackList, "该实例尚无数据包，拖拽 zip 到上方即可导入。"); return; }

        var items = Directory.GetFileSystemEntries(folder).OrderBy(p => Path.GetFileName(p)).ToList();
        if (items.Count == 0) { AddEmptyHint(PanDataPackList, "该实例尚无数据包。"); return; }

        foreach (var p in items)
        {
            var name = Path.GetFileName(p);
            var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = name };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };
            var hint = new TextBlock
            {
                Text = "数据包已就位于 datapacks 目录，进入世界后可在「数据包」界面启用。",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(hint);
            card.Children.Add(stack);
            PanDataPackList.Children.Add(card);
        }
    }

    #endregion

    private static void AddEmptyHint(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
    }
}
