using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Pastel;
using SkiaSharp;

// Teste de interface: tudo em memória e dentro de uma pasta de teste (PASTEL_SANDBOX). Não grava nada do usuário.
internal static class Program
{
    private static string _sandbox = "";
    private static string _out = "";

    private static int Main(string[] args)
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "pastel-harness");
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("PASTEL_SANDBOX", _sandbox);
        _out = args.Length > 0 ? args[0] : Path.Combine(_sandbox, "out");
        Directory.CreateDirectory(_out);

        AppBuilder.Configure<PastelApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        ConfigStore.Current = new AppConfig { Animations = false, AccentMode = "brand" };
        var items = MakeItems(14);

        foreach (var dark in new[] { true, false })
        {
            ConfigStore.Current.ThemeMode = dark ? "dark" : "light";
            IconCache.Clear();
            string t = dark ? "escuro" : "claro";
            Dispatcher.UIThread.Invoke(() =>
            {
                Popup(items, 0, $"balao-{t}-p1");
                Popup(items, 1, $"balao-{t}-p2");
                Docks(items, $"flutuantes-{t}");
                Settings(items, "Pastas", $"config-pastas-{t}");
                if (dark) { Settings(items, "Flutuantes", "config-flutuantes-escuro"); Settings(items, "Aparência", "config-aparencia-escuro"); Settings(items, "Geral", "config-geral-escuro"); }
            });
        }
        Console.WriteLine("imagens em " + _out);
        return 0;
    }

    // ---------- apps de mentira com ícones variados ----------

    private static List<ShortcutItem> MakeItems(int n)
    {
        string dir = Path.Combine(_sandbox, "apps");
        Directory.CreateDirectory(dir);
        var list = new List<ShortcutItem>();
        for (int i = 0; i < n; i++)
        {
            string icon = Path.Combine(dir, $"icone{i}.png");
            using (var bmp = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul)))
            using (var c = new SKCanvas(bmp))
            {
                var hue = new SKColor((byte)(60 + i * 13 % 160), (byte)(90 + i * 29 % 140), (byte)(120 + i * 37 % 120));
                using var p = new SKPaint { IsAntialias = true, Color = hue };
                c.Clear(SKColors.Transparent);
                switch (i % 4)
                {
                    case 0: // quadrado cheio com borda cinza (o caso "esquisito")
                        c.DrawRect(3, 3, 250, 250, new SKPaint { Color = SKColors.White });
                        c.DrawRect(3, 3, 250, 250, new SKPaint { Color = new SKColor(0xC8, 0xC8, 0xC8), IsStroke = true, StrokeWidth = 6 });
                        c.DrawCircle(128, 128, 60, p); break;
                    case 1: c.DrawCircle(128, 128, 100, p); c.DrawRect(98, 98, 60, 60, new SKPaint { Color = SKColors.White }); break; // desenho colorido
                    case 2: c.DrawCircle(128, 128, 96, new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), IsAntialias = true }); break;   // desenho escuro
                    default: c.Clear(hue); c.DrawCircle(128, 128, 64, new SKPaint { Color = SKColors.White, IsAntialias = true }); break;   // quadrado colorido
                }
                using var img = SKImage.FromBitmap(bmp);
                File.WriteAllBytes(icon, img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
            }
            string name = new[] { "Navegador", "Calculadora", "Terminal", "Editor", "Músicas", "Fotos", "Vídeos", "Jogos", "Mensagens", "Email", "Notas", "Mapas", "Câmera", "Relógio" }[i % 14];
            string desk = Path.Combine(dir, name + ".desktop");
            File.WriteAllText(desk, $"[Desktop Entry]\nType=Application\nName={name}\nExec=/bin/true\nIcon={icon}\n");
            list.Add(new ShortcutItem { Name = name, Path = desk });
        }
        return list;
    }

    // ---------- captura ----------

    private static void Save(Window w, string name, bool darkBackdrop = true, bool fit = true)
    {
        Dispatcher.UIThread.RunJobs();
        if (fit && w.Content is Control root)
        {
            // a plataforma de teste não encolhe a janela sozinha: ajusta ao conteúdo (+ margem da sombra)
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            w.Width = root.DesiredSize.Width + root.Margin.Left + root.Margin.Right;
            w.Height = root.DesiredSize.Height + root.Margin.Top + root.Margin.Bottom;
            Dispatcher.UIThread.RunJobs();
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        string raw = Path.Combine(_out, name + "-raw.png");
        if (fit)
        {
            // desenha a janela em uma imagem com transparência (a captura de tela do teste perde o alfa)
            var size = new PixelSize((int)Math.Ceiling(w.Width), (int)Math.Ceiling(w.Height));
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(w);
            rtb.Save(raw);
        }
        else
        {
            var frame = w.CaptureRenderedFrame();
            if (frame == null) { Console.WriteLine("sem imagem: " + name); return; }
            frame.Save(raw);
        }

        // compõe sobre um fundo colorido, para enxergar a transparência e a sombra
        using var src = SKBitmap.Decode(raw);
        int pad = 30;
        using var bmp = new SKBitmap(src.Width + pad * 2, src.Height + pad * 2);
        using var c = new SKCanvas(bmp);
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(bmp.Width, bmp.Height),
            new[] { darkBackdrop ? new SKColor(0x14, 0x3A, 0x6B) : new SKColor(0xD8, 0xE6, 0xF7), darkBackdrop ? new SKColor(0x8A, 0x3A, 0x1E) : new SKColor(0xF7, 0xD6, 0xC0) },
            SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            c.DrawRect(0, 0, bmp.Width, bmp.Height, paint);
        c.DrawBitmap(src, pad, pad);
        using var img = SKImage.FromBitmap(bmp);
        File.WriteAllBytes(Path.Combine(_out, name + ".png"), img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        File.Delete(raw);
        Console.WriteLine("ok  " + name);
    }

    private static void Popup(List<ShortcutItem> items, int page, string name)
    {
        var f = new FolderModel { Name = "Desenvolvimento" };
        f.Items.AddRange(items);
        var w = new PopupWindow(f, new PixelRect(400, 60, 76, 70));
        w.Show();
        if (page > 0) typeof(PopupWindow).GetMethod("GoToPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(w, new object[] { page });
        w.Opacity = 1;
        Save(w, name, !ConfigStore.Current.ThemeMode.Equals("light"));
        w.Close();
    }

    private static void Docks(List<ShortcutItem> items, string name)
    {
        // Cada barra num arquivo; a "aproximação" é simulada com a posição do mouse no 3º ícone.
        var specs = new (string label, Action<FloatingModel> cfg, int hover)[]
        {
            ("padrão", m => { }, -1),
            ("destaque", m => m.HoverEffect = "focus", 2),
            ("aproximação", m => m.HoverEffect = "proximity", 2),
            ("sem fundo, sem nome", m => { m.ShowBackground = false; m.ShowName = false; m.HoverEffect = "proximity"; }, 3),
            ("mínimo, sem fundo", m => { m.IconSize = 24; m.ShowBackground = false; }, -1),
        };
        int k = 0;
        foreach (var (label, cfg, hover) in specs)
        {
            var m = new FloatingModel { Name = "Favoritos", Col = 2, Row = 1 + k };
            m.Items.AddRange(items.Take(6));
            cfg(m);
            var w = new FloatingWindow(m);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            if (hover >= 0 && m.HoverEffect == "proximity")
            {
                var tiles = (System.Collections.IList)typeof(FloatingWindow).GetField("_tiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
                var tile = (Border)tiles[hover]!.GetType().GetField("Item1")!.GetValue(tiles[hover])!;
                var itemsPanel = (StackPanel)typeof(FloatingWindow).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
                var c = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), itemsPanel) ?? default;
                w.ApplyProximity(c);
            }
            Save(w, $"{name}-{k}-{label.Replace(", ", "-").Replace(" ", "-")}", !ConfigStore.Current.ThemeMode.Equals("light"));
            w.Close();
            k++;
        }
    }

    private static void Settings(List<ShortcutItem> items, string page, string name)
    {
        var cfg = ConfigStore.Current;
        cfg.Folders.Clear(); cfg.Floats.Clear();
        var f1 = new FolderModel { Name = "Jogos" }; f1.Items.AddRange(items.Take(5));
        var f2 = new FolderModel { Name = "Desenvolvimento" }; f2.Items.AddRange(items.Skip(5).Take(3));
        cfg.Folders.Add(f1); cfg.Folders.Add(f2);
        var fl = new FloatingModel { Name = "Favoritos" }; fl.Items.AddRange(items.Take(4)); cfg.Floats.Add(fl);

        var w = new SettingsWindow(page);
        w.Show();
        Save(w, name, !ConfigStore.Current.ThemeMode.Equals("light"), fit: false);
        w.Close();
    }
}
