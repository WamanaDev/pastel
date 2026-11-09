using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pastel
{
    /// <summary>Decide para onde vai cada arquivo solto na área de trabalho, conforme as regras do usuário.</summary>
    internal static class AutoRules
    {
        /// <summary>Arquivos que o navegador e o Windows criam no meio de uma cópia ou download.</summary>
        private static readonly HashSet<string> Temporary = new(StringComparer.OrdinalIgnoreCase)
            { ".tmp", ".crdownload", ".part", ".partial", ".download", ".opdownload" };

        /// <summary>Espera um arquivo "assentar" antes de mexer nele (cópia ou download ainda pode estar rolando).</summary>
        public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

        public static List<string> ParseExtensions(string text) =>
            text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('.', '*').ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct()
                .ToList();

        public static string FormatExtensions(IEnumerable<string> exts) => string.Join(", ", exts);

        /// <summary>Uma regra sem extensão nem trecho de nome casaria com tudo; isso não é permitido.</summary>
        public static bool IsUsable(AutoRule r) =>
            r.Category.Length > 0 ? RuleCatalog.Find(r.Category) != null : r.Extensions.Count > 0 || r.NameContains.Trim().Length > 0;

        public static bool Matches(AutoRule r, string path)
        {
            if (!r.Enabled || !IsUsable(r)) return false;
            if (r.Category.Length > 0) return RuleCatalog.Matches(r.Category, path);
            string file = Path.GetFileName(path);
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (r.Extensions.Count > 0 && !r.Extensions.Contains(ext)) return false;
            string part = r.NameContains.Trim();
            if (part.Length > 0 && file.IndexOf(part, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            return true;
        }

        public static bool IsTemporary(string path) => Temporary.Contains(Path.GetExtension(path));

        public enum Eligibility
        {
            /// <summary>O Pastel nunca mexe: arquivo de uma pasta dele, desktop.ini, oculto ou de sistema.</summary>
            Never,
            /// <summary>Parece uma cópia ou download em andamento; vale tentar de novo daqui a pouco.</summary>
            NotYet,
            Ready
        }

        public static Eligibility Check(string path)
        {
            try
            {
                string ext = Path.GetExtension(path);
                if (ext.Equals(GroupShortcut.Ext, StringComparison.OrdinalIgnoreCase)) return Eligibility.Never;
                if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return Eligibility.Never;
                var info = new FileInfo(path);
                if (!info.Exists || (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return Eligibility.Never;
                if (Temporary.Contains(ext)) return Eligibility.NotYet; // o navegador renomeia ao terminar
                return DateTime.UtcNow - info.LastWriteTimeUtc >= Settle ? Eligibility.Ready : Eligibility.NotYet;
            }
            catch { return Eligibility.Never; }
        }

        /// <summary>Primeira regra ativa que casa com o arquivo (a ordem da lista é a prioridade).</summary>
        public static AutoRule? Find(IEnumerable<AutoRule> rules, string path) =>
            rules.FirstOrDefault(r => Matches(r, path));
    }
}
