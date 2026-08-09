# Fire Will

Fire Will 是仅面向 Windows 的《魔兽争霸 III / 羁绊 I》流程辅助工具。本分支以原
AutoHotkey v2 配置器为行为基线，重写为单进程 .NET 10 WPF 应用。

运行时不需要 AutoHotkey、Electron、WebView2、Python 或额外 .NET Runtime，也不读取
`Game.dll`，不访问游戏内存，不做进程注入。

## 当前功能

- 7 个固定刷本项、5 个 NPC、8 个流程，每个流程最多 8 组。
- 7 个刷本任务与 9 个释放方案完全分离；释放方案固定为 Q/W/E/R/D/F/B 技能和装备 1/2。
- 启动键、技能键和装备键都保存为平台映射引用，修改平台按键后流程自动跟随。
- 执行流程每组分别选择“刷本任务”和“技能释放”，可只执行其中一项或自由组合。
- 12 个技能栏按键和 6 个装备栏按键映射；界面按 War3 左侧 2 x 3 装备栏、右侧 4 x 3 命令卡排列，技能 1 从底行开始编号。
- 全局键盘、侧键和中键热键；仅在已绑定游戏窗口前台时触发。
- F6 记录当前 NPC 点，↓ 切换 NPC；F5/↑/F7/F8 不占用，可用于自定义流程。
- 新采集的 NPC 点保存 War3 客户区比例和采集时宽高比；窗口移动、等比例或不等比例缩放后，点击前按当前窗口实时换算。
- 单击停止，350ms 内再次按停止键恢复；退出前等待按键释放和钩子卸载。
- 自动查找 War3、手动绑定平台窗口、前台窗口诊断和单实例运行。
- 须佐斑、流年佐助、动态流转三种视频背景，以及透明度持久化。
- 现有 UTF-8 INI 和英雄配置导入；源文件与原配置始终只读。

## 目录

- `src/FireWill.Core`：配置模型、INI 兼容、流程编译和执行调度。
- `src/FireWill.App`：WPF 界面、Win32 输入、游戏窗口绑定和动态背景。
- `tests`：兼容性黄金测试与 Windows 输入生命周期测试。
- `assets/backgrounds`：发布时嵌入 EXE 的两段无声 H.264 视频。
- `tools/wallpaper`：仅开发期使用的背景转换脚本和 smoke 测试。
- `docs/compatibility-matrix.md`：本轮 1:1 功能范围与基准哈希。

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
$publishRoot = Join-Path $repoRoot "artifacts/publish"
$publishDir = Join-Path $publishRoot "win-x64"
if (Test-Path -LiteralPath $publishRoot) {
  Get-ChildItem -LiteralPath $publishRoot -Directory |
    Where-Object Name -ne "win-x64" |
    Remove-Item -Recurse -Force
}
if (Test-Path -LiteralPath $publishDir) {
  Remove-Item -LiteralPath $publishDir -Recurse -Force
}
dotnet publish src/FireWill.App/FireWill.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o $publishDir
```

发布目录只保留 `artifacts/publish/win-x64` 一个目录，每次发布直接覆盖它，
不创建带版本号或审计后缀的副本；最终只需分发其中的 `Fire Will.exe`。

## 本地数据

程序不会把用户配置写回安装目录：

- `%LOCALAPPDATA%\FireWill\war3_macro_gui.ini`
- `%LOCALAPPDATA%\FireWill\profiles\`
- `%LOCALAPPDATA%\FireWill\background.json`
- `%LOCALAPPDATA%\FireWill\BackgroundCache\`
- `%LOCALAPPDATA%\FireWill\logs\`

NPC 点使用窗口自适应坐标。录入后，移动窗口、任意改变窗口宽高或恢复原尺寸，都会按当前
Warcraft III 客户区实时换算，不需要再次录入。流程需要的 NPC 点位未记录完整时，程序会在
执行前列出缺少项并停止，避免误点。
