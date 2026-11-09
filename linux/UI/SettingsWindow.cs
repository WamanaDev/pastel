using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Pastel
{
    /// <summary>Janela de configurações: pastas, flutuantes, aparência e geral. Segue a identidade do Pastel.</summary>
    internal sealed class SettingsWindow : Window
    {
        private static readonly string[] Pages = { "Pastas", "Flutuantes", "Aparência", "Geral", "Sobre" };
        private static AppConfig Cfg => ConfigStore.Current;

        private string _page = "Pastas";
        private readonly HashSet<string> _expanded = new();
        private Palette _p = new();
        private readonly StackPanel _nav = new();
        private readonly StackPanel _body = new() { Margin = new Thickness(34, 28) };
        private readonly Border _navPanel = new() { Padding = new Thickness(14, 18) };
        private readonly TextBlock _brand = new();
        private readonly ContentControl _symbol = new() { Width = 34, Height = 34 };
        private readonly TextBlock _footer = new();

        public SettingsWindow(string? page = null)
        {
            if (page != null && Pages.Contains(page)) _page = page;
            Title = "Pastel – Configurações";
            Width = 920; Height = 640; MinWidth = 780; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Ui.Body; FontSize = 13;
            try { Icon = Controller.WindowIcon(); } catch { }

            var navStack = new DockPanel();
            var brandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 20) };
            _brand.Text = "Pastel"; _brand.FontFamily = Ui.Title; _brand.FontSize = 22; _brand.FontWeight = FontWeight.SemiBold;
            _brand.VerticalAlignment = VerticalAlignment.Center; _brand.Margin = new Thickness(10, 0, 0, 0);
            brandRow.Children.Add(_symbol);
            brandRow.Children.Add(_brand);
            DockPanel.SetDock(brandRow, Dock.Top);
            navStack.Children.Add(brandRow);
            _footer.Text = "Feito por Wictor Pamplona"; _footer.FontSize = 11; _footer.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(_footer, Dock.Bottom);
            navStack.Children.Add(_footer);
            navStack.Children.Add(_nav);
            _navPanel.Child = navStack;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
            grid.Children.Add(_navPanel);
            var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            Content = grid;

            Rebuild();
            Controller.StateChanged += OnState;
            Closed += (_, _) => Controller.StateChanged -= OnState;
        }

        private void OnState() => Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);

        public void ShowPage(string page)
        {
            if (!Pages.Contains(page)) return;
            _page = page;
            Rebuild();
        }

        // ---------- tema ----------

        private void Rebuild()
        {
            _p = new Palette();
            RequestedThemeVariant = _p.Light ? ThemeVariant.Light : ThemeVariant.Dark;
            Background = Palette.B(_p.Surface);
            _navPanel.Background = Palette.B(_p.Nav);
            _brand.Foreground = Palette.B(_p.Text);
            _footer.Foreground = Palette.B(PastelTheme.WithAlpha(_p.Text, 0x99));
            _symbol.Content = BuildSymbol(34);
            BuildNav();
            BuildPage();
        }

        /// <summary>O símbolo da marca: quadrado creme com a grade 2x2 em coral, pêssego, lilás e menta.</summary>
        public static Control BuildSymbol(double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(size * 0.26), Background = Palette.B(PastelTheme.Cream),
                BorderBrush = Palette.B(Color.FromArgb(0x24, 0x2B, 0x25, 0x40)), BorderThickness = new Thickness(1)
            });
            double gap = size * 0.09, c = (size - 3 * gap) / 2, r = size * 0.11;
            var colors = new[] { "#FF8FA3", "#FFC79A", "#B9A6FF", "#8FE3C0" };
            var canvas = new Canvas();
            for (int i = 0; i < 4; i++)
            {
                var sq = new Rectangle { Width = c, Height = c, RadiusX = r, RadiusY = r, Fill = Palette.B(Color.Parse(colors[i])) };
                Canvas.SetLeft(sq, gap + (i % 2) * (c + gap));
                Canvas.SetTop(sq, gap + (i / 2) * (c + gap));
                canvas.Children.Add(sq);
            }
            g.Children.Add(canvas);
            return g;
        }

        // ---------- menu lateral ----------

        private void BuildNav()
        {
            _nav.Children.Clear();
            foreach (var name in Pages)
            {
                bool on = name == _page;
                var item = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9), Margin = new Thickness(0, 2),
                    Background = Palette.B(on ? _p.NavActive : Colors.Transparent), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = Ui.Text(name, _p.Text, 13, on ? FontWeight.SemiBold : FontWeight.Normal, wrap: false)
                };
                string n = name;
                item.PointerEntered += (_, _) => { if (n != _page) item.Background = Palette.B(_p.Hover); };
                item.PointerExited += (_, _) => { if (n != _page) item.Background = Brushes.Transparent; };
                item.PointerReleased += (_, _) => { _page = n; BuildNav(); BuildPage(); };
                _nav.Children.Add(item);
            }
        }

        // ---------- blocos ----------

        private Control Header(string title, string subtitle, Control? action = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            var top = new DockPanel();
            if (action != null) { DockPanel.SetDock(action, Dock.Right); top.Children.Add(action); }
            top.Children.Add(Ui.Text(title, _p.Text, 26, FontWeight.SemiBold, wrap: false, family: Ui.Title));
            sp.Children.Add(top);
            sp.Children.Add(Ui.Text(subtitle, _p.Subtle, 13));
            return sp;
        }

        private Control Preview(IEnumerable<ShortcutItem> items, double size)
        {
            var grid = new UniformGrid { Rows = 2, Columns = 2, Width = size, Height = size };
            foreach (var it in items.Take(4))
            {
                var img = new Image { Source = IconCache.GetBitmap(it, 64), Margin = new Thickness(1.5) };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
                grid.Children.Add(img);
            }
            return new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Width = size + 10, Height = size + 10, Background = Palette.B(_p.Hover), Child = grid };
        }

        private Control AppList(IItemOwner owner)
        {
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (owner.Items.Count == 0) list.Children.Add(Ui.Text("Nenhum app ainda. Arraste atalhos para cá.", _p.Subtle, 12));
            foreach (var item in owner.Items.ToList())
            {
                var it = item;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 3) };
                var img = new Image { Width = 24, Height = 24, Source = IconCache.GetBitmap(it, 64), Margin = new Thickness(0, 0, 10, 0) };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
                row.Children.Add(img);
                var name = Ui.Text(it.Name, _p.Text, 13, wrap: false);
                name.VerticalAlignment = VerticalAlignment.Center; name.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var rm = Ui.Button(_p, "Remover", () => Controller.RemoveItem(owner, it));
                Grid.SetColumn(rm, 2);
                row.Children.Add(rm);
                list.Children.Add(row);
            }
            return list;
        }

        /// <summary>Cartão de uma pasta ou flutuante, com a lista de apps que abre e fecha.</summary>
        private Control OwnerCard(IItemOwner owner, IEnumerable<Control> controls, IEnumerable<Control>? extra = null)
        {
            var root = new StackPanel();
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var prev = Preview(owner.Items, 38);
            prev.Margin = new Thickness(0, 0, 14, 0);
            top.Children.Add(prev);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text(owner.Name, _p.Text, 15, FontWeight.SemiBold, wrap: false));
            int n = owner.Items.Count;
            info.Children.Add(Ui.Text(n == 1 ? "1 app" : $"{n} apps", _p.Subtle, 12));
            Grid.SetColumn(info, 1);
            top.Children.Add(info);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in controls) buttons.Children.Add(c);
            bool open = _expanded.Contains(owner.Id);
            buttons.Children.Add(Ui.Button(_p, open ? "Esconder apps" : "Ver apps", () =>
            {
                if (!_expanded.Remove(owner.Id)) _expanded.Add(owner.Id);
                BuildPage();
            }));
            Grid.SetColumn(buttons, 2);
            top.Children.Add(buttons);
            root.Children.Add(top);

            if (extra != null) foreach (var e in extra) root.Children.Add(e);
            if (open) root.Children.Add(AppList(owner));
            return Ui.Card(_p, root);
        }

        private static Control Gap(double h = 10) => new Border { Height = h };

        // ---------- páginas ----------

        private void BuildPage()
        {
            _body.Children.Clear();
            switch (_page)
            {
                case "Pastas": PagePastas(); break;
                case "Flutuantes": PageFlutuantes(); break;
                case "Aparência": PageAparencia(); break;
                case "Geral": PageGeral(); break;
                default: PageSobre(); break;
            }
        }

        private void PagePastas()
        {
            _body.Children.Add(Header("Pastas", "Cada pasta é um ícone na sua área de trabalho. Dê dois cliques nele para abrir os apps.",
                Ui.Button(_p, "Nova pasta", () => Controller.NewFolder(), "primary")));
            if (Cfg.Folders.Count == 0)
            {
                _body.Children.Add(Ui.Card(_p, Ui.Text("Nenhuma pasta ainda. Clique em Nova pasta, ou arraste apps para uma na sua área de trabalho.", _p.Subtle)));
                return;
            }
            foreach (var f in Cfg.Folders.ToList())
            {
                var folder = f;
                _body.Children.Add(OwnerCard(folder, new[]
                {
                    Ui.Button(_p, "Abrir", () => Controller.ShowPopupCentered(folder)),
                    Ui.Button(_p, "Renomear", () => Controller.RenameFolder(folder)),
                    Ui.Button(_p, "Excluir", () => Controller.DeleteFolder(folder), "danger")
                }));
            }
        }

        private void PageFlutuantes()
        {
            _body.Children.Add(Header("Flutuantes", "Barras de apps soltas na área de trabalho. Elas encaixam na grade de ícones, como um atalho comum.",
                Ui.Button(_p, "Novo flutuante", () => Controller.NewFloat(), "primary")));

            // No Plasma a grade não pode ser lida; aqui o usuário ajusta até a barra alinhar com os ícones.
            var grid = new StackPanel();
            grid.Children.Add(Ui.Row(_p, "Grade da área de trabalho", "O Plasma não informa o tamanho da célula dos ícones. Ajuste até a barra alinhar (padrão 96 × 104).", Gap(0)));
            grid.Children.Add(Gap());
            grid.Children.Add(Ui.Row(_p, "Largura da célula", null, Stepper(Cfg.GridCellWidth >= 40 ? Cfg.GridCellWidth : GridMetrics.DefaultCx, 40, 300, v => { Cfg.GridCellWidth = v; GridChanged(); })));
            grid.Children.Add(Gap());
            grid.Children.Add(Ui.Row(_p, "Altura da célula", null, Stepper(Cfg.GridCellHeight >= 40 ? Cfg.GridCellHeight : GridMetrics.DefaultCy, 40, 300, v => { Cfg.GridCellHeight = v; GridChanged(); })));
            _body.Children.Add(Ui.Card(_p, grid));

            if (Cfg.Floats.Count == 0)
            {
                _body.Children.Add(Ui.Card(_p, Ui.Text("Nenhum flutuante ainda. Clique em Novo flutuante, ou envie um app pelo botão direito (Pastel > Flutuante).", _p.Subtle)));
                return;
            }
            foreach (var m in Cfg.Floats.ToList())
            {
                var fl = m;
                var options = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
                options.Children.Add(Ui.Row(_p, "Mostrar na área de trabalho", null, Ui.Switch(_p, fl.Visible, v => Controller.SetFloatVisible(fl, v))));
                options.Children.Add(Gap());
                options.Children.Add(Ui.Row(_p, "Orientação", null, Ui.Segmented(_p, new[] { "Horizontal", "Vertical" }, fl.Vertical ? 1 : 0, i => { fl.Vertical = i == 1; Controller.FloatChanged(fl); })));
                options.Children.Add(Gap());
                int[] sizes = { 24, 32, 40, 52 };
                int sel = Array.IndexOf(sizes, fl.IconSize); if (sel < 0) sel = 2;
                options.Children.Add(Ui.Row(_p, "Tamanho dos ícones", null, Ui.Segmented(_p, new[] { "Mínimo", "Pequeno", "Médio", "Grande" }, sel, i => { fl.IconSize = sizes[i]; Controller.FloatChanged(fl); })));
                options.Children.Add(Gap());
                options.Children.Add(Ui.Row(_p, "Fundo", "Sem fundo, os ícones ficam soltos na área de trabalho.", Ui.Segmented(_p, new[] { "Com vidro", "Sem fundo" }, fl.ShowBackground ? 0 : 1, i => { fl.ShowBackground = i == 0; Controller.FloatChanged(fl); })));
                options.Children.Add(Gap());
                options.Children.Add(Ui.Row(_p, "Mostrar nome", null, Ui.Switch(_p, fl.ShowName, v => { fl.ShowName = v; Controller.FloatChanged(fl); })));
                options.Children.Add(Gap());
                string[] fx = { "none", "focus", "proximity" };
                options.Children.Add(Ui.Row(_p, "Efeito ao passar o mouse", "Destaque: o ícone sob o mouse cresce. Aproximação: quanto mais perto do mouse, maior.",
                    Ui.Segmented(_p, new[] { "Nenhum", "Destaque", "Aproximação" }, Math.Max(0, Array.IndexOf(fx, fl.HoverEffect)), i => { fl.HoverEffect = fx[i]; Controller.FloatChanged(fl); })));

                _body.Children.Add(OwnerCard(fl, new[]
                {
                    Ui.Button(_p, "Renomear", () => Controller.RenameFloat(fl)),
                    Ui.Button(_p, "Excluir", () => Controller.DeleteFloat(fl), "danger")
                }, new Control[] { options }));
            }
        }

        private void GridChanged()
        {
            ConfigStore.Save();
            Controller.RelayoutFloats();
        }

        private Control Stepper(int value, int min, int max, Action<int> onChange)
        {
            var label = Ui.Text(value + " px", _p.Text, 13, FontWeight.SemiBold, wrap: false);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MinWidth = 54;
            label.TextAlignment = TextAlignment.Center;
            void Set(int v) { value = Math.Clamp(v, min, max); label.Text = value + " px"; onChange(value); }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Button(_p, "–", () => Set(value - 4)));
            row.Children.Add(label);
            row.Children.Add(Ui.Button(_p, "+", () => Set(value + 4)));
            return row;
        }

        private void PageAparencia()
        {
            _body.Children.Add(Header("Aparência", "Vale para o balão das pastas, os flutuantes, os ícones das pastas e esta janela."));

            string[] modes = { "auto", "light", "dark" };
            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Tema", "Por padrão o Pastel segue o tema do KDE.",
                Ui.Segmented(_p, new[] { "Seguir o KDE", "Claro", "Escuro" }, Math.Max(0, Array.IndexOf(modes, Cfg.ThemeMode)),
                    i => { Cfg.ThemeMode = modes[i]; ConfigStore.Save(); Controller.ApplyAppearance(); }))));

            string[] accents = { "system", "brand" };
            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Cor de destaque", "Contornos, realces e a página atual dos balões.",
                Ui.Segmented(_p, new[] { "Do KDE", "Violeta do Pastel" }, Math.Max(0, Array.IndexOf(accents, Cfg.AccentMode)),
                    i => { Cfg.AccentMode = accents[i]; ConfigStore.Save(); Controller.ApplyAppearance(); }))));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Animações", "Entrada e saída suaves do balão e troca de página.",
                Ui.Switch(_p, Cfg.Animations, v => { Cfg.Animations = v; ConfigStore.Save(); }))));
        }

        private void PageGeral()
        {
            _body.Children.Add(Header("Geral", "O básico para o Pastel ficar de boa no seu KDE."));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Iniciar com a sessão", "Assim as pastas abrem rápido, sem esperar o Pastel carregar.",
                Ui.Switch(_p, ShellMenus.IsAutostart(), v => Controller.SetAutostart(v)))));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Menu \"Pastel\" no botão direito", "Em qualquer arquivo ou pasta do Dolphin: enviar para uma pasta ou flutuante.",
                Ui.Switch(_p, Cfg.SendToMenu, v => Controller.SetSendToMenu(v)))));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Ocultar pastas vazias", "Esconde da área de trabalho a pasta que ficou sem nenhum item e mostra de novo quando ela receber algo. Pasta recém-criada continua visível até ganhar o primeiro item.",
                Ui.Switch(_p, Cfg.HideEmptyFolders, v => Controller.SetHideEmptyFolders(v)))));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Avisar de versões novas", "Procura no GitHub, a cada 12 horas, se saiu uma versão nova. É a única vez que o Pastel usa a internet.",
                Ui.Switch(_p, Cfg.CheckUpdates, v => { Cfg.CheckUpdates = v; ConfigStore.Save(); }))));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Procurar atualização", "Confere agora se há uma versão nova do Pastel.",
                Ui.Button(_p, "Verificar", async () =>
                {
                    var msg = await Controller.CheckForUpdateAsync(manual: true);
                    if (msg != null) Dialogs.Info("Pastel", msg);
                }))));

            _body.Children.Add(Ui.Card(_p, Ui.Row(_p, "Pasta de dados", "Onde o Pastel guarda a configuração e os atalhos que saíram da área de trabalho.",
                Ui.Button(_p, "Abrir", () => Controller.OpenDataFolder()))));
        }

        private void PageSobre()
        {
            var sp = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(BuildSymbol(64));
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            names.Children.Add(Ui.Text("Pastel", _p.Text, 26, FontWeight.SemiBold, wrap: false, family: Ui.Title));
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            names.Children.Add(Ui.Text($"Versão {v?.Major}.{v?.Minor}.{v?.Build} para Linux (beta)", _p.Subtle, 12));
            row.Children.Add(names);
            sp.Children.Add(row);
            sp.Children.Add(Ui.Text("A pasta do celular, no seu PC.", _p.Text, 15, FontWeight.SemiBold));
            sp.Children.Add(Gap(6));
            sp.Children.Add(Ui.Text("O Pastel junta seus apps em pastas na área de trabalho. Ainda tá em beta, então pode ter um bug ou outro escondido. Se algo quebrar, o Pastel pede desculpa e o Wictor corrige.", _p.Subtle));
            sp.Children.Add(Gap());
            sp.Children.Add(Ui.Text("Feito por Wictor Pamplona.", _p.Text, 13, FontWeight.SemiBold));
            _body.Children.Add(Header("Sobre", "Quem faz e como o Pastel funciona."));
            _body.Children.Add(Ui.Card(_p, sp));
        }
    }
}
