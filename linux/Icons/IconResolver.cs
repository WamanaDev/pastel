using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiaSharp;
using Svg.Skia;

namespace Pastel
{
    /// <summary>
    /// Acha o ícone de um app como o Linux faz: o atalho .desktop diz só o NOME do ícone ("firefox"), e ele
    /// está nos temas de ícones do sistema (o do KDE, os herdados dele e o hicolor), em PNG ou SVG.
    /// </summary>
    internal static class IconResolver
    {
        private sealed record Entry(string Path, int Size, bool Svg);

        private static Dictionary<string, List<Entry>>? _index;
        private static readonly object Gate = new();
        private static readonly Regex SizeDir = new(@"[/\\](\d+)x\1[/\\]", RegexOptions.Compiled);
        private static readonly Regex NumDir = new(@"[/\\](\d+)[/\\]", RegexOptions.Compiled);

        // ---------- onde procurar ----------

        private static IEnumerable<string> DataDirs()
        {
            yield return Paths.DataHome;
            var env = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            var list = string.IsNullOrEmpty(env) ? "/usr/local/share:/usr/share" : env;
            foreach (var d in list.Split(':', StringSplitOptions.RemoveEmptyEntries)) yield return d;
            yield return "/var/lib/flatpak/exports/share";
            yield return Path.Combine(Paths.DataHome, "flatpak", "exports", "share");
        }

        private static string CurrentTheme()
        {
            try
            {
                if (File.Exists(Paths.KdeGlobals))
                {
                    bool inIcons = false;
                    foreach (var raw in File.ReadLines(Paths.KdeGlobals))
                    {
                        var l = raw.Trim();
                        if (l.StartsWith('[')) { inIcons = l == "[Icons]"; continue; }
                        if (inIcons && l.StartsWith("Theme=")) return l[6..].Trim();
                    }
                }
            }
            catch { }
            return "breeze";
        }

        /// <summary>O tema e os que ele herda (index.theme), terminando no hicolor.</summary>
        private static List<string> ThemeChain(IReadOnlyList<string> iconRoots)
        {
            var chain = new List<string>();
            void Add(string t, int depth)
            {
                if (depth > 6 || string.IsNullOrWhiteSpace(t) || chain.Contains(t, StringComparer.OrdinalIgnoreCase)) return;
                chain.Add(t);
                foreach (var root in iconRoots)
                {
                    var index = Path.Combine(root, t, "index.theme");
                    if (!File.Exists(index)) continue;
                    try
                    {
                        foreach (var l in File.ReadLines(index))
                            if (l.StartsWith("Inherits=", StringComparison.Ordinal))
                            {
                                foreach (var parent in l[9..].Split(',', StringSplitOptions.RemoveEmptyEntries)) Add(parent.Trim(), depth + 1);
                                break;
                            }
                    }
                    catch { }
                    break;
                }
            }
            Add(CurrentTheme(), 0);
            Add("breeze", 0);
            Add("hicolor", 0);
            return chain;
        }

        private static void BuildIndex()
        {
            var idx = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            var iconRoots = DataDirs().Select(d => Path.Combine(d, "icons")).Where(Directory.Exists).Distinct().ToList();

            void Scan(string dir)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        bool svg = f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                        if (!svg && !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                        // só as categorias que importam para apps, pastas e tipos de arquivo
                        if (!(f.Contains("/apps/") || f.Contains("/places/") || f.Contains("/mimetypes/") ||
                              f.Contains("\\apps\\") || f.Contains("\\places\\") || f.Contains("\\mimetypes\\"))) continue;

                        int size = 48;
                        var m = SizeDir.Match(f);
                        if (m.Success) size = int.Parse(m.Groups[1].Value);
                        else if (f.Contains("scalable", StringComparison.OrdinalIgnoreCase)) size = 512;
                        else if ((m = NumDir.Match(f)).Success) size = int.Parse(m.Groups[1].Value);

                        string key = Path.GetFileNameWithoutExtension(f);
                        if (!idx.TryGetValue(key, out var list)) idx[key] = list = new();
                        list.Add(new Entry(f, size, svg));
                    }
                }
                catch { }
            }

