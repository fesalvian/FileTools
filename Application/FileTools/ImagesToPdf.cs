// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FileTools
{
    public static class ImagesToPdf
    {
        public static Task<string> ConvertAsync(string[] images, string destination, bool merge, CancellationToken token)
        {
            if (images.Length == 0) throw new ArgumentException("Selecione imagens.");
            string name = merge ? "Imagens_unificadas.pdf" : Path.GetFileNameWithoutExtension(images[0]) + ".pdf";
            return new FileOutputManager().CreateAsync(destination,name,(temporary,ct) => Task.Run(() =>
            {
                using (var document = new PdfDocument())
                {
                    foreach (string path in images.OrderBy(p => p, new NaturalPathComparer()))
                    {
                        ct.ThrowIfCancellationRequested();
                        using (var stream = File.OpenRead(WindowsPaths.ForIO(path)))
                        using (var image = XImage.FromStream(stream))
                        {
                            var page = document.AddPage();
                            if (image.PixelWidth > image.PixelHeight) page.Orientation = PdfSharp.PageOrientation.Landscape;
                            double scale = Math.Min((page.Width.Point - 36) / image.PointWidth, (page.Height.Point - 36) / image.PointHeight);
                            double width = image.PointWidth * scale, height = image.PointHeight * scale;
                            using (var graphics = XGraphics.FromPdfPage(page))
                                graphics.DrawImage(image,(page.Width.Point-width)/2,(page.Height.Point-height)/2,width,height);
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    PdfPublication.Save(document,temporary);
                }
            },ct),token);
        }
    }
}
