using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>资源收藏页面：收藏在线项目并与本地实例安装状态同步。</summary>
public partial class PageToolsFavorites
{
    public PageToolsFavorites()
    {
        InitializeComponent();
        PageEnter += RefreshAll;
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshInstances();
        RefreshFavorites();
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

    private void ComboInstance_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFavorites();

    private void RefreshFavorites()
    {
        PanFavoriteList.Children.Clear();
        var favorites = SymbioTools.GetFavorites();
        var instance = GetSelectedInstance();

        if (favorites.Count == 0)
        {
            LabFavSummary.Text = "还没有收藏。可以在上方添加，或从已安装模组批量导入。";
            return;
        }

        var installed = 0;
        if (instance is not null)
            installed = favorites.Count(f => SymbioTools.IsFavoriteInstalled(instance, f));

        LabFavSummary.Text = instance is null
            ? $"共 {favorites.Count} 条收藏。选择一个实例可以显示安装状态。"
            : $"共 {favorites.Count} 条收藏，「{instance.Name}」中已安装 {installed} 条。";

        foreach (var favorite in favorites) PanFavoriteList.Children.Add(BuildFavoriteCard(favorite, instance));
    }

    private MyCard BuildFavoriteCard(FavoriteEntry favorite, McInstance instance)
    {
        var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = favorite.Name };
        var stack = new StackPanel { Margin = new Thickness(25, 36, 25, 14) };

        var isInstalled = instance is not null && SymbioTools.IsFavoriteInstalled(instance, favorite);
        var statusText = instance is null ? "" : isInstalled ? "✅ 已安装到当前实例" : "· 当前实例未安装";
        var statusColor = isInstalled ? Color.FromRgb(0x2E, 0x9E, 0x44) : Color.FromRgb(0x8A, 0x8A, 0x8A);

        var line = $"{SymbioTools.FavoriteTypeLabel(favorite.Type)}" +
                   (string.IsNullOrWhiteSpace(favorite.Note) ? "" : $"　{favorite.Note}");
        stack.Children.Add(new TextBlock
        {
            Text = line,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3")
        });

        if (instance is not null)
            stack.Children.Add(new TextBlock
            {
                Text = statusText,
                FontSize = 12.5,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(statusColor)
            });

        stack.Children.Add(new TextBlock
        {
            Text = $"添加于 {favorite.AddedAt:yyyy-MM-dd HH:mm}",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3")
        });

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var btnOpen = new MyButton { Text = "打开链接", MinWidth = 110, Margin = new Thickness(0, 0, 10, 0) };
        btnOpen.IsEnabled = !string.IsNullOrWhiteSpace(favorite.Url);
        btnOpen.Click += (_, _) =>
        {
            try
            {
                ModBase.OpenWebsite(favorite.Url);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "打开收藏链接失败", ModBase.LogLevel.Hint);
            }
        };
        buttons.Children.Add(btnOpen);

        var btnRemove = new MyButton
        {
            Text = "取消收藏",
            MinWidth = 110,
            ColorType = MyButton.ColorState.Red
        };
        btnRemove.Click += (_, _) =>
        {
            if (ModMain.MyMsgBox($"确定取消收藏「{favorite.Name}」吗？", "取消收藏", "取消收藏", "返回") == 2) return;
            SymbioTools.RemoveFavorite(favorite);
            RefreshFavorites();
        };
        buttons.Children.Add(btnRemove);

        stack.Children.Add(buttons);
        card.Children.Add(stack);
        return card;
    }

    private void BtnAddFavorite_Click(object sender, MouseButtonEventArgs e)
    {
        var name = TextFavName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            HintService.Hint("请先填写要收藏的名称。", HintType.Error);
            return;
        }

        var type = (ComboFavType.SelectedItem as MyComboBoxItem)?.Tag?.ToString() ?? "mod";
        var entry = new FavoriteEntry
        {
            Id = name.ToLowerInvariant(),
            Name = name,
            Type = type,
            Url = TextFavUrl.Text?.Trim() ?? "",
            Note = TextFavNote.Text?.Trim() ?? ""
        };

        if (!SymbioTools.AddFavorite(entry))
        {
            HintService.Hint("这个项目已经在收藏里了。", HintType.Warning);
            return;
        }

        TextFavName.Text = "";
        TextFavUrl.Text = "";
        TextFavNote.Text = "";
        HintService.Hint($"已收藏「{name}」。", HintType.Success);
        RefreshFavorites();
    }

    private void BtnImportMods_Click(object sender, MouseButtonEventArgs e)
    {
        var instance = GetSelectedInstance();
        if (instance is null)
        {
            HintService.Hint("请先在下方选择一个实例。", HintType.Error);
            return;
        }

        var modsFolder = SymbioTools.GetSubFolder(instance, "mods");
        if (!System.IO.Directory.Exists(modsFolder))
        {
            HintService.Hint("这个实例没有 mods 目录。", HintType.Error);
            return;
        }

        var added = 0;
        var skipped = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(modsFolder, "*.jar",
                     System.IO.SearchOption.TopDirectoryOnly))
        {
            try
            {
                var comp = new ModLocalComp.LocalCompFile(file);
                comp.Load();
                var displayName = comp.Name ?? comp.ModId ?? System.IO.Path.GetFileNameWithoutExtension(file);
                var id = comp.ModId ?? System.IO.Path.GetFileNameWithoutExtension(file);
                var entry = new FavoriteEntry
                {
                    Id = id.ToLowerInvariant(),
                    Name = displayName,
                    Type = "mod",
                    Url = "https://modrinth.com/mods?q=" + Uri.EscapeDataString(id),
                    Note = comp.Version ?? ""
                };
                if (SymbioTools.AddFavorite(entry)) added++;
                else skipped++;
            }
            catch
            {
                skipped++;
            }
        }

        HintService.Hint($"已收藏 {added} 个模组（跳过 {skipped} 个已存在的）。", HintType.Success);
        RefreshFavorites();
    }

    private void BtnRefreshFavorites_Click(object sender, MouseButtonEventArgs e)
    {
        RefreshInstances();
        RefreshFavorites();
    }
}
