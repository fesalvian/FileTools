$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = '158.0.8111'
$url = 'https://github.com/bblanchon/pdfium-binaries/releases/download/chromium/8111/pdfium-win-x64.tgz'
$expected = '3452d11031050ec65e57f180215b57b9e6d465e3fbedafa8a77ad90885c7d23e'
$archive = Join-Path $root '.tools\pdfium-8111-win-x64.tgz'
$target = Join-Path $root '.tools\pdfium'
New-Item -ItemType Directory -Force -Path $target | Out-Null
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Checksum PDFium incorreto.' }
& tar.exe -xzf $archive -C $target
if ($LASTEXITCODE -ne 0) { throw 'Falha ao extrair PDFium.' }
[IO.File]::WriteAllText((Join-Path $target 'ORIGIN.txt'), "PDFium $version x64 (sem V8/XFA). Source: $url" + [Environment]::NewLine + "SHA256: $expected" + [Environment]::NewLine + 'Licencas: LICENSE e licenses/ da distribuicao. Fontes: https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/8111')
Write-Output "PDFium $version preparado localmente, sem instalacao."
