using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App;

namespace PCL;

public partial class PageToolsGuide
{
    public PageToolsGuide()
    {
        InitializeComponent();
        PanSearchBox.Search += (_, _) => SearchGuide();
        PanSchematicSearch.Search += (_, _) => SearchSchematic();
    }

    private void BtnSearch_Click(object sender, MouseButtonEventArgs e) => SearchGuide();
    private void BtnSearchSchematic_Click(object sender, MouseButtonEventArgs e) => SearchSchematic();

    private string? SearchText => PanSearchBox.Text?.Trim();

    private void SearchGuide()
    {
        var keyword = SearchText;
        if (string.IsNullOrEmpty(keyword))
        {
            HintService.Hint("请输入搜索关键词。", HintType.Error);
            return;
        }

        PanGuideResults.Children.Clear();
        var links = SymbioTools.BuildGuideLinks(keyword);
        foreach (var (title, url) in links)
            AddLinkCard(PanGuideResults, title, url);
    }

    private void SearchSchematic()
    {
        var keyword = PanSchematicSearch.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            HintService.Hint("请输入投影搜索关键词。", HintType.Error);
            return;
        }

        PanSchematicResults.Children.Clear();
        var links = SymbioTools.BuildSchematicLinks(keyword);
        foreach (var (title, url) in links)
            AddLinkCard(PanSchematicResults, title, url);
    }

    private void AddLinkCard(StackPanel panel, string title, string url)
    {
        var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = title };
        var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };

        stack.Children.Add(new TextBlock
        {
            Text = url,
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var btn = new MyButton { Text = "在浏览器打开", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var capturedUrl = url;
        btn.Click += (_, _) => OpenUrl(capturedUrl);
        stack.Children.Add(btn);

        card.Children.Add(stack);
        panel.Children.Add(card);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开链接失败：" + url);
        }
    }

    private void BtnCatPack_Click(object sender, MouseButtonEventArgs e) => SearchByCategory("整合包");
    private void BtnCatPvp_Click(object sender, MouseButtonEventArgs e) => SearchByCategory("PvP");
    private void BtnCatSurvival_Click(object sender, MouseButtonEventArgs e) => SearchByCategory("生存");
    private void BtnCatRedstone_Click(object sender, MouseButtonEventArgs e) => SearchByCategory("红石");
    private void BtnCatBuild_Click(object sender, MouseButtonEventArgs e) => SearchByCategory("建筑");
    private void BtnCatCommand_Click(object sender, MouseButtonEventArgs e) => SearchByCategory("指令");

    private void SearchByCategory(string category)
    {
        PanSearchBox.Text = category;
        SearchGuide();
    }
}
