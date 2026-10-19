// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;

namespace FileTools
{
    internal static class PdfPublication
    {
        public static void Save(PdfDocument document,string destination)
        {
            // PDFsharp 6.2.4 emits Flate streams without an Adler-32 trailer.
            // Let the existing qpdf engine compress new streams correctly instead.
            // Source: PDFsharp v6.2.4 Pdf.Filters/FlateDecode.cs, Encode.
            document.Options.NoCompression=true;
            document.Options.CompressContentStreams=false;
            string raw=WindowsPaths.ForIO(Path.Combine(Path.GetDirectoryName(destination),".filetools-raw-"+Guid.NewGuid().ToString("N")+".pdf"));
            try
            {
                document.Save(raw);
                new PdfOperations(Dependencies.Qpdf).NormalizeAsync(raw,destination,CancellationToken.None).GetAwaiter().GetResult();
            }
            finally{if(File.Exists(raw))File.Delete(raw);}
        }
    }
}
