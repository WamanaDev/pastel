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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint bufferLength);

        /// <summary>
        /// Converte um caminho em nome curto (8.3, ex.: ARENAB~1.URL) para o nome de verdade. O Explorer entrega os
        /// arquivos assim ao menu "Enviar para" e ao soltar sobre a pasta, e sem isso o arquivo seria renomeado para o nome curto.
        /// </summary>
        public static string ToLongPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !path.Contains('~')) return path;
                var sb = new System.Text.StringBuilder(1024);
                uint n = GetLongPathName(path, sb, (uint)sb.Capacity);
                return n > 0 && n < sb.Capacity ? sb.ToString() : path;
            }
            catch { return path; }
        }

        /// <summary>
        /// Arquivo com nome curto (ex.: ARENAB~1.URL) que é cópia exata de outro da mesma pasta. É lixo de versões antigas
        /// do Pastel (ver <see cref="ToLongPath"/>): não deve ser organizado como se fosse um atalho a mais.
        /// </summary>
        public static bool IsShortNameDuplicate(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[^ ]{1,8}~\d+\.[^.]+$")) return false;
                string? dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return false;
                var info = new FileInfo(path);
                foreach (var other in new DirectoryInfo(dir).EnumerateFiles())
                {
                    if (string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase) || other.Length != info.Length) continue;
                    if (System.Text.RegularExpressions.Regex.IsMatch(other.Name, @"^[^ ]{1,8}~\d+\.[^.]+$")) continue;
                    if (File.ReadAllBytes(other.FullName).AsSpan().SequenceEqual(File.ReadAllBytes(path))) return true;
                }
            }
            catch { }
            return false;
        }

        public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private static IEnumerable<string> DesktopDirs()
        {
            yield return UserDesktop;
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        }

        private static string Norm(string p)
        {
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(ToLongPath(p))); }
            catch { return p; }
        }

        public static bool IsOnDesktop(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false; // só arquivos; pastas ficam onde estão
                if ((File.GetAttributes(path) & FileAttributes.Hidden) != 0) return false; // oculto = já tratado (ou desktop.ini)
                var dir = Path.GetDirectoryName(Path.GetFullPath(ToLongPath(path)));
                if (string.IsNullOrEmpty(dir)) return false;
                dir = Norm(dir);
                foreach (var d in DesktopDirs())
                    if (!string.IsNullOrEmpty(d) && string.Equals(dir, Norm(d), StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        /// <summary>Atalhos de verdade (.lnk, .url, .appref-ms): só estes podem ser movidos para a pasta do grupo.</summary>
        public static bool IsShortcutFile(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Arquivo, programa ou pasta que não é atalho: o original fica onde está e a pasta do grupo recebe um atalho (.lnk)
        /// apontando para ele. Null se não deu para criar.
        /// </summary>
        public static string? CreateLink(string target, string groupId)
        {
            dynamic? shell = null, sc = null;
            try
            {
                string dir = Path.Combine(ConfigStore.Dir, "Grupos", groupId);
                Directory.CreateDirectory(dir);
                string name = Directory.Exists(target) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(target)) : Path.GetFileNameWithoutExtension(target);
                if (string.IsNullOrEmpty(name)) name = target;
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                string lnk = UniquePath(dir, name + ".lnk");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                sc = shell!.CreateShortcut(lnk);
                sc.TargetPath = target;
                string? wd = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(wd)) sc.WorkingDirectory = wd;
                sc.Save();
                return File.Exists(lnk) ? lnk : null;
            }
            catch { return null; }
            finally
            {
                if (sc != null) Marshal.FinalReleaseComObject(sc);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
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

        /// <summary>
        /// Se o arquivo está na área de trabalho, leva para a pasta do grupo. Se mover falhar (ex.: área de trabalho
        /// pública sem permissão), copia e tenta apagar o original; se não der, oculta o original. Nunca perde o arquivo.
        /// </summary>
        public static bool TryTake(string path, string groupId, out string newPath)
        {
            newPath = path;
            try
            {
                path = ToLongPath(path);
                if (!IsShortcutFile(path)) return false; // arquivos e programas nunca são movidos (ver CreateLink)
                if (!IsOnDesktop(path)) return false;
                string dir = Path.Combine(ConfigStore.Dir, "Grupos", groupId);
                Directory.CreateDirectory(dir);
                string target = UniquePath(dir, Path.GetFileName(path));
                try
                {
                    File.Move(path, target);
                }
                catch
                {
                    File.Copy(path, target, false);
                    bool gone = false;
                    try { File.Delete(path); gone = !File.Exists(path); } catch { }
                    if (!gone)
                    {
                        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
                        catch
                        {
                            // Nem apagar nem ocultar: desfaz a cópia e deixa tudo como estava.
                            try { File.Delete(target); } catch { }
                            return false;
                        }
                    }
                }
                Notify(SHCNE_DELETE, path);
                newPath = target;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Arquivos que o usuário mandou voltar para a área de trabalho. As regras automáticas não os pegam de novo,
        /// senão "remover da pasta" seria desfeito na hora.
        /// </summary>
        public static readonly HashSet<string> Returned = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Devolve o atalho para onde ele estava.</summary>
        public static void Restore(ShortcutItem item)
        {
            if (string.IsNullOrEmpty(item.OriginalPath) || !File.Exists(item.Path)) return;
            try
            {
                // Original só oculto (não deu para apagar): desoculta e descarta a cópia gerenciada.
                if (File.Exists(item.OriginalPath) && (File.GetAttributes(item.OriginalPath) & FileAttributes.Hidden) != 0
                    && new FileInfo(item.OriginalPath).Length == new FileInfo(item.Path).Length)
                {
                    File.SetAttributes(item.OriginalPath, File.GetAttributes(item.OriginalPath) & ~FileAttributes.Hidden);
                    File.Delete(item.Path);
                    Notify(SHCNE_CREATE, item.OriginalPath);
                    return;
                }
                string? dir = Path.GetDirectoryName(item.OriginalPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                string target = UniquePath(dir, Path.GetFileName(item.OriginalPath));
                lock (Returned) Returned.Add(target);
                File.Move(item.Path, target);
                Notify(SHCNE_CREATE, target);
            }
            catch { }
        }
    }
}
