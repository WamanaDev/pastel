using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Pastel
{
    /// <summary>
    /// Grade de ícones da área de trabalho. O Plasma não oferece um jeito de perguntar o tamanho da célula, então vem
    /// da configuração (Configurações > Aparência) com um padrão razoável; a origem é o canto da tela principal.
    /// </summary>
    internal readonly record struct GridMetrics(int OriginX, int OriginY, int Cx, int Cy)
    {
        public const int DefaultCx = 96, DefaultCy = 104;

        public static GridMetrics Get(Screens? screens)
        {
            var cfg = ConfigStore.Current;
            var wa = screens?.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            return new GridMetrics(wa.X, wa.Y,
                cfg.GridCellWidth >= 40 ? cfg.GridCellWidth : DefaultCx,
                cfg.GridCellHeight >= 40 ? cfg.GridCellHeight : DefaultCy);
        }

        public (int col, int row) CellAt(int x, int y) =>
            ((int)Math.Floor((x - OriginX) / (double)Cx + 0.5), (int)Math.Floor((y - OriginY) / (double)Cy + 0.5));

        public (int x, int y) PixelOf(int col, int row) => (OriginX + col * Cx, OriginY + row * Cy);
    }

    /// <summary>
    /// Barra flutuante de apps, parte da área de trabalho: no fundo das janelas, com tamanho em múltiplos das células
    /// da grade e encaixando nelas ao ser arrastada. O efeito do mouse acontece DENTRO da janela (só o desenho cresce),
    /// então a grade nunca desalinha.
    /// </summary>
    internal sealed class FloatingWindow : Window
    {
        private readonly FloatingModel _m;
        private readonly Border _glass = new();
        private readonly StackPanel _items = new();
        private readonly TextBlock _caption = new();
        private readonly List<(Border tile, ScaleTransform scale)> _tiles = new();
        private bool _moving;
        private PixelPoint _grab;
        private GridMetrics _g;

        public FloatingModel Model => _m;
        private double Slot => _m.IconSize + 16;

        public FloatingWindow(FloatingModel model)
        {
            _m = model;

            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = false;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "Pastel – " + model.Name;

            _items.HorizontalAlignment = HorizontalAlignment.Center;
            _items.VerticalAlignment = VerticalAlignment.Center;
            _items.Background = Brushes.Transparent; // precisa existir para receber o mouse nos vãos
            _items.PointerMoved += (_, e) => ApplyProximity(e.GetPosition(_items));
            _items.PointerExited += (_, _) =>
            {
                if (_m.HoverEffect != "proximity") return;
                foreach (var (tile, scale) in _tiles) { ScaleTo(scale, 1); tile.ZIndex = 0; }
            };

            _caption.HorizontalAlignment = HorizontalAlignment.Center;
            _caption.FontFamily = Ui.Body; _caption.FontSize = 11; _caption.Margin = new Thickness(0, 0, 0, 5);
            _caption.TextTrimming = TextTrimming.CharacterEllipsis; _caption.MaxWidth = 140;

            var inner = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            inner.Children.Add(_items);
            Grid.SetRow(_caption, 1);
            inner.Children.Add(_caption);

            _glass.Margin = new Thickness(4);
            _glass.CornerRadius = new CornerRadius(18);
            _glass.Child = inner;
            Content = _glass;

            _glass.ContextMenu = BuildMenu();
            _glass.PointerPressed += GlassPressed;
            _glass.PointerMoved += GlassMoved;
            _glass.PointerReleased += GlassReleased;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; });
            AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
                if (paths is { Count: > 0 }) Controller.AddPaths(_m, paths);
                e.Handled = true;
            });

            Opened += (_, _) => { MakeDesktopLike(); Relayout(); EnsureAtCell(); }; // reafirma depois de aberta (o KWin aplica o pedido)
        }

        /// <summary>
        /// Abre a barra JÁ na célula certa e já com o estado de janela de área de trabalho. As duas coisas são pedidas
        /// ANTES de abrir: depois, o gerenciador de janelas pode colocar a janela em outro lugar e atender tarde.
        /// </summary>
        public void ShowPlaced()
        {
            Reload();      // monta o conteúdo, calcula o tamanho e a posição (Position vale antes de abrir)
            MakeDesktopLike(initial: true);
            Show();
        }

        /// <summary>No fundo, fora da barra de tarefas e em todas as áreas de trabalho (pedido ao gerenciador de janelas).</summary>
        private void MakeDesktopLike(bool initial = false)
        {
            var handle = TryGetPlatformHandle();
            if (handle == null) return;
            if (initial) X11.SetInitialDesktopState(handle.Handle); // janela ainda fechada: é a propriedade inicial
            else X11.MakeDesktopLike(handle.Handle);                // janela já aberta: pedido ao gerenciador
        }

        // ---------- aparência e conteúdo ----------

        private void ApplyTheme(Palette p)
        {
            var glass = PastelTheme.WithAlpha(PastelTheme.Mix(p.Light ? Colors.White : Color.FromRgb(0x1E, 0x22, 0x2C), p.Accent, 0.16), p.Light ? (byte)0xC8 : (byte)0xB8);
            if (_m.ShowBackground)
            {
                _glass.Background = Palette.B(glass);
                _glass.BorderBrush = Palette.B(PastelTheme.WithAlpha(p.Accent, 0x90));
                _glass.BorderThickness = new Thickness(1.5);
                _caption.Foreground = Palette.B(p.Text);
                _caption.Effect = null;
            }
            else
            {
                _glass.Background = Brushes.Transparent; // continua recebendo o mouse (arrastar)
                _glass.BorderBrush = null;
                _glass.BorderThickness = new Thickness(0);
                _caption.Foreground = Brushes.White;
                _caption.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, OffsetX = 0, OffsetY = 1, Opacity = 0.9 };
            }
            _caption.Text = _m.Name;
        }

        public void Reload()
        {
            var p = new Palette();
            ApplyTheme(p);
            _items.Orientation = _m.Vertical ? Orientation.Vertical : Orientation.Horizontal;
            _items.Children.Clear();
            _tiles.Clear();

            if (_m.Items.Count == 0)
                _items.Children.Add(Ui.Text("Arraste apps pra cá", PastelTheme.WithAlpha(p.Text, 0xA0), 12, wrap: false));
            foreach (var item in _m.Items.ToList()) _items.Children.Add(BuildItem(item, p));
            Relayout();
        }

        private Control BuildItem(ShortcutItem item, Palette p)
        {
            var scale = new ScaleTransform(1, 1);
            var img = new Image
            {
                Source = IconCache.GetBitmap(item, 128), Width = _m.IconSize, Height = _m.IconSize,
                RenderTransform = scale, RenderTransformOrigin = RelativePoint.Center
            };
            RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);

            var tile = new Border
            {
                Width = Slot, Height = Slot, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand), Child = img
            };
            ToolTip.SetTip(tile, item.Name);
            _tiles.Add((tile, scale));

            if (_m.HoverEffect == "focus")
            {
                var hover = Palette.B(PastelTheme.WithAlpha(p.Accent, 0x30));
                tile.PointerEntered += (_, _) => { tile.Background = hover; tile.ZIndex = 5; ScaleTo(scale, 1.4); };
                tile.PointerExited += (_, _) => { tile.Background = Brushes.Transparent; tile.ZIndex = 0; ScaleTo(scale, 1); };
            }

            Point? down = null;
            tile.PointerPressed += (_, e) =>
            {
                e.Handled = true; // não arrasta a janela a partir de um app
                if (e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) down = e.GetPosition(this);
            };
            tile.PointerExited += (_, _) => down = null;
            tile.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || down == null) return;
                down = null;
                Controller.Launch(item);
            };
            tile.PointerMoved += (_, e) =>
            {
                if (down == null || !e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
                var d = e.GetPosition(this) - down.Value;
                if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
                down = null;
                StartDrag(item, e);
            };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Abrir local do arquivo" };
            open.Click += (_, _) => AppLauncher.ShowInFileManager(item.Path);
            var remove = new MenuItem { Header = "Remover do flutuante" };
            remove.Click += (_, _) => Controller.RemoveItem(_m, item);
            menu.Items.Add(open);
            menu.Items.Add(remove);
            tile.ContextMenu = menu;
            return tile;
        }

        private async void StartDrag(ShortcutItem item, PointerEventArgs e)
        {
            bool managed = item.OriginalPath != null && File.Exists(item.Path);
            var data = new DataObject();
            try
            {
                var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(item.Path));
                if (file != null) data.Set(DataFormats.Files, new[] { file });
                var effect = await DragDrop.DoDragDrop(e, data, managed ? DragDropEffects.Move : DragDropEffects.Link);
                if (managed && effect.HasFlag(DragDropEffects.Move)) Controller.ItemMovedOut(_m, item);
            }
            catch { }
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            MenuItem Item(string header, Action action, bool check = false) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => action(); return mi; }

            menu.Items.Add(Item("Adicionar apps…", () => Controller.PickFiles(_m)));
            menu.Items.Add(Item("Renomear flutuante", () => Controller.RenameFloat(_m)));

            var orient = new MenuItem { Header = "Orientação" };
            orient.Items.Add(Item("Horizontal", () => { _m.Vertical = false; Controller.FloatChanged(_m); }));
            orient.Items.Add(Item("Vertical", () => { _m.Vertical = true; Controller.FloatChanged(_m); }));
            menu.Items.Add(orient);

            var size = new MenuItem { Header = "Tamanho dos ícones" };
            foreach (var (label, px) in new[] { ("Mínimo", 24), ("Pequeno", 32), ("Médio", 40), ("Grande", 52) })
            {
                int v = px;
                size.Items.Add(Item(label, () => { _m.IconSize = v; Controller.FloatChanged(_m); }));
            }
            menu.Items.Add(size);

            menu.Items.Add(Item("Alternar fundo de vidro", () => { _m.ShowBackground = !_m.ShowBackground; Controller.FloatChanged(_m); }));
            menu.Items.Add(Item("Alternar nome", () => { _m.ShowName = !_m.ShowName; Controller.FloatChanged(_m); }));

            var fx = new MenuItem { Header = "Efeito ao passar o mouse" };
            fx.Items.Add(Item("Nenhum", () => { _m.HoverEffect = "none"; Controller.FloatChanged(_m); }));
            fx.Items.Add(Item("Destaque no ícone", () => { _m.HoverEffect = "focus"; Controller.FloatChanged(_m); }));
            fx.Items.Add(Item("Quanto mais perto, maior", () => { _m.HoverEffect = "proximity"; Controller.FloatChanged(_m); }));
            menu.Items.Add(fx);

            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Ocultar", () => Controller.SetFloatVisible(_m, false)));
            menu.Items.Add(Item("Excluir flutuante", () => Controller.DeleteFloat(_m)));
            return menu;
        }

        // ---------- efeitos do mouse ----------

        private static void ScaleTo(ScaleTransform s, double to)
        {
            double from = s.ScaleX;
            Anim.Run(130, t => s.ScaleX = s.ScaleY = from + (to - from) * t);
        }

        /// <summary>Estilo dock: cada ícone cresce conforme a distância até o mouse (curva suave).</summary>
        public void ApplyProximity(Point mouse)
        {
            if (_m.HoverEffect != "proximity" || _tiles.Count == 0) return;
            double reach = Slot * 1.1;
            foreach (var (tile, scale) in _tiles)
            {
                var c = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), _items) ?? default;
                double d = Math.Sqrt((c.X - mouse.X) * (c.X - mouse.X) + (c.Y - mouse.Y) * (c.Y - mouse.Y));
                double k = 1 + 0.75 * Math.Exp(-Math.Pow(d / reach, 2));
                scale.ScaleX = scale.ScaleY = k; // acompanha o mouse sem atraso
                tile.ZIndex = (int)(k * 100);
            }
        }

        // ---------- tamanho e posição na grade ----------

        public void Relayout()
        {
            _g = GridMetrics.Get(Screens);
            double s = RenderScaling;

            int count = Math.Max(_m.Items.Count, 2);              // vazio: espaço para a dica
            double lengthPx = (count * Slot + 20) * s;
            int cellSize = _m.Vertical ? _g.Cy : _g.Cx;
            int cells = Math.Max(1, (int)Math.Ceiling(lengthPx / cellSize));

            int wPx = _m.Vertical ? _g.Cx : cells * _g.Cx;
            int hPx = _m.Vertical ? cells * _g.Cy : _g.Cy;
            Width = wPx / s;
            Height = hPx / s;

            double crossDip = (_m.Vertical ? wPx : hPx) / s - 8;
            _caption.IsVisible = _m.ShowName && crossDip >= Slot + 22;

            if (_m.Col == int.MinValue || _m.Row == int.MinValue)
            {
                var wa = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
                (_m.Col, _m.Row) = _g.CellAt(wa.X + wa.Width / 2 - wPx / 2, wa.Y + wa.Height / 2 - hPx / 2);
                ConfigStore.Save();
            }
            var (x, y) = _g.PixelOf(_m.Col, _m.Row);
            Position = new PixelPoint(x, y);
        }

        /// <summary>
        /// Alguns gerenciadores de janelas colocam a janela onde querem ao abrir e só depois atendem o pedido de posição.
        /// Confere a posição real algumas vezes e, se não bater com a célula, pede de novo.
        /// </summary>
        private void EnsureAtCell(int attempt = 0)
        {
            if (attempt >= 8) return;
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (_moving || !IsVisible || _m.Col == int.MinValue) return;
                var (x, y) = GridMetrics.Get(Screens).PixelOf(_m.Col, _m.Row);
                if (Math.Abs(Position.X - x) > 1 || Math.Abs(Position.Y - y) > 1)
                {
                    if (GuiSelfTest.Active) Console.WriteLine($"   [dock] reafirmando célula: real={Position} esperado=({x},{y}) tentativa {attempt}");
                    Position = new PixelPoint(x, y);
                }
                EnsureAtCell(attempt + 1);
            }, TimeSpan.FromMilliseconds(250));
        }

        // ---------- arrastar a janela, encaixando célula por célula ----------

        private void GlassPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Handled || !e.GetCurrentPoint(_glass).Properties.IsLeftButtonPressed) return;
            _g = GridMetrics.Get(Screens);
            var cur = this.PointToScreen(e.GetPosition(this));
            _grab = new PixelPoint(cur.X - Position.X, cur.Y - Position.Y);
            _moving = true;
            e.Pointer.Capture(_glass);
            e.Handled = true;
        }

        private void GlassMoved(object? sender, PointerEventArgs e)
        {
            if (!_moving) return;
            var cur = this.PointToScreen(e.GetPosition(this));
            var (c, r) = _g.CellAt(cur.X - _grab.X, cur.Y - _grab.Y);
            if (c == _m.Col && r == _m.Row) return;
            _m.Col = c;
            _m.Row = r;
            var (x, y) = _g.PixelOf(c, r);
            Position = new PixelPoint(x, y);
        }

        private void GlassReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_moving) return;
            _moving = false;
            e.Pointer.Capture(null);
            ConfigStore.Save();
        }
    }
}
