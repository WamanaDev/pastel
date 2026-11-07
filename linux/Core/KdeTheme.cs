using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media;

namespace Pastel
{
    /// <summary>
    /// Tema claro/escuro e cor de destaque. Por padrão seguem o KDE (lidos de kdeglobals); nas
    /// configurações do Pastel dá para forçar claro/escuro e usar o violeta da marca.
    /// </summary>
    internal static class PastelTheme
    {
        public static readonly Color Violet = Color.FromRgb(0x6C, 0x4D, 0xFF);
        public static readonly Color Ink = Color.FromRgb(0x2B, 0x25, 0x40);
        public static readonly Color Lavender = Color.FromRgb(0xF4, 0xEE, 0xFF);
        public static readonly Color Cream = Color.FromRgb(0xFF, 0xF9, 0xF5);
        public static readonly Color Plum = Color.FromRgb(0x1B, 0x17, 0x26);

        private static AppConfig Cfg => ConfigStore.Current;

        // ---------- lendo o kdeglobals ----------

        private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return result;
                Dictionary<string, string>? cur = null;
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    if (line[0] == '[' && line[^1] == ']') { cur = new(StringComparer.Ordinal); result[line[1..^1]] = cur; continue; }
                    int eq = line.IndexOf('=');
                    if (cur != null && eq > 0) cur[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
            catch { }
            return result;
        }

        private static Color? ParseRgb(string? v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var p = v.Split(',');
            if (p.Length >= 3 && byte.TryParse(p[0], out var r) && byte.TryParse(p[1], out var g) && byte.TryParse(p[2], out var b))
                return Color.FromRgb(r, g, b);
            return null;
        }

        /// <summary>O KDE está no esquema escuro? Vem da cor de fundo das janelas (vale para qualquer esquema, não só o Breeze).</summary>
        public static bool KdeIsLight
        {
            get
            {
                var ini = ReadIni(Paths.KdeGlobals);
                if (ini.TryGetValue("Colors:Window", out var w) && ParseRgb(w.GetValueOrDefault("BackgroundNormal")) is Color bg)
                    return (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) > 128;
                // sem kdeglobals (outro ambiente): tenta o nome do esquema e, por fim, assume claro
                if (ini.TryGetValue("General", out var g) && g.GetValueOrDefault("ColorScheme") is string name)
                    return !name.Contains("dark", StringComparison.OrdinalIgnoreCase);
                return true;
            }
        }

        public static Color KdeAccent
        {
            get
            {
                var ini = ReadIni(Paths.KdeGlobals);
                if (ini.TryGetValue("General", out var g) && ParseRgb(g.GetValueOrDefault("AccentColor")) is Color a) return a; // Plasma 6
                if (ini.TryGetValue("Colors:Selection", out var s) && ParseRgb(s.GetValueOrDefault("BackgroundNormal")) is Color c) return c;
                return Violet;
            }
        }

        // ---------- o que o Pastel usa ----------

        public static bool IsLight => Cfg.ThemeMode switch { "light" => true, "dark" => false, _ => KdeIsLight };

        public static Color Accent => Cfg.AccentMode == "brand" ? Violet : KdeAccent;

        /// <summary>Muda quando o tema, o destaque ou a versão do desenho dos ícones muda.</summary>
        public static string Signature
        {
            get { var a = Accent; return $"lx1-{Cfg.ThemeMode}-{(IsLight ? "L" : "D")}-{Cfg.AccentMode}-{a.R:X2}{a.G:X2}{a.B:X2}"; }
        }

        /// <summary>A cor de destaque, ou um neutro do tema quando ela some no fundo (ex.: destaque preto no tema escuro).</summary>
        public static Color Visible(Color accent, bool light)
        {
            double luma = 0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B;
            if (!light && luma < 60) return Lavender;
            if (light && luma > 200) return Ink;
            return accent;
        }

        public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        public static Color Text(bool light) => light ? Ink : Lavender;
        public static Color Surface(bool light) => light ? Cream : Plum;
    }
}
