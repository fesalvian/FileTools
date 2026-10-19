// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileTools
{
    public sealed class DocumentConversion
    {
        private readonly string executable;
        private readonly IExternalProcess process;
        public DocumentConversion(string executable = null, IExternalProcess process = null)
        {
            this.executable = executable ?? FindLibreOffice();
            this.process = process ?? new ExternalProcess();
        }
        public static string FindLibreOffice()
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                foreach (string name in new[] { "soffice.com", "soffice.exe" })
                {
                    string path = Path.Combine(root,"LibreOffice","program",name);
                    if (File.Exists(path)) return path;
                }
            throw new FileNotFoundException("LibreOffice não encontrado. Instale o LibreOffice para converter DOC/DOCX e PDF → DOCX localmente. O FileTools não envia documentos à internet.");
        }
        public Task<string> ConvertAsync(string input, string directory, bool toDocx, CancellationToken token)
        {
            string extension = toDocx ? ".docx" : ".pdf";
            return new FileOutputManager().CreateAsync(directory,Path.GetFileNameWithoutExtension(input)+extension,async (temporary,ct) =>
            {
                string stage = Path.GetDirectoryName(temporary);
                string work = Path.Combine(stage,"libreoffice");
                string profile = Path.Combine(work,"profile");
                string sourceDir = Path.Combine(work,"input");
                Directory.CreateDirectory(sourceDir);
                string source = Path.Combine(sourceDir,"document"+Path.GetExtension(input));
                File.Copy(WindowsPaths.ForIO(input),source,false); // No macros/edits ever touch the original.
                var args = new System.Collections.Generic.List<string> {
                    "-env:UserInstallation="+new Uri(WindowsPaths.ForShell(profile)).AbsoluteUri, "--headless","--nologo","--nodefault","--norestore",
                    "--convert-to",toDocx ? "docx:Office Open XML Text" : "pdf:writer_pdf_Export","--outdir",work };
                if (toDocx) args.Add("--infilter=writer_pdf_import");
                args.Add(source);
                var result = await process.RunAsync(executable,args,ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                string generated = Path.Combine(work,"document"+extension);
                if (result.ExitCode != 0 || !File.Exists(generated) || new FileInfo(generated).Length == 0)
                    throw new InvalidDataException("O LibreOffice não conseguiu converter o documento. PDFs digitalizados não geram texto editável sem OCR; tabelas e fontes podem perder fidelidade.");
                File.Move(generated,temporary);
            },token);
        }
    }
}
