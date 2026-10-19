# Fase 1 — análise e plano técnico

**Revisão vigente:** [PLANO-PRODUTIVIDADE.md](PLANO-PRODUTIVIDADE.md) antecipa editor, conversões Office/imagens/PDF e impressão para o MVP 0.3.0. O texto abaixo registra o plano inicial.

Base inspecionada: fork fesalvian/FileConverter, branch integration, commit 5dce6c0.
A pasta de trabalho estava vazia e recebeu um clone completo; o histórico e o copyright foram preservados.

## Componentes reaproveitados

- Application/FileConverter: aplicação WPF .NET Framework 4.8, presets XML, processamento assíncrono, progresso e cancelamento; conversores Magick.NET, FFmpeg, Office via NetOffice, extração de CD e localizações.
- Application/FileConverterExtension: COM SharpShell 2.7.2, IShellExtInit/IContextMenu. SelectedItemPaths já representa a seleção completa, sem depender de %1.
- Middleware: FFmpeg, Ghostscript e bibliotecas legadas. Installer: WiX 5 com instalação por máquina e ações que exigem elevação.
- Magick.NET-Q16-AnyCPU 14.10.2 já usado pelo fork será reaproveitado para imagens.
- Problemas identificados: inicialização da extensão depende de chave FileConverter; ausência de presets pode produzir referência nula; arquivo-lista temporário tem disputa de nomes e limpeza vinculada ao Explorer; serviço de atualização consulta a rede; conversões legadas permitem políticas destrutivas configuráveis.

## Decisões do MVP

1. Manter C#, WPF, net48 x64 e a extensão SharpShell. Adicionar um núcleo leve sem motores de imagem para ser carregado no Explorer; tarefas novas em FileTools.exe fora do Explorer.
2. Transportar seleção inteira em manifesto JSON Unicode de nome GUID, por usuário. Consumir e remover no worker; limpeza de solicitações antigas fora do Explorer.
3. Usar novo CLSID para coexistir com instalações do FileConverter. Reaproveitar código da extensão; não alterar associações, serviços ou inicialização automática.
4. Operações PDF estruturais com qpdf em processo cancelável, sem rasterização. União em ordem natural; destino sempre da primeira entrada recebida antes de ordenar. Opção de reordenação e escolha de grupos em janelas WPF pontuais.
5. Saída temporária no mesmo volume, movimento atômico sem sobrescrita e nomes numerados; subpastas exclusivas para divisões. Seleção de destino quando a origem não permite escrita.
6. Preservar todos os conversores legados e seus fontes. Compilar aplicação existente com SDK moderno; oferecer presets legados no mesmo submenu quando disponíveis. Desativar consulta automática de atualização e ações destrutivas no caminho FileTools.
7. Instalação por usuário via script auditável com registro COM em HKCU. Não executar scripts de instalação neste ambiente sem autorização explícita para o Registro.
8. Testes automatizados reais do núcleo, qpdf, imagens, transporte e menu gerenciado. Validação interativa Explorer/registro é checklist manual separado e não será anunciada como executada.

## Escopo

Fase 2: menu, seleção múltipla, união, divisão por página/grupos e extração, PNG/JPG/WebP em lote, salvamento, cancelamento, progresso, pacote e instalador por usuário.
Fases 3 e 4 permanecem futuras: compressão, LibreOffice, ferramentas de arquivos, editor e configurações avançadas. Nenhuma ferramenta dessas será anunciada como implementada.

## Atualização de segurança durante a fase 2

A auditoria NuGet identificou vulnerabilidades conhecidas na versão 14.10.2 herdada. A implementação final usa Magick.NET 15.0.0 / ImageMagick 7.1.2-33, com as chamadas AlphaOption/OrientationType migradas para a nova API. A compilação e os testes foram repetidos após a atualização; a auditoria permanece habilitada. Referência oficial: https://github.com/dlemstra/Magick.NET/releases/tag/15.0.0.
