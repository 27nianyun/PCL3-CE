using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;

namespace PCL;

public static class UpdateManager
{
    public static bool isUpdateWaitingRestart;

    /// <summary>
    ///     更新源。当前指向本项目的 GitHub 仓库 Release。
    /// </summary>
    public static UpdatesWrapperModel remoteServer = new(new List<IUpdateSource>
    {
        new UpdatesGitHubModel(ModBase.UpdateOwner, ModBase.UpdateRepo)
    });

    public static bool IsCurrentVersionBeta
    {
        get
        {
            if (ModBase.versionBaseName.Contains("beta"))
                return true;
            return (int)Config.Update.UpdateChannel == 1;
        }
    }

    public static UpdateEnums.VersionStatus GetVersionStatus()
    {
        try
        {
            var channel = IsCurrentVersionBeta ? UpdateChannel.beta : UpdateChannel.stable;
            var arch = SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64;
            if (remoteServer.IsLatest(channel, arch, SemVer.Parse(ModBase.versionBaseName), ModBase.versionCode))
                return UpdateEnums.VersionStatus.Latest;
            return UpdateEnums.VersionStatus.NotLatest;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Update] 检查版本状态失败");
            return UpdateEnums.VersionStatus.Unknown;
        }
    }

    public static ModLoader.LoaderCombo<JsonObject> updateLoader;

    /// <summary>
    ///     开始更新流程：检查最新版本并下载更新文件。下载完成后等待重启安装。
    /// </summary>
    public static void UpdateStart(UpdateEnums.UpdateType type, string receivedKey = null, bool forceValidated = false)
    {
        if (updateLoader is not null && updateLoader.State == ModBase.LoadState.Loading)
        {
            ModBase.Log("[Update] 已有更新任务进行中，忽略重复请求");
            return;
        }

        var channel = IsCurrentVersionBeta ? UpdateChannel.beta : UpdateChannel.stable;
        var arch = SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64;

        VersionDataModel version;
        try
        {
            remoteServer.RefreshCache();
            version = remoteServer.GetLatestVersion(channel, arch);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "获取最新版本信息失败",
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Update.Check.Failed"));
            return;
        }

        // 目标版本不高于当前版本时无需更新
        if (SemVer.Parse(version.VersionName) <= SemVer.Parse(ModBase.versionBaseName))
        {
            isUpdateWaitingRestart = false;
            HintService.Hint(Lang.Text("Setup.Update.Latest"), HintType.Success);
            return;
        }

        var output = ModBase.pathTemp + "SE-Latest.exe";
        var downloadLoaders = remoteServer.GetDownloadLoader(channel, arch, output, version);

        var loader = new ModLoader.LoaderCombo<JsonObject>(Lang.Text("Update.Task.Install"), downloadLoaders);
        loader.OnStateChanged = loaderInstance =>
        {
            switch (loaderInstance.State)
            {
                case ModBase.LoadState.Finished:
                {
                    isUpdateWaitingRestart = true;
                    ModBase.Log("[Update] 更新文件下载完成，等待重启安装");
                    ModMain.frmMain?.RefreshRestartButton(true);
                    if (type != UpdateEnums.UpdateType.Silent)
                        ModMain.MyMsgBox(
                            Lang.Text("Update.DownloadFinished.Message"),
                            Lang.Text("Update.Title"),
                            Lang.Text("Update.DownloadFinished.RestartNow"),
                            Lang.Text("Update.DownloadFinished.RestartLater"));
                    break;
                }
                case ModBase.LoadState.Failed:
                {
                    ModBase.Log(
                        "[Update] 更新下载失败",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Update.Error.OperationFailed"));
                    break;
                }
            }
        };
        updateLoader = loader;
        loader.Start();
        ModLoader.LoaderTaskbarAdd(loader);
        ModMain.frmMain?.BtnExtraDownload.ShowRefresh();
    }

    /// <summary>
    ///     安装已下载的更新：替换当前程序文件并（可选）重启。
    /// </summary>
    public static void UpdateRestart(bool triggerRestartAndByEnd, bool triggerRestart = true)
    {
        isUpdateWaitingRestart = false;

        var latestExe = ModBase.pathTemp + "SE-Latest.exe";
        string currentExe;
        try
        {
            currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        }
        catch
        {
            currentExe = "";
        }

        if (!File.Exists(latestExe) || string.IsNullOrEmpty(currentExe))
        {
            ModBase.Log("[Update] 更新文件不存在，无法安装", ModBase.LogLevel.Hint);
            return;
        }

        // 生成延迟替换脚本：等待进程退出 → 覆盖 → 可选重启
        var batPath = ModBase.pathTemp + "PCLUpdate.bat";
        var exeName = Path.GetFileName(currentExe);
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine(":wait");
        sb.AppendLine("timeout /t 1 /nobreak >nul");
        sb.AppendLine("tasklist /fi \"IMAGENAME eq " + exeName + "\" | find /i \"" + exeName + "\" >nul && goto wait");
        sb.AppendLine("copy /y \"" + latestExe + "\" \"" + currentExe + "\" >nul");
        if (triggerRestart)
            sb.AppendLine("start \"\" \"" + currentExe + "\"");
        sb.AppendLine("del \"%~f0\" >nul 2>&1");
        ModBase.WriteFile(batPath, sb.ToString(), encoding: Encoding.GetEncoding(0));

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + batPath + "\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        });

        // 结束程序，让批处理在后台完成替换与重启
        FormMain.EndProgramForce(isUpdating: true);
    }

    /// <summary>
    ///     确保 PathTemp 下的 SE-Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    /// </summary>
    internal static void DownloadLatestPCL(ModLoader.LoaderBase loaderToSyncProgress = null)
    {
        try
        {
            var channel = UpdateChannel.stable;
            var arch = SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64;
            var version = remoteServer.GetLatestVersion(channel, arch);
            var output = ModBase.pathTemp + "SE-Latest.exe";
            var loaders = remoteServer.GetDownloadLoader(channel, arch, output, version);

            // 同步执行下载
            var combo = new ModLoader.LoaderCombo<JsonObject>(Lang.Text("Update.Task.DownloadLatestStable"), loaders);
            combo.WaitForExit(null, loaderToSyncProgress);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Update] 下载最新稳定版 PCL 失败");
        }
    }

    public static ModLoader.LoaderTask<int, int> serverLoader =
        new(Lang.Text("Update.Service.PclCe"),
            _ => LoadOnlineInfo(),
            priority: ThreadPriority.BelowNormal);

    private static void LoadOnlineInfo()
    {
        ScheduleBasedOnConfig();
        AnnouncementService.Load();
    }

    private static void ScheduleBasedOnConfig()
    {
        // 根据配置决定是否自动检查并处理更新
        var mode = Config.Update.UpdateMode;
        if (mode == LauncherAutoUpdateBehavior.Disable)
            return;

        var status = GetVersionStatus();
        switch (status)
        {
            case UpdateEnums.VersionStatus.NotLatest:
            {
                switch (mode)
                {
                    case LauncherAutoUpdateBehavior.DownloadAndInstall:
                        UpdateStart(UpdateEnums.UpdateType.Silent);
                        break;
                    case LauncherAutoUpdateBehavior.DownloadAndAnnounce:
                    case LauncherAutoUpdateBehavior.AnnounceOnly:
                        ModMain.MyMsgBox(
                            Lang.Text("Update.Available", ModBase.versionBaseName,
                                remoteServer.GetLatestVersion(
                                        IsCurrentVersionBeta ? UpdateChannel.beta : UpdateChannel.stable,
                                        SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64)
                                    .VersionName),
                            Lang.Text("Update.Title"),
                            Lang.Text("Update.Action"),
                            Lang.Text("Common.Action.Cancel"));
                        break;
                }

                break;
            }
        }
    }

    /// <summary>
    ///     展示社区版提示
    /// </summary>
    /// <param name="IsUpdate">是否为更新时启动</param>
    public static void ShowCEAnnounce()
    {
        ModMain.MyMsgBox(Lang.Text("Update.CommunityNotice.Body"),
            Lang.Text("Update.CommunityNotice.Title"),
            Lang.Text("Update.CommunityNotice.Confirm"));
    }
}
