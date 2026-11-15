using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL;

public partial class PageToolsRecommend
{
    public PageToolsRecommend()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshRecommend();
    }

    private void Page_Entered() => RefreshRecommend();

    private void BtnRefreshRecommend_Click(object sender, MouseButtonEventArgs e) => RefreshRecommend();

    private void RefreshRecommend()
    {
        PanRecommend.Children.Clear();

        // 获取系统信息
        long totalRam = 0;
        int cpuCores = 0;
        try
        {
            totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            cpuCores = Environment.ProcessorCount;
        }
        catch
        {
            // 忽略
        }

        if (totalRam <= 0)
            totalRam = 8L * 1024 * 1024 * 1024; // 默认按 8GB

        double gb = totalRam / 1024.0 / 1024.0 / 1024.0;
        bool lowSpec = gb < 6 || cpuCores <= 2;

        var memGb = SymbioTools.RecommendMemoryGb(totalRam);
        var shaderTier = SymbioTools.RecommendShaderTier(cpuCores, totalRam);
        var mods = SymbioTools.RecommendMods(cpuCores, totalRam, lowSpec);

        // 配置概览卡片
        AddInfoCard("检测到的配置", $"CPU 核心：{cpuCores} 核　内存：{gb:F1} GB", $"性能档位：{(lowSpec ? "低配" : "主流/高配")}");
        AddInfoCard("推荐分配内存", $"{memGb} GB", "可在实例设置中调整内存大小");
        AddInfoCard("推荐光影档位", shaderTier, "配合 Iris/Sodium 使用");

        // 推荐模组列表
        var modCard = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = "推荐优化模组" };
        var modStack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };
        foreach (var (name, desc) in mods)
        {
            modStack.Children.Add(new TextBlock
            {
                Text = $"• {name} — {desc}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        modCard.Children.Add(modStack);
        PanRecommend.Children.Add(modCard);
    }

    private void AddInfoCard(string title, string value, string desc)
    {
        var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = title };
        var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = desc,
            Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        card.Children.Add(stack);
        PanRecommend.Children.Add(card);
    }

    private void BtnPrefStable_Click(object sender, MouseButtonEventArgs e) => ShowVersionRecommend("稳定版", "推荐 1.21.x / 1.20.x 等长期稳定版本，模组与光影生态成熟，适合长期游玩。");
    private void BtnPrefLatest_Click(object sender, MouseButtonEventArgs e) => ShowVersionRecommend("最新版", "推荐最新正式版，体验最新特性，但部分模组可能尚未适配。");
    private void BtnPrefModded_Click(object sender, MouseButtonEventArgs e) => ShowVersionRecommend("模组友好", "推荐 Forge/NeoForge/Fabric 支持最完善的版本（如 1.20.1、1.19.2），模组数量最多。");
    private void BtnPrefPerformance_Click(object sender, MouseButtonEventArgs e) => ShowVersionRecommend("低配优化", "推荐 1.20.x + Sodium/Lithium 等优化模组，帧率与流畅度最佳。");

    private void ShowVersionRecommend(string title, string desc)
    {
        PanVersionRecommend.Children.Clear();
        var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = title };
        var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };
        stack.Children.Add(new TextBlock
        {
            Text = desc,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        card.Children.Add(stack);
        PanVersionRecommend.Children.Add(card);
    }
}
