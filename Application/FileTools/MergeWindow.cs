// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileTools
{
    public sealed class DocumentOrderItem
    {
        public string Path { get; set; }
        public int Pages { get; set; }
        public override string ToString() => System.IO.Path.GetFileName(Path) + "  •  " + (Pages == 0 ? "lendo páginas…" : Pages + " página(s)");
    }
    public static class PageOrdering
    {
        public static void Move<T>(ObservableCollection<T> items, int from, int to)
        {
            if (from < 0 || from >= items.Count || to < 0 || to >= items.Count) throw new ArgumentOutOfRangeException();
            items.Move(from,to);
        }
    }
    internal static class MergeWindow
    {
        public static string[] Select(string[] paths, string action = "Unir PDFs")
        {
            var items = new ObservableCollection<DocumentOrderItem>(paths.OrderBy(p => p, new NaturalPathComparer()).Select(p => new DocumentOrderItem { Path=p }));
            var window = Dialogs.Window(action + " — conferir ordem",620,420);
            var panel = new DockPanel { Margin = new Thickness(16) };
            var info = new TextBlock { Text="Arraste para ordenar. A ordem inicial usa os nomes; não representa a sequência dos cliques.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) };
            DockPanel.SetDock(info,Dock.Top); panel.Children.Add(info);
            var footer = new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
            DockPanel.SetDock(footer,Dock.Bottom); panel.Children.Add(footer);
            var list = new ListBox { ItemsSource=items, AllowDrop=true, Margin=new Thickness(0,0,0,12),ToolTip="Arraste um documento sobre outro para alterar a ordem." };
            int dragIndex = -1;
            Point start = new Point();
            list.PreviewMouseLeftButtonDown += (s,e) => { start=e.GetPosition(list); dragIndex=list.SelectedIndex; };
            list.PreviewMouseMove += (s,e) =>
            {
                Point position=e.GetPosition(list);
                if (e.LeftButton==MouseButtonState.Pressed && list.SelectedIndex>=0 && (Math.Abs(position.X-start.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(position.Y-start.Y)>SystemParameters.MinimumVerticalDragDistance))
                { dragIndex=list.SelectedIndex; DragDrop.DoDragDrop(list,new DataObject("FileTools.Document",dragIndex),DragDropEffects.Move); }
            };
            list.Drop += (s,e) =>
            {
                if (!e.Data.GetDataPresent("FileTools.Document")) return;
                var row=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;
                int target=row==null?items.Count-1:list.ItemContainerGenerator.IndexFromContainer(row);
                PageOrdering.Move(items,(int)e.Data.GetData("FileTools.Document"),target);
                list.SelectedIndex=target;
            };
            void Shift(int step) { int index=list.SelectedIndex; if(index>=0 && index+step>=0 && index+step<items.Count) { PageOrdering.Move(items,index,index+step); list.SelectedIndex=index+step; } }
            footer.Children.Add(Dialogs.Button("↑",()=>Shift(-1)));
            footer.Children.Add(Dialogs.Button("↓",()=>Shift(1)));
            footer.Children.Add(Dialogs.Button("Cancelar",()=>window.DialogResult=false));
            var accept=Dialogs.Button(action,()=>window.DialogResult=true);
            accept.IsDefault=true; accept.IsEnabled=false; footer.Children.Add(accept);
            panel.Children.Add(list); window.Content=panel;
            window.Loaded += async (s,e) =>
            {
                try
                {
                    foreach(var item in items.ToArray())
                    {
                        item.Pages=await Task.Run(()=> { using(var pdf=new PdfRenderer(item.Path)) return pdf.PageCount; });
                        if(!window.IsVisible)return;
                        list.Items.Refresh();
                    }
                    info.Text=items.Count+" documentos • "+items.Sum(i=>i.Pages)+" páginas. Arraste para ordenar; Enter para confirmar.";
                    accept.IsEnabled=true;
                }
                catch(Exception) { info.Text="Não foi possível ler um dos PDFs. Verifique a integridade e a senha. Nenhum arquivo foi alterado."; }
            };
            return window.ShowDialog()==true?items.Select(i=>i.Path).ToArray():null;
        }
    }
}
