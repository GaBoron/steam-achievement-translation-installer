# Steam Achievement Translation Installer

Steam 成就翻译安装器是一个面向 Windows 10/11 x64 的 WinUI 3 桌面应用和命令行工具，用于扫描本地 Steam
数据，匹配
[Steam Achievement Translation Library](https://github.com/GaBoron/steam-achievement-translation-library)
中的成就翻译，安全安装对应 schema，并在需要时恢复安装前文件。

当前版本：**0.1.0（Alpha）**。本项目不负责编辑或创作翻译；需要编辑
`UserGameStatsSchema_*.bin` 时，可以使用独立的
[PanVena/SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer)。

## 安全原则

- Steam 正在运行时拒绝写入，不会强制结束 Steam 进程。
- 只接受 `index.json` version 1，并验证 App ID、版本 ID 和严格的仓库路径。
- 多版本条目直接读取 `schema_files`，不会通过 GitHub 目录树猜测版本。
- 下载文件必须同时通过声明大小和对应版本的 SHA-256 校验。
- 安装前创建快照，在目标目录中暂存后原子替换；失败时自动回滚。
- 每次重复安装都会保留独立历史。恢复时若目标已被其他程序修改，默认拒绝覆盖。
- `--force` 恢复会先归档当前目标，然后再恢复安装前快照。

SATL 仍是早期软件。首次使用前建议自行备份 Steam 的
`appcache\stats` 目录。

## 安装

### 便携版

从带标签构建产物中解压 `satl-win-x64.zip`，双击 `SATLInstaller.exe` 打开图形界面。
便携包同时保留完整命令行工具：

```powershell
satl.exe --version
```

当前构建未进行代码签名，Windows 可能显示 SmartScreen 提示。请同时核对发布的
SHA-256 文件。

### 图形界面

Windows 11 风格界面覆盖完整工作流：

- “游戏”页扫描本地 Steam 数据、搜索游戏、选择翻译版本并批量安装。
- “已管理”页查看 SATL 安装状态，执行普通恢复；目标被修改时可明确选择强制恢复并先归档当前文件。
- “设置”页可覆盖 Steam/数据目录、切换离线模式和主题，并刷新翻译目录缓存。

GUI 不会绕过 CLI 的安全检查。安装与恢复仍由 `satl.exe` 执行，Steam 正在运行时仍会拒绝写入。

### Python 源码

需要 Python 3.13：

```powershell
python -m pip install -e .
satl --version
```

运行测试或构建时安装开发依赖：

```powershell
python -m pip install -e ".[dev]"
```

## 使用

扫描所有本地账号缓存和已安装清单，并匹配可用翻译：

```powershell
satl scan
satl scan --json
satl scan --account 7656119xxxxxxxxxx
```

安装指定游戏的默认版本，或指定一个多版本条目：

```powershell
satl install 250900
satl install 250900 --variant 250900=with-unlock-conditions
satl install --matched
```

每次安装都会先打印计划并要求确认。自动化环境必须显式传入 `--yes`：

```powershell
satl install 250900 --yes
satl install --matched --yes
```

`status` 可以在没有 Steam 的情况下读取 SATL 的本地事务记录：

```powershell
satl status
satl status 250900 --json
```

恢复最近一次安装前的状态：

```powershell
satl restore 250900
satl restore --all
```

如果 Steam 或游戏已经改写目标文件，普通恢复会拒绝操作。确认需要恢复时，使用：

```powershell
satl restore 250900 --force
```

查看计划而不下载、创建目录或写入文件：

```powershell
satl install --matched --dry-run
satl restore --all --dry-run
```

刷新 catalog 或只使用经过验证的缓存：

```powershell
satl cache refresh
satl scan --offline
satl install 250900 --offline
```

Steam 自动检测失败时，各相关命令均支持 `--steam-dir`。测试或便携环境可用
`--data-dir` 覆盖默认数据目录。默认目录是：

```text
%LOCALAPPDATA%\SteamAchievementTranslationInstaller
```

其中包含：

- `cache/index.json`：最近一次验证通过的 catalog。
- `cache/schemas/<sha256>.bin`：按内容哈希保存的 schema。
- `backups/<app-id>/<transaction-id>/`：安装和强制恢复快照。
- `state.json`：version 1 的原子事务状态。

## JSON 输出

`scan --json` 和 `status --json` 返回记录数组。每条记录稳定包含：

```json
{
  "app_id": "250900",
  "game_name": "The Binding of Isaac: Rebirth 以撒的结合：重生",
  "discovery": ["account-cache", "installed"],
  "catalog_status": "current",
  "variants": [],
  "installed_state": "unmanaged",
  "action": "available",
  "error": null
}
```

桌面前端使用各命令的 `--jsonl` 模式。每行都是独立事件，包含
`protocol_version`、`operation`、`event` 和 `payload`；协议版本当前为 1。
人类可读输出以及原有 `scan/status --json` 输出保持兼容。

退出码：

| 退出码 | 含义 |
|---:|---|
| 0 | 成功 |
| 2 | 参数错误、缺少确认或用户取消 |
| 3 | Steam 正在运行、路径或本地数据预检查失败 |
| 4 | 网络、catalog 或缓存失败 |
| 5 | 大小、SHA-256 或备份完整性失败 |
| 6 | 文件系统或事务失败 |
| 7 | 批量操作部分失败 |

## 状态说明

- `unmanaged`：SATL 没有安装记录。
- `installed`：目标与最近一次 SATL 安装的 SHA-256 一致。
- `modified`：安装后目标被其他程序修改。
- `missing`：安装记录存在，但目标文件已不存在。
- `restored`：所有 SATL 安装记录均已恢复。
- `unreadable`：无法读取目标文件。

catalog 中不是 `current` 的条目会在扫描中显示，但安装时默认阻止。明确接受风险时
可使用 `--allow-outdated`。

## 开发与构建

```powershell
python -m compileall -q src tests scripts
python -m pytest -q
python scripts/offline_smoke.py
dotnet test tests/Satl.Gui.Tests/Satl.Gui.Tests.csproj -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

标签构建会生成 `satl-win-x64.zip` 和对应 SHA-256 文件，但不会自动创建 GitHub
Release。

## 范围

0.1.0 不包含自动更新、schema 编辑、翻译生成、Linux 或 macOS 支持。

许可证和第三方权利说明见 [LICENSE](LICENSE) 与
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
