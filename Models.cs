using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pastel
{
    public class ShortcutItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        /// <summary>Se o atalho foi tirado da área de trabalho, onde ele estava (para devolver depois).</summary>
        public string? OriginalPath { get; set; }
    }

    /// <summary>Quem guarda uma lista de apps (pasta ou flutuante).</summary>
    public interface IItemOwner
    {
        string Id { get; }
        string Name { get; set; }
        List<ShortcutItem> Items { get; }
    }

    public class FolderModel : IItemOwner
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Pasta";
        /// <summary>Arquivo da pasta (.wfgroup) na área de trabalho. (Versões antigas: .lnk.)</summary>
        public string? LnkPath { get; set; }
        public List<ShortcutItem> Items { get; set; } = new();
    }

    /// <summary>Barra flutuante de apps na área de trabalho, encaixada na grade de ícones do Windows.</summary>
    public class FloatingModel : IItemOwner
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Flutuante";
        /// <summary>Célula da grade da área de trabalho. int.MinValue = ainda sem lugar.</summary>
        public int Col { get; set; } = int.MinValue;
        public int Row { get; set; } = int.MinValue;
        public bool Vertical { get; set; }
        /// <summary>24 (mínimo), 32 (pequeno), 40 (médio) ou 52 (grande).</summary>
        public int IconSize { get; set; } = 40;
        public bool Visible { get; set; } = true;
        /// <summary>false = sem o vidro por trás (os ícones ficam soltos na área de trabalho).</summary>
        public bool ShowBackground { get; set; } = true;
        /// <summary>false = sem o nome embaixo (ou ao lado) dos ícones.</summary>
        public bool ShowName { get; set; } = true;
        /// <summary>"none" (nenhum), "focus" (o ícone sob o mouse cresce) ou "proximity" (quanto mais perto do mouse, maior).</summary>
        public string HoverEffect { get; set; } = "focus";
        public List<ShortcutItem> Items { get; set; } = new();
    }

    public class AppConfig
    {
        /// <summary>Tamanho do ícone em pixels (48 = "médio" do Windows, 96 = "grande").</summary>
        public int IconSize { get; set; } = 48;
        /// <summary>Mantém o botão "Nova pasta" no menu de contexto da área de trabalho.</summary>
        public bool DesktopMenu { get; set; } = true;
        /// <summary>Tema do Windows com que os ícones das pastas foram desenhados (para refazê-los se mudar).</summary>
        public string ThemeSig { get; set; } = "";
        /// <summary>"auto" (segue o Windows), "light" ou "dark".</summary>
        public string ThemeMode { get; set; } = "auto";
        /// <summary>"windows" (cor de destaque do Windows) ou "brand" (violeta do Pastel).</summary>
        public string AccentMode { get; set; } = "windows";
        public bool Animations { get; set; } = true;
        /// <summary>Menu "Pastel > Enviar para" ao clicar com o botão direito em qualquer arquivo ou pasta.</summary>
        public bool SendToMenu { get; set; } = true;
        public List<FolderModel> Folders { get; set; } = new();
        public List<FloatingModel> Floats { get; set; } = new();
    }

    public static class ConfigStore
    {
        public static readonly string Dir =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pastel");
        private static readonly string OldFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PastaApp", "config.json");
        // Nome anterior do app: a pasta de dados era %AppData%\WFolder.
        private static readonly string WFolderDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WFolder");
        private static readonly string FilePath = System.IO.Path.Combine(Dir, "config.json");
        private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

        public static AppConfig Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                bool migratedFromWFolder = false;
                if (!File.Exists(FilePath) && File.Exists(System.IO.Path.Combine(WFolderDir, "config.json")))
                {
                    CopyDirectory(WFolderDir, Dir); // copia (não move): a pasta antiga fica intacta
                    migratedFromWFolder = true;
                }
                else if (!File.Exists(FilePath) && File.Exists(OldFilePath))
                {
                    Directory.CreateDirectory(Dir);
                    File.Copy(OldFilePath, FilePath); // migra a configuração da versão PastaApp
                }
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new AppConfig();

                if (migratedFromWFolder)
                {
                    // Os atalhos tirados da área de trabalho agora moram na pasta nova.
                    foreach (var f in Current.Folders)
                        foreach (var it in f.Items)
                            if (it.Path.StartsWith(WFolderDir + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                it.Path = System.IO.Path.Combine(Dir, it.Path.Substring(WFolderDir.Length + 1));
                    Save();
                }
            }
            catch { Current = new AppConfig(); }
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), true);
            foreach (var sub in Directory.GetDirectories(from))
                CopyDirectory(sub, System.IO.Path.Combine(to, System.IO.Path.GetFileName(sub)));
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Opts));
            }
            catch { /* melhor esforço */ }
        }
    }
}
