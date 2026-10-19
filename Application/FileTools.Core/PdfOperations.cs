// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FileTools
{
    public sealed class PdfOperations
    {
        private readonly string executable;
        private readonly IExternalProcess runner;
        private readonly FileOutputManager output = new FileOutputManager();
        public PdfOperations(string executable, IExternalProcess runner = null)
        {
            this.executable = executable;
            this.runner = runner ?? new ExternalProcess();
        }
        private async Task<ProcessResult> Run(IEnumerable<string> arguments, CancellationToken token)
        {
            ProcessResult result;
            string response = Path.Combine(Path.GetTempPath(), "filetools-qpdf-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                using (var stream = new FileStream(response, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    foreach (string argument in arguments) writer.WriteLine(argument);
                result = await runner.RunAsync(executable, new[] { "@" + response }, token).ConfigureAwait(false);
            }
            finally { if (File.Exists(response)) File.Delete(response); }
            // qpdf 3 means warnings: accepting repaired/damaged inputs silently would conceal data loss.
            if (result.ExitCode != 0)
                throw new InvalidDataException("qpdf recusou o PDF ou encontrou avisos (código " + result.ExitCode + "). O arquivo pode estar danificado, criptografado ou inacessível. Os originais foram preservados.");
            return result;
        }
        public async Task<int> PageCountAsync(string path, CancellationToken token)
        {
            var result = await Run(new[] { "--show-npages", WindowsPaths.ForIO(path) }, token).ConfigureAwait(false);
            if (!int.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
                throw new InvalidDataException("PDF sem páginas válidas.");
            return count;
        }
        public async Task<string> MergeAsync(string[] files, string directory, bool preserveOrder, CancellationToken token)
        {
            if (!Operations.Supports(Operations.Merge, files)) throw new ArgumentException("Selecione pelo menos dois PDFs.");
            string[] sorted = preserveOrder ? files.ToArray() : files.OrderBy(p => p, new NaturalPathComparer()).ToArray();
            return await output.CreateAsync(directory, "Documentos_unificados.pdf", async (temporary, ct) =>
            {
                // qpdf response files accept one UTF-8 argument per line; the selection never hits Windows' argv limit.
                var arguments = new List<string> { "--empty", "--pages" };
                foreach (string path in sorted) { arguments.Add(WindowsPaths.ForIO(path)); arguments.Add("1-z"); }
                arguments.Add("--");
                arguments.Add(temporary);
                await Run(arguments, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        public static string[] ParseGroups(string specification, int pageCount)
        {
            if (string.IsNullOrWhiteSpace(specification)) throw new ArgumentException("Informe as páginas.");
            var groups = specification.Replace('–', '-').Split(';');
            foreach (string group in groups)
            {
                if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Intervalo vazio.");
                foreach (string range in group.Split(','))
                {
                    var match = Regex.Match(range.Trim(), @"^([1-9][0-9]*)(?:-([1-9][0-9]*))?$");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out int first) ||
                        first > pageCount || (match.Groups[2].Success && (!int.TryParse(match.Groups[2].Value, out int last) || last < first || last > pageCount)))
                        throw new ArgumentException("Páginas inválidas. Use 1-5;6-10 para separar grupos ou 1,3,5 para extrair páginas.");
                }
            }
            return groups.Select(g => Regex.Replace(g, @"\s", "")).ToArray();
        }
        public async Task<string[]> SplitAsync(string path, string directory, string specification, IProgress<string> progress, CancellationToken token)
        {
            int count = await PageCountAsync(path, token).ConfigureAwait(false);
            string[] groups = specification == null ? Enumerable.Range(1, count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray() : ParseGroups(specification, count);
            string folder = FileOutputManager.CreateOutputDirectory(directory, Path.GetFileNameWithoutExtension(path) + "_paginas");
            var committed = new List<string>();
            try
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report("Grupo " + (i + 1) + " de " + groups.Length);
                    string range = groups[i];
                    string filename = Path.GetFileNameWithoutExtension(path) + "_" + (i + 1).ToString("D4") + ".pdf";
                    committed.Add(await output.CreateAsync(folder, filename, async (temp, ct) =>
                        await Run(new[] { "--empty", "--pages", WindowsPaths.ForIO(path), range, "--", temp }, ct).ConfigureAwait(false), token).ConfigureAwait(false));
                }
                return committed.ToArray();
            }
            catch
            {
                // This new folder belongs exclusively to this operation. Roll back only our committed files.
                foreach (string created in committed) File.Delete(created);
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
                throw;
            }
        }
    }
}
