using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PCL.Core.App;
using PCL.Core.UI;

namespace PCL;

public partial class PageToolsWheel
{
    private const int WheelItemCount = 8; // 转盘扇区数量
    private const double WheelSize = 340d; // 转盘直径
    private const double CenterX = WheelSize / 2;
    private const double CenterY = WheelSize / 2;
    private const double Radius = WheelSize / 2 - 6;

    private static readonly Random Rand = new();

    private readonly List<WheelItem> _currentItems = new();
    private double _currentAngle; // 当前累计旋转角度
    private bool _spinning;

    public PageToolsWheel()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => { Shuffle(); };
    }

    private void Page_Entered()
    {
        // 首次进入时若尚未填充，则随机一批
        if (_currentItems.Count == 0)
            Shuffle();
    }

    #region 转盘项目

    /// <summary>随机挑选一批项目放到转盘上。</summary>
    private void Shuffle()
    {
        if (_spinning)
            return;

        _currentItems.Clear();
        var pool = SymbioTools.FeaturedWheelItems
            .OrderBy(_ => Rand.Next())
            .Take(WheelItemCount)
            .ToList();
        _currentItems.AddRange(pool);

        DrawWheel();
        RefreshItemList();
        LabResult.Text = "转盘已就绪，点击「转动转盘」开始。";
    }

    /// <summary>绘制转盘扇形。</summary>
    private void DrawWheel()
    {
        PanWheel.Children.Clear();
        if (_currentItems.Count == 0)
            return;

        var sectorAngle = 360.0 / _currentItems.Count;

        // 交替使用的扇形配色（品牌蓝色系，由深到浅）
        var palette = new[]
        {
            Color.FromRgb(0x0B, 0x5B, 0xCB), // ColorBrush2
            Color.FromRgb(0x13, 0x70, 0xF3), // ColorBrush3
            Color.FromRgb(0x48, 0x90, 0xF5), // ColorBrush4
            Color.FromRgb(0x96, 0xC0, 0xF9), // ColorBrush5（浅，用深字）
        };

        for (var i = 0; i < _currentItems.Count; i++)
        {
            var start = i * sectorAngle;
            var end = start + sectorAngle;

            var color = palette[i % palette.Length];
            var isLight = i % palette.Length == 3; // 最浅的那档用深色文字

            // 绘制扇形 Path
            var path = BuildSectorPath(start, end, color);
            PanWheel.Children.Add(path);

            // 绘制扇形标签文字
            var label = BuildSectorLabel(_currentItems[i], start, end, sectorAngle, isLight);
            PanWheel.Children.Add(label);
        }

        // 中心圆点
        var centerDot = new Ellipse
        {
            Width = 56,
            Height = 56,
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(centerDot, CenterX - 28);
        Canvas.SetTop(centerDot, CenterY - 28);
        PanWheel.Children.Add(centerDot);
    }

    /// <summary>构造一个扇形 Path（从 start 到 end 角度）。</summary>
    private static Path BuildSectorPath(double start, double end, Color color)
    {
        var startRad = (start - 90) * Math.PI / 180.0;
        var endRad = (end - 90) * Math.PI / 180.0;

        var p1 = new Point(CenterX + Radius * Math.Cos(startRad), CenterY + Radius * Math.Sin(startRad));
        var p2 = new Point(CenterX + Radius * Math.Cos(endRad), CenterY + Radius * Math.Sin(endRad));

        var figure = new PathFigure
        {
            StartPoint = new Point(CenterX, CenterY),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment(p1, true));
        figure.Segments.Add(new ArcSegment
        {
            Point = p2,
            Size = new Size(Radius, Radius),
            IsLargeArc = end - start > 180,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.5,
        };
    }

    /// <summary>构造扇形标签文字（旋转到扇区中间角度，沿径向排列）。</summary>
    private static TextBlock BuildSectorLabel(WheelItem item, double start, double end, double sectorAngle, bool isLight)
    {
        // 扇形中线角度（0°=右侧，顺时针递增）
        var mid = start + sectorAngle / 2;
        // 用标准数学角：0°=正右，逆时针为正。扇形绘制时起点为顶部(-90°)，故换算
        var midRad = (mid - 90) * Math.PI / 180.0;

        // 文字沿半径方向放置，中心落在半径 62% 处
        var textRadius = Radius * 0.60;
        var cx = CenterX + textRadius * Math.Cos(midRad);
        var cy = CenterY + textRadius * Math.Sin(midRad);

        var name = item.Name;
        if (name.Length > 12)
            name = name[..12] + "…";

        var text = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(isLight ? Color.FromRgb(0x0B, 0x5B, 0xCB) : Colors.White),
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
            // 关键：不设固定 Width，用 Measure 得到实际尺寸后再定位，避免文字被拉伸/重叠
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = text.DesiredSize.Width;
        var textHeight = text.DesiredSize.Height;

        // 文字旋转角：让文字沿半径方向，从圆心向外读。
        // 若扇区在左半（中线位于 90°~270° 即文字头朝下），再补 180° 使文字保持正立可读。
        var rotation = mid - 90;
        if (rotation > 90 || rotation < -90)
            rotation += 180;

        text.RenderTransform = new RotateTransform(rotation);
        text.RenderTransformOrigin = new Point(0.5, 0.5);

        // 以文字中心为锚点放置到 (cx, cy)
        Canvas.SetLeft(text, cx - textWidth / 2);
        Canvas.SetTop(text, cy - textHeight / 2);

        return text;
    }

    private void RefreshItemList()
    {
        PanWheelItems.Children.Clear();
        if (_currentItems.Count == 0)
            return;

        foreach (var item in _currentItems)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var kindTag = new TextBlock
            {
                Text = $"[{SymbioTools.KindLabel(item.Kind)}] ",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrush3"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var nameTag = new TextBlock
            {
                Text = item.Name,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(kindTag);
            row.Children.Add(nameTag);
            PanWheelItems.Children.Add(row);
        }
    }

    #endregion

    #region 转动

    private void BtnShuffle_Click(object sender, MouseButtonEventArgs e) => Shuffle();

    private void BtnSpin_Click(object sender, MouseButtonEventArgs e)
    {
        if (_spinning || _currentItems.Count == 0)
            return;

        _spinning = true;
        LabResult.Text = "转盘中……";

        // 随机命中一个扇区（指针在顶部 = -90° 方向）
        var hitIndex = Rand.Next(_currentItems.Count);
        var sectorAngle = 360.0 / _currentItems.Count;

        // 目标：让第 hitIndex 个扇区的中线转到指针（顶部 -90°）位置
        var sectorMid = hitIndex * sectorAngle + sectorAngle / 2;
        // 当前角度下，指针方向对应的扇区中线角度应等于 sectorMid
        // 需要旋转的角度 = (sectorMid) 到顶部(-90) 的差值，加上若干整圈
        var targetDelta = NormalizeAngle(-90 - sectorMid);
        // 至少转 4 圈，增加悬念
        var totalSpin = 4 * 360 + targetDelta;
        // 加上一点随机抖动让落点不总在正中心
        var jitter = (Rand.NextDouble() - 0.5) * sectorAngle * 0.5;
        totalSpin += jitter;

        // 使用旋转动画（AaRotateTransform 的 value 是增量角度）
        ModAnimation.AniStop("WheelSpin");
        ModAnimation.AniStart(
            ModAnimation.AaRotateTransform(PanWheel, totalSpin, 4200, 0, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Strong)),
            "WheelSpin");

        _currentAngle += totalSpin;

        // 动画结束后结算
        var hit = _currentItems[hitIndex];
        var delay = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(4300),
        };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            _spinning = false;
            OnSpinFinished(hit);
        };
        delay.Start();
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle < 0)
            angle += 360;
        return angle;
    }

    private void OnSpinFinished(WheelItem hit)
    {
        LabResult.Text = $"🎯 命中：{hit.Name}（{SymbioTools.KindLabel(hit.Kind)}）";

        ModMain.MyMsgBox(
            $"转盘命中了「{hit.Name}」（{SymbioTools.KindLabel(hit.Kind)}）。\n\n是否前往对应下载页获取该项目？",
            "转盘结果",
            "前往下载页",
            "取消",
            button1Action: () => OpenDownloadPage(hit));
    }

    private void OpenDownloadPage(WheelItem item)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.Url))
            {
                HintService.Hint($"「{item.Name}」暂无下载链接。", HintType.Error);
                return;
            }

            ModBase.OpenWebsite(item.Url);
            HintService.Hint($"已打开「{item.Name}」的下载页。", HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开转盘项目下载页失败");
            HintService.Hint("打开下载页失败。", HintType.Error);
        }
    }

    #endregion
}
