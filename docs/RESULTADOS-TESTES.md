# Resultados reais — MVP 0.3.0

Ambiente: Windows 10 x64 build 19045; .NET Framework 4.8; SDK 10.0.402; qpdf 12.4.1; Magick.NET 15.0.0; PDFsharp-WPF 6.2.4; PDFium 158.0.8111 x64 sem V8/XFA.

`scripts/Build.ps1 -Test`: **57 aprovados, 0 falhos**. Compilação sem erros/avisos. Log bruto em `TestResults/results.txt` (gerado, não versionado).

## Cobertura executada

- Os 36 testes anteriores: união de 2/10 PDFs, ordem/conteúdo, hashes, divisão/extração, conflitos e concorrência, ACL real sem escrita, Unicode/espaços, caminhos >260 caracteres, PDF corrompido e 32 MiB, cancelamento, limpeza, seleção completa por manifesto e `IShellExtInit`/`CF_HDROP` sintético, imagens PNG/JPG/WebP/BMP/TIFF/GIF, fila/mutex.
- PDFium real: abrir, contar e renderizar pixels de conteúdo, nomes Unicode, limite de pixels e documento inválido.
- Todas as páginas PDF → PNG/JPG, conteúdo rasterizado, preservação e cancelamento com limpeza do documento corrente.
- JPG/PNG → PDF individual/unido: contagem real, ordem natural por cor renderizada e hashes dos originais.
- Editor: texto realmente acrescentado e renderizado; arquivo final passa em `qpdf --check` sem avisos. Imagem/assinatura visual, `/Highlight` e `/Text` persistidos.
- Reorganização de várias páginas, inserção, remoção, rotação e bloqueio de exclusão da última página; conteúdo/ordem conferidos por qpdf. Sobreposições conferidas por pixels em 0/90/180/270° em página retangular.
- Desfazer/refazer, serialização/recuperação, fallback para diário `.bak`, salvamento por cópia, confirmação para original, backup íntegro e bloqueio após alteração externa; cancelamento sem tocar no original.
- AcroForm sintético real com texto, checkbox e combo: valores reabertos, aparência renderizada e validação qpdf.
- Janela WPF real aberta: carregamento assíncrono, preview PDFium, botão Salvar, publicação de arquivo legível e fechamento/limpeza. O teste aciona o controle programaticamente; não simula todos os gestos/atalhos do usuário.
- Paginador Windows produz visual real e respeita limites, **sem enviar trabalho a uma impressora**.
- Compatibilidade do menu e movimentação do modelo de ordem.
- Adaptador LibreOffice com processo simulado: filtros, perfil isolado, saída, limpeza e preservação. Cancelamento de processo externo real que cria um filho verifica encerramento de ambos pelo Windows Job.

## Limites da evidência

**Não executados:** conversões reais DOC/DOCX/PDF→DOCX (LibreOffice ausente), instalação/desinstalação COM, cliques/Ctrl/Shift no Explorer, drag and drop/atalhos completos do editor, diálogo Windows/saída física de impressão, atualização do MSI instalado. O roteiro está em [VALIDACAO-EXPLORER.md](VALIDACAO-EXPLORER.md).

Não foram medidos latência/memória em produção, arquivos de vários GiB, todos os formulários/fontes/CropBoxes, desempenho em rede ou políticas corporativas. Um formulário sintético não demonstra fidelidade universal de AcroForms.

A validação encontrou e corrigiu chamadas indevidas da API de páginas/formulários, caminhos estendidos no GDI+, e trailers Flate incompletos do PDFsharp 6.2.4. A escrita usa a variante WPF e delega a compressão final ao qpdf com seu transporte UTF-8 por arquivo de argumentos. Os testes verificam o resultado final sem relaxar avisos do qpdf. A versão 6.5.0 foi descartada por mudança de licença; o lock fixa a 6.2.4 MIT.

O MSI é gerado e aberto somente em leitura por `scripts/Inspect-Msi.ps1`: handler/CLSID exclusivo, identidade COM, payload (incluindo PDFium/PDFsharp), pré-requisitos e gravações exclusivamente HKCU. A única custom action permitida é o cálculo de propriedade do NetFx (tipo 51). Essa inspeção não instala o produto.

Não foi modificado o Registro para esta entrega. A extensão permanece sem registro. A limpeza anterior do certificado HTTPS gerado pelo primeiro uso do SDK foi preservada; os scripts desativam sua geração e alterações do PATH.
