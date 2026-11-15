using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.UI;

namespace PCL;

public partial class PageToolsLabs
{
    public PageToolsLabs()
    {
        InitializeComponent();
        PageEnter += Page_Entered;
        Loaded += (_, _) => RefreshInstances();
    }

    private void Page_Entered()
    {
        RefreshInstances();
        if (!string.IsNullOrEmpty(SymbioTools.Config.PortalPath) && string.IsNullOrEmpty(TextPortalPath.Text))
            TextPortalPath.Text = SymbioTools.Config.PortalPath;
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

    #region 联机 Portal

    private void BtnBrowsePortal_Click(object sender, MouseButtonEventArgs e)
    {
        var file = SystemDialogs.SelectFile("选择 Portal 程序");
        if (!string.IsNullOrEmpty(file))
        {
            TextPortalPath.Text = file;
            SymbioTools.Config.PortalPath = file;
            SymbioTools.SaveConfig();
        }
    }

    private void BtnLaunchPortal_Click(object sender, MouseButtonEventArgs e)
    {
        var path = TextPortalPath.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            path = AutoDetectPortal();
        }

        if (!string.IsNullOrEmpty(path))
        {
            SymbioTools.Config.PortalPath = path;
            SymbioTools.SaveConfig();
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            HintService.Hint("未找到 Portal 程序。请点击「浏览」手动选择 Portal 可执行文件。", HintType.Error);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            HintService.Hint("已启动 Portal 联机工具。", HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "启动 Portal 失败");
            HintService.Hint("启动 Portal 失败：" + ex.Message, HintType.Error);
        }
    }

    private static string? AutoDetectPortal()
    {
        // 常见候选：与启动器同目录、下载目录等
        var candidates = new[]
        {
            Path.Combine(ModBase.exePath, "Portal.exe"),
            Path.Combine(ModBase.exePath, "Portal", "Portal.exe"),
        };

        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        return null;
    }

    #endregion

    #region 开服务器

    private void BtnOpenServerFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = SelectedInstance;
        if (inst is null) { HintService.Hint("请先选择实例。", HintType.Error); return; }

        var serverDir = Path.Combine(inst.PathInstance, "server");
        if (!Directory.Exists(serverDir))
            Directory.CreateDirectory(serverDir);

        SymbioTools.OpenFolder(serverDir);
    }

    private void BtnCreateServerScript_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = SelectedInstance;
        if (inst is null) { HintService.Hint("请先选择实例。", HintType.Error); return; }

        var serverDir = Path.Combine(inst.PathInstance, "server");
        Directory.CreateDirectory(serverDir);

        // 生成启动脚本（需 server.jar）
        var scriptPath = Path.Combine(serverDir, "启动服务器.bat");
        var content = "@echo off\r\n"
                      + "chcp 65001 >nul\r\n"
                      + "title Minecraft Server\r\n"
                      + "if not exist server.jar (\r\n"
                      + "    echo 未找到 server.jar，请先放入服务器核心文件（server.jar）。\r\n"
                      + "    pause\r\n"
                      + "    exit /b\r\n"
                      + ")\r\n"
                      + "java -Xmx2G -Xms1G -jar server.jar nogui\r\n"
                      + "pause\r\n";

        try
        {
            File.WriteAllText(scriptPath, content, System.Text.Encoding.UTF8);
            HintService.Hint($"已生成启动脚本：{scriptPath}", HintType.Success);
            SymbioTools.OpenFolder(serverDir);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "生成启动脚本失败");
            HintService.Hint("生成脚本失败：" + ex.Message, HintType.Error);
        }
    }

    #endregion
}
