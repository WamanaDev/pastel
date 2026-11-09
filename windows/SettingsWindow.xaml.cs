using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pastel
{
    /// <summary>Janela de configurações: pastas, flutuantes, aparência e geral. Segue a identidade do Pastel.</summary>
    public partial class SettingsWindow : Window
    {
        private static readonly string[] Pages = { "Pastas", "Flutuantes", "Regras", "Aparência", "Geral", "Sobre" };
        private string _page = "Pastas";
        private readonly HashSet<string> _expanded = new();
        private bool _building;

        private static App TheApp => (App)Application.Current;
        private static AppConfig Cfg => ConfigStore.Current;

        public SettingsWindow(string? page = null)
        {
            InitializeComponent();
            if (page != null && Pages.Contains(page)) _page = page;

            try
            {
                using var s = typeof(SettingsWindow).Assembly.GetManifestResourceStream("Pastel.ico");
                if (s != null) Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch { }

            ApplyTheme();
            TheApp.StateChanged += OnStateChanged;
            Closed += (_, _) => TheApp.StateChanged -= OnStateChanged;
        }

        public void ShowPage(string page)
        {
            if (!Pages.Contains(page)) return;
            _page = page;
            BuildNav();
            BuildPage();
        }

        private void OnStateChanged() => Dispatcher.BeginInvoke(new Action(() => { ApplyTheme(); }));

        // ---------- tema da própria janela ----------

        private void ApplyTheme()
        {
            bool light = WindowsTheme.IsLight;
            Color text = WindowsTheme.Text(light), surface = WindowsTheme.Surface(light);
            Color card = light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x25, 0x20, 0x33);
            Color nav = light ? Color.FromRgb(0xF7, 0xF0, 0xEA) : Color.FromRgb(0x16, 0x12, 0x20);

            void Set(string key, Color c) => Resources[key] = new SolidColorBrush(c);
            Set("Brush.Surface", surface);
            Set("Brush.Nav", nav);
            Set("Brush.Card", card);
            Set("Brush.CardBorder", WindowsTheme.WithAlpha(text, 0x22));
            Set("Brush.Text", text);
            Set("Brush.Subtle", WindowsTheme.WithAlpha(text, 0xB0));
            Set("Brush.Hover", WindowsTheme.WithAlpha(text, 0x14));
            Set("Brush.NavActive", WindowsTheme.WithAlpha(WindowsTheme.Violet, light ? (byte)0x24 : (byte)0x40));
            Set("Brush.Violet", WindowsTheme.Violet);
            Set("Brush.VioletText", light ? WindowsTheme.Violet : Color.FromRgb(0xB9, 0xA9, 0xFF)); // violeta legível como texto
            Set("Brush.Danger", light ? Color.FromRgb(0xC2, 0x2B, 0x4A) : Color.FromRgb(0xFF, 0x8F, 0xA3));

            BrandSymbol.Content = BuildSymbol(34);
            NavFooter.Text = "Feito por Wictor Pamplona";
            BuildNav();
            BuildPage();
        }

        /// <summary>O símbolo da marca: quadrado creme com a grade 2x2 em coral, pêssego, lilás e menta.</summary>
        public static UIElement BuildSymbol(double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(size * 0.26),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF9, 0xF5)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0x2B, 0x25, 0x40)),
                BorderThickness = new Thickness(1)
            });
            double gap = size * 0.09, c = (size - 3 * gap) / 2, r = size * 0.11;
            var colors = new[] { "#FF8FA3", "#FFC79A", "#B9A6FF", "#8FE3C0" };
            var canvas = new Canvas();
            for (int i = 0; i < 4; i++)
            {
                var sq = new Rectangle
                {
                    Width = c, Height = c, RadiusX = r, RadiusY = r,
                    Fill = (Brush)new BrushConverter().ConvertFromString(colors[i])!
                };
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
            Nav.Children.Clear();
            foreach (var p in Pages)
            {
                string name = p;
                var rb = new RadioButton { Content = name, GroupName = "nav", IsChecked = name == _page, Style = (Style)FindResource("NavButton") };
                rb.Checked += (_, _) => { if (_building || _page == name) return; _page = name; BuildPage(); };
                Nav.Children.Add(rb);
            }
        }

        // ---------- blocos reutilizáveis ----------

        private TextBlock Text(string text, double size = 13, FontWeight? weight = null, string brush = "Brush.Text", bool wrap = true)
        {
            var tb = new TextBlock { Text = text, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
            if (weight != null) tb.FontWeight = weight.Value;
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return tb;
        }

        private TextBlock Heading(string text)
        {
            var tb = Text(text, 26, FontWeights.SemiBold, wrap: false);
            tb.FontFamily = new FontFamily("Fredoka, Nunito, Segoe UI Variable Display, Segoe UI");
            return tb;
        }

        private Border Card(UIElement child, double bottom = 12)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 15, 18, 15), Margin = new Thickness(0, 0, 0, bottom), BorderThickness = new Thickness(1), Child = child };
            b.SetResourceReference(Border.BackgroundProperty, "Brush.Card");
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.CardBorder");
            return b;
        }

        private Button Btn(string label, Action action, string style = "GhostButton")
        {
            var b = new Button { Content = label, Style = (Style)FindResource(style), Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (_, _) => action();
            return b;
        }

        /// <summary>Linha "título + descrição" à esquerda e um controle à direita.</summary>
        private Grid Row(string title, string? desc, UIElement control)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(Text(title, 14, FontWeights.SemiBold));
            if (desc != null) left.Children.Add(Text(desc, 12, brush: "Brush.Subtle"));
            g.Children.Add(left);
            Grid.SetColumn((FrameworkElement)control, 1);
            ((FrameworkElement)control).VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(control);
            return g;
        }

        private StackPanel Segmented(string[] labels, int selected, Action<int> onPick)
        {
            var group = Guid.NewGuid().ToString("N");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var wrap = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(2), BorderThickness = new Thickness(1) };
            wrap.SetResourceReference(Border.BorderBrushProperty, "Brush.CardBorder");
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var rb = new RadioButton { Content = labels[i], GroupName = group, IsChecked = i == selected, Style = (Style)FindResource("SegButton") };
                rb.Checked += (_, _) => { if (!_building) onPick(idx); };
                sp.Children.Add(rb);
            }
            wrap.Child = sp;
            var outer = new StackPanel { Orientation = Orientation.Horizontal };
            outer.Children.Add(wrap);
            return outer;
        }

        private ToggleSwitch Switch(bool on, Action<bool> onChange)
        {
            var sw = new ToggleSwitch(on);
            sw.Toggled += v => onChange(v);
            return sw;
        }

        private UIElement Preview(IEnumerable<ShortcutItem> items, double size)
        {
            var grid = new UniformGrid { Rows = 2, Columns = 2, Width = size, Height = size };
            foreach (var it in items.Take(4))
            {
                var img = new Image { Source = IconService.GetStyled(it.Path, 64), Margin = new Thickness(1.5) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
            }
            var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Width = size + 10, Height = size + 10, Child = grid };
            b.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
            return b;
        }

        private StackPanel Header(string title, string subtitle, UIElement? action = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            var top = new DockPanel();
            if (action != null) { DockPanel.SetDock(action, Dock.Right); top.Children.Add(action); }
            top.Children.Add(Heading(title));
            sp.Children.Add(top);
            sp.Children.Add(Text(subtitle, 13, brush: "Brush.Subtle"));
            return sp;
        }

        // ---------- blocos do estilo minimalista (linhas finas, sem cartões) ----------

        /// <summary>Botão em forma de texto. kind: LinkButton (neutro), LinkAccent (violeta) ou LinkDanger.</summary>
        private Button Link(string label, Action action, string kind = "LinkButton")
        {
            var b = new Button { Content = label, Style = (Style)FindResource(kind) };
            b.Click += (_, _) => action();
            return b;
        }

        private Border Divider(double top = 0, double bottom = 0)
        {
            var d = new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom) };
            d.SetResourceReference(Border.BackgroundProperty, "Brush.CardBorder");
            return d;
        }

        private TextBlock SectionLabel(string text, double top = 26)
        {
            var t = Text(text.ToUpperInvariant(), 11, FontWeights.SemiBold, "Brush.Subtle", wrap: false);
            t.Margin = new Thickness(0, top, 0, 8);
            return t;
        }

        /// <summary>Uma opção: título e descrição, com o controle ao lado (inline) ou embaixo, e uma linha fina abaixo.</summary>
        private UIElement Opt(string title, string? desc, FrameworkElement control, bool inline = true, bool last = false)
        {
            var root = new StackPanel { Margin = new Thickness(0, 11, 0, 11) };
            if (inline)
                root.Children.Add(Row(title, desc, control));
            else
            {
                root.Children.Add(Text(title, 14, FontWeights.SemiBold));
                if (desc != null) root.Children.Add(Text(desc, 12, brush: "Brush.Subtle"));
                control.Margin = new Thickness(0, 9, 0, 0);
                control.HorizontalAlignment = HorizontalAlignment.Left;
                root.Children.Add(control);
            }
            var b = new Border { BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = root };
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.CardBorder");
            return b;
        }

        private Border RowFrame(UIElement child)
        {
            var b = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = child };
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.CardBorder");
            return b;
        }

        /// <summary>O "⋯" que abre um menu.</summary>
        private Button Dots(Func<ContextMenu> build)
        {
            var b = Btn("⋯", () => { }, "IconButton");
            b.Margin = new Thickness(2, 0, 0, 0);
            b.FontSize = 18;
            b.Click += (_, _) => { var m = build(); m.PlacementTarget = b; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
            return b;
        }

        private static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        /// <summary>Os apps de uma pasta ou flutuante, em duas colunas, cada um com o ícone ao lado do nome.</summary>
        private UIElement AppRows(IItemOwner owner, double indent)
        {
            bool mirror = owner is FolderModel { IsMirror: true };
            var wrap = new StackPanel { Margin = new Thickness(indent, 2, 0, 12) };
            if (owner.Items.Count == 0)
            {
                wrap.Children.Add(Text(mirror ? "A pasta de origem está vazia." : "Nenhum app ainda. Arraste atalhos para cá.", 12, brush: "Brush.Subtle"));
                return wrap;
            }

            const int Max = 24; // a lista é só para conferir; o resto fica no balão
            var grid = new UniformGrid { Columns = indent > 0 ? 2 : 1 };
            foreach (var item in owner.Items.Take(Max).ToList())
            {
                var it = item;
                var cell = new Grid { Margin = new Thickness(0, 2, 12, 2) };
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var img = new Image { Width = 24, Height = 24, Source = IconService.GetStyled(it.Path, 64), Margin = new Thickness(0, 0, 10, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                cell.Children.Add(img);

                var name = Text(it.Name, 13, wrap: false);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(name, 1);
                cell.Children.Add(name);

                if (!mirror) // espelhada: os arquivos não são do Pastel
                {
                    var rm = Link("Remover", () => TheApp.RemoveItem(owner, it));
                    Grid.SetColumn(rm, 2);
                    cell.Children.Add(rm);
                }
                grid.Children.Add(cell);
            }
            wrap.Children.Add(grid);
            if (owner.Items.Count > Max)
            {
                var more = Text($"+ {owner.Items.Count - Max} {(mirror ? "itens" : "apps")}", 12, brush: "Brush.Subtle");
                more.Margin = new Thickness(0, 4, 0, 0);
                wrap.Children.Add(more);
            }
            return wrap;
        }

        // ---------- páginas ----------

        private void BuildPage()
        {
            _building = true;
            Page.Children.Clear();
            switch (_page)
            {
                case "Pastas": PagePastas(); break;
                case "Flutuantes": PageFlutuantes(); break;
                case "Regras": PageRegras(); break;
                case "Aparência": PageAparencia(); break;
                case "Geral": PageGeral(); break;
                default: PageSobre(); break;
            }
            _building = false;
        }

        private void PagePastas()
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Link("Espelhar uma pasta…", () => { TheApp.NewMirrorGroup(); BuildPage(); }));
            actions.Children.Add(Btn("+ Nova pasta", () => { TheApp.NewGroup(); BuildPage(); }, "PrimaryButton"));
            Page.Children.Add(Header("Pastas", "Cada pasta é um ícone na área de trabalho. Dois cliques abrem o balão.", actions));

            if (Cfg.Folders.Count == 0)
            {
                Page.Children.Add(Text("Nenhuma pasta ainda. Clique em + Nova pasta, ou use o botão direito na área de trabalho.", 13, brush: "Brush.Subtle"));
                return;
            }
            Page.Children.Add(Divider());
            foreach (var f in Cfg.Folders.ToList()) Page.Children.Add(FolderRow(f));

            var hint = Text("Uma pasta espelhada mostra o conteúdo de uma pasta de verdade, sem mexer nos arquivos. Excluir uma pasta devolve os atalhos para a área de trabalho.", 12, brush: "Brush.Subtle");
            hint.Margin = new Thickness(0, 16, 0, 0);
            Page.Children.Add(hint);
        }

        private UIElement FolderRow(FolderModel f)
        {
            bool open = _expanded.Contains(f.Id);
            var line = new Grid { MinHeight = 60 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prev = Preview(f.Items, 28);
            prev.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            prev.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            line.Children.Add(prev);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Text(f.Name, 15, FontWeights.SemiBold, wrap: false));
            int n = f.Items.Count;
            string sub = f.IsMirror ? $"Espelha {f.SourcePath} · {(n == 1 ? "1 item" : n + " itens")}" : (n == 1 ? "1 app" : $"{n} apps");
            var subText = Text(sub, 12, brush: "Brush.Subtle", wrap: false);
            subText.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Children.Add(subText);
            Grid.SetColumn(info, 1);
            line.Children.Add(info);

            var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            links.Children.Add(Link("Abrir", () => TheApp.ShowPopupCentered(f), "LinkAccent"));
            links.Children.Add(Link(open ? "Esconder" : (f.IsMirror ? "Ver itens" : "Ver apps"), () =>
            {
                if (!_expanded.Remove(f.Id)) _expanded.Add(f.Id);
                BuildPage();
            }));
            links.Children.Add(Dots(() =>
            {
                var menu = new ContextMenu();
                var rename = new MenuItem { Header = "Renomear" };
                rename.Click += (_, _) => TheApp.RenameGroup(f);
                var delete = new MenuItem { Header = "Excluir" };
                delete.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
                delete.Click += (_, _) => TheApp.DeleteGroup(f);
                menu.Items.Add(rename);
                menu.Items.Add(delete);
                return menu;
            }));
            Grid.SetColumn(links, 2);
            line.Children.Add(links);

            var root = new StackPanel();
            root.Children.Add(line);
            if (open) root.Children.Add(AppRows(f, 52));
            return RowFrame(root);
        }

        private void PageFlutuantes()
        {
            Page.Children.Add(Header("Flutuantes", "Barras de apps soltas na área de trabalho, encaixadas na grade de ícones do Windows.",
                Btn("+ Novo flutuante", () => { TheApp.NewFloat(); BuildPage(); }, "PrimaryButton")));

            if (Cfg.Floats.Count == 0)
            {
                Page.Children.Add(Text("Nenhum flutuante ainda. Clique em + Novo flutuante, ou envie um app pelo botão direito (Pastel > Enviar para flutuante).", 13, brush: "Brush.Subtle"));
                return;
            }
            foreach (var m in Cfg.Floats.ToList()) Page.Children.Add(FloatSection(m));
        }

        private UIElement FloatSection(FloatingModel fl)
        {
            bool open = _expanded.Contains(fl.Id);
            var root = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };

            // cabeçalho: nome, contagem e links
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Text(fl.Name, 15, FontWeights.SemiBold, wrap: false));
            var count = Text(fl.Items.Count == 1 ? "1 app" : $"{fl.Items.Count} apps", 12, brush: "Brush.Subtle", wrap: false);
            count.Margin = new Thickness(10, 3, 0, 0);
            title.Children.Add(count);
            head.Children.Add(title);
            var links = new StackPanel { Orientation = Orientation.Horizontal };
            links.Children.Add(Link(open ? "Esconder apps" : "Ver apps", () =>
            {
                if (!_expanded.Remove(fl.Id)) _expanded.Add(fl.Id);
                BuildPage();
            }));
            links.Children.Add(Link("Renomear", () => TheApp.RenameFloat(fl)));
            links.Children.Add(Link("Excluir", () => TheApp.DeleteFloat(fl), "LinkDanger"));
            Grid.SetColumn(links, 1);
            head.Children.Add(links);
            root.Children.Add(head);
            root.Children.Add(Divider());

            // opções (esquerda) + prévia ao vivo (direita); em janela estreita a prévia desce
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            var options = new StackPanel { Width = 280, Margin = new Thickness(0, 0, 24, 0) };
            options.Children.Add(Opt("Mostrar na área de trabalho", null, Switch(fl.Visible, v => TheApp.SetFloatVisible(fl, v))));
            options.Children.Add(Opt("Orientação", null, Segmented(new[] { "Horizontal", "Vertical" }, fl.Vertical ? 1 : 0,
                i => { fl.Vertical = i == 1; TheApp.FloatChanged(fl); }), inline: false));
            int[] sizes = { 24, 32, 40, 52 };
            int sel = Array.IndexOf(sizes, fl.IconSize); if (sel < 0) sel = 2;
            options.Children.Add(Opt("Tamanho dos ícones", null, Segmented(new[] { "Mínimo", "Pequeno", "Médio", "Grande" }, sel,
                i => { fl.IconSize = sizes[i]; TheApp.FloatChanged(fl); }), inline: false));
            options.Children.Add(Opt("Fundo", "Sem fundo, os ícones ficam soltos na área de trabalho.",
                Segmented(new[] { "Com vidro", "Sem fundo" }, fl.ShowBackground ? 0 : 1,
                    i => { fl.ShowBackground = i == 0; TheApp.FloatChanged(fl); }), inline: false));
            options.Children.Add(Opt("Mostrar nome", "O nome do flutuante aparece embaixo dos ícones.",
                Switch(fl.ShowName, v => { fl.ShowName = v; TheApp.FloatChanged(fl); })));
            string[] fx = { "none", "focus", "proximity" };
            options.Children.Add(Opt("Efeito ao passar o mouse", "Destaque: o ícone sob o mouse cresce. Aproximação: quanto mais perto do mouse, maior.",
                Segmented(new[] { "Nenhum", "Destaque", "Aproximação" }, Math.Max(0, Array.IndexOf(fx, fl.HoverEffect)),
                    i => { fl.HoverEffect = fx[i]; TheApp.FloatChanged(fl); }), inline: false, last: true));
            wrap.Children.Add(options);

            var right = new StackPanel { Width = 280, Margin = new Thickness(0, 12, 0, 0) };
            var label = Text("PRÉVIA AO VIVO", 11, FontWeights.SemiBold, "Brush.Subtle", wrap: false);
            label.Margin = new Thickness(0, 0, 0, 8);
            right.Children.Add(label);
            right.Children.Add(new FloatingPreview(fl));
            var tip = Text("Passe o mouse nos ícones para ver o efeito.", 11.5, brush: "Brush.Subtle");
            tip.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(tip);
            if (open)
            {
                var apps = AppRows(fl, 0);
                ((FrameworkElement)apps).Margin = new Thickness(0, 14, 0, 0);
                right.Children.Add(apps);
            }
            wrap.Children.Add(right);

            root.Children.Add(wrap);
            return root;
        }

        private TextBox Field(string value, double width, Action<string> commit)
        {
            var tb = new TextBox { Text = value, Width = width, Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(1) };
            tb.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            tb.SetResourceReference(Control.BackgroundProperty, "Brush.Hover");
            tb.SetResourceReference(Control.BorderBrushProperty, "Brush.CardBorder");
            tb.LostFocus += (_, _) => commit(tb.Text);
            return tb;
        }

        // ---------- aba Regras: lista enxuta + menu "Adicionar" ----------

        private StackPanel? _ruleList;
        private Popup? _addPopup;
        private StackPanel? _addList;
        private TextBox? _addSearch;
        private Button? _addButton;
        private bool _reopenAdd;                         // a página é refeita a cada mudança; isto reabre o menu depois
        private readonly HashSet<string> _ruleEditing = new(); // regras próprias com o editor aberto

        private void PageRegras()
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Btn("Aplicar agora", () => ShowApplied(TheApp.ApplyRulesNow())));
            actions.Children.Add(BuildAddMenu());
            Page.Children.Add(Header("Regras", "O que chega na área de trabalho vai para a pasta certa.", actions));

            _ruleList = new StackPanel();
            Page.Children.Add(_ruleList);
            RefreshRules();

            var hint = Text("Não achou o tipo? Em Adicionar > Regra própria dá para criar uma por extensão ou parte do nome.", 12, brush: "Brush.Subtle");
            hint.Margin = new Thickness(0, 18, 0, 0);
            Page.Children.Add(hint);

            if (_reopenAdd)
            {
                _reopenAdd = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(OpenAddMenu));
            }
        }

        private void ShowApplied(int n) =>
            MessageBox.Show(n == 0 ? "Nenhum arquivo da área de trabalho casa com as regras ativas."
                                   : n == 1 ? "1 arquivo foi enviado para a pasta." : $"{n} arquivos foram enviados para as pastas.",
                "Pastel", MessageBoxButton.OK, MessageBoxImage.Information);

        // --- lista de regras ---

        private void RefreshRules()
        {
            if (_ruleList == null) return;
            _ruleList.Children.Clear();
            if (Cfg.Rules.Count == 0)
            {
                _ruleList.Children.Add(Text("Nenhuma regra ainda. Use + Adicionar para escolher um tipo.", 13, brush: "Brush.Subtle"));
                return;
            }
            var targets = TheApp.RuleTargets().ToList();
            foreach (var r in Cfg.Rules.ToList()) _ruleList.Children.Add(RuleRow(r, targets));
        }

        private UIElement RuleRow(AutoRule rule, List<IItemOwner> targets)
        {
            var target = targets.FirstOrDefault(t => t.Id == rule.TargetId);
            var preset = rule.Category.Length > 0 ? RuleCatalog.Find(rule.Category) : null;

            var line = new Grid { Height = 50 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = Text(rule.Name, 14, FontWeights.SemiBold, wrap: false);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(0, 0, 8, 0);
            if (!rule.Enabled) name.Opacity = 0.55;
            line.Children.Add(name);

            var arrow = Text("→", 14, brush: "Brush.Subtle", wrap: false);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(arrow, 1);
            line.Children.Add(arrow);

            string targetName = target?.Name ?? (rule.TargetName.Length > 0 ? rule.TargetName : preset?.Name ?? "—");
            var chipText = Text(targetName, 12.5, wrap: false);
            var chip = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = chipText };
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
            if (target == null) chip.Opacity = 0.55;
            Grid.SetColumn(chip, 2);
            line.Children.Add(chip);

            string meta = target == null ? (rule.TargetId.Length == 0 && rule.TargetName.Length == 0 ? "escolha uma pasta"
                                          : rule.TargetId.Length == 0 ? "pasta criada com o 1º arquivo" : "pasta será recriada")
                                         : target.Items.Count == 1 ? "1 item" : $"{target.Items.Count} itens";
            var metaText = Text(meta, 12.5, brush: "Brush.Subtle", wrap: false);
            metaText.VerticalAlignment = VerticalAlignment.Center;
            metaText.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(metaText, 3);
            line.Children.Add(metaText);

            var sw = Switch(rule.Enabled, v =>
            {
                if (v && !AutoRules.IsUsable(rule))
                {
                    MessageBox.Show("Defina pelo menos uma extensão ou um trecho do nome antes de ligar, senão a regra pegaria todos os arquivos.",
                        "Pastel", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshRules();
                    return;
                }
                rule.Enabled = v;
                TheApp.RuleChanged();
                RefreshRules();
            });
            Grid.SetColumn(sw, 4);
            line.Children.Add(sw);

            var dots = Btn("⋯", () => { }, "IconButton");
            dots.FontSize = 18;
            dots.Click += (_, _) => { var m = RuleMenu(rule, targets); m.PlacementTarget = dots; m.IsOpen = true; };
            Grid.SetColumn(dots, 5);
            line.Children.Add(dots);

            var root = new StackPanel();
            root.Children.Add(line);
            if (_ruleEditing.Contains(rule.Id)) root.Children.Add(RuleEditor(rule, targets));

            var wrap = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = root };
            wrap.SetResourceReference(Border.BorderBrushProperty, "Brush.CardBorder");
            return wrap;
        }

        private ContextMenu RuleMenu(AutoRule rule, List<IItemOwner> targets)
        {
            var menu = new ContextMenu { Placement = PlacementMode.Bottom };

            var apply = new MenuItem { Header = "Aplicar só esta" };
            apply.Click += (_, _) =>
            {
                if (!rule.Enabled) { MessageBox.Show("Ligue a regra primeiro.", "Pastel", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                int n = TheApp.ApplyRulesNow(rule);
                ShowApplied(n);
                RefreshRules();
            };
            menu.Items.Add(apply);

            if (rule.Category.Length == 0)
            {
                bool open = _ruleEditing.Contains(rule.Id);
                var edit = new MenuItem { Header = open ? "Fechar edição" : "Editar…" };
                edit.Click += (_, _) => { if (!_ruleEditing.Remove(rule.Id)) _ruleEditing.Add(rule.Id); RefreshRules(); };
                menu.Items.Add(edit);
            }

            var dest = new MenuItem { Header = "Mudar pasta de destino" };
            if (targets.Count == 0) dest.IsEnabled = false;
            foreach (var t in targets)
            {
                var tt = t;
                var mi = new MenuItem { Header = tt.Name, IsChecked = tt.Id == rule.TargetId };
                mi.Click += (_, _) => { rule.TargetId = tt.Id; rule.TargetName = tt.Name; TheApp.RuleChanged(); RefreshRules(); };
                dest.Items.Add(mi);
            }
            menu.Items.Add(dest);

            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Remover regra" };
            remove.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
            remove.Click += (_, _) =>
            {
                TheApp.DeleteRule(rule);
                _ruleEditing.Remove(rule.Id);
                RefreshRules();
                RefreshAddList();
            };
            menu.Items.Add(remove);
            return menu;
        }

        /// <summary>Editor de uma regra própria: nome, extensões, trecho do nome.</summary>
        private UIElement RuleEditor(AutoRule rule, List<IItemOwner> targets)
        {
            var body = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            body.Children.Add(Row("Nome da regra", null,
                Field(rule.Name, 220, v => { if (!string.IsNullOrWhiteSpace(v)) { rule.Name = v.Trim(); TheApp.RuleChanged(); } })));
            body.Children.Add(new Border { Height = 10 });
            body.Children.Add(Row("Extensões", "Separe por vírgula. Ex.: pdf, docx, xlsx",
                Field(AutoRules.FormatExtensions(rule.Extensions), 220, v =>
                {
                    rule.Extensions = AutoRules.ParseExtensions(v);
                    if (rule.Enabled && !AutoRules.IsUsable(rule)) rule.Enabled = false;
                    TheApp.RuleChanged();
                    RefreshRules();
                })));
            body.Children.Add(new Border { Height = 10 });
            body.Children.Add(Row("Nome contém", "Opcional. Ex.: fatura",
                Field(rule.NameContains, 220, v =>
                {
                    rule.NameContains = v.Trim();
                    if (rule.Enabled && !AutoRules.IsUsable(rule)) rule.Enabled = false;
                    TheApp.RuleChanged();
                    RefreshRules();
                })));
            return body;
        }

        // --- menu "+ Adicionar" ---

        private UIElement BuildAddMenu()
        {
            var host = new Grid();
            _addButton = Btn("+ Adicionar", OpenAddMenu, "PrimaryButton");

            _addSearch = new TextBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) };
            _addSearch.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            _addSearch.SetResourceReference(Control.BackgroundProperty, "Brush.Hover");
            _addSearch.SetResourceReference(Control.BorderBrushProperty, "Brush.CardBorder");
            _addSearch.TextChanged += (_, _) => RefreshAddList();

            _addList = new StackPanel();
            var body = new StackPanel();
            body.Children.Add(_addSearch);
            body.Children.Add(new ScrollViewer { Content = _addList, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var surface = new Border { Width = 300, Padding = new Thickness(8), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = body };
            surface.SetResourceReference(Border.BackgroundProperty, "Brush.Card");
            surface.SetResourceReference(Border.BorderBrushProperty, "Brush.CardBorder");

            _addPopup = new Popup
            {
                PlacementTarget = _addButton,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 6,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = surface
            };
            host.Children.Add(_addButton);
            host.Children.Add(_addPopup);
            return host;
        }

        private void OpenAddMenu()
        {
            if (_addPopup == null || _addButton == null || _addSearch == null) return;
            _addSearch.Text = "";
            RefreshAddList();
            _addPopup.HorizontalOffset = _addButton.ActualWidth - 300; // alinha a borda direita com o botão
            _addPopup.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _addSearch.Focus()));
        }

        private Border MenuRow(string left, string? right, Action onClick, string leftBrush = "Brush.Text", FontWeight? weight = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Text(left, 13.5, weight, leftBrush, wrap: false);
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            if (right != null)
            {
                var r = Text(right, 12, brush: "Brush.VioletText", wrap: false);
                r.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(r, 1);
                g.Children.Add(r);
            }
            var row = new Border { Height = 34, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = g };
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
            row.MouseLeave += (_, _) => { row.ClearValue(Border.BackgroundProperty); row.Background = Brushes.Transparent; };
            row.MouseLeftButtonUp += (_, _) => onClick();
            return row;
        }

        private TextBlock MenuLabel(string text)
        {
            var t = Text(text.ToUpperInvariant(), 10.5, FontWeights.SemiBold, "Brush.Subtle", wrap: false);
            t.Margin = new Thickness(10, 8, 0, 4);
            return t;
        }

        private void RefreshAddList()
        {
            if (_addList == null) return;
            _addList.Children.Clear();
            string q = (_addSearch?.Text ?? "").Trim();
            bool Matches(RuleCatalog.Category c) => q.Length == 0 ||
                c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || c.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase);
            var free = RuleCatalog.All.Where(c => !Cfg.Rules.Any(r => r.Category == c.Id)).ToList();
            var shown = free.Where(Matches).ToList();

            void Group(string title, IEnumerable<RuleCatalog.Category> cats)
            {
                var list = cats.ToList();
                if (list.Count == 0) return;
                _addList.Children.Add(MenuLabel(title));
                foreach (var c in list)
                {
                    var cat = c;
                    var row = MenuRow(cat.Name, "Adicionar", () => AddFromMenu(() => TheApp.AddPresetRule(cat.Id)));
                    row.ToolTip = cat.Description;
                    _addList.Children.Add(row);
                }
            }
            Group("Aplicativos", shown.Where(c => c.FileExts.Length == 0));
            Group("Arquivos", shown.Where(c => c.FileExts.Length > 0));

            if (shown.Count == 0)
                _addList.Children.Add(Text(free.Count == 0 ? "Todos os tipos já foram adicionados." : "Nenhum tipo com esse nome.", 12.5, brush: "Brush.Subtle"));

            var sep = new Border { Height = 1, Margin = new Thickness(2, 6, 2, 6) };
            sep.SetResourceReference(Border.BackgroundProperty, "Brush.CardBorder");
            _addList.Children.Add(sep);
            _addList.Children.Add(MenuRow("Regra própria…", "extensão ou nome", () =>
            {
                if (_addPopup != null) _addPopup.IsOpen = false;
                var r = TheApp.NewRule();
                _ruleEditing.Add(r.Id);
                RefreshRules();
            }));
            if (free.Count > 0 && q.Length == 0)
                _addList.Children.Add(MenuRow("Adicionar todas", null, () => AddFromMenu(() =>
                {
                    foreach (var c in RuleCatalog.All) TheApp.AddPresetRule(c.Id);
                }), "Brush.VioletText", FontWeights.SemiBold));
        }

        /// <summary>Adiciona pelo menu e o mantém aberto (criar a pasta refaz a página; por isso o menu é reaberto).</summary>
        private void AddFromMenu(Action add)
        {
            _reopenAdd = true;
            add();
            RefreshRules();
            RefreshAddList();
            // Se a página não foi refeita, o menu continua aberto e nada precisa reabrir.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _reopenAdd = false));
        }

        private void PageAparencia()
        {
            Page.Children.Add(Header("Aparência", "Vale para o balão das pastas, os flutuantes, os ícones das pastas e esta janela."));

            var wrap = new WrapPanel();

            var left = new StackPanel { Width = 280, Margin = new Thickness(0, 0, 24, 0) };
            left.Children.Add(Divider());
            string[] modes = { "auto", "light", "dark" };
            left.Children.Add(Opt("Tema", "Por padrão o Pastel segue o tema do Windows.",
                Segmented(new[] { "Seguir o Windows", "Claro", "Escuro" }, Math.Max(0, Array.IndexOf(modes, Cfg.ThemeMode)),
                    i => { Cfg.ThemeMode = modes[i]; ConfigStore.Save(); TheApp.ApplyAppearance(); }), inline: false));
            string[] accents = { "windows", "brand" };
            left.Children.Add(Opt("Cor de destaque", "Contornos, realces e a página atual dos balões.",
                Segmented(new[] { "Do Windows", "Violeta do Pastel" }, Math.Max(0, Array.IndexOf(accents, Cfg.AccentMode)),
                    i => { Cfg.AccentMode = accents[i]; ConfigStore.Save(); TheApp.ApplyAppearance(); }), inline: false));
            left.Children.Add(Opt("Animações", "Entrada e saída suaves do balão e troca de página.",
                Switch(Cfg.Animations, v => { Cfg.Animations = v; ConfigStore.Save(); }), last: true));
            wrap.Children.Add(left);

            var right = new StackPanel { Width = 280, Margin = new Thickness(0, 12, 0, 0) };
            var preview = new BalloonPreview();
            var top = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Text("PRÉVIA AO VIVO", 11, FontWeights.SemiBold, "Brush.Subtle", wrap: false);
            label.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(label);
            var replay = Link("↻ Reabrir balão", () => preview.Replay(), "LinkAccent");
            Grid.SetColumn(replay, 1);
            top.Children.Add(replay);
            right.Children.Add(top);
            right.Children.Add(preview);
            wrap.Children.Add(right);

            Page.Children.Add(wrap);
        }

        private void PageGeral()
        {
            Page.Children.Add(Header("Geral", "O básico para o Pastel ficar de boa no seu Windows."));

            Page.Children.Add(SectionLabel("Inicialização", 4));
            Page.Children.Add(Divider());
            Page.Children.Add(Opt("Iniciar com o Windows", "Assim as pastas abrem rápido, sem esperar o Pastel carregar.",
                Switch(App.IsAutoStart(), v => App.SetAutoStart(v)), last: true));

            Page.Children.Add(SectionLabel("Integração com o Windows"));
            Page.Children.Add(Divider());
            Page.Children.Add(Opt("Botão \"Nova pasta\" na área de trabalho", "Aparece no botão direito da área de trabalho (em Mostrar mais opções, no Windows 11).",
                Switch(Cfg.DesktopMenu, v => TheApp.SetDesktopMenu(v))));
            Page.Children.Add(Opt("Menu \"Pastel > Enviar para\"", "Clique com o botão direito em qualquer arquivo ou pasta para mandar para uma pasta ou flutuante.",
                Switch(Cfg.SendToMenu, v => TheApp.SetSendToMenu(v)), last: true));

            Page.Children.Add(SectionLabel("Área de trabalho"));
            Page.Children.Add(Divider());
            Page.Children.Add(Opt("Ocultar pastas vazias", "Esconde da área de trabalho a pasta que ficou sem nenhum item e mostra de novo quando ela receber algo. Pasta recém-criada continua visível até ganhar o primeiro item; pastas espelhadas nunca somem.",
                Switch(Cfg.HideEmptyFolders, v => TheApp.SetHideEmptyFolders(v)), last: true));

            Page.Children.Add(SectionLabel("Atualizações"));
            Page.Children.Add(Divider());
            Page.Children.Add(Opt("Avisar de versões novas", "Procura no GitHub, a cada 12 horas, se saiu uma versão nova. É a única vez que o Pastel usa a internet.",
                Switch(Cfg.CheckUpdates, v => { Cfg.CheckUpdates = v; ConfigStore.Save(); })));
            Page.Children.Add(Opt("Procurar atualização", "Confere agora se há uma versão nova do Pastel.",
                Link("Verificar", async () =>
                {
                    var msg = await TheApp.CheckForUpdateAsync(manual: true);
                    if (msg != null) MessageBox.Show(msg, "Pastel", MessageBoxButton.OK, MessageBoxImage.Information);
                }, "LinkAccent"), last: true));

            Page.Children.Add(SectionLabel("Dados"));
            Page.Children.Add(Divider());
            Page.Children.Add(Opt("Pasta de dados", "Onde o Pastel guarda a configuração e os atalhos que saíram da área de trabalho.",
                Link("Abrir", () => TheApp.OpenDataFolder(), "LinkAccent"), last: true));
        }

        private void PageSobre()
        {
            Page.Children.Add(Header("Sobre", "Quem faz e como o Pastel funciona."));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 22) };
            row.Children.Add(BuildSymbol(72));
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            var pastel = Heading("Pastel");
            pastel.FontSize = 32;
            names.Children.Add(pastel);
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            names.Children.Add(Text($"Versão {v?.Major}.{v?.Minor}.{v?.Build} (beta)", 13, brush: "Brush.Subtle"));
            row.Children.Add(names);
            Page.Children.Add(row);

            var text = new StackPanel { MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left };
            text.Children.Add(Text("A pasta do celular, no seu PC.", 16, FontWeights.SemiBold));
            var para = Text("O Pastel junta seus apps em pastas na área de trabalho. Ainda tá em beta, então pode ter um bug ou outro escondido. Se algo quebrar, o Pastel pede desculpa e o Wictor corrige.", 13.5, brush: "Brush.Subtle");
            para.Margin = new Thickness(0, 8, 0, 0);
            text.Children.Add(para);
            Page.Children.Add(text);

            var lines = new StackPanel { MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 28, 0, 0) };
            lines.Children.Add(Divider());
            lines.Children.Add(Opt("Site", null, Link("wamanadev.github.io/pastel", () => OpenUrl("https://wamanadev.github.io/pastel/"), "LinkAccent")));
            lines.Children.Add(Opt("Reportar um problema", null, Link("Abrir no GitHub", () => OpenUrl("https://github.com/WamanaDev/pastel/issues"), "LinkAccent")));
            lines.Children.Add(Opt("Licença", null, Text("MIT · grátis, sem conta e sem internet", 13, brush: "Brush.Subtle", wrap: false), last: true));
            Page.Children.Add(lines);
        }
    }

    /// <summary>Botão liga/desliga no estilo dos controles do Windows 11.</summary>
    internal sealed class ToggleSwitch : Border
    {
        private readonly Border _thumb;
        public bool IsOn { get; private set; }
        public event Action<bool>? Toggled;

        public ToggleSwitch(bool on)
        {
            Width = 44; Height = 24; CornerRadius = new CornerRadius(12); Cursor = Cursors.Hand;
            _thumb = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            Child = _thumb;
            IsOn = on;
            Update(false);
            MouseLeftButtonUp += (_, _) => { IsOn = !IsOn; Update(true); Toggled?.Invoke(IsOn); };
        }

        private void Update(bool animate)
        {
            Background = IsOn ? new SolidColorBrush(WindowsTheme.Violet)
                              : new SolidColorBrush(WindowsTheme.WithAlpha(WindowsTheme.Text(WindowsTheme.IsLight), 0x40));
            var to = new Thickness(IsOn ? 23 : 3, 0, 0, 0);
            if (animate && ConfigStore.Current.Animations)
                _thumb.BeginAnimation(MarginProperty, new ThicknessAnimation(to, TimeSpan.FromMilliseconds(140))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else
                _thumb.Margin = to;
        }
    }
}
