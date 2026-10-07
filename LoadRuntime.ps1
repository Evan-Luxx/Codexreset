# Cache compiled helpers by their source content; source edits always rebuild.
function Import-ResetRuntime {
    Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Net.Http,System.Web.Extensions
    $paths=@((Join-Path $PSScriptRoot 'ResetPopup.cs'),(Join-Path $PSScriptRoot 'CodexQuotaClient.cs'),(Join-Path $PSScriptRoot 'NewsPopup.cs'))
    $source=($paths | ForEach-Object {[IO.File]::ReadAllText($_)}) -join "`n"
    $hash=[Security.Cryptography.SHA256]::Create()
    try{$key=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($source+[Environment]::Version.ToString())))).Replace('-','')}finally{$hash.Dispose()}
    $directory=Join-Path $PSScriptRoot '.runtime'
    $null=New-Item -ItemType Directory -Force -Path $directory
    $assembly=Join-Path $directory ($key+'.dll')
    if(Test-Path $assembly){try{Add-Type -Path $assembly;return}catch{Remove-Item -LiteralPath $assembly -Force}}
    $temporary=Join-Path $directory ([Guid]::NewGuid().ToString()+'.dll')
    try{
        Add-Type -Path $paths -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions -OutputAssembly $temporary
        Move-Item -LiteralPath $temporary -Destination $assembly -Force
        Add-Type -Path $assembly
    }finally{if(Test-Path $temporary){Remove-Item -LiteralPath $temporary -Force}}
}
