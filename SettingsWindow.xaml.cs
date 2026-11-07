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

namespace Pastel
{
    /// <summary>Janela de configurações: pastas, flutuantes, aparência e geral. Segue a identidade do Pastel.</summary>
    public partial class SettingsWindow : Window
    {
        private static readonly string[] Pages = { "Pastas", "Flutuantes", "Aparência", "Geral", "Sobre" };
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

        private StackPanel Header(string title, string subtitle, Button? action = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            var top = new DockPanel();
            if (action != null) { DockPanel.SetDock(action, Dock.Right); top.Children.Add(action); }
            top.Children.Add(Heading(title));
            sp.Children.Add(top);
            sp.Children.Add(Text(subtitle, 13, brush: "Brush.Subtle"));
            return sp;
        }

        private UIElement AppList(IItemOwner owner)
        {
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (owner.Items.Count == 0) list.Children.Add(Text("Nenhum app ainda. Arraste atalhos para cá.", 12, brush: "Brush.Subtle"));
            foreach (var item in owner.Items.ToList())
            {
                var it = item;
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var img = new Image { Width = 24, Height = 24, Source = IconService.GetStyled(it.Path, 64), Margin = new Thickness(0, 0, 10, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                row.Children.Add(img);
                var name = Text(it.Name, 13, wrap: false);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var rm = Btn("Remover", () => TheApp.RemoveItem(owner, it));
                Grid.SetColumn(rm, 2);
                row.Children.Add(rm);
                list.Children.Add(row);
            }
            return list;
        }

        /// <summary>Cartão de uma pasta ou flutuante, com a lista de apps que abre e fecha.</summary>
        private Border OwnerCard(IItemOwner owner, IEnumerable<UIElement> controls, IEnumerable<UIElement>? extra = null)
        {
            var root = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prev = Preview(owner.Items, 38);
            prev.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            top.Children.Add(prev);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Text(owner.Name, 15, FontWeights.SemiBold, wrap: false));
            int n = owner.Items.Count;
            info.Children.Add(Text(n == 1 ? "1 app" : $"{n} apps", 12, brush: "Brush.Subtle"));
            Grid.SetColumn(info, 1);
            top.Children.Add(info);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in controls) buttons.Children.Add(c);
            bool open = _expanded.Contains(owner.Id);
            buttons.Children.Add(Btn(open ? "Esconder apps" : "Ver apps", () =>
            {
                if (!_expanded.Remove(owner.Id)) _expanded.Add(owner.Id);
                BuildPage();
            }));
            Grid.SetColumn(buttons, 2);
            top.Children.Add(buttons);
            root.Children.Add(top);

            if (extra != null)
                foreach (var e in extra) root.Children.Add(e);
            if (open) root.Children.Add(AppList(owner));
            return Card(root);
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
                case "Aparência": PageAparencia(); break;
                case "Geral": PageGeral(); break;
                default: PageSobre(); break;
            }
            _building = false;
        }

        private void PagePastas()
        {
            Page.Children.Add(Header("Pastas", "Cada pasta é um ícone na sua área de trabalho. Dê dois cliques nele para abrir os apps.",
                Btn("Nova pasta", () => { TheApp.NewGroup(); BuildPage(); }, "PrimaryButton")));

            if (Cfg.Folders.Count == 0)
            {
                Page.Children.Add(Card(Text("Nenhuma pasta ainda. Clique em Nova pasta, ou use o botão direito na área de trabalho.", 13, brush: "Brush.Subtle")));
                return;
            }
            foreach (var f in Cfg.Folders.ToList())
            {
                var folder = f;
                Page.Children.Add(OwnerCard(folder, new UIElement[]
                {
                    Btn("Abrir", () => TheApp.ShowPopupCentered(folder)),
                    Btn("Renomear", () => TheApp.RenameGroup(folder)),
                    Btn("Excluir", () => TheApp.DeleteGroup(folder), "DangerButton")
                }));
            }
        }

        private void PageFlutuantes()
        {
            Page.Children.Add(Header("Flutuantes", "Barras de apps soltas na área de trabalho. Elas encaixam na grade de ícones do Windows, como um atalho comum.",
                Btn("Novo flutuante", () => { TheApp.NewFloat(); BuildPage(); }, "PrimaryButton")));

            if (Cfg.Floats.Count == 0)
            {
                Page.Children.Add(Card(Text("Nenhum flutuante ainda. Clique em Novo flutuante, ou envie um app pelo botão direito (Pastel > Enviar para flutuante).", 13, brush: "Brush.Subtle")));
                return;
            }
            foreach (var m in Cfg.Floats.ToList())
            {
                var fl = m;
                var options = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
                options.Children.Add(Row("Mostrar na área de trabalho", null, Switch(fl.Visible, v => TheApp.SetFloatVisible(fl, v))));
                options.Children.Add(new Border { Height = 10 });
                options.Children.Add(Row("Orientação", null, Segmented(new[] { "Horizontal", "Vertical" }, fl.Vertical ? 1 : 0,
                    i => { fl.Vertical = i == 1; TheApp.FloatChanged(fl); })));
                options.Children.Add(new Border { Height = 10 });
                int[] sizes = { 24, 32, 40, 52 };
                int sel = Array.IndexOf(sizes, fl.IconSize); if (sel < 0) sel = 2;
                options.Children.Add(Row("Tamanho dos ícones", null, Segmented(new[] { "Mínimo", "Pequeno", "Médio", "Grande" }, sel,
                    i => { fl.IconSize = sizes[i]; TheApp.FloatChanged(fl); })));
                options.Children.Add(new Border { Height = 10 });
                options.Children.Add(Row("Fundo", "Sem fundo, os ícones ficam soltos na área de trabalho.",
                    Segmented(new[] { "Com vidro", "Sem fundo" }, fl.ShowBackground ? 0 : 1,
                        i => { fl.ShowBackground = i == 0; TheApp.FloatChanged(fl); })));
                options.Children.Add(new Border { Height = 10 });
                options.Children.Add(Row("Mostrar nome", null, Switch(fl.ShowName, v => { fl.ShowName = v; TheApp.FloatChanged(fl); })));
                options.Children.Add(new Border { Height = 10 });
                string[] fx = { "none", "focus", "proximity" };
                options.Children.Add(Row("Efeito ao passar o mouse", "Destaque: o ícone sob o mouse cresce. Aproximação: quanto mais perto do mouse, maior.",
                    Segmented(new[] { "Nenhum", "Destaque", "Aproximação" }, Math.Max(0, Array.IndexOf(fx, fl.HoverEffect)),
                        i => { fl.HoverEffect = fx[i]; TheApp.FloatChanged(fl); })));

                Page.Children.Add(OwnerCard(fl, new UIElement[]
                {
                    Btn("Renomear", () => TheApp.RenameFloat(fl)),
                    Btn("Excluir", () => TheApp.DeleteFloat(fl), "DangerButton")
                }, new UIElement[] { options }));
            }
        }

