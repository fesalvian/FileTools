// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileTools
{
    public static class PdfVisuals
    {
        public static BitmapSource ToSource(Bitmap bitmap)
        {
            var bits=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var source=BitmapSource.Create(bitmap.Width,bitmap.Height,96,96,PixelFormats.Bgra32,null,bits.Scan0,bits.Stride*bitmap.Height,bits.Stride);
                source.Freeze();return source;
            }
            finally{bitmap.UnlockBits(bits);}
        }
    }
    public sealed class PdfPrintPage
    {
        public string Path {get;set;}
        public int Index {get;set;}
    }
    public sealed class PdfBatchPaginator : DocumentPaginator
    {
        private readonly PdfPrintPage[] pages;
        private System.Windows.Size size;
        public PdfBatchPaginator(IEnumerable<PdfPrintPage> pages,System.Windows.Size size) {this.pages=pages.ToArray();PageSize=size;}
        public override bool IsPageCountValid=>true;
        public override int PageCount=>pages.Length;
        public override IDocumentPaginatorSource Source=>null;
        public override System.Windows.Size PageSize {get=>size;set {if(value.Width<=0||value.Height<=0)throw new ArgumentException("Papel inválido.");size=value;}}
        public override DocumentPage GetPage(int pageNumber)
        {
            if(pageNumber<0||pageNumber>=pages.Length)return DocumentPage.Missing;
            var item=pages[pageNumber];
            using(var pdf=new PdfRenderer(item.Path))
            using(var bitmap=pdf.Render(item.Index,200))
            {
                double scale=Math.Min((size.Width-16)/bitmap.Width,(size.Height-16)/bitmap.Height);
                var bounds=new Rect((size.Width-bitmap.Width*scale)/2,(size.Height-bitmap.Height*scale)/2,bitmap.Width*scale,bitmap.Height*scale);
                var visual=new DrawingVisual();
                using(var draw=visual.RenderOpen()) {draw.DrawRectangle(System.Windows.Media.Brushes.White,null,new Rect(new System.Windows.Point(),size));draw.DrawImage(PdfVisuals.ToSource(bitmap),bounds);}
                return new DocumentPage(visual,size,new Rect(new System.Windows.Point(),size),bounds);
            }
        }
    }
    internal static class PdfPrinting
    {
        public static async Task PrintAsync(string[] paths)
        {
            if(paths.Length>1){paths=MergeWindow.Select(paths,"Continuar para impressão");if(paths==null)return;}
            var pages=await Task.Run(()=>
            {
                var result=new List<PdfPrintPage>();
                foreach(string path in paths)using(var document=new PdfRenderer(path))
                    for(int i=0;i<document.PageCount;i++)result.Add(new PdfPrintPage{Path=path,Index=i});
                return result;
            });
            var dialog=new System.Windows.Controls.PrintDialog{UserPageRangeEnabled=true,MinPage=1,MaxPage=(uint)pages.Count};
            // ShowDialog is the only point that authorizes spooling. Never set a default printer or persist a ticket.
            if(dialog.ShowDialog()!=true)return;
            if(dialog.PageRangeSelection==PageRangeSelection.UserPages)
            {
                int first=dialog.PageRange.PageFrom,last=dialog.PageRange.PageTo;
                if(first<1||last<first||last>pages.Count)throw new ArgumentException("Intervalo de impressão inválido.");
                pages=pages.Skip(first-1).Take(last-first+1).ToList();
            }
            dialog.PrintDocument(new PdfBatchPaginator(pages,new System.Windows.Size(dialog.PrintableAreaWidth,dialog.PrintableAreaHeight)),"FileTools — "+paths.Length+" documento(s)");
        }
    }
}
