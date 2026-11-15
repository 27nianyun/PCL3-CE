using System;
using System.Diagnostics;
using System.IO;
using PCL.Core.App.IoC;
using PCL.Core.Utils.Exts;

namespace PCL.Core.App.Essentials;

[LifecycleService(LifecycleState.BeforeLoading)]
public sealed class UpdateService : GeneralService
{
    private static LifecycleContext? _context;
    private static LifecycleContext Context => _context!;

    private UpdateService() : base("update", "更新", false) { _context = ServiceContext; }

    public override void Start()
    {
        // PCL₃：已彻底禁用自更新，防止从原地址下载后覆盖修改内容。
        // 无论是否传入 update/update_finished/update_failed 参数，都不执行任何文件替换或下载操作。
        Context.Debug("自更新功能已禁用，忽略所有更新参数");
        Context.DeclareStopped();
    }
}
