function Find-CodexExecutable {
    $command = Get-Command codex.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    # Desktop installs keep a versioned CLI beside the app; do not assume a version hash.
    $base = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
    if (Test-Path $base) {
        $found = Get-ChildItem -LiteralPath $base -Filter codex.exe -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

function Read-QuotaData([string]$raw) {
    $message = $raw | ConvertFrom-Json
    if ($message.error) { throw '无法读取额度，请检查 Codex 登录状态或稍后重试' }
    if (-not $message.result) { throw '额度响应缺少结果' }
    $result = $message.result
    $bucket = $null
    if ($result.rateLimitsByLimitId) {
        $bucket = $result.rateLimitsByLimitId.codex
    } elseif ($result.rateLimits -and ($result.rateLimits.limitId -eq 'codex' -or -not $result.rateLimits.limitId)) {
        $bucket = $result.rateLimits
    }
    if (-not $bucket) { throw '当前账户没有可用的 Codex 额度数据' }
    $five = $null; $week = $null
    foreach ($window in @($bucket.primary, $bucket.secondary)) {
        if ($null -eq $window) { continue }
        if ($window.windowDurationMins -notin @(300,10080)) { continue }
        if ($null -eq $window.usedPercent) { continue }
        $used = 0.0
        if (-not [double]::TryParse([string]$window.usedPercent, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$used) -or
            [double]::IsNaN($used) -or [double]::IsInfinity($used) -or $used -lt 0 -or $used -gt 100) { throw '额度百分比无效' }
        $reset = $null
        if ($null -ne $window.resetsAt) {
            $seconds = 0L
            if (-not [long]::TryParse([string]$window.resetsAt, [ref]$seconds) -or $seconds -le 0) { throw '额度恢复时间无效' }
            $reset = ([DateTimeOffset]'1970-01-01T00:00:00Z').AddSeconds($seconds)
        }
        $value = [pscustomobject]@{ Remaining=(100.0-$used); Reset=$reset }
        if ($window.windowDurationMins -eq 300) { $five=$value } else { $week=$value }
    }
    if (-not $five -and -not $week) { throw '当前账户未提供 5 小时或每周额度' }
    [pscustomobject]@{ Five=$five; Week=$week; Updated=[DateTimeOffset]::UtcNow }
}

function Format-QuotaResetTime($window) {
    if (-not $window -or -not $window.Reset) { return '--:--' }
    return $window.Reset.ToLocalTime().ToString('HH:mm')
}

function Format-QuotaWindow($window, [string]$name) {
    if (-not $window) { return "$name 暂不可用" }
    $s = $name+' 剩余 '+$window.Remaining.ToString('0.#',[Globalization.CultureInfo]::InvariantCulture)+'%'
    if ($window.Reset) { $s += ' · 恢复 '+$window.Reset.ToLocalTime().ToString('MM-dd HH:mm') }
    return $s
}
