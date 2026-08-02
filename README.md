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

## 新版本地客户端

当前主框架为 Electron Portable，目标是打包成一个可直接双击运行的 Windows EXE。播放器、HTML/CSS UI 和本地配置桥接都包含在客户端中，不需要浏览器、WebView2、Qt 或 Python。首次启动时会把可写配置释放到当前用户的应用数据目录，后续修改不会因关闭客户端而丢失。

```powershell
cd .\electron
npm install
npm start
```

生成便携 EXE：

```powershell
npm run dist
```

主要目录：

- `electron/main.js`：本地主进程、INI 读写和 AHK 执行器启动
- `electron/preload.js`：隔离的前端/本地能力桥接
- `electron/backend/`：内置 AHK 执行器、配置和英雄档案
- `ui/`：全屏视频背景与按旧版 AHK `BuildGui()` 顺序迁移的配置面板
- `legacy-ahk/`：旧 AHK 源码归档

## 远程仓库

目标远程仓库：

```text
git@github.com:LiuAnclouds/Fire_Will.git
```