            // Temas na ordem de preferência: o primeiro que tiver o ícone vence (por isso a ordem da lista é mantida).
            foreach (var theme in ThemeChain(iconRoots))
                foreach (var root in iconRoots)
                    Scan(Path.Combine(root, theme));

            // ícones soltos (/usr/share/pixmaps)
            foreach (var d in DataDirs())
            {
                var pix = Path.Combine(d, "pixmaps");
                if (!Directory.Exists(pix)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(pix))
                    {
                        bool svg = f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                        if (!svg && !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                        string key = Path.GetFileNameWithoutExtension(f);
                        if (!idx.TryGetValue(key, out var list)) idx[key] = list = new();
                        list.Add(new Entry(f, 64, svg));
                    }
                }
                catch { }
            }
            _index = idx;
        }

        // ---------- resolver ----------

        /// <summary>Caminho do arquivo de ícone para um nome (ou caminho) de ícone; null se não achar.</summary>
        public static string? ResolveFile(string icon, int wanted = 128)
        {
            if (string.IsNullOrWhiteSpace(icon)) return null;
            if (Path.IsPathRooted(icon)) return File.Exists(icon) ? icon : null;

            lock (Gate) { if (_index == null) BuildIndex(); }
            if (!_index!.TryGetValue(Path.GetFileNameWithoutExtension(icon), out var list) || list.Count == 0) return null;

            // SVG renderiza nítido em qualquer tamanho; senão o menor PNG que seja >= ao pedido; senão o maior
            var svg = list.FirstOrDefault(e => e.Svg);
            if (svg != null) return svg.Path;
            var big = list.Where(e => e.Size >= wanted).OrderBy(e => e.Size).FirstOrDefault();
            return (big ?? list.OrderByDescending(e => e.Size).First()).Path;
        }

        /// <summary>Outras versões do mesmo ícone (mesmo nome em outros tamanhos/temas): SVG primeiro, depois PNG do maior para o menor.
        /// Serve para trocar um PNG minúsculo (ou com margem enorme) por uma versão maior.</summary>
        public static List<string> Alternatives(string file)
        {
            var result = new List<string>();
            try
            {
                lock (Gate) { if (_index == null) BuildIndex(); }
                if (_index!.TryGetValue(Path.GetFileNameWithoutExtension(file), out var list))
                    result.AddRange(list.Where(e => !string.Equals(e.Path, file, StringComparison.Ordinal))
                        .OrderByDescending(e => e.Svg).ThenByDescending(e => e.Size).Select(e => e.Path));
            }
            catch { }
            return result;
        }

        /// <summary>O ícone de um item da pasta: do .desktop, do próprio arquivo de imagem, ou um genérico.</summary>
        public static string? ResolveForItem(ShortcutItem item)
        {
            string p = item.Path;
            try
            {
                if (Directory.Exists(p)) return ResolveFile("folder");

                if (p.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                {
                    var e = DesktopEntry.Read(p);
                    if (e.TryGetValue("Icon", out var icon) && ResolveFile(icon) is string found) return found;
                    return ResolveFile("application-x-executable");
                }

                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext is ".png" or ".svg" or ".jpg" or ".jpeg" or ".webp") return p;
                if (ext is ".appimage" or ".sh" or ".bin" or "") return ResolveFile("application-x-executable");
                return ResolveFile("text-x-generic") ?? ResolveFile("application-x-executable");
            }
            catch { return null; }
        }

        // ---------- carregar como imagem ----------

        /// <summary>Carrega o arquivo no tamanho pedido (SVG é desenhado nesse tamanho; PNG vem como é).</summary>
        public static SKBitmap? Load(string file, int size)
        {
            try
            {
                if (file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    using var svg = new SKSvg();
                    var pic = svg.Load(file);
                    if (pic == null) return null;
                    var cull = pic.CullRect;
                    if (cull.Width <= 0 || cull.Height <= 0) return null;
                    float scale = size / Math.Max(cull.Width, cull.Height);
                    var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
                    using var canvas = new SKCanvas(bmp);
                    canvas.Clear(SKColors.Transparent);
                    canvas.Translate((size - cull.Width * scale) / 2f, (size - cull.Height * scale) / 2f);
                    canvas.Scale(scale);
                    canvas.DrawPicture(pic);
                    return bmp;
                }
                return SKBitmap.Decode(file);
            }
            catch { return null; }
        }
    }
}
