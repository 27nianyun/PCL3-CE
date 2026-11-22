using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PCL;

/// <summary>关系图节点类型。</summary>
public enum DepNodeKind
{
    Normal,
    Warning,
    Missing
}

/// <summary>关系图连线类型。</summary>
public enum DepEdgeKind
{
    Satisfied,
    Optional,
    Missing
}

/// <summary>关系图中的一个节点（模组或缺失的前置占位）。</summary>
public sealed class DepGraphNode
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public DepNodeKind Kind { get; init; }

    public double X { get; set; }
    public double Y { get; set; }
    public double BoxWidth { get; set; } = 176;
    public double BoxHeight { get; set; } = 52;

    internal Border Visual { get; set; }
}

/// <summary>关系图中的一条连线：From 依赖 To。</summary>
public sealed class DepGraphEdge
{
    public string FromKey { get; init; } = "";
    public string ToKey { get; init; } = "";
    public DepEdgeKind Kind { get; init; }
    public string Tip { get; init; } = "";

    internal DepGraphNode From { get; set; }
    internal DepGraphNode To { get; set; }
    internal Path Visual { get; set; }
}

/// <summary>
///     简易分层依赖关系图：按「依赖深度」分列，用贝塞尔曲线连接，节点可拖动。
/// </summary>
/// <remarks>
///     PCL₃-CE 新增控件（配合工具页「模组依赖图谱」使用）。
///     不引入第三方图形库，纯 Canvas 绘制，避免增加打包体积。
/// </remarks>
public class MyDependencyGraph : Canvas
{
    private const double ColumnGap = 84;
    private const double RowGap = 16;
    private const double Pad = 26;

    private readonly List<DepGraphNode> _nodes = [];
    private readonly List<DepGraphEdge> _edges = [];

    private DepGraphNode _dragNode;
    private Point _dragOffset;
    private bool _layoutDone;

