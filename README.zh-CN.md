# <img src="Assets/icon.ico" alt="Lychee for macOS" width="56" height="56" style="vertical-align: middle; margin-top: 8px"> Lychee for macOS

[English](./README.md) | 简体中文

一个极小的 macOS 桌面悬浮球，置顶显示日期时间、网速、公网 IP、CPU、内存和延迟。鼠标悬停展开信息面板，移开自动收起。基于 .NET 8 + Avalonia UI 构建。无需安装、无后台服务。

## 🔗 与 Lychee（Windows 版）的关系

Lychee for macOS 是 [Lychee](https://github.com/Qinging-wu/Lychee)（Windows 版悬浮球）的 macOS 姊妹仓库：

- **同一产品，交互同源** — 悬浮球、信息面板、设置和模块开关的行为均从 Windows 版移植而来。
- **架构同构，代码独立** — `Core/`（模块接口、管理器、设置）与 Windows 仓库保持相同结构；UI 层因 WPF/WinForms 无法在 macOS 运行而改用 Avalonia 重写。
- **版本各自独立** — 本仓库拥有独立的发布线和更新日志，从 v1.0.0 起步，版本号不与 Windows 版对齐。
- **本仓库没有帧性能（FPS）** — Windows 版的帧监控（DWM 桌面输出 / PresentMon 前台应用采集）依赖 Windows 专属 API，不向本仓库移植。其余核心模块均可用。

## ✨ 功能

- **📅 日期时间** — 每秒刷新
- **🌐 网络速度** — 自动选择活动网卡，每秒采样上传/下载速率
- **📍 公网 IP / 归属地** — 每 20 秒查询 ip-api.com，IP 变化时弹出提醒 + 系统通知（可选）
- **💻 CPU 使用率** — 通过 Mach `host_statistics(HOST_CPU_LOAD_INFO)` 计数器采集全系统利用率，每秒刷新
- **🧠 内存** — `sysctl hw.memsize` 获取总量，`host_statistics(HOST_VM_INFO)` 页计数器计算占用（free + inactive 页）
- **⏱️ 网络延迟** — 每 5 秒 ping 223.5.5.5 / 1.1.1.1 / 8.8.8.8，取最优值

每个模块都可以在设置中单独开关。

### 与 Windows 版的差异

| | Windows（[Lychee](https://github.com/Qinging-wu/Lychee)） | macOS（本仓库） |
|---|---|---|
| UI 框架 | WPF + WinForms | Avalonia UI 11 |
| 帧性能（FPS） | ✅ DWM / PresentMon | ❌ 未移植（依赖 Windows 专属 API） |
| 弹跳球 | ✅ | ❌ 未移植 |
| IP 变化提醒 | 应用内 Toast + 托盘气泡 | 应用内 Toast + 系统通知（AppleScript） |
| 内存副行 | 空闲 + 页面文件 | 仅空闲（macOS 无页面文件） |

## 🖱️ 使用

- **👆 悬停**悬浮球展开面板，移开自动收起
- **✋ 拖拽**悬浮球到任意显示器任意位置，面板根据屏幕位置自动翻转方向
- **👆👆 双击**悬浮球固定/取消固定面板
- **📌 固定** — 面板保持展开，不再跟随鼠标自动收起
- **⚙️ 设置** — 模块开关、置顶面板、贴边吸附、提醒开关
- **◀️ 收起** — 强制收起面板
- **❌ 退出** — 退出应用
- **菜单栏图标** — 右键弹出菜单（显示/隐藏、设置、退出）

公网 IP 发生变化时（可能是 VPN 掉线或网络切换），右下角弹出红色提醒，系统通知显示新旧 IP。

## 🚀 快速开始

从源码构建（打包版本和 .app bundle 后续推出）：

```bash
git clone https://github.com/Qinging-wu/Lychee4MacOS.git
cd Lychee4MacOS
dotnet run
```

系统要求：**macOS 11+**（Apple Silicon 或 Intel），**.NET SDK 8.0+**。

悬浮球出现在屏幕右侧中部，菜单栏会添加一个 Lychee 图标，Dock 图标自动隐藏。

关闭方式：点击面板上的 **✕**，或菜单栏图标 → Quit Lychee。

## 🔧 构建

```bash
dotnet build -c Release
```

发布自包含的可执行文件（暂无 .app bundle）：

```bash
dotnet publish -c Release -r osx-arm64   # Apple Silicon
dotnet publish -c Release -r osx-x64     # Intel
```

产物路径：`bin/Release/net8.0/osx-arm64/publish/Lychee` — 在终端直接运行。

## 🏗️ 架构

```
Lychee4MacOS/
├── App.axaml(.cs)               # 应用入口；macOS 上隐藏 Dock 图标
├── Program.cs
├── MainWindow.axaml(.cs)        # 悬浮球 + 信息面板 + 交互逻辑
├── Core/                        # 核心抽象（与 Windows 版同构）
│   ├── IInfoModule.cs           # 模块接口 + InfoModuleBase
│   ├── ModuleManager.cs         # 模块注册 / 生命周期
│   ├── SettingsService.cs       # JSON 设置持久化
│   ├── TrayIconService.cs       # 菜单栏 / 托盘图标
│   ├── AppLog.cs                # 滚动文件日志
│   └── IpChangedEventArgs.cs / QueryFailedEventArgs.cs / QueryRecoveredEventArgs.cs
├── Modules/                     # 内置模块
│   ├── DateTimeModule.cs
│   ├── NetworkSpeedModule.cs
│   ├── PublicIpModule.cs
│   ├── CpuModule.cs             # mach host_statistics(HOST_CPU_LOAD_INFO)
│   ├── MemoryModule.cs          # sysctl hw.memsize + host_statistics(HOST_VM_INFO)
│   └── LatencyModule.cs
├── Platform/                    # 分平台数据源（macOS 优先，Windows 回退）
│   ├── SystemCpuTimes.cs        # mach host_statistics / GetSystemTimes
│   ├── SystemMemory.cs          # sysctl + host_statistics / GlobalMemoryStatusEx
│   ├── GlobalCursor.cs          # CGEventGetLocation / GetCursorPos
│   ├── MacAppActivation.cs      # NSApplicationActivationPolicy（隐藏 Dock 图标）
│   └── MacNotifier.cs           # AppleScript display notification
└── Views/
    └── SettingsWindow.axaml(.cs)
```

### 🔌 模块接口

```csharp
public interface IInfoModule : IDisposable
{
    string Id { get; }              // 唯一标识
    string DisplayName { get; }     // 列表标签
    string Icon { get; }            // emoji 字形
    bool IsEnabled { get; set; }    // 用户开关
    string CurrentValue { get; }    // 当前值（数据绑定）
    string? Detail { get; }         // 可选的副行
    event EventHandler? ValueChanged;
    void Start();
    void Stop();
}
```

### 📦 新增模块

1. 继承 `InfoModuleBase`，实现 `Start()` / `Stop()`，数据更新时给 `CurrentValue` 赋值即可（与 Windows 版写法一致）。
2. 在 `MainWindow.axaml.cs` 中注册：

```csharp
_moduleManager.RegisterModule(new WeatherModule());
```

3. 重新编译。模块会自动出现在信息面板和设置开关列表中，无需改动 UI 代码。

## ⚙️ 配置文件

`~/Library/Application Support/Lychee/settings.json`

与 Windows 版 schema 一致——把 Windows 的 `settings.json` 拷过来，模块开关可以直接沿用。

```json
{
  "AlwaysShowPanel": false,
  "AlertOnIpChange": true,
  "AlertOnQueryFailure": true,
  "SnapToEdge": false,
  "ModuleEnabled": {
    "datetime": true,
    "network-speed": true,
    "public-ip": true,
    "cpu": true,
    "memory": true,
    "latency": true
  }
}
```

日志：`~/Library/Application Support/Lychee/logs/lychee.log`

## ⚠️ 已知限制

- 帧性能（FPS）不可用——DWM 与 PresentMon 是 Windows 专属
- 内存为估算值：已用 = 总量 − (free + inactive 页)。与活动监视器变化方向一致，但数值口径不同
- 系统通知通过 AppleScript（`display notification`）发送；首次使用时 macOS 可能要求允许"脚本编辑器"发送通知
- 部分独占全屏应用可能仍会盖住悬浮球
- Retina 与多显示器坐标处理已实现，但尚未在真机上充分验证——如果悬浮球、吸附或提醒位置异常，请附上"系统设置 → 显示器"的排布截图反馈
- 暂无 .app bundle：请通过 `dotnet run` 或终端运行发布产物

## 🤝 社区与贡献

- [贡献指南](./CONTRIBUTING.md)
- [行为准则](./CODE_OF_CONDUCT.md)
- [安全策略](./SECURITY.md)

## 📄 许可证

Lychee for macOS 使用 [MIT 许可证](./LICENSE) 发布。
