// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ImageMagick;

namespace FileTools
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show("Falha na interface. Tente executar novamente.", "FileTools");
                e.Handled = true;
                app.Shutdown(1);
            };
            app.Startup += async (s, e) =>
            {
                int code = 0;
                try
                {
                    if (args.Length != 2 || args[0] != "--request")
                    {
                        MessageBox.Show("Selecione arquivos no Explorador e use FileTools no menu de contexto.\n\nPDF: unir, separar e extrair páginas.\nImagens: PNG, JPG e WebP.\n\nInstalação: execute Install.cmd no pacote.\nTodos os novos resultados preservam os originais.", "FileTools");
                        return;
                    }
                    SelectionRequest request = SelectionTransport.Read(args[1], true);
                    SelectionTransport.CleanupExpired();
                    if (request.Files.Any(p => !File.Exists(p))) throw new FileNotFoundException("Um arquivo selecionado não está mais disponível.");
                    string destination = Path.GetDirectoryName(request.Files[0]); // Capture BEFORE natural ordering.
                    string[] ordered = request.Files;
                    string specification = null;
                    if (request.Operation == Operations.MergeOrdered)
                    {
                        ordered = Dialogs.OrderFiles(request.Files.OrderBy(p => p, new NaturalPathComparer()).ToArray());
                        if (ordered == null) return;
                    }
                    if (request.Operation == Operations.Extract)
                    {
                        specification = Dialogs.PageGroups();
                        if (specification == null) return;
                    }
                    // Probe on a background thread; only present a destination picker if permission actually fails.
                    while (true)
                    {
                        try { await Task.Run(() => FileOutputManager.EnsureWritable(destination)); break; }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            destination = Dialogs.ChooseDestination();
                            if (destination == null) return;
                        }
                    }
                    using (var cancellation = new CancellationTokenSource())
                    {
                        var progressWindow = new ProgressWindow(cancellation);
                        bool completed = false;
                        var delay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                        delay.Tick += (sender, ev) => { delay.Stop(); if (!completed) progressWindow.Show(); };
                        delay.Start();
                        var progress = new Progress<string>(value => progressWindow.SetStatus(value));
                        try
                        {
                            string[] results = await ExecuteAsync(request.Operation, ordered, destination, specification, progress, cancellation.Token);
                            completed = true;
                            delay.Stop();
                            progressWindow.Finish();
                            await Dialogs.NotifyAsync(results);
                        }
                        finally { completed = true; delay.Stop(); progressWindow.Finish(); }
                    }
                }
                catch (OperationCanceledException)
                {
                    code = 2;
                    MessageBox.Show("Operação cancelada. Os originais foram preservados. Resultados de itens concluídos anteriormente permanecem na pasta de destino.", "FileTools");
                }
                catch (Exception ex)
                {
                    code = 1;
                    LogFailure(ex);
                    string message = ex is FileNotFoundException ? "Um arquivo ou dependência não está disponível. Verifique se o pacote contém Tools/qpdf/bin/qpdf.exe e selecione novamente os arquivos." :
                        ex is InvalidDataException || ex is ArgumentException ? ex.Message :
                        "Não foi possível concluir a operação. Verifique permissões, espaço em disco e a integridade dos arquivos. Itens já concluídos permanecem no destino.";
                    MessageBox.Show(message, "FileTools", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { app.Shutdown(code); }
            };
            return app.Run();
        }
        public static async Task<string[]> ExecuteAsync(string operation, string[] paths, string destination, string specification, IProgress<string> progress, CancellationToken token)
        {
            if (!Operations.Supports(operation, paths)) throw new ArgumentException("Seleção incompatível.");
            var results = new List<string>();
            progress?.Report("Aguardando outros trabalhos…");
            return await BatchOperations.RunAsync(async () =>
            {
                    if (operation == Operations.Merge || operation == Operations.MergeOrdered)
                    {
                        progress?.Report("Unindo " + paths.Length + " PDFs…");
                        results.Add(await new PdfOperations(Dependencies.Qpdf).MergeAsync(paths, destination, operation == Operations.MergeOrdered, token));
                    }
                    else if (operation == Operations.Split || operation == Operations.Extract)
                    {
                        var pdf = new PdfOperations(Dependencies.Qpdf);
                        foreach (string path in paths)
                        {
                            token.ThrowIfCancellationRequested();
                            results.AddRange(await pdf.SplitAsync(path, destination, specification, progress, token));
                        }
                    }
                    else
                    {
                        // Resource limits are set only in this worker, never explorer.exe.
                        ResourceLimits.Memory = 256UL * 1024 * 1024;
                        ResourceLimits.MaxMemoryRequest = 512UL * 1024 * 1024;
                        ResourceLimits.Disk = 2UL * 1024 * 1024 * 1024;
                        ResourceLimits.Thread = 2;
                        var images = new ImageConversion();
                        for (int i = 0; i < paths.Length; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            progress?.Report("Convertendo imagem " + (i + 1) + " de " + paths.Length + "…");
                            results.Add(await images.ConvertAsync(paths[i], destination, operation.Substring(6), token));
                        }
                    }
                    return results.ToArray();
            }, token);
        }
        private static void LogFailure(Exception exception)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileTools", "Logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
                // Do not log document names, paths, file content, or converter stderr.
                File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + exception.GetType().Name + " HResult=" + exception.HResult + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
