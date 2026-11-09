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
        /// <summary>Se o item é um atalho criado pelo Pastel para um arquivo ou programa (que não é atalho), o caminho dele. O original nunca é movido.</summary>
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
        /// <summary>Arquivo da pasta (.wfgroup) na área de trabalho. (Versões antigas: .lnk.)</summary>
        public string? LnkPath { get; set; }
        public List<ShortcutItem> Items { get; set; } = new();
        /// <summary>Recém-criada pelo usuário e ainda sem nenhum item: "ocultar pastas vazias" não a esconde até ela receber o primeiro item. Pastas antigas (campo ausente) valem como não-novas.</summary>
        public bool Fresh { get; set; }
        /// <summary>
        /// Se preenchido, a pasta espelha este diretório de verdade (ex.: Downloads): o balão mostra o que há nele,
        /// sempre atualizado, e a lista de Items é só um reflexo. Nada é movido nem apagado de lá.
        /// </summary>
        public string? SourcePath { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsMirror => !string.IsNullOrEmpty(SourcePath);
    }

    /// <summary>Regra que manda sozinha para uma pasta ou flutuante os arquivos novos da área de trabalho.</summary>
    public class AutoRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Regra";
        public bool Enabled { get; set; } = true;
        /// <summary>Extensões sem ponto, minúsculas (ex.: "pdf", "docx"). Vazio = qualquer uma.</summary>
        public List<string> Extensions { get; set; } = new();
        /// <summary>Trecho que o nome do arquivo precisa conter (ignora maiúsculas). Vazio = qualquer nome.</summary>
        public string NameContains { get; set; } = "";
        /// <summary>Regra pronta por tipo (id do RuleCatalog, ex.: "jogos"). Quando preenchida, as extensões e o trecho do nome são ignorados.</summary>
        public string Category { get; set; } = "";
        /// <summary>Id da pasta ou flutuante que recebe os arquivos.</summary>
        public string TargetId { get; set; } = "";
        /// <summary>Nome do destino na última vez que foi escolhido. Se a pasta for excluída, é com esse nome que ela é recriada.</summary>
        public string TargetName { get; set; } = "";
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
        /// <summary>Procura versões novas no GitHub (a única conexão de rede do Pastel).</summary>
        public bool CheckUpdates { get; set; } = true;
        /// <summary>Oculta da área de trabalho as pastas que ficaram vazias (o arquivo .wfgroup sai da área de trabalho e fica guardado em Ocultas\; volta quando receberem itens).</summary>
        public bool HideEmptyFolders { get; set; }
        /// <summary>Última versão que o usuário disse "agora não"; só volta a avisar quando sair outra.</summary>
        public string SkippedVersion { get; set; } = "";
        public List<FolderModel> Folders { get; set; } = new();
        public List<FloatingModel> Floats { get; set; } = new();
        public List<AutoRule> Rules { get; set; } = new();
        /// <summary>Já perguntamos, na primeira abertura, se podia organizar a área de trabalho por tipo.</summary>
        public bool OrganizeAsked { get; set; }
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
                bool migratedFromPastaApp = false;
                if (!File.Exists(FilePath) && File.Exists(System.IO.Path.Combine(WFolderDir, "config.json")))
                {
                    CopyDirectory(WFolderDir, Dir); // copia (não move): a pasta antiga fica intacta até a migração terminar
                    migratedFromWFolder = true;
                }
                else if (!File.Exists(FilePath) && File.Exists(OldFilePath))
                {
                    Directory.CreateDirectory(Dir);
                    File.Copy(OldFilePath, FilePath); // migra a configuração da versão PastaApp
                    migratedFromPastaApp = true;
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

                // A migração é de uma vez só. Se os dados antigos ficassem lá, depois de desinstalar (que apaga a pasta
                // nova e devolve os atalhos à área de trabalho) a próxima abertura os copiaria de novo e os atalhos
                // apareceriam duplicados: o original na área de trabalho e a cópia dentro da pasta.
                // Renomeia em vez de apagar, para o usuário ainda poder recuperar algo.
                if (migratedFromWFolder && File.Exists(FilePath)) RetireLegacy(WFolderDir);
                if (migratedFromPastaApp && File.Exists(FilePath)) RetireLegacy(OldFilePath);
            }
            catch { Current = new AppConfig(); }
        }

        private static void RetireLegacy(string path)
        {
            try
            {
                string target = path + ".migrado";
                for (int i = 2; Directory.Exists(target) || File.Exists(target); i++) target = path + ".migrado" + i;
                if (Directory.Exists(path)) Directory.Move(path, target);
                else if (File.Exists(path)) File.Move(path, target);
            }
            catch { /* melhor esforço: pior caso, migra de novo */ }
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
