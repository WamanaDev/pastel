using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pastel
{
    /// <summary>
    /// Regras prontas, por tipo (como o "Agrupar por tipo" do macOS e as regras por tipo do Fences).
    /// Arquivos são classificados pela extensão; atalhos (.lnk, .url) e programas (.exe) pelo que eles abrem.
    /// A classificação de aplicativos é uma heurística por nome e destino: serve para a maioria, não para todos.
    /// </summary>
    internal static class RuleCatalog
    {
        public sealed record Category(
            string Id, string Name, string Description,
            string[] FileExts,      // arquivos comuns, pela extensão
            string[] Exes,          // programas: nome do executável de destino, sem .exe (igualdade)
            string[] Names,         // trecho do nome do atalho ou do programa (mín. 4 letras, para não casar à toa)
            string[] UrlPrefixes);  // atalhos de internet (.url): começo do endereço

        private static readonly string[] None = Array.Empty<string>();

        /// <summary>Em ordem de prioridade: a primeira categoria que casa vence.</summary>
        public static readonly Category[] All =
        {
            // ----- aplicativos (atalhos e programas) -----
            new("jogos", "Jogos", "Jogos e lojas de jogos: Steam, Epic, Riot, Battle.net, Roblox, Minecraft…",
                None,
                new[] { "steam", "epicgameslauncher", "riotclientservices", "battle.net", "upc", "eadesktop", "galaxyclient", "robloxplayerbeta", "robloxstudiobeta", "minecraftlauncher", "rockstarlauncher", "pcsx2-qt" },
                new[] { "steam", "epic games", "riot", "valorant", "league of legends", "battle.net", "ubisoft", "ea app", "gog galaxy", "minecraft", "roblox", "rockstar", "fortnite", "xbox", "pcsx2", "emulator", "genshin", "counter-strike", "dota", "fivem", "habbo",
                  // jogo instalado numa pasta de loja ou de jogos: o caminho do executável entrega
                  "\\steamapps\\", "\\epic games\\", "\\riot games\\", "\\xboxgames\\", "\\games\\", "\\gog games\\" },
                new[] { "steam://", "com.epicgames.launcher://", "uplay://", "battlenet://", "origin2://", "goggalaxy://", "roblox://", "riotclient://" }),
            new("navegadores", "Navegadores", "Chrome, Edge, Firefox, Brave, Opera, Vivaldi…",
                None,
                new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "tor", "arc" },
                new[] { "google chrome", "microsoft edge", "firefox", "brave", "opera", "vivaldi", "tor browser", "chromium" },
                None),
            new("comunicacao", "Comunicação", "Discord, WhatsApp, Telegram, Teams, Zoom, Slack, Skype…",
                None,
                new[] { "discord", "whatsapp", "telegram", "teams", "ms-teams", "zoom", "slack", "skype", "signal" },
                new[] { "discord", "whatsapp", "telegram", "microsoft teams", "zoom", "slack", "skype", "messenger" },
                new[] { "discord://", "tg://", "whatsapp://" }),
            new("dev", "Desenvolvimento", "Editores, IDEs, Docker, Git, clientes de API e terminais.",
                None,
                new[] { "code", "devenv", "docker desktop", "postman", "insomnia", "bruno", "studio64", "idea64", "pycharm64", "webstorm64", "rider64", "notepad++", "githubdesktop", "windowsterminal", "wt", "powershell", "cmd" },
                new[] { "visual studio", "docker", "postman", "insomnia", "bruno", "android studio", "jetbrains", "intellij", "pycharm", "webstorm", "rider", "notepad++", "github desktop", "git bash", "terminal", "antigravity", "cursor", "xampp", "mysql", "dbeaver" },
                None),
            new("criacao", "Criação e design", "Photoshop, Figma, Blender, Premiere, DaVinci, OBS, CapCut, Canva…",
                None,
                new[] { "photoshop", "illustrator", "figma", "blender", "premiere pro", "afterfx", "resolve", "obs64", "capcut", "audacity", "krita", "gimp-2.10" },
                new[] { "photoshop", "illustrator", "figma", "blender", "premiere", "after effects", "davinci", "obs studio", "capcut", "canva", "audacity", "krita", "gimp", "lightroom", "unreal engine", "unity", "wallpaper engine", "voicestudio" },
                None),
            new("escritorio", "Escritório", "Word, Excel, PowerPoint, Outlook, OneNote, LibreOffice, Notion, Acrobat…",
                None,
                new[] { "winword", "excel", "powerpnt", "outlook", "onenote", "soffice", "notion", "acrord32", "acrobat" },
                new[] { "microsoft word", "microsoft excel", "powerpoint", "outlook", "onenote", "libreoffice", "notion", "acrobat", "office" },
                None),
            new("midia", "Música e vídeo (apps)", "Spotify, VLC, YouTube Music, Netflix, players e streaming.",
                None,
                new[] { "spotify", "vlc", "mpv", "potplayer", "mpc-hc64" },
                new[] { "spotify", "vlc", "youtube music", "netflix", "prime video", "disney", "deezer", "elive", "karafun", "media player" },
                None),
            new("utilitarios", "Utilitários", "Compactadores, VPN, acesso remoto, limpeza e ferramentas do sistema.",
                None,
                new[] { "7zfm", "winrar", "anydesk", "teamviewer", "ccleaner64", "radmin" },
                new[] { "7-zip", "winrar", "anydesk", "teamviewer", "ccleaner", "radmin", "powertoys" },
                None),

            // ----- arquivos (pela extensão) -----
            new("documentos", "Documentos", "Word, texto, RTF, Markdown e similares.",
                new[] { "doc", "docx", "odt", "rtf", "txt", "md", "pages" }, None, None, None),
            new("pdfs", "PDFs", "Arquivos PDF.",
                new[] { "pdf" }, None, None, None),
            new("planilhas", "Planilhas", "Excel, CSV e similares.",
                new[] { "xls", "xlsx", "xlsm", "csv", "ods", "numbers" }, None, None, None),
            new("apresentacoes", "Apresentações", "PowerPoint e similares.",
                new[] { "ppt", "pptx", "pps", "ppsx", "odp", "key" }, None, None, None),
            new("imagens", "Imagens", "Fotos, capturas e desenhos.",
                new[] { "png", "jpg", "jpeg", "gif", "webp", "bmp", "svg", "heic", "tif", "tiff", "ico", "psd" }, None, None, None),
            new("videos", "Vídeos", "Filmes e gravações.",
                new[] { "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v" }, None, None, None),
            new("audio", "Áudio", "Músicas e gravações de som.",
                new[] { "mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus" }, None, None, None),
            new("compactados", "Compactados", "ZIP, RAR, 7z e similares.",
                new[] { "zip", "rar", "7z", "tar", "gz", "bz2", "xz" }, None, None, None),
            new("instaladores", "Instaladores e imagens de disco", "MSI, MSIX, ISO e similares. (Executáveis .exe ficam de fora, pois também são programas.)",
                new[] { "msi", "msix", "msixbundle", "appx", "iso", "img" }, None, None, None),
            new("codigo", "Código e dados", "Fontes, JSON, SQL, planilhas de dados e projetos.",
                new[] { "js", "ts", "tsx", "jsx", "py", "cs", "java", "cpp", "c", "h", "go", "rs", "php", "html", "css", "json", "xml", "yml", "yaml", "sql", "sh", "ps1", "ipynb" }, None, None, None),
        };

        public static Category? Find(string id) => All.FirstOrDefault(c => c.Id == id);

        /// <summary>Primeira categoria (na ordem de prioridade) a que o arquivo pertence, ou null.</summary>
        public static Category? Classify(string path) => All.FirstOrDefault(c => Matches(c.Id, path));

        private static readonly HashSet<string> AppExts = new(StringComparer.OrdinalIgnoreCase) { "lnk", "url", "exe" };

        /// <summary>O arquivo (ou atalho) pertence a esta categoria?</summary>
        public static bool Matches(string categoryId, string path)
        {
            var cat = Find(categoryId);
            if (cat == null) return false;
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            if (!AppExts.Contains(ext)) return cat.FileExts.Contains(ext);
            if (cat.Exes.Length == 0 && cat.Names.Length == 0 && cat.UrlPrefixes.Length == 0) return false;

            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            string exe = name, url = "", full = "";
            if (ext == "lnk")
            {
                string? target = ShortcutTarget(path);
                if (!string.IsNullOrEmpty(target)) { exe = Path.GetFileNameWithoutExtension(target).ToLowerInvariant(); full = target.ToLowerInvariant(); }
                // Atalho que só chama um endereço (ex.: steam://) também conta como .url.
                if (target != null && target.Contains("://")) url = target.ToLowerInvariant();
            }
            else if (ext == "url") url = ReadUrl(path).ToLowerInvariant();

            if (cat.Exes.Contains(exe) || cat.Exes.Contains(name)) return true;
            if (url.Length > 0 && cat.UrlPrefixes.Any(p => url.StartsWith(p, StringComparison.Ordinal))) return true;
            if (ext == "exe") full = path.ToLowerInvariant();
            return cat.Names.Any(n => name.Contains(n) || exe.Contains(n) || full.Contains(n));
        }

        private static string ReadUrl(string path)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return line.Substring(4).Trim();
            }
            catch { }
            return "";
        }

        /// <summary>Destino de um atalho .lnk (programa ou endereço). Sem destino legível, null.</summary>
        private static string? ShortcutTarget(string path)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                dynamic shell = Activator.CreateInstance(t)!;
                dynamic link = shell.CreateShortcut(path);
                string target = link.TargetPath ?? "";
                return target.Length > 0 ? target : null;
            }
            catch { return null; }
        }
    }
}
