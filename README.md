# Codex Reset 1.1.1

Windows 桌面 Codex 额度与重置监控工具。托盘图标显示剩余额度，小面板同时显示 5 小时和 7 天余量，支持中文重置消息、卡片翻转与长文滚动。

## 数据来源

- 个人额度：通过本机已登录的 Codex App Server 查询，每两分钟刷新。需要安装并登录 Codex；未知额度不显示为零。
- 全局重置与中文消息：https://aihot.news/codex-reset 。区分额度重置和重置卡发放，显示来源状态、预计时间、适用范围与上次重置日期。AIHOT 为非官方整理，预计时间不代表到账，发卡不代表额度恢复。
- 全局数据默认每 2 小时更新，右键“自动更新数据”打开单行数字滚轮，显示如 2h；鼠标滚轮或按住上下拖动可在 1～24 小时之间循环选择，带滚动动画并自动保存；启动时立即更新，失败保留缓存并重试，限流时遵循服务器等待时间。“刷新数据”支持手动查询。
- 个人倒计时：用户手动设置。

感谢以上网站作者与维护者，数据权利归属与相应权利人。

## 1.1.1 更新说明

- 新增右键“开机自启”开关，沿用原有菜单配色与样式，默认关闭。
- 支持源码版与 EXE 版，在当前用户登录 Windows 后后台启动，无需管理员权限。
- 勾选状态读取实际启动快捷方式，关闭时移除快捷方式；移动程序后需重新关闭并开启。
- 补充隔离测试，验证自启开启、关闭及启动命令，不改动真实用户的自启设置。

## 使用

右键“开机自启”可开启或关闭当前用户登录后的自动启动，默认关闭，无需管理员权限，沿用现有菜单风格。开启后请保留 EXE 或源码所在位置，移动后重新关闭并开启。

双击 CodexReset.exe 后后台运行，右键菜单可以设置额度周期、个人时间或退出。作者卡可勾选“不再提醒”。依赖 Windows PowerShell 与 .NET Framework；EXE 未签名。

EXE 的设置和公开网页缓存保存在本机 LocalAppData/CodexReset。个人额度只在内存中使用，不保存登录令牌，不发送个人额度给第三方网站。发布包不含用户设置、缓存、登录信息或测试截图；个人信息唯一例外是用户授权的作者卡署名和主页链接。

## 下载

[下载 1.1.1](https://github.com/Evan-Luxx/Codexreset/releases/tag/v1.1.1)

## 源码启动、测试与构建

在源码目录执行：

```powershell
.\启动.cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\CodexReset.ps1 -SelfTest
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\CodexReset.ps1 -UiTest
Get-Content .\test-output\额度界面测试.txt
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Exe.ps1
```

构建生成 releases/CodexReset.exe 和配套图标，并检查源码及实际内嵌资源。发布前必须遵守 agent.md；测试输出和运行数据不得发布。
