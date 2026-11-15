using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.UI;

namespace PCL;

public partial class PageToolsPreset
{
    private static readonly (string Name, string Desc)[] FamousMods =
    {
        ("Sodium", "渲染优化"),
        ("Lithium", "逻辑优化"),
        ("Phosphor", "光照优化"),
        ("Iris Shaders", "光影加载"),
        ("FerriteCore", "内存优化"),
        ("EntityCulling", "实体剔除"),
        ("Sodium Extra", "渲染增强"),
        ("Reese's Sodium Options", "渲染选项"),
    };

    public PageToolsPreset()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshAll();
    }

    private void Page_Entered() => RefreshAll();

    private void RefreshAll()
    {
        RefreshSourceInstances();
        BuildFamousModButtons();
        RefreshPresets();
    }

    private void RefreshSourceInstances()
    {
        var current = ((MyComboBoxItem?)ComboSourceInstance.SelectedItem)?.Tag?.ToString();
        ComboSourceInstance.Items.Clear();
        foreach (var inst in SymbioTools.EnumerateAllInstances())
            ComboSourceInstance.Items.Add(new MyComboBoxItem { Content = inst.Name, Tag = inst.PathInstance });

        if (!string.IsNullOrEmpty(current))
            foreach (MyComboBoxItem item in ComboSourceInstance.Items)
                if (item.Tag?.ToString() == current) { ComboSourceInstance.SelectedItem = item; break; }

        if (ComboSourceInstance.SelectedItem is null && ComboSourceInstance.Items.Count > 0)
            ComboSourceInstance.SelectedIndex = 0;
    }

    private McInstance? SourceInstance
    {
        get
        {
            var item = (MyComboBoxItem?)ComboSourceInstance.SelectedItem;
            return item is null ? null : SymbioTools.EnumerateAllInstances().FirstOrDefault(i => i.PathInstance == item.Tag?.ToString());
        }
    }

    private void BuildFamousModButtons()
    {
        PanFamousMods.Children.Clear();
        foreach (var (name, desc) in FamousMods)
        {
            var btn = new MyButton
            {
                Text = $"{name}（{desc}）",
                MinWidth = 150,
                Margin = new Thickness(0, 0, 12, 10)
            };
            var capturedName = name;
            btn.Click += (_, _) => AddFamousMod(capturedName);
            PanFamousMods.Children.Add(btn);
        }
    }

    private void AddFamousMod(string modName)
    {
        // 检查是否已有同名预设，否则新建一个"优化模组"预设
        var existing = SymbioTools.Config.Presets.FirstOrDefault(p => p.Name == "常用优化模组");
        if (existing is null)
        {
            existing = new PresetEntry { Name = "常用优化模组", Description = "一键添加的著名优化模组" };
            SymbioTools.Config.Presets.Add(existing);
        }

        var marker = modName; // 记录模组名（真实下载需联网，这里先记录名称）
        if (!existing.ModFiles.Contains(marker))
        {
            existing.ModFiles.Add(marker);
            SymbioTools.SaveConfig();
            HintService.Hint($"已将「{modName}」加入预设「常用优化模组」。", HintType.Success);
        }
        else
        {
            HintService.Hint($"「{modName}」已在预设中。", HintType.Info);
        }

        RefreshPresets();
    }

    private void BtnCreateFromInstance_Click(object sender, MouseButtonEventArgs e)
    {
        var name = TextPresetName.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            HintService.Hint("请先输入预设名称。", HintType.Error);
            return;
        }

        var inst = SourceInstance;
        if (inst is null)
        {
            HintService.Hint("请选择来源实例。", HintType.Error);
            return;
        }

        var preset = SymbioTools.BuildPresetFromInstance(name, inst, $"来自实例 {inst.Name}");
        if (preset.ModFiles.Count == 0)
        {
            HintService.Hint("该实例的 mods 目录没有模组文件。", HintType.Error);
            return;
        }

        SymbioTools.AddPreset(preset);
        HintService.Hint($"已从 {inst.Name} 创建预设「{name}」，包含 {preset.ModFiles.Count} 个模组。", HintType.Success);
        RefreshPresets();
    }

    private void BtnCreateFromFiles_Click(object sender, MouseButtonEventArgs e)
    {
        var name = TextPresetName.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            HintService.Hint("请先输入预设名称。", HintType.Error);
            return;
        }

        var files = SystemDialogs.SelectFiles("选择模组文件（可多选）");
        if (files is null || files.Length == 0)
            return;

        var preset = new PresetEntry { Name = name, Description = "从本地文件选择", ModFiles = files.ToList() };
        SymbioTools.AddPreset(preset);
        HintService.Hint($"已创建预设「{name}」，包含 {files.Length} 个模组。", HintType.Success);
        RefreshPresets();
    }

    private void BtnRefreshPresets_Click(object sender, MouseButtonEventArgs e) => RefreshPresets();

    private void RefreshPresets()
    {
        PanPresetList.Children.Clear();
        var presets = SymbioTools.Config.Presets;

        if (presets.Count == 0)
        {
            PanPresetList.Children.Add(new TextBlock
            {
                Text = "暂无预设。可从实例导入模组、从本地文件选择，或一键添加著名优化模组。",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            return;
        }

        foreach (var preset in presets)
        {
            var card = new MyCard { Margin = new Thickness(0, 0, 0, 10), Title = preset.Name };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };

            stack.Children.Add(new TextBlock
            {
                Text = $"包含 {preset.ModFiles.Count} 项　{preset.Description}",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // 列出前几项
            var preview = string.Join("、", preset.ModFiles.Take(6).Select(f => Path.GetFileName(f)));
            if (preset.ModFiles.Count > 6) preview += " …";
            if (!string.IsNullOrEmpty(preview))
                stack.Children.Add(new TextBlock
                {
                    Text = preview,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 10)
                });

            var actions = new WrapPanel();
            var btnApply = new MyButton { Text = "应用到实例", MinWidth = 120, Margin = new Thickness(0, 0, 12, 0) };
            var capturedPreset = preset;
            btnApply.Click += (_, _) => ApplyPresetToInstance(capturedPreset);
            actions.Children.Add(btnApply);

            var btnDelete = new MyButton { Text = "删除", MinWidth = 80, ColorType = MyButton.ColorState.Red };
            btnDelete.Click += (_, _) => DeletePreset(capturedPreset.Name);
            actions.Children.Add(btnDelete);

            stack.Children.Add(actions);
            card.Children.Add(stack);
            PanPresetList.Children.Add(card);
        }
    }

    private void ApplyPresetToInstance(PresetEntry preset)
    {
        // 弹窗选择目标实例
        var instances = SymbioTools.EnumerateAllInstances();
        if (instances.Count == 0)
        {
            HintService.Hint("未找到任何实例。", HintType.Error);
            return;
        }

        var sels = instances.Select(i => (IMyRadio)new MyRadioBox { Text = i.Name }).ToList();
        var idx = ModMain.MyMsgBoxSelect(sels, "选择要应用预设的目标实例", "应用", "取消", true);
        if (idx is null || idx < 0 || idx >= instances.Count)
            return;

        var inst = instances[idx.Value];

        // 检查预设里是否有本地文件（而非仅模组名）
        var realFiles = preset.ModFiles.Where(File.Exists).ToList();
        if (realFiles.Count == 0)
        {
            HintService.Hint("该预设仅记录了模组名称（未包含本地文件），请先在对应版本下载模组后再导入。", HintType.Info);
            return;
        }

        var presetWithFiles = new PresetEntry { Name = preset.Name, ModFiles = realFiles };
        var count = SymbioTools.ApplyPreset(presetWithFiles, inst, true);
        HintService.Hint($"已将 {count} 个模组应用到 {inst.Name}。", HintType.Success);
    }

    private void DeletePreset(string name)
    {
        var r = ModMain.MyMsgBox($"确定删除预设「{name}」吗？", "删除预设", button2: "删除", button3: "取消");
        if (r == 2)
        {
            SymbioTools.RemovePreset(name);
            RefreshPresets();
            HintService.Hint("预设已删除。", HintType.Success);
        }
    }
}
