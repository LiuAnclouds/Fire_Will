# Fire Will

Fire Will 是仅面向 Windows 的《魔兽争霸 III / 羁绊 I》流程辅助工具。本分支以原
AutoHotkey v2 配置器为行为基线，重写为单进程 .NET 10 WPF 应用。

运行时不需要 AutoHotkey、Electron、WebView2、Python 或额外 .NET Runtime，也不读取
`Game.dll`，不访问游戏内存，不做进程注入。

## 当前功能

- 7 个固定刷本项、5 个 NPC、8 个流程，每个流程最多 8 组。
- 12 个技能栏按键和 6 个装备栏按键映射。
- 全局键盘、侧键和中键热键；仅在已绑定游戏窗口前台时触发。
- F5/F6 采集与切换刷本目标，F7/F8 采集与切换 NPC。
- 新采集的 NPC 与技能落点会保存 War3 客户区比例和采集时宽高比；窗口移动、等比例或不等比例缩放后，点击前按当前窗口实时换算。
- 单击停止，350ms 内再次按停止键恢复；退出前等待按键释放和钩子卸载。
- 自动查找 War3、手动绑定平台窗口、前台窗口诊断和单实例运行。
- 须佐斑、流年佐助、动态流转三种视频背景，以及透明度持久化。
- 旧版 UTF-8 INI 和英雄配置迁移；旧源文件与原配置始终只读。

## 目录

- `src/FireWill.Core`：配置模型、旧 INI 兼容、流程编译和执行调度。
- `src/FireWill.App`：WPF 界面、Win32 输入、游戏窗口绑定和动态背景。
- `tests`：兼容性黄金测试与 Windows 输入生命周期测试。
- `assets/backgrounds`：发布时嵌入 EXE 的两段无声 H.264 视频。
- `tools/wallpaper`：仅开发期使用的背景转换脚本和 smoke 测试。
- `docs/compatibility-matrix.md`：本轮 1:1 兼容范围与旧版哈希。

## 构建与测试

需要 Windows x64 和 .NET SDK `10.0.302`，SDK 版本由 `global.json` 固定。

```powershell
dotnet restore FireWill.slnx
dotnet build FireWill.slnx -c Release --no-restore -warnaserror
dotnet test FireWill.slnx -c Release --no-restore
dotnet run --project tools/wallpaper/smoke-tests/BackgroundSmokeTests.csproj -c Release
```

生成低于 500MB、请求管理员权限的自包含单文件 EXE。先清理已确认位于仓库内的发布目录，避免上个版本的 PDB 或其他残留文件混入：

```powershell
$repoRoot = (Resolve-Path .).Path
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "FireWill.slnx"))) {
  throw "请先切换到 Fire Will 仓库根目录。"
}
$publishDir = Join-Path $repoRoot "artifacts/publish/win-x64"
if (Test-Path -LiteralPath $publishDir) {
  Remove-Item -LiteralPath $publishDir -Recurse -Force
}
dotnet publish src/FireWill.App/FireWill.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o $publishDir
```

发布目录最终只需分发 `Fire Will.exe`。

## 本地数据

程序不会把用户配置写回安装目录：

- `%LOCALAPPDATA%\FireWill\war3_macro_gui.ini`
- `%LOCALAPPDATA%\FireWill\profiles\`
- `%LOCALAPPDATA%\FireWill\background.json`
- `%LOCALAPPDATA%\FireWill\BackgroundCache\`
- `%LOCALAPPDATA%\FireWill\logs\`

纯旧配置中的绝对坐标仍可直接使用，但不会随窗口缩放。开始采集自适应点位后，同一流程用到的
NPC 点和技能目标点必须全部重新采集，程序会在执行前列出遗漏点位并停止，避免新旧坐标混用而误点。
`0.1.3` 保存的比例坐标没有记录采集时宽高比，也需要在 `0.1.4` 中重新采集一次；以后移动窗口、
任意改变窗口宽高或恢复原尺寸，都由同一套实时换算处理，不需要再次录入。
