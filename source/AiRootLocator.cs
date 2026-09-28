using System;
using System.Collections.Generic;
using System.IO;

namespace PriestessPet
{
    // Finds the shared offline Ollama files after the pet folder is moved. A
    // small sidecar file keeps the release portable without embedding a path.
    internal static class AiRootLocator
    {
        private const string SidecarName = "Priestess-ai-root.txt";

        public static string Resolve(string explicitRoot)
        {
            return Resolve(explicitRoot, AppDomain.CurrentDomain.BaseDirectory);
        }

        internal static string Resolve(string explicitRoot, string baseDirectory)
        {
            if (!String.IsNullOrWhiteSpace(explicitRoot))
            {
                string selected;
                if (TryFullPath(explicitRoot, out selected) && HasRuntime(selected)) return selected;
            }

            string environmentRoot = Environment.GetEnvironmentVariable("PRIESTESS_AI_ROOT");
            if (!String.IsNullOrWhiteSpace(environmentRoot))
            {
                string selected;
                if (TryFullPath(environmentRoot, out selected) && HasRuntime(selected)) return selected;
            }

            string basePath = String.IsNullOrWhiteSpace(baseDirectory) ? AppDomain.CurrentDomain.BaseDirectory : baseDirectory;
            basePath = Path.GetFullPath(basePath);
            string sidecar = Path.Combine(basePath, SidecarName);
            string sidecarProblem = null;
            if (File.Exists(sidecar))
            {
                string configured = ReadSidecar(sidecar);
                string selected;
                if (TryFullPath(configured, out selected) && HasRuntime(selected)) return selected;
                sidecarProblem = "Priestess-ai-root.txt points to an AI folder that is missing the portable Ollama files.";
            }

            DirectoryInfo folder = new DirectoryInfo(basePath);
            while (folder != null)
            {
                string candidate = Path.Combine(folder.FullName, "ai");
                if (HasRuntime(candidate)) return Path.GetFullPath(candidate);
                folder = folder.Parent;
            }

            if (sidecarProblem != null)
                throw new InvalidOperationException(sidecarProblem + " Update that file to the folder containing ai\\ollama\\ollama.exe, then reopen Priestess.");
            throw new InvalidOperationException("The portable AI files could not be found. Keep the complete ai folder beside Priestess, or create Priestess-ai-root.txt beside Priestess.exe with one line containing its full path, then reopen Priestess.");
        }

        internal static bool HasRuntime(string aiRoot)
        {
            if (String.IsNullOrWhiteSpace(aiRoot)) return false;
            try { return File.Exists(Path.Combine(aiRoot, "ollama", "ollama.exe")); }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static string ReadSidecar(string path)
        {
            string[] lines = File.ReadAllLines(path);
            List<string> values = new List<string>();
            foreach (string line in lines)
            {
                string value = (line ?? String.Empty).Trim();
                if (value.Length > 0) values.Add(value);
            }
            if (values.Count != 1 || values[0].Length > 4096)
                throw new InvalidOperationException("Priestess-ai-root.txt must contain exactly one AI folder path on one line.");
            return values[0];
        }

        private static bool TryFullPath(string value, out string path)
        {
            path = null;
            if (String.IsNullOrWhiteSpace(value)) return false;
            try { path = Path.GetFullPath(value.Trim()); return true; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
    }
}
