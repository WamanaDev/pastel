using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pastel
{
    /// <summary>Lê e escreve arquivos .desktop (o "atalho" do Linux). Cada pasta do Pastel é um deles na área de trabalho.</summary>
    internal static class DesktopEntry
    {
        public const string IdKey = "X-Pastel-Id";

        // ---------- leitura ----------

        /// <summary>Chaves do grupo [Desktop Entry] (a primeira ocorrência de cada uma).</summary>
        public static Dictionary<string, string> Read(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                bool inMain = false;
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line[0] == '[') { inMain = line == "[Desktop Entry]"; continue; }
                    if (!inMain) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    if (!map.ContainsKey(key)) map[key] = Unescape(line.Substring(eq + 1).Trim());
                }
            }
            catch { }
            return map;
        }

        public static string? ReadId(string path) => Read(path).TryGetValue(IdKey, out var id) ? id : null;

        /// <summary>O "Name" do atalho, preferindo o do idioma do sistema quando existir.</summary>
        public static string? LocalName(Dictionary<string, string> e)
        {
            string lang = (Environment.GetEnvironmentVariable("LANG") ?? "").Split('.')[0];
            if (lang.Length > 0 && e.TryGetValue($"Name[{lang}]", out var l)) return l;
            string shortLang = lang.Split('_')[0];
            if (shortLang.Length > 0 && e.TryGetValue($"Name[{shortLang}]", out var s)) return s;
            return e.TryGetValue("Name", out var n) ? n : null;
        }

        // ---------- escrita ----------

        private static string Escape(string v) => v.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "");

        private static string Unescape(string v) => v.Replace("\\n", "\n").Replace("\\s", " ").Replace("\\\\", "\\");

        /// <summary>Argumento do Exec= com aspas, como manda a especificação (aspas, crase, cifrão e barra escapados).</summary>
        public static string QuoteExecArg(string arg)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in arg)
            {
                // A especificação escapa aspas, crase, cifrão e barra com uma barra (dentro das aspas) e o
                // valor do arquivo dobra cada barra de novo: 2 barras antes do caractere, 4 se ele for a barra.
                if (c == '\\') sb.Append(@"\\\\");
                else if (c is '"' or '`' or '$') sb.Append(@"\\").Append(c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Atalho de uma pasta: abrir = Pastel, soltar arquivos = passa os caminhos (%F).</summary>
        public static string BuildFolderLauncher(FolderModel f, string exe, string iconPath) => string.Join("\n", new[]
        {
            "[Desktop Entry]",
            "Type=Application",
            "Version=1.0",
            "Name=" + Escape(f.Name),
            "Comment=Pasta do Pastel",
            $"Exec={QuoteExecArg(exe)} --folder {f.Id} %F",
            "Icon=" + Escape(iconPath),
            "Terminal=false",
            "StartupNotify=false",
            $"{IdKey}={f.Id}",
            ""
        });

        public static void WriteExecutable(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            // O KDE só confia em atalhos .desktop executáveis fora das pastas do sistema (senão pergunta ao abrir).
            try { File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead); } catch { }
            File.Move(tmp, path, overwrite: true);
        }

        public static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return name.Length == 0 ? "Pasta" : name;
        }

        public static string UniquePath(string dir, string fileName)
        {
            string target = Path.Combine(dir, fileName);
            string name = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
            for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, $"{name} ({i}){ext}");
            return target;
        }

        /// <summary>Acha o atalho da pasta na área de trabalho, mesmo que tenha sido renomeado.</summary>
        public static string? FindById(string id)
        {
            try
            {
                foreach (var p in Directory.EnumerateFiles(Paths.DesktopDir, "*.desktop"))
                    if (string.Equals(ReadId(p), id, StringComparison.OrdinalIgnoreCase)) return p;
            }
            catch { }
            return null;
        }
    }
}
