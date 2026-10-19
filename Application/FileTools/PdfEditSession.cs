// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;

namespace FileTools
{
    [DataContract] public sealed class PdfMark
    {
        [DataMember] public string Kind;
        [DataMember] public string Text;
        [DataMember] public string Asset;
        [DataMember] public double X, Y, Width, Height, Angle, FontSize;
    }
    [DataContract] public sealed class PdfEditPage
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Source;
        [DataMember] public int Index, SourceRotation, Rotation;
        [DataMember] public double Width, Height;
        [DataMember] public List<PdfMark> Marks = new List<PdfMark>();
        public int TotalRotation => ((SourceRotation+Rotation)%360+360)%360;
        public double ViewWidth => TotalRotation%180==0 ? Width : Height;
        public double ViewHeight => TotalRotation%180==0 ? Height : Width;
        public override string ToString() => "Página " + (Index+1) + " • " + (Rotation==0?"original":Rotation+"°");
    }
    [DataContract] public sealed class PdfFormValue
    {
        [DataMember] public string Name, Kind, Value;
        [DataMember] public List<string> Choices = new List<string>();
    }
    [DataContract] public sealed class PdfEditContent
    {
        [DataMember] public List<PdfEditPage> Pages = new List<PdfEditPage>();
        [DataMember] public List<PdfFormValue> Forms = new List<PdfFormValue>();
    }
    [DataContract] public sealed class PdfRecovery
    {
        [DataMember] public int Version = 1;
        [DataMember] public string OriginalPath, OriginalHash, SavedPath, SavedHash;
        [DataMember] public bool Dirty;
        [DataMember] public PdfEditContent Content;
    }

    public sealed class PdfEditSession
    {
        private PdfRecovery state;
        private readonly Stack<byte[]> undo = new Stack<byte[]>(), redo = new Stack<byte[]>();
        public string DirectoryPath { get; private set; }
        public string OriginalPath => state.OriginalPath;
        public string SavedPath => state.SavedPath;
        public bool Dirty => state.Dirty;
        public bool CanUndo => undo.Count>0;
        public bool CanRedo => redo.Count>0;
        public IReadOnlyList<PdfEditPage> Pages => state.Content.Pages.AsReadOnly();
        public IReadOnlyList<PdfFormValue> Forms => state.Content.Forms.AsReadOnly();
        public static string RecoveryRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FileTools","Recovery");
        private PdfEditSession() { }
        public static PdfEditSession Create(string input,string recoveryRoot=null)
        {
            var session=new PdfEditSession();
            string folder=Path.Combine(recoveryRoot??RecoveryRoot,Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); session.DirectoryPath=folder;
            try
            {
                File.Copy(WindowsPaths.ForIO(input),Path.Combine(folder,"source.pdf"));
                using(var document=PdfReader.Open(Path.Combine(folder,"source.pdf"),PdfDocumentOpenMode.Modify))
                {
                    bool hasForm=document.Internals.Catalog.Elements.ContainsKey("/AcroForm");
                    if(hasForm&&document.AcroForm.Elements.ContainsKey("/XFA")) throw new NotSupportedException("Este editor não suporta formulários XFA. Use um editor compatível.");
                    var content=new PdfEditContent();
                    for(int i=0;i<document.PageCount;i++)
                    {
                        var page=document.Pages[i];
                        content.Pages.Add(new PdfEditPage{Source="source.pdf",Index=i,Width=page.Width.Point,Height=page.Height.Point,SourceRotation=page.Rotate});
                    }
                    if(hasForm) ReadForms(document.AcroForm.Fields,"",content.Forms);
                    session.state=new PdfRecovery{OriginalPath=Path.GetFullPath(input),OriginalHash=Hash(Path.Combine(folder,"source.pdf")),Content=content};
                }
                session.Persist(); return session;
            }
            catch { Directory.Delete(folder,true); throw; }
        }
        private static void ReadForms(PdfAcroField.PdfAcroFieldCollection fields,string prefix,List<PdfFormValue> result)
        {
            for(int i=0;i<fields.Count;i++)
            {
                var field=fields[i];
                string name=prefix+field.Name;
                if(field.Elements.GetArray("/Kids")?.Elements.Count>0) {ReadForms(field.Fields,name+".",result);continue;}
                if(field.ReadOnly || field is PdfSignatureField)continue;
                var form=new PdfFormValue{Name=name};
                if(field is PdfTextField text) {form.Kind="text";form.Value=text.Text;}
                else if(field is PdfCheckBoxField check) {form.Kind="check";form.Value=check.Checked?"true":"false";}
                else if(field is PdfComboBoxField combo)
                {
                    form.Kind="choice"; form.Value=combo.SelectedIndex.ToString();
                    var opts=field.Elements.GetArray("/Opt");
                    if(opts!=null) foreach(PdfItem item in opts.Elements)
                        form.Choices.Add(item is PdfString str?str.Value:item is PdfArray arr && arr.Elements.Count>1?arr.Elements.GetString(1):item.ToString());
                }
                else continue;
                result.Add(form);
            }
        }
        public string AssetPath(string relative)
        {
            string root=Path.GetFullPath(DirectoryPath)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(Path.Combine(root,relative??""));
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Arquivo de recuperação inválido.");
            return full;
        }
        public PdfEditContent Capture() => Deserialize<PdfEditContent>(Serialize(state.Content));
        private void Change(Action action)
        {
            byte[] previous=Serialize(state.Content);
            try {action();}
            catch {state.Content=Deserialize<PdfEditContent>(previous);throw;}
            undo.Push(previous);
            if(undo.Count>50){var recent=undo.Take(50).Reverse().ToArray();undo.Clear();foreach(var item in recent)undo.Push(item);}
            redo.Clear();state.Dirty=true;Persist();
        }
        public void Undo() {if(!CanUndo)return;redo.Push(Serialize(state.Content));state.Content=Deserialize<PdfEditContent>(undo.Pop());state.Dirty=true;Persist();}
        public void Redo() {if(!CanRedo)return;undo.Push(Serialize(state.Content));state.Content=Deserialize<PdfEditContent>(redo.Pop());state.Dirty=true;Persist();}
        public void Rotate(string[] ids) => Change(()=> {foreach(var page in state.Content.Pages.Where(p=>ids.Contains(p.Id)))page.Rotation=(page.Rotation+90)%360;});
        public void Delete(string[] ids)
        {
            if(state.Content.Pages.Count(p=>!ids.Contains(p.Id))==0)throw new InvalidOperationException("O PDF deve manter pelo menos uma página.");
            Change(()=>state.Content.Pages.RemoveAll(p=>ids.Contains(p.Id)));
        }
        public void Move(string[] ids,int target)
        {
            Change(()=>
            {
                target=Math.Max(0,Math.Min(target,state.Content.Pages.Count));
                var chosen=state.Content.Pages.Where(p=>ids.Contains(p.Id)).ToList();
                int removedBefore=state.Content.Pages.Take(target).Count(p=>ids.Contains(p.Id));
                state.Content.Pages.RemoveAll(p=>ids.Contains(p.Id));
                state.Content.Pages.InsertRange(target-removedBefore,chosen);
            });
        }
        public void Insert(string path,int position)
        {
            string asset=Guid.NewGuid().ToString("N")+".pdf";
            File.Copy(WindowsPaths.ForIO(path),AssetPath(asset));
            var added=new List<PdfEditPage>();
            using(var pdf=PdfReader.Open(AssetPath(asset),PdfDocumentOpenMode.Import))
                for(int i=0;i<pdf.PageCount;i++)added.Add(new PdfEditPage{Source=asset,Index=i,Width=pdf.Pages[i].Width.Point,Height=pdf.Pages[i].Height.Point,SourceRotation=pdf.Pages[i].Rotate});
            Change(()=>state.Content.Pages.InsertRange(Math.Max(0,Math.Min(position,state.Content.Pages.Count)),added));
        }
        public void AddMark(string id,string kind,string text,double x,double y,double width=150,double height=50,double fontSize=14,string image=null)
        {
            if(!new[]{"text","image","signature","highlight","comment"}.Contains(kind))throw new ArgumentException("Ferramenta inválida.");
            var page=state.Content.Pages.Single(p=>p.Id==id);
            if(double.IsNaN(x)||double.IsNaN(y)||x<0||y<0||x>page.ViewWidth||y>page.ViewHeight||width<=0||height<=0||fontSize<4||fontSize>200)
                throw new ArgumentException("Posição ou tamanho inválido.");
            string asset=null;
            if(kind=="image"||kind=="signature")
            {
                asset=Guid.NewGuid().ToString("N")+Path.GetExtension(image);
                File.Copy(WindowsPaths.ForIO(image),AssetPath(asset));
            }
            var point=ViewToPage(page,x,y);
            Change(()=>page.Marks.Add(new PdfMark{Kind=kind,Text=text,Asset=asset,X=point.X,Y=point.Y,Width=width,Height=height,Angle=-page.TotalRotation,FontSize=fontSize}));
        }
        public static XPoint ViewToPage(PdfEditPage page,double x,double y)
        {
            switch(page.TotalRotation)
            {
                case 90:return new XPoint(y,page.Height-x);
                case 180:return new XPoint(page.Width-x,page.Height-y);
                case 270:return new XPoint(page.Width-y,x);
                default:return new XPoint(x,y);
            }
        }
        public void SetForm(string name,string value)
        {
            var form=state.Content.Forms.Single(f=>f.Name==name);
            if(form.Kind=="check" && value!="true" && value!="false")throw new ArgumentException("Use verdadeiro ou falso.");
            if(form.Kind=="choice" && (!int.TryParse(value,out int index)||index<0||index>=form.Choices.Count))throw new ArgumentException("Escolha inválida.");
            Change(()=>form.Value=value);
        }
        private void Persist()
        {
            string path=Path.Combine(DirectoryPath,"state.json"),temp=path+"."+Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp,Serialize(state));
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static string FindRecovery(string original)
        {
            if(!Directory.Exists(RecoveryRoot))return null;
            foreach(string dir in Directory.GetDirectories(RecoveryRoot).OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                try
                {
                    var session=Load(dir);
                    if(session.Dirty && string.Equals(Path.GetFullPath(original),session.OriginalPath,StringComparison.OrdinalIgnoreCase))return dir;
                }
                catch(IOException){}catch(SerializationException){}catch(ArgumentException){}
            }
            return null;
        }
        public static PdfEditSession Load(string directory)
        {
            var path=Path.Combine(directory,"state.json");
            try{return LoadState(directory,path);}
            catch(Exception ex) when(ex is IOException||ex is SerializationException||ex is ArgumentException)
            {
                if(!File.Exists(path+".bak"))throw;
                return LoadState(directory,path+".bak");
            }
        }
        private static PdfEditSession LoadState(string directory,string path)
        {
            if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Recuperação muito grande.");
            var session=new PdfEditSession{DirectoryPath=Path.GetFullPath(directory),state=Deserialize<PdfRecovery>(File.ReadAllBytes(path))};
            if(session.state?.Version!=1||session.state.Content?.Pages==null||session.state.Content.Forms==null||session.Pages.Count==0)throw new InvalidDataException("Recuperação inválida.");
            foreach(var page in session.Pages)
            {
                if(page==null||page.Marks==null||page.Index<0||!File.Exists(session.AssetPath(page.Source)))throw new InvalidDataException("Fonte de recuperação ausente.");
                foreach(var mark in page.Marks)if(mark.Asset!=null&&!File.Exists(session.AssetPath(mark.Asset)))throw new InvalidDataException("Imagem de recuperação ausente.");
            }
            return session;
        }
        public void Export(string destination,PdfEditContent content=null)
        {
            content=content??Capture();
            using(var document=PdfReader.Open(AssetPath("source.pdf"),PdfDocumentOpenMode.Modify))
            {
                var originalPages=document.Pages.Cast<PdfPage>().ToArray();
                foreach(var form in content.Forms)
                {
                    var field=document.AcroForm?.Fields[form.Name];
                    if(field is PdfTextField text){text.Font=new XFont("Arial",11);text.Text=form.Value??"";}
                    else if(field is PdfCheckBoxField check)check.Checked=form.Value=="true";
                    else if(field is PdfComboBoxField combo)combo.SelectedIndex=int.Parse(form.Value);
                }
                var retained=new HashSet<int>(content.Pages.Where(p=>p.Source=="source.pdf").Select(p=>p.Index));
                for(int i=originalPages.Length-1;i>=0;i--)if(!retained.Contains(i))document.Pages.RemoveAt(i);
                var imports=new Dictionary<string,PdfDocument>();
                try
                {
                    int outputIndex=0;
                    foreach(var item in content.Pages)
                    {
                        PdfPage page;
                        if(item.Source=="source.pdf")
                        {
                            page=originalPages[item.Index];
                            int current=document.Pages.Cast<PdfPage>().ToList().IndexOf(page);
                            document.Pages.MovePage(current,outputIndex);
                        }
                        else
                        {
                            if(!imports.ContainsKey(item.Source))imports[item.Source]=PdfReader.Open(AssetPath(item.Source),PdfDocumentOpenMode.Import);
                            page=document.Pages.Insert(outputIndex,imports[item.Source].Pages[item.Index]);
                        }
                        // Draw in unrotated page coordinates, then apply rotation to all content together.
                        int rotation=page.Rotate;page.Rotate=0;
                        foreach(var mark in item.Marks)
                        {
                            if(mark.Kind=="comment"||mark.Kind=="highlight"){AddAnnotation(document,page,item,mark);continue;}
                            using(var graphics=XGraphics.FromPdfPage(page,XGraphicsPdfPageOptions.Append))
                            {
                                graphics.TranslateTransform(mark.X,mark.Y);graphics.RotateTransform(mark.Angle);
                                if(mark.Kind=="text")
                                {
                                    var font=new XFont("Arial",mark.FontSize);
                                    double line=0;
                                    foreach(string text in (mark.Text??"").Split('\n')){graphics.DrawString(text,font,XBrushes.Black,new XPoint(0,line+mark.FontSize));line+=mark.FontSize*1.25;}
                                }
                                else using(var stream=File.OpenRead(AssetPath(mark.Asset)))
                                using(var image=XImage.FromStream(stream))
                                    graphics.DrawImage(image,0,0,mark.Width,mark.Height);
                            }
                        }
                        page.Rotate=item.TotalRotation;
                        outputIndex++;
                    }
                    PdfPublication.Save(document,destination);
                }
                finally{foreach(var source in imports.Values)source.Dispose();}
            }
        }
        private static void AddAnnotation(PdfDocument document,PdfPage page,PdfEditPage edit,PdfMark mark)
        {
            double a=mark.Angle*Math.PI/180,cos=Math.Cos(a),sin=Math.Sin(a);
            Func<double,double,XPoint> point=(x,y)=>new XPoint(mark.X+x*cos-y*sin,edit.Height-(mark.Y+x*sin+y*cos));
            var p0=point(0,0);var p1=point(mark.Width,0);var p2=point(0,mark.Height);var p3=point(mark.Width,mark.Height);
            var points=new[]{p0,p1,p2,p3};
            var annotation=new PdfTextAnnotation(document){Contents=mark.Text??"",Title="FileTools",Flags=PdfAnnotationFlags.Print,Icon=PdfTextAnnotationIcon.Comment};
            annotation.Rectangle=new PdfRectangle(new XPoint(points.Min(p=>p.X),points.Min(p=>p.Y)),new XPoint(points.Max(p=>p.X),points.Max(p=>p.Y)));
            if(mark.Kind=="highlight")
            {
                annotation.Elements.SetName("/Subtype","/Highlight");
                var quads=new PdfArray(document);
                foreach(var p in points){quads.Elements.Add(new PdfReal(p.X));quads.Elements.Add(new PdfReal(p.Y));}
                annotation.Elements["/QuadPoints"]=quads;
                annotation.Color=XColors.Yellow;annotation.Elements.SetReal("/CA",0.4);
            }
            page.Annotations.Add(annotation);
        }
        public Task<string> SaveAsync(string destination,bool allowOverwriteOriginal,CancellationToken token)
        {
            var content=Capture();
            return Task.Run(()=>
            {
                destination=WindowsPaths.ForIO(destination);
                bool original=string.Equals(WindowsPaths.ForShell(destination),WindowsPaths.ForShell(OriginalPath),StringComparison.OrdinalIgnoreCase);
                if(original&&!allowOverwriteOriginal)throw new InvalidOperationException("Confirme a substituição do original ou use Salvar como.");
                if(original&&Hash(destination)!=state.OriginalHash)throw new IOException("O original mudou fora do editor. Use Salvar como para preservar as duas versões.");
                if(File.Exists(destination)&&string.Equals(destination,state.SavedPath,StringComparison.OrdinalIgnoreCase)&&state.SavedHash!=null&&Hash(destination)!=state.SavedHash)
                    throw new IOException("O arquivo salvo mudou fora do editor. Use Salvar como.");
                string stage=WindowsPaths.ForIO(Path.Combine(Path.GetDirectoryName(destination),".filetools-save-"+Guid.NewGuid().ToString("N")+".pdf"));
                try
                {
                    token.ThrowIfCancellationRequested();Export(stage,content);token.ThrowIfCancellationRequested();
                    if(File.Exists(destination))
                    {
                        string backup=WindowsPaths.ForIO(Path.Combine(Path.GetDirectoryName(destination),Path.GetFileNameWithoutExtension(destination)+".filetools-backup-"+Guid.NewGuid().ToString("N")+".pdf"));
                        File.Replace(stage,destination,backup);
                    }
                    else File.Move(stage,destination);
                    state.SavedPath=destination;state.SavedHash=Hash(destination);state.Dirty=false;
                    if(original)state.OriginalHash=state.SavedHash;
                    Persist();return destination;
                }
                finally{if(File.Exists(stage))File.Delete(stage);}
            },token);
        }
        public Task<string> SaveCopyAsync(string directory,CancellationToken token)
        {
            var snapshot=Capture();
            return SaveNewAsync(directory,snapshot,token);
        }
        private async Task<string> SaveNewAsync(string directory,PdfEditContent snapshot,CancellationToken token)
        {
            string path=await new FileOutputManager().CreateAsync(directory,Path.GetFileNameWithoutExtension(OriginalPath)+"_editado.pdf",(temp,ct)=>Task.Run(()=>Export(temp,snapshot),ct),token).ConfigureAwait(false);
            state.SavedPath=path;state.SavedHash=Hash(path);state.Dirty=false;Persist();return path;
        }
        public void Discard()
        {
            string leaf=Path.GetFileName(DirectoryPath);
            if(!Guid.TryParseExact(leaf,"N",out _))throw new InvalidOperationException("Pasta de recuperação inesperada.");
            Directory.Delete(WindowsPaths.ForIO(DirectoryPath,true),true);
        }
        public static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(WindowsPaths.ForIO(path)))return Convert.ToBase64String(sha.ComputeHash(stream));}
        private static byte[] Serialize<T>(T item){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,item);return stream.ToArray();}}
        private static T Deserialize<T>(byte[] data){using(var stream=new MemoryStream(data))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
    }
}
