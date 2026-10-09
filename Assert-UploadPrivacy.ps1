param(
    [string]$Root = $PSScriptRoot,
    [string[]]$Files
)
$ErrorActionPreference='Stop'
$allowed=@('启动.cmd','CodexReset.ps1','ResetPopup.cs','NewsPopup.cs','CodexQuotaClient.cs','Quota.ps1','Source.ps1','QuotaUi.ps1','LoadRuntime.ps1','Launcher.cs','NativeApp.cs','NativeData.cs','NativeTests.cs','Build-Exe.ps1','README.md','AGENTS.md','agent.md','.gitignore','Assert-UploadPrivacy.ps1','CodexReset.exe')
$rootPath=[IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
if(-not (Test-Path -LiteralPath $rootPath -PathType Container)){throw '上传候选目录不存在'}
if(-not $Files){
    $Files=@(Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force | ForEach-Object {$_.FullName.Substring($rootPath.Length+1)})
}
if(-not $Files){throw '没有候选文件，不能确认上传安全'}
$patterns=@{
    'API key'='sk-[A-Za-z0-9_-]{20,}'
    'Bearer credential'='(?i)Bearer\s+[A-Za-z0-9._~-]{20,}'
    'JWT credential'='eyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}'
    'Credential assignment'='(?i)(access_token|refresh_token|api_key|password)\s*["'']?\s*[:=]\s*["''][^"'']{8,}'
    'Email address'='[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
    'Personal Windows path'='(?i)[A-Z]:[\\/]+Users[\\/]+[^\s"''<>]+'
    'Literal account identifier'='(?i)["'']?account_?id["'']?\s*[:=]\s*["''][A-Za-z0-9_-]{8,}["'']'
}
$issues=New-Object 'Collections.Generic.List[string]'
foreach($relative in $Files){
    if($relative -notin $allowed){$issues.Add('文件不在发布白名单：'+$relative);continue}
    $path=[IO.Path]::GetFullPath((Join-Path $rootPath $relative))
    if(-not $path.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '候选文件超出上传目录'}
    $item=Get-Item -LiteralPath $path
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '禁止目录或链接候选项'}
    if($relative -eq 'CodexReset.exe'){
        $bytes=[IO.File]::ReadAllBytes($path)
        $assembly=[Reflection.Assembly]::Load($bytes)
        if(@($assembly.GetManifestResourceNames()).Count -ne 0){throw '发布 EXE 包含非预期嵌入资源'}
        if($null -eq $assembly.GetType('NativeApp') -or $null -eq $assembly.GetType('NativeData')){throw '发布 EXE 不是原生程序'}
        if('System.Management.Automation' -in @($assembly.GetReferencedAssemblies() | ForEach-Object {$_.Name})){throw '发布 EXE 引用了 PowerShell 运行库'}
        $body=[Text.Encoding]::UTF8.GetString($bytes)+"`n"+[Text.Encoding]::Unicode.GetString($bytes)
    }else{$body=[IO.File]::ReadAllText($path)}
    foreach($category in $patterns.Keys){if([regex]::IsMatch($body,$patterns[$category])){$issues.Add($relative+'：'+$category+'（值已隐藏）')}}
}
if($issues.Count){$issues | ForEach-Object {Write-Output $_};throw '隐私检查失败，禁止上传候选内容'}
'PASS: '+$Files.Count+' 个白名单文件通过基础扫描；上传前仍须按 agent.md 人工检查及验证最终归档/提交。'
