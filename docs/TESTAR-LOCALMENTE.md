# Testar antes de gerar o instalador

Abra o **Windows PowerShell 64 bits** na raiz do projeto. Use arquivos de teste numa pasta como `C:\Testes FileTools`, com dois PDFs, um DOCX e duas imagens PNG/JPG.

Testar C# exige compilar executáveis locais de desenvolvimento. Os comandos abaixo não geram o MSI de distribuição nem registram a extensão no Windows. Os testes abrem e fecham uma janela própria do editor; não enviam trabalhos para a impressora.

## 1. Preparar e executar os testes automatizados

Requer SDK .NET para Windows, .NET Framework 4.8 e runtime Visual C++ v14 x64. Nesta pasta já há SDK/dependências locais em `.tools`; em um clone novo, instale um SDK .NET atual antes de executar. Os downloads são necessários só na primeira preparação; o aplicativo processa documentos offline.

```powershell
Set-Location 'C:\Users\Uninga\Desktop\FileTools'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-Qpdf.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-Pdfium.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Test
Get-Content .\TestResults\results.txt -Tail 5
```

A última execução validada teve **57 aprovados, 0 falhos**. O script termina com erro se um teste falhar. Não acrescente `-Package` nesta etapa.

## 2. Abrir as funções sem instalar o menu do Windows

Depois de `Build.ps1 -Test`, a pasta de testes contém o worker, suas bibliotecas e o qpdf. Ela permite exercitar a mesma operação chamada pelo menu, sem registrar COM. Cole este bloco no mesmo Windows PowerShell:

```powershell
$bin = (Resolve-Path '.\Tests\FileTools.Tests\bin\x64\Release\net48').Path
Add-Type -Path (Join-Path $bin 'FileTools.Core.dll')

function Testar-FileTools {
    param([string]$Operacao, [string[]]$Arquivos)
    $caminhos = [string[]]@($Arquivos | ForEach-Object {
        (Resolve-Path -LiteralPath $_ -ErrorAction Stop).Path
    })
    $pedido = [FileTools.SelectionTransport]::Write($Operacao, $caminhos)
    $argumentos = '--request ' + [FileTools.ExternalProcess]::Quote($pedido)
    $processo = Start-Process -FilePath (Join-Path $bin 'FileTools.exe') -ArgumentList $argumentos -Wait -PassThru
    Write-Host ('Codigo de saida: ' + $processo.ExitCode)
}
```

Execute uma operação por vez, trocando os nomes pelos seus arquivos:

```powershell
# Conferir ordem e unir dois PDFs.
Testar-FileTools 'pdf-merge' @('C:\Testes FileTools\doc10.pdf', 'C:\Testes FileTools\doc2.pdf')

# Abrir o editor diretamente.
Testar-FileTools 'pdf-edit' @('C:\Testes FileTools\doc2.pdf')

# Gerar um PDF com duas imagens.
Testar-FileTools 'images-pdf-merge' @('C:\Testes FileTools\foto1.jpg', 'C:\Testes FileTools\foto2.png')

# Exportar todas as paginas; repetir com pdf-jpg para JPG.
Testar-FileTools 'pdf-png' @('C:\Testes FileTools\doc2.pdf')

# Requer LibreOffice x64 instalado no caminho padrao.
Testar-FileTools 'document-pdf' @('C:\Testes FileTools\documento.docx')

# Abre o dialogo Windows; cancelar nao envia impressao.
Testar-FileTools 'pdf-print' @('C:\Testes FileTools\doc2.pdf')
```

Essas operações criam resultados na pasta de origem e dados temporários de solicitações/recuperação em `%LOCALAPPDATA%/FileTools`. Confirme o conteúdo dos arquivos gerados e compare os originais. Sem LibreOffice, DOC/DOCX e PDF→DOCX apresentam mensagem de dependência ausente; isso não é uma conversão validada.

## 3. Conferir o que importa

1. **União:** nomes e páginas corretos, ordem inicial doc2 antes de doc10, arrastar/Enter funcionando, resultado na origem. Repetir deve gerar `(2)` e preservar o primeiro resultado.
2. **Editor:** inserir texto, imagem, destaque/comentário; girar/reordenar páginas, desfazer/refazer. Ctrl+S deve criar `_editado.pdf`; o próximo Ctrl+S reutiliza essa saída. Reabrir o resultado e conferir. Testar Ctrl+Shift+S e o aviso ao fechar com alterações.
3. **Imagens/PDF:** verificar quantidade de páginas, cores, ordem e exportação de todas as páginas em PNG/JPG.
4. **DOCX:** com LibreOffice instalado, comparar fontes, imagens e tabelas no PDF. A conversão real ainda precisa ser validada neste ambiente.
5. **Impressão:** testar primeiro o cancelamento do diálogo. Só confirmar impressão quando quiser enviar um trabalho à impressora escolhida; conferir o PDF editado e o intervalo de páginas.

Esse caminho valida o worker. O botão direito real, instalação/desinstalação e atualização da extensão exigem instalação com gravações em HKCU, portanto ficam para uma etapa expressamente autorizada. Roteiro completo: [VALIDACAO-EXPLORER.md](VALIDACAO-EXPLORER.md).

Depois de aprovar os testes, `Build.ps1 -Test -Package` gera o MSI e o ZIP de fontes. O instalador continua sem execução automática.
