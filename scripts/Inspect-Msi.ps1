param([string]$Path = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Path) { $Path = Join-Path $root 'artifacts\installer\FileTools-0.3.0-win-x64.msi' }
$Path = [IO.Path]::GetFullPath($Path)
# OpenDatabase mode 0 is read-only. Never call InstallProduct or run msiexec here.
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($Path, 0)
function Query([string]$sql,[int]$columns) {
    $sql = [Regex]::Replace($sql, '\b(Root|Key|Name|Value|Registry|Property|_Tables|Action|Type|Source|Target|CustomAction|FileName|FileSize|File)\b', { param($match) [string][char]96 + $match.Value + [string][char]96 })
    $view = $database.OpenView($sql)
    [void]$view.Execute()
    try {
        while ($record = $view.Fetch()) {
            $values = @()
            for ($i=1;$i -le $columns;$i++) { $values += $record.StringData($i) }
            ,$values
        }
    } finally { [void]$view.Close() }
}
$registry = @(Query 'SELECT Root, Key, Name, Value FROM Registry' 4)
if ($registry.Count -lt 8) { throw 'Registro do shell incompleto.' }
if ($registry | Where-Object { $_[0] -ne '1' }) { throw 'O MSI grava fora de HKCU.' }
if (-not ($registry | Where-Object { $_[1] -eq 'Software\Classes\*\shellex\ContextMenuHandlers\FileTools' -and $_[3] -eq '{2776E742-1628-40AA-97F0-F7805F77DAB2}' })) { throw 'Handler incorreto.' }
$assembly = $registry | Where-Object { $_[2] -eq 'Assembly' } | Select-Object -First 1
if (-not $assembly -or $assembly[3] -match '%2c' -or $assembly[3] -notlike 'FileConverterExtension, Version=*') { throw 'Identidade COM inválida.' }
$properties = @(Query 'SELECT Property, Value FROM Property' 2)
if ($properties | Where-Object { $_[0] -eq 'ALLUSERS' -and $_[1] }) { throw 'Pacote não é estritamente por usuário.' }
$tables = @(Query 'SELECT Name FROM _Tables' 1)
if ($tables | Where-Object { $_[0] -eq 'CustomAction' }) {
    $actions = @(Query 'SELECT Action, Type, Source, Target FROM CustomAction' 4)
    $unexpected = $actions | Where-Object { $_[0] -ne 'SetWIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' -or $_[1] -ne '51' -or $_[2] -ne 'WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' -or $_[3] -ne '1' }
    if ($unexpected) { throw 'Custom actions inesperadas.' }
}
$files = @(Query 'SELECT FileName, FileSize FROM File' 2)
if ($files | Where-Object { $_[0] -match 'vcruntime|msvcp140|concrt140' }) { throw 'Runtime Microsoft foi incorporado indevidamente.' }
foreach ($name in @('FileTools.exe','FileTools.Core.dll','FileConverterExtension.dll','qpdf.exe','Magick.Native-Q16-x64.dll','pdfium.dll','PdfSharp-wpf.dll')) {
    if (-not ($files | Where-Object { ($_[0] -split '\|')[-1] -eq $name })) { throw "Arquivo ausente: $name" }
}
$worker = $files | Where-Object { ($_[0] -split '\|')[-1] -eq 'FileTools.exe' } | Select-Object -First 1
$current = Get-Item -LiteralPath (Join-Path $root 'Application\FileTools\bin\x64\Release\net48\FileTools.exe')
if ([long]$worker[1] -ne $current.Length) { throw 'MSI não corresponde ao executável atual.' }
Write-Output "PASS MSI em modo leitura: $($files.Count) arquivos; todas as gravações de Registro em HKCU; identidade COM, pré-requisitos e conteúdo conferidos."
Write-Output 'Instalação, desinstalação e ativação pelo Explorer continuam NÃO EXECUTADAS.'
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
