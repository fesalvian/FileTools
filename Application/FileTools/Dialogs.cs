// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileTools
{
    internal static class Dialogs
    {
        public static Window Window(string title, double width = 560, double height = 360)
        {
            bool dark = false;
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            return new Window { Title = "FileTools • " + title, Width = width, Height = height, MinWidth = 400, MinHeight = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
                Background = dark ? new SolidColorBrush(Color.FromRgb(32,32,32)) : Brushes.White,
                Foreground = dark ? Brushes.WhiteSmoke : Brushes.Black };
        }
        public static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(4) };
            button.Click += (s, e) => action();
            return button;
        }
        public static string[] OrderFiles(string[] paths)
        {
            var window = Window("Ordem dos documentos");
            var panel = new DockPanel { Margin = new Thickness(18) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom);
            panel.Children.Add(buttons);
            var list = new ListBox { ItemsSource = paths, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) };
            void Move(int step)
            {
                int index = list.SelectedIndex;
                int target = index + step;
                if (index < 0 || target < 0 || target >= paths.Length) return;
                string temp = paths[index]; paths[index] = paths[target]; paths[target] = temp;
                list.Items.Refresh(); list.SelectedIndex = target;
            }
            buttons.Children.Add(Button("Subir", () => Move(-1)));
            buttons.Children.Add(Button("Descer", () => Move(1)));
            buttons.Children.Add(Button("Cancelar", () => window.DialogResult = false));
            buttons.Children.Add(Button("Unir PDFs", () => window.DialogResult = true));
            panel.Children.Add(list);
            window.Content = panel;
            return window.ShowDialog() == true ? paths : null;
        }
        public static string PageGroups()
        {
            var window = Window("Páginas", 520, 290);
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock { Text = "Separe grupos com ponto e vírgula.\nExemplo: 1-5;6-10 gera dois arquivos.\nExemplo: 1,3,5 extrai três páginas em um arquivo.\nA mesma seleção será aplicada a cada PDF.", TextWrapping = TextWrapping.Wrap });
            var text = new TextBox { Text = "1", Margin = new Thickness(0, 14, 0, 12) };
            panel.Children.Add(text);
            panel.Children.Add(Button("Continuar", () =>
            {
                try { PdfOperations.ParseGroups(text.Text, int.MaxValue); window.DialogResult = true; }
                catch (ArgumentException ex) { MessageBox.Show(window, ex.Message, "FileTools"); }
            }));
            window.Content = panel;
            return window.ShowDialog() == true ? text.Text : null;
        }
        public static string ChooseDestination()
        {
            using (var picker = new System.Windows.Forms.FolderBrowserDialog { Description = "A pasta de origem não permite gravação. Escolha onde salvar os resultados.", ShowNewFolderButton = true })
                return picker.ShowDialog() == System.Windows.Forms.DialogResult.OK ? picker.SelectedPath : null;
        }
        public static async Task NotifyAsync(string[] results)
        {
            if (results.Length == 0) return;
            using (var icon = new System.Windows.Forms.NotifyIcon { Text = "FileTools", Visible = true })
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FileTools.ico");
                using (var drawingIcon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Information)
                {
                    icon.Icon = drawingIcon;
                    var menu = new System.Windows.Forms.ContextMenuStrip();
                    if (results.Length == 1) menu.Items.Add("Abrir arquivo", null, (s,e) => Open(results[0]));
                    menu.Items.Add("Abrir pasta", null, (s,e) => Open(Path.GetDirectoryName(results[0])));
                    icon.ContextMenuStrip = menu;
                    icon.BalloonTipClicked += (s,e) => Open(results.Length == 1 ? results[0] : Path.GetDirectoryName(results[0]));
                    icon.DoubleClick += (s,e) => Open(Path.GetDirectoryName(results[0]));
                    icon.ShowBalloonTip(6000, "FileTools", results.Length + " arquivo(s) criado(s). Clique para abrir. Use o botão direito no ícone para abrir a pasta.", System.Windows.Forms.ToolTipIcon.Info);
                    await Task.Delay(8000);
                    icon.Visible = false;
                    menu.Dispose();
                }
            }
        }
        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(WindowsPaths.ForShell(path)) { UseShellExecute = true }); }
            catch (Exception) { MessageBox.Show("Não foi possível abrir o resultado. Ele permanece salvo na pasta de destino.", "FileTools"); }
        }
    }
    internal sealed class ProgressWindow
    {
        private readonly Window window;
        private readonly TextBlock status;
        private bool finished;
        public ProgressWindow(CancellationTokenSource cancellation)
        {
            window = Dialogs.Window("Processando", 460, 240);
            var panel = new StackPanel { Margin = new Thickness(22) };
            status = new TextBlock { Text = "Preparando…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
            panel.Children.Add(status);
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 0, 0, 18) });
            panel.Children.Add(Dialogs.Button("Cancelar", () => { cancellation.Cancel(); status.Text = "Cancelando…"; }));
            window.Content = panel;
            window.Closing += (s, e) => { if (!finished) { e.Cancel = true; cancellation.Cancel(); status.Text = "Cancelando…"; } };
        }
        public void Show() => window.Show();
        public void SetStatus(string text) => status.Text = text;
        public void Finish() { if (finished) return; finished = true; window.Close(); }
    }
}
