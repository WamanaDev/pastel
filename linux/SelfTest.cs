using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace Pastel
{
    /// <summary>
    /// Autoteste em linha de comando (pastel --selftest). Roda SEMPRE dentro de uma pasta de teste
    /// (PASTEL_SANDBOX), nunca na pasta pessoal de verdade. Serve para provar em um Linux real as partes
    /// que não dependem de tela: atalhos .desktop, aspas no Exec, abrir com gio, socket entre instâncias,
    /// procura de ícones nos temas (PNG e SVG) e geração dos menus.
    /// </summary>
    internal static class SelfTest
    {
        private static int _pass, _fail;

        private static void Check(string name, bool ok, string? detail = null)
        {
            if (ok) _pass++; else _fail++;
            Console.WriteLine($"{(ok ? "PASSOU" : "FALHOU")}  {name}" + (detail != null && !ok ? $"   -> {detail}" : ""));
        }

        private static void WritePng(string path, int size, SKColor color, bool glyph)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var c = new SKCanvas(bmp);
            c.Clear(glyph ? SKColors.Transparent : color);
            using var p = new SKPaint { IsAntialias = true, Color = color };
            if (glyph) c.DrawCircle(size / 2f, size / 2f, size * 0.42f, p);
            using var img = SKImage.FromBitmap(bmp);
            File.WriteAllBytes(path, img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        public static int Run(string[] args)
        {
            string? sandbox = Environment.GetEnvironmentVariable("PASTEL_SANDBOX");
            if (string.IsNullOrEmpty(sandbox))
            {
                Console.WriteLine("Recuso rodar fora de uma pasta de teste. Use: PASTEL_SANDBOX=/tmp/pastel-teste pastel --selftest");
                return 2;
            }
            string outDir = Path.Combine(sandbox, "out");
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"pasta de teste: {sandbox}");
            Console.WriteLine($"sessão: {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "(sem sessão)"}  DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "-"}");
            Console.WriteLine();

            // ---- 1) caminhos ----
            Check("caminhos ficam dentro da pasta de teste",
                Paths.ConfigDir.StartsWith(sandbox) && Paths.DataDir.StartsWith(sandbox) && Paths.DesktopDir.StartsWith(sandbox));
            Directory.CreateDirectory(Paths.DesktopDir);

            // ---- 2) configuração ----
            ConfigStore.Current = new AppConfig { ThemeMode = "dark", AccentMode = "brand" };
            ConfigStore.Current.Floats.Add(new FloatingModel { Name = "Fav", HoverEffect = "proximity", ShowBackground = false });
            ConfigStore.Save();
            ConfigStore.Current = new AppConfig();
            ConfigStore.Load();
            Check("configuração grava e lê de volta (JSON)",
                ConfigStore.Current.ThemeMode == "dark" && ConfigStore.Current.Floats.Count == 1 && ConfigStore.Current.Floats[0].HoverEffect == "proximity");
            ConfigStore.Current = new AppConfig { ThemeMode = "dark", AccentMode = "brand" };

            // ---- 3) tema de ícones falso + procura de ícones (PNG e SVG) ----
            string icons = Path.Combine(Paths.DataHome, "icons", "hicolor");
            WritePng(Path.Combine(icons, "48x48", "apps", "app-um.png"), 48, new SKColor(0xE0, 0x30, 0x40), false);
            WritePng(Path.Combine(icons, "256x256", "apps", "app-um.png"), 256, new SKColor(0xE0, 0x30, 0x40), false);
            WritePng(Path.Combine(icons, "128x128", "apps", "app-glifo.png"), 128, new SKColor(0x20, 0x20, 0x20), true);
            Directory.CreateDirectory(Path.Combine(icons, "scalable", "apps"));
            File.WriteAllText(Path.Combine(icons, "scalable", "apps", "app-svg.svg"),
                "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'><circle cx='32' cy='32' r='28' fill='#3090f0'/><rect x='20' y='20' width='24' height='24' fill='#f0b020'/></svg>");

            string? um = IconResolver.ResolveFile("app-um", 128);
            Check("ícone PNG por nome: escolhe o tamanho certo (>= 128)", um != null && um.Contains("256x256"), um);
            string? sv = IconResolver.ResolveFile("app-svg");
            Check("ícone SVG por nome (scalable)", sv != null && sv.EndsWith(".svg"), sv);
            using (var svgBmp = sv == null ? null : IconResolver.Load(sv, 128))
                Check("SVG é desenhado com Svg.Skia (tem pixels coloridos)", svgBmp != null && svgBmp.Pixels.Any(p => p.Alpha > 200 && p.Blue > 200));
            using (var pngBmp = um == null ? null : IconResolver.Load(um, 128))
                Check("PNG é decodificado", pngBmp != null && pngBmp.Width == 256);

            // ---- 4) apps de mentira na área de trabalho ----
            string marker = Path.Combine(outDir, "marcador.txt");
            string scriptDir = Path.Combine(sandbox, "bin dir com espaço");   // espaço de propósito
            Directory.CreateDirectory(scriptDir);
            string script = Path.Combine(scriptDir, "meu 'app' \"ola\".sh");    // aspas simples e duplas de propósito
            File.WriteAllText(script, "#!/bin/sh\necho \"$@\" > \"" + marker + "\"\n");
            File.SetUnixFileMode(script, (UnixFileMode)0b111_101_101);

            string appDesk(string n, string icon) => Path.Combine(Paths.DesktopDir, n + ".desktop");
            File.WriteAllText(appDesk("Um App", ""), $"[Desktop Entry]\nType=Application\nName=Um App\nName[pt_BR]=Um Aplicativo\nExec=/bin/true\nIcon=app-um\n");
            File.WriteAllText(appDesk("Glifo", ""), "[Desktop Entry]\nType=Application\nName=Glifo\nExec=/bin/true\nIcon=app-glifo\n");
            File.WriteAllText(appDesk("Vetor", ""), "[Desktop Entry]\nType=Application\nName=Vetor\nExec=/bin/true\nIcon=app-svg\n");
            File.WriteAllText(appDesk("Outro", ""), "[Desktop Entry]\nType=Application\nName=Outro\nExec=/bin/true\nIcon=nao-existe\n");

            // ---- 5) criar pasta: atalho .desktop + ícone ----
            var folder = Library.CreateFolder("Jogos & Cia");
            bool launcherOk = folder.LauncherPath != null && File.Exists(folder.LauncherPath);
            Check("criar pasta gera o atalho .desktop na área de trabalho", launcherOk, folder.LauncherPath);
            if (launcherOk)
            {
                var mode = File.GetUnixFileMode(folder.LauncherPath!);
                Check("atalho é executável (o KDE só confia assim)", (mode & UnixFileMode.UserExecute) != 0);
                var entry = DesktopEntry.Read(folder.LauncherPath!);
                Check("atalho guarda o id da pasta e o nome", entry.GetValueOrDefault("X-Pastel-Id") == folder.Id && entry.GetValueOrDefault("Name") == folder.Name);
                string? iconFile = entry.GetValueOrDefault("Icon");
                Check("ícone da pasta existe e é PNG válido", iconFile != null && File.Exists(iconFile) && SKBitmap.Decode(iconFile) is { Width: 256 });
            }

            // ---- 6) soltar atalhos da área de trabalho na pasta: eles SAEM de lá ----
            var drop = new[] { appDesk("Um App", ""), appDesk("Glifo", ""), appDesk("Vetor", ""), appDesk("Outro", "") };
            Library.AddPaths(folder, drop);
            Check("4 apps entraram na pasta", folder.Items.Count == 4);
            Check("os atalhos saíram da área de trabalho", drop.All(d => !File.Exists(d)));
            Check("e foram para a pasta gerenciada", folder.Items.All(i => File.Exists(i.Path) && i.Path.Contains(Paths.ManagedDir(folder.Id))));
            Check("o nome do item usa o idioma do sistema quando existe (ou o Name)", folder.Items.Any(i => i.Name is "Um App" or "Um Aplicativo"));

            // ---- 7) o ícone da pasta com os 4 apps ----
            GroupLauncher.Ensure(folder, force: true);
            string? folderIcon = GroupLauncher.IconPath(folder);
            Check("ícone da pasta é refeito com nome novo (cache do KDE)", folderIcon != null && File.Exists(folderIcon) && folder.IconRev >= 2);
            if (folderIcon != null) File.Copy(folderIcon, Path.Combine(outDir, "pasta.png"), true);
            foreach (var (it, n) in folder.Items.Select((it, n) => (it, n)))
            {
                using var st = IconCache.GetStyled(it, 192);
                if (st != null) { using var im = SKImage.FromBitmap(st); File.WriteAllBytes(Path.Combine(outDir, $"item{n}.png"), im.Encode(SKEncodedImageFormat.Png, 100).ToArray()); }
            }
            Check("ícones dos apps foram achados e padronizados (3 de 4; o 4o não tem ícone)",
                folder.Items.Count(it => IconCache.GetStyled(it, 96) != null) >= 3);

            // ---- 8) devolver um atalho ----
            var first = folder.Items[0];
            string originalPath = first.OriginalPath!;
            Library.RemoveItem(folder, first);
            Check("remover da pasta devolve o atalho à área de trabalho", File.Exists(originalPath));

            // ---- 9) renomear o atalho por fora (Dolphin/Plasma) ----
            string renamed = Path.Combine(Paths.DesktopDir, "Jogos Renomeados.desktop");
            File.Move(folder.LauncherPath!, renamed);
            Library.ReconcileAll();
            Check("pasta acompanha o atalho renomeado (acha pelo id)", folder.LauncherPath == renamed, folder.LauncherPath);

            // ---- 10) abrir um .desktop com gio (aspas e espaços no Exec) ----
            var tricky = new FolderModel { Name = "Aspas" };
            string trickyDesk = Path.Combine(outDir, "aspas.desktop");
            // o Exec do atalho usa o script com espaço, aspas simples e aspas duplas no caminho
            DesktopEntry.WriteExecutable(trickyDesk, DesktopEntry.BuildFolderLauncher(tricky, script, "x"));
            bool launched = AppLauncher.TryOpen(trickyDesk);
            bool markerOk = false;
            for (int i = 0; i < 40 && !markerOk; i++) { Thread.Sleep(100); markerOk = File.Exists(marker); }
            string markerText = markerOk ? File.ReadAllText(marker).Trim() : "";
            Check("gio launch abre o atalho e o Exec com aspas/espaços chega certo",
                launched && markerOk && markerText.StartsWith("--folder " + tricky.Id), $"abriu={launched} marcador={markerOk} texto='{markerText}'");

            // ---- 11) socket entre instâncias ----
            string[]? got = null;
            using var gate = new ManualResetEventSlim();
            Ipc.Listen((a, x, y) => { got = a; gate.Set(); });
            Thread.Sleep(300);
            bool sent = Ipc.TrySend(new[] { "--folder", "abc", "/tmp/arquivo com espaço.txt" });
            gate.Wait(3000);
            Check("segunda instância entrega os argumentos pelo socket",
                sent && got != null && got.Length == 3 && got[2] == "/tmp/arquivo com espaço.txt");

            // ---- 12) menus ----
            string exe = Paths.ExePath ?? "/usr/bin/pastel";
            var floatModel = new FloatingModel { Name = "Barra & Cia" };
            ShellMenus.RegisterServiceMenu(new[] { folder }, new[] { floatModel }, exe, "x");
            string sm = Path.Combine(Paths.ServiceMenusDir, "pastel-enviar.desktop");
            string smText = File.Exists(sm) ? File.ReadAllText(sm) : "";
            Check("menu de contexto (Dolphin) foi gerado com pasta e flutuante",
                smText.Contains("X-KDE-Submenu=Pastel") && smText.Contains("--send-folder " + folder.Id) &&
                smText.Contains("--send-float " + floatModel.Id) && smText.Contains("--send-folder-new") && smText.Contains("%F"));
            ShellMenus.RegisterApplication(exe, "x");
            string app = Path.Combine(Paths.ApplicationsDir, "pastel.desktop");
            Check("entrada do menu de aplicativos tem as 3 ações",
                File.Exists(app) && File.ReadAllText(app).Contains("[Desktop Action NewFolder]") && File.ReadAllText(app).Contains("[Desktop Action Settings]"));
            ShellMenus.SetAutostart(true, exe, "x");
            Check("iniciar com a sessão cria o autostart", ShellMenus.IsAutostart());
            ShellMenus.SetAutostart(false, exe, "x");
            Check("e desligar apaga", !ShellMenus.IsAutostart());

            // ---- 13) apagar o atalho por fora: a pasta some e os apps voltam ----
            var kept = folder.Items.ToList();
            File.Delete(renamed);
            Library.ReconcileAll();
            Check("apagar o atalho por fora remove a pasta", !ConfigStore.Current.Folders.Contains(folder));
            Check("e devolve os apps à área de trabalho", kept.All(k => k.OriginalPath != null && File.Exists(k.OriginalPath)));

            // ---- 14) tema ----
            Check("tema escuro forçado nas configurações vale", !PastelTheme.IsLight);
            ConfigStore.Current.ThemeMode = "light";
            Check("e o claro também", PastelTheme.IsLight);

            // ---- 15) X11 (só se houver tela) ----
            if (X11.Available)
            {
                Check("X11: lê a posição do mouse", X11.TryGetPointer(out _, out _));
            }
            else Console.WriteLine("(sem DISPLAY: testes de X11 pulados)");

            Console.WriteLine();
            Console.WriteLine($"{_pass} passaram, {_fail} falharam");
            return _fail == 0 ? 0 : 1;
        }
    }
}
