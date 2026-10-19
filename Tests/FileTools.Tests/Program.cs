// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileTools;
using ImageMagick;
using ShellExtension = FileConverterExtension.FileConverterExtension;

internal static partial class Tests
{
    private static string root, qpdf;
    private static int passed, failed;
    private static readonly List<string> report = new List<string>();
    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.Combine(Path.GetTempPath(), "FileTools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        qpdf = args.Length == 0 ? Dependencies.Qpdf : args[0];
        try
        {
            Run("qpdf real disponivel", async () => Assert((await new ExternalProcess().RunAsync(qpdf, new[] { "--version" }, CancellationToken.None)).ExitCode == 0));
            Run("uniao de 2 PDFs e ordem natural", () => MergeCount(2));
            Run("uniao de 10 PDFs e originais intactos", () => MergeCount(10));
            Run("acentos Unicode e pastas com espacos", async () =>
            {
                var dir = NewDirectory("ação 中文 espaços");
                string a = Pdf(dir, "relatório 10.pdf", "TEN"), b = Pdf(dir, "relatório 2.pdf", "TWO");
                string result = await new PdfOperations(qpdf).MergeAsync(new[] { a, b }, dir, false, CancellationToken.None);
                await AssertPage(result, 1, "TWO");
                Assert(File.Exists(a) && File.Exists(b));
            });
            Run("diretorios diferentes preservam destino da primeira entrada", async () =>
            {
                string a = Pdf(NewDirectory("primeira"), "z10.pdf", "TEN"), b = Pdf(NewDirectory("segunda"), "a2.pdf", "TWO");
                string result = (await FileTools.Program.ExecuteAsync(Operations.Merge, new[] { a, b }, Path.GetDirectoryName(a), null, null, CancellationToken.None))[0];
                Assert(Path.GetDirectoryName(result) == Path.GetDirectoryName(a));
                await AssertPage(result, 1, "TWO");
            });
            Run("nomes em conflito geram (2) e (3)", async () =>
            {
                var dir = NewDirectory();
                string a = Pdf(dir,"a.pdf","A"), b = Pdf(dir,"b.pdf","B");
                var pdf = new PdfOperations(qpdf);
                string first = await pdf.MergeAsync(new[] {a,b},dir,false,CancellationToken.None);
                string digest = Hash(first);
                string second = await pdf.MergeAsync(new[] {a,b},dir,false,CancellationToken.None);
                string third = await pdf.MergeAsync(new[] {a,b},dir,false,CancellationToken.None);
                Assert(Path.GetFileName(second) == "Documentos_unificados (2).pdf");
                Assert(Path.GetFileName(third) == "Documentos_unificados (3).pdf");
                Assert(Hash(first) == digest);
            });
            Run("concorrencia nao sobrescreve saidas", async () =>
            {
                string dir = NewDirectory();
                var tasks = Enumerable.Range(0,8).Select(i => new FileOutputManager().CreateAsync(dir, "resultado.txt",
                    (p,t) => Task.Run(() => File.WriteAllText(p, i.ToString())), CancellationToken.None)).ToArray();
                string[] results = await Task.WhenAll(tasks);
                Assert(results.Distinct().Count() == 8);
                Assert(results.Select(File.ReadAllText).Distinct().Count() == 8);
            });
            Run("pasta sem permissao de escrita (ACL real)", async () =>
            {
                string dir = NewDirectory();
                var acl = Directory.GetAccessControl(dir);
                var identity = WindowsIdentity.GetCurrent().User;
                var deny = new FileSystemAccessRule(identity, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
                var restricted = Directory.GetAccessControl(dir);
                restricted.AddAccessRule(deny);
                Directory.SetAccessControl(dir, restricted);
                try { await Throws<UnauthorizedAccessException>(() => { FileOutputManager.EnsureWritable(dir); return Task.CompletedTask; }); }
                finally { restricted.RemoveAccessRuleSpecific(deny); Directory.SetAccessControl(dir, restricted); }
                FileOutputManager.EnsureWritable(dir);
                Assert(!Directory.EnumerateFileSystemEntries(dir).Any());
            });
            Run("PDF corrompido falha sem residuos", async () =>
            {
                string dir = NewDirectory(), good = Pdf(dir,"bom.pdf","GOOD"), bad = Path.Combine(dir,"corrompido.pdf");
                File.WriteAllText(bad,"%PDF-1.7\nINVALID");
                await Throws<InvalidDataException>(() => new PdfOperations(qpdf).MergeAsync(new[] {good,bad},dir,false,CancellationToken.None));
                Assert(Directory.GetFiles(dir).Length == 2 && Directory.GetDirectories(dir).Length == 0);
            });
            Run("PDF grande 32 MiB", async () =>
            {
                string dir = NewDirectory(), large = Pdf(dir,"grande.pdf","LARGE",32*1024*1024), small = Pdf(dir,"pequeno.pdf","SMALL");
                string digest = Hash(large);
                string result = await new PdfOperations(qpdf).MergeAsync(new[] {large,small},dir,false,CancellationToken.None);
                Assert(await new PdfOperations(qpdf).PageCountAsync(result,CancellationToken.None) == 2);
                Assert(Hash(large) == digest);
            });
            Run("cancelamento antes da operacao nao cria saida", async () =>
            {
                string dir = NewDirectory(), a = Pdf(dir,"a.pdf","A"), b = Pdf(dir,"b.pdf","B");
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    await Throws<OperationCanceledException>(() => new PdfOperations(qpdf).MergeAsync(new[] {a,b},dir,false,cts.Token));
                }
                Assert(Directory.GetDirectories(dir).Length == 0 && Directory.GetFiles(dir).Length == 2);
            });
            Run("cancelamento durante processo externo", async () =>
            {
                using (var cts = new CancellationTokenSource(250))
                    await Throws<OperationCanceledException>(() => new ExternalProcess().RunAsync(
                        Path.Combine(Environment.SystemDirectory,"ping.exe"),new[] {"-n","30","127.0.0.1"},cts.Token));
            });
            Run("cancelamento durante producao limpa temporarios", async () =>
            {
                string dir = NewDirectory();
                using (var cts = new CancellationTokenSource())
                {
                    await Throws<OperationCanceledException>(() => new FileOutputManager().CreateAsync(dir,"result.pdf",async (p,t) =>
                    {
                        File.WriteAllText(p,"incomplete"); cts.Cancel(); await Task.Delay(10,t);
                    },cts.Token));
                }
                Assert(!Directory.EnumerateFileSystemEntries(dir).Any());
            });
            Run("dependencia externa ausente", async () =>
            {
                string dir = NewDirectory(), a = Pdf(dir,"a.pdf","A"), b = Pdf(dir,"b.pdf","B");
                await Throws<System.ComponentModel.Win32Exception>(() => new PdfOperations(Path.Combine(dir,"missing.exe")).MergeAsync(new[] {a,b},dir,false,CancellationToken.None));
                Assert(Directory.GetDirectories(dir).Length == 0);
            });
            Run("falha de conversor remove saida parcial", async () =>
            {
                string dir = NewDirectory();
                await Throws<IOException>(() => new FileOutputManager().CreateAsync(dir,"x.pdf",(p,t) =>
                {
                    File.WriteAllText(p,"partial"); throw new IOException("simulated");
                },CancellationToken.None));
                Assert(!Directory.EnumerateFileSystemEntries(dir).Any());
            });
            Run("conversor sem saida nao publica arquivo vazio", async () =>
            {
                string dir = NewDirectory();
                await Throws<InvalidDataException>(() => new FileOutputManager().CreateAsync(dir,"x.pdf",(p,t) =>
                {
                    File.WriteAllText(p,""); return Task.CompletedTask;
                },CancellationToken.None));
                Assert(!Directory.EnumerateFileSystemEntries(dir).Any());
            });
            Run("avisos qpdf sao apresentados como falha", async () =>
            {
                string dir = NewDirectory(), a = Pdf(dir,"a.pdf","A"), b = Pdf(dir,"b.pdf","B");
                await Throws<InvalidDataException>(() => new PdfOperations(qpdf,new WarningRunner()).MergeAsync(new[] {a,b},dir,false,CancellationToken.None));
            });
            Run("divisao individual e preservacao do PDF", async () =>
            {
                string dir = NewDirectory(), input = await MakeMerged(dir,3);
                string digest = Hash(input);
                string[] split = await new PdfOperations(qpdf).SplitAsync(input,dir,null,null,CancellationToken.None);
                Assert(split.Length == 3);
                foreach (string path in split) Assert(await new PdfOperations(qpdf).PageCountAsync(path,CancellationToken.None) == 1);
                Assert(Hash(input) == digest);
                Assert(split.All(p => Path.GetDirectoryName(p) != dir));
            });
            Run("divisao por intervalos", async () =>
            {
                string dir = NewDirectory(), input = await MakeMerged(dir,10);
                string[] split = await new PdfOperations(qpdf).SplitAsync(input,dir,"1-5;6-10",null,CancellationToken.None);
                Assert(split.Length == 2);
                foreach (string path in split) Assert(await new PdfOperations(qpdf).PageCountAsync(path,CancellationToken.None) == 5);
            });
            Run("extracao de paginas selecionadas", async () =>
            {
                string dir = NewDirectory(), input = await MakeMerged(dir,5);
                string result = (await new PdfOperations(qpdf).SplitAsync(input,dir,"1,3,5",null,CancellationToken.None))[0];
                Assert(await new PdfOperations(qpdf).PageCountAsync(result,CancellationToken.None) == 3);
                await AssertPage(result,2,"MARK3");
            });
            Run("intervalos invalidos nao criam pasta", async () =>
            {
                string dir = NewDirectory(), input = Pdf(dir,"a.pdf","A");
                foreach (string value in new[] {"0","2","1-0","1;;1","a","9999999999999"})
                    await Throws<ArgumentException>(() => new PdfOperations(qpdf).SplitAsync(input,dir,value,null,CancellationToken.None));
                Assert(Directory.GetDirectories(dir).Length == 0);
            });
            Run("reordenacao explicita prevalece sobre ordem natural", async () =>
            {
                string dir = NewDirectory(), a = Pdf(dir,"1.pdf","FIRST"), b = Pdf(dir,"2.pdf","SECOND");
                string result = await new PdfOperations(qpdf).MergeAsync(new[] {b,a},dir,true,CancellationToken.None);
                await AssertPage(result,1,"SECOND");
            });
            Run("manifesto Unicode extenso preserva toda a selecao", () =>
            {
                var paths = Enumerable.Range(1,1200).Select(i => Path.Combine(root,"pasta ação 中文",new string('x',80) + i + ".pdf")).ToArray();
                string request = SelectionTransport.Write(Operations.Merge,paths);
                var restored = SelectionTransport.Read(request,true);
                Assert(restored.Files.SequenceEqual(paths) && !File.Exists(request));
                return Task.CompletedTask;
            });
            Run("solicitacoes concorrentes tem nomes exclusivos", () =>
            {
                string[] paths = {Path.Combine(root,"a.pdf"),Path.Combine(root,"b.pdf")};
                string[] requests = Enumerable.Range(0,20).AsParallel().Select(i => SelectionTransport.Write(Operations.Merge,paths)).ToArray();
                Assert(requests.Distinct().Count() == requests.Length);
                foreach (string request in requests) SelectionTransport.Read(request,true);
                return Task.CompletedTask;
            });
            Run("manifesto externo nao e lido nem excluido", async () =>
            {
                string path = Path.Combine(NewDirectory(),Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(path,"{}");
                await Throws<InvalidDataException>(() => {SelectionTransport.Read(path,true); return Task.CompletedTask;});
                Assert(File.Exists(path));
            });
            Run("selecao mista e duplicada e recusada", async () =>
            {
                Assert(!Operations.Supports(Operations.Merge,new[] {"C:\\a.pdf","C:\\b.png"}));
                await Throws<InvalidDataException>(() => {SelectionTransport.Write(Operations.Merge,new[] {"C:\\a.pdf","C:\\a.pdf"});return Task.CompletedTask;});
            });
            Run("menu esconde comandos incompativeis", () =>
            {
                using (var menu = ShellExtension.BuildFileToolsMenu(new[] {"C:\\a.pdf"},(op,files)=>{}))
                {
                    var rootItem = (ToolStripMenuItem)menu.Items[0];
                    var pdf = rootItem.DropDownItems.OfType<ToolStripMenuItem>().Single(i=>i.Text=="PDF");
                    Assert(rootItem.Text == "FileTools" && pdf.DropDownItems.Find(Operations.Merge,false).Length == 0);
                    Assert(pdf.DropDownItems.Find(Operations.Split,false).Length == 1);
                    Assert(rootItem.DropDownItems.Find(Operations.Edit,false).Length == 1);
                    Assert(rootItem.DropDownItems.Find(Operations.Print,false).Length == 1);
                }
                return Task.CompletedTask;
            });
            Run("menu captura snapshot e entrega todos os caminhos", () =>
            {
                string[] selected = {Path.Combine(root,"ação 1.pdf"),Path.Combine(root,"ação 2.pdf")};
                string[] original = selected.ToArray(), received = null;
                using (var menu = ShellExtension.BuildFileToolsMenu(selected,(op,files) =>
                {
                    string request = SelectionTransport.Write(op,files);
                    received = SelectionTransport.Read(request,true).Files;
                }))
                {
                    selected[0] = Path.Combine(root,"changed.pdf");
                    var rootItem = (ToolStripMenuItem)menu.Items[0];
                    ((ToolStripMenuItem)rootItem.DropDownItems.Find(Operations.Merge,false)[0]).PerformClick();
                }
                Assert(received.SequenceEqual(original));
                return Task.CompletedTask;
            });
            Run("Shell API IShellExtInit recebe CF_HDROP multiplo", () =>
            {
                string dir = NewDirectory("shell seleção");
                string[] expected = {Pdf(dir,"ação 1.pdf","A"),Pdf(dir,"ação 2.pdf","B")};
                var data = new DataObject();
                var files = new StringCollection(); files.AddRange(expected); data.SetFileDropList(files);
                IntPtr pointer = Marshal.GetComInterfaceForObject(data,typeof(System.Runtime.InteropServices.ComTypes.IDataObject));
                var extension = new ShellExtension();
                {
                    try { ((SharpShell.Interop.IShellExtInit)extension).Initialize(IntPtr.Zero,pointer,IntPtr.Zero); }
                    finally { Marshal.Release(pointer); }
                    var property = typeof(ShellExtension).GetProperty("SelectedItemPaths",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    var selected = ((IEnumerable<string>)property.GetValue(extension)).ToArray();
                    Assert(selected.SequenceEqual(expected));
                    var show = typeof(ShellExtension).GetMethod("CanShowMenu",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    Assert((bool)show.Invoke(extension,null));
                }
                return Task.CompletedTask;
            });
            Run("caminho longo acima de 260 caracteres", async () =>
            {
                string dir = NewDirectory();
                for (int i=0;i<5;i++) dir = Path.Combine(dir,new string('á',45));
                Directory.CreateDirectory(WindowsPaths.ForIO(dir));
                string a=Pdf(dir,"ação 1.pdf","A"), b=Pdf(dir,"ação 2.pdf","B");
                Assert(a.Length > 260);
                string result=await new PdfOperations(qpdf).MergeAsync(new[]{a,b},dir,false,CancellationToken.None);
                Assert(await new PdfOperations(qpdf).PageCountAsync(result,CancellationToken.None)==2);
            });
            Run("PNG para JPG WebP e PNG preserva originais", async () =>
            {
                string dir=NewDirectory(), source=Path.Combine(dir,"imagem ação.png");
                using(var image=new MagickImage(MagickColors.Red,64,32)) image.Write(source,MagickFormat.Png);
                string digest=Hash(source);
                foreach(string format in new[]{"jpg","webp","png"})
                {
                    string result=await new ImageConversion().ConvertAsync(source,dir,format,CancellationToken.None);
                    using(var read=new MagickImage(result)) Assert(read.Width==64 && read.Height==32);
                    Assert(result!=source);
                }
                Assert(Hash(source)==digest);
            });
            Run("lote de imagens produz todos os resultados", async () =>
            {
                string dir=NewDirectory();
                string[] inputs=Enumerable.Range(1,3).Select(i=>Path.Combine(dir,"img "+i+".png")).ToArray();
                foreach(string path in inputs) using(var image=new MagickImage(MagickColors.Blue,40,20)) image.Write(path);
                string[] results=await FileTools.Program.ExecuteAsync("image-webp",inputs,dir,null,null,CancellationToken.None);
                Assert(results.Length==3 && results.All(File.Exists) && inputs.All(File.Exists));
            });
            Run("entradas JPG WebP BMP TIFF GIF para PNG", async () =>
            {
                string dir=NewDirectory();
                foreach (string extension in new[]{"jpg","webp","bmp","tiff","gif"})
                {
                    string source=Path.Combine(dir,"entrada."+extension);
                    using(var image=new MagickImage(MagickColors.Green,37,19)) image.Write(source);
                    string digest=Hash(source);
                    string result=await new ImageConversion().ConvertAsync(source,dir,"png",CancellationToken.None);
                    using(var image=new MagickImage(result)) Assert(image.Width==37 && image.Height==19);
                    Assert(Hash(source)==digest);
                }
            });
            Run("fila serial permite cancelar trabalho em espera", async () =>
            {
                using(var entered=new ManualResetEventSlim())
                using(var release=new ManualResetEventSlim())
                using(var cts=new CancellationTokenSource())
                {
                    var first=BatchOperations.RunAsync(async () => {entered.Set(); await Task.Run(()=>release.Wait()); return 1;},CancellationToken.None);
                    Assert(entered.Wait(5000));
                    try
                    {
                        var waiting=BatchOperations.RunAsync(()=>Task.FromResult(2),cts.Token);
                        cts.CancelAfter(100);
                        await Throws<OperationCanceledException>(async ()=> {await waiting;});
                    }
                    finally {release.Set(); await first;}
                }
            });
            Run("fila recupera mutex abandonado por processo", async () =>
            {
                string user=WindowsIdentity.GetCurrent().User.Value;
                using(var lease=new Mutex(false,"Local\\FileTools.Worker."+user))
                {
                    var thread=new Thread(()=>lease.WaitOne());
                    thread.Start(); Assert(thread.Join(5000));
                    using(var cts=new CancellationTokenSource(5000))
                        Assert(await BatchOperations.RunAsync(()=>Task.FromResult(7),cts.Token)==7);
                }
            });
            Run("imagem corrompida nao deixa temporarios", async () =>
            {
                string dir=NewDirectory(), source=Path.Combine(dir,"bad.png"); File.WriteAllText(source,"invalid");
                await Throws<MagickException>(() => new ImageConversion().ConvertAsync(source,dir,"jpg",CancellationToken.None));
                Assert(Directory.GetDirectories(dir).Length==0 && Directory.GetFiles(dir).Length==1);
            });
            NewFeatures();
        }
        finally
        {
            // Delete only the uniquely generated test directory verified under TEMP.
            if (root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(WindowsPaths.ForIO(root,true),true);
            report.Add("RESULTADO: " + passed + " aprovados, " + failed + " falhos.");
            report.Add("Explorer interativo / instalacao COM: NAO EXECUTADOS. Harness executado no Windows 10.");
            Console.WriteLine(report[report.Count-2]);
            string resultsDir=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..","..","..","TestResults"));
            Directory.CreateDirectory(resultsDir);
            File.WriteAllLines(Path.Combine(resultsDir,"results.txt"),report,Encoding.UTF8);
        }
        return failed==0?0:1;
    }
    private static void Run(string name,Func<Task> test)
    {
        try {SynchronizationContext.SetSynchronizationContext(null);test().GetAwaiter().GetResult();passed++; Record("PASS " + name);}
        catch(Exception ex) {failed++;Record("FAIL " + name + ": " + ex.ToString());}
    }
    private static void Record(string text) {report.Add(text);Console.WriteLine(text);}
    private static void Assert(bool condition) {if(!condition)throw new Exception("Verificacao falhou.");}
    private static async Task Throws<T>(Func<Task> action) where T:Exception
    {
        try {await action();} catch(T) {return;} throw new Exception("Esperava " + typeof(T).Name);
    }
    private static string NewDirectory(string suffix="case")
    {
        string dir=Path.Combine(root,suffix+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return dir;
    }
    private static string Hash(string path)
    {
        using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return Convert.ToBase64String(sha.ComputeHash(file));
    }
    private static async Task MergeCount(int count)
    {
        string dir=NewDirectory(), merged=await MakeMerged(dir,count);
        Assert(await new PdfOperations(qpdf).PageCountAsync(merged,CancellationToken.None)==count);
        await AssertPage(merged,1,"MARK1");
        if(count==10)await AssertPage(merged,2,"MARK2");
    }
    private static async Task<string> MakeMerged(string dir,int count)
    {
        string[] paths=Enumerable.Range(1,count).Select(i=>Pdf(dir,"doc"+i+".pdf","MARK"+i)).Reverse().ToArray();
        string[] hashes=paths.Select(Hash).ToArray();
        string output=await new PdfOperations(qpdf).MergeAsync(paths,dir,false,CancellationToken.None);
        Assert(paths.Select(Hash).SequenceEqual(hashes));
        return output;
    }
    private static async Task AssertPage(string path,int page,string marker)
    {
        string output=Path.Combine(Path.GetDirectoryName(path),"verify-"+Guid.NewGuid().ToString("N")+".pdf");
        try
        {
            var result=await new ExternalProcess().RunAsync(qpdf,new[]{"--qdf","--object-streams=disable","--empty","--pages",path,page.ToString(),"--",output},CancellationToken.None);
            Assert(result.ExitCode==0 && Encoding.ASCII.GetString(File.ReadAllBytes(output)).Contains("("+marker+")"));
        }
        finally {if(File.Exists(output))File.Delete(output);}
    }
    private static string Pdf(string dir,string name,string marker,int padding=0)
    {
        string path=WindowsPaths.ForIO(Path.Combine(dir,name));
        using(var stream=File.Create(path))
        {
            var offsets=new List<long>{0};
            Action<string> write=s=>{byte[] data=Encoding.ASCII.GetBytes(s);stream.Write(data,0,data.Length);};
            write("%PDF-1.4\n");
            Action<int,string> obj=(i,body)=>{offsets.Add(stream.Position);write(i+" 0 obj\n"+body+"\nendobj\n");};
            obj(1,"<< /Type /Catalog /Pages 2 0 R >>");
            obj(2,"<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            obj(3,"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
            string content="BT /F1 12 Tf 20 50 Td ("+marker+") Tj ET\n";
            offsets.Add(stream.Position);write("4 0 obj\n<< /Length "+(content.Length+padding)+" >>\nstream\n"+content);
            byte[] spaces=Enumerable.Repeat((byte)' ',8192).ToArray();
            for(int left=padding;left>0;left-=Math.Min(left,spaces.Length))stream.Write(spaces,0,Math.Min(left,spaces.Length));
            write("\nendstream\nendobj\n");
            obj(5,"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            long xref=stream.Position;write("xref\n0 6\n0000000000 65535 f \n");
            foreach(long offset in offsets.Skip(1))write(offset.ToString("D10")+" 00000 n \n");
            write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");
        }
        return path;
    }
    private sealed class WarningRunner:IExternalProcess
    {
        public Task<ProcessResult> RunAsync(string executable,IEnumerable<string> arguments,CancellationToken token)=>
            Task.FromResult(new ProcessResult{ExitCode=3,Output="",Error="warning"});
    }
}
