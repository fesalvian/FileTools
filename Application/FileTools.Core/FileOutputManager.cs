// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileTools
{
    public sealed class FileOutputManager
    {
        public static void EnsureWritable(string directory)
        {
            string probe = WindowsPaths.ForIO(Path.Combine(directory, ".filetools-probe-" + Guid.NewGuid().ToString("N")));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }

        // Stage alongside the destination: commit is a same-volume rename and never replaces an existing file.
        public async Task<string> CreateAsync(string directory, string filename, Func<string, CancellationToken, Task> produce, CancellationToken token)
        {
            if (Path.GetFileName(filename) != filename) throw new ArgumentException("Nome inválido.", nameof(filename));
            string stageDirectory = WindowsPaths.ForIO(Path.Combine(directory, ".filetools-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(stageDirectory);
            string temporary = WindowsPaths.ForIO(Path.Combine(stageDirectory, filename));
            try
            {
                token.ThrowIfCancellationRequested();
                await produce(temporary, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                    throw new InvalidDataException("O conversor não gerou um arquivo válido.");
                for (int index = 1; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = WindowsPaths.ForIO(Path.Combine(directory, index == 1 ? filename :
                        Path.GetFileNameWithoutExtension(filename) + " (" + index + ")" + Path.GetExtension(filename)));
                    try { File.Move(temporary, candidate); return candidate; }
                    catch (IOException) { if (!File.Exists(candidate) && !Directory.Exists(candidate)) throw; }
                }
            }
            finally { Directory.Delete(stageDirectory, true); }
        }

        public static string CreateOutputDirectory(string parent, string name)
        {
            for (int index = 1; ; index++)
            {
                string path = WindowsPaths.ForIO(Path.Combine(parent, index == 1 ? name : name + " (" + index + ")"));
                // Reserve a sibling marker so concurrent workers cannot share the same output directory.
                string marker = path + ".filetools-lock";
                FileStream lease;
                try { lease = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) { if (File.Exists(marker)) continue; throw; }
                using (lease)
                {
                    try
                    {
                        if (Directory.Exists(path) || File.Exists(path)) continue;
                        Directory.CreateDirectory(path);
                        return path;
                    }
                    finally { lease.Dispose(); File.Delete(marker); }
                }
            }
        }
    }
}
