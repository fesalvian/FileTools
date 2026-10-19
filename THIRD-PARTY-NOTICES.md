# Dependências e avisos

O FileTools deriva do fork fesalvian/FileConverter, commit base 5dce6c0, de Adrien Allard e colaboradores. GPL-3.0-or-later; avisos originais mantidos. Arquivos modificados nesta derivação são identificáveis pelos commits e pelo plano técnico.

## Distribuídos no MSI do MVP

| Componente | Versão | Origem | Licença |
|---|---|---|---|
| SharpShell | 2.7.2 | NuGet SharpShell / github.com/dwmkerr/sharpshell | MIT |
| Magick.NET-Q16-AnyCPU e Magick.NET.Core | 15.0.0 | NuGet / github.com/dlemstra/Magick.NET, commit 79bb8700d7fe3a564f62926ab299fc2520cb2269 | Apache-2.0 |
| ImageMagick e codecs nativos | 7.1.2-33, 2026-10-10, conforme Notice.txt do Magick.NET | Inclusos na DLL nativa x64 do pacote anterior | ImageMagick License e licenças dos codecs em licenses/Magick.NET-Notice.txt |
| qpdf | 12.4.1 MSVC x64 | github.com/qpdf/qpdf/releases/tag/v12.4.1 | Apache-2.0; documentação e avisos do pacote preservados |
| System.Resources.Extensions e dependências Microsoft | versões exatas no inventário licenses/nuget | NuGet / repositórios oficiais indicados nos arquivos nuspec | MIT, exceto onde o nuspec indicar outra licença |

O qpdf foi baixado de https://github.com/qpdf/qpdf/releases/download/v12.4.1/qpdf-12.4.1-msvc64.zip.
SHA-256 conferido com o manifesto oficial: 3cd016cd433ef7232e42f4c13348a49cc14907a3c7278ef4f99120593126f7a6.
Somente os executáveis qpdf/fix-qdf/zlib-flate e qpdf30.dll acompanham o MVP; os runtimes Microsoft presentes no ZIP oficial não são incorporados ao MSI.

O inventário gerado registra ID, versão, licença, copyright e origem de cada pacote, junto dos nuspec e avisos incluídos. As bibliotecas permanecem sem modificação. As licenças Apache-2.0/MIT e ImageMagick permitem a combinação com GPLv3 observadas suas condições; o Notice.txt traz também as condições específicas dos codecs nativos. Fontes de qpdf: https://github.com/qpdf/qpdf/tree/v12.4.1. Fontes de Magick.NET: https://github.com/dlemstra/Magick.NET/tree/15.0.0. Fontes do SharpShell: https://github.com/dwmkerr/sharpshell.

## Pré-requisitos externos

- .NET Framework 4.8: componente Microsoft do sistema, não redistribuído no MSI.
- Microsoft Visual C++ Redistributable x64: runtime Microsoft, instalado previamente pelo usuário/TI sob os termos da Microsoft; não redistribuído. A consulta do instalador verifica a instalação do runtime v14 x64.
- NuGet, SDK .NET 10.0.402 e WiX 5.0.2 são ferramentas de build; o SDK não acompanha o aplicativo. WiX: https://github.com/wixtoolset/wix, MS-RL.

## Motores preservados no fork, fora do MSI novo

- FFmpeg: binário e GPL presentes em Middleware/ffmpeg; não acrescentado ao MSI. Determinar versão, opções de build e fontes correspondentes antes de distribuir este binário como FileTools.
- Ghostscript: binários herdados em Middleware/gs; AGPL/comercial. A revisão exata e o conjunto de avisos/fontes do binário precisam ser conferidos antes da distribuição.
- Markdown.Xaml.dll, Ripper.dll e yeti.mmedia.dll: binários herdados; referências históricas constam no README original do commit base. Proveniência/licença exatas ainda precisam de confirmação; não constam no MSI novo.
- Office/NetOffice, WpfAnimatedGif e bibliotecas de MVVM: permanecem no build legado, fora do MSI da fase 2.
- LibreOffice: avaliado para fase 3, não usado/distribuído.
- Nenhum novo motor não documentado foi incorporado ao instalador.

O ZIP de fontes preserva Middleware exatamente como veio do fork para reconstruir a aplicação legada. Isso não representa auditoria concluída desses binários históricos. Antes de redistribuir o build legado, concluir essa auditoria e fornecer os avisos e fontes exigidos.

Ao disponibilizar o MSI a terceiros, disponibilize simultaneamente o ZIP de fontes correspondente, instruções de build e todos os avisos. Não substituir o ZIP de fontes por um link apenas para o projeto original, pois ele não contém estas modificações.
