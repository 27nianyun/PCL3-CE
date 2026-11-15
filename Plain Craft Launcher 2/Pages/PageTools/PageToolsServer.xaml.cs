using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App;

namespace PCL;

public partial class PageToolsServer
{
    public PageToolsServer()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshAll();
    }

    private void Page_Entered() => RefreshAll();

    private void RefreshAll()
    {
        RefreshInstances();
        RefreshFeaturedServers();
        RefreshCustomServers();
    }

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

    private void RefreshFeaturedServers()
    {
        PanFeaturedServers.Children.Clear();
        foreach (var server in SymbioTools.FeaturedServers)
        {
            var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = server.Name };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };

            stack.Children.Add(new TextBlock
            {
                Text = $"{server.Address}　·　{server.Category}",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var btn = new MyButton { Text = "加入实例", MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Left };
            var captured = server;
            btn.Click += (_, _) => AddServer(captured.Name, captured.Address, captured.Port);
            stack.Children.Add(btn);

            card.Children.Add(stack);
            PanFeaturedServers.Children.Add(card);
        }
    }

    private void RefreshCustomServers()
    {
        PanCustomServers.Children.Clear();
        var servers = SymbioTools.CustomServers;
        if (servers.Count == 0)
        {
            PanCustomServers.Children.Add(new TextBlock
            {
                Text = "暂无自定义服务器。",
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 12
            });
            return;
        }

        foreach (var server in servers)
        {
            var card = new MyCard { Margin = new Thickness(0, 0, 0, 8), Title = server.Name };
            var stack = new StackPanel { Margin = new Thickness(25, 40, 25, 16) };

            stack.Children.Add(new TextBlock
            {
                Text = $"{server.Address}" + (server.Port != 25565 ? $":{server.Port}" : ""),
                Foreground = (Brush)Application.Current.TryFindResource("ColorBrushGray3"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var actions = new WrapPanel();
            var btnAdd = new MyButton { Text = "加入实例", MinWidth = 100, Margin = new Thickness(0, 0, 12, 0) };
            var captured = server;
            btnAdd.Click += (_, _) => AddServer(captured.Name, captured.Address, captured.Port);
            actions.Children.Add(btnAdd);

            var btnDel = new MyButton { Text = "删除", MinWidth = 70, ColorType = MyButton.ColorState.Red };
            btnDel.Click += (_, _) => DeleteCustom(captured.Address);
            actions.Children.Add(btnDel);

            stack.Children.Add(actions);
            card.Children.Add(stack);
            PanCustomServers.Children.Add(card);
        }
    }

    private void BtnAddCustom_Click(object sender, MouseButtonEventArgs e)
    {
        var name = TextServerName.Text?.Trim();
        var address = TextServerAddress.Text?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(address))
        {
            HintService.Hint("请填写服务器名称和地址。", HintType.Error);
            return;
        }

        var port = 25565;
        if (address.Contains(':'))
        {
            var parts = address.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var p))
            {
                port = p;
                address = parts[0];
            }
        }

        SymbioTools.AddCustomServer(name, address, port);
        HintService.Hint($"已保存自定义服务器「{name}」。", HintType.Success);
        TextServerName.Text = "";
        TextServerAddress.Text = "";
        RefreshCustomServers();
    }

    private void DeleteCustom(string address)
    {
        SymbioTools.RemoveCustomServer(address);
        RefreshCustomServers();
        HintService.Hint("已删除。", HintType.Success);
    }

    private async void AddServer(string name, string address, int port)
    {
        var inst = SelectedInstance;
        if (inst is null)
        {
            HintService.Hint("请先选择目标实例。", HintType.Error);
            return;
        }

        HintService.Hint($"正在将 {name} 加入 {inst.Name} …", HintType.Info);
        var ok = await SymbioTools.AddServerToInstanceAsync(inst, name, address, port);
        if (ok)
            HintService.Hint($"已将 {name} 加入 {inst.Name} 的服务器列表。", HintType.Success);
        else
            HintService.Hint($"加入失败，请检查实例或稍后重试。", HintType.Error);
    }
}
