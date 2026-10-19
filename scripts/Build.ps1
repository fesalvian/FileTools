param([string]$Dotnet = 'dotnet', [switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $root
if (Test-Path -LiteralPath (Join-Path $root '.tools\dotnet\dotnet.exe')) { $Dotnet = Join-Path $root '.tools\dotnet\dotnet.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $root '.tools\packages'
& (Join-Path $PSScriptRoot 'New-Icon.ps1')
& $Dotnet build FileTools.sln -c Release -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
if ($Test) {
    $qpdfTarget = Join-Path $root 'Tests\FileTools.Tests\bin\x64\Release\net48\Tools\qpdf'
    New-Item -ItemType Directory -Path $qpdfTarget -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root '.tools\qpdf\bin') -Destination $qpdfTarget -Recurse -Force
    & (Join-Path $root 'Tests\FileTools.Tests\bin\x64\Release\net48\FileTools.Tests.exe') (Join-Path $root '.tools\qpdf\bin\qpdf.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes.' }
}
if ($Package) { & (Join-Path $PSScriptRoot 'Package.ps1') }
