function Start-QuotaFetch {
    if($script:quotaTask -or $UiTest){return}
    $script:nextQuota=[DateTimeOffset]::UtcNow.AddMinutes(2)
    $executable=Find-CodexExecutable
    if(-not $executable){$script:quota=$null;$script:quotaFailure='未找到 Codex，请安装或加入 PATH';return}
    $script:quotaTask=$quotaClient.ReadAsync($executable)
}
function Update-QuotaView {
    $selected=$null;$period='5h'
    if($script:quota){$selected=$script:quota.Five}
    if($script:quotaPeriod -eq 'week'){$period='周';if($script:quota){$selected=$script:quota.Week}}
    $fiveItem.Checked=$script:quotaPeriod -eq 'five';$weekItem.Checked=$script:quotaPeriod -eq 'week'
    $stale=[bool]$script:quotaFailure
    if($script:quota -and ([DateTimeOffset]::UtcNow-$script:quota.Updated).TotalMinutes -gt 5){$stale=$true}
    $fiveRemaining=-1.0;$weekRemaining=-1.0;$fiveOld=$stale;$weekOld=$stale
    if($script:quota.Five){$fiveRemaining=$script:quota.Five.Remaining;if($script:quota.Five.Reset -and $script:quota.Five.Reset -le [DateTimeOffset]::UtcNow){$fiveOld=$true}}
    if($script:quota.Week){$weekRemaining=$script:quota.Week.Remaining;if($script:quota.Week.Reset -and $script:quota.Week.Reset -le [DateTimeOffset]::UtcNow){$weekOld=$true}}
    $details.SetPanelQuota($fiveRemaining,$weekRemaining,$fiveOld,$weekOld)
    if($selected -and $selected.Reset -and $selected.Reset -le [DateTimeOffset]::UtcNow){$stale=$true}
    $remaining=-1.0;if($selected){$remaining=$selected.Remaining}
    $gauge.SetQuota($remaining,$stale)
    $status='正在查询';if($script:quota){$status='暂不可用'}
    if($selected){$status=$remaining.ToString('0.#')+'% 剩余';if($stale){$status+=' · 旧数据'}}
    elseif($script:quotaFailure){$status='查询失败'}
    if($script:quota -and (($script:quota.Five -and $script:quota.Five.Remaining -eq 0) -or ($script:quota.Week -and $script:quota.Week.Remaining -eq 0))){$status+=' · 额度耗尽'}
    $trayText='Codex '+$period+' '+$status
    $tray.Text=$trayText.Substring(0,[Math]::Min(63,$trayText.Length))
}
