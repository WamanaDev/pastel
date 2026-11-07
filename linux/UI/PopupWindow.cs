using System;
using System.ComponentModel;
using System.IO;
using Path = Avalonia.Controls.Shapes.Path;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Pastel
{
    /// <summary>
    /// Balão com os apps da pasta, estilo pasta do Android: ponta apontando para o ícone da pasta, páginas como
    /// no menu de aplicativos (roda do mouse, setas, pontinhos) e entrada/saída suaves.
    /// </summary>
    internal sealed class PopupWindow : Window
    {
        private const double TailHeight = 11, TailHalfBase = 11, Radius = 16;
        private const int CellWidth = 90, RowsPerPage = 3;

        private readonly FolderModel _folder;
        private readonly PixelRect _anchor;                 // ícone da pasta na tela, em pixels
        private readonly Palette _p = new();
        private bool _tailTop = true, _closing, _closeDone, _placed, _dragging;
        private double _tailX = 100;                        // posição da ponta dentro do balão (DIP)
        private int _page, _pageCount = 1;
        private double _pageWidth;

        private readonly Grid _root = new();
        private readonly Path _bubble = new();
        private readonly StackPanel _content = new();
        private readonly TextBlock _header = new(), _empty = new();
        private readonly Canvas _viewport = new();
        private readonly StackPanel _pages = new() { Orientation = Orientation.Horizontal };
        private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        private readonly TranslateTransform _pagesShift = new(), _slide = new();

        public PopupWindow(FolderModel folder, PixelRect anchor)
        {
            _folder = folder;
            _anchor = anchor;

            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0; // aparece pela animação de entrada

            // fundo do balão (o desenho é feito em UpdateBubble, com a ponta no lugar certo)
            _bubble.StrokeThickness = 1;
            _bubble.HorizontalAlignment = HorizontalAlignment.Left;
            _bubble.VerticalAlignment = VerticalAlignment.Top;
            _bubble.Fill = Palette.B(PastelTheme.WithAlpha(_p.Surface, _p.Light ? (byte)0xE8 : (byte)0xE2));
            _bubble.Stroke = Palette.B(PastelTheme.WithAlpha(_p.Text, 0x2A));
            _bubble.Effect = new DropShadowEffect { BlurRadius = 24, OffsetY = 4, Opacity = 0.38, Color = Colors.Black };

            _header.FontFamily = Ui.Title; _header.FontSize = 14; _header.FontWeight = FontWeight.SemiBold;
            _header.HorizontalAlignment = HorizontalAlignment.Center; _header.Margin = new Thickness(0, 0, 0, 10);
            _header.Foreground = Palette.B(_p.Text);

            _empty.FontFamily = Ui.Body; _empty.FontSize = 12; _empty.TextAlignment = TextAlignment.Center; _empty.TextWrapping = TextWrapping.Wrap;
            _empty.MaxWidth = 220; _empty.Margin = new Thickness(0, 8); _empty.Text = "Arraste apps pra cá ou pro ícone da pasta.";
            _empty.Foreground = Palette.B(PastelTheme.WithAlpha(_p.Text, 0xA6));

            _viewport.ClipToBounds = true;
            _viewport.HorizontalAlignment = HorizontalAlignment.Center;
            _pages.RenderTransform = _pagesShift;
            Canvas.SetLeft(_pages, 0);
            Canvas.SetTop(_pages, 0);
            _viewport.Children.Add(_pages);

            _content.MinWidth = 200;
            _content.Margin = new Thickness(16, 14 + TailHeight, 16, 16);
            _content.Children.Add(_header);
            _content.Children.Add(_empty);
            _content.Children.Add(_viewport);
            _content.Children.Add(_dots);
            _content.PropertyChanged += (_, e) =>
            {
                if (e.Property != BoundsProperty) return;
                UpdateBubble();
                if (_placed) Place(); // o conteúdo mudou de tamanho (ex.: adicionou apps): reposiciona
            };

            _root.Margin = new Thickness(16);
            _root.HorizontalAlignment = HorizontalAlignment.Left; // abraça o conteúdo, mesmo se a janela for maior
            _root.VerticalAlignment = VerticalAlignment.Top;
            _root.RenderTransform = _slide;
            _root.Children.Add(_bubble);
            _root.Children.Add(_content);
            Content = _root;

            BuildMenu();
            Reload();

            Opened += (_, _) => { Place(); Activate(); PlayOpen(); };
            Deactivated += (_, _) => { if (!_closing && !_dragging) Close(); }; // fecha ao clicar fora
            KeyDown += OnKeyDown;
            PointerWheelChanged += (_, e) =>
            {
                if (_pageCount <= 1) return;
                GoToPage(_page + (e.Delta.Y < 0 ? 1 : -1));
                e.Handled = true;
            };

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                e.DragEffects = !_dragging && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Link : DragDropEffects.None;
                e.Handled = true;
            });
            AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
                if (paths is { Count: > 0 }) Controller.AddPaths(_folder, paths);
                e.Handled = true;
            });
        }

        // ---------- balão ----------

        private void UpdateBubble()
        {
            var m = _content.Margin;
            double w = _content.Bounds.Width + m.Left + m.Right;
            double h = _content.Bounds.Height + m.Top + m.Bottom;
            if (w < 10 || h < 10) return;

            double top = _tailTop ? TailHeight : 0;
            double bottom = _tailTop ? h : h - TailHeight;
            double tx = Math.Max(Radius + TailHalfBase + 2, Math.Min(_tailX, w - Radius - TailHalfBase - 2));
            var r = new Size(Radius, Radius);

            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(Radius, top), true);
                if (_tailTop)
                {
                    c.LineTo(new Point(tx - TailHalfBase, top));
                    c.LineTo(new Point(tx, top - TailHeight + 0.5));
                    c.LineTo(new Point(tx + TailHalfBase, top));
                }
                c.LineTo(new Point(w - Radius, top));
                c.ArcTo(new Point(w, top + Radius), r, 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(w, bottom - Radius));
                c.ArcTo(new Point(w - Radius, bottom), r, 0, false, SweepDirection.Clockwise);
                if (!_tailTop)
                {
                    c.LineTo(new Point(tx + TailHalfBase, bottom));
                    c.LineTo(new Point(tx, bottom + TailHeight - 0.5));
                    c.LineTo(new Point(tx - TailHalfBase, bottom));
                }
                c.LineTo(new Point(Radius, bottom));
                c.ArcTo(new Point(0, bottom - Radius), r, 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(0, top + Radius));
                c.ArcTo(new Point(Radius, top), r, 0, false, SweepDirection.Clockwise);
                c.EndFigure(true);
            }
            _bubble.Data = g;
        }

        /// <summary>Abaixo do ícone da pasta (ou acima, se não couber), com a ponta no centro do ícone.</summary>
        private void Place()
        {
            double s = RenderScaling;
            int w = SizeW(s), h = SizeH(s);
            int margin = (int)Math.Round(_root.Margin.Left * s), gap = (int)Math.Round(2 * s);

            int cx = _anchor.X + _anchor.Width / 2, cy = _anchor.Y + _anchor.Height / 2;
            var screen = Screens.ScreenFromPoint(new PixelPoint(cx, cy)) ?? Screens.Primary;
            var wa = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

            bool below = _anchor.Bottom + gap + (h - margin) <= wa.Bottom;
            if (below != _tailTop || !_placed)
            {
                _tailTop = below;
                _content.Margin = new Thickness(16, 14 + (below ? TailHeight : 0), 16, 16 + (below ? 0 : TailHeight));
                _root.InvalidateMeasure();
                _root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (IsVisible) UpdateLayout();
                w = SizeW(s);
                h = SizeH(s);
            }

            int left = Math.Clamp(cx - w / 2, wa.X, Math.Max(wa.X, wa.Right - w));
            int top = below ? _anchor.Bottom + gap - margin : _anchor.Y - gap - h + margin;
            top = Math.Clamp(top, wa.Y - margin, Math.Max(wa.Y - margin, wa.Bottom - h + margin));

            _tailX = (cx - left) / s - _root.Margin.Left; // a ponta aponta para o centro do ícone
            UpdateBubble();
            Position = new PixelPoint(left, top);
            _placed = true;
        }

        // Tamanho da janela = conteúdo + margem da sombra (não o que o gerenciador de janelas der). Antes de abrir ainda não
        // há layout: usa o tamanho desejado, que já inclui as margens.
        private int SizeW(double s) => (int)Math.Ceiling((_root.Bounds.Width > 0 ? _root.Bounds.Width + _root.Margin.Left + _root.Margin.Right : _root.DesiredSize.Width) * s);
        private int SizeH(double s) => (int)Math.Ceiling((_root.Bounds.Height > 0 ? _root.Bounds.Height + _root.Margin.Top + _root.Margin.Bottom : _root.DesiredSize.Height) * s);

        /// <summary>
        /// Abre o balão JÁ no lugar certo. Posicionar depois de abrir é instável (o gerenciador de janelas coloca a janela
        /// onde quiser e só depois atende o pedido), então a posição é calculada antes, com o tamanho medido do conteúdo.
        /// </summary>
        public void ShowAtAnchor()
        {
            _root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Place();
            Show();
        }

        // ---------- animação ----------

        private void PlayOpen()
        {
            double from = _tailTop ? 8 : -8; // vem do lado do ícone
            Anim.Run(190, t => { Opacity = Math.Min(1, t * 1.27); _slide.Y = from * (1 - t); });
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _closing = true;
            if (!_closeDone && ConfigStore.Current.Animations && IsVisible)
            {
                e.Cancel = true; // espera a animação de saída e fecha de verdade depois
                if (!_closeAnimating)
                {
                    _closeAnimating = true;
                    double to = _tailTop ? 4 : -4;
                    Anim.Run(100, t => { Opacity = 1 - t; _slide.Y = to * t; }, Anim.EaseIn, () => { _closeDone = true; Close(); });
                }
                return;
            }
            base.OnClosing(e);
        }

        private bool _closeAnimating;

        // ---------- menu ----------

        private void BuildMenu()
        {
            var menu = new ContextMenu();
            void Add(string header, Action action)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => { Close(); action(); }; // fecha antes: os diálogos o desativariam no meio da ação
                menu.Items.Add(mi);
            }
            Add("Adicionar apps…", () => Controller.PickFiles(_folder));
            Add("Renomear pasta", () => Controller.RenameFolder(_folder));
            menu.Items.Add(new Separator());
            Add("Excluir pasta", () => Controller.DeleteFolder(_folder));
            _content.ContextMenu = menu;
        }

        // ---------- conteúdo e páginas ----------

        public void Reload()
        {
            _header.Text = _folder.Name;
            var items = _folder.Items.ToList();
            int n = items.Count;

            _empty.IsVisible = n == 0;
            _viewport.IsVisible = n > 0;

            int cols = n <= 2 ? 2 : (n <= 6 ? 3 : 4);
            int pageSize = cols * RowsPerPage;
            _pageCount = Math.Max(1, (int)Math.Ceiling(n / (double)pageSize));
            int rows = _pageCount > 1 ? RowsPerPage : Math.Max(1, (int)Math.Ceiling(n / (double)cols)); // várias páginas: todas com 3 linhas
            _pageWidth = cols * CellWidth;

            _pages.Children.Clear();
            for (int p = 0; p < _pageCount; p++)
            {
                var grid = new UniformGrid { Columns = cols, Rows = rows, Width = _pageWidth };
                foreach (var item in items.Skip(p * pageSize).Take(pageSize)) grid.Children.Add(BuildItem(item));
                _pages.Children.Add(grid);
            }

            _page = Math.Max(0, Math.Min(_page, _pageCount - 1));
            _pages.Measure(Size.Infinity);
            _viewport.Width = _pageWidth;
            _viewport.Height = _pages.DesiredSize.Height;
            _pagesShift.X = -_page * _pageWidth;
            BuildDots();
        }

        private void BuildDots()
        {
            _dots.Children.Clear();
            _dots.IsVisible = _pageCount > 1;
            for (int i = 0; i < _pageCount; i++)
            {
                int target = i;
                bool active = i == _page;
                var dot = new Border
                {
                    Width = active ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0),
                    Background = Palette.B(active ? _p.Accent : PastelTheme.WithAlpha(_p.Text, 0x55)), Cursor = new Cursor(StandardCursorType.Hand)
                };
                dot.PointerReleased += (_, _) => GoToPage(target);
                _dots.Children.Add(dot);
            }
        }

        private void GoToPage(int page)
        {
            page = Math.Max(0, Math.Min(page, _pageCount - 1));
            if (page == _page) return;
            _page = page;
            double from = _pagesShift.X, to = -_page * _pageWidth;
            Anim.Run(240, t => _pagesShift.X = from + (to - from) * t);
            BuildDots();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Right: case Key.PageDown: GoToPage(_page + 1); break;
                case Key.Left: case Key.PageUp: GoToPage(_page - 1); break;
            }
        }

        private Control BuildItem(ShortcutItem item)
        {
            var img = new Image { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center, Source = IconCache.GetBitmap(item, 128) };
            RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);

            var text = new TextBlock
            {
                Text = item.Name, Foreground = Palette.B(_p.Text), FontFamily = Ui.Body, FontSize = 12, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Margin = new Thickness(0, 6, 0, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(img);
            stack.Children.Add(text);

            var hover = Palette.B(PastelTheme.WithAlpha(_p.Accent, 0x38));
            var cell = new Border
            {
                Width = 86, Padding = new Thickness(6), Margin = new Thickness(2), CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = stack
            };
            ToolTip.SetTip(cell, item.Path);
            cell.PointerEntered += (_, _) => cell.Background = hover;

            Point? down = null;
            cell.PointerExited += (_, _) => { cell.Background = Brushes.Transparent; down = null; };
            cell.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) down = e.GetPosition(this);
            };
            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || down == null) return;
                down = null;
                Close();
                Controller.Launch(item);
            };
            cell.PointerMoved += (_, e) =>
            {
                if (down == null || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                var d = e.GetPosition(this) - down.Value;
                if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
                down = null;
                StartDrag(item, e);
            };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Abrir local do arquivo" };
            open.Click += (_, _) => AppLauncher.ShowInFileManager(item.Path);
            var remove = new MenuItem { Header = "Remover da pasta" };
            remove.Click += (_, _) => Controller.RemoveItem(_folder, item);
            menu.Items.Add(open);
            menu.Items.Add(remove);
            cell.ContextMenu = menu;
            return cell;
        }

        /// <summary>
        /// Arrastar para fora: atalho gerenciado (veio da área de trabalho) MOVE o arquivo para onde soltar;
        /// app só referenciado vira um atalho no destino, sem copiar o programa.
        /// </summary>
        private async void StartDrag(ShortcutItem item, PointerEventArgs e)
        {
            bool managed = item.OriginalPath != null && File.Exists(item.Path);
            var data = new DataObject();
            try
            {
                var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(item.Path));
                if (file != null) data.Set(DataFormats.Files, new[] { file });
            }
            catch { }

            _dragging = true;
            try
            {
                var effect = await DragDrop.DoDragDrop(e, data, managed ? DragDropEffects.Move : DragDropEffects.Link);
                if (managed && effect.HasFlag(DragDropEffects.Move)) Controller.ItemMovedOut(_folder, item);
            }
            catch { }
            finally { _dragging = false; }
            if (!_closing && !IsActive) Close();
        }
    }
}
