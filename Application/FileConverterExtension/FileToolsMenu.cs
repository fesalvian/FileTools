// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FileTools;

namespace FileConverterExtension
{
    public partial class FileConverterExtension
    {
        public static ContextMenuStrip BuildFileToolsMenu(string[] selection, Action<string, string[]> invoke)
        {
            // Snapshot IDataObject selection before returning to Explorer. No conversion or filesystem scan here.
            string[] snapshot = selection.ToArray();
            var menu = new ContextMenuStrip();
            var root = new ToolStripMenuItem("FileTools") { Name = "FileTools" };
            string iconPath = Path.Combine(Path.GetDirectoryName(typeof(FileConverterExtension).Assembly.Location), "FileTools.ico");
            try { using (var icon = new Icon(iconPath, SystemInformation.SmallIconSize)) root.Image = icon.ToBitmap(); }
            catch (IOException) { } catch (ArgumentException) { }
            var pdf = new ToolStripMenuItem("PDF");
            var images = new ToolStripMenuItem("Imagens");
            AddOperation(root, "Unir PDFs…", Operations.Merge, snapshot, invoke);
            AddOperation(root, "Editar PDF", Operations.Edit, snapshot, invoke);
            AddOperation(root, "Imprimir PDF…", Operations.Print, snapshot, invoke);
            AddOperation(root, "Converter DOC/DOCX para PDF", Operations.DocumentPdf, snapshot, invoke);
            AddOperation(root, "Imagens para PDF único", Operations.ImagesPdf, snapshot, invoke);
            AddOperation(images, "Cada imagem para um PDF", Operations.ImagePdf, snapshot, invoke);
            AddOperation(pdf, "Separar todas as páginas", Operations.Split, snapshot, invoke);
            AddOperation(pdf, "Dividir por intervalos / extrair páginas…", Operations.Extract, snapshot, invoke);
            AddOperation(pdf, "Todas as páginas para PNG", Operations.PdfPng, snapshot, invoke);
            AddOperation(pdf, "Todas as páginas para JPG", Operations.PdfJpg, snapshot, invoke);
            AddOperation(pdf, "Converter para DOCX editável…", Operations.PdfDocx, snapshot, invoke);
            AddOperation(images, "Converter para PNG", "image-png", snapshot, invoke);
            AddOperation(images, "Converter para JPG", "image-jpg", snapshot, invoke);
            AddOperation(images, "Converter para WebP", "image-webp", snapshot, invoke);
            if (pdf.DropDownItems.Count > 0) root.DropDownItems.Add(pdf);
            if (images.DropDownItems.Count > 0) root.DropDownItems.Add(images);
            menu.Items.Add(root);
            return menu;
        }
        private static void AddOperation(ToolStripMenuItem parent, string title, string operation, string[] snapshot, Action<string, string[]> invoke)
        {
            if (!Operations.Supports(operation, snapshot)) return;
            var item = new ToolStripMenuItem(title) { Name = operation };
            item.Click += (s, e) => invoke(operation, snapshot.ToArray());
            parent.DropDownItems.Add(item);
        }
        protected override ContextMenuStrip CreateMenu()
        {
            var menu = BuildFileToolsMenu(SelectedItemPaths.ToArray(), LaunchFileTools);
            // Keep upstream conversion presets available without requiring an existing machine installation.
            try
            {
                LoadExtensionSettingsIfNecessary();
                if (presetReferences != null && presetReferences.Length > 0)
                {
                    using (var legacy = CreateLegacyMenu())
                    {
                        var legacyRoot = (ToolStripMenuItem)legacy.Items[0];
                        legacy.Items.Remove(legacyRoot);
                        legacyRoot.Text = "Conversões existentes";
                        ((ToolStripMenuItem)menu.Items[0]).DropDownItems.Add(legacyRoot);
                    }
                }
            }
            catch (Exception) { /* Never propagate settings failures into explorer.exe. */ }
            return menu;
        }
        private static void LaunchFileTools(string operation, string[] paths)
        {
            string request = null;
            try
            {
                string executable = Path.Combine(Path.GetDirectoryName(typeof(FileConverterExtension).Assembly.Location), "FileTools.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("FileTools.exe ausente. Reinstale o pacote completo.");
                request = SelectionTransport.Write(operation, paths);
                Process.Start(new ProcessStartInfo(executable, "--request " + ExternalProcess.Quote(request))
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception)
            {
                if (request != null) try { File.Delete(request); } catch (IOException) { }
                MessageBox.Show("Não foi possível iniciar o FileTools. Verifique a instalação e a permissão da pasta de solicitações.", "FileTools");
            }
        }
    }
}
