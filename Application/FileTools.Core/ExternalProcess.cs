// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTools
{
    public sealed class ProcessResult
    {
        public int ExitCode;
        public string Output;
        public string Error;
    }
    public interface IExternalProcess
    {
        Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token);
    }
    public sealed class ExternalProcess : IExternalProcess
    {
        // CommandLineToArgvW escaping; do not route user paths through cmd.exe or PowerShell.
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
                result.Append(ch);
                slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }
        public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var process = new Process { StartInfo = new ProcessStartInfo(executable,
                string.Join(" ", arguments.Select(Quote))) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } })
            using(var job=new ProcessJob())
            {
                process.Start();
                try{job.Assign(process);}catch{try{process.Kill();}catch(InvalidOperationException){}throw;}
                var output = ReadBoundedAsync(process.StandardOutput);
                var error = ReadBoundedAsync(process.StandardError);
                using (token.Register(job.Cancel))
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                var result = new ProcessResult { ExitCode = process.ExitCode, Output = await output.ConfigureAwait(false), Error = await error.ConfigureAwait(false) };
                token.ThrowIfCancellationRequested();
                return result;
            }
        }
        // An unnamed Windows job contains only this converter and its descendants.
        // Cancellation/worker failure must not leave an isolated soffice.bin running.
        private sealed class ProcessJob:IDisposable
        {
            private IntPtr handle;
            public ProcessJob()
            {
                handle=CreateJobObject(IntPtr.Zero,null);
                if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
                IntPtr limits=Marshal.AllocHGlobal(144); // JOBOBJECT_EXTENDED_LIMIT_INFORMATION, Windows x64.
                try
                {
                    Marshal.Copy(new byte[144],0,limits,144);
                    Marshal.WriteInt32(limits,16,0x2000); // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
                    if(!SetInformationJobObject(handle,9,limits,144))throw new System.ComponentModel.Win32Exception();
                }
                catch{Dispose();throw;}
                finally{Marshal.FreeHGlobal(limits);}
            }
            public void Assign(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new System.ComponentModel.Win32Exception();}
            public void Cancel(){TerminateJobObject(handle,1);}
            public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
            [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr attributes,string name);
            [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job,int information,IntPtr data,uint size);
            [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
            [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job,uint code);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        }
        private static async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                if (text.Length < 32768) text.Append(buffer, 0, Math.Min(count, 32768 - text.Length));
            return text.ToString();
        }
    }

    public static class Dependencies
    {
        public static string Qpdf
        {
            get
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "qpdf", "bin", "qpdf.exe");
                if (File.Exists(path)) return path;
                // Look only at explicit application and Program Files paths, not current-directory executables.
                foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string dir in Directory.EnumerateDirectories(root, "qpdf*").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        path = Path.Combine(dir, "bin", "qpdf.exe");
                        if (File.Exists(path)) return path;
                    }
                }
                throw new FileNotFoundException("qpdf ausente. Use o pacote completo do FileTools ou instale o qpdf x64.");
            }
        }
    }
}
