using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SkiaSharp;

namespace Pastel
{
    /// <summary>
    /// Teste com tela (pastel --selftest-gui): sobe o Avalonia de verdade em um X11, abre o balão e uma barra
    /// flutuante e lê de volta, pelo X11, onde as janelas ficaram e o que o gerenciador de janelas aplicou.
    /// Sempre dentro de PASTEL_SANDBOX.
    /// </summary>
    internal static class GuiSelfTest
    {
        public static bool Active;
        private static int _pass, _fail;

        private static void Check(string name, bool ok, string? detail = null)
        {
            if (ok) _pass++; else _fail++;
            Console.WriteLine($"{(ok ? "PASSOU" : "FALHOU")}  {name}" + (detail != null ? $"   [{detail}]" : ""));
        }

        public static int Run()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PASTEL_SANDBOX")))
            {
                Console.WriteLine("Recuso rodar fora de uma pasta de teste (PASTEL_SANDBOX).");
                return 2;
            }
            if (!X11.Available) { Console.WriteLine("Sem DISPLAY: não há X11 para testar."); return 3; }
            Active = true;
            return UiHost.Run();
        }

        /// <summary>Chamado pelo PastelApp no lugar da inicialização normal.</summary>
        public static void Start(IClassicDesktopStyleApplicationLifetime lifetime)
        {
            ConfigStore.Current = new AppConfig { Animations = false, ThemeMode = "dark", AccentMode = "brand" };
            DispatcherTimer.RunOnce(() => Execute(lifetime), TimeSpan.FromMilliseconds(400));
        }