        private void PageAparencia()
        {
            Page.Children.Add(Header("Aparência", "Vale para o balão das pastas, os flutuantes, os ícones das pastas e esta janela."));

            string[] modes = { "auto", "light", "dark" };
            Page.Children.Add(Card(Row("Tema", "Por padrão o Pastel segue o tema do Windows.",
                Segmented(new[] { "Seguir o Windows", "Claro", "Escuro" }, Math.Max(0, Array.IndexOf(modes, Cfg.ThemeMode)),
                    i => { Cfg.ThemeMode = modes[i]; ConfigStore.Save(); TheApp.ApplyAppearance(); }))));

            string[] accents = { "windows", "brand" };
            Page.Children.Add(Card(Row("Cor de destaque", "Contornos, realces e a página atual dos balões.",
                Segmented(new[] { "Do Windows", "Violeta do Pastel" }, Math.Max(0, Array.IndexOf(accents, Cfg.AccentMode)),
                    i => { Cfg.AccentMode = accents[i]; ConfigStore.Save(); TheApp.ApplyAppearance(); }))));

            Page.Children.Add(Card(Row("Animações", "Entrada e saída suaves do balão e troca de página.",
                Switch(Cfg.Animations, v => { Cfg.Animations = v; ConfigStore.Save(); }))));
        }

        private void PageGeral()
        {
            Page.Children.Add(Header("Geral", "O básico para o Pastel ficar de boa no seu Windows."));

            Page.Children.Add(Card(Row("Iniciar com o Windows", "Assim as pastas abrem rápido, sem esperar o Pastel carregar.",
                Switch(App.IsAutoStart(), v => App.SetAutoStart(v)))));

            Page.Children.Add(Card(Row("Botão \"Nova pasta\" na área de trabalho", "Aparece no botão direito da área de trabalho (em Mostrar mais opções, no Windows 11).",
                Switch(Cfg.DesktopMenu, v => TheApp.SetDesktopMenu(v)))));

            Page.Children.Add(Card(Row("Menu \"Pastel > Enviar para\"", "Clique com o botão direito em qualquer arquivo ou pasta para mandar para uma pasta ou flutuante.",
                Switch(Cfg.SendToMenu, v => TheApp.SetSendToMenu(v)))));

            Page.Children.Add(Card(Row("Avisar de versões novas", "Procura no GitHub, a cada 12 horas, se saiu uma versão nova. É a única vez que o Pastel usa a internet.",
                Switch(Cfg.CheckUpdates, v => { Cfg.CheckUpdates = v; ConfigStore.Save(); }))));

            Page.Children.Add(Card(Row("Procurar atualização", "Confere agora se há uma versão nova do Pastel.",
                Btn("Verificar", async () =>
                {
                    var msg = await TheApp.CheckForUpdateAsync(manual: true);
                    if (msg != null) MessageBox.Show(msg, "Pastel", MessageBoxButton.OK, MessageBoxImage.Information);
                }))));

            Page.Children.Add(Card(Row("Pasta de dados", "Onde o Pastel guarda a configuração e os atalhos que saíram da área de trabalho.",
                Btn("Abrir", () => TheApp.OpenDataFolder()))));
        }

        private void PageSobre()
        {
            var sp = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(BuildSymbol(64));
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            names.Children.Add(Heading("Pastel"));
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            names.Children.Add(Text($"Versão {v?.Major}.{v?.Minor}.{v?.Build} (beta)", 12, brush: "Brush.Subtle"));
            row.Children.Add(names);
            sp.Children.Add(row);
            sp.Children.Add(Text("A pasta do celular, no seu PC.", 15, FontWeights.SemiBold));
            sp.Children.Add(new Border { Height = 6 });
            sp.Children.Add(Text("O Pastel junta seus apps em pastas na área de trabalho. Ainda tá em beta, então pode ter um bug ou outro escondido. Se algo quebrar, o Pastel pede desculpa e o Wictor corrige.", 13, brush: "Brush.Subtle"));
            sp.Children.Add(new Border { Height = 10 });
            sp.Children.Add(Text("Feito por Wictor Pamplona.", 13, FontWeights.SemiBold));
            Page.Children.Add(Header("Sobre", "Quem faz e como o Pastel funciona."));
            Page.Children.Add(Card(sp));
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
