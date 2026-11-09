using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pastel
{
    /// <summary>
    /// Integração com o Windows, sempre só no usuário atual (HKCU): botão "Nova pasta" no menu da
    /// área de trabalho, o tipo de arquivo das pastas e o menu "Pastel > Enviar para".
    /// </summary>
    internal static class ShellIntegration
    {
        public const string NewGroupArg = "--new-group";
        public const string NewFloatArg = "--new-float";
        public const string SendFolderArg = "--send-folder";
        public const string SendFolderNewArg = "--send-folder-new";
        public const string SendFloatArg = "--send-float";
        public const string SendFloatNewArg = "--send-float-new";
        public const string SettingsArg = "--settings";

        private const string KeyPath = @"Software\Classes\DesktopBackground\Shell\Pastel.Menu";
        private const string OldKeyPath = @"Software\Classes\DesktopBackground\Shell\Pastel.NovoGrupo"; // era um botão só
        private const string ProgId = "Pastel.Group";
        private const string TypeName = "Pasta do Pastel";
        private const string ExeDropTarget = "{86C86720-42A0-1069-A2E8-08002B30309D}"; // o mesmo de .exe/.bat

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        // ---------- botão "Nova pasta" no menu da área de trabalho ----------

        public static bool IsRegistered()
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k != null;
        }

        /// <summary>Submenu "Pastel" no botão direito da área de trabalho: Nova pasta, Novo flutuante, Configurações.</summary>
        public static void Register()
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return;
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(OldKeyPath, false);
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("MUIVerb", "Pastel");
                    k.SetValue("Icon", $"\"{exe}\"");
                    k.SetValue("SubCommands", "");
                }
                WriteEntry(exe, KeyPath + @"\shell\10nova", "Nova pasta", NewGroupArg, separator: false);
                WriteEntry(exe, KeyPath + @"\shell\20flutuante", "Novo flutuante", NewFloatArg, separator: false);
                WriteEntry(exe, KeyPath + @"\shell\30config", "Configurações…", SettingsArg, separator: true);
            }
            catch { }
        }

        public static void Unregister()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(OldKeyPath, false); } catch { }
        }

        // ---------- tipo de arquivo das pastas (.wfgroup) ----------

        /// <summary>
        /// Registra o tipo de arquivo das pastas: ícone = o próprio arquivo ("%1", sem a seta de
        /// atalho), abrir = Pastel, soltar arquivos = passa os caminhos. Também pede ao Windows para
        /// não listar as pastas em "Recentes" do Menu Iniciar (NoRecentDocs) nem o Pastel entre os
        /// apps mais usados (NoStartPage).
        /// </summary>
        public static void RegisterGroupType()
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return;
            try
            {
                string command = $"\"{exe}\" {GroupShortcut.GroupFileArg} \"%1\" %*";
                bool upToDate;
                using (var c = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                using (var d = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}"))
                using (var a = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Applications\{Path.GetFileName(exe)}"))
                    upToDate = (c?.GetValue("") as string) == command && (d?.GetValue("") as string) == TypeName &&
                               d?.GetValue("NoRecentDocs") != null && a?.GetValue("NoStartPage") != null;
                if (upToDate) return;

                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{GroupShortcut.Ext}"))
                    k.SetValue("", ProgId);
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                {
                    k.SetValue("", TypeName);
                    k.SetValue("NeverShowExt", "");
                    k.SetValue("NoRecentDocs", ""); // não entra em "Recentes"
                }
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
                    k.SetValue("", "%1");
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                    k.SetValue("", command);
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shellex\DropHandler"))
                    k.SetValue("", ExeDropTarget);
                using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{Path.GetFileName(exe)}"))
                    k.SetValue("NoStartPage", ""); // não entra nos apps mais usados do Menu Iniciar

                SHChangeNotify(0x08000000 /*ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        /// <summary>
        /// Apaga dos Recentes do Windows (de onde o Menu Iniciar tira "Recentes") as entradas que
        /// apontam para arquivos de pasta do Pastel. Só mexe nessas; as demais ficam como estão.
        /// </summary>
        public static void PurgeRecents(IEnumerable<string> folderFiles)
        {
            try
            {
                string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (!Directory.Exists(recent)) return;

                var bases = folderFiles.Select(p => Path.GetFileNameWithoutExtension(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (bases.Count == 0) return;

                dynamic? shell = null;
                try
                {
                    foreach (var lnk in Directory.EnumerateFiles(recent, "*.lnk"))
                    {
                        string name = Path.GetFileNameWithoutExtension(lnk);
                        // o Windows nomeia como "Nome.lnk" ou "Nome (2).lnk"
                        string stripped = Regex.Replace(name, @" \(\d+\)$", "");
                        if (!bases.Contains(stripped, StringComparer.OrdinalIgnoreCase)) continue;

                        shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                        dynamic sc = shell!.CreateShortcut(lnk);
                        string target = sc.TargetPath;
                        Marshal.FinalReleaseComObject(sc);
                        if (target.EndsWith(GroupShortcut.Ext, StringComparison.OrdinalIgnoreCase))
                            try { File.Delete(lnk); } catch { }
                    }
                }
                finally { if (shell != null) Marshal.FinalReleaseComObject(shell); }
            }
            catch { }
        }

        // ---------- menu "Pastel > Enviar para" (qualquer arquivo ou pasta) ----------

        // "*" cobre arquivos em geral (inclusive .lnk), "Directory" as pastas e "InternetShortcut" os .url,
        // que o Windows trata à parte (testado: sem isto o menu não aparece neles). "lnkfile" (.lnk) também: só assim o
        // Explorer entrega o próprio atalho em "%1"; pela chave "*" ele entrega o programa de destino do atalho.
        private static readonly string[] SendRoots = { "*", "Directory", "InternetShortcut", "lnkfile" };
        private static string SendKey(string root) => $@"Software\Classes\{root}\shell\Pastel.Enviar";

        /// <summary>
        /// Recria o submenu com as pastas e flutuantes atuais. Menus por registro são estáticos,
        /// então isto roda sempre que a lista muda. No Windows 11 aparece em "Mostrar mais opções".
        /// </summary>
        public static void RegisterSendTo(IEnumerable<FolderModel> folders, IEnumerable<FloatingModel> floats)
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return;
            try
            {
                var folderEntries = folders.Select(f => (Label(f.Name), $"{SendFolderArg} {f.Id} \"%1\"")).ToList();
                var floatEntries = floats.Select(f => (Label(f.Name), $"{SendFloatArg} {f.Id} \"%1\"")).ToList();

                foreach (var root in SendRoots)
                {
                    string key = SendKey(root);
                    Registry.CurrentUser.DeleteSubKeyTree(key, false);
                    using (var k = Registry.CurrentUser.CreateSubKey(key))
                    {
                        k.SetValue("MUIVerb", "Pastel");
                        k.SetValue("Icon", $"\"{exe}\"");
                        k.SetValue("SubCommands", "");
                    }
                    WriteSubmenu(exe, key + @"\shell\10pasta", "Enviar para pasta", folderEntries, "Nova pasta…", $"{SendFolderNewArg} \"%1\"", "p");
                    WriteSubmenu(exe, key + @"\shell\20flutuante", "Enviar para flutuante", floatEntries, "Novo flutuante…", $"{SendFloatNewArg} \"%1\"", "f");
                }
            }
            catch { }
        }

        public static void UnregisterSendTo()
        {
            foreach (var root in SendRoots)
                try { Registry.CurrentUser.DeleteSubKeyTree(SendKey(root), false); } catch { }
        }

        private static string Label(string name) => name.Replace("&", "&&"); // "&" seria tecla de atalho no menu

        private static void WriteSubmenu(string exe, string path, string title,
            List<(string label, string args)> entries, string newLabel, string newArgs, string prefix)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(path))
            {
                k.SetValue("MUIVerb", title);
                k.SetValue("SubCommands", "");
            }

            int i = 0;
            foreach (var (label, args) in entries)
                WriteEntry(exe, $@"{path}\shell\{prefix}{i++:D3}", label, args, separator: false);
            WriteEntry(exe, $@"{path}\shell\{prefix}{i:D3}", newLabel, newArgs, separator: entries.Count > 0);
        }

        private static void WriteEntry(string exe, string path, string label, string args, bool separator)
        {
            using var k = Registry.CurrentUser.CreateSubKey(path);
            k.SetValue("MUIVerb", label);
            if (separator) k.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord); // linha separadora acima
            using var c = k.CreateSubKey("command");
            c.SetValue("", $"\"{exe}\" {args}");
        }

        // ---------- nome anterior ----------

        /// <summary>Remove as chaves do registro criadas quando o app se chamava WFolder.</summary>
        public static void RemoveLegacyKeys()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\Shell\WFolder.NovoGrupo", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\WFolder.Group", false); } catch { }
        }
    }
}
