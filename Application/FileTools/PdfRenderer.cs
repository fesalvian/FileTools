// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTools
{
    // PDFium is intentionally referenced only by the worker. Its C API uses UTF-8 file names.
    public sealed class PdfRenderer : IDisposable
    {
        private static readonly object gate = new object();
        private static bool initialized;
        private IntPtr document, form, formInfo;
        public int PageCount { get; private set; }
        public PdfRenderer(string path)
        {
            lock (gate)
            {
                if (!initialized) { Native.FPDF_InitLibrary(); initialized = true; }
                document = Native.FPDF_LoadDocument(Encoding.UTF8.GetBytes(WindowsPaths.ForIO(path) + "\0"), IntPtr.Zero);
                if (document == IntPtr.Zero) throw new InvalidDataException("PDFium não pôde abrir o PDF. Verifique integridade e senha.");
                PageCount = Native.FPDF_GetPageCount(document);
                if (PageCount <= 0) { Dispose(); throw new InvalidDataException("PDF sem páginas."); }
                // All callbacks are optional for static rendering; no JavaScript or external file callbacks.
                formInfo = Marshal.AllocHGlobal(2048);
                Marshal.Copy(new byte[2048], 0, formInfo, 2048);
                Marshal.WriteInt32(formInfo, 2);
                form = Native.FPDFDOC_InitFormFillEnvironment(document, formInfo);
            }
        }
        public SizeF PageSize(int index)
        {
            lock (gate)
            {
                IntPtr page = LoadPage(index);
                try { return new SizeF((float)Native.FPDF_GetPageWidth(page), (float)Native.FPDF_GetPageHeight(page)); }
                finally { Native.FPDF_ClosePage(page); }
            }
        }
        private IntPtr LoadPage(int index)
        {
            if (document == IntPtr.Zero) throw new ObjectDisposedException(nameof(PdfRenderer));
            if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
            var page = Native.FPDF_LoadPage(document, index);
            if (page == IntPtr.Zero) throw new InvalidDataException("Página PDF indisponível.");
            return page;
        }
        public Bitmap Render(int index, int dpi = 150, int maximumPixels = 24000000)
        {
            if (dpi < 36 || dpi > 600) throw new ArgumentOutOfRangeException(nameof(dpi));
            if (maximumPixels < 1) throw new ArgumentOutOfRangeException(nameof(maximumPixels));
            lock (gate)
            {
                var page = LoadPage(index);
                try
                {
                    double width = Native.FPDF_GetPageWidth(page) * dpi / 72.0;
                    double height = Native.FPDF_GetPageHeight(page) * dpi / 72.0;
                    double scale = Math.Min(1, Math.Sqrt(maximumPixels / (width * height)));
                    int w = Math.Max(1, (int)(width * scale)), h = Math.Max(1, (int)(height * scale));
                    var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    try
                    {
                        var data = bitmap.LockBits(new Rectangle(0,0,w,h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        IntPtr native = IntPtr.Zero;
                        try
                        {
                            native = Native.FPDFBitmap_CreateEx(w,h,4,data.Scan0,data.Stride);
                            if (native == IntPtr.Zero) throw new OutOfMemoryException("Não foi possível renderizar a página.");
                            Native.FPDFBitmap_FillRect(native,0,0,w,h,0xffffffff);
                            Native.FPDF_RenderPageBitmap(native,page,0,0,w,h,0,1);
                            if (form != IntPtr.Zero)
                            {
                                Native.FORM_OnAfterLoadPage(page,form);
                                Native.FPDF_FFLDraw(form,native,page,0,0,w,h,0,1);
                                Native.FORM_OnBeforeClosePage(page,form);
                            }
                        }
                        finally { if (native != IntPtr.Zero) Native.FPDFBitmap_Destroy(native); bitmap.UnlockBits(data); }
                        return bitmap;
                    }
                    catch { bitmap.Dispose(); throw; }
                }
                finally { Native.FPDF_ClosePage(page); }
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (form != IntPtr.Zero) { Native.FPDFDOC_ExitFormFillEnvironment(form); form = IntPtr.Zero; }
                if (formInfo != IntPtr.Zero) { Marshal.FreeHGlobal(formInfo); formInfo = IntPtr.Zero; }
                if (document != IntPtr.Zero) { Native.FPDF_CloseDocument(document); document = IntPtr.Zero; }
            }
        }
        public static async Task<string[]> ExportAsync(string path, string directory, string format, IProgress<string> progress, CancellationToken token)
        {
            if (format != "jpg" && format != "png") throw new ArgumentException("Formato inválido.");
            return await Task.Run(async () =>
            {
                using (var reader = new PdfRenderer(path))
                {
                    string folder = FileOutputManager.CreateOutputDirectory(directory, Path.GetFileNameWithoutExtension(path) + "_" + format);
                    var outputs = new System.Collections.Generic.List<string>();
                    try
                    {
                        for (int index = 0; index < reader.PageCount; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            progress?.Report("Renderizando página " + (index + 1) + " de " + reader.PageCount);
                            int page = index;
                            outputs.Add(await new FileOutputManager().CreateAsync(folder,
                                Path.GetFileNameWithoutExtension(path) + "_" + (index + 1).ToString("D4") + "." + format,
                                (temporary,ct) => { using (var bitmap = reader.Render(page,200)) using(var stream=File.Create(temporary)) bitmap.Save(stream, format == "png" ? ImageFormat.Png : ImageFormat.Jpeg); return Task.CompletedTask; }, token));
                        }
                        return outputs.ToArray();
                    }
                    catch { foreach (string output in outputs) File.Delete(output); if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder); throw; }
                }
            }, token).ConfigureAwait(false);
        }
        private static class Native
        {
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDF_InitLibrary();
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr FPDF_LoadDocument(byte[] path, IntPtr password);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int FPDF_GetPageCount(IntPtr doc);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern double FPDF_GetPageWidth(IntPtr page);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern double FPDF_GetPageHeight(IntPtr page);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDF_ClosePage(IntPtr page);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDF_CloseDocument(IntPtr doc);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr FPDFBitmap_CreateEx(int w,int h,int format,IntPtr buffer,int stride);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap,int x,int y,int w,int h,uint color);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap,IntPtr page,int x,int y,int w,int h,int rotate,int flags);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr doc,IntPtr info);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDFDOC_ExitFormFillEnvironment(IntPtr form);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FORM_OnAfterLoadPage(IntPtr page,IntPtr form);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FORM_OnBeforeClosePage(IntPtr page,IntPtr form);
            [DllImport("pdfium.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void FPDF_FFLDraw(IntPtr form,IntPtr bitmap,IntPtr page,int x,int y,int w,int h,int rotate,int flags);
        }
    }
}
