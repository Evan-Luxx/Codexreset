# Codex Reset 1.0.0

Windows 桌面 Codex 额度与重置监控工具。托盘图标显示剩余额度，小面板同时显示 5 小时和 7 天余量，支持中文重置消息、卡片翻转与长文滚动。

## 数据来源

- 个人额度：通过本机已登录的 Codex App Server 查询，每两分钟刷新。需要安装并登录 Codex；未知额度不显示为零。
- 全局重置、历史预测与中文消息：https://codex-reset.com/zh/ 。每分钟查询，失败保留缓存。网站预测不代表个人额度恢复承诺。
- 个人倒计时：用户手动设置。
感谢以上网站作者与维护者，数据权利归属与相应权利人。
## 使用

双击 CodexReset.exe 后后台运行，右键菜单可以设置额度周期、个人时间或退出。作者卡可勾选“不再提醒”。依赖 Windows PowerShell 与 .NET Framework；EXE 未签名。

EXE 的设置和公开网页缓存保存在本机 LocalAppData/CodexReset。个人额度只在内存中使用，不保存登录令牌，不发送个人额度给第三方网站。发布包不含用户设置、缓存、登录信息或测试截图；个人信息唯一例外是用户授权的作者卡署名和主页链接。

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