        private static void Execute(IClassicDesktopStyleApplicationLifetime lifetime)
        {
            try
            {
                string sandbox = Environment.GetEnvironmentVariable("PASTEL_SANDBOX")!;
                string appsDir = Path.Combine(sandbox, "apps");
                Directory.CreateDirectory(appsDir);
                var items = new List<ShortcutItem>();
                for (int i = 0; i < 5; i++)
                {
                    string icon = Path.Combine(appsDir, $"i{i}.png");
                    using (var bmp = new SKBitmap(new SKImageInfo(256, 256)))
                    using (var c = new SKCanvas(bmp))
                    {
                        c.Clear(SKColors.Transparent);
                        c.DrawCircle(128, 128, 100, new SKPaint { Color = new SKColor((byte)(40 + i * 40), 120, (byte)(220 - i * 30)), IsAntialias = true });
                        File.WriteAllBytes(icon, SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 100).ToArray());
                    }
                    string desk = Path.Combine(appsDir, $"App {i}.desktop");
                    File.WriteAllText(desk, $"[Desktop Entry]\nType=Application\nName=App {i}\nExec=/bin/true\nIcon={icon}\n");
                    items.Add(new ShortcutItem { Name = $"App {i}", Path = desk });
                }

                var folder = new FolderModel { Name = "Pasta GUI" };
                folder.Items.AddRange(items);
                // O pedido de estado inicial ("no fundo", "fora da barra de tarefas") precisa ficar gravado na janela ANTES de abrir.
                {
                    var probe = new FloatingWindow(new FloatingModel { Name = "Sonda", Col = 0, Row = 0 });
                    var handle = probe.TryGetPlatformHandle();
                    bool set = handle != null && X11.SetInitialDesktopState(handle.Handle);
                    var state = handle == null ? Array.Empty<string>() : X11Inspect.StateOf(handle.Handle);
                    Check("estado inicial 'no fundo' + 'fora da barra' é gravado na janela antes de abrir",
                        set && state.Contains("_NET_WM_STATE_BELOW") && state.Contains("_NET_WM_STATE_SKIP_TASKBAR"), "[" + string.Join(",", state) + "]");
                    probe.Close();
                }

                // a barra abre primeiro: o balão fecha sozinho se perder o foco (como deve ser)
                var dock = new FloatingModel { Name = "Dock GUI", Col = 3, Row = 4 };
                dock.Items.AddRange(items);
                var dockWin = new FloatingWindow(dock);
                dockWin.ShowPlaced();

                // A barra se assenta primeiro (alguns gerenciadores de janelas dão foco a uma janela quando ela é movida,
                // e o balão fecha ao perder o foco, como deve ser). Só então o balão abre.
                DispatcherTimer.RunOnce(() =>
                {
                    var popup = new PopupWindow(folder, new PixelRect(500, 100, 76, 70)) { Title = "Pastel GUI popup" };
                    popup.ShowAtAnchor();
                    DispatcherTimer.RunOnce(() => Verify(lifetime, popup, dockWin, dock), TimeSpan.FromMilliseconds(1800));
                }, TimeSpan.FromMilliseconds(2600));
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERRO ao montar as janelas: " + ex);
                lifetime.Shutdown(1);
            }
        }

        private static void Verify(IClassicDesktopStyleApplicationLifetime lifetime, PopupWindow popup, FloatingWindow dockWin, FloatingModel dock)
        {
            try
            {
                Console.WriteLine("telas vistas pelo Avalonia: " + string.Join(" | ", popup.Screens.All.Select(sc => $"area={sc.WorkingArea} total={sc.Bounds} escala={sc.Scaling}")));
                Console.WriteLine($"balão: Position={popup.Position} Bounds={popup.Bounds} RenderScaling={popup.RenderScaling}");
                var wins = X11Inspect.TopLevels();
                Console.WriteLine($"janelas com nome no X11: {wins.Count}");
                foreach (var w in wins.Where(w => w.Name != null && w.Name.StartsWith("Pastel")))
                    Console.WriteLine($"   {w.Name}  pos=({w.X},{w.Y})  tam={w.W}x{w.H}  profundidade={w.Depth}  estado=[{string.Join(",", w.State)}]");

                var p = wins.FirstOrDefault(w => w.Name == "Pastel GUI popup");
                var d = wins.FirstOrDefault(w => w.Name == "Pastel – Dock GUI" || (w.Name?.Contains("Dock GUI") ?? false));
                Check("o balão existe como janela no X11", p != null);
                Check("a barra flutuante existe como janela no X11", d != null);

                if (p != null)
                {
                    Check("balão tem profundidade 32 (transparência de verdade)", p.Depth == 32, $"profundidade {p.Depth}");
                    Check("balão tem o tamanho do conteúdo (menor que a tela)", p.W > 150 && p.W < 900 && p.H > 100 && p.H < 700, $"{p.W}x{p.H}");
                    // Avalonia: Position é o canto da janela em pixels, igual ao que o X11 devolve
                    // âncora em (500,100) 76x70: o balão deve ficar centrado nela e abaixo dela
                    Check("balão aparece embaixo do ícone de referência (y perto de 170)", Math.Abs(p.Y - 154) <= 30, $"X11 y={p.Y}");
                    Check("e centrado nele (x perto de 538 - largura/2)", Math.Abs(p.X - (538 - p.W / 2)) <= 20, $"X11 x={p.X}, esperado ~{538 - p.W / 2}");
                }
                if (d != null)
                {
                    var g = GridMetrics.Get(dockWin.Screens);
                    var (ex, ey) = g.PixelOf(dock.Col, dock.Row);
                    Check("barra foi para a célula da grade (posição exata)", Math.Abs(d.X - ex) <= 2 && Math.Abs(d.Y - ey) <= 2, $"X11 ({d.X},{d.Y}) esperado ({ex},{ey})");
                    Check("tamanho da barra é múltiplo das células da grade", d.H == g.Cy || Math.Abs(d.H - g.Cy) <= 2, $"altura {d.H}, célula {g.Cy}");
                    bool below = d.State.Contains("_NET_WM_STATE_BELOW");
                    bool skipTb = d.State.Contains("_NET_WM_STATE_SKIP_TASKBAR");
                    Console.WriteLine($"   pedidos de janela de área de trabalho: no fundo={below}  fora da barra de tarefas={skipTb}  (depende do gerenciador de janelas; o KWin aplica)");
                }
                Console.WriteLine();
                Console.WriteLine($"{_pass} passaram, {_fail} falharam");
                lifetime.Shutdown(_fail == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERRO na verificação: " + ex);
                lifetime.Shutdown(1);
            }
        }
    }
}
