// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FileTools
{
    public static class Operations
    {
        public const string Merge = "pdf-merge", MergeOrdered = "pdf-merge-order";
        public const string Split = "pdf-split", Extract = "pdf-extract";
        public static readonly string[] Images = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif" };
        public static bool Supports(string operation, string[] paths)
        {
            if (paths == null || paths.Length == 0) return false;
            if (operation == Merge || operation == MergeOrdered)
                return paths.Length >= 2 && paths.All(IsPdf);
            if (operation == Split || operation == Extract) return paths.All(IsPdf);
            return new[] { "image-png", "image-jpg", "image-webp" }.Contains(operation)
                && paths.All(p => Images.Contains(Path.GetExtension(p).ToLowerInvariant()));
        }
        private static bool IsPdf(string p) => string.Equals(Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class NaturalPathComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
        public int Compare(string a, string b)
        {
            int result = StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
            return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }
    }

    [DataContract]
    public sealed class SelectionRequest
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Operation;
        [DataMember] public string[] Files;

        public void Validate()
        {
            if (Version != 1 || Files == null || Files.Length > 100000 || !Operations.Supports(Operation, Files))
                throw new InvalidDataException("Seleção incompatível com a operação.");
            if (Files.Any(p => string.IsNullOrWhiteSpace(p) || !Path.IsPathRooted(p) || p.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
                throw new InvalidDataException("Caminho de arquivo inválido.");
            Files = Files.Select(p => WindowsPaths.ForIO(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!Operations.Supports(Operation, Files)) throw new InvalidDataException("Selecione arquivos distintos.");
        }
    }

    // Only metadata lives in the shell process. No converters or native image libraries are referenced here.
    public static class SelectionTransport
    {
        public static string RequestDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileTools", "Requests");
        public static string Write(string operation, IEnumerable<string> files)
        {
            var request = new SelectionRequest { Operation = operation, Files = files.ToArray() };
            request.Validate();
            Directory.CreateDirectory(RequestDirectory);
            var path = Path.Combine(RequestDirectory, Guid.NewGuid().ToString("N") + ".json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(SelectionRequest)).WriteObject(stream, request);
            return path;
        }
        public static SelectionRequest Read(string path, bool consume)
        {
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(path), RequestDirectory, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)
                || Path.GetExtension(path) != ".json")
                throw new InvalidDataException("Manifesto fora da pasta de solicitações.");
            try
            {
                if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Manifesto muito grande.");
                SelectionRequest request;
                using (var stream = File.OpenRead(path))
                    request = (SelectionRequest)new DataContractJsonSerializer(typeof(SelectionRequest)).ReadObject(stream);
                request.Validate();
                return request;
            }
            finally { if (consume) File.Delete(path); }
        }
        public static void CleanupExpired()
        {
            if (!Directory.Exists(RequestDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(RequestDirectory, "*.json"))
                try { if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
