$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = Join-Path $root 'licenses\nuget'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$inventory = @{}
foreach ($name in @('FileTools','FileConverterExtension')) {
    $assets = Get-Content -LiteralPath (Join-Path $root "Application\$name\obj\project.assets.json") -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package' -or $library.Name -like 'Microsoft.NETFramework.ReferenceAssemblies*') { continue }
        $base = Join-Path $root ('.tools\packages\' + $library.Name.ToLowerInvariant().Replace('/','\'))
        $nuspec = Get-ChildItem -LiteralPath $base -Filter '*.nuspec' | Select-Object -First 1
        [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName
        $manager = New-Object Xml.XmlNamespaceManager($metadata.NameTable)
        $manager.AddNamespace('p',$metadata.DocumentElement.NamespaceURI)
        $node = $metadata.SelectSingleNode('/p:package/p:metadata',$manager)
        function Value([string]$field) { $item=$node.SelectSingleNode("p:$field",$manager); if($item){return $item.InnerText}; return '' }
        $folder = Join-Path $destination ($library.Name.Replace('/','-'))
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        Copy-Item -LiteralPath $nuspec.FullName -Destination $folder -Force
        Get-ChildItem -LiteralPath $base -File | Where-Object { $_.Name -match 'license|notice|copyright' } | Copy-Item -Destination $folder -Force
        $license = Value 'license'
        if (-not $license -and ($library.Name -like 'SharpShell/*' -or $library.Name -like 'System.*')) { $license = 'MIT' }
        $inventory[$library.Name] = [pscustomobject]@{ Package=$library.Name; License=$license; Copyright=(Value 'copyright'); Origin=(Value 'projectUrl'); RepositoryUrl=$(if($node.repository){$node.repository.GetAttribute("url")}else{""}); RepositoryCommit=$(if($node.repository){$node.repository.GetAttribute("commit")}else{""}) }
    }
}
$inventory.Values | Sort-Object Package | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'inventory.json') -Encoding UTF8
