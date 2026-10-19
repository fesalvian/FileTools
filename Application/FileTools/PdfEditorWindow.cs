// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FileTools
{
    public sealed class EditorThumbnail : INotifyPropertyChanged
    {
        public PdfEditPage Page {get;set;}
        public int Number {get;set;}
        private ImageSource image;
        public ImageSource Image {get=>image;set{image=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Image)));}}
        public string Label=>"Página "+Number;
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public sealed class PdfEditorWindow : Window
    {
        private readonly string original;
        private PdfEditSession session;
        private readonly ObservableCollection<EditorThumbnail> thumbnails=new ObservableCollection<EditorThumbnail>();
        private readonly ListBox pages=new ListBox {SelectionMode=SelectionMode.Extended,AllowDrop=true,MinWidth=135};
        private readonly System.Windows.Controls.Image pageImage=new System.Windows.Controls.Image{Stretch=Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
        private readonly TextBlock status=new TextBlock{Text="Abrindo PDF…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(10)};
        private readonly TextBox text=new TextBox{Text="Texto",AcceptsReturn=true,Height=70,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8)};
        private readonly TextBox fontSize=new TextBox{Text="14",Margin=new Thickness(0,4,0,8)};
        private readonly StackPanel formPanel=new StackPanel();
        private readonly WrapPanel toolbar=new WrapPanel{Margin=new Thickness(8)};
        private readonly Dictionary<string,Func<string>> formInputs=new Dictionary<string,Func<string>>();
        private bool busy,refreshing,allowClose,confirmedOriginal,closed;
        private double imageRatio=1;
        private string mode="text",imagePath,preview;
        private Point? dragStart;
        private Task thumbnailTask=Task.CompletedTask;
        public PdfEditorWindow(string path)
        {
            original=path;Title="FileTools • Editor PDF";Width=1120;Height=780;MinWidth=800;MinHeight=550;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new FontFamily("Segoe UI");FontSize=13;
            var layout=new DockPanel();
            DockPanel.SetDock(toolbar,Dock.Top);layout.Children.Add(toolbar);
            DockPanel.SetDock(status,Dock.Bottom);layout.Children.Add(status);
            AddButton("Salvar  Ctrl+S",()=>SaveAsync(false));
            AddButton("Salvar como…",()=>SaveAsync(true));
            AddButton("Imprimir  Ctrl+P",PrintAsync);
            AddButton("Desfazer",()=>EditAsync(()=>session.Undo(),true));
            AddButton("Refazer",()=>EditAsync(()=>session.Redo(),true));
            AddButton("Girar",()=>EditAsync(()=>session.Rotate(SelectedIds())));
            AddButton("Excluir",()=>EditAsync(()=>session.Delete(SelectedIds())));
            AddButton("Inserir PDF…",InsertAsync);
            var grid=new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(160)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});
            pages.ItemsSource=thumbnails;pages.Margin=new Thickness(6);
            var template=new DataTemplate(typeof(EditorThumbnail));
            var stack=new FrameworkElementFactory(typeof(StackPanel));
            var image=new FrameworkElementFactory(typeof(System.Windows.Controls.Image));
            image.SetValue(FrameworkElement.HeightProperty,145.0);image.SetValue(System.Windows.Controls.Image.StretchProperty,Stretch.Uniform);
            image.SetBinding(System.Windows.Controls.Image.SourceProperty,new System.Windows.Data.Binding(nameof(EditorThumbnail.Image)));
            var label=new FrameworkElementFactory(typeof(TextBlock));
            label.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);
            label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(EditorThumbnail.Label)));
            stack.AppendChild(image);stack.AppendChild(label);template.VisualTree=stack;pages.ItemTemplate=template;
            grid.Children.Add(pages);
            var scroll=new ScrollViewer{Content=pageImage,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=new SolidColorBrush(Color.FromRgb(220,223,228)),Margin=new Thickness(4)};
            Grid.SetColumn(scroll,1);grid.Children.Add(scroll);
            var inspector=new StackPanel{Margin=new Thickness(10)};
            inspector.Children.Add(new TextBlock{Text="Sobreposições e anotações",FontWeight=FontWeights.SemiBold});
            inspector.Children.Add(text);
            inspector.Children.Add(new TextBlock{Text="Tamanho do texto (pt)"});inspector.Children.Add(fontSize);
            inspector.Children.Add(Dialogs.Button("Texto — clicar na página",()=>SelectMode("text")));
            inspector.Children.Add(Dialogs.Button("Imagem…",()=>ChooseImage("image")));
            inspector.Children.Add(Dialogs.Button("Assinatura visual…",()=>ChooseImage("signature")));
            inspector.Children.Add(Dialogs.Button("Destacar — arrastar área",()=>SelectMode("highlight")));
            inspector.Children.Add(Dialogs.Button("Comentar — clicar",()=>SelectMode("comment")));
            inspector.Children.Add(new TextBlock{Text="Ctrl/Shift selecionam páginas nas miniaturas. Arraste para reordenar.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,10)});
            inspector.Children.Add(new TextBlock{Text="Formulários AcroForm",FontWeight=FontWeights.SemiBold});inspector.Children.Add(formPanel);
            inspector.Children.Add(new TextBlock{Text="Texto existente não é reescrito. Assinatura visual não é assinatura digital. Alterações invalidam assinaturas digitais existentes.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,12,0,0)});
            var inspectorScroll=new ScrollViewer{Content=inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            Grid.SetColumn(inspectorScroll,2);grid.Children.Add(inspectorScroll);
            layout.Children.Add(grid);Content=layout;
            pages.SelectionChanged+=async(s,e)=>{if(!refreshing&&!busy&&session!=null)try{await RenderSelectedAsync();}catch(Exception ex){Error(ex);}};
            pages.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((s,e)=>QueueThumbnails()));
            Point listStart=new Point();
            pages.PreviewMouseLeftButtonDown+=(s,e)=>listStart=e.GetPosition(pages);
            pages.PreviewMouseMove+=(s,e)=>
            {
                if(busy||pages.SelectedItems.Count==0||e.LeftButton!=MouseButtonState.Pressed)return;
                Point now=e.GetPosition(pages);
                if(Math.Abs(now.X-listStart.X)>SystemParameters.MinimumHorizontalDragDistance||Math.Abs(now.Y-listStart.Y)>SystemParameters.MinimumVerticalDragDistance)
                    DragDrop.DoDragDrop(pages,new DataObject("FileTools.Pages",SelectedIds()),DragDropEffects.Move);
            };
            pages.Drop+=async(s,e)=>
            {
                if(busy||!e.Data.GetDataPresent("FileTools.Pages"))return;
                var row=ItemsControl.ContainerFromElement(pages,e.OriginalSource as DependencyObject) as ListBoxItem;
                int target=row==null?thumbnails.Count:pages.ItemContainerGenerator.IndexFromContainer(row);
                var ids=(string[])e.Data.GetData("FileTools.Pages");
                await EditAsync(()=>session.Move(ids,target));
            };
            pageImage.MouseLeftButtonDown+=(s,e)=>{if(busy)return;dragStart=e.GetPosition(pageImage);pageImage.CaptureMouse();};
            pageImage.MouseLeftButtonUp+=async(s,e)=>
            {
                pageImage.ReleaseMouseCapture();
                if(busy||dragStart==null||session==null)return;
                Point start=dragStart.Value,end=e.GetPosition(pageImage);dragStart=null;
                var selected=SelectedPage();
                if(selected==null||pageImage.ActualWidth<=0)return;
                double sx=selected.ViewWidth/pageImage.ActualWidth,sy=selected.ViewHeight/pageImage.ActualHeight;
                double x=Math.Max(0,Math.Min(start.X,end.X)*sx),y=Math.Max(0,Math.Min(start.Y,end.Y)*sy);
                double w=mode=="highlight"?Math.Max(3,Math.Abs(end.X-start.X)*sx):mode=="comment"?18:160;
                double h=mode=="highlight"?Math.Max(3,Math.Abs(end.Y-start.Y)*sy):mode=="comment"?18:60;
                if((mode=="text"||mode=="comment")&&string.IsNullOrWhiteSpace(text.Text)){status.Text="Digite o texto ou comentário antes de clicar.";return;}
                if(!double.TryParse(fontSize.Text,out double font)||font<4||font>200){status.Text="Tamanho do texto: 4 a 200 pt.";return;}
                if((mode=="image"||mode=="signature")&&imagePath==null){status.Text="Escolha uma imagem primeiro.";return;}
                if(mode=="image"||mode=="signature")h=w*imageRatio;
                await EditAsync(()=>session.AddMark(selected.Id,mode,text.Text,x,y,w,h,font,imagePath));
            };
            PreviewKeyDown+=async(s,e)=>
            {
                if((Keyboard.Modifiers&ModifierKeys.Control)==0)return;
                if(e.Key==Key.S){e.Handled=true;await SaveAsync((Keyboard.Modifiers&ModifierKeys.Shift)!=0);}
                else if(e.Key==Key.P){e.Handled=true;await PrintAsync();}
                else if(!(Keyboard.FocusedElement is TextBox)&&e.Key==Key.Z){e.Handled=true;await EditAsync(()=>session.Undo(),true);}
                else if(!(Keyboard.FocusedElement is TextBox)&&e.Key==Key.Y){e.Handled=true;await EditAsync(()=>session.Redo(),true);}
            };
            Loaded+=async(s,e)=>await OpenAsync();
            Closing+=async(s,e)=>
            {
                if(allowClose)return;
                if(busy){e.Cancel=true;status.Text="Aguarde a operação atual antes de fechar.";return;}
                if(session==null)return;
                if(session.Dirty||PendingForms())
                {
                    e.Cancel=true;
                    var answer=MessageBox.Show(this,"Salvar as alterações antes de fechar?","FileTools",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                    if(answer==MessageBoxResult.Cancel)return;
                    if(answer==MessageBoxResult.Yes&&!await SaveAsync(false))return;
                    allowClose=true;Close();
                }
                else allowClose=true;
            };
            Closed+=async(s,e)=>{closed=true;await thumbnailTask;if(session!=null)try{session.Discard();}catch(IOException){}catch(UnauthorizedAccessException){}};
        }
        private void AddButton(string label,Func<Task> action)
        {
            var button=Dialogs.Button(label,async()=> {if(!busy&&session!=null)try{await action();}catch(Exception ex){Error(ex);}});
            toolbar.Children.Add(button);
        }
        private void SetBusy(bool value,string message=null){busy=value;toolbar.IsEnabled=!value;formPanel.IsEnabled=!value;if(message!=null)status.Text=message;}
        private void Error(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"FileTools",MessageBoxButton.OK,MessageBoxImage.Error);}
        private PdfEditPage SelectedPage()=>pages.SelectedItem is EditorThumbnail item?session.Pages.FirstOrDefault(p=>p.Id==item.Page.Id):null;
        private string[] SelectedIds()=>pages.SelectedItems.Cast<EditorThumbnail>().Select(i=>i.Page.Id).ToArray();
        private void SelectMode(string value){mode=value;status.Text=value=="highlight"?"Arraste uma área para destacar.":"Clique na página para inserir "+value+".";}
        private void ChooseImage(string kind)
        {
            var dialog=new OpenFileDialog{Filter="Imagens PNG/JPEG|*.png;*.jpg;*.jpeg"};
            if(dialog.ShowDialog(this)==true)
            {
                try{using(var bitmap=new System.Drawing.Bitmap(dialog.FileName))imageRatio=(double)bitmap.Height/bitmap.Width;imagePath=dialog.FileName;SelectMode(kind);}
                catch(Exception ex){Error(new InvalidDataException("Não foi possível abrir a imagem escolhida.",ex));}
            }
        }
        private async Task OpenAsync()
        {
            SetBusy(true,"Preparando cópia local de recuperação…");
            try
            {
                string recovery=await Task.Run(()=>PdfEditSession.FindRecovery(original));
                bool restore=recovery!=null&&MessageBox.Show(this,"Há alterações recuperáveis de uma sessão anterior. Restaurar?","Recuperação FileTools",MessageBoxButton.YesNo)==MessageBoxResult.Yes;
                session=await Task.Run(()=>restore?PdfEditSession.Load(recovery):PdfEditSession.Create(original));
                ReloadForms();await RefreshAsync();status.Text="Pronto. Texto/imagem: clique na página; destaque: arraste uma área.";
            }
            catch(Exception ex){Error(ex);}
            finally{SetBusy(false);}
        }
        private void ReloadForms()
        {
            formPanel.Children.Clear();formInputs.Clear();
            foreach(var field in session.Forms)
            {
                formPanel.Children.Add(new TextBlock{Text=field.Name,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,3)});
                if(field.Kind=="check")
                {
                    var input=new CheckBox{IsChecked=field.Value=="true",Content="Marcado"};
                    formPanel.Children.Add(input);formInputs[field.Name]=()=>input.IsChecked==true?"true":"false";
                }
                else if(field.Kind=="choice")
                {
                    var input=new ComboBox{ItemsSource=field.Choices,SelectedIndex=int.Parse(field.Value)};
                    formPanel.Children.Add(input);formInputs[field.Name]=()=>input.SelectedIndex.ToString();
                }
                else
                {
                    var input=new TextBox{Text=field.Value??""};formPanel.Children.Add(input);formInputs[field.Name]=()=>input.Text;
                }
            }
            if(session.Forms.Count==0)formPanel.Children.Add(new TextBlock{Text="Sem campos editáveis suportados.",TextWrapping=TextWrapping.Wrap,FontSize=11});
        }
        private bool PendingForms()=>session!=null&&session.Forms.Any(f=>formInputs.ContainsKey(f.Name)&&formInputs[f.Name]()!=f.Value);
        private void CommitForms(){foreach(var field in session.Forms.ToArray())if(formInputs.TryGetValue(field.Name,out var read)&&read()!=field.Value)session.SetForm(field.Name,read());}
        private async Task EditAsync(Action edit,bool reloadForms=false)
        {
            if(busy||session==null)return;
            SetBusy(true,"Atualizando documento…");
            try{CommitForms();edit();if(reloadForms)ReloadForms();await RefreshAsync();status.Text="Alterações recuperáveis. Ctrl+S para salvar.";}
            catch(Exception ex){Error(ex);}
            finally{SetBusy(false);}
        }
        private async Task InsertAsync()
        {
            var dialog=new OpenFileDialog{Filter="PDF|*.pdf"};
            if(dialog.ShowDialog(this)==true)
            {
                int at=Math.Max(0,pages.SelectedIndex+1);
                await EditAsync(()=>session.Insert(dialog.FileName,at));
                status.Text="Páginas inseridas. Campos de formulários de PDFs importados não são mesclados ao formulário original.";
            }
        }
        private async Task RefreshAsync()
        {
            await thumbnailTask;
            string selected=SelectedPage()?.Id;
            var snapshot=session.Capture();
            string next=Path.Combine(session.DirectoryPath,"preview-"+Guid.NewGuid().ToString("N")+".pdf");
            await Task.Run(()=>session.Export(next,snapshot));
            string old=preview;preview=next;
            refreshing=true;thumbnails.Clear();
            for(int i=0;i<session.Pages.Count;i++)thumbnails.Add(new EditorThumbnail{Page=session.Pages[i],Number=i+1});
            pages.SelectedIndex=Math.Max(0,thumbnails.ToList().FindIndex(t=>t.Page.Id==selected));
            refreshing=false;
            await RenderSelectedAsync();
            if(old!=null)try{File.Delete(old);}catch(IOException){}
            Title="FileTools • "+Path.GetFileName(original)+(session.Dirty?" *":"");
        }
        private async Task RenderSelectedAsync()
        {
            if(preview==null||pages.SelectedIndex<0)return;
            int index=pages.SelectedIndex;
            string path=preview;
            var rendered=await Task.Run(()=> {using(var pdf=new PdfRenderer(path))using(var bitmap=pdf.Render(index,100))return PdfVisuals.ToSource(bitmap);});
            if(closed||preview!=path||pages.SelectedIndex!=index)return;
            pageImage.Source=rendered;pageImage.Width=rendered.PixelWidth;pageImage.Height=rendered.PixelHeight;
            QueueThumbnails(true);
        }
        private void QueueThumbnails(bool force=false)
        {
            if(closed||preview==null||!thumbnailTask.IsCompleted||(!force&&busy))return;
            string path=preview;
            var visible=thumbnails.Select((item,index)=>new{item,index}).Where(t=>t.item.Image==null&&(pages.ItemContainerGenerator.ContainerFromIndex(t.index) as FrameworkElement)?.IsVisible==true).Take(12).ToArray();
            if(visible.Length==0&&force)visible=thumbnails.Take(6).Select((item,index)=>new{item,index}).ToArray();
            thumbnailTask=Load();
            async Task Load()
            {
                try
                {
                    var results=await Task.Run(()=>
                    {
                        var list=new List<Tuple<EditorThumbnail,BitmapSource>>();
                        using(var pdf=new PdfRenderer(path))foreach(var item in visible)using(var bitmap=pdf.Render(item.index,36,1000000))list.Add(Tuple.Create(item.item,PdfVisuals.ToSource(bitmap)));
                        return list;
                    });
                    if(!closed&&preview==path)foreach(var item in results)item.Item1.Image=item.Item2;
                }
                catch(Exception ex){if(!closed)status.Text="Miniaturas indisponíveis: "+ex.Message;}
            }
        }
        private async Task<bool> SaveAsync(bool saveAs)
        {
            if(busy||session==null)return false;
            string target=session.SavedPath;
            if(saveAs)
            {
                var dialog=new SaveFileDialog{Filter="PDF|*.pdf",DefaultExt=".pdf",FileName=Path.GetFileNameWithoutExtension(original)+"_editado.pdf",InitialDirectory=Path.GetDirectoryName(original),OverwritePrompt=true};
                if(dialog.ShowDialog(this)!=true)return false;target=dialog.FileName;
            }
            bool originalTarget=target!=null&&string.Equals(WindowsPaths.ForShell(target),WindowsPaths.ForShell(original),StringComparison.OrdinalIgnoreCase);
            if(originalTarget&&!confirmedOriginal)
            {
                if(MessageBox.Show(this,"Substituir o PDF original? Será criado um backup ao lado dele. Assinaturas digitais existentes serão invalidadas.","Confirmar substituição",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return false;
                confirmedOriginal=true;
            }
            SetBusy(true,"Salvando…");
            try
            {
                CommitForms();
                string result=target==null?await session.SaveCopyAsync(Path.GetDirectoryName(original),CancellationToken.None):await session.SaveAsync(target,originalTarget,CancellationToken.None);
                status.Text="Salvo: "+result;Title="FileTools • "+Path.GetFileName(result);return true;
            }
            catch(UnauthorizedAccessException){status.Text="Sem permissão de escrita. Use Salvar como.";return false;}
            catch(Exception ex){Error(ex);return false;}
            finally{SetBusy(false);}
        }
        private async Task PrintAsync()
        {
            if(busy||session==null)return;
            SetBusy(true,"Preparando impressão…");
            try{CommitForms();await RefreshAsync();await PdfPrinting.PrintAsync(new[]{preview});}
            catch(Exception ex){Error(ex);}
            finally{SetBusy(false);}
        }
    }
}
