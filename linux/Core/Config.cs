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
        /// <summary>Se o Pastel criou este atalho .desktop (Path) apontando para um arquivo, programa ou pasta, o caminho desse original.
        /// O original nunca é movido nem apagado; ao remover o item só o atalho criado é apagado.</summary>
        public string? SourcePath { get; set; }
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
        /// <summary>Atalho .desktop da pasta na área de trabalho.</summary>
        public string? LauncherPath { get; set; }
        /// <summary>Versão do ícone gerado; muda a cada atualização para o KDE não usar o ícone em cache.</summary>
        public int IconRev { get; set; }
        /// <summary>Recém-criada pelo usuário e ainda sem nenhum item: não é ocultada por "Ocultar pastas vazias". Pastas antigas valem false.</summary>
        public bool Fresh { get; set; }
        public List<ShortcutItem> Items { get; set; } = new();
    }

    /// <summary>Barra flutuante de apps na área de trabalho.</summary>
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
        public bool ShowBackground { get; set; } = true;
        public bool ShowName { get; set; } = true;
        /// <summary>"none", "focus" ou "proximity".</summary>
        public string HoverEffect { get; set; } = "focus";
        public List<ShortcutItem> Items { get; set; } = new();
    }

    public class AppConfig
    {
        /// <summary>"auto" (segue o KDE), "light" ou "dark".</summary>
        public string ThemeMode { get; set; } = "auto";
        /// <summary>"system" (cor de destaque do KDE) ou "brand" (violeta do Pastel).</summary>
        public string AccentMode { get; set; } = "system";
        public bool Animations { get; set; } = true;
        /// <summary>Submenu "Pastel" no botão direito (Dolphin e área de trabalho).</summary>
        public bool SendToMenu { get; set; } = true;
        /// <summary>Tema com que os ícones das pastas foram desenhados (para refazê-los se mudar).</summary>
        public string ThemeSig { get; set; } = "";
        /// <summary>Tamanho da célula da grade de ícones do Plasma, em pixels. 0 = padrão.</summary>
        public int GridCellWidth { get; set; }
        public int GridCellHeight { get; set; }
        /// <summary>Procura versões novas no GitHub (a única conexão de rede do Pastel).</summary>
        public bool CheckUpdates { get; set; } = true;
        /// <summary>Última versão que o usuário disse "agora não"; só volta a avisar quando sair outra.</summary>
        public string SkippedVersion { get; set; } = "";
        /// <summary>Pasta vazia (e não recém-criada) sai da área de trabalho e fica guardada em Ocultas/ até ganhar item.</summary>
        public bool HideEmptyFolders { get; set; }
        public List<FolderModel> Folders { get; set; } = new();
        public List<FloatingModel> Floats { get; set; } = new();
    }

    public static class ConfigStore
    {
        private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public static AppConfig Current { get; internal set; } = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(Paths.ConfigFile))
                    Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.ConfigFile), Opts) ?? new AppConfig();
            }
            catch { Current = new AppConfig(); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.ConfigDir);
                string tmp = Paths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
                File.Move(tmp, Paths.ConfigFile, overwrite: true); // nunca deixa a configuração pela metade
            }
            catch { /* melhor esforço */ }
        }
    }
}
