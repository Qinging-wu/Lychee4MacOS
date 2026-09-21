# 更新日志

## v1.0.0

| 变更 | 说明 |
|---|---|
| 🍎 macOS 初版发布 | 悬浮球 + 信息面板从 Windows 版 [Lychee](https://github.com/Qinging-wu/Lychee) 移植到 .NET 8 + Avalonia UI 11 |
| 📊 六个模块 | 日期时间、网速、公网 IP / 归属地、CPU（mach `host_statistics`）、内存（`sysctl` + `host_statistics`）、网络延迟 |
| 🖱️ 悬浮球交互 | 悬停展开/收起、跨显示器拖拽、双击固定、贴边吸附、面板随屏幕位置翻转 |
| ⚙️ 设置窗口 | 模块开关、置顶面板、贴边吸附、提醒开关 |
| 🔔 提醒 | IP 变化、查询失败、网络恢复时弹出应用内 Toast + 系统通知（AppleScript） |
| 🍔 菜单栏图标 | 通过 Avalonia TrayIcon（NSStatusItem）提供显示/隐藏、设置、退出菜单 |
