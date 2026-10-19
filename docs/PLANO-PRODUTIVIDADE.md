# MVP de produtividade — revisão de prioridades

Data: 19/10/2026. Branch: feature/filetools-mvp. Este plano substitui o adiamento do editor nas fases anteriores.

## Ordem incremental

1. Conversões locais: JPG/PNG → PDF individual/unificado com PDFsharp; PDF → JPG/PNG com PDFium; DOC/DOCX → PDF e PDF → DOCX via LibreOffice. A dependência ausente é erro informativo; PDF → DOCX exige aviso de fidelidade e não promete OCR.
2. União inteligente: CF_HDROP preserva a seleção completa, porém não há contrato de histórico de cliques no IDataObject/IShellItemArray. Iniciar em ordem natural; mostrar nomes e páginas; arraste, Enter e botão para confirmar. O destino continua sendo a pasta da primeira entrada recebida antes da ordenação.
3. Editor WPF integrado: preview real PDFium, miniaturas, texto/imagens/assinatura visual, anotações, AcroForms suportados, organização de páginas, desfazer/refazer, atalhos e impressão. Texto original não é reescrito. Assinatura visual não é assinatura digital.
4. Recuperação e salvamento: diário local com cópia imutável das fontes; novo arquivo por padrão. Ctrl+S reutiliza a saída criada pelo editor; sobrescrita de original exige confirmação e backup. Detectar alterações externas antes de substituir um arquivo. Salvar como usa diálogo único.
5. Impressão: diálogo do Windows; lote em um trabalho somente após conferência, sem mudar impressora padrão. Verificação física depende de impressora e autorização do usuário.
6. Regressão e pacote: suíte existente + PDFsharp/PDFium reais, conversões, snapshots, recuperação, anotações, formulários, ordenação e cancelamento. Não registrar COM nem instalar dependências no sistema de desenvolvimento.

## Motores e limites

- qpdf 12.4.1: operações estruturais existentes, preservadas.
- PDFsharp-WPF 6.2.4: MIT, https://github.com/empira/PDFsharp. Somente no worker.
- PDFium 158.0.8111 x64 sem V8/XFA: BSD e avisos de terceiros, distribuição bblanchon/pdfium-binaries, hash fixo. Somente no worker.
- LibreOffice: externo, MPL-2.0/LGPL, não redistribuído nem instalado automaticamente. Ausente neste ambiente no início da implementação. Sucesso real com DOC/DOCX deve ser testado onde o motor estiver disponível.
- Documentos protegidos, XFA, assinatura digital e edição de texto original não serão anunciados como suportados pelo editor básico.
- A integração no Explorer permanece SharpShell + manifesto Unicode; nenhum motor é carregado dentro de explorer.exe.
- Instalação/desinstalação, Ctrl/Shift reais no Explorer e impressão física permanecem validações manuais até autorização explícita.

## Referências primárias

- IShellItemArray: https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellitemarray
- PDFsharp e .NET Framework: https://docs.pdfsharp.net/General/Overview/Whats-New.html
- PDFium C API: https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/8111/public/
- LibreOffice CLI: https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html

## Estado entregue e próximos incrementos

Os seis incrementos acima têm implementação no MVP 0.3.0. A suíte tem 57 testes aprovados, incluindo motores PDF reais e abertura/preview/Salvar na janela WPF. O adaptador LibreOffice foi testado com processo simulado; a conversão real depende da instalação externa. Instalação COM, gestos completos e impressão permanecem no roteiro manual. A evidência não equivale à aprovação dos cinco critérios em uma máquina com a extensão instalada.

O editor usa PDFsharp-WPF 6.2.4 MIT. A compressão final fica com qpdf, evitando o trailer Flate incompleto dessa versão; não há implementação de conversor/compressor próprio. PDFium e avisos são fixados por versão/hash. Manter os motores fora do Explorer é requisito de qualquer incremento futuro.

Próximos incrementos: concluir matriz manual Windows/LibreOffice/impressoras/formulários; medir latência/memória com documentos grandes; ampliar fidelidade de formulários/CropBox e manipulação de anotações; somente depois avançar para OCR, edição de conteúdo ou ferramentas de arquivos. Não anunciar edição do texto original ou assinatura digital.
