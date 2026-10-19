// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace FileTools
{
    public sealed class ImageConversion
    {
        public async Task<string> ConvertAsync(string path, string directory, string format, CancellationToken token)
        {
            MagickFormat target;
            switch (format)
            {
                case "png": target = MagickFormat.Png; break;
                case "jpg": target = MagickFormat.Jpeg; break;
                case "webp": target = MagickFormat.WebP; break;
                default: throw new ArgumentException("Formato não suportado.");
            }
            return await new FileOutputManager().CreateAsync(directory,
                Path.GetFileNameWithoutExtension(path) + "." + format, (temporary, ct) => Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    MagickFormat input;
                    switch (Path.GetExtension(path).ToLowerInvariant())
                    {
                        case ".png": input = MagickFormat.Png; break;
                        case ".jpg": case ".jpeg": input = MagickFormat.Jpeg; break;
                        case ".webp": input = MagickFormat.WebP; break;
                        case ".bmp": input = MagickFormat.Bmp; break;
                        case ".tif": case ".tiff": input = MagickFormat.Tiff; break;
                        case ".gif": input = MagickFormat.Gif; break;
                        default: throw new ArgumentException("Formato de entrada não suportado.");
                    }
                    var settings = new MagickReadSettings { Format = input, FrameIndex = 0, FrameCount = 1 };
                    using (var image = new MagickImage())
                    {
                        image.Progress += (s, e) => e.Cancel = ct.IsCancellationRequested;
                        using (var source = File.OpenRead(WindowsPaths.ForIO(path))) image.Read(source, settings);
                        ct.ThrowIfCancellationRequested();
                        image.AutoOrient();
                        if (target == MagickFormat.Jpeg)
                        {
                            image.BackgroundColor = MagickColors.White;
                            image.Alpha(AlphaAction.Remove);
                        }
                        image.Quality = 90;
                        image.Write(temporary, target);
                        ct.ThrowIfCancellationRequested();
                    }
                }, ct), token).ConfigureAwait(false);
        }
    }
}
