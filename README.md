**简体中文** | [English](README-EN.md) | [繁體中文](README-ZH_TW.md)

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.png" alt="Logo" width="96" height="96">

# 三氯化磷启动器（PCL₃-CE）

[![Stars](https://img.shields.io/github/stars/27nianyun/PCL3-CE?style=for-the-badge&logo=github&label=stars&labelColor=444444&color=eac54f)](https://github.com/27nianyun/PCL3-CE/stargazers)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue?style=for-the-badge)](LICENSE)

[下载 PCL₃-CE](https://github.com/27nianyun/PCL3-CE/releases) ·
[提交问题](https://github.com/27nianyun/PCL3-CE/issues) ·
[更新日志](CHANGELOG.md)

</div>

**三氯化磷启动器（PCL₃-CE）** 是一款免费开源的 Minecraft 启动器，基于 PCL（Plain Craft Launcher）开源代码二次开发，在保留 PCL 原有交互习惯的基础上，补齐了基岩版支持、实例管理、资源下载、模组管理增强等能力，并加入了多项专属功能。

- 当前版本：`2.17.15`
- 上游基线：PCL `2.12.1`
- 技术栈：C# / WPF / .NET 10

> PCL₃-CE 与 PCL 官方版、PCL CE 官方版均无隶属关系，请勿向官方仓库反馈 PCL₃-CE 的问题。

---

## 功能概览

### Java 版

- 官方正式版、快照与历史版本一键安装，自动补全依赖并选择合适的 Java 运行环境
- 8 种安装选项：Forge、NeoForge、Fabric、Legacy Fabric、OptiFine、LiteLoader、Cleanroom、LabyMod
- 支持整合包安装与模组加载器切换

### 基岩版

- 读取系统中已安装的 Minecraft 基岩版，区分 UWP 旧版与 GDK 新版
- 支持导入安装包并解包到独立目录，不影响其他版本
- 内置 Xbox 账号登录，登录后可直接启动
- GDK 便携版可按版本选择加载器：原版、LeviLamina（按游戏版本自动安装）或 BLoader（随启动器分发）
- 按版本类型筛选，支持按版本号搜索与本地管理

### 资源下载

- CurseForge 与 Modrinth 双源检索
- 覆盖模组、整合包、光影、资源包、数据包与世界存档，下载后自动放入对应目录

### 实例管理

- 每个实例拥有独立的模组、存档、截图与配置
- 单个模组可随时禁用，便于排查冲突
- 存档与截图集中浏览、备份，并可一键打开所在目录
- 实例内维护常用服务器列表，支持整体打包导出

### 使用体验

- 简体中文与 English (US) 提供完整界面资源
- 主题配色与背景可自定义，页面切换与列表滚动带有过渡动画
- 自动扫描本机 Java，减少手动配置
- 启动失败时对日志进行分析，给出可读的原因与建议

---

## 本分支新增功能

相比上游 PCL SE，PCL₃-CE 额外实现了以下专属功能：

### 模组管理增强

- **模组版本自动匹配**：在「工具」页一键扫描本地模组，依据整合包要求的 Minecraft 版本与模组加载器，自动筛选并提示版本不匹配的模组，支持「只看不匹配」过滤。
- **强制删除模组**：提供「强制删除」按钮，从文件资源管理器中直接永久删除模组（不经过回收站），删除前二次确认。

### 整合包与存档导入

- **导入整合包到现有游戏文件夹**：导入整合包时，除了新建实例，还可选择直接导入到某个已存在的游戏文件夹（.minecraft 根目录），不必新建实例；实例名输入框默认填入整合包名字。
- **一键导入压缩存档**：直接选择 `.zip` / `.rar` / `.7z` / `.tar` / `.gz` 等压缩包导入存档，自动解压并定位存档目录。

### 启动器增强

- **每次启动默认为中文**：游戏每次启动都强制使用启动器语言（默认简体中文），并自动开启 Unicode 字体，避免中文乱码。
- **工具页转盘与滚动**：工具一栏支持上下滑动，转盘工具修复文字显示。
- **自动更新**：内置指向本仓库 GitHub Release 的自动更新源，可在设置中检查并更新到最新版本。
- **右上角品牌装饰图**：主界面右上角显示 PCL₃-CE 品牌徽标。

### 存档保护与快捷进入

- **启动前自动备份存档**：启动游戏前自动把有改动的存档打包为 zip，保存在 `<实例目录>\PCL\SaveBackup\<存档名>\`；每个存档可设置保留份数（默认 5 份），超出后自动清理最旧备份。可在「设置 → 游戏管理 → 存档备份」中开关。
- **继续上次的世界**：存档页一键进入最近修改过的存档，跳过主菜单（需 1.20+ 版本或支持 Quick Play 的模组）。
- **手动备份与还原**：每个存档新增「备份该存档」「还原最近备份」按钮；还原前会把当前存档改名保留为 `.restore-old-时间戳`，不会立刻删除。存档页还提供「立即备份存档」与「备份文件夹」入口。

### 工具百宝箱

「工具 → 百宝箱」集中了四个日常最常用的小工具，全部离线可用：

- **今日人品**：按日期与启动器标识生成固定种子，每天给出稳定的人品值与评语，同一天多次查询结果一致。
- **内存优化**：强制回收托管内存并清空启动器的工作集，把已经占用的物理内存归还系统；面板实时显示启动器占用与系统可用内存，优化后给出实际释放量。只作用于启动器进程，不会影响正在运行的游戏或其他程序。
- **游戏垃圾清理**：改为「先扫描、后清理」两步。扫描只统计、不删除，按崩溃报告 / 日志 / Natives / 启动器临时文件 / 启动器缓存分类列出文件数与体积；普通清理只删除 Minecraft 侧可以重新生成的内容，**不需要重启启动器**，被占用的文件会跳过并单独计数。需要连启动器缓存一起清时可用「深度清理」。
- **自定义文件下载**：填入链接、保存目录与文件名即可下载任意文件，走启动器统一的下载队列（有进度显示，可与其他任务并行），同时支持 HTTP 链接与 UNC 共享路径。

### 整合包增量更新

- **三方对比后再更新**：在「工具 → 整合包更新」选择一个新版本整合包（`.zip` / `.mrpack`），启动器会把它的文件清单与实例现状逐一按 SHA1 比对，分成「新增 / 更新 / 移除 / 已一致」四类先展示清单，确认后才下载，不必整个整合包重装。
- **支持格式**：Modrinth 格式（自带直链与哈希）与 CurseForge 清单。CurseForge 清单里没有 `downloadUrl` 时，可在同一页填入自备的 CurseForge API Key（console.curseforge.com 免费申请），启动器会用官方 API 批量解析文件名、下载地址与 SHA1；未填 Key 会给出明确提示而不是静默失败。
- **保护个性化修改**：「移除」只在存在基线清单时才判定，且默认不删除；所有下载都先落到临时文件、校验 SHA1 通过后才替换。

### 环境还原点

- **整套快照**：把实例的版本 JSON、加载器信息、模组清单与 `config` 配置打包到 `<实例目录>\PCL\Snapshots\`，随时可整套恢复。
- **不影响存档**：快照不包含 `saves`，恢复环境不会让存档进度回退。
- **可回退的还原**：还原前会把当前 `mods` / `config` 改名保留为 `*.restore-old-时间戳`，不直接删除；配置可选择「一并恢复」或「仅恢复模组」。

### 模组依赖图谱与预检

- **依赖关系可视化**：扫描 `mods` 目录里每个 jar 的依赖声明，逐条标出「谁依赖谁」、版本约束是否满足、由哪个文件提供。
- **关系图视图**：点「显示关系图」可切换为分层关系图——左侧是被依赖的前置、右侧是依赖方，用贝塞尔曲线连接；节点可直接拖动重新摆放，点「重置布局」恢复自动排版。蓝框为已安装模组、橙框为版本不满足、红框为缺失的必选前置。
- **缺失前置检测**：把缺失的必选前置单独汇总并高亮，标注是哪个模组要求的，可一键跳到 Modrinth 搜索；可选依赖缺失单独统计，不误报为错误。
- **冲突声明**：列出模组声明的硬/软冲突，并标注冲突目标是否已安装。

### 资源收藏

- **收藏任意在线项目**：模组、整合包、光影、数据包、资源包都可以记一条，保存名称、链接与备注，换实例或重装启动器后依然在。
- **与本地状态同步**：选择一个实例后，收藏页会直接显示每一条「已安装 / 未安装」，按文件名与模组名模糊比对。
- **批量导入**：一键把当前实例已安装的模组全部收藏，自动带上 Mod ID、版本与 Modrinth 搜索链接。

### 外观

- **浅色 / 深色 / 跟随系统**：在「设置 → 界面」中即时切换，可分别为浅色与深色指定主色。该能力沿用 PCL 原版主题系统，本仓库仅确认可用并纳入功能清单。


---

## 支持平台

| 操作系统 | 支持情况 | 环境要求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高 | 完整支持 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 - Windows 10 1809 (17763) | 理论能跑，酌情提供支持 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 或更低版本 | 不支持 | / |
| macOS / Linux / 其他操作系统 | 仅支持交叉编译开发，无法直接运行 | [.NET 10 SDK](https://get.dot.net/10) |

> 仅对最新版本的启动器提供支持。

## 下载与使用

1. 从 [Releases](https://github.com/27nianyun/PCL3-CE/releases) 下载最新版本的 `PCL₃-CE.exe`
2. 放到任意目录（启动器为绿色便携版，不会写入系统目录）
3. 运行 `PCL₃-CE.exe`，选择 Java 版或基岩版后即可启动游戏

> Release 中提供的为自包含单文件版本，无需额外安装 .NET 运行时。若使用框架依赖方式自行构建，则需要预先安装 [.NET 10 Desktop Runtime](https://get.dot.net/10)。

## 从源码构建

需要 [.NET 10 SDK](https://get.dot.net/10)。SDK 基线由 [global.json](global.json) 固定。请在仓库根目录执行：

```powershell
# 自包含单文件发布（内置运行时，推荐用于分发）
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -r win-x64 --self-contained true -o ./publish

# 与 CI 一致的框架依赖发布
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true `
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained
```

将 `-p:Platform=x64` 换成 `-p:Platform=ARM64` 即可构建 ARM64 版本。

仅需验证编译是否通过时，可直接执行：

```powershell
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Release -p:Platform=x64
```

## 项目结构

| 目录 | 说明 |
|---|---|
| `Plain Craft Launcher 2/` | WPF 主程序：界面、页面与 Minecraft 启动逻辑 |
| `PCL.Core/` | 核心库：配置、网络与下载、Java、存档、动画、本地化、日志等 |
| `PCL.Core.SourceGenerators/` | 配置与依赖注入相关的源生成器 |
| `PCL.Core.Test/` | PCL.Core 的单元测试 |
| `.github/` | CI 构建与发布工作流 |

## 声明

- **项目性质**：PCL₃-CE 是基于 PCL Community Edition（PCL CE）分支衍生的独立版本，与 LTCatt（龙腾猫跃）维护的 PCL2 官方项目、PCL-Community 维护的 PCL CE 官方项目均无隶属关系。
- **支持范围**：PCL₃-CE 不支持「我的世界中国版」，也不提供与该版本相关的任何功能或适配。
- **API 使用**：PCL₃-CE 仅通过经 Mojang Studios 批准的 API 访问 Minecraft 国际版的相关服务。
- **代码来源**：本项目不包含任何 Minecraft（含「我的世界中国版」）的代码，也不包含 Mojang Studios 及其中国代理商的任何代码。
- **正版倡导**：我们鼓励通过官方渠道购买并游玩正版 Minecraft。

## 许可证与开源合规

本项目的许可证结构如下：

- `Plain Craft Launcher 2/` 使用 [PCL 自定义许可证](<Plain Craft Launcher 2/LICENCE>)（《PCL 分发有限许可》+《PCL 存储库合理使用指南》）
- 其余所有目录（`PCL.Core/`、`PCL.Core.SourceGenerators/` 等）使用 [Apache License 2.0](LICENSE)

### 第三方组件合规声明

本项目严格遵循各第三方组件的开源协议：

- **基岩版功能**：对照移植了 Round Studio 的 [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) 与 [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core)（均为 GPL-3.0）的部分逻辑，已在源码注释逐处标注出处，详见 [NOTICE](NOTICE)。
- **BLoader**：随启动器分发未经修改的 BLoader.dll（GPL-3.0），完整许可证随附于 `THIRD-PARTY/BLoader/LICENSE`。
- **LeviLamina / lip**：不随本项目分发，仅在用户选择该加载器时通过官方 lip 运行时安装。
- **NuGet 依赖**：全部第三方程序包的清单与许可证可在启动器「关于」页面查看（由 `metadata.json` 生成）。
- **内容密钥（CIK）**：本项目不附带、不分发任何 Xbox GDK 内容密钥。

### 参考的启动器项目

以下项目仅作为功能设计与实现思路的参考，当前源码中不包含它们的代码：

| 项目 | 用途 | 许可证（以 LICENSE 文件为准） |
|---|---|---|
| [PCL Symbio Edition](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition) | 本项目的基础 | PCL 自定义许可 + Apache-2.0 |
| [Portal Launcher](https://github.com/Portal-Launcher/PortalLauncher) | 联机 / 多人整合包同步 | AGPL-3.0（子模块 `src/Portal.Bedrock.Injector/` 为 Apache-2.0） |
| [Axolotl Launcher](https://github.com/Axolotl-Launcher/Axolotl) | 多人游戏 / 皮肤选择器 / 实验室 | GPL-3.0 |
| [SeaLantern](https://github.com/SeaLantern-Studio/SeaLantern) | 自建服务器管理 | AGPL-3.0 |
| [PCL2-Nex](https://github.com/PCL-Nex-Developer/PCL2-Nex) | 插件商店 / 插件扩展 | Apache-2.0 |

> ⚠️ **copyleft 提示**：Portal Launcher、Axolotl Launcher 与 SeaLantern 均属强 copyleft（AGPL-3.0 / GPL-3.0）。若日后直接复制或翻译其源代码，相应部分必须改以对应的 GPL / AGPL 许可证开源，不能继续沿用 Apache-2.0；AGPL 还要求通过网络提供服务时同样提供源码。本项目坚持「读思路 + 独立实现」，并在必要处于源码注释标注出处。

> 完整的第三方来源与许可说明见 [NOTICE](NOTICE)。

## 致谢

- [PCL](https://github.com/Meloong-Git/PCL) — 原作者 龙腾猫跃
- PCL CE — 社区维护分支，本项目的直接上游
- [PCL Symbio Edition](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition) — 本项目的基础（基岩版管理与资源下载参考）
- [Portal Launcher](https://github.com/Portal-Launcher/PortalLauncher) — 联机 / 多人整合包同步（AGPL-3.0）
- [Axolotl Launcher](https://github.com/Axolotl-Launcher/Axolotl) — 多人游戏 / 皮肤选择器 / 实验室（GPL-3.0）
- [SeaLantern](https://github.com/SeaLantern-Studio/SeaLantern) — 自建服务器管理（AGPL-3.0）
- [PCL2-Nex](https://github.com/PCL-Nex-Developer/PCL2-Nex) — 插件商店 / 插件扩展（Apache-2.0）
- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) 与 [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core) — Round Studio 开发的基岩版项目（GPL-3.0）
- [BLoader](https://github.com/Chlna6666/BLoader) — Chlna6666 开发的基岩版 Mod 加载器（GPL-3.0）
- [LeviLamina](https://github.com/LiteLDev/LeviLamina) 与 [lip](https://github.com/LiteLDev/lip) — LeviLamina 客户端（LGPL-3.0 / GPL-3.0）
- 以及 Dapper、Downloader、Markdig.Wpf、NAudio、Microsoft.Data.Sqlite、protobuf-net、Lucide 等开源项目，完整信息见 [metadata.json](<Plain Craft Launcher 2/metadata.json>) 与启动器「关于」页面
