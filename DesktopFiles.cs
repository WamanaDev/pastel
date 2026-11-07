using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Pastel
{
    /// <summary>Tira atalhos da área de trabalho (move para a pasta do grupo) e os devolve depois.</summary>
    internal static class DesktopFiles
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        private const int SHCNE_CREATE = 0x00000002, SHCNE_DELETE = 0x00000004, SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_PATHW = 0x0005;

        /// <summary>Avisa o Explorer que o arquivo mudou, para a área de trabalho atualizar na hora.</summary>
        private static void Notify(int eventId, string path)
        {
            try
            {
                SHChangeNotify(eventId, SHCNF_PATHW, path, IntPtr.Zero); // sem FLUSH: não espera o Explorer
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, dir, IntPtr.Zero);
            }
            catch { }
        }

        public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private static IEnumerable<string> DesktopDirs()
        {
            yield return UserDesktop;
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        }

        public static bool IsOnDesktop(string path)
        {
            if (!File.Exists(path)) return false; // só arquivos; pastas ficam onde estão
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return false;
            foreach (var d in DesktopDirs())
                if (!string.IsNullOrEmpty(d) &&
                    string.Equals(Path.TrimEndingDirectorySeparator(dir),
                                  Path.TrimEndingDirectorySeparator(d), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static string UniquePath(string dir, string fileName)
        {
            string target = Path.Combine(dir, fileName);
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            for (int i = 2; File.Exists(target); i++)
                target = Path.Combine(dir, $"{name} ({i}){ext}");
            return target;
        }

        /// <summary>Se o arquivo está na área de trabalho, move para a pasta do grupo. Falha = não mexe.</summary>
        public static bool TryTake(string path, string groupId, out string newPath)
        {
            newPath = path;
            try
            {
                if (!IsOnDesktop(path)) return false;
                string dir = Path.Combine(ConfigStore.Dir, "Grupos", groupId);
                Directory.CreateDirectory(dir);
                string target = UniquePath(dir, Path.GetFileName(path));
                File.Move(path, target);
                Notify(SHCNE_DELETE, path);
                newPath = target;
                return true;
            }
            catch { return false; } // ex.: área de trabalho pública sem permissão de administrador
        }

        /// <summary>Devolve o atalho para onde ele estava.</summary>
        public static void Restore(ShortcutItem item)
        {
            if (string.IsNullOrEmpty(item.OriginalPath) || !File.Exists(item.Path)) return;
            try
            {
                string? dir = Path.GetDirectoryName(item.OriginalPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                string target = UniquePath(dir, Path.GetFileName(item.OriginalPath));
                File.Move(item.Path, target);
                Notify(SHCNE_CREATE, target);
            }
            catch { }
        }
    }
}
