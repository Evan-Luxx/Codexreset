param([switch]$SelfTest, [switch]$QuotaTest, [switch]$UiTest)
$ErrorActionPreference = 'Stop'
$launchClock=[Diagnostics.Stopwatch]::StartNew()
$SmokeTest=$env:CODEX_RESET_SMOKE -eq '1'
. (Join-Path $PSScriptRoot 'Quota.ps1')
. (Join-Path $PSScriptRoot 'Source.ps1')

function Set-Autostart([bool]$enabled,[string]$path) {
    if(-not $enabled){
        if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}
        return
    }
    $shell=New-Object -ComObject WScript.Shell
    $shortcut=$null
    try {
        $shortcut=$shell.CreateShortcut($path)
        if($env:CODEX_RESET_LAUNCHER -and (Test-Path -LiteralPath $env:CODEX_RESET_LAUNCHER -PathType Leaf)){
            $shortcut.TargetPath=$env:CODEX_RESET_LAUNCHER
            $shortcut.Arguments=''
            $shortcut.WorkingDirectory=Split-Path -Parent $env:CODEX_RESET_LAUNCHER
        } else {
            $shortcut.TargetPath=Join-Path $PSHOME 'powershell.exe'
            $shortcut.Arguments='-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'CodexReset.ps1')+'"'
            $shortcut.WorkingDirectory=$PSScriptRoot
        }
        $shortcut.WindowStyle=7
        $shortcut.Description='Codex Reset 开机自启'
        $shortcut.Save()
    } finally {
        if($null -ne $shortcut){$null=[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}
        $null=[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

function Read-ResetData($json) {
    $d = $json | ConvertFrom-Json
    if($d.Source -eq 'aihot'){$d.Last=[DateTimeOffset]::Parse([string]$d.Last);return $d}
    if (-not $d.last_reset_at) { throw '数据缺少最近重置时间' }
    $last = [DateTimeOffset]::Parse([string]$d.last_reset_at)
    if ($last -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) { throw '最近重置时间异常' }
    $alert=$null
    if($d.latest_alert -and $d.latest_alert.summary){
        $link='';$uri=$null
        if([Uri]::TryCreate([string]$d.latest_alert.url,[UriKind]::Absolute,[ref]$uri) -and $uri.Scheme -eq 'https' -and $uri.Host -in @('x.com','twitter.com','codex-reset.com','www.codex-reset.com')){$link=$uri.AbsoluteUri}
        $meta='来源消息';$sourceTime=[DateTimeOffset]::MinValue
        if([DateTimeOffset]::TryParse([string]$d.latest_alert.source_at,[ref]$sourceTime)){$meta=$sourceTime.ToOffset([TimeSpan]::FromHours(8)).ToString('MM-dd HH:mm')+' · 北京时间'}
        $alert=[pscustomobject]@{Key=[string]$d.latest_alert.id;Text=[string]$d.latest_alert.summary;Meta=$meta;Link=$link}
    }
    [pscustomobject]@{ Last=$last; Updated=[string]$d.updated_at; Signal=$d.official_signal; P24=$d.probabilities.rounded_24h; P48=$d.probabilities.rounded_48h; Alert=$alert }
}
function Format-Remaining([DateTimeOffset]$target, [DateTimeOffset]$now) {
    $seconds = [Math]::Ceiling(($target-$now).TotalSeconds)
    if ($seconds -le 0) { return '时间已到 · 请核实额度' }
    $span = [TimeSpan]::FromSeconds($seconds)
    if ($span.TotalDays -ge 1) { return ('{0}天 {1:00}:{2:00}:{3:00}' -f [Math]::Floor($span.TotalDays),$span.Hours,$span.Minutes,$span.Seconds) }
    return ('{0:00}:{1:00}:{2:00}' -f $span.Hours,$span.Minutes,$span.Seconds)
}
function Read-ChineseNews([string]$html) {
    $options=[Text.RegularExpressions.RegexOptions]::Singleline
    $banner=[regex]::Match($html,'<span\b[^>]*id="signal-banner"[^>]*>(.*?)</span>\s*</div>',$options).Value
    $source=[regex]::Match($banner,'class="signal-evidence-source[^" ]*(?: [^"]*)?"\s+href="([^"]+)"').Groups[1].Value
    if(-not $source){throw '中文页面缺少重置消息来源'}
    $link=[Net.WebUtility]::HtmlDecode($source);$uri=$null
    if(-not [Uri]::TryCreate($link,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.Host -notin @('x.com','twitter.com','codex-reset.com')){throw '中文消息链接无效'}
    $text=''
    foreach($item in [regex]::Matches($html,'<li\b[^>]*>(.*?)</li>',$options)){
        if($item.Value.Contains('href="'+$source+'"')){
            $body=[regex]::Match($item.Value,'<(?:p|span)\b[^>]*class="(?:feed-text|event-summary)"[^>]*>(.*?)</(?:p|span)>',$options)
            if($body.Success){$text=[Net.WebUtility]::HtmlDecode([regex]::Replace($body.Groups[1].Value,'<[^>]+>','')).Trim();if($text -match '[\u4e00-\u9fff]'){break};$text=''}
        }
    }
    if(-not $text){throw '尚未取得对应消息的中文'}
    $verdict=[regex]::Match($html,'<strong\b[^>]*id="forecast-verdict"[^>]*>(.*?)</strong>',$options).Groups[1].Value
    [pscustomobject]@{Key=$link;Text=$text;Meta='中文网页 · 与来源同步';Link=$link;HasSignal=($verdict -and $verdict -notmatch '暂无')}
}
if ($SelfTest) {
    foreach($value in @($null,0,25,'invalid')){if((Get-RefreshHours $value) -ne 2){throw '默认更新间隔失败'}}
    foreach($value in 1..24){if((Get-RefreshHours $value) -ne $value){throw '更新间隔范围失败'}}
    $fixture='[{"_1":2},"loaderData",{"_3":4},"codex-reset",{"_5":6,"_7":8,"_11":-5},"schemaVersion",1,"stats",{"_9":10},"lastResetDate","2026-01-01","current"]'
    $fixtureHtml='streamController.enqueue('+($fixture|ConvertTo-Json -Compress)+')'
    $sourceTest=Read-AihotData $fixtureHtml
    if($sourceTest.Last.ToString('yyyy-MM-dd') -ne '2026-01-01' -or $sourceTest.Signal){throw 'AIHOT 空消息或日期解析失败'}
    foreach($bad in @('invalid',$fixtureHtml.Replace('schemaVersion','unknown'),$fixtureHtml.Replace('2026-01-01','2099-01-01'))){
        $rejected=$false;try{$null=Read-AihotData $bad}catch{$rejected=$true};if(-not $rejected){throw 'AIHOT 异常数据未拒绝'}
    }
    $html='<strong id="forecast-verdict">暂无官方重置信号</strong><div><span id="signal-banner"><span>English</span><a class="signal-evidence-source icon-link" href="https://x.com/example/status/123">原文</a></span></div><li><a href="https://x.com/example/status/456">其他</a><p class="feed-text">其他消息</p></li><li><a href="https://x.com/example/status/123">来源</a><p class="feed-text">虚构中文 &amp; 测试消息</p></li>'
    $news=Read-ChineseNews $html
    if($news.Text -ne '虚构中文 & 测试消息' -or $news.HasSignal){throw '中文来源匹配失败'}
    $rejected=$false;try{$null=Read-ChineseNews ($html.Replace('虚构中文 &amp; 测试消息','English only'))}catch{$rejected=$true};if(-not $rejected){throw '缺失中文未拒绝'}
    $now=[DateTimeOffset]::UtcNow
    if ((Format-Remaining $now.AddSeconds(3661) $now) -ne '01:01:01') { throw '小时倒计时失败' }
    if ((Format-Remaining $now.AddDays(2).AddSeconds(1) $now) -ne '2天 00:00:01') { throw '跨日倒计时失败' }
    if ((Format-Remaining $now.AddSeconds(-1) $now) -ne '时间已到 · 请核实额度') { throw '到期状态失败' }
    $sample='{"last_reset_at":"2026-10-07T03:35:09Z","probabilities":{"rounded_24h":15,"rounded_48h":28},"official_signal":null}'
    $r=Read-ResetData $sample
    if ($r.Last.ToUniversalTime().Hour -ne 3 -or $r.P24 -ne 15) { throw '接口解析失败' }
    $r=Read-ResetData '{"last_reset_at":"2026-10-07T03:35:09Z","latest_alert":{"id":"sample","summary":"测试消息","url":"file:///C:/example","source_at":"invalid"}}'
    if($r.Alert.Text -ne '测试消息' -or $r.Alert.Link){throw '来源消息解析或链接限制失败'}
    foreach ($bad in @('{}','not-json','{"last_reset_at":"invalid"}','{"last_reset_at":"2099-01-01T00:00:00Z"}')) {
        $rejected=$false; try { $null=Read-ResetData $bad } catch { $rejected=$true }
        if (-not $rejected) { throw '异常数据未拒绝' }
    }
    $quota=Read-QuotaData '{"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":5,"windowDurationMins":10080},"secondary":{"usedPercent":32,"windowDurationMins":300,"resetsAt":1791363810}}}}}'
    if($quota.Five.Remaining -ne 68 -or $quota.Week.Remaining -ne 95 -or -not $quota.Five.Reset){throw '额度窗口识别失败'}
    if((Format-QuotaResetTime $quota.Five) -ne $quota.Five.Reset.ToLocalTime().ToString('HH:mm')){throw '个人重置时间未使用本机时间'}
    if((Format-QuotaResetTime $null) -ne '--:--' -or (Format-QuotaResetTime ([pscustomobject]@{Reset=$null})) -ne '--:--'){throw '缺失重置时间处理失败'}
    $quota=Read-QuotaData '{"result":{"rateLimits":{"primary":{"usedPercent":100,"windowDurationMins":300},"secondary":null}}}'
    if($quota.Five.Remaining -ne 0 -or $null -ne $quota.Week){throw '额度缺失或耗尽处理失败'}
    foreach($bad in @('{"error":{"code":-1}}','{"result":{}}','{"result":{"rateLimits":{"primary":{"usedPercent":101,"windowDurationMins":300}}}}','{"result":{"rateLimits":{"primary":{"windowDurationMins":300}}}}','{"result":{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":15}}}}')){
        $rejected=$false;try{$null=Read-QuotaData $bad}catch{$rejected=$true};if(-not $rejected){throw '异常额度数据未拒绝'}
    }
    'PASS: countdown, UTC, malformed data, quota periods, missing windows, zero quota, invalid quota'
    exit
}

. (Join-Path $PSScriptRoot 'LoadRuntime.ps1')
if(-not $QuotaTest){
    $mutexName='Local\CodexResetDesktop';if($UiTest -or $SmokeTest){$mutexName+='Test'}
    $instanceMutex=New-Object Threading.Mutex($false,$mutexName)
    if(-not $instanceMutex.WaitOne(0)){$instanceMutex.Dispose();exit}
}
Import-ResetRuntime
if($QuotaTest){
    $executable=Find-CodexExecutable
    if(-not $executable){throw '未找到 codex.exe，请安装 Codex 或将其加入 PATH'}
    $probe=New-Object CodexQuotaClient
    try{$q=Read-QuotaData ($probe.ReadAsync($executable).GetAwaiter().GetResult());Format-QuotaWindow $q.Five '5 小时';Format-QuotaWindow $q.Week '每周';'PASS: real account quota query'}finally{$probe.Dispose()}
    exit
}

[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
[System.Windows.Forms.Application]::EnableVisualStyles()
$green=[Drawing.ColorTranslator]::FromHtml('#43ef8b')
$amber=[Drawing.ColorTranslator]::FromHtml('#e7b658')
$bg=[Drawing.ColorTranslator]::FromHtml('#0b1210')
$muted=[Drawing.ColorTranslator]::FromHtml('#a0b0a8')
$font=New-Object Drawing.Font('Consolas',11)
$script:data=$null; $script:failure=$null; $script:task=$null; $script:manual=$null
$script:lastFetch=$null; $script:nextFetch=[DateTimeOffset]::UtcNow; $script:closed=$false
$script:newsTask=$null;$script:chineseAlert=$null;$script:newsFailure=$false;$script:publicRaw=$null
$quotaClient=New-Object CodexQuotaClient
$script:quota=$null; $script:quotaTask=$null; $script:quotaFailure=$null; $script:quotaPeriod='five'; $script:nextQuota=[DateTimeOffset]::UtcNow
$statePath=Join-Path $PSScriptRoot 'settings.json'
$cachePath=Join-Path $PSScriptRoot 'cache.json'
if($env:CODEX_RESET_DATA_DIR){$null=New-Item -ItemType Directory -Force $env:CODEX_RESET_DATA_DIR;$statePath=Join-Path $env:CODEX_RESET_DATA_DIR 'settings.json';$cachePath=Join-Path $env:CODEX_RESET_DATA_DIR 'cache.json'}
if($UiTest -or $SmokeTest){
    $testDirectory=Join-Path $PSScriptRoot 'test-output'
    if($env:CODEX_RESET_TEST_OUT){$testDirectory=$env:CODEX_RESET_TEST_OUT}
    $null=New-Item -ItemType Directory -Force -Path $testDirectory
    $statePath=Join-Path $testDirectory 'settings.json';$cachePath=Join-Path $testDirectory 'cache.json'
}
$autostartPath=Join-Path ([Environment]::GetFolderPath('Startup')) 'CodexReset.lnk'
if($UiTest -or $SmokeTest){$autostartPath=Join-Path $testDirectory 'CodexReset.lnk'}
$script:settings=@{}
try { if(Test-Path $statePath) { $script:settings=Get-Content $statePath -Raw | ConvertFrom-Json; if($script:settings.manual) { $script:manual=[DateTimeOffset]::Parse($script:settings.manual) } } } catch { $script:settings=@{} }
if($script:settings.quotaPeriod -in @('five','week')){$script:quotaPeriod=$script:settings.quotaPeriod}
$script:refreshHours=Get-RefreshHours $script:settings.refreshHours
$script:hideAuthorReminder=$script:settings.hideAuthorReminder -eq $true
try { if(Test-Path $cachePath) { $cached=Read-ResetData (Get-Content $cachePath -Raw);if($cached.Source -ne 'aihot'){throw '旧来源缓存'};$script:data=$cached; $script:failure='缓存 · 等待联网更新' } } catch {}
try{if(Test-Path $cachePath){$script:publicRaw=Get-Content $cachePath -Raw|ConvertFrom-Json;if($script:publicRaw.Source -eq 'aihot'){$script:chineseAlert=$script:publicRaw.Alert};$script:newsFailure=$true}}catch{}
$client=New-Object Net.Http.HttpClient
$client.Timeout=[TimeSpan]::FromSeconds(20)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('CodexResetDesktop/1.1.1')

$form=New-Object Windows.Forms.Form
$form.Text='Codex Reset'; $form.FormBorderStyle='None'; $form.Size=New-Object Drawing.Size(244,34)
$form.BackColor=$bg; $form.TopMost=$true; $form.ShowInTaskbar=$false; $form.StartPosition='Manual'
$area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Location=New-Object Drawing.Point(($area.Right-264),($area.Bottom-44))
try { if($null -ne $script:settings.x) { $p=New-Object Drawing.Point([int]$script:settings.x,[int]$script:settings.y); if(@([Windows.Forms.Screen]::AllScreens | Where-Object {$_.WorkingArea.Contains($p)}).Count -gt 0) { $form.Location=$p } } } catch {}
$label=New-Object Windows.Forms.Label
$label.Dock='Fill'; $label.TextAlign='MiddleCenter'; $label.Font=$font; $label.ForeColor=$green; $label.Text='❯ Codex · 正在连接'
$form.Controls.Add($label)
$gauge=New-Object QuotaIndicator
$form.Add_Paint({param($s,$e) $pen=New-Object Drawing.Pen($green); $e.Graphics.DrawRectangle($pen,0,0,$s.Width-1,$s.Height-1); $pen.Dispose()})
$tip=New-Object Windows.Forms.ToolTip
$details=New-Object ResetPopup
$author=New-Object AuthorCard
$author.SetPreference($script:hideAuthorReminder)
$author.Add_ReminderChanged({$script:hideAuthorReminder=$author.Suppressed;Save-Settings})
$author.Add_BilibiliClicked({Start-Process 'https://space.bilibili.com/309229096'})
$author.Add_GithubClicked({Start-Process 'https://github.com/Evan-Luxx'})
$form.Add_LocationChanged({if($author.Visible){$author.PositionAbove($form.Bounds)}})
$details.Add_SourceClicked({Start-Process 'https://aihot.news/codex-reset'})
$details.Add_NewsSourceClicked({if($details.NewsLink){Start-Process $details.NewsLink}})
function Set-PersonalTime {
    $details.Suspended=$true
    $dialog=New-Object Windows.Forms.Form; $dialog.Text='个人额度重置时间'; $dialog.Size=New-Object Drawing.Size(360,155); $dialog.StartPosition='CenterScreen'; $dialog.BackColor=$bg; $dialog.ForeColor=$green; $dialog.TopMost=$true
    $picker=New-Object Windows.Forms.DateTimePicker; $picker.Format='Custom'; $picker.CustomFormat='yyyy-MM-dd HH:mm:ss'; $picker.Location=New-Object Drawing.Point(15,15); $picker.Width=310
    if($script:manual) {$picker.Value=$script:manual.LocalDateTime} else {$picker.Value=[DateTime]::Now.AddHours(5)}
    $save=New-Object Windows.Forms.Button; $save.Text='保存（本机时间）'; $save.Location=New-Object Drawing.Point(15,55); $save.Width=190
    $save.Add_Click({$script:manual=New-Object DateTimeOffset($picker.Value); Save-Settings; $dialog.Close()})
    $dialog.Controls.AddRange(@($picker,$save)); try {$null=$dialog.ShowDialog()} finally {$dialog.Dispose();$details.Suspended=$false}; Update-View
}
function Open-Panel {
    $p=[Windows.Forms.Cursor]::Position
    $details.TogglePinned($p,(New-Object Drawing.Rectangle(($p.X-18),($p.Y-18),36,36)))
}
$tray=New-Object Windows.Forms.NotifyIcon; $tray.Icon=[QuotaIndicator]::MakeIcon(-1,$false); $tray.Text='Codex Reset'; $tray.Visible=$true
$gauge.Tray=$tray
$menu=New-Object ResetMenu
$periodMenu=New-Object Windows.Forms.ToolStripMenuItem('显示额度')
$fiveItem=New-Object Windows.Forms.ToolStripMenuItem('5 小时余量')
$weekItem=New-Object Windows.Forms.ToolStripMenuItem('每周余量')
$fiveItem.Add_Click({$script:quotaPeriod='five';Save-Settings;Update-View})
$weekItem.Add_Click({$script:quotaPeriod='week';Save-Settings;Update-View})
$null=$periodMenu.DropDownItems.Add($fiveItem);$null=$periodMenu.DropDownItems.Add($weekItem);$null=$menu.Items.Add($periodMenu)
$null=$menu.Items.Add('刷新个人额度',$null,{if([DateTimeOffset]::UtcNow -ge $script:nextQuota.AddSeconds(-105)){Start-QuotaFetch}})
$null=$menu.Items.Add('显示 / 隐藏小区域',$null,{if($form.Visible){$form.Hide()}else{$form.Show()}})
$null=$menu.Items.Add('打开 / 固定面板',$null,{Open-Panel})
$null=$menu.Items.Add('刷新数据',$null,{if(-not $script:task -and (-not $script:lastAttempt -or [DateTimeOffset]::UtcNow -ge $script:lastAttempt.AddMinutes(1)) -and (-not $script:rateLimitUntil -or [DateTimeOffset]::UtcNow -ge $script:rateLimitUntil)) {$script:nextFetch=[DateTimeOffset]::UtcNow; Start-Fetch}})
$refreshMenu=New-Object Windows.Forms.ToolStripMenuItem('自动更新数据')
$refreshWheel=New-Object RefreshWheel
$refreshWheel.InitializeHours($script:refreshHours)
$refreshHost=New-Object Windows.Forms.ToolStripControlHost($refreshWheel)
$refreshHost.Margin=New-Object Windows.Forms.Padding(0)
$refreshHost.Padding=New-Object Windows.Forms.Padding(0)
$refreshDrop=New-Object Windows.Forms.ToolStripDropDown
$null=$refreshDrop.Items.Add($refreshHost)
$refreshMenu.DropDown=$refreshDrop
$refreshWheel.Add_ValueChanged({
        $script:refreshHours=$refreshWheel.Hours
        $script:nextFetch=[DateTimeOffset]::UtcNow.AddHours($script:refreshHours)
        if($script:rateLimitUntil -and $script:rateLimitUntil -gt $script:nextFetch){$script:nextFetch=$script:rateLimitUntil}
        Save-Settings;Update-View
    })
$null=$menu.Items.Add($refreshMenu)
$autostartItem=New-Object Windows.Forms.ToolStripMenuItem('开机自启')
$autostartItem.Checked=Test-Path -LiteralPath $autostartPath
$autostartItem.Add_Click({
    try {Set-Autostart (-not $autostartItem.Checked) $autostartPath}
    catch {$null=[Windows.Forms.MessageBox]::Show('无法更改开机自启设置，请检查启动目录权限。','Codex Reset','OK','Warning')}
    finally {$autostartItem.Checked=Test-Path -LiteralPath $autostartPath}
})
$null=$menu.Items.Add($autostartItem)
$null=$menu.Items.Add('设置我的时间',$null,{Set-PersonalTime})
$null=$menu.Items.Add('清除个人时间',$null,{$script:manual=$null;Save-Settings;Update-View})
$null=$menu.Items.Add('关于作者',$null,{$author.Open($form.Bounds)})
$details.ContextMenuStrip=$menu
$menu.Add_Opening({$details.Suspended=$true;$autostartItem.Checked=Test-Path -LiteralPath $autostartPath})
$menu.Add_Closed({$details.Suspended=$false})
$null=$menu.Items.Add('退出',$null,{$script:closed=$true; $form.Close()})
$menu.ApplyTheme()
$tray.ContextMenuStrip=$menu
$tray.Add_MouseMove({$p=[Windows.Forms.Cursor]::Position;$details.Arm($p,(New-Object Drawing.Rectangle(($p.X-18),($p.Y-18),36,36)))})
$tray.Add_MouseClick({param($s,$e) if($e.Button -eq 'Left'){Open-Panel}})
$label.Add_MouseEnter({$details.Arm((New-Object Drawing.Point(($form.Right-20),$form.Top)),$form.Bounds)})
$label.ContextMenuStrip=$menu
$script:drag=$false; $script:moved=$false; $script:origin=$null
$label.Add_MouseDown({param($s,$e) if($e.Button -eq 'Left') {$script:drag=$true; $script:moved=$false; $script:origin=$e.Location}})
$label.Add_MouseMove({param($s,$e) if($script:drag) {$dx=$e.X-$script:origin.X; $dy=$e.Y-$script:origin.Y; if([Math]::Abs($dx)+[Math]::Abs($dy) -gt 3) {$script:moved=$true; $form.Location=New-Object Drawing.Point(($form.Left+$dx),($form.Top+$dy))}}})
$label.Add_MouseUp({param($s,$e) if($script:drag) {$script:drag=$false; if($script:moved){Save-Settings}else{Open-Panel}}})
function Save-Settings {
    try { @{refreshHours=$script:refreshHours;x=$form.Left;y=$form.Top;quotaPeriod=$script:quotaPeriod;hideAuthorReminder=$script:hideAuthorReminder;manual=$(if($script:manual){$script:manual.ToString('o')}else{$null})} | ConvertTo-Json | Set-Content $statePath -Encoding UTF8 } catch {}
}
function Start-Fetch {
    if($script:task){return}
    $script:lastAttempt=[DateTimeOffset]::UtcNow;$script:nextFetch=$script:lastAttempt.AddMinutes(1)
    $script:task=$client.GetAsync('https://aihot.news/codex-reset')

}
function Save-PublicCache {
    if($script:publicRaw){try{$script:publicRaw|Add-Member -NotePropertyName desktopChineseAlert -NotePropertyValue $script:chineseAlert -Force;$script:publicRaw|ConvertTo-Json -Depth 32|Set-Content $cachePath -Encoding UTF8}catch{}}
}
function Update-View {
    $status='暂无已确认数据'; $color=$amber
    if($script:data) {
        $last=$script:data.Last.ToOffset([TimeSpan]::FromHours(8))
        $status='等待信号'; $color=$green
        if($last.Date -eq [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).Date) {$status='来源记录今日重置'}
    }
    if($script:data) {$status=$script:data.Status; $color=$amber}
    if($script:manual) {$status='个人 '+(Format-Remaining $script:manual ([DateTimeOffset]::UtcNow))}
    if($script:failure) {$color=$amber; if(-not $script:manual){$status='离线 · 查看详情'}}
    $label.Text='❯ Codex · '+$status; $label.ForeColor=$color
    $text="全局重置监控 · 北京时间`r`n`r`n"
    if($script:data) {
        $text+='上次额度重置：'+$last.ToString('yyyy-MM-dd')+"`r`n"
        $text+=$script:data.Status+"`r`n"+$script:data.Estimate+"`r`n适用范围："+$script:data.Scope+"`r`n"
    }else{$text+="尚未取得有效数据`r`n"}
    if($script:lastFetch){$text+='本机更新：'+$script:lastFetch.ToLocalTime().ToString('HH:mm:ss')+"`r`n"}
    if($script:failure){$text+=$script:failure+"`r`n"}
    if($script:manual){$text+="个人时间（手动）："+$script:manual.LocalDateTime.ToString('MM-dd HH:mm')+"`r`n"}
    $text+="`r`n发卡不代表额度恢复；预计时间仅供参考。"
$details.Offline=[bool]$script:failure
$details.HasSignal=[bool]($script:data -and $script:data.Signal)
if($script:chineseAlert){$a=$script:chineseAlert;$meta=$a.Meta;if($script:newsFailure){$meta='中文缓存 · 等待同步'};$details.SetNews($a.Key,$a.Text,$meta,$a.Link);$details.HasSignal=[bool]$a.HasSignal}else{$details.SetNews('','正在等待中文来源消息，请稍后刷新。','中文网页 · 等待同步','https://aihot.news/codex-reset')}
$details.StatusText='等待下一次重置信号'
$details.SubText='暂无官方时间 · 持续监控中'
$details.ScopeText='全局监控'
$details.TimeLabel='上次额度重置 / 北京日期'
$details.TimeText='--.--  --:--'
if($script:data){$details.TimeText=$last.ToString('yyyy.MM.dd');$details.StatusText=$script:data.Status;$details.SubText=$script:data.Estimate}
if($script:failure){$details.SubText='离线 · 显示缓存，稍后重试'}
$details.UpdateText='正在连接'
if($script:lastFetch){$details.UpdateText='已更新 '+$script:lastFetch.ToLocalTime().ToString('HH:mm')+' · '+$script:refreshHours+'h'}
elseif($script:data){$details.UpdateText='缓存 · 尚未联网核验'}
if($script:failure){$details.UpdateText='更新失败 · 保留缓存'}
if($script:manual){$details.ScopeText='个人倒计时';$details.StatusText='我的额度恢复时间';$details.SubText='手动设置 · 本机时间';$details.TimeLabel='剩余时间 / 到期后请核实额度';$details.TimeText=Format-Remaining $script:manual ([DateTimeOffset]::UtcNow)}
$renderState=@($details.StatusText,$details.SubText,$details.ScopeText,$details.TimeLabel,$details.TimeText,$details.UpdateText,$details.Offline) -join '|'
if($script:lastRenderState -ne $renderState){$script:lastRenderState=$renderState;if($details.Visible){$details.Invalidate()}}
if($script:lastTooltip -ne $text){$script:lastTooltip=$text;$tip.SetToolTip($label,$text)}
Update-QuotaView
}
. (Join-Path $PSScriptRoot 'QuotaUi.ps1')
$timer=New-Object Windows.Forms.Timer; $timer.Interval=1000
$timer.Add_Tick({
    try {
        if($script:newsTask -and $script:newsTask.IsCompleted){
            $newsResponse=$null
            try{$newsResponse=$script:newsTask.GetAwaiter().GetResult();if([int]$newsResponse.StatusCode -eq 429){$retry=[DateTimeOffset]::UtcNow.AddMinutes(15);if($newsResponse.Headers.RetryAfter.Delta){$retry=[DateTimeOffset]::UtcNow.Add($newsResponse.Headers.RetryAfter.Delta)}elseif($newsResponse.Headers.RetryAfter.Date){$retry=$newsResponse.Headers.RetryAfter.Date};if($retry -gt $script:nextFetch){$script:nextFetch=$retry}};$null=$newsResponse.EnsureSuccessStatusCode();$script:chineseAlert=Read-ChineseNews ($newsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());$script:newsFailure=$false;Save-PublicCache}
            catch{$script:newsFailure=$true}
            finally{if($newsResponse){$newsResponse.Dispose()};$script:newsTask=$null}
        }
        if($script:quotaTask -and $script:quotaTask.IsCompleted){
            try{$script:quota=Read-QuotaData ($script:quotaTask.GetAwaiter().GetResult());$script:quotaFailure=$null}
            catch{
                $script:quotaFailure='查询失败 · 请检查 Codex 登录状态'
                if($_.Exception.GetBaseException() -is [TimeoutException]){$script:quotaFailure='查询超时 · 旧数据，稍后重试'}else{$script:quota=$null}
            }finally{$script:quotaTask=$null}
        }
        if(-not $script:quotaTask -and [DateTimeOffset]::UtcNow -ge $script:nextQuota){Start-QuotaFetch}
        if($script:task -and $script:task.IsCompleted) {
            $response=$null
            try {
                $response=$script:task.GetAwaiter().GetResult()
                if([int]$response.StatusCode -eq 429){$script:nextFetch=[DateTimeOffset]::UtcNow.AddMinutes(15); if($response.Headers.RetryAfter.Delta){$script:nextFetch=[DateTimeOffset]::UtcNow.Add($response.Headers.RetryAfter.Delta)}elseif($response.Headers.RetryAfter.Date){$script:nextFetch=$response.Headers.RetryAfter.Date.Value}; $script:rateLimitUntil=$script:nextFetch; throw '请求过多，稍后重试'}
                $null=$response.EnsureSuccessStatusCode()
                $raw=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); $parsed=Read-AihotData $raw
                if($script:data -and $parsed.Last -gt $script:data.Last){$tray.ShowBalloonTip(5000,'Codex 重置消息','AIHOT 新增了额度重置记录，请在 Codex 内核实。',[Windows.Forms.ToolTipIcon]::Info)}
                $script:data=$parsed; $script:failure=$null; $script:lastFetch=[DateTimeOffset]::UtcNow
                $script:chineseAlert=$parsed.Alert;$script:newsFailure=$false;$script:publicRaw=$parsed;Save-PublicCache;$script:nextFetch=$script:lastFetch.AddHours($script:refreshHours)
            } catch {$script:failure='联网更新失败 · 保留缓存，稍后重试'} finally {if($response){$response.Dispose()}; $script:task=$null}
        }
        if(-not $UiTest -and -not $script:task -and [DateTimeOffset]::UtcNow -ge $script:nextFetch){Start-Fetch}
        Update-View
    } catch {$script:failure='更新异常 · 稍后重试'}
})
$form.Add_FormClosed({$script:closed=$true; Save-Settings; $timer.Stop();$quotaClient.Dispose();$gauge.Dispose(); $tray.Visible=$false; $tray.Icon.Dispose(); $tray.Dispose(); $menu.Dispose(); $author.Dispose(); $details.Dispose(); $client.Dispose(); $tip.Dispose(); $timer.Dispose(); $font.Dispose(); $instanceMutex.ReleaseMutex(); $instanceMutex.Dispose()})
if($UiTest){
    $script:data=$null;$script:chineseAlert=$null;$script:failure=$null;$script:manual=$null
    $script:quota=Read-QuotaData '{"result":{"rateLimits":{"primary":{"usedPercent":28,"windowDurationMins":300,"resetsAt":1893456000},"secondary":{"usedPercent":62,"windowDurationMins":10080,"resetsAt":1894060800}}}}'
    $script:testStart=[DateTimeOffset]::UtcNow
    $timer.Add_Tick({
        $elapsed=([DateTimeOffset]::UtcNow-$script:testStart).TotalSeconds
        if($elapsed -lt 3){
            if(-not $script:quotaAnimationTested){
                $gauge.TestInstant=$false;$gauge.SetQuota(38,$false)
                if($gauge.DisplayedPercent -ne 72){throw '刻度动画没有渐进变化'}
                $script:quotaAnimationTested=$true
            }
            return
        }
        try{
            $timer.Stop()
            $author.Open($form.Bounds)
            $bitmap=New-Object Drawing.Bitmap($author.Width,$author.Height);$author.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$author.Width,$author.Height)));$bitmap.Save((Join-Path $testDirectory '作者提醒.png'));$bitmap.Dispose()
            $author.SetPreference($false);$author.ToggleReminder()
            $saved=Get-Content $statePath -Raw|ConvertFrom-Json;if(-not $saved.hideAuthorReminder){throw '作者提醒偏好未保存'}
            $author.Dismiss();if($author.Visible){throw '作者提醒关闭失败'}
            $author.Open($form.Bounds);if(-not $author.Visible -or -not $author.Suppressed){throw '关于作者重新打开失败'}
            $author.Dismiss()
            if(@($menu.Items | ForEach-Object {$_.Text}) -contains '关闭动画'){throw '动画设置未移除'}
            $details.TestInstant=$true;$gauge.TestInstant=$true;$details.OpenForTest((New-Object Drawing.Point(800,600)));Update-View
            if($details.FiveQuota -ne 72 -or $details.WeekQuota -ne 38){throw '双周期刻度未绑定'}
            if($details.FiveResetText -ne (Format-QuotaResetTime $script:quota.Five)){throw '5小时重置时间未绑定'}
            if($details.StatusText -ne '等待下一次重置信号' -or $details.ScopeText -ne '全局监控' -or ($label.Text -notlike '❯ Codex · *' -or $label.Text.Contains('%'))){throw '原有窗口内容未恢复'}
            $bitmap=New-Object Drawing.Bitmap($details.Width,$details.Height);$details.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$details.Width,$details.Height)));$bitmap.Save((Join-Path $testDirectory '额度面板.png'));$bitmap.Dispose()
            $bitmap=New-Object Drawing.Bitmap($form.Width,$form.Height);$form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)));$bitmap.Save((Join-Path $testDirectory '额度浮窗.png'));$bitmap.Dispose()
            $dummy=('这是虚构的滚动测试内容，用于检查黄色消息框的长文显示。' + "`r`n`r`n")*16
            $details.SetNews('scroll-test',$dummy,'虚构测试消息','')
            $details.TestInstant=$false;$details.FlipPage()
            if(-not $details.IsFlipping){throw '翻转动画未启动'}
            $watch=[Diagnostics.Stopwatch]::StartNew()
            while($watch.ElapsedMilliseconds -lt 150){[Windows.Forms.Application]::DoEvents();[Threading.Thread]::Sleep(10)}
            if(-not $details.IsFlipping -or $details.Region.IsVisible(8,140)){throw '翻转背景未随卡片裁切'}
            $bitmap=New-Object Drawing.Bitmap($details.Width,$details.Height);$details.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$details.Width,$details.Height)));$bitmap.Save((Join-Path $testDirectory '卡片翻转.png'));$bitmap.Dispose()
            while($watch.ElapsedMilliseconds -lt 650){[Windows.Forms.Application]::DoEvents();[Threading.Thread]::Sleep(10)}
            if($details.IsFlipping -or -not $details.IsNewsPage){throw '翻转完成状态失败'}
            if(-not $details.Region.IsVisible(8,140)){throw '翻转后窗口轮廓未恢复'}
            ('Flip paints: {0}; maximum paint time: {1:F2} ms' -f $details.FlipPaintCount,$details.MaxFlipPaintMilliseconds)|Set-Content (Join-Path $testDirectory '翻转性能.txt') -Encoding UTF8
            $bitmap=New-Object Drawing.Bitmap($details.Width,$details.Height);$details.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$details.Width,$details.Height)));$bitmap.Save((Join-Path $testDirectory '消息页.png'));$bitmap.Dispose()
            $details.ScrollNews(-120);if($details.NewsScroll -le 0){throw '长文滚动失败'}
            $details.SetNews('scroll-test',$dummy,'虚构测试消息','');if($details.NewsScroll -le 0){throw '相同消息滚动位置未保留'}
            $details.ScrollNews(120000);if($details.NewsScroll -ne 0){throw '滚动上界失败'}
            $details.ScrollNews(-120000);$details.SetNews('new-message','新的虚构测试消息','测试','');if($details.NewsScroll -ne 0){throw '新消息滚动位置未重置'}
            $details.TestInstant=$true;$details.FlipPage();if($details.IsNewsPage){throw '返回监控失败'}
            $script:quotaPeriod='five';Update-View
            if($gauge.DisplayedPercent -ne 72 -or -not $fiveItem.Checked){throw '5小时切换失败'}
            $weekItem.PerformClick()
            if($gauge.DisplayedPercent -ne 38 -or -not $weekItem.Checked -or -not $tray.Text.Contains('周')){throw '每周菜单切换失败'}
            if($details.FiveQuota -ne 72 -or $details.WeekQuota -ne 38){throw '托盘切换影响双周期刻度'}
            $saved=Get-Content $statePath -Raw|ConvertFrom-Json;if($saved.quotaPeriod -ne 'week'){throw '周期保存失败'}
            $script:quotaFailure='查询超时 · 旧数据';Update-View
            if(-not $tray.Text.Contains('旧数据') -or $label.Text.Contains('旧数据')){throw '旧数据状态失败'}
            $script:quota=$null;Update-View
            if($details.FiveResetText -ne '--:--'){throw '查询错误后重置时间未清除'}
            if($details.FiveQuota -ne -1 -or $details.WeekQuota -ne -1){throw '双周期未知状态失败'}
            if($gauge.DisplayedPercent -ne -1 -or -not $tray.Text.Contains('查询失败')){throw '缺失额度失败'}
            $script:quotaFailure=$null
            $script:quota=Read-QuotaData '{"result":{"rateLimits":{"primary":{"usedPercent":100,"windowDurationMins":300},"secondary":{"usedPercent":0,"windowDurationMins":10080}}}}'
            Update-View;if(-not $tray.Text.Contains('耗尽')){throw '另一周期耗尽提示失败'}
            $sheet=New-Object Drawing.Bitmap(240,64);$graphics=[Drawing.Graphics]::FromImage($sheet);$graphics.Clear($bg)
            $values=@(100,72,25,8,0,-1);for($i=0;$i -lt $values.Count;$i++){[QuotaIndicator]::DrawSegments($graphics,(New-Object Drawing.RectangleF(($i*40),0,32,32)),$values[$i],$false);[QuotaIndicator]::DrawSegments($graphics,(New-Object Drawing.RectangleF(($i*40),40,16,16)),$values[$i],$false)}
            $sheet.Save((Join-Path $testDirectory '分段图标.png'));$graphics.Dispose();$sheet.Dispose()
            $menu.Show((New-Object Drawing.Point(800,600)));$periodMenu.Select();$periodMenu.ShowDropDown()
            $bitmap=New-Object Drawing.Bitmap($menu.Width,$menu.Height);$menu.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$menu.Width,$menu.Height)));$bitmap.Save((Join-Path $testDirectory '右键菜单.png'));$bitmap.Dispose()
            $dropdown=$periodMenu.DropDown
            $bitmap=New-Object Drawing.Bitmap($dropdown.Width,$dropdown.Height);$dropdown.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$dropdown.Width,$dropdown.Height)));$bitmap.Save((Join-Path $testDirectory '周期子菜单.png'));$bitmap.Dispose()
            $periodMenu.HideDropDown();$menu.Close()
            $details.Dismiss();if($details.Visible){throw '收起失败'}
            if($refreshMenu.DropDownItems.Count -ne 1 -or $refreshWheel.Height -gt 32){throw '更新间隔选择器尺寸异常'}
            $refreshMenu.Select();$refreshMenu.ShowDropDown()
            $refreshWheel.InitializeHours(1);$refreshWheel.Roll(-120)
            if($script:refreshHours -ne 24 -or (Get-Content $statePath -Raw|ConvertFrom-Json).refreshHours -ne 24){throw '更新间隔保存失败'}
            if(-not $refreshWheel.Animating -or $refreshWheel.DisplayedPosition -eq 0){throw '滚轮动画未渐进启动'}
            $refreshWheel.Roll(240)
            if($script:refreshHours -ne 2){throw '更新间隔循环切换失败'}
            $watch.Restart();while($watch.ElapsedMilliseconds -lt 300){[Windows.Forms.Application]::DoEvents();[Threading.Thread]::Sleep(10)}
            if($refreshWheel.Animating -or $refreshWheel.DisplayedPosition -ne 2){throw '滚轮动画未停稳'}
            $bitmap=New-Object Drawing.Bitmap($refreshDrop.Width,$refreshDrop.Height);$refreshDrop.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$refreshDrop.Width,$refreshDrop.Height)));$bitmap.Save((Join-Path $testDirectory '更新间隔滚轮.png'));$bitmap.Dispose()
            $refreshMenu.HideDropDown();$menu.Close()
            try {
                Set-Autostart $false $autostartPath
                $autostartItem.Checked=$false
                $autostartItem.PerformClick()
                if(-not $autostartItem.Checked){throw '开机自启开启失败'}
                $startupShell=New-Object -ComObject WScript.Shell
                $startupShortcut=$null
                try {
                    $startupShortcut=$startupShell.CreateShortcut($autostartPath)
                    if($env:CODEX_RESET_LAUNCHER){
                        if($startupShortcut.TargetPath -ne $env:CODEX_RESET_LAUNCHER -or $startupShortcut.Arguments){throw 'EXE 自启命令错误'}
                    } elseif($startupShortcut.TargetPath -ne (Join-Path $PSHOME 'powershell.exe') -or -not $startupShortcut.Arguments.Contains('"'+(Join-Path $PSScriptRoot 'CodexReset.ps1')+'"')){throw '源码自启命令错误'}
                } finally {
                    if($null -ne $startupShortcut){$null=[Runtime.InteropServices.Marshal]::FinalReleaseComObject($startupShortcut)}
                    $null=[Runtime.InteropServices.Marshal]::FinalReleaseComObject($startupShell)
                }
                $autostartItem.PerformClick()
                if($autostartItem.Checked -or (Test-Path -LiteralPath $autostartPath)){throw '开机自启关闭失败'}
            } finally {Set-Autostart $false $autostartPath}
            'PASS: original windows preserved, 3D flip, news scroll, message replacement, return button, tray menu switch, autostart enable/disable and command, settings persistence, refresh interval menu and persistence, stale data, missing data, exhausted window, icon sizes, dismiss'|Set-Content (Join-Path $testDirectory '额度界面测试.txt') -Encoding UTF8
        }catch{('FAIL: '+$_)|Set-Content (Join-Path $testDirectory '额度界面测试.txt') -Encoding UTF8}
        $form.Close()
    })
}
if($SmokeTest) {
    $script:smokeStart=[DateTimeOffset]::UtcNow
    $timer.Add_Tick({
                if(([DateTimeOffset]::UtcNow-$script:smokeStart).TotalSeconds -ge 21 -and -not $script:animationStarted) {
            $script:animationStarted=$true
            $details.TestInstant=$false
            $details.OpenForTest((New-Object Drawing.Point(800,600)))
            if($details.RevealProgress -ge 1){throw '动画没有渐进展开'}
        }
        if(([DateTimeOffset]::UtcNow-$script:smokeStart).TotalSeconds -ge 24) {
            try {
                if(-not $script:data -or $script:failure){throw ('联网刷新失败: '+$script:failure)}
                if(-not $script:chineseAlert -or $script:newsFailure){throw '中文网页消息同步失败'}
                $details.TestInstant=$true
                $details.OpenForTest((New-Object Drawing.Point(800,600))); Update-View
                if(-not $details.Visible -or $details.RevealProgress -ne 1){throw '展开失败'}
                $bitmap=New-Object Drawing.Bitmap($details.Width,$details.Height); $details.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$details.Width,$details.Height))); $bitmap.Save((Join-Path $env:CODEX_RESET_TEST_OUT '新版面板.png')); $bitmap.Dispose()
                $script:manual=[DateTimeOffset]::UtcNow.AddMinutes(2); Update-View
                if(-not $details.TimeText.StartsWith('00:')){throw '个人倒计时未显示'}
                $script:manual=$null; $script:failure='模拟断网'; Update-View
                if(-not $details.Offline -or -not $details.SubText.Contains('离线')){throw '断网状态未显示'}
                $script:failure=$null; Update-View
                $details.Dismiss(); if($details.Visible){throw '收起失败'}
                $fit=[ResetPopup]::Fit((New-Object Drawing.Point(5,5)),(New-Object Drawing.Rectangle(0,0,1920,1080)),(New-Object Drawing.Size(352,283)))
                if($fit.X -lt 0 -or $fit.Y -lt 0){throw '屏幕边缘定位失败'}
                'PASS: real HTTP refresh, borderless rendering, personal countdown, offline state, reveal, dismiss, edge positioning' | Set-Content (Join-Path $env:CODEX_RESET_TEST_OUT '新版测试结果.txt') -Encoding UTF8
            } catch {('FAIL: '+$_) | Set-Content (Join-Path $env:CODEX_RESET_TEST_OUT '新版测试结果.txt') -Encoding UTF8}
            $script:manual=$null; $script:closed=$true; $form.Close()
        }
    })
}
$form.Add_Shown({
    if(-not $script:hideAuthorReminder){$author.Open($form.Bounds)}
    if($UiTest -or $SmokeTest){('Window shown after '+$launchClock.ElapsedMilliseconds+' ms') | Set-Content (Join-Path $testDirectory '启动耗时.txt') -Encoding UTF8}
    $form.BeginInvoke([Action]{$details.Prepare()}) | Out-Null
})
$timer.Start();Update-View
[Windows.Forms.Application]::Run($form)
