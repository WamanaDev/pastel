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

            // ---- 3b) ícones pequenos/com margem preenchem o tile; ícones cheios ficam como estão ----
            {
                static SKBitmap Make(int n, SKColor? bg, SKColor fg, double frac, bool circle)
                {
                    var bm = new SKBitmap(n, n, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    using var cv = new SKCanvas(bm);
                    cv.Clear(bg ?? SKColors.Transparent);
                    float g = (float)(n * frac), o = (n - g) / 2f;
                    using var pt = new SKPaint { Color = fg, IsAntialias = true };
                    if (circle) cv.DrawOval(new SKRect(o, o, o + g, o + g), pt); else cv.DrawRect(new SKRect(o, o, o + g, o + g), pt);
                    return bm;
                }
                static double Span(SKBitmap b, Func<SKColor, bool> is_)
                {
                    int min = b.Width, max = -1;
                    for (int y = 0; y < b.Height; y++)
                        for (int x = 0; x < b.Width; x++)
                            if (is_(b.GetPixel(x, y))) { if (x < min) min = x; if (x > max) max = x; }
                    return max < min ? 0 : (max - min + 1) / (double)b.Width;
                }
                bool Reddish(SKColor c) => c.Alpha > 200 && c.Red > 200 && c.Green < 80 && c.Blue < 80;
                bool Whitish(SKColor c) => c.Alpha > 200 && c.Red > 235 && c.Green > 235 && c.Blue > 235;

                // glifo vermelho de 48px com margem, fundo transparente: deve ocupar ~66% do tile
                using (var small = Make(48, null, new SKColor(0xE0, 0x10, 0x10), 0.5, false))
                using (var st = IconStyler.Style(small, 192, false))
                {
                    double span = Span(st, Reddish);
                    Check("ícone pequeno com margem (transparente) enche o tile (~66%)", span is > 0.58 and < 0.74, span.ToString("0.00"));
                    Check("ContentExtent mede só o miolo sólido (24px < 160)", IconStyler.ContentExtent(small) is >= 22 and <= 26);
                }
                // fundo opaco azul uniforme com glifo branco pequeno: tile na cor do fundo + glifo ampliado
                using (var bgSmall = Make(256, new SKColor(0x20, 0x50, 0xB0), SKColors.White, 0.3, true))
                using (var st = IconStyler.Style(bgSmall, 192, false))
                {
                    double span = Span(st, Whitish);
                    var mid = st.GetPixel(96, 8);
                    Check("fundo opaco com glifo pequeno: glifo ampliado (~66%)", span is > 0.58 and < 0.74, span.ToString("0.00"));
                    Check("e o tile ganha a cor do fundo", mid.Alpha == 255 && Math.Abs(mid.Blue - 0xB0) < 12 && Math.Abs(mid.Red - 0x20) < 12);
                }
                // ícone cheio (fundo opaco + glifo grande): continua cheio, sem ampliar o glifo
                using (var full = Make(256, new SKColor(0x20, 0x90, 0x40), SKColors.White, 0.8, false))
                using (var st = IconStyler.Style(full, 192, false))
                {
                    double span = Span(st, Whitish);
                    var mid = st.GetPixel(96, 8);
                    Check("ícone cheio continua cheio (glifo >= 80% do tile, mesma escala)", span is > 0.80 and < 0.92 && mid.Alpha == 255 && mid.Green > 0x70, span.ToString("0.00"));
                }
                // ícone transparente que já preenche a área: continua ocupando ~66%, centralizado
                using (var big = Make(256, null, new SKColor(0xE0, 0x10, 0x10), 0.95, true))
                using (var st = IconStyler.Style(big, 192, false))
                {
                    double span = Span(st, Reddish);
                    Check("ícone transparente grande vira ~66% do tile (como antes)", span is > 0.58 and < 0.74, span.ToString("0.00"));
                }
                // procura de versão maior: o PNG de 48px tem alternativa maior no tema
                string? um48 = IconResolver.ResolveFile("app-um", 16);
                var alts = um48 == null ? new List<string>() : IconResolver.Alternatives(um48);
                Check("ícones alternativos de maior tamanho são oferecidos (PNG 48 -> 256)", um48 != null && um48.Contains("48x48") && alts.Any(a => a.Contains("256x256")), um48);
            }

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

            // ---- 8b) só atalhos .desktop são movidos; o resto ganha um atalho criado ----
            {
                var f2 = Library.CreateFolder("Teste Atalhos");
                string lnkDesk = Path.Combine(Paths.DesktopDir, "Atalho Movel.desktop");
                File.WriteAllText(lnkDesk, "[Desktop Entry]\nType=Application\nName=Atalho Movel\nExec=/bin/true\n");
                string exeFile = Path.Combine(Paths.DesktopDir, "programa 'x' \"y\" 50%.sh");   // espaço, aspas e % de propósito
                File.WriteAllText(exeFile, "#!/bin/sh\necho ok\n");
                File.SetUnixFileMode(exeFile, (UnixFileMode)0b111_101_101);
                string txtFile = Path.Combine(Paths.DesktopDir, "nota com espaço.txt");
                File.WriteAllText(txtFile, "oi");
                string dirPath = Path.Combine(Paths.DesktopDir, "Minha Pasta");
                Directory.CreateDirectory(dirPath);

                Library.AddPaths(f2, new[] { lnkDesk, exeFile, txtFile, dirPath });
                Check("4 itens entraram (atalho, programa, arquivo, diretório)", f2.Items.Count == 4);
                Check("o atalho .desktop foi movido da área de trabalho", !File.Exists(lnkDesk) &&
                      f2.Items.Any(i => i.OriginalPath == lnkDesk && File.Exists(i.Path)));
                Check("programa, arquivo e diretório NÃO foram movidos", File.Exists(exeFile) && File.Exists(txtFile) && Directory.Exists(dirPath));
                var viaExe = f2.Items.FirstOrDefault(i => i.SourcePath == exeFile);
                var viaTxt = f2.Items.FirstOrDefault(i => i.SourcePath == txtFile);
                var viaDir = f2.Items.FirstOrDefault(i => i.SourcePath == dirPath);
                Check("cada um ganhou um .desktop na pasta gerenciada",
                    viaExe != null && viaTxt != null && viaDir != null &&
                    new[] { viaExe!, viaTxt!, viaDir! }.All(i => i.Path.StartsWith(Paths.ManagedDir(f2.Id)) && i.Path.EndsWith(".desktop") && File.Exists(i.Path) && i.OriginalPath == null));
                if (viaExe != null && viaTxt != null && viaDir != null)
                {
                    var ee = DesktopEntry.Read(viaExe.Path);
                    Check("Exec do programa aponta para ele (aspas e % escapados)",
                        ee.GetValueOrDefault("Exec") == DesktopEntry.QuoteExecArg(exeFile.Replace("%", "%%")).Replace("\\\\", "\\") /* Read já desfaz um nível de barra */ && ee.GetValueOrDefault("Type") == "Application" && ee.GetValueOrDefault("Terminal") == "false");
                    Check("Exec do arquivo comum e do diretório usa xdg-open",
                        (DesktopEntry.Read(viaTxt.Path).GetValueOrDefault("Exec") ?? "").StartsWith("xdg-open \"") &&
                        (DesktopEntry.Read(viaDir.Path).GetValueOrDefault("Exec") ?? "").StartsWith("xdg-open \""));
                    Check("o atalho criado é executável (confiável)", (File.GetUnixFileMode(viaTxt.Path) & UnixFileMode.UserExecute) != 0);
                    Check("Name do atalho é o nome do arquivo", ee.GetValueOrDefault("Name") == "programa 'x' \"y\" 50%" &&
                          DesktopEntry.Read(viaDir.Path).GetValueOrDefault("Name") == "Minha Pasta");
                }
                int before = f2.Items.Count, linksBefore = Directory.GetFiles(Paths.ManagedDir(f2.Id)).Length;
                Library.AddPaths(f2, new[] { exeFile, txtFile, dirPath, Path.Combine(Paths.ManagedDir(f2.Id), Path.GetFileName(lnkDesk)) });
                Check("enviar o mesmo de novo não duplica", f2.Items.Count == before && Directory.GetFiles(Paths.ManagedDir(f2.Id)).Length == linksBefore);
                // devolve o atalho movido
                var movedItem = f2.Items.First(i => i.OriginalPath == lnkDesk);
                Library.RemoveItem(f2, movedItem);
                Check("remover o atalho movido devolve ele à área de trabalho", File.Exists(lnkDesk));
                // remove os criados: só o .desktop some
                var linkPaths = new[] { viaExe!, viaTxt!, viaDir! }.Select(i => i.Path).ToList();
                foreach (var it in new[] { viaExe!, viaTxt!, viaDir! }) Library.RemoveItem(f2, it);
                Check("remover apaga só o atalho criado", linkPaths.All(l => !File.Exists(l)));
                Check("e os originais continuam intactos", File.Exists(exeFile) && File.Exists(txtFile) && Directory.Exists(dirPath) && File.Exists(lnkDesk));
                Library.DeleteFolder(f2);
                foreach (var x in new[] { lnkDesk, exeFile, txtFile }) File.Delete(x);
                Directory.Delete(dirPath);
            }

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

            // ---- 13b) ocultar pastas vazias ----
            {
                var cfg = ConfigStore.Current;
                cfg.HideEmptyFolders = true;
                string stashDir = Path.Combine(Paths.DataDir, "Ocultas");

                var fresh = Library.CreateFolder("Nova Vazia");
                string freshPath = fresh.LauncherPath!;
                Library.ReconcileAll();
                Check("ocultar: pasta nova e vazia fica na área de trabalho", fresh.Fresh && File.Exists(freshPath) && !GroupLauncher.IsStashed(fresh));

                var oldF = Library.CreateFolder("Antiga Vazia");
                string oldPath = oldF.LauncherPath!;
                oldF.Fresh = false; // como uma pasta de config antiga (sem o campo)
                Library.ReconcileAll();
                Check("ocultar: pasta antiga e vazia vai para Ocultas (movida, não some do disco)",
                    !File.Exists(oldPath) && GroupLauncher.IsStashed(oldF) && GroupLauncher.StashPath(oldF).StartsWith(stashDir));
                Check("ocultar: a reconciliação NÃO trata a pasta guardada como apagada", cfg.Folders.Contains(oldF));
                Library.ReconcileAll(); // o watcher dispara com o nosso próprio move: segunda passada não muda nada
                Check("ocultar: reconciliar de novo é estável (sem remover nem criar de volta)",
                    cfg.Folders.Contains(oldF) && !File.Exists(oldPath) && GroupLauncher.IsStashed(oldF));
                Check("ocultar: a nova continua visível", File.Exists(freshPath));

                string extra = appDesk("Extra", "");
                File.WriteAllText(extra, "[Desktop Entry]\nType=Application\nName=Extra\nExec=/bin/true\n");
                Library.AddPaths(oldF, new[] { extra });
                Check("ocultar: recebeu item, volta ao mesmo caminho", File.Exists(oldPath) && !GroupLauncher.IsStashed(oldF) && !oldF.Fresh);

                Library.RemoveItem(oldF, oldF.Items[0]);
                Check("ocultar: ficou vazia de novo, some outra vez", !File.Exists(oldPath) && GroupLauncher.IsStashed(oldF));

                GroupLauncher.EditingId = oldF.Id; // balão aberto
                GroupLauncher.SyncHidden(oldF);
                GroupLauncher.Ensure(oldF, force: true);
                Library.ReconcileAll();
                Check("ocultar: balão aberto, a pasta aparece e não some", File.Exists(oldPath) && !GroupLauncher.IsStashed(oldF));
                GroupLauncher.EditingId = null;
                GroupLauncher.SyncHidden(oldF);
                Check("ocultar: ao fechar o balão (vazia) sincroniza e some", !File.Exists(oldPath) && GroupLauncher.IsStashed(oldF));

                Library.RenameFolder(oldF, "Antiga Renomeada");
                string renamedStash = Path.Combine(Paths.DesktopDir, "Antiga Renomeada.desktop");
                Check("ocultar: renomear guardada só muda o nome de origem", GroupLauncher.IsStashed(oldF) && !File.Exists(renamedStash) && oldF.LauncherPath == renamedStash && cfg.Folders.Contains(oldF));

                cfg.HideEmptyFolders = false;
                foreach (var f in cfg.Folders) GroupLauncher.SyncHidden(f);
                Check("ocultar: opção desligada devolve a pasta (com o nome novo)", File.Exists(renamedStash) && !GroupLauncher.IsStashed(oldF));

                cfg.HideEmptyFolders = true;
                GroupLauncher.SyncHidden(oldF);
                string stashed = GroupLauncher.StashPath(oldF);
                bool wasStashed = File.Exists(stashed);
                Library.DeleteFolder(oldF);
                Check("ocultar: excluir pasta guardada apaga também o guardado", wasStashed && !File.Exists(stashed) && !File.Exists(renamedStash) && !cfg.Folders.Contains(oldF));

                Library.DeleteFolder(fresh);
                cfg.HideEmptyFolders = false;
            }

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
