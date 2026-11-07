using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Pastel
{
    /// <summary>
    /// Integração com o KDE (e o que for padrão freedesktop): entrada no menu de aplicativos com ações,
    /// submenu "Pastel" no botão direito do Dolphin e da área de trabalho, e iniciar com a sessão.
    /// Tudo só na pasta do usuário, em arquivos .desktop; nada de sistema é tocado.
    /// </summary>
    internal static class ShellMenus
    {
        public const string FolderArg = "--folder";
        public const string SendFolderArg = "--send-folder";
        public const string SendFolderNewArg = "--send-folder-new";
        public const string SendFloatArg = "--send-float";
        public const string SendFloatNewArg = "--send-float-new";
        public const string NewFolderArg = "--new-folder";
        public const string NewFloatArg = "--new-float";
        public const string SettingsArg = "--settings";
        public const string BackgroundArg = "--background";

        private const string ServiceMenuFile = "pastel-enviar.desktop";

        private static string Exec(string exe, string args) => $"{DesktopEntry.QuoteExecArg(exe)} {args}";

        private static string Esc(string v) => v.Replace("\\", "\\\\").Replace("\n", " ").Replace("\r", "");

        // ---------- menu de aplicativos ----------

        /// <summary>Entrada "Pastel" no menu de aplicativos; o botão direito nela mostra Nova pasta, Novo flutuante e Configurações.</summary>
        public static void RegisterApplication(string exe, string iconPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[Desktop Entry]");
                sb.AppendLine("Type=Application");
                sb.AppendLine("Version=1.0");
                sb.AppendLine("Name=Pastel");
                sb.AppendLine("Comment=A pasta do celular, no seu PC.");
                sb.AppendLine("Exec=" + Exec(exe, SettingsArg));
                sb.AppendLine("Icon=" + Esc(iconPath));
                sb.AppendLine("Terminal=false");
                sb.AppendLine("Categories=Utility;");
                sb.AppendLine("StartupNotify=false");
                sb.AppendLine("Actions=NewFolder;NewFloat;Settings;");
                sb.AppendLine();
                sb.AppendLine("[Desktop Action NewFolder]");
                sb.AppendLine("Name=Nova pasta");
                sb.AppendLine("Exec=" + Exec(exe, NewFolderArg));
                sb.AppendLine();
                sb.AppendLine("[Desktop Action NewFloat]");
                sb.AppendLine("Name=Novo flutuante");
                sb.AppendLine("Exec=" + Exec(exe, NewFloatArg));
                sb.AppendLine();
                sb.AppendLine("[Desktop Action Settings]");
                sb.AppendLine("Name=Configurações");
                sb.AppendLine("Exec=" + Exec(exe, SettingsArg));
                DesktopEntry.WriteExecutable(Path.Combine(Paths.ApplicationsDir, "pastel.desktop"), sb.ToString());
            }
            catch { }
        }

        // ---------- iniciar com a sessão ----------

        private static string AutostartFile => Path.Combine(Paths.AutostartDir, "pastel.desktop");

        public static bool IsAutostart() => File.Exists(AutostartFile);

        public static void SetAutostart(bool on, string exe, string iconPath)
        {
            try
            {
                if (!on) { if (File.Exists(AutostartFile)) File.Delete(AutostartFile); return; }
                var content = string.Join("\n", new[]
                {
                    "[Desktop Entry]", "Type=Application", "Name=Pastel",
                    "Exec=" + Exec(exe, BackgroundArg), "Icon=" + Esc(iconPath),
                    "X-GNOME-Autostart-enabled=true", "X-KDE-autostart-after=panel", "Terminal=false", ""
                });
                DesktopEntry.WriteExecutable(AutostartFile, content);
            }
            catch { }
        }

        // ---------- botão direito: Pastel > Enviar para ----------

        /// <summary>
        /// Submenu "Pastel" nos menus de contexto do Dolphin (e de arquivos na área de trabalho do Plasma), com as
        /// pastas e flutuantes atuais. É um arquivo estático, então é recriado sempre que a lista muda.
        /// O gerenciador manda todos os arquivos selecionados de uma vez (%F).
        /// </summary>
        public static void RegisterServiceMenu(IEnumerable<FolderModel> folders, IEnumerable<FloatingModel> floats, string exe, string iconPath)
        {
            try
            {
                var actions = new List<(string id, string name, string args)>();
                int i = 0;
                foreach (var f in folders) actions.Add(($"F{i++}", "Pasta: " + f.Name, $"{SendFolderArg} {f.Id} %F"));
                actions.Add(("FNew", "Nova pasta…", $"{SendFolderNewArg} %F"));
                i = 0;
                foreach (var f in floats) actions.Add(($"G{i++}", "Flutuante: " + f.Name, $"{SendFloatArg} {f.Id} %F"));
                actions.Add(("GNew", "Novo flutuante…", $"{SendFloatNewArg} %F"));

                var sb = new StringBuilder();
                sb.AppendLine("[Desktop Entry]");
                sb.AppendLine("Type=Service");
                sb.AppendLine("MimeType=all/all;inode/directory;");
                sb.AppendLine("X-KDE-ServiceTypes=KonqPopupMenu/Plugin");
                sb.AppendLine("X-KDE-Submenu=Pastel");
                sb.AppendLine("Icon=" + Esc(iconPath));
                sb.AppendLine("Actions=" + string.Join(";", actions.Select(a => a.id)) + ";");
                foreach (var (id, name, args) in actions)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[Desktop Action {id}]");
                    sb.AppendLine("Name=" + Esc(name));
                    sb.AppendLine("Exec=" + Exec(exe, args));
                }
                DesktopEntry.WriteExecutable(Path.Combine(Paths.ServiceMenusDir, ServiceMenuFile), sb.ToString());
                RefreshKdeCache();
            }
            catch { }
        }

        public static void UnregisterServiceMenu()
        {
            try { File.Delete(Path.Combine(Paths.ServiceMenusDir, ServiceMenuFile)); } catch { }
        }

        /// <summary>O Plasma 5 só enxerga menus novos depois de atualizar o cache de serviços; o 6 lê na hora.</summary>
        private static void RefreshKdeCache()
        {
            foreach (var tool in new[] { "kbuildsycoca6", "kbuildsycoca5" })
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                    if (p != null) return;
                }
                catch { /* não instalado */ }
            }
        }
    }
}
