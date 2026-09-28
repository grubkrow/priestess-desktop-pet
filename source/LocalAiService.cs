using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PriestessPet
{
    // The portable runtime starts only when local chat needs it.
    // Only the explicit setup helper downloads models.
    public sealed class LocalAiService : IDisposable
    {
        private readonly object processGate = new object();
        private readonly SemaphoreSlim startGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HttpClient client;
        private readonly string explicitRoot;
        private Process ownedProcess;
        private IntPtr ownedJob;
        private bool disposed;

        public Uri Endpoint { get; private set; }
        public string Model { get; private set; }

        public LocalAiService() : this(null) { }

        public LocalAiService(string aiRoot)
        {
            explicitRoot = aiRoot == null ? null : Path.GetFullPath(aiRoot);
            Endpoint = new Uri("http://127.0.0.1:11435/");
            Model = "gemma3:4b";
            client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            client.BaseAddress = Endpoint;
            client.Timeout = TimeSpan.FromSeconds(2);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        }

        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            return EnsureCoreAsync(true, cancellationToken);
        }

        // Used only by the explicit one-time setup helper before it downloads the model.
        internal Task EnsureServerAsync(CancellationToken cancellationToken)
        {
            return EnsureCoreAsync(false, cancellationToken);
        }

        private async Task EnsureCoreAsync(bool requireModel, CancellationToken cancellationToken)
        {
            Trace("ensure start");
            lock (processGate) ThrowIfDisposed();
            using (CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                CancellationToken token = request.Token;
                await startGate.WaitAsync(token).ConfigureAwait(false);
                Trace("startup gate entered");
                bool startedHere = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    // Reusing a compatible local server does not give us ownership of it.
                    if (await ProbeAsync(requireModel, token).ConfigureAwait(false)) {Trace("existing server ready");return;}
                    lock (processGate)
                    {
                        ThrowIfDisposed();
                        if (ownedProcess != null && !ownedProcess.HasExited)
                            throw new InvalidOperationException("Priestess's local AI is not responding. Close Priestess and reopen her to restart it.");
                        StopOwnedProcess();
                        StartOwnedProcess(ResolveAiRoot());
                        Trace("owned server started");
                        startedHere = true;
                    }

                    Stopwatch readiness = Stopwatch.StartNew();
                    while (readiness.Elapsed < TimeSpan.FromSeconds(20))
                    {
                        token.ThrowIfCancellationRequested();
                        lock (processGate)
                        {
                            ThrowIfDisposed();
                            if (ownedProcess == null || ownedProcess.HasExited)
                                throw new InvalidOperationException("The local AI could not start. Make sure the portable Ollama files are complete and port 11435 is available, then try again.");
                        }
                        if (await ProbeAsync(requireModel, token).ConfigureAwait(false)) {Trace("owned server ready");return;}
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("The local AI did not become ready within 20 seconds. Try again, or reopen Priestess if the problem continues.");
                }
                catch
                {
                    // A cancelled/failed startup must not leave a hidden runner behind.
                    if (startedHere) lock (processGate) StopOwnedProcess();
                    throw;
                }
                finally { startGate.Release(); }
            }
        }

        private async Task<bool> ProbeAsync(bool requireModel, CancellationToken token)
        {
            Trace("probe GET tags");
            HttpResponseMessage response;
            try { response = await client.GetAsync("api/tags", token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Trace("probe timeout/cancel");token.ThrowIfCancellationRequested(); return false; }
            catch (HttpRequestException) { Trace("probe connection failed");return false; }
            using (response)
            {
                Trace("probe response "+response.StatusCode);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Port 11435 is responding, but it is not a ready Ollama API. Close the other service on that port and try again.");
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Dictionary<string, object> data;
                try { data = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>; }
                catch (Exception e)
                {
                    if (!(e is ArgumentException) && !(e is InvalidOperationException)) throw;
                    throw new InvalidOperationException("Port 11435 did not return a valid Ollama model list. Close the other service on that port and try again.", e);
                }
                object value;
                IEnumerable models = data != null && data.TryGetValue("models", out value) ? value as IEnumerable : null;
                if (models == null || models is string)
                    throw new InvalidOperationException("Port 11435 did not return a valid Ollama model list. Close the other service on that port and try again.");
                if (!requireModel) return true;
                foreach (object item in models)
                {
                    Dictionary<string, object> model = item as Dictionary<string, object>;
                    if (model == null) continue;
                    string name = ReadString(model, "name");
                    if (name != Model && ReadString(model, "model") != Model) continue;
                    // Do not accept a cloud alias when attaching to a server we did not start.
                    if (!String.IsNullOrEmpty(ReadString(model, "remote_host")) || !String.IsNullOrEmpty(ReadString(model, "remote_model")))
                        throw new InvalidOperationException("The selected model is a cloud alias. Priestess needs her downloaded local vision model for offline chat and attached image context.");
                    return true;
                }
                throw new InvalidOperationException("Priestess's local vision model is missing. Run Setup-local-AI.ps1 in the Priestess folder to finish the one-time setup, then try again. Chat works offline after setup.");
            }
        }

        private static string ReadString(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string : null;
        }
        private static void Trace(string message)
        {
            string path=Environment.GetEnvironmentVariable("PRIESTESS_AI_TRACE");
            if(String.IsNullOrWhiteSpace(path))return;
            try{File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}catch(IOException){}
        }

        private string ResolveAiRoot()
        {
            return AiRootLocator.Resolve(explicitRoot);
        }

        // The child is suspended until attached to a job. Every runner it later creates
        // belongs to that same job; closing the app cleans up only this process family.
        private void StartOwnedProcess(string aiRoot)
        {
            string executable = Path.Combine(aiRoot, "ollama", "ollama.exe");
            string state = Path.Combine(aiRoot, "state");
            string temp = Path.Combine(aiRoot, "temp");
            string models = Path.Combine(aiRoot, "models");
            string roaming = Path.Combine(state, "AppData", "Roaming");
            string local = Path.Combine(state, "AppData", "Local");
            foreach (string folder in new[] { state, temp, models, roaming, local }) Directory.CreateDirectory(folder);
            SortedDictionary<string, string> environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables()) environment[(string)item.Key] = (string)item.Value;
            environment["OLLAMA_HOST"] = "127.0.0.1:11435";
            environment["OLLAMA_NO_CLOUD"] = "1";
            environment["OLLAMA_MODELS"] = models;
            environment["OLLAMA_KEEP_ALIVE"] = "2m";
            environment["OLLAMA_NUM_PARALLEL"] = "1";
            environment["OLLAMA_MAX_LOADED_MODELS"] = "1";
            environment["OLLAMA_CONTEXT_LENGTH"] = "4096";
            environment["OLLAMA_NOHISTORY"] = "1";
            environment["HOME"] = state;
            environment["USERPROFILE"] = state;
            environment["APPDATA"] = roaming;
            environment["LOCALAPPDATA"] = local;
            environment["XDG_CACHE_HOME"] = Path.Combine(state, "cache");
            environment["TMP"] = temp;
            environment["TEMP"] = temp;
            environment["TMPDIR"] = temp;
            environment["OLLAMA_TMPDIR"] = temp;
            environment["NO_PROXY"] = "127.0.0.1,localhost,::1";
            environment.Remove("HTTP_PROXY");
            environment.Remove("HTTPS_PROXY");
            environment.Remove("ALL_PROXY");
            StringBuilder block = new StringBuilder();
            foreach (KeyValuePair<string, string> item in environment) block.Append(item.Key).Append('=').Append(item.Value).Append('\0');
            block.Append('\0');

            IntPtr environmentPointer = IntPtr.Zero;
            PROCESS_INFORMATION process = new PROCESS_INFORMATION();
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw StartupError("create a local AI process group");
            try
            {
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                    throw StartupError("configure local AI cleanup");
                STARTUPINFO startup = new STARTUPINFO();
                startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                startup.dwFlags = 1; // STARTF_USESHOWWINDOW
                startup.wShowWindow = 0;
                environmentPointer = Marshal.StringToHGlobalUni(block.ToString());
                if (!CreateProcess(executable, new StringBuilder("\"" + executable + "\" serve"), IntPtr.Zero, IntPtr.Zero, false,
                    0x08000000 | 0x00000004 | 0x00000400, environmentPointer, aiRoot, ref startup, out process))
                    throw StartupError("start portable Ollama");
                if (!AssignProcessToJobObject(job, process.hProcess))
                    throw StartupError("attach local AI cleanup");
                ownedProcess = Process.GetProcessById((int)process.dwProcessId);
                if (ResumeThread(process.hThread) == UInt32.MaxValue) throw StartupError("resume portable Ollama");
                ownedJob = job;
                job = IntPtr.Zero;
            }
            catch
            {
                if (process.hProcess != IntPtr.Zero) TerminateProcess(process.hProcess, 1);
                if (ownedProcess != null) { ownedProcess.Dispose(); ownedProcess = null; }
                throw;
            }
            finally
            {
                if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
                if (process.hProcess != IntPtr.Zero) CloseHandle(process.hProcess);
                if (job != IntPtr.Zero) CloseHandle(job);
                if (environmentPointer != IntPtr.Zero) Marshal.FreeHGlobal(environmentPointer);
            }
        }

        private static InvalidOperationException StartupError(string action)
        {
            return new InvalidOperationException("Priestess could not " + action + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        // processGate must be held. Never enumerate or terminate unrelated Ollama instances.
        private void StopOwnedProcess()
        {
            if (ownedJob != IntPtr.Zero) { CloseHandle(ownedJob); ownedJob = IntPtr.Zero; }
            if (ownedProcess != null) { ownedProcess.Dispose(); ownedProcess = null; }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("LocalAiService");
        }

        public void Dispose()
        {
            lock (processGate)
            {
                if (disposed) return;
                disposed = true;
                lifetime.Cancel();
                StopOwnedProcess();
                client.Dispose();
            }
            // Keep these small synchronization objects alive until in-flight awaits finish.
            // Disposing a semaphore here would make their finally/Release throw.
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb; public IntPtr lpReserved, lpDesktop, lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public ushort wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    }
}
