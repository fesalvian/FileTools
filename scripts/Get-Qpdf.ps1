$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = '12.4.1'
$archive = Join-Path $root ".tools\qpdf-$version-msvc64.zip"
$hashes = Join-Path $root ".tools\qpdf-$version.sha256"
$base = "https://github.com/qpdf/qpdf/releases/download/v$version"
New-Item -ItemType Directory -Force -Path (Join-Path $root '.tools') | Out-Null
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -UseBasicParsing -Uri "$base/qpdf-$version-msvc64.zip" -OutFile $archive }
Invoke-WebRequest -UseBasicParsing -Uri "$base/qpdf-$version.sha256" -OutFile $hashes
$line = Get-Content -LiteralPath $hashes | Where-Object { $_ -match "qpdf-$([Regex]::Escape($version))-msvc64.zip$" } | Select-Object -First 1
if (-not $line) { throw 'Hash oficial do pacote ausente.' }
$expected = ($line -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw 'Hash do qpdf não corresponde ao manifesto oficial.' }
$expanded = Join-Path $root ".tools\qpdf-$version"
Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
$binary = Get-ChildItem -LiteralPath $expanded -Filter qpdf.exe -Recurse | Where-Object { $_.Directory.Name -eq 'bin' } | Select-Object -First 1
if (-not $binary) { throw 'qpdf.exe ausente.' }
$packageRoot = $binary.Directory.Parent.FullName
$target = Join-Path $root '.tools\qpdf'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $packageRoot '*') -Destination $target -Recurse -Force
[IO.File]::WriteAllText((Join-Path $target 'ORIGIN.txt'), "qpdf $version x64 MSVC\n$base/qpdf-$version-msvc64.zip\nSHA256 $actual\nLicenca Apache-2.0. Dependencias: consulte share/doc e licencas do pacote.")
& (Join-Path $target 'bin\qpdf.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'qpdf indisponível; verifique o runtime Visual C++ x64.' }
