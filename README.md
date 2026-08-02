# Fire Will

Warcraft III / 羁绊 I visual macro configurator.

## 开发目录

这个目录用于后续开发和版本管理，当前包含：

- `war3_macro_gui.ahk`：主 GUI 宏配置器源码
- `war3_npc_macro.ahk`：较早的 NPC 宏模板
- `profiles/`：英雄/流程配置档案
- `info/`：识别和操作参考图片
- `recognition_probe.py`：识别测试辅助脚本
- `docs/`：安装使用说明与执行逻辑说明

## 本地运行

安装 AutoHotkey v2 后，可以直接运行：

```powershell
.\war3_macro_gui.ahk
```

也可以双击 `启动源码宏配置器.bat`。

## 新版 UI 框架

第一版新版 UI 已切到 WebView2 桌面壳：

```powershell
dotnet run --project .\app\FireWill.App\FireWill.App.csproj
```

新版 UI 的定位是流程编排器：

- `ui/index.html`：全屏视频背景与三栏流程驾驶舱
- `ui/styles/app.css`：响应式布局、半透明面板、HUD 风格视觉
- `ui/scripts/app.js`：读取/渲染配置、保存用户快捷键与技能 CD 秒数
- `app/FireWill.App`：WinForms + WebView2 宿主，读取现有 INI 配置

当前底层执行逻辑仍由旧 `war3_macro_gui.ahk` 承担；新版 UI 先负责配置展示、用户快捷键输入、技能 CD 计时器和打开旧版配置器。

## 远程仓库

目标远程仓库：

```text
git@github.com:LiuAnclouds/Fire_Will.git
```

