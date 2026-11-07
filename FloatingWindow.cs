using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pastel
{
    /// <summary>
    /// Barra flutuante de apps, parte da área de trabalho: fica no fundo das janelas, tem tamanho em
    /// múltiplos das células da grade de ícones do Windows e encaixa nelas ao ser arrastada. O efeito
    /// ao passar o mouse acontece DENTRO da janela, então a grade nunca é desalinhada.
    /// </summary>
    internal sealed class FloatingWindow : Window
    {
        private readonly FloatingModel _m;
        private readonly Border _glass = new();
        private readonly StackPanel _items = new();
        private readonly TextBlock _caption = new();
        private DesktopGrid.Metrics _g;

        private bool _movingWindow;
        private (int x, int y) _grab;
        private readonly System.Collections.Generic.List<(Border tile, ScaleTransform scale)> _tiles = new();

        private static App TheApp => (App)Application.Current;
        public FloatingModel Model => _m;

        public FloatingWindow(FloatingModel model)
        {
            _m = model;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            Topmost = false;
            AllowDrop = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Title = "Pastel – " + model.Name;

            _items.HorizontalAlignment = HorizontalAlignment.Center;
            _items.VerticalAlignment = VerticalAlignment.Center;
            _items.Background = Brushes.Transparent; // precisa existir para receber o mouse nos vãos
            _items.MouseMove += Items_MouseMove;
            _items.MouseLeave += Items_MouseLeave;
            _caption.HorizontalAlignment = HorizontalAlignment.Center;
            _caption.FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI");
            _caption.FontSize = 11;
            _caption.Margin = new Thickness(0, 0, 0, 5);
            _caption.TextTrimming = TextTrimming.CharacterEllipsis;
            _caption.MaxWidth = 140;

            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.Children.Add(_items);
            Grid.SetRow(_caption, 1);
            inner.Children.Add(_caption);

            _glass.Margin = new Thickness(4);
            _glass.CornerRadius = new CornerRadius(18);
            _glass.BorderThickness = new Thickness(1.5);
            _glass.Child = inner;
            Content = _glass;

            _glass.ContextMenu = BuildMenu();
            _glass.MouseLeftButtonDown += Glass_MouseDown;
            _glass.MouseMove += Glass_MouseMove;
            _glass.MouseLeftButtonUp += Glass_MouseUp;
            DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
            Drop += (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files) TheApp.AddPaths(_m, files);
                e.Handled = true;
            };

            SourceInitialized += (_, _) => { DesktopWindow.MakeDesktopLike(this); Reload(); };
            ContentRendered += (_, _) => Relayout();
        }

        // ---------- aparência e conteúdo ----------

        private void ApplyTheme()
        {
            bool light = WindowsTheme.IsLight;
            var accent = WindowsTheme.Visible(WindowsTheme.Accent, light);
            var baseColor = light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1E, 0x22, 0x2C);
            var glass = WindowsTheme.WithAlpha(WindowsTheme.Mix(baseColor, accent, 0.16), light ? (byte)0xC8 : (byte)0xB8);
            if (_m.ShowBackground)
            {
                _glass.Background = new SolidColorBrush(glass);
                _glass.BorderBrush = new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0x90));
                _glass.BorderThickness = new Thickness(1.5);
                _caption.Foreground = new SolidColorBrush(WindowsTheme.Text(light));
                _caption.Effect = null;
            }
            else
            {
                // Sem fundo: os ícones ficam soltos na área de trabalho; o nome ganha sombra, como nos ícones do Windows.
                _glass.Background = Brushes.Transparent; // continua recebendo o mouse (arrastar)
                _glass.BorderBrush = null;
                _glass.BorderThickness = new Thickness(0);
                _caption.Foreground = Brushes.White;
                _caption.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
            }
            _caption.Text = _m.Name;
        }

        public void Reload()
        {
            ApplyTheme();
            _items.Orientation = _m.Vertical ? Orientation.Vertical : Orientation.Horizontal;
            _items.Children.Clear();
            _tiles.Clear();

            if (_m.Items.Count == 0)
            {
                _items.Children.Add(new TextBlock
                {
                    Text = "Arraste apps pra cá",
                    Foreground = new SolidColorBrush(WindowsTheme.WithAlpha(WindowsTheme.Text(WindowsTheme.IsLight), 0xA0)),
                    FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI"),
                    FontSize = 12,
                    Margin = new Thickness(8)
                });
            }
            foreach (var item in _m.Items.ToList()) _items.Children.Add(BuildItem(item));
            Relayout();
        }

        private double Slot => _m.IconSize + 16;

        private UIElement BuildItem(ShortcutItem item)
        {
            bool light = WindowsTheme.IsLight;
            var accent = WindowsTheme.Visible(WindowsTheme.Accent, light);

            var scale = new ScaleTransform(1, 1);
            var img = new Image
            {
                Source = IconService.GetStyled(item.Path, 128),
                Width = _m.IconSize,
                Height = _m.IconSize,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var tile = new Border
            {
                Width = Slot,
                Height = Slot,
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = item.Name,
                Child = img
            };

            // Efeitos ao passar o mouse. Tudo acontece DENTRO da janela (só o desenho cresce), então a grade
            // do Windows nunca é desalinhada.
            _tiles.Add((tile, scale));
            if (_m.HoverEffect == "focus")
            {
                // o ícone sob o mouse fica maior
                var hover = new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0x30));
                tile.MouseEnter += (_, _) => { tile.Background = hover; Panel.SetZIndex(tile, 5); ScaleTo(scale, 1.4); };
                tile.MouseLeave += (_, _) => { tile.Background = Brushes.Transparent; Panel.SetZIndex(tile, 0); ScaleTo(scale, 1); };
            }
            // "proximity": quem calcula é o painel (Items_MouseMove); "none": nenhum efeito.

            Point? downPos = null;
            tile.PreviewMouseLeftButtonDown += (_, e) => downPos = e.GetPosition(this);
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true; // não arrasta a janela a partir de um app
            tile.MouseLeftButtonUp += (_, _) => { if (downPos != null) Launcher.Open(item); downPos = null; };
            tile.MouseLeave += (_, _) => downPos = null;
            tile.MouseMove += (_, e) =>
            {
                if (downPos == null || e.LeftButton != MouseButtonState.Pressed) return;
                var d = e.GetPosition(this) - downPos.Value;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                downPos = null;
                StartDrag(item, tile);
            };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Abrir local do arquivo" };
            open.Click += (_, _) => Launcher.ShowInExplorer(item);
            var remove = new MenuItem { Header = "Remover do flutuante" };
            remove.Click += (_, _) => TheApp.RemoveItem(_m, item);
            menu.Items.Add(open);
            menu.Items.Add(remove);
            tile.ContextMenu = menu;
            return tile;
        }

        private void StartDrag(ShortcutItem item, UIElement source)
        {
            // Mesma regra do popup: atalho gerenciado MOVE (volta para onde soltar); o resto só vira atalho.
            bool managed = item.OriginalPath != null && System.IO.File.Exists(item.Path);
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            var effect = DragDrop.DoDragDrop(source, data, managed ? DragDropEffects.Move : DragDropEffects.Link);
            if (managed && effect.HasFlag(DragDropEffects.Move)) TheApp.ItemMovedOut(_m, item);
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();

            var add = new MenuItem { Header = "Adicionar apps…" };
            add.Click += (_, _) => TheApp.PickFiles(_m);
            var rename = new MenuItem { Header = "Renomear flutuante" };
            rename.Click += (_, _) => TheApp.RenameFloat(_m);

            var orient = new MenuItem { Header = "Orientação" };
            var horiz = new MenuItem { Header = "Horizontal", IsCheckable = true };
            var vert = new MenuItem { Header = "Vertical", IsCheckable = true };
            horiz.Click += (_, _) => { _m.Vertical = false; TheApp.FloatChanged(_m); };
            vert.Click += (_, _) => { _m.Vertical = true; TheApp.FloatChanged(_m); };
            orient.Items.Add(horiz);
            orient.Items.Add(vert);
            orient.SubmenuOpened += (_, _) => { horiz.IsChecked = !_m.Vertical; vert.IsChecked = _m.Vertical; };

            var size = new MenuItem { Header = "Tamanho dos ícones" };
            foreach (var (label, px) in new[] { ("Mínimo", 24), ("Pequeno", 32), ("Médio", 40), ("Grande", 52) })
            {
                var mi = new MenuItem { Header = label, IsCheckable = true };
                int v = px;
                mi.Click += (_, _) => { _m.IconSize = v; TheApp.FloatChanged(_m); };
                size.Items.Add(mi);
            }
            size.SubmenuOpened += (_, _) =>
            {
                int[] px = { 24, 32, 40, 52 };
                for (int i = 0; i < size.Items.Count; i++) ((MenuItem)size.Items[i]).IsChecked = _m.IconSize == px[i];
            };

            var bg = new MenuItem { Header = "Fundo de vidro", IsCheckable = true };
            bg.Click += (_, _) => { _m.ShowBackground = bg.IsChecked; TheApp.FloatChanged(_m); };
            var name = new MenuItem { Header = "Mostrar nome", IsCheckable = true };
            name.Click += (_, _) => { _m.ShowName = name.IsChecked; TheApp.FloatChanged(_m); };

            var fx = new MenuItem { Header = "Efeito ao passar o mouse" };
            string[] fxKeys = { "none", "focus", "proximity" };
            string[] fxLabels = { "Nenhum", "Destaque no ícone", "Quanto mais perto, maior" };
            for (int i = 0; i < fxKeys.Length; i++)
            {
                string key = fxKeys[i];
                var mi = new MenuItem { Header = fxLabels[i], IsCheckable = true };
                mi.Click += (_, _) => { _m.HoverEffect = key; TheApp.FloatChanged(_m); };
                fx.Items.Add(mi);
            }
            fx.SubmenuOpened += (_, _) =>
            {
                for (int i = 0; i < fxKeys.Length; i++) ((MenuItem)fx.Items[i]).IsChecked = _m.HoverEffect == fxKeys[i];
            };
            menu.Opened += (_, _) => { bg.IsChecked = _m.ShowBackground; name.IsChecked = _m.ShowName; };

            var hide = new MenuItem { Header = "Ocultar" };
            hide.Click += (_, _) => TheApp.SetFloatVisible(_m, false);
            var delete = new MenuItem { Header = "Excluir flutuante" };
            delete.Click += (_, _) => TheApp.DeleteFloat(_m);

            menu.Items.Add(add);
            menu.Items.Add(rename);
            menu.Items.Add(orient);
            menu.Items.Add(size);
            menu.Items.Add(bg);
            menu.Items.Add(name);
            menu.Items.Add(fx);
            menu.Items.Add(new Separator());
            menu.Items.Add(hide);
            menu.Items.Add(delete);
            return menu;
        }

        // ---------- efeitos do mouse ----------

        private static void ScaleTo(ScaleTransform s, double to)
        {
            if (!ConfigStore.Current.Animations)
            {
                s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                s.ScaleX = s.ScaleY = to;
                return;
            }
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>Estilo dock: cada ícone cresce conforme a distância até o mouse (curva suave).</summary>
        private void Items_MouseMove(object sender, MouseEventArgs e) => ApplyProximity(e.GetPosition(_items));

        private void ApplyProximity(Point mouse)
        {
            if (_m.HoverEffect != "proximity" || _tiles.Count == 0) return;
            double reach = Slot * 1.1;
            foreach (var (tile, scale) in _tiles)
            {
                var c = tile.TranslatePoint(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2), _items);
                double d = Math.Sqrt((c.X - mouse.X) * (c.X - mouse.X) + (c.Y - mouse.Y) * (c.Y - mouse.Y));
                double k = 1 + 0.75 * Math.Exp(-Math.Pow(d / reach, 2));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); // acompanha o mouse sem atraso
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = k;
                Panel.SetZIndex(tile, (int)(k * 100));
            }
        }

        private void Items_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_m.HoverEffect != "proximity") return;
            foreach (var (tile, scale) in _tiles) { ScaleTo(scale, 1); Panel.SetZIndex(tile, 0); }
        }

        // ---------- tamanho e posição na grade ----------

        /// <summary>Tamanho em múltiplos das células e posição na célula guardada.</summary>
        public void Relayout()
        {
            var hwndReady = new System.Windows.Interop.WindowInteropHelper(this).Handle != IntPtr.Zero;
            if (!hwndReady) return;

            _g = DesktopGrid.Get();
            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

            int count = Math.Max(_m.Items.Count, 2);              // vazio: espaço para a dica
            double lengthPx = (count * Slot + 20) * (_m.Vertical ? sy : sx);
            int cellSize = _m.Vertical ? _g.Cy : _g.Cx;
            int cells = Math.Max(1, (int)Math.Ceiling(lengthPx / cellSize));

            int wPx = _m.Vertical ? _g.Cx : cells * _g.Cx;
            int hPx = _m.Vertical ? cells * _g.Cy : _g.Cy;
            Width = wPx / sx;
            Height = hPx / sy;

            // sem altura/largura sobrando para o nome, esconde a legenda
            double crossDip = (_m.Vertical ? wPx / sx : hPx / sy) - 8;
            _caption.Visibility = _m.ShowName && crossDip >= Slot + 22 ? Visibility.Visible : Visibility.Collapsed;

            if (_m.Col == int.MinValue || _m.Row == int.MinValue)
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
                (_m.Col, _m.Row) = DesktopGrid.CellAt(_g, wa.Left + wa.Width / 2 - wPx / 2, wa.Top + wa.Height / 2 - hPx / 2);
                ConfigStore.Save();
            }
            var (x, y) = DesktopGrid.PixelOf(_g, _m.Col, _m.Row);
            DesktopWindow.MoveTo(this, x, y);
        }

        // ---------- arrastar a janela, encaixando célula por célula ----------

        private void Glass_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            _g = DesktopGrid.Get();
            var cur = DesktopWindow.CursorPixels();
            var pos = DesktopWindow.PositionPixels(this);
            _grab = (cur.x - pos.x, cur.y - pos.y);
            _movingWindow = true;
            _glass.CaptureMouse();
            e.Handled = true;
        }

        private void Glass_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_movingWindow) return;
            var cur = DesktopWindow.CursorPixels();
            var (c, r) = DesktopGrid.CellAt(_g, cur.x - _grab.x, cur.y - _grab.y);
            if (c == _m.Col && r == _m.Row) return;
            _m.Col = c;
            _m.Row = r;
            var (x, y) = DesktopGrid.PixelOf(_g, c, r);
            DesktopWindow.MoveTo(this, x, y);
        }

        private void Glass_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_movingWindow) return;
            _movingWindow = false;
            _glass.ReleaseMouseCapture();
            ConfigStore.Save();
        }
    }
}
