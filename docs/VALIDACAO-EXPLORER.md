# Validação manual pendente — Windows 10 x64

Esta lista não foi executada nesta entrega. Compilar o MSI não equivale a instalar ou validar a extensão no Explorer. Executar em máquina/VM Windows 10 x64 autorizada, com usuário comum e pré-requisitos preparados.

1. Registrar estado de HKCU/Software/Classes/CLSID e do handler FileTools; se o CLSID exclusivo já pertencer a outra instalação não gerenciada, não prosseguir.
2. Instalar o MSI como usuário comum e confirmar o destino LocalAppData/Programs/FileTools. Confirmar que não alterou associações de arquivos, FileConverter anterior, Run/Startup, HKLM ou serviços.
3. Selecionar 2 e 10 PDFs com Ctrl e Shift, incluindo nomes acentuados e pastas com espaços. Executar “Unir PDFs” e conferir ordem natural, contagem e conteúdo de cada página.
4. Testar várias invocações, seleção mista (PDF/imagens), arquivo único, ausência de presets, Unicode, caminhos longos, diretórios de rede e pastas distintas quando uma janela do shell permitir seleção de origens distintas.
5. Verificar que comandos incompatíveis desaparecem, que há um único submenu FileTools e que o ícone aparece.
6. Conferir nome padrão, conflitos (2)/(3), originais intactos, saída no diretório da primeira entrada e fallback de destino com pasta sem escrita.
7. Dividir por página, por grupos 1-5;6-10 e extrair 1,3,5. Verificar PDFs danificados, protegidos por senha, grande volume, disco cheio e dependência ausente.
8. Converter JPG/PNG/WebP/BMP/TIFF/GIF, cancelando um lote longo. Verificar primeira página/quadro e ausência de arquivos parciais.
9. Confirmar responsividade do Explorer, processamento pesado em FileTools.exe/qpdf.exe, serialização entre invocações e saída de todos os processos após encerrar a notificação.
10. Validar tema, reordenação, intervalos, progresso, seleção de destino, notificações e abertura de arquivo/pasta.
11. Desinstalar por Aplicativos e Recursos. Confirmar remoção dos arquivos instalados, handler FileTools e CLSID exclusivo; confirmar preservação de resultados e integrações anteriores.
12. Testar reinstalação/reparo, atualização e remoção com explorer.exe mantendo a DLL em memória. O Windows Installer pode solicitar encerramento de aplicativos/sessão. Não matar o Explorer automaticamente.

Registrar versão exata do Windows, resultados, horário e problemas. Somente depois declarar instalação e integração interativa testadas.
