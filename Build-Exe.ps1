$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$payload=@('Launcher.cs','NativeApp.cs','NativeData.cs','NativeTests.cs','ResetPopup.cs','NewsPopup.cs','CodexQuotaClient.cs')
$review=@($payload)+@('启动.cmd','Build-Exe.ps1','README.md','AGENTS.md','agent.md','Assert-UploadPrivacy.ps1')
& (Join-Path $PSScriptRoot 'Assert-UploadPrivacy.ps1') -Files $review
$output=Join-Path $PSScriptRoot 'releases'
$null=New-Item -ItemType Directory -Force $output
$executable=Join-Path $output 'CodexReset.exe'
$sourceHashes=@{}
foreach($file in $payload){$sourceHashes[$file]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $file) -Algorithm SHA256).Hash}
$icon=Join-Path $output 'CodexReset.ico'
# Generate multi-resolution ICO artwork from geometry; no external/user files are used.
$images=@()
foreach($size in @(16,24,32,48,64,128,256)){
    $bitmap=New-Object Drawing.Bitmap($size,$size)
    $g=[Drawing.Graphics]::FromImage($bitmap);$g.SmoothingMode='AntiAlias';$g.Clear([Drawing.Color]::Transparent)
    $bg=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#0B1210'))
    $g.FillEllipse($bg,0,0,$size-1,$size-1);$bg.Dispose()
    for($i=0;$i -lt 12;$i++){
        $color=[Drawing.ColorTranslator]::FromHtml($(if($i -lt 9){'#43EF8B'}else{'#294438'}))
        $pen=New-Object Drawing.Pen($color,([single]($size*.085)));$pen.StartCap='Round';$pen.EndCap='Round'
        $g.DrawArc($pen,[single]($size*.18),[single]($size*.18),[single]($size*.64),[single]($size*.64),[single](-90+$i*30),[single]17);$pen.Dispose()
    }
    $g.Dispose();$stream=New-Object IO.MemoryStream
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$images+=,@{Size=$size;Bytes=$stream.ToArray()};$stream.Dispose();$bitmap.Dispose()
}
$stream=[IO.File]::Create($icon);$writer=New-Object IO.BinaryWriter($stream)
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach($item in $images){$edge=if($item.Size -eq 256){0}else{$item.Size};$writer.Write([byte]$edge);$writer.Write([byte]$edge);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$item.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$item.Bytes.Length}
    foreach($item in $images){$writer.Write([byte[]]$item.Bytes)}
}finally{$writer.Dispose();$stream.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$arguments=@('/nologo','/target:winexe','/optimize+','/debug-','/warnaserror+','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Net.Http.dll',('/win32icon:'+$icon),('/out:'+(Join-Path $output 'CodexReset.exe')))
foreach($file in $payload){$arguments+=(Join-Path $PSScriptRoot $file)}
& $compiler $arguments
if($LASTEXITCODE -ne 0){throw 'EXE 编译失败'}
# Read the actual output without locking it; reject stale launcher builds.
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($executable))
if(@($assembly.GetManifestResourceNames()).Count -ne 0){throw '原生 EXE 不应嵌入源码或用户数据资源'}
if($null -eq $assembly.GetType('NativeApp') -or $null -eq $assembly.GetType('NativeData')){throw 'EXE 缺少原生业务源码，禁止使用旧启动器产物'}
$references=@($assembly.GetReferencedAssemblies() | ForEach-Object {$_.Name})
if('System.Management.Automation' -in $references){throw '原生 EXE 不应引用 PowerShell'}
foreach($file in $payload){if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $file) -Algorithm SHA256).Hash -ne $sourceHashes[$file]){throw '构建期间源码发生变化，请重新审阅并构建'}}
'PASS: native WinForms EXE; no embedded source/user payload; no PowerShell runtime reference'
'EXE SHA256: '+(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
