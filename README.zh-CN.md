# Lychee for macOS

[English](./README.md) | 简体中文

Lychee 是一款轻量级 macOS 桌面系统监视器。悬浮球始终置顶，鼠标悬停后展开信息面板。应用使用 .NET 8 和 Avalonia UI 构建。

> **注意！** 本项目仍处于测试阶段，使用过程中可能遇到问题。欢迎提交 PR。

## 功能

- 日期时间，每秒刷新
- 网络上传和下载速度
- 公网 IP 与大致归属地，并可在 IP 变化、查询失败或恢复时提醒
- CPU 和内存使用情况
- 对多个公共地址进行网络延迟检测
- 内置悬浮球样式，也可导入 PNG、JPEG、BMP 或动态 GIF 图片
- 信息面板支持拖动边缘缩放
- 可设置面板背景透明度、背景和文字颜色、系统字体，并预览或恢复默认
- 可单独开关模块、固定面板和启用贴边吸附
- 通过菜单栏图标显示面板、打开设置或退出

## 使用

- 鼠标悬停在悬浮球上可展开面板，移开后自动收起。
- 拖动悬浮球可移动位置；面板会根据悬浮球所在方向翻转。
- 双击悬浮球或点击固定按钮，可保持面板展开。
- 展开面板后，拖动外侧边缘或角落即可调整尺寸。
- 点击面板或菜单栏中的“设置”，可调整模块、提醒、球体样式和面板外观。
- 可从面板或菜单栏退出应用。

## 环境要求与快速开始

- macOS 11 或更高版本，支持 Apple Silicon 和 Intel
- .NET SDK 8.0 或更高版本

```bash
git clone https://github.com/Qinging-wu/Lychee4MacOS.git
cd Lychee4MacOS
dotnet run
```

悬浮球会出现在屏幕右侧附近。Lychee 会在菜单栏添加图标，并隐藏 Dock 图标。当前尚未提供 `.app` 包或可下载的安装程序。

### 构建与发布

构建：

```bash
dotnet build -c Release
```

为 Apple Silicon 或 Intel 发布自包含可执行文件：

```bash
dotnet publish -c Release -r osx-arm64 --self-contained true
dotnet publish -c Release -r osx-x64 --self-contained true
```

发布后的可执行文件可在终端运行。这些命令不会生成 macOS `.app` 包。

## 平台差异与限制

- 未包含 Windows 版的帧率监控（FPS）、弹跳球和音频电平/频谱监控。
- 内存使用量根据 macOS 内存计数器估算，可能与“活动监视器”的数值不同；不会单独显示交换内存。
- 系统通知通过 AppleScript 发送；macOS 可能会询问是否允许“脚本编辑器”发送通知。
- 部分独占全屏应用可能会遮挡悬浮球。
- Retina 与多显示器定位仍需在更多 Mac 真机上验证。反馈定位问题时，请附上显示器排列方式。

## 设置与日志

设置和日志存放在：

```text
~/Library/Application Support/Lychee/
```

设置文件为 `settings.json`；滚动日志位于 `logs/lychee.log`。

## 特别鸣谢

特别感谢 [Icyjerry](https://github.com/Icyjerry) 提交的 [PR #1](https://github.com/Qinging-wu/Lychee4MacOS/pull/1)，修复并改进了悬浮球拖动、面板位置和菜单栏图标。

## 参与贡献

欢迎提交问题反馈和 Pull Request。请先阅读[贡献指南](./CONTRIBUTING.md)、[行为准则](./CODE_OF_CONDUCT.md)和[安全策略](./SECURITY.md)。

## 许可证

MIT，详见 [LICENSE](./LICENSE)。
