# FileTools

Ferramentas offline para Windows 10 x64 pelo menu de contexto do Explorador. Derivado de [FileConverter](https://github.com/fesalvian/FileConverter), preservando C#, .NET Framework 4.8, WPF, SharpShell e Magick.NET.

**Versão 0.2.0 — MVP da fase 2.** A análise está em [docs/PLANO-TECNICO.md](docs/PLANO-TECNICO.md). A suíte automatizada passou no Windows 10 build 19045; a instalação e o uso interativo no Explorer ainda precisam da validação descrita abaixo.

## O que está implementado

- FileTools → PDF → Unir PDFs; ordem natural dos nomes, com desempate pelo caminho. Há um comando opcional para escolher a ordem.
- Separar todas as páginas; dividir por intervalos como `1-5;6-10`; extrair páginas como `1,3,5`. Cada grupo vira um PDF. Em seleção de vários PDFs, os mesmos intervalos são aplicados a cada entrada.
- FileTools → Imagens → PNG, JPG e WebP em lote. Entradas JPG/JPEG, PNG, WebP, BMP, TIFF e GIF. Para TIFF/GIF, somente a primeira página/quadro; não há preservação de animação neste MVP.
- Originais preservados, saída junto ao primeiro arquivo recebido, nomes exclusivos e subpastas nas divisões. O destino é capturado antes da ordenação.
- Progresso após 700 ms, cancelamento, fila serial por usuário e notificação com acesso ao arquivo/pasta. O ícone de notificação dura oito segundos; não há serviço residente.
- Seleção completa por Shell API/SharpShell e manifesto JSON Unicode, inclusive quando a lista excede os limites da linha de comando.
- Tema claro/escuro conforme Windows nas janelas novas. Controles nativos mantêm o estilo do sistema.
- Instalador MSI x64 por usuário. Sem alteração de associações, inicialização automática ou integrações anteriores do FileConverter.

## Instalar e usar

Pré-requisitos: Windows 10 x64, .NET Framework 4.8 e [Microsoft Visual C++ Redistributable x64](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist). Podem ser preparados offline pela TI. O runtime Microsoft não é redistribuído no MSI.

1. Abra `artifacts/installer/FileTools-0.2.0-win-x64.msi`.
2. A instalação grava em `%LOCALAPPDATA%/Programs/FileTools` e registra apenas o CLSID próprio e o handler FileTools em HKCU.
3. Feche e abra uma janela do Explorador. Se o Windows mantiver a extensão antiga em memória, encerre a sessão e entre novamente.
4. Selecione arquivos com Ctrl/Shift, clique com o botão direito e escolha FileTools.
5. Para desinstalar, use Aplicativos e Recursos → FileTools → Desinstalar.

Na máquina autorizada para instalação, também é possível abrir o instalador pelo terminal:

```powershell
msiexec.exe /i "artifacts\installer\FileTools-0.2.0-win-x64.msi"
```

Para desinstalar pelo terminal, use `/x` com o mesmo MSI utilizado na instalação. Esses comandos alteram a integração do Windows; compilar, executar os testes automatizados e inspecionar o MSI não os executa.

Depois de instalar, valide o caminho principal: selecione dois PDFs com Ctrl/Shift, escolha **FileTools → PDF → Unir PDFs** e confira `Documentos_unificados.pdf` na pasta de origem. Repita mantendo esse resultado na pasta: o novo arquivo deve ser `Documentos_unificados (2).pdf`, preservando os PDFs originais. O checklist completo está em `docs/VALIDACAO-EXPLORER.md`.

A instalação **não foi executada no computador de desenvolvimento**. Políticas corporativas que proíbem COM por usuário ou extensões não aprovadas podem exigir implantação/autorização pela TI. Não há tentativa de contornar essas políticas. Consulte [docs/VALIDACAO-EXPLORER.md](docs/VALIDACAO-EXPLORER.md).

União produz `Documentos_unificados.pdf`, depois `Documentos_unificados (2).pdf` etc. Na divisão, a subpasta usa o nome do PDF com sufixo `_paginas`. Uma pasta sem permissão de escrita abre o seletor de destino. Nenhum arquivo é enviado à rede.

## Compilar, testar e gerar MSI

Instale um SDK .NET atual para Windows (a execução desta entrega usou 10.0.402) ou use um SDK local em `.tools/dotnet`. As referências net48 e o WiX 5.0.2 são restaurados pelo NuGet; Visual Studio não é obrigatório. A compilação inicial requer internet; os aplicativos instalados operam offline.

No PowerShell, na raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Get-Qpdf.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Test -Package
```

O bypass vale apenas para o processo; não muda a política persistente de execução. `Get-Qpdf.ps1` baixa o qpdf 12.4.1 MSVC x64 da origem oficial e confere o SHA-256 publicado. Não instala nem registra nada.

- `FileTools.sln`: núcleo, processo WPF, extensão, aplicação original e testes.
- `scripts/Build.ps1 -Test`: compila e executa testes reais; código de saída não zero se houver falha.
- `scripts/Package.ps1`: gera MSI, inventário de arquivos e ZIP de fontes; não executa o instalador.
- `scripts/Inspect-Msi.ps1`: confere o banco do MSI somente em leitura, sem instalar nem registrar a extensão.
- `TestResults/results.txt`: resultado da última execução. O relatório versionado está em [docs/RESULTADOS-TESTES.md](docs/RESULTADOS-TESTES.md).
- Saída de compilação: `Application/FileTools/bin/x64/Release/net48/FileTools.exe`.
- O executável é chamado com `--request <manifesto>`; executar sem argumentos mostra orientações. Não é uma janela principal residente.

## Arquitetura e preservação do fork

`FileTools.Core` contém FileSelection, transporte, Shell command policy, PdfOperations, FileOutputManager, BatchOperations e execução de processos. A extensão original foi adaptada, mantendo SharpShell. `FileTools` contém ImageConversion e as janelas WPF pontuais. Temas vêm do Windows, e os logs novos registram apenas tipo de erro, código e horário em `%LOCALAPPDATA%/FileTools/Logs`.

Os conversores originais (FFmpeg, Office, Ghostscript, presets e localizações) permanecem no código e são compilados em `Application/FileConverter/bin/x64/Release/net48`. O instalador do MVP contém somente os motores inventariados necessários à fase 2. Não inclui a aplicação legada nem redistribui Ripper, yeti.mmedia, Markdown.Xaml, FFmpeg ou Ghostscript, cujos binários históricos precisam de auditoria de proveniência/código-fonte antes de uma nova distribuição. Uma instalação anterior do FileConverter continua intacta.

O código da extensão conserva o submenu “Conversões existentes” para uma distribuição legada completa quando `FileConverter.exe` e `Settings.default.xml` estiverem ao lado dela. Esse caminho é separado do MVP: preserva os conversores anteriores, mas ainda não possui o novo commit atômico nem o mesmo conjunto de testes. A consulta automática de atualizações foi desativada, as configurações legadas ficam em FileTools/Legacy e a exclusão automática de originais é forçada para None.

## Limitações conhecidas

- Instalação/desinstalação, ativação COM pelo explorer.exe, Ctrl/Shift no Explorer e aparências precisam de validação manual. O teste da Shell usa um IDataObject/CF_HDROP sintético em Windows real e não substitui esse teste.
- PDFs criptografados por senha não têm diálogo de desbloqueio. PDFs com avisos de reparo do qpdf são recusados em vez de produzir resultado silenciosamente.
- União/divisão trabalha com páginas. Formulários interativos, anexos, marcadores de navegação e metadados globais não têm garantia de preservação ao combinar documentos; assinaturas digitais deixam de validar após qualquer transformação.
- Lotes são sequenciais. No cancelamento/falha, itens concluídos anteriormente permanecem; a divisão do PDF corrente desfaz apenas suas saídas. Interrupção abrupta do computador pode deixar diretórios temporários `.filetools-*` na origem e arquivos de argumentos temporários; o cancelamento normal faz a limpeza.
- O núcleo usa prefixos estendidos para caminhos longos sem mudar o Registro. A abertura em visualizadores externos depende do suporte do respectivo programa.
- A compressão de PDF, LibreOffice, conversão de PDF para imagens/TXT/DOCX, imagens para PDF, renomeação, organização, ZIP, extração de imagens e editor visual pertencem às fases 3/4 e ainda não foram implementados.
- MSI não assinado digitalmente. Não é uma versão declarada pronta para implantação corporativa antes da validação manual.

## Licença

GPL-3.0-or-later conforme o projeto original, com copyright e [LICENSE.md](LICENSE.md) preservados. Bibliotecas têm licenças próprias em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e `licenses/`. Distribua o ZIP de código-fonte correspondente junto com o MSI.
