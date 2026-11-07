using System;
using System.Diagnostics;
using System.IO;

namespace Pastel
{
    /// <summary>
    /// Pastas do usuário seguindo o padrão XDG (o mesmo que KDE, GNOME e os outros usam).
    /// Com PASTEL_SANDBOX definido tudo vai para essa pasta: os testes nunca mexem na pasta pessoal de verdade.
    /// </summary>
    internal static class Paths
    {
        private static string? Sandbox => Environment.GetEnvironmentVariable("PASTEL_SANDBOX") is { Length: > 0 } s ? s : null;

        public static string Home => Sandbox ?? Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string Xdg(string variable, string fallbackUnderHome)
        {
            if (Sandbox == null && Environment.GetEnvironmentVariable(variable) is { Length: > 0 } v && Path.IsPathRooted(v)) return v;
            return Path.Combine(Home, fallbackUnderHome);
        }

        public static string ConfigHome => Xdg("XDG_CONFIG_HOME", ".config");
        public static string DataHome => Xdg("XDG_DATA_HOME", ".local/share");

        public static string ConfigDir => Path.Combine(ConfigHome, "pastel");
        public static string DataDir => Path.Combine(DataHome, "pastel");
        public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
        public static string IconsDir => Path.Combine(DataDir, "icons");
        public static string ManagedDir(string id) => Path.Combine(DataDir, "Grupos", id);

        public static string ApplicationsDir => Path.Combine(DataHome, "applications");
        public static string ServiceMenusDir => Path.Combine(DataHome, "kio", "servicemenus");
        public static string AutostartDir => Path.Combine(ConfigHome, "autostart");
        public static string KdeGlobals => Path.Combine(ConfigHome, "kdeglobals");

        public static string? ExePath => Environment.ProcessPath;

        private static string? _desktop;

        /// <summary>A pasta "Área de Trabalho" do usuário (pode ter outro nome conforme o idioma).</summary>
        public static string DesktopDir
        {
            get
            {
                if (_desktop != null) return _desktop;
                if (Sandbox != null) return _desktop = Path.Combine(Sandbox, "Desktop");

                string? found = null;
                try
                {
                    var psi = new ProcessStartInfo("xdg-user-dir", "DESKTOP") { RedirectStandardOutput = true, UseShellExecute = false };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        found = p.StandardOutput.ReadLine()?.Trim();
                        p.WaitForExit(2000);
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(found) || !Path.IsPathRooted(found)) found = Path.Combine(Home, "Desktop");
                return _desktop = found;
            }
        }

        /// <summary>Sistema rodando com a sessão gráfica Wayland? (o app usa XWayland, mas isto muda o que dá para fazer.)</summary>
        public static bool IsWaylandSession =>
            string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }
}
