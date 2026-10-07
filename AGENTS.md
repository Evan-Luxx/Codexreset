# CodexReset

Windows 桌面额度与重置监控工具，使用 PowerShell 和 C# WinForms。个人额度来自本机已登录的 Codex App Server，全局重置数据来自 `codex-reset.com`；保留手动倒计时。

## 项目结构

- `启动.cmd`：通过 Windows PowerShell 以 STA 模式后台启动。
- `Launcher.cs` / `Build-Exe.ps1`：单文件 EXE 启动器、几何图标生成及显式源码资源打包。产物在 releases；禁止加入用户设置、缓存或额度。构建使用系统 .NET Framework C# 编译器，运行仍依赖 Windows PowerShell。
- `CodexReset.ps1`：程序入口；负责请求、缓存、设置、托盘菜单、状态更新和测试。
- `ResetPopup.cs`：自绘详情面板、动画、鼠标交互及屏幕定位。
- `NewsPopup.cs`：消息页、460ms 透视卡片翻转、固定黄框滚动和无焦点滚轮处理；与 ResetPopup.cs 为同一 partial 类。
- `CodexQuotaClient.cs`：异步 App Server stdio 通信、查询超时与子进程清理，不启动模型任务。
- `Quota.ps1`：定位 Codex 可执行文件、解析额度窗口和格式化。
- `QuotaUi.ps1`：额度刷新和个人额度界面状态。
- 个人额度显示在系统托盘及详情面板右侧的双周期分段刻度；桌面小浮窗保留全局监控、手动倒计时。详情面板上方为 5 小时余量，下方为 7 天余量，不随托盘周期切换。
- `settings.json`：用户设置，包括位置、额度周期和个人时间；旧 reducedMotion 字段不再读取。
- `cache.json`：最近一次接口响应，供离线展示。

当前目录有 README，没有项目文件、依赖清单或独立构建配置。启动时由 `Add-Type` 编译并加载 C#，引用系统的 Windows Forms、Drawing 和 Web.Extensions；没有已确认的独立打包流程。

## 开发约定

- 发布隐私唯一例外是作者卡中的 Evan-Luxx 署名及用户指定的 B站、GitHub 主页；其他任何用户或开发者隐私不得发布。每次按 agent.md 检查最终发布内容，EXE 构建必须核验实际嵌入资源。

- 源码及相关资料保留在本项目目录，遵循上级 `../AGENTS.md`。
- 保留用户已有修改；不要覆盖或提交无关的 `settings.json`、`cache.json` 运行数据变更。
- 延续现有分工：业务和数据逻辑放在 PowerShell，详情面板绘制与交互放在 C#。
- 保持中文界面及现有时间语义：全局记录显示北京时间，个人时间按本机时间设置，内部使用 `DateTimeOffset`。
- 保留单实例、请求失败保留缓存和限流重试行为；不要把历史预测或手动时间表述为个人账户已确认的额度恢复时间。
- 额度按返回的窗口时长识别（300 / 10080 分钟），不可固定假设 primary / secondary 的顺序；未知额度不能显示为 0%。不保存登录令牌或个人额度到磁盘，超时可保留内存中的旧数据并标注，其他查询错误清除个人显示。
- 修改数据解析或倒计时后运行自测；修改界面后在 Windows 上核验交互和显示。其他编码规范尚未明确，遵循附近代码风格。

## 启动与测试

在项目根目录的 PowerShell 中执行：

```powershell
# 常规启动（隐藏控制台）
.\启动.cmd

# 前台启动，便于查看错误
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\CodexReset.ps1

# 无 GUI、无联网自测；已实际运行通过
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\CodexReset.ps1 -SelfTest
```

自测覆盖倒计时、跨日、到期、UTC 解析、可选字段及异常数据拒绝。

新增命令（已在 Windows PowerShell 验证通过）：

```powershell
# 真实账户只读查询，需要安装并登录 Codex
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\CodexReset.ps1 -QuotaTest

# 离线 GUI 测试，使用示例额度与独立 test-output 设置
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\CodexReset.ps1 -UiTest
Get-Content .\test-output\额度界面测试.txt
```

自测另覆盖额度窗口识别、缺失窗口、耗尽和异常百分比；GUI 测试生成面板、浮窗与 16/32px 图标截图。个人额度每两分钟查询一次，设置通过右键“显示额度”选择 5 小时或每周。

代码还提供联网和 GUI 冒烟测试，约 24 秒后写出结果及截图：

```powershell
# 先退出正在运行的程序，否则单实例锁会使测试直接退出。
New-Item -ItemType Directory -Force .\test-output | Out-Null
$env:CODEX_RESET_SMOKE = '1'
$env:CODEX_RESET_TEST_OUT = Join-Path (Get-Location).Path 'test-output'
try {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\CodexReset.ps1
    Get-Content .\test-output\新版测试结果.txt
} finally {
    Remove-Item Env:CODEX_RESET_SMOKE, Env:CODEX_RESET_TEST_OUT -ErrorAction SilentlyContinue
}
```

该冒烟测试需要 Windows 桌面会话和网络。应检查结果文件中的 `PASS` / `FAIL`，不能仅依赖进程退出码。GUI 与冒烟测试均使用独立测试实例锁，并将设置和缓存写入 test-output，不改动根目录的用户数据。PowerShell 7 兼容性、CI 和打包命令尚未确认。

- 动画始终开启，不提供关闭选项；TestInstant 仅供隔离测试使用。
- LoadRuntime.ps1 按源码 SHA-256 将编译结果缓存到 .runtime，源码变化自动重新编译。

## 每次上传必须遵守的隐私规则

提交、推送、发布、打包和分享文件或截图前，必须读取并严格遵守 [agent.md](agent.md)。禁止上传任何用户的凭据、身份、个人额度、设置、缓存、日志或测试截图。仅上传审阅通过的源码白名单；对实际候选文件执行 Assert-UploadPrivacy.ps1 并人工复核，失败时禁止上传。Git 历史和最终归档也必须检查，不能仅依赖 .gitignore。本规则不授予上传权限。
