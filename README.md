# FileTools

Ferramentas locais para Windows 10 x64 pelo menu de contexto do Explorador. Derivado de [FileConverter](https://github.com/fesalvian/FileConverter), preservando C#, .NET Framework 4.8, WPF, SharpShell, Magick.NET e o build legado.

**MVP 0.3.0 — produtividade e editor básico.** Prioridades atualizadas em [docs/PLANO-PRODUTIVIDADE.md](docs/PLANO-PRODUTIVIDADE.md). Os testes exercitam motores reais e a abertura/preview/botão Salvar do editor. Instalação COM no Explorer, gestos/atalhos completos, impressão e conversão real por LibreOffice ainda exigem validação manual.

## Operações

| Seleção | Menu FileTools | Resultado |
|---|---|---|
| Dois ou mais PDFs | Unir PDFs… | Janela compacta com nomes, páginas, arraste, ↑/↓ e Enter; novo PDF na origem |
| Um PDF | Editar PDF | Editor integrado, sem janela de inicialização |
| Um ou vários PDFs | Imprimir PDF… | Diálogo de impressão do Windows; lote exige conferência prévia |
| DOC/DOCX | Converter DOC/DOCX para PDF | Conversão direta pelo LibreOffice instalado localmente |
| JPG/JPEG/PNG | Imagens para PDF único | Um PDF, imagens em ordem natural por nome |
| JPG/JPEG/PNG | Imagens → Cada imagem para um PDF | Um PDF por imagem |
| PDFs | PDF → Todas as páginas para PNG/JPG | Todas as páginas, 200 dpi com limite de 24 milhões de pixels, subpasta por documento |
| PDFs | PDF → Converter para DOCX editável… | LibreOffice Writer PDF import; aviso de fidelidade antes do lote |
| PDFs | PDF → Separar / Dividir por intervalos | Páginas individuais, grupos `1-5;6-10` ou extração `1,3,5` |
| Imagens | Imagens → PNG/JPG/WebP | Conversão em lote; JPG↔PNG incluído |

As conversões preservam os originais. Resultados vão para a pasta da primeira entrada recebida antes de ordenar. Nomes existentes recebem `(2)`, `(3)` etc.; divisões/exportações usam subpastas exclusivas. Só há escolha de destino quando falta permissão. Em seleção de origens distintas, o primeiro diretório recebido é o destino do lote.

A Shell API entrega todos os caminhos, mas não documenta o histórico de cliques. Por isso a união começa por **ordem natural dos nomes** e sempre permite conferir e mudar a ordem. Não há promessa de recuperar a sequência dos cliques.

Entradas para conversão de imagens: JPG/JPEG, PNG, WebP, BMP, TIFF e GIF. TIFF/GIF usam somente o primeiro quadro. JPG/PNG → PDF usa páginas A4 com orientação conforme a imagem e margem de 18 pt.

## Editor integrado

- Preview real e miniaturas; texto sobreposto, imagens e assinatura visual a partir de PNG/JPEG.
- Destaques por área retangular e comentários inseridos como anotações PDF. Digite o comentário/texto no painel e clique na página; arraste a área para destacar.
- Preenchimento de campos AcroForm de texto, checkbox e lista suspensa, no painel lateral. XFA não é suportado.
- Ctrl/Shift selecionam várias miniaturas; arraste para ordenar, gire, exclua ou insira páginas de outro PDF. Não é possível excluir a última página.
- Desfazer/refazer (até 50 ações), Ctrl+Z/Ctrl+Y; em caixas de texto, os atalhos mantêm a edição do controle.
- Botões visíveis **Salvar** e **Imprimir**; Ctrl+S, Ctrl+Shift+S e Ctrl+P. Fechar com alterações oferece Salvar/Descartar/Cancelar.

A edição é **visual e por anotações**: não reescreve o texto original. Assinatura visual não é assinatura digital. Destaques são áreas, sem seleção semântica de palavras. O painel guarda e edita campos suportados; formulários complexos podem exigir outro editor. Campos de PDFs inseridos não são incorporados ao formulário original.

No primeiro Ctrl+S, o editor cria `nome_editado.pdf` junto ao original; nos próximos, reutiliza essa saída. **Salvar como** abre um único diálogo. Escolher o original exige confirmação explícita na sessão; substituições usam commit atômico com backup `nome.filetools-backup-GUID.pdf`. Alterações externas no original/resultado previamente salvo bloqueiam a substituição e pedem Salvar como. Os backups ficam na origem até o usuário removê-los.

Cada ação aplicada é registrada em `%LOCALAPPDATA%/FileTools/Recovery/<GUID>`, com cópias das fontes e imagens. Após falha, reabra o PDF original e aceite restaurar. O diário possui uma cópia anterior para recuperação quando o atual estiver corrompido. Fechamento normal remove a sessão; interrupção abrupta pode deixar cópias locais de documentos nessa pasta. O histórico de desfazer não atravessa sessões. Conteúdo ainda não aplicado pelo painel pode precisar ser digitado novamente.

A impressão usa a versão atual do editor, inclusive antes de salvar. O diálogo permite impressora, propriedades e intervalo de páginas. O lote gera um trabalho com todos os PDFs conferidos; cancelar o diálogo não imprime. FileTools não muda a impressora padrão nem persiste configurações globais.

## Compilar, testar e gerar MSI

Para validar antes de gerar o instalador, siga [Testar localmente, sem registrar a extensão](docs/TESTAR-LOCALMENTE.md).

Requer Windows x64 e SDK .NET para Windows (validado com 10.0.402). Use um SDK instalado ou `.tools/dotnet/dotnet.exe`. Referências net48 e WiX 5.0.2 vêm do NuGet. Na raiz:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Get-Qpdf.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Get-Pdfium.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Test -Package
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Inspect-Msi.ps1
```

O bypass só vale para o processo. Os scripts de download fixam versão e verificam SHA-256. A primeira compilação requer rede; o aplicativo opera offline. Nenhum desses comandos instala ou registra a extensão. `Inspect-Msi.ps1` abre o MSI somente em leitura.

Saídas: `artifacts/installer/FileTools-0.3.0-win-x64.msi`, `artifacts/FileTools-0.3.0-source.zip` e `TestResults/results.txt`. Evidências em [docs/RESULTADOS-TESTES.md](docs/RESULTADOS-TESTES.md). `FileTools.exe` fica em `Application/FileTools/bin/x64/Release/net48`; sem argumentos mostra orientações, e a extensão chama `--request <manifesto>`.

## Instalar e desinstalar — máquina autorizada

Pré-requisitos: Windows 10 x64, .NET Framework 4.8 e [Microsoft Visual C++ Redistributable v14 x64](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist). Para DOC/DOCX e PDF → DOCX, instale também [LibreOffice x64](https://www.libreoffice.org/download/download-libreoffice/) no diretório padrão `Program Files/LibreOffice`. O FileTools detecta `program/soffice.com` ou `.exe`; não instala esse motor automaticamente. Fontes ausentes no computador podem alterar o layout.

1. Abra `artifacts/installer/FileTools-0.3.0-win-x64.msi`.
2. A instalação grava em `%LOCALAPPDATA%/Programs/FileTools` e registra o CLSID exclusivo/handler em HKCU. **Isso altera o Registro; execute apenas com autorização.**
3. Feche e reabra o Explorador; se a DLL anterior continuar carregada, encerre a sessão e entre novamente.
4. Selecione arquivos com Ctrl/Shift e use FileTools no botão direito.
5. Desinstale por **Aplicativos e Recursos → FileTools → Desinstalar**. Documentos/resultados/backups e recuperações locais não são apagados pelo MSI.

Em terminal autorizado: `msiexec.exe /i "artifacts\installer\FileTools-0.3.0-win-x64.msi"`; desinstalação com `/x` e o mesmo MSI. Não executados neste computador. O pacote não muda associações, inicialização automática, serviços ou a instalação anterior do FileConverter. Políticas corporativas podem exigir implantação pela TI. Roteiro completo em [docs/VALIDACAO-EXPLORER.md](docs/VALIDACAO-EXPLORER.md).

## Arquitetura e limites

A extensão reaproveita SharpShell e entrega a seleção completa via manifesto Unicode. `FileTools.Core` contém políticas, transporte, saídas atômicas, qpdf, adaptador LibreOffice e execução cancelável em um Windows Job próprio. Somente o worker carrega PDFium, PDFsharp e Magick.NET. Nenhum motor é carregado dentro do Explorador. Não há serviço residente. Conversões mostram progresso após 700 ms, serializam por usuário e notificam conclusão discretamente; as sessões do editor são independentes.

PDFsharp-WPF 6.2.4 (MIT) escreve as alterações; qpdf finaliza a compressão de streams, evitando o trailer Flate incompleto emitido por essa versão do PDFsharp. PDFium 158.0.8111 x64, sem V8/XFA, renderiza preview/exportações/impressão. LibreOffice usa perfil temporário isolado; cancelamento encerra somente o conversor e seus filhos. Logs novos guardam apenas horário, tipo e código de erro.

- Sem OCR, desbloqueio por senha, edição do texto original, assinatura digital ou suporte XFA. PDF → DOCX tem fidelidade limitada, especialmente tabelas, fontes e digitalizações. A conversão pode falhar em vez de gerar um DOCX utilizável.
- Assinaturas digitais deixam de validar após transformações. Formulários, anexos, navegação e metadados globais não têm garantia de preservação ao combinar páginas/documentos.
- Cancelamento de lote preserva itens concluídos; exportação/divisão desfaz as saídas do documento corrente. Renderização de uma página não é interrompida no meio da chamada nativa. Falha abrupta pode deixar temporários `.filetools-*`.
- Caminhos longos usam prefixos estendidos sem mudar o Registro. Programas externos, impressão e LibreOffice podem ter limites próprios.
- Não foram medidos desempenho em produção nem arquivos de vários GiB. O editor pode ficar ocupado durante a regeneração do preview de PDFs grandes.
- MSI sem assinatura digital. Integração COM real, impressão, teclas/gestos completos e conversão real por LibreOffice permanecem pendentes de validação.
- Ferramentas de arquivos, ZIP, OCR, seleção semântica e edição de conteúdo original ficam para incrementos futuros.

Os fontes/build dos conversores legados permanecem em `Application/FileConverter`. O MSI não inclui o aplicativo legado, Ripper, yeti.mmedia, Markdown.Xaml, FFmpeg ou Ghostscript; a proveniência dos binários históricos precisa ser auditada antes da redistribuição. O submenu legado permanece disponível para uma distribuição completa anterior, com atualização automática desativada e exclusão de originais forçada para None.

## Licença

GPL-3.0-or-later, copyright e [LICENSE.md](LICENSE.md) preservados. Dependências e avisos em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e `licenses/`. Distribua o ZIP de fontes correspondente junto com o MSI.
