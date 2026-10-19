# Resultados reais

Ambiente: Windows 10 x64, build 19045; .NET Framework 4.8/4.8.1 instalado; SDK local 10.0.402; qpdf 12.4.1 x64; Magick.NET 15.0.0.

Execução: scripts/Build.ps1 -Test. Resultado mais recente antes do empacotamento: **36 aprovados, 0 falhos**. O log bruto fica em TestResults/results.txt.

Cobertura automatizada:

- União real de 2 e 10 PDFs com contagem, conteúdo/ordem e hashes dos originais.
- Nomes acentuados/Unicode, pastas com espaços, diferentes diretórios e caminhos acima de 260 caracteres.
- Colisões com (2)/(3), publicação concorrente sem sobrescrita, pasta sem escrita com ACL Windows real.
- PDF corrompido e PDF com stream de 32 MiB.
- Cancelamento antes do trabalho, durante processo externo e durante produção parcial.
- Dependência ausente, avisos de qpdf, arquivo parcial/vazio, limpeza de temporários.
- Divisão individual, grupos 1-5;6-10, extração 1,3,5, reordenação e intervalos inválidos.
- Manifesto Unicode com 1.200 entradas, solicitações concorrentes, rejeição de manifestos externos e seleção inválida.
- Política do menu, snapshot completo e Shell API IShellExtInit com IDataObject/CF_HDROP múltiplo sintético em Windows real.
- Conversões reais PNG/JPG/WebP, lote e imagem corrompida.

A seleção sintética verifica a entrada da API da extensão, sem registrar COM nem executar dentro do explorer.exe. **Não foram executados instalação/desinstalação do MSI nem cliques/Ctrl/Shift no Explorer.** Também não foram medidos consumo de memória, tempos de abertura do menu em produção, arquivos de vários GiB ou comportamento em todas as políticas corporativas.

Falhas iniciais revelaram dependência ausente no diretório do harness, contexto de sincronização de controles WinForms no teste, restauração de ACL e limitação nativa de caminhos longos. Foram corrigidas, e a suíte foi repetida até passar integralmente. Não foram alteradas as políticas de caminhos longos nem o Registro para fazer os testes passarem.
- Verificação adicional: leitores JPG/WebP/BMP/TIFF/GIF, cancelamento de trabalho em espera e recuperação de mutex abandonado por término de uma thread simulando interrupção do worker.

Validação adicional do instalador: MSI gerado com WiX 5.0.2 e inspecionado em modo somente leitura por scripts/Inspect-Msi.ps1. Conferidos handler/CLSID exclusivos, identidade COM, arquivos obrigatórios e gravações de Registry exclusivamente em HKCU; não há execução de scripts/processos de instalação personalizados. A única ação adicional é a ação padrão tipo 51 do NetFx para calcular a propriedade do pré-requisito. ICE91 foi suprimido porque este produto é deliberadamente apenas por usuário; as verificações de componentes, registro e remoção de pastas permanecem habilitadas. Isso não equivale a testar instalação/desinstalação.

Limpeza do ambiente: o primeiro uso do SDK local criou automaticamente um certificado HTTPS de desenvolvimento ASP.NET. Esse certificado específico foi identificado por horário/hash e removido com sua chave privada; os demais certificados foram preservados. Os scripts finais desativam essa geração e a inclusão de ferramentas globais no PATH. A extensão FileTools permaneceu sem registro durante toda a validação.
