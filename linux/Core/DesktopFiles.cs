using System;
using System.IO;

namespace Pastel
{
    /// <summary>Tira atalhos da área de trabalho (move para a pasta gerenciada) e os devolve depois.</summary>
    internal static class DesktopFiles
    {
        public static bool IsOnDesktop(string path)
        {
            try
            {
                if (!File.Exists(path)) return false; // só arquivos; pastas ficam onde estão
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                return dir != null && string.Equals(Path.TrimEndingDirectorySeparator(dir),
                    Path.TrimEndingDirectorySeparator(Paths.DesktopDir), StringComparison.Ordinal);
            }
            catch { return false; }
        }

        /// <summary>Se o arquivo está na área de trabalho, move para a pasta gerenciada. Falha = não mexe.</summary>
        public static bool TryTake(string path, string ownerId, out string newPath)
        {
            newPath = path;
            try
            {
                if (!IsOnDesktop(path)) return false;
                string dir = Paths.ManagedDir(ownerId);
                Directory.CreateDirectory(dir);
                string target = DesktopEntry.UniquePath(dir, Path.GetFileName(path));
                File.Move(path, target);
                newPath = target;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Devolve o atalho para onde ele estava.</summary>
        public static void Restore(ShortcutItem item)
        {
            if (string.IsNullOrEmpty(item.OriginalPath) || !File.Exists(item.Path)) return;
            try
            {
                string? dir = Path.GetDirectoryName(item.OriginalPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                File.Move(item.Path, DesktopEntry.UniquePath(dir, Path.GetFileName(item.OriginalPath)));
            }
            catch { }
        }
    }
}
