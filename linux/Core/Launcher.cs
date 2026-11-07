using System;
using System.Diagnostics;
using System.IO;

namespace Pastel
{
    /// <summary>Abre um app, atalho ou pasta usando as ferramentas do próprio desktop (KDE ou outro).</summary>
    internal static class AppLauncher
    {
        private static bool Run(string program, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                var p = Process.Start(psi);
                return p != null;
            }
            catch { return false; } // programa não instalado
        }

        /// <summary>true se algum método conseguiu iniciar o item.</summary>
        public static bool TryOpen(string path)
        {
            if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                // gio launch entende o Exec, o ícone de inicialização e os atalhos .desktop de qualquer ambiente
                if (Run("gio", "launch", path)) return true;
                if (Run("kioclient6", "exec", path) || Run("kioclient5", "exec", path)) return true;
            }

            if (File.Exists(path) && IsExecutable(path) && !path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
                if (Run(path)) return true;

            return Run("xdg-open", path) || Run("gio", "open", path);
        }

        private static bool IsExecutable(string path)
        {
            try { return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
            catch { return false; }
        }

        /// <summary>Mostra o arquivo no gerenciador de arquivos (Dolphin usa --select).</summary>
        public static void ShowInFileManager(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? path;
            if (Run("dolphin", "--select", path)) return;
            if (Run("xdg-open", dir)) return;
            Run("gio", "open", dir);
        }

        public static void OpenFolder(string dir)
        {
            Directory.CreateDirectory(dir);
            if (!Run("xdg-open", dir)) Run("gio", "open", dir);
        }
    }
}
