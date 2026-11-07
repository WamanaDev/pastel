using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pastel
{
    /// <summary>
    /// Popup com os apps da pasta, estilo pasta do Android: um balão com uma ponta apontando para o
    /// ícone da pasta, páginas como no Menu Iniciar e entrada/saída suaves.
    /// </summary>
    public partial class FolderPopupWindow : Window
    {
        private const double TailHeight = 11, TailHalfBase = 11, Radius = 16;
        private const int CellWidth = 90, RowsPerPage = 3;

        private readonly FolderModel _folder;
        private readonly System.Drawing.Rectangle _anchor; // ícone da pasta na tela, em pixels
        private bool _closing, _dragging, _closeAnimDone, _closeAnimating, _placed;
        private bool _tailTop = true;
        private double _tailX = 100;                        // posição da ponta, dentro do balão (DIP)

        // Tema escolhido (claro/escuro e cor de destaque)
        private readonly bool _light = WindowsTheme.IsLight;
        private readonly Color _accent = WindowsTheme.Visible(WindowsTheme.Accent, WindowsTheme.IsLight);
        private Color _text;

        // Páginas
        private int _page, _pageCount = 1;
        private double _pageWidth;

        private static App TheApp => (App)Application.Current;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public FolderPopupWindow(FolderModel folder, System.Drawing.Rectangle anchor)
        {
            InitializeComponent();
            _folder = folder;
            _anchor = anchor;
            Opacity = 0; // aparece pela animação de entrada

            ApplyTheme();
            BuildMenu();
            Loaded += (_, _) =>
            {
                Place();
                Activate();
                SetForegroundWindow(new WindowInteropHelper(this).Handle);
                PlayOpen();
            };
            // Fecha ao clicar fora; o guard evita Close() duplo
            Deactivated += (_, _) => { if (!_closing && !_dragging) Close(); };
            Reload();
        }

        // ---------- tema ----------

        private void ApplyTheme()
        {
            _text = WindowsTheme.Text(_light);
            var surface = WindowsTheme.Surface(_light);

            HeaderText.Foreground = new SolidColorBrush(_text);
            EmptyHint.Foreground = new SolidColorBrush(WindowsTheme.WithAlpha(_text, 0xA6));
            Bubble.Fill = new SolidColorBrush(WindowsTheme.WithAlpha(surface, _light ? (byte)0xE8 : (byte)0xE2));
            Bubble.Stroke = new SolidColorBrush(WindowsTheme.WithAlpha(_text, 0x2A));
        }

        // ---------- balão ----------

        private void Content_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateBubble();
            if (_placed) Place(); // o conteúdo mudou de tamanho (ex.: adicionou apps): reposiciona
        }

        private void UpdateBubble()
        {
            // O balão cobre o conteúdo MAIS as margens (ActualWidth/Height não contam as margens).
            var mg = BubbleContent.Margin;
            double w = BubbleContent.ActualWidth + mg.Left + mg.Right;
            double h = BubbleContent.ActualHeight + mg.Top + mg.Bottom;
            if (w < 10 || h < 10) return;

            double top = _tailTop ? TailHeight : 0;
            double bottom = _tailTop ? h : h - TailHeight;
            double tx = Math.Max(Radius + TailHalfBase + 2, Math.Min(_tailX, w - Radius - TailHalfBase - 2));

            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(Radius, top), true, true);
                if (_tailTop)
                {
                    c.LineTo(new Point(tx - TailHalfBase, top), true, true);
                    c.LineTo(new Point(tx, top - TailHeight + 0.5), true, true);
                    c.LineTo(new Point(tx + TailHalfBase, top), true, true);
                }
                c.LineTo(new Point(w - Radius, top), true, true);
                c.ArcTo(new Point(w, top + Radius), new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(new Point(w, bottom - Radius), true, true);
                c.ArcTo(new Point(w - Radius, bottom), new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true, true);
                if (!_tailTop)
                {
                    c.LineTo(new Point(tx + TailHalfBase, bottom), true, true);
                    c.LineTo(new Point(tx, bottom + TailHeight - 0.5), true, true);
                    c.LineTo(new Point(tx - TailHalfBase, bottom), true, true);
                }
                c.LineTo(new Point(Radius, bottom), true, true);
                c.ArcTo(new Point(0, bottom - Radius), new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(new Point(0, top + Radius), true, true);
                c.ArcTo(new Point(Radius, top), new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            Bubble.Data = g;
        }

        /// <summary>Abaixo do ícone da pasta (ou acima, se não couber), com a ponta no centro do ícone.</summary>
        private void Place()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            double margin = Root.Margin.Left;                          // espaço da sombra (DIP)
            int w = (int)Math.Ceiling(ActualWidth * sx);
            int h = (int)Math.Ceiling(ActualHeight * sy);
            int gap = (int)Math.Round(2 * sy);

            int cx = _anchor.Left + _anchor.Width / 2;
            var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cx, _anchor.Top + _anchor.Height / 2)).WorkingArea;

            // O balão ocupa a janela menos a margem da sombra; a ponta fica na borda do balão.
            int marginPx = (int)Math.Round(margin * sy);
            bool below = _anchor.Bottom + gap + (h - marginPx) <= wa.Bottom;   // cabe embaixo?
            if (below != _tailTop || !_placed)
            {
                _tailTop = below;
                BubbleContent.Margin = new Thickness(16, 14 + (_tailTop ? TailHeight : 0), 16, 16 + (_tailTop ? 0 : TailHeight));
                UpdateLayout();
                w = (int)Math.Ceiling(ActualWidth * sx);
                h = (int)Math.Ceiling(ActualHeight * sy);
            }

            int left = cx - w / 2;
            left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
            int top = _tailTop ? _anchor.Bottom + gap - marginPx
                               : _anchor.Top - gap - h + marginPx;
            top = Math.Max(wa.Top - marginPx, Math.Min(top, wa.Bottom - h + marginPx));

            // A ponta aponta para o centro do ícone (em DIP, dentro do balão).
            _tailX = (cx - left) / sx - margin;
            UpdateBubble();

            const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4;
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
            _placed = true;
        }

        // ---------- animação (entrada e saída suaves, no estilo dos flyouts do Windows 11) ----------

        private static bool Animated => ConfigStore.Current.Animations;

        private void PlayOpen()
        {
            if (!Animated) { Opacity = 1; return; }
            double from = _tailTop ? 8 : -8; // vem do lado do ícone
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(190))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void PlayClose(Action done)
        {
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(100))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => done();
            BeginAnimation(OpacityProperty, fade);
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, _tailTop ? 4 : -4, TimeSpan.FromMilliseconds(100))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _closing = true;
            if (!_closeAnimDone && Animated && IsLoaded)
            {
                e.Cancel = true; // espera a animação de saída e fecha de verdade depois
                if (!_closeAnimating)
                {
                    _closeAnimating = true;
                    PlayClose(() => { _closeAnimDone = true; Close(); });
                }
                return;
            }
            base.OnClosing(e);
        }

        // ---------- menu ----------

        private void BuildMenu()
        {
            var menu = new ContextMenu();
            void Add(string header, Action action)
            {
                var mi = new MenuItem { Header = header };
                // Fecha o popup antes: diálogos o desativariam no meio da ação.
                mi.Click += (_, _) => { Close(); action(); };
                menu.Items.Add(mi);
            }
            var f = _folder;
            Add("Adicionar apps…", () => TheApp.PickFiles(f));
            Add("Renomear pasta", () => TheApp.RenameGroup(f));
            menu.Items.Add(new Separator());
            Add("Excluir pasta", () => TheApp.DeleteGroup(f));
            BubbleContent.ContextMenu = menu;
        }

        // ---------- conteúdo e páginas ----------

        public void Reload()
        {
            HeaderText.Text = _folder.Name;
            var items = _folder.Items.ToList();
            int n = items.Count;

            EmptyHint.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            Viewport.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;

            int cols = n <= 2 ? 2 : (n <= 6 ? 3 : 4);
            int pageSize = cols * RowsPerPage;
            _pageCount = Math.Max(1, (int)Math.Ceiling(n / (double)pageSize));
            // Com várias páginas todas têm 3 linhas (o balão não muda de altura ao trocar de página).
            int rows = _pageCount > 1 ? RowsPerPage : Math.Max(1, (int)Math.Ceiling(n / (double)cols));
            _pageWidth = cols * CellWidth;

            PagesHost.Children.Clear();
            for (int p = 0; p < _pageCount; p++)
            {
                var grid = new UniformGrid { Columns = cols, Rows = rows, Width = _pageWidth };
                foreach (var item in items.Skip(p * pageSize).Take(pageSize))
                    grid.Children.Add(BuildItem(item));
                PagesHost.Children.Add(grid);
            }

            _page = Math.Max(0, Math.Min(_page, _pageCount - 1));
            // O Canvas não mede os filhos: a altura vem da faixa de páginas.
            PagesHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Viewport.Width = _pageWidth;
            Viewport.Height = PagesHost.DesiredSize.Height;
            PagesShift.BeginAnimation(TranslateTransform.XProperty, null);
            PagesShift.X = -_page * _pageWidth;
            BuildDots();
        }

        private void BuildDots()
        {
            Dots.Children.Clear();
            Dots.Visibility = _pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < _pageCount; i++)
            {
                int target = i;
                bool active = i == _page;
                var dot = new Border
                {
                    Width = active ? 18 : 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(3, 0, 3, 0),
                    Background = new SolidColorBrush(active ? _accent : WindowsTheme.WithAlpha(_text, 0x55)),
                    Cursor = Cursors.Hand
                };
                dot.MouseLeftButtonUp += (_, _) => GoToPage(target);
                Dots.Children.Add(dot);
            }
        }

        private void GoToPage(int page)
        {
            page = Math.Max(0, Math.Min(page, _pageCount - 1));
            if (page == _page) return;
            _page = page;
            double to = -_page * _pageWidth;
            if (Animated)
                PagesShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(240))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else
                PagesShift.X = to;
            BuildDots();
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_pageCount <= 1) return;
            GoToPage(_page + (e.Delta < 0 ? 1 : -1));
            e.Handled = true;
        }

        private UIElement BuildItem(ShortcutItem item)
        {
            var img = new Image
            {
                Source = IconService.GetStyled(item.Path, 128),
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var text = new TextBlock
            {
                Text = item.Name,
                Foreground = new SolidColorBrush(_text),
                FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var stack = new StackPanel();
            stack.Children.Add(img);
            stack.Children.Add(text);

            Point? downPos = null;
            var hover = new SolidColorBrush(WindowsTheme.WithAlpha(_accent, 0x38)); // cor de destaque
            var cell = new Border
            {
                Width = 86,
                Padding = new Thickness(6),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = item.Path,
                Child = stack
            };
            cell.MouseEnter += (_, _) => cell.Background = hover;
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            cell.MouseLeftButtonUp += (_, _) => { if (downPos != null) { Close(); Launcher.Open(item); } downPos = null; };

            // Arrastar para fora do popup (ex.: de volta para a área de trabalho).
            cell.PreviewMouseLeftButtonDown += (_, e) => downPos = e.GetPosition(this);
            cell.MouseLeave += (_, _) => downPos = null;
            cell.MouseMove += (_, e) =>
            {
                if (downPos == null || e.LeftButton != MouseButtonState.Pressed) return;
                var d = e.GetPosition(this) - downPos.Value;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                downPos = null;
                StartDrag(item, cell);
            };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Abrir local do arquivo" };
            open.Click += (_, _) => Launcher.ShowInExplorer(item);
            var remove = new MenuItem { Header = "Remover da pasta" };
            remove.Click += (_, _) => TheApp.RemoveItem(_folder, item);
            menu.Items.Add(open);
            menu.Items.Add(remove);
            cell.ContextMenu = menu;

            return cell;
        }

        private void StartDrag(ShortcutItem item, UIElement source)
        {
            // Atalho gerenciado (veio da área de trabalho): arrastar MOVE o arquivo para onde soltar.
            // App só referenciado: arrastar cria um atalho no destino, sem copiar o programa.
            bool managed = item.OriginalPath != null && System.IO.File.Exists(item.Path);
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });

            _dragging = true;
            DragDropEffects effect;
            try { effect = DragDrop.DoDragDrop(source, data, managed ? DragDropEffects.Move : DragDropEffects.Link); }
            finally { _dragging = false; }

            if (managed && effect.HasFlag(DragDropEffects.Move)) TheApp.ItemMovedOut(_folder, item);
            if (!_closing && !IsActive) Close();
        }

        // ---------- teclado e soltar arquivos ----------

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Right: case Key.PageDown: GoToPage(_page + 1); break;
                case Key.Left: case Key.PageUp: GoToPage(_page - 1); break;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_dragging && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) TheApp.AddPaths(_folder, files);
            e.Handled = true;
        }
    }
}
