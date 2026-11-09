using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pastel
{
    /// <summary>
    /// Tema claro/escuro e cor de destaque. Por padrão seguem o Windows; nas configurações do
    /// Pastel dá para forçar claro/escuro e usar o violeta da marca como destaque.
    /// </summary>
    internal static class WindowsTheme
    {
        /// <summary>Violeta do Pastel (cor de ação da marca).</summary>
        public static readonly Color Violet = Color.FromRgb(0x6C, 0x4D, 0xFF);

        private static AppConfig Cfg => ConfigStore.Current;

        /// <summary>O tema que o Windows está usando (padrão claro quando a chave não existe).</summary>
        public static bool WindowsIsLight
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    return k?.GetValue("AppsUseLightTheme") is int v ? v != 0 : true;
                }
                catch { return true; }
            }
        }

        /// <summary>O tema que o Pastel usa: o do Windows, a menos que o usuário tenha escolhido outro.</summary>
        public static bool IsLight => Cfg.ThemeMode switch
        {
            "light" => true,
            "dark" => false,
            _ => WindowsIsLight
        };

        /// <summary>Cor de destaque do Windows (a que aparece em Configurações > Cores).</summary>
        public static Color WindowsAccent
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                    if (k?.GetValue("AccentColor") is int abgr)
                        return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                }
                catch { }
                var g = SystemParameters.WindowGlassColor;
                return Color.FromRgb(g.R, g.G, g.B);
            }
        }

        public static Color Accent => Cfg.AccentMode == "brand" ? Violet : WindowsAccent;

        /// <summary>
        /// Muda quando o tema, o destaque ou a versão do desenho dos ícones muda; serve para saber
        /// se os ícones das pastas precisam ser refeitos.
        /// </summary>
        public static string Signature
        {
            get
            {
                var a = Accent;
                return $"v3-{Cfg.ThemeMode}-{(IsLight ? "L" : "D")}-{Cfg.AccentMode}-{a.R:X2}{a.G:X2}{a.B:X2}";
            }
        }

        /// <summary>
        /// A cor de destaque, ou um neutro do tema quando ela some no fundo (ex.: destaque preto no
        /// tema escuro). Assim contornos e realces continuam visíveis.
        /// </summary>
        public static Color Visible(Color accent, bool light)
        {
            double luma = 0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B; // 0..255
            if (!light && luma < 60) return Color.FromRgb(0xF4, 0xEE, 0xFF);
            if (light && luma > 200) return Color.FromRgb(0x2B, 0x25, 0x40);
            return accent;
        }

        public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>Mistura b em a (t = 0 só a, 1 só b), sem alterar o alfa.</summary>
        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // Cores da marca por tema
        public static Color Ink => Color.FromRgb(0x2B, 0x25, 0x40);        // Tinta
        public static Color Lavender => Color.FromRgb(0xF4, 0xEE, 0xFF);   // texto no escuro
        public static Color Cream => Color.FromRgb(0xFF, 0xF9, 0xF5);      // fundo claro
        public static Color Plum => Color.FromRgb(0x1B, 0x17, 0x26);       // Ameixa, fundo escuro
        public static Color Text(bool light) => light ? Ink : Lavender;
        public static Color Surface(bool light) => light ? Cream : Plum;
    }
}
