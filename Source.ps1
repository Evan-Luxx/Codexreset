function Read-AihotData([string]$html) {
    # Decode the React Router reference table without executing remote scripts.
    $match=[regex]::Match($html,'streamController.enqueue\(("(?:\\.|[^"\\])*")\)')
    if(-not $match.Success){throw 'AIHOT 页面缺少结构化数据'}
    $table=ConvertFrom-Json (ConvertFrom-Json $match.Groups[1].Value)
    function Expand-Node([int]$index,[int]$depth=0) {
        if($index -eq -5){return $null}
        if($index -lt 0 -or $index -ge $table.Count -or $depth -gt 40){throw 'AIHOT 数据引用异常'}
        $node=$table[$index]
        if($node -and $node.GetType() -eq [System.Management.Automation.PSCustomObject]){
            $result=[ordered]@{}
            foreach($prop in $node.PSObject.Properties){
                if($prop.Name -notmatch '^_(\d+)$'){throw 'AIHOT 对象结构异常'}
                $result[[string]$table[[int]$Matches[1]]]=Expand-Node ([int]$prop.Value) ($depth+1)
            }
            return [pscustomobject]$result
        }
        if($node -is [array]){return ,@($node|ForEach-Object {Expand-Node ([int]$_) ($depth+1)})}
        return $node
    }
    $source=(Expand-Node 0).loaderData.'codex-reset'
    if($source.schemaVersion -ne 1 -or $source.stats.lastResetDate -notmatch '^\d{4}-\d{2}-\d{2}$'){throw 'AIHOT 数据版本或日期异常'}
    $last=[DateTimeOffset]::Parse($source.stats.lastResetDate+'T00:00:00+08:00')
    if($last -gt [DateTimeOffset]::UtcNow.AddMinutes(5)){throw 'AIHOT 重置日期异常'}
    $event=$source.current
    $status='暂无新的重置消息';$scope='适用范围以来源为准';$estimate='暂无预计时间';$text='暂无新的重置或发卡消息。'
    if($event){
        if($event.type -notin @('direct_reset','reset_credit')){throw 'AIHOT 事件类型未知'}
        $status=[string]$event.title
        if(-not $status){$status=[string]$event.displayLabel}
        if($event.presentation.audienceZh){$scope=[string]$event.presentation.audienceZh}elseif($event.scope){$scope=[string]$event.scope}
        if($event.estimate.label){$estimate=[string]$event.estimate.label}elseif($event.schedule.label){$estimate=[string]$event.schedule.label}else{$estimate='时间以来源确认为准'}
        $text=$event.displayLabel+' · '+$status+"`r`n"+$estimate+"`r`n适用范围："+$scope+"`r`n"+$event.estimate.reason
        foreach($post in $event.posts){$text+="`r`n`r`n"+$post.fullText}
        $text+="`r`n`r`n发卡不代表额度恢复；预计时间不代表已到账。请在 Codex 内核实。"
    }
    $alert=[pscustomobject]@{Key=[string]$event.id;Text=$text;Meta='AIHOT 整理 · 非 OpenAI 官方';Link='https://aihot.news/codex-reset';HasSignal=[bool]$event}
    [pscustomobject]@{Source='aihot';Last=$last;Signal=[bool]$event;Status=$status;Scope=$scope;Estimate=$estimate;Stats=$source.stats;Alert=$alert;Updated=$source.checkedAt}
}
function Get-RefreshHours($value) {
    $hours=0
    if([int]::TryParse([string]$value,[ref]$hours) -and $hours -ge 1 -and $hours -le 24){return $hours}
    return 2
}
