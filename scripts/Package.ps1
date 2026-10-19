param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $root '.tools\packages'
if (Test-Path -LiteralPath (Join-Path $root '.tools\dotnet\dotnet.exe')) { $Dotnet = Join-Path $root '.tools\dotnet\dotnet.exe' }
$qpdf = Join-Path $root '.tools\qpdf'
if (-not (Test-Path -LiteralPath (Join-Path $qpdf 'bin\qpdf.exe'))) { throw 'Execute scripts/Get-Qpdf.ps1 primeiro.' }
& (Join-Path $PSScriptRoot 'Collect-Licenses.ps1')
# Unique staging directory avoids deleting or mixing prior packages.
$package = Join-Path $root ('artifacts\package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $package -Force | Out-Null
$app = Join-Path $root 'Application\FileTools\bin\x64\Release\net48'
$shell = Join-Path $root 'Application\FileConverterExtension\bin\x64\Release\net48'
foreach ($folder in @($app, $shell)) {
    Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Extension -in @('.dll','.exe','.config') -and $_.Name -notmatch 'Magick.Native-Q16-(x86|arm64)' } | Copy-Item -Destination $package -Force
}
Copy-Item -LiteralPath (Join-Path $root 'Resources\FileTools.ico') -Destination $package
foreach ($name in @('README.md','LICENSE.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package }
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $package -Recurse
$tools = Join-Path $package 'Tools\qpdf'
New-Item -ItemType Directory -Path $tools -Force | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $tools 'bin') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $qpdf 'bin') -File | Where-Object { $_.Name -in @('qpdf.exe','qpdf30.dll','fix-qdf.exe','zlib-flate.exe') } | Copy-Item -Destination (Join-Path $tools 'bin')
$qpdfDocs = Join-Path $tools 'share\doc\qpdf'
New-Item -ItemType Directory -Force -Path $qpdfDocs | Out-Null
Copy-Item -LiteralPath (Join-Path $qpdf 'share\doc\qpdf\qpdf-manual.pdf') -Destination $qpdfDocs
Copy-Item -LiteralPath (Join-Path $qpdf 'share\doc\qpdf\manual-html\_sources\license.rst.txt') -Destination $qpdfDocs
Copy-Item -LiteralPath (Join-Path $qpdf 'ORIGIN.txt') -Destination $tools
# Save the exact source used for this build alongside the MSI (GPL corresponding source of FileTools).
$source = Join-Path $root ('artifacts\source-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $source | Out-Null
foreach ($name in @('Application','Tests','scripts','Installer','Resources','docs','licenses')) {
    $target = Join-Path $source $name
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $base = Join-Path $root $name
    Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
        $relative = $_.FullName.Substring($base.Length + 1)
        $destination = Join-Path $target $relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
}
foreach ($name in @('FileTools.sln','Directory.Build.props','README.md','LICENSE.md','THIRD-PARTY-NOTICES.md','.gitignore')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $source -Force }
# Original middleware remains in the repository; include it in corresponding source for the preserved legacy build.
Copy-Item -LiteralPath (Join-Path $root 'Middleware') -Destination $source -Recurse -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip = Join-Path $root 'artifacts\FileTools-0.3.0-source.zip'
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$false)
# Generate explicit per-user components, each with an HKCU key path for correct MSI repair.
$ns = 'http://wixtoolset.org/schemas/v4/wxs'
$doc = New-Object Xml.XmlDocument
$doc.LoadXml('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><DirectoryRef Id="INSTALLFOLDER" /><ComponentGroup Id="Payload" /></Fragment></Wix>')
$manager = New-Object Xml.XmlNamespaceManager($doc.NameTable); $manager.AddNamespace('w',$ns)
$directoryRoot = $doc.SelectSingleNode('//w:DirectoryRef',$manager)
$componentGroup = $doc.SelectSingleNode('//w:ComponentGroup',$manager)
$directories = @{ '' = $directoryRoot }
$sha = [Security.Cryptography.SHA256]::Create()
function StableId([string]$prefix,[string]$text) {
    return $prefix + ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text.ToLowerInvariant())))).Replace('-','').Substring(0,24)
}
function StableGuid([string]$text) {
    $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('FileTools/x64/' + $text.ToLowerInvariant()))
    $bytes = [byte[]]$hash[0..15]
    $bytes[7] = ($bytes[7] -band 15) -bor 80
    $bytes[8] = ($bytes[8] -band 63) -bor 128
    return [Guid]::new($bytes).ToString('B')
}
function Element([string]$name,[hashtable]$attributes) {
    $element = $doc.CreateElement($name,$ns)
    foreach ($key in $attributes.Keys) { $element.SetAttribute($key,[string]$attributes[$key]) }
    return ,$element
}
Get-ChildItem -LiteralPath $package -Recurse -Directory | Sort-Object { $_.FullName.Length } | ForEach-Object {
    $relative = $_.FullName.Substring($package.Length+1)
    $parent = [IO.Path]::GetDirectoryName($relative); if ($null -eq $parent) { $parent = '' }
    $directory = Element 'Directory' @{Id=(StableId 'D' $relative);Name=$_.Name}
    [void]$directories[$parent].AppendChild($directory); $directories[$relative] = $directory
    $cleanupId = StableId 'Cleanup' $relative
    $cleanup = Element 'Component' @{Id=$cleanupId;Guid=(StableGuid ('directory/' + $relative))}
    [void]$cleanup.AppendChild((Element 'RegistryValue' @{Root='HKCU';Key='Software\FileTools\Components';Name=$cleanupId;Type='integer';Value='1';KeyPath='yes'}))
    [void]$cleanup.AppendChild((Element 'RemoveFolder' @{Id=(StableId 'Remove' $relative);On='uninstall'}))
    [void]$directory.AppendChild($cleanup)
    [void]$componentGroup.AppendChild((Element 'ComponentRef' @{Id=$cleanupId}))
}
Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($package.Length+1)
    $parent = [IO.Path]::GetDirectoryName($relative); if ($null -eq $parent) { $parent = '' }
    $id = StableId 'C' $relative
    $component = Element 'Component' @{Id=$id;Guid=(StableGuid $relative)}
    $fileAttributes = @{Id=(StableId 'F' $relative);Source=$_.FullName;KeyPath='no'}
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).FileVersion
    if ($version -and $version -notmatch '^0\.0\.0\.0') { $fileAttributes.DefaultLanguage = '0' }
    [void]$component.AppendChild((Element 'File' $fileAttributes))
    [void]$component.AppendChild((Element 'RegistryValue' @{Root='HKCU';Key='Software\FileTools\Components';Name=$id;Type='integer';Value='1';KeyPath='yes'}))
    if ($parent -ne '') { [void]$component.AppendChild((Element 'RemoveFolder' @{Id=(StableId 'R' $relative);On='uninstall'})) }
    [void]$directories[$parent].AppendChild($component)
    [void]$componentGroup.AppendChild((Element 'ComponentRef' @{Id=$id}))
}
$sha.Dispose()
$doc.Save((Join-Path $root 'artifacts\Payload.wxs'))
$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $package 'FileConverterExtension.dll')).FullName.Replace(',', '%2c')
& $Dotnet build Installer\FileTools.wixproj -c Release -p:Platform=x64 "-p:DefineConstants=ShellAssembly=$assembly" --nologo 2>&1 | Out-File -LiteralPath (Join-Path $root '.tools\msi-build.log') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $root '.tools\msi-build.log') -Tail 18
if ($LASTEXITCODE -ne 0) { throw 'Falha na geracao do MSI.' }
$installer = Join-Path $root 'artifacts\installer\FileTools-0.3.0-win-x64.msi'
Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Format-List
Write-Output "Instalador: $installer"
Write-Output "Codigo-fonte: $sourceZip"
Write-Output 'MSI gerado, sem executar instalacao nem alterar o Registro.'
