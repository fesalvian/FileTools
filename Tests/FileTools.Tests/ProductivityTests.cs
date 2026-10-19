// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileTools;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static partial class Tests
{
    private static void NewFeatures()
    {
        Run("PDFium renderiza conteudo real, Unicode e tamanho limitado",()=>
        {
            string path=Pdf(NewDirectory("ação 中文"),"página.pdf","VISIBLE");
            using(var pdf=new PdfRenderer(path))using(var bitmap=pdf.Render(0,144,100000))
            {
                Assert(pdf.PageCount==1 && pdf.PageSize(0).Width==200);
                Assert(bitmap.Width*bitmap.Height<=100000 && bitmap.Width>100);
                Assert(DarkPixels(bitmap)>20);
            }
            return Task.CompletedTask;
        });
        Run("PDFium recusa PDF corrompido sem fechar o worker",async()=>
        {
            string path=Path.Combine(NewDirectory(),"bad.pdf");File.WriteAllText(path,"broken");
            await Throws<InvalidDataException>(()=>{using(var renderer=new PdfRenderer(path)){}return Task.CompletedTask;});
        });
        Run("PDF para todas as paginas PNG e JPG preserva original",async()=>
        {
            string dir=NewDirectory(),input=await MakeMerged(dir,3),hash=Hash(input);
            foreach(string format in new[]{"png","jpg"})
            {
                string[] outputs=await PdfRenderer.ExportAsync(input,dir,format,null,CancellationToken.None);
                Assert(outputs.Length==3&&outputs.All(File.Exists));
                foreach(string output in outputs)using(var image=new System.Drawing.Bitmap(output))Assert(DarkPixels(image)>10);
            }
            Assert(Hash(input)==hash);
        });
        Run("exportacao PDF cancelada remove lote incompleto",async()=>
        {
            string dir=NewDirectory(),input=await MakeMerged(dir,3);
            using(var cts=new CancellationTokenSource())
            {
                var progress=new InlineProgress(_=>cts.Cancel());
                await Throws<OperationCanceledException>(()=>PdfRenderer.ExportAsync(input,dir,"png",progress,cts.Token));
            }
            Assert(Directory.GetDirectories(dir).Length==0);
        });
        Run("PNG e JPG para PDF unido em ordem natural e individual",async()=>
        {
            string dir=NewDirectory(),red=Image(dir,"img2.png",MagickColors.Red),blue=Image(dir,"img10.jpg",MagickColors.Blue);
            string[] hashes=new[]{Hash(red),Hash(blue)};
            string output=await ImagesToPdf.ConvertAsync(new[]{blue,red},dir,true,CancellationToken.None);
            Assert(await new PdfOperations(qpdf).PageCountAsync(output,CancellationToken.None)==2);
            using(var pdf=new PdfRenderer(output))using(var bitmap=pdf.Render(0,72))
            {var center=bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2);Assert(center.R>200&&center.B<50);}
            string[] individual=await FileTools.Program.ExecuteAsync(Operations.ImagePdf,new[]{red,blue},dir,null,null,CancellationToken.None);
            Assert(individual.Length==2&&individual.All(File.Exists));
            Assert(new[]{Hash(red),Hash(blue)}.SequenceEqual(hashes));
        });
        Run("editor insere texto real e salva copia sem alterar original",async()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"original.pdf","SOURCE"),hash=Hash(input);
            var session=PdfEditSession.Create(input,dir);
            try
            {
                session.AddMark(session.Pages[0].Id,"text","NOVO TEXTO ação",15,15,150,30,18);
                Assert(session.Dirty&&session.CanUndo);
                string output=await session.SaveCopyAsync(dir,CancellationToken.None);
                if(Hash(input)!=hash||session.Dirty||output==input)throw new Exception("Original ou estado de salvamento alterado incorretamente.");
                using(var before=new PdfRenderer(input))using(var after=new PdfRenderer(output))
                using(var a=before.Render(0,144))using(var b=after.Render(0,144))if(DarkPixels(b)<=DarkPixels(a)+100)throw new Exception("Texto nao apareceu: "+DarkPixels(a)+" / "+DarkPixels(b));
                var validation=await new ExternalProcess().RunAsync(qpdf,new[]{"--check",output},CancellationToken.None);
                if(validation.ExitCode!=0)throw new Exception("qpdf: "+validation.Error+validation.Output);
            }
            finally{session.Discard();}
        });
        Run("editor desfaz refaz e recupera diario de alteracoes",()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"base.pdf","SOURCE");
            var session=PdfEditSession.Create(input,dir);
            try
            {
                string id=session.Pages[0].Id;
                session.AddMark(id,"text","A",10,10);session.Rotate(new[]{id});
                session.Undo();Assert(session.Pages[0].Rotation==0&&session.Pages[0].Marks.Count==1);
                session.Redo();Assert(session.Pages[0].Rotation==90);
                var recovered=PdfEditSession.Load(session.DirectoryPath);
                Assert(recovered.Dirty&&recovered.Pages[0].Rotation==90&&recovered.Pages[0].Marks[0].Text=="A");
                recovered.Export(Path.Combine(dir,"recovered.pdf"));
            }
            finally{session.Discard();}
            return Task.CompletedTask;
        });
        Run("editor ordena multipaginas insere remove gira e preserva fontes",async()=>
        {
            string dir=NewDirectory(),input=await MakeMerged(dir,3),insert=Pdf(dir,"insert.pdf","INSERT"),hash=Hash(input);
            var session=PdfEditSession.Create(input,dir);
            try
            {
                session.Move(new[]{session.Pages[0].Id,session.Pages[1].Id},3);
                Assert(session.Pages.Select(p=>p.Index).SequenceEqual(new[]{2,0,1}));
                session.Delete(new[]{session.Pages[1].Id});session.Insert(insert,1);
                session.Rotate(new[]{session.Pages[2].Id});
                string output=await session.SaveCopyAsync(dir,CancellationToken.None);
                await AssertPage(output,1,"MARK3");await AssertPage(output,2,"INSERT");await AssertPage(output,3,"MARK2");
                using(var pdf=PdfReader.Open(output,PdfDocumentOpenMode.Import))Assert(pdf.Pages[2].Rotate==90&&pdf.PageCount==3);
                Assert(Hash(input)==hash);
                await Throws<InvalidOperationException>(()=>{session.Delete(session.Pages.Select(p=>p.Id).ToArray());return Task.CompletedTask;});
            }
            finally{session.Discard();}
        });
        Run("editor grava imagem assinatura destaque e comentario",async()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"base.pdf","BASE"),image=Image(dir,"signature.png",MagickColors.Red);
            var session=PdfEditSession.Create(input,dir);
            try
            {
                string id=session.Pages[0].Id;
                session.AddMark(id,"image",null,20,20,30,20,14,image);
                session.AddMark(id,"signature",null,60,20,40,20,14,image);
                session.AddMark(id,"highlight","Destaque",20,80,80,20);
                session.AddMark(id,"comment","Comentário",110,80,18,18);
                string output=await session.SaveCopyAsync(dir,CancellationToken.None);
                using(var pdf=PdfReader.Open(output,PdfDocumentOpenMode.Modify))
                {Assert(pdf.Pages[0].Annotations.Count==2);Assert(pdf.Pages[0].Annotations[0].Elements.GetName("/Subtype")=="/Highlight");}
                using(var pdf=new PdfRenderer(output))using(var bitmap=pdf.Render(0,72))Assert(bitmap.GetPixel(25,25).R>200&&bitmap.GetPixel(25,25).G<50);
            }
            finally{session.Discard();}
        });
        Run("original exige confirmacao, backup e bloqueio de mudanca externa",async()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"original.pdf","ORIGINAL"),hash=Hash(input);
            var session=PdfEditSession.Create(input,dir);
            try
            {
                session.AddMark(session.Pages[0].Id,"text","ADD",10,10);
                await Throws<InvalidOperationException>(()=>session.SaveAsync(input,false,CancellationToken.None));
                Assert(Hash(input)==hash);
                await session.SaveAsync(input,true,CancellationToken.None);
                string backup=Directory.GetFiles(dir,"*.filetools-backup-*.pdf").Single();
                Assert(Hash(backup)==hash&&Hash(input)!=hash);
                File.AppendAllText(input,"\n%external");
                await Throws<IOException>(()=>session.SaveAsync(input,true,CancellationToken.None));
                Assert(!Directory.GetFiles(dir,".filetools-save-*").Any());
            }
            finally{session.Discard();}
        });
        Run("editor cancelado nao altera arquivo nem deixa temporario",async()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"original.pdf","ORIGINAL"),hash=Hash(input);
            var session=PdfEditSession.Create(input,dir);
            try
            {
                using(var cts=new CancellationTokenSource())
                {cts.Cancel();await Throws<OperationCanceledException>(()=>session.SaveAsync(input,true,cts.Token));}
                Assert(Hash(input)==hash&&!Directory.GetFiles(dir,".filetools-save-*").Any());
            }
            finally{session.Discard();}
        });
        Run("recuperacao usa ultimo diario integro apos corrupcao",()=>
        {
            string dir=NewDirectory();var session=PdfEditSession.Create(Pdf(dir,"input.pdf","PDF"),dir);
            try
            {
                session.AddMark(session.Pages[0].Id,"text","RECOVER",10,10);
                session.Rotate(new[]{session.Pages[0].Id});
                File.WriteAllText(Path.Combine(session.DirectoryPath,"state.json"),"broken");
                var restored=PdfEditSession.Load(session.DirectoryPath);
                Assert(restored.Dirty&&restored.Pages[0].Marks.Single().Text=="RECOVER"&&restored.Pages[0].Rotation==0);
            }
            finally{session.Discard();}return Task.CompletedTask;
        });
        Run("sobreposicao acompanha coordenadas em quatro rotacoes",async()=>
        {
            string dir=NewDirectory(),input=FormPdf(dir),image=Image(dir,"red.png",MagickColors.Red);
            for(int turns=0;turns<4;turns++)
            {
                var session=PdfEditSession.Create(input,dir);
                try
                {
                    for(int i=0;i<turns;i++)session.Rotate(new[]{session.Pages[0].Id});
                    session.AddMark(session.Pages[0].Id,"image",null,40,40,30,20,14,image);
                    string output=await session.SaveCopyAsync(dir,CancellationToken.None);
                    using(var pdf=new PdfRenderer(output))using(var bitmap=pdf.Render(0,72))
                    {var color=bitmap.GetPixel(45,45);Assert(color.R>200&&color.G<50);}
                }
                finally{session.Discard();}
            }
        });
        Run("AcroForm texto checkbox e lista salvam valores e aparencias",async()=>
        {
            string dir=NewDirectory(),input=FormPdf(dir);var session=PdfEditSession.Create(input,dir);
            try
            {
                Assert(session.Forms.Count==3);
                session.SetForm("Nome","Preenchido ação");session.SetForm("Aceito","true");session.SetForm("Opcao","1");
                string output=await session.SaveCopyAsync(dir,CancellationToken.None);
                using(var pdf=PdfReader.Open(output,PdfDocumentOpenMode.Modify))
                {
                    Assert(((PdfSharp.Pdf.AcroForms.PdfTextField)pdf.AcroForm.Fields["Nome"]).Text=="Preenchido ação");
                    Assert(((PdfSharp.Pdf.AcroForms.PdfCheckBoxField)pdf.AcroForm.Fields["Aceito"]).Checked);
                    Assert(((PdfSharp.Pdf.AcroForms.PdfComboBoxField)pdf.AcroForm.Fields["Opcao"]).SelectedIndex==1);
                }
                using(var pdf=new PdfRenderer(output))using(var bitmap=pdf.Render(0,144))Assert(DarkPixels(bitmap)>100);
                Assert((await new ExternalProcess().RunAsync(qpdf,new[]{"--check",output},CancellationToken.None)).ExitCode==0);
            }
            finally{session.Discard();}
        });
        Run("paginador de impressao renderiza sem enviar para impressora",()=>
        {
            string input=Pdf(NewDirectory(),"print.pdf","PRINT");
            var paginator=new PdfBatchPaginator(new[]{new PdfPrintPage{Path=input,Index=0}},new System.Windows.Size(793,1122));
            Assert(paginator.PageCount==1&&paginator.GetPage(0).Visual!=null);
            Assert(paginator.GetPage(1)==System.Windows.Documents.DocumentPage.Missing);
            return Task.CompletedTask;
        });
        Run("editor WPF abre preview real e botao Salvar publica PDF",()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"ui.pdf","UI");
            var previous=SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            var window=new PdfEditorWindow(input);PdfEditSession session=null;
            object Field(string name)=>typeof(PdfEditorWindow).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(window);
            try
            {
                window.Show();
                PumpUntil(()=>!(bool)Field("busy")&&Field("session")!=null&&((System.Windows.Controls.Image)Field("pageImage")).Source!=null);
                session=(PdfEditSession)Field("session");session.AddMark(session.Pages[0].Id,"text","SALVO UI",10,10);
                var toolbar=(System.Windows.Controls.WrapPanel)Field("toolbar");
                var save=toolbar.Children.OfType<System.Windows.Controls.Button>().First(b=>b.Content.ToString().StartsWith("Salvar  "));
                save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(()=>session.SavedPath!=null&&!(bool)Field("busy"));
                Assert(File.Exists(session.SavedPath)&&!session.Dirty);
                using(var pdf=new PdfRenderer(session.SavedPath))Assert(pdf.PageCount==1);
            }
            finally
            {
                typeof(PdfEditorWindow).GetField("allowClose",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(window,true);
                window.Close();if(session!=null)PumpUntil(()=>!Directory.Exists(session.DirectoryPath));
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            return Task.CompletedTask;
        });
        Run("ordenacao compacta move itens e conserva paginas",()=>
        {
            var items=new ObservableCollection<string>(new[]{"a","b","c"});
            PageOrdering.Move(items,0,2);Assert(items.SequenceEqual(new[]{"b","c","a"}));
            return Task.CompletedTask;
        });
        Run("menu rapido habilita apenas operacoes compativeis",()=>
        {
            Assert(Operations.Supports(Operations.DocumentPdf,new[]{"C:\\a.doc","C:\\b.docx"}));
            Assert(Operations.Supports(Operations.ImagesPdf,new[]{"C:\\a.jpg","C:\\b.png"}));
            Assert(!Operations.Supports(Operations.Edit,new[]{"C:\\a.pdf","C:\\b.pdf"}));
            Assert(!Operations.Supports(Operations.DocumentPdf,new[]{"C:\\a.docx","C:\\b.pdf"}));
            Assert(!Operations.Supports(Operations.Print,new string[]{null}));
            return Task.CompletedTask;
        });
        Run("adaptador LibreOffice usa perfil isolado e preserva DOCX",async()=>
        {
            string dir=NewDirectory(),input=Path.Combine(dir,"documento.docx");File.WriteAllText(input,"fake test input");string hash=Hash(input);
            var runner=new OfficeRunner();
            string output=await new DocumentConversion("soffice-test",runner).ConvertAsync(input,dir,false,CancellationToken.None);
            Assert(File.Exists(output)&&runner.Arguments.Any(a=>a.StartsWith("-env:UserInstallation=file:///")));
            Assert(runner.Arguments.Contains("pdf:writer_pdf_Export")&&Hash(input)==hash&&Directory.GetDirectories(dir).Length==0);
        });
        Run("cancelamento encerra somente processo e filhos do conversor",async()=>
        {
            string dir=NewDirectory(),pid=Path.Combine(dir,"child.pid"),script=Path.Combine(dir,"child.ps1");
            File.WriteAllText(script,"$child=Start-Process -FilePath '"+Path.Combine(Environment.SystemDirectory,"ping.exe").Replace("'","''")+"' -ArgumentList '-n','30','127.0.0.1' -PassThru -WindowStyle Hidden\n[IO.File]::WriteAllText('"+pid.Replace("'","''")+"',[string]$child.Id)\nWait-Process -Id $child.Id\n");
            using(var cts=new CancellationTokenSource())
            {
                var operation=new ExternalProcess().RunAsync(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"),new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script},cts.Token);
                try
                {
                    for(int i=0;i<100&&!File.Exists(pid)&&!operation.IsCompleted;i++)await Task.Delay(100);
                    Assert(File.Exists(pid));int child=int.Parse(File.ReadAllText(pid));cts.Cancel();
                    await Throws<OperationCanceledException>(async()=>{await operation;});
                    try{using(var process=System.Diagnostics.Process.GetProcessById(child))Assert(process.HasExited);}catch(ArgumentException){}
                }
                finally{cts.Cancel();try{await operation;}catch(OperationCanceledException){}}
            }
        });
        Run("adaptador PDF DOCX informa filtro Writer e falhas",async()=>
        {
            string dir=NewDirectory(),input=Pdf(dir,"input.pdf","PDF");var runner=new OfficeRunner();
            await new DocumentConversion("soffice-test",runner).ConvertAsync(input,dir,true,CancellationToken.None);
            Assert(runner.Arguments.Contains("--infilter=writer_pdf_import")&&runner.Arguments.Contains("docx:Office Open XML Text"));
            await Throws<InvalidDataException>(()=>new DocumentConversion("soffice-test",new WarningRunner()).ConvertAsync(input,dir,true,CancellationToken.None));
            Assert(Directory.GetDirectories(dir).Length==0);
        });
        try {Record("INFO LibreOffice real: "+DocumentConversion.FindLibreOffice()+"; executar roteiro manual DOC/DOCX/PDF.");}
        catch(FileNotFoundException){Record("NAO EXECUTADO conversao real DOC/DOCX/PDF-DOCX: LibreOffice ausente. Adaptador testado com processo simulado.");}
        Record("NAO EXECUTADO impressao fisica/dialogo Windows, Explorer real e gestos/atalhos completos do editor: requer validacao manual. Abertura/preview/botao Salvar WPF exercitados.");
    }
    private static int DarkPixels(System.Drawing.Bitmap bitmap)
    {
        int count=0;for(int y=0;y<bitmap.Height;y+=2)for(int x=0;x<bitmap.Width;x+=2)
        {var c=bitmap.GetPixel(x,y);if(c.R<180&&c.G<180&&c.B<180)count++;}return count;
    }
    private static void PumpUntil(Func<bool> completed)
    {
        DateTime deadline=DateTime.UtcNow.AddSeconds(30);
        while(!completed())
        {
            if(DateTime.UtcNow>deadline)throw new TimeoutException("Interface nao concluiu em 30 segundos.");
            var frame=new System.Windows.Threading.DispatcherFrame();
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
    private static string FormPdf(string dir)
    {
        Func<string,string> stream=s=>"<< /Length "+s.Length+" /Type /XObject /Subtype /Form /BBox [0 0 15 15] >>\nstream\n"+s+"\nendstream";
        string[] objects={
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 11 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Annots [6 0 R 7 0 R 10 0 R] /Resources << /Font << /Helv 5 0 R >> >> /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Nome) /V (Antes) /Rect [10 220 180 250] /P 3 0 R /DA (/Helv 12 Tf 0 g) /F 4 >>",
            "<< /Type /Annot /Subtype /Widget /FT /Btn /T (Aceito) /V /Off /AS /Off /Rect [10 180 25 195] /P 3 0 R /AP << /N << /Yes 8 0 R /Off 9 0 R >> >> /F 4 >>",
            stream("0 0 15 15 re S 0 0 m 15 15 l S\n"),stream("0 0 15 15 re S\n"),
            "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 131072 /T (Opcao) /Opt [(Um) (Dois)] /I [0] /V (Um) /Rect [10 130 180 160] /P 3 0 R /DA (/Helv 12 Tf 0 g) /F 4 >>",
            "<< /Fields [6 0 R 7 0 R 10 0 R] /NeedAppearances true /DA (/Helv 12 Tf 0 g) /DR << /Font << /Helv 5 0 R >> >> >>"};
        string path=Path.Combine(dir,"form.pdf");var text=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>();
        for(int i=0;i<objects.Length;i++){offsets.Add(text.Length);text.Append((i+1)+" 0 obj\n"+objects[i]+"\nendobj\n");}
        int xref=text.Length;text.Append("xref\n0 "+(objects.Length+1)+"\n0000000000 65535 f \n");
        foreach(int offset in offsets)text.Append(offset.ToString("D10")+" 00000 n \n");
        text.Append("trailer\n<< /Size "+(objects.Length+1)+" /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");
        File.WriteAllText(path,text.ToString(),Encoding.ASCII);return path;
    }
    private static string Image(string dir,string name,MagickColor color)
    {
        string path=Path.Combine(dir,name);using(var image=new MagickImage(color,60,40))image.Write(path);return path;
    }
    private sealed class InlineProgress:IProgress<string>
    {
        private readonly Action<string> action;public InlineProgress(Action<string> action){this.action=action;}public void Report(string text)=>action(text);
    }
    private sealed class OfficeRunner:IExternalProcess
    {
        public string[] Arguments;
        public Task<ProcessResult> RunAsync(string executable,IEnumerable<string> arguments,CancellationToken token)
        {
            Arguments=arguments.ToArray();string dir=Arguments[Array.IndexOf(Arguments,"--outdir")+1];
            File.WriteAllText(Path.Combine(dir,"document"+(Arguments.Contains("pdf:writer_pdf_Export")?".pdf":".docx")),"test adapter output");
            return Task.FromResult(new ProcessResult{ExitCode=0});
        }
    }
}
