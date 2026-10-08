$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$payload=@('CodexReset.ps1','ResetPopup.cs','NewsPopup.cs','CodexQuotaClient.cs','Quota.ps1','Source.ps1','QuotaUi.ps1','LoadRuntime.ps1')
$review=@($payload)+@('Launcher.cs','Build-Exe.ps1','README.md','AGENTS.md','agent.md','Assert-UploadPrivacy.ps1')
& (Join-Path $PSScriptRoot 'Assert-UploadPrivacy.ps1') -Files $review
$output=Join-Path $PSScriptRoot 'releases'
$null=New-Item -ItemType Directory -Force $output
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
$arguments=@('/nologo','/target:winexe','/optimize+','/reference:System.Windows.Forms.dll',('/win32icon:'+$icon),('/out:'+(Join-Path $output 'CodexReset.exe')))
foreach($file in $payload){$arguments+=('/resource:'+(Join-Path $PSScriptRoot $file)+',payload.'+$file)}
$arguments+=(Join-Path $PSScriptRoot 'Launcher.cs')
& $compiler $arguments
if($LASTEXITCODE -ne 0){throw 'EXE 编译失败'}
# Inspect the actual compiled payload, rather than trusting a source-directory scan alone.
$assembly=[Reflection.Assembly]::LoadFile((Join-Path $output 'CodexReset.exe'))
$resources=@($assembly.GetManifestResourceNames())
if($resources.Count -ne $payload.Count){throw 'EXE 资源数量不符合发布白名单'}
foreach($file in $payload){
    $name='payload.'+$file
    if($name -notin $resources){throw 'EXE 存在非预期资源或缺少源码'}
    $input=$assembly.GetManifestResourceStream($name);$hash=[Security.Cryptography.SHA256]::Create()
    try{
        $embedded=[BitConverter]::ToString($hash.ComputeHash($input));$source=[IO.File]::OpenRead((Join-Path $PSScriptRoot $file))
        try{$reviewed=[BitConverter]::ToString($hash.ComputeHash($source))}finally{$source.Dispose()}
        if($embedded -ne $reviewed){throw 'EXE 内嵌内容与审阅源码不一致'}
    }finally{$input.Dispose();$hash.Dispose()}
}
'PASS: actual EXE contains exactly the reviewed source resources; no user runtime files'
'PASS: standalone launcher built from reviewed source payload only'
