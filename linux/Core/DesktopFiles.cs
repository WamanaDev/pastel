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

        /// <summary>Só atalhos (.desktop) podem ser movidos da área de trabalho; o resto fica onde está (ver CreateLink).</summary>
        public static bool IsShortcutFile(string path) =>
            path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

        private static bool IsExecutableFile(string path)
        {
            try
            {
                var m = File.GetUnixFileMode(path);
                return (m & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch { return false; }
        }

        private static string ValueEscape(string v) => v.Replace("\\", "\\\\").Replace("\n", " ").Replace("\r", "");

        /// <summary>Argumento do Exec= com aspas; "%" vira "%%" para não ser lido como código de campo.</summary>
        private static string ExecArg(string p) => DesktopEntry.QuoteExecArg(p.Replace("%", "%%"));

        /// <summary>
        /// Arquivo, programa ou pasta que não é atalho: o original fica onde está e a pasta gerenciada recebe um
        /// atalho .desktop (executável, para o KDE confiar) apontando para ele. Null se não deu para criar.
        /// </summary>
        public static string? CreateLink(string target, string ownerId)
        {
            try
            {
                bool isDir = Directory.Exists(target);
                if (!isDir && !File.Exists(target)) return null;
                string full = Path.GetFullPath(target);
                string name = isDir ? Path.GetFileName(Path.TrimEndingDirectorySeparator(full)) : Path.GetFileNameWithoutExtension(full);
                if (string.IsNullOrEmpty(name)) name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name)) name = full;

                string exec, icon;
                if (isDir) { exec = "xdg-open " + ExecArg(full); icon = "folder"; }
                else if (IsExecutableFile(full)) { exec = ExecArg(full); icon = "application-x-executable"; }
                else { exec = "xdg-open " + ExecArg(full); icon = "text-x-generic"; }

                string dir = Paths.ManagedDir(ownerId);
                string link = DesktopEntry.UniquePath(dir, DesktopEntry.Sanitize(name) + ".desktop");
                string content = string.Join("\n", new[]
                {
                    "[Desktop Entry]", "Type=Application", "Version=1.0",
                    "Name=" + ValueEscape(name), "Exec=" + exec, "Icon=" + icon,
                    "Terminal=false", "StartupNotify=false", ""
                });
                DesktopEntry.WriteExecutable(link, content); // cria a pasta e marca como executável
                return File.Exists(link) ? link : null;
            }
            catch { return null; }
        }

        /// <summary>Se o arquivo está na área de trabalho, move para a pasta gerenciada. Falha = não mexe.</summary>
        public static bool TryTake(string path, string ownerId, out string newPath)
        {
            newPath = path;
            try
            {
                if (!IsShortcutFile(path)) return false; // arquivos e programas nunca são movidos
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
            if (item.SourcePath != null)
            {
                // atalho criado pelo Pastel: apaga só ele (e só dentro da pasta gerenciada); o original não é tocado
                try
                {
                    string root = Path.Combine(Paths.DataDir, "Grupos") + Path.DirectorySeparatorChar;
                    if (item.Path.StartsWith(root, StringComparison.Ordinal) && item.Path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
                        File.Delete(item.Path);
                }
                catch { }
                return;
            }
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