    public MyDependencyGraph()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Width = 320;
        Height = 320;
    }

    /// <summary>设置数据并重排版；在 UI 线程调用。</summary>
    public void SetData(IReadOnlyList<DepGraphNode> nodes, IReadOnlyList<DepGraphEdge> edges)
    {
        _nodes.Clear();
        _nodes.AddRange(nodes);
        _edges.Clear();

        foreach (var edge in edges)
        {
            edge.From = _nodes.FirstOrDefault(n => n.Key == edge.FromKey);
            edge.To = _nodes.FirstOrDefault(n => n.Key == edge.ToKey);
            if (edge.From is null || edge.To is null || ReferenceEquals(edge.From, edge.To)) continue;
            _edges.Add(edge);
        }

        _layoutDone = false;
        BuildLayout();
        Render();
    }

    /// <summary>恢复自动排版（拖动后想还原时调用）。</summary>
    public void ResetLayout()
    {
        if (_nodes.Count == 0) return;
        BuildLayout();
        Render();
    }

    /// <summary>已排版的节点数，供外部判断是否为空图。</summary>
    public int NodeCount => _nodes.Count;

    #region 排版

    private void BuildLayout()
    {
        if (_nodes.Count == 0)
        {
            Width = 0;
            Height = 0;
            return;
        }

        // 出边表：From（依赖方）→ 它依赖的节点
        var outgoing = new Dictionary<string, List<DepGraphNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in _edges)
        {
            if (!outgoing.TryGetValue(edge.FromKey, out var list))
                outgoing[edge.FromKey] = list = [];
            if (!list.Contains(edge.To)) list.Add(edge.To);
        }

        // 深度 = 依赖链最长长度；被依赖方在最左列。用 visiting 防环。
        var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int Walk(DepGraphNode node, HashSet<string> visiting)
        {
            if (depth.TryGetValue(node.Key, out var known)) return known;
            if (!visiting.Add(node.Key)) return 0;

            var best = 0;
            if (outgoing.TryGetValue(node.Key, out var targets))
                foreach (var target in targets)
                    best = Math.Max(best, Walk(target, visiting) + 1);

            visiting.Remove(node.Key);
            depth[node.Key] = best;
            return best;
        }

        foreach (var node in _nodes) Walk(node, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var columns = _nodes
            .GroupBy(n => depth.TryGetValue(n.Key, out var d) ? d : 0)
            .OrderBy(g => g.Key)
            .ToList();

        var colWidth = _nodes.Max(n => n.BoxWidth);
        var rowHeight = _nodes.Max(n => n.BoxHeight);
        var maxRows = columns.Max(g => g.Count());
        var contentHeight = maxRows * (rowHeight + RowGap) - RowGap;

        foreach (var column in columns)
        {
            // 缺失前置排在最前，其余按名称排序，布局稳定便于对照前后两次分析
            var list = column
                .OrderBy(n => n.Kind == DepNodeKind.Missing ? 0 : 1)
                .ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var colHeight = list.Count * (rowHeight + RowGap) - RowGap;
            var y0 = Pad + (contentHeight - colHeight) / 2;

            for (var i = 0; i < list.Count; i++)
            {
                list[i].X = Pad + column.Key * (colWidth + ColumnGap);
                list[i].Y = y0 + i * (rowHeight + RowGap);
            }
        }

        Width = Pad * 2 + columns.Count * colWidth + Math.Max(0, columns.Count - 1) * ColumnGap;
        Height = Math.Max(Pad * 2 + contentHeight, 220);
        _layoutDone = true;
    }

    #endregion

    #region 绘制

    private void Render()
    {
        Children.Clear();
        _dragNode = null;
        if (!_layoutDone) return;

        foreach (var edge in _edges)
        {
            edge.Visual = BuildEdgeVisual(edge);
            Children.Add(edge.Visual);
        }

        foreach (var node in _nodes)
        {
            node.Visual = BuildNodeVisual(node);
            Children.Add(node.Visual);
        }
    }

    private Path BuildEdgeVisual(DepGraphEdge edge)
    {
        var color = edge.Kind switch
        {
            DepEdgeKind.Missing => Color.FromRgb(0xE5, 0x4A, 0x4A),
            DepEdgeKind.Optional => Color.FromRgb(0xE0, 0x9B, 0x2E),
            _ => Color.FromArgb(0x99, 0x8C, 0x8C, 0x8C)
        };

        var path = new Path
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = edge.Kind == DepEdgeKind.Satisfied ? 1.4 : 1.9,
            Data = BuildEdgeGeometry(edge),
            IsHitTestVisible = false
        };

        if (edge.Kind != DepEdgeKind.Satisfied)
            path.StrokeDashArray = new DoubleCollection { 4, 2.5 };

        if (!string.IsNullOrWhiteSpace(edge.Tip)) path.ToolTip = edge.Tip;
        return path;
    }

    private static Geometry BuildEdgeGeometry(DepGraphEdge edge)
    {
        // 被依赖方在左，依赖方在右
        var left = edge.To;
        var right = edge.From;

        var x1 = left.X + left.BoxWidth;
        var y1 = left.Y + left.BoxHeight / 2;
        var x2 = right.X;
        var y2 = right.Y + right.BoxHeight / 2;

        var dx = Math.Max(28, (x2 - x1) * 0.45);
        var figure = new PathFigure
        {
            StartPoint = new Point(x1, y1),
            IsFilled = false,
            IsClosed = false
        };
        figure.Segments.Add(new BezierSegment(new Point(x1 + dx, y1), new Point(x2 - dx, y2),
            new Point(x2, y2), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private Border BuildNodeVisual(DepGraphNode node)
    {
        var (borderColor, fillColor) = node.Kind switch
        {
            DepNodeKind.Missing => (Color.FromRgb(0xE5, 0x4A, 0x4A), Color.FromArgb(0x24, 0xE5, 0x4A, 0x4A)),
            DepNodeKind.Warning => (Color.FromRgb(0xE0, 0x9B, 0x2E), Color.FromArgb(0x1E, 0xE0, 0x9B, 0x2E)),
            _ => (Color.FromRgb(0x2A, 0x6F, 0xC9), Color.FromArgb(0x18, 0x2A, 0x6F, 0xC9))
        };

        var title = new TextBlock
        {
            Text = node.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = node.Kind == DepNodeKind.Missing
                ? new SolidColorBrush(Color.FromRgb(0xE5, 0x4A, 0x4A))
                : FindBrush("ColorBrushGray1", Color.FromRgb(0x40, 0x40, 0x40))
        };

        var subtitle = new TextBlock
        {
            Text = node.Subtitle,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = FindBrush("ColorBrushGray3", Color.FromRgb(0x8C, 0x8C, 0x8C))
        };

        var stack = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
        stack.Children.Add(title);
        stack.Children.Add(subtitle);

        var box = new Border
        {
            Width = node.BoxWidth,
            Height = node.BoxHeight,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1.2),
            BorderBrush = new SolidColorBrush(borderColor),
            Background = new SolidColorBrush(fillColor),
            Child = stack,
            Cursor = Cursors.SizeAll,
            ToolTip = string.IsNullOrWhiteSpace(node.Subtitle) ? node.Title : $"{node.Title}\n{node.Subtitle}"
        };

        Canvas.SetLeft(box, node.X);
        Canvas.SetTop(box, node.Y);

        box.MouseLeftButtonDown += NodeMouseDown;
        box.MouseMove += NodeMouseMove;
        box.MouseLeftButtonUp += NodeMouseUp;

        return box;
    }

    private static Brush FindBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    #endregion

    #region 拖动

    private void NodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border box) return;
        var node = _nodes.FirstOrDefault(n => ReferenceEquals(n.Visual, box));
        if (node is null) return;

        _dragNode = node;
        var point = e.GetPosition(this);
        _dragOffset = new Point(point.X - node.X, point.Y - node.Y);
        box.CaptureMouse();
        box.Opacity = 0.82;
        Panel.SetZIndex(box, 10);
        e.Handled = true;
    }

    private void NodeMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is null || sender is not Border box) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var point = e.GetPosition(this);
        _dragNode.X = Math.Max(0, point.X - _dragOffset.X);
        _dragNode.Y = Math.Max(0, point.Y - _dragOffset.Y);
        Canvas.SetLeft(box, _dragNode.X);
        Canvas.SetTop(box, _dragNode.Y);

        foreach (var edge in _edges)
            if (ReferenceEquals(edge.From, _dragNode) || ReferenceEquals(edge.To, _dragNode))
                if (edge.Visual is not null)
                    edge.Visual.Data = BuildEdgeGeometry(edge);

        e.Handled = true;
    }

    private void NodeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border box)
        {
            box.ReleaseMouseCapture();
            box.Opacity = 1;
        }

        _dragNode = null;
    }

    #endregion
}
