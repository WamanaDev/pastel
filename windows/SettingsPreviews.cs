using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Pastel
{
    /// <summary>Fundo que lembra uma área de trabalho (papel de parede escuro), onde as prévias "flutuam".</summary>
    internal static class PreviewStage
    {
        public static Brush Wallpaper() => new LinearGradientBrush(
            Color.FromRgb(0x2D, 0x24, 0x48), Color.FromRgb(0x14, 0x10, 0x20), 60);

        public static readonly Color[] Sample =
        {
            Color.FromRgb(0xFF, 0x8F, 0xB1), Color.FromRgb(0xFF, 0xC7, 0x9A), Color.FromRgb(0x8F, 0xE3, 0xC0),
            Color.FromRgb(0xB9, 0xA6, 0xFF), Color.FromRgb(0x8F, 0xB8, 0xFF)
        };

        public static UIElement Icon(ShortcutItem? item, double size, int sampleIndex)
        {
            if (item != null)
            {
                var img = new Image { Source = IconService.GetStyled(item.Path, 128), Width = size, Height = size };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28),
                Background = new SolidColorBrush(Sample[sampleIndex % Sample.Length])
            };
        }
    }

    /// <summary>
    /// Prévia ao vivo de um flutuante: mesmas cores, tamanhos, nome e efeitos do mouse da barra de verdade.
    /// É refeita a cada mudança nas configurações, então acompanha tudo que a pessoa mexe.
    /// </summary>
    internal sealed class FloatingPreview : Border
    {
        private readonly FloatingModel _m;
        private readonly StackPanel _items = new();
        private readonly List<(Border tile, ScaleTransform scale)> _tiles = new();
        private double Slot => _m.IconSize + 16;

        public FloatingPreview(FloatingModel m)
        {
            _m = m;
            Width = 280;
            Height = 214;
            CornerRadius = new CornerRadius(14);
            ClipToBounds = true;
            Background = PreviewStage.Wallpaper();
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            Build();
        }

        private void Build()
        {
            bool light = WindowsTheme.IsLight;
            var accent = WindowsTheme.Visible(WindowsTheme.Accent, light);
            var baseColor = light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1E, 0x22, 0x2C);
            var glassColor = WindowsTheme.WithAlpha(WindowsTheme.Mix(baseColor, accent, 0.16), light ? (byte)0xC8 : (byte)0xB8);

            var caption = new TextBlock
            {
                Text = _m.Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 5),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 140,
                Visibility = _m.ShowName ? Visibility.Visible : Visibility.Collapsed
            };

            var glass = new Border
            {
                CornerRadius = new CornerRadius(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 6, 8, 2)
            };
            if (_m.ShowBackground)
            {
                glass.Background = new SolidColorBrush(glassColor);
                glass.BorderBrush = new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0x90));
                glass.BorderThickness = new Thickness(1.5);
                caption.Foreground = new SolidColorBrush(WindowsTheme.Text(light));
            }
            else
            {
                glass.Background = Brushes.Transparent;
                caption.Foreground = Brushes.White;
                caption.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
            }

            _items.Orientation = _m.Vertical ? Orientation.Vertical : Orientation.Horizontal;
            _items.HorizontalAlignment = HorizontalAlignment.Center;
            _items.Background = Brushes.Transparent;
            _items.MouseMove += (_, e) => ApplyProximity(e.GetPosition(_items));
            _items.MouseLeave += (_, _) =>
            {
                if (_m.HoverEffect != "proximity") return;
                foreach (var (tile, scale) in _tiles) { ScaleTo(scale, 1); Panel.SetZIndex(tile, 0); }
            };

            // Quantos cabem na prévia (ela não rola): sempre pelo menos 2.
            double room = _m.Vertical ? Height - 60 : Width - 40;
            int fit = Math.Max(2, (int)(room / Slot));
            int count = Math.Min(_m.Items.Count == 0 ? 5 : _m.Items.Count, fit);
            for (int i = 0; i < count; i++)
                _items.Children.Add(BuildTile(i < _m.Items.Count ? _m.Items[i] : null, i, accent));

            var stack = new StackPanel();
            stack.Children.Add(_items);
            stack.Children.Add(caption);
            glass.Child = stack;

            // A prévia não rola: se o flutuante tem mais ícones do que cabem aqui, avisa quantos ficaram de fora.
            var root = new Grid();
            root.Children.Add(glass);
            int hidden = _m.Items.Count - count;
            if (hidden > 0)
                root.Children.Add(new TextBlock
                {
                    Text = "+" + hidden, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 8)
                });
            Child = root;
        }

        private Border BuildTile(ShortcutItem? item, int index, Color accent)
        {
            var scale = new ScaleTransform(1, 1);
            var icon = PreviewStage.Icon(item, _m.IconSize, index);
            icon.RenderTransform = scale;
            icon.RenderTransformOrigin = new Point(0.5, 0.5);

            var tile = new Border
            {
                Width = Slot, Height = Slot, CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent, Child = icon
            };
            _tiles.Add((tile, scale));
            if (_m.HoverEffect == "focus")
            {
                var hover = new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0x30));
                tile.MouseEnter += (_, _) => { tile.Background = hover; Panel.SetZIndex(tile, 5); ScaleTo(scale, 1.4); };
                tile.MouseLeave += (_, _) => { tile.Background = Brushes.Transparent; Panel.SetZIndex(tile, 0); ScaleTo(scale, 1); };
            }
            return tile;
        }

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

        /// <summary>Estilo dock: cada ícone cresce conforme a distância até o mouse (a mesma curva da barra de verdade).</summary>
        private void ApplyProximity(Point mouse)
        {
            if (_m.HoverEffect != "proximity" || _tiles.Count == 0) return;
            double reach = Slot * 1.1;
            foreach (var (tile, scale) in _tiles)
            {
                var c = tile.TranslatePoint(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2), _items);
                double d = Math.Sqrt((c.X - mouse.X) * (c.X - mouse.X) + (c.Y - mouse.Y) * (c.Y - mouse.Y));
                double k = 1 + 0.75 * Math.Exp(-Math.Pow(d / reach, 2));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = k;
                Panel.SetZIndex(tile, (int)(k * 100));
            }
        }
    }

    /// <summary>
    /// Prévia ao vivo do balão de uma pasta, com o tema e a cor de destaque escolhidos.
    /// Usa as cores do balão de verdade (FolderPopupWindow).
    /// </summary>
    internal sealed class BalloonPreview : Border
    {
        private readonly StackPanel _pop = new();

        public BalloonPreview()
        {
            Width = 280;
            Height = 340;
            CornerRadius = new CornerRadius(14);
            ClipToBounds = true;
            Background = PreviewStage.Wallpaper();
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

            bool light = WindowsTheme.IsLight;
            var accent = WindowsTheme.Visible(WindowsTheme.Accent, light);
            var text = WindowsTheme.Text(light);
            var surface = WindowsTheme.Surface(light);
            var fill = new SolidColorBrush(WindowsTheme.WithAlpha(surface, light ? (byte)0xE8 : (byte)0xE2));
            var stroke = new SolidColorBrush(WindowsTheme.WithAlpha(text, 0x2A));

            // Com apps de verdade da primeira pasta que tiver; senão, quadradinhos de exemplo.
            var folder = ConfigStore.Current.Folders.FirstOrDefault(f => f.Items.Count > 0);
            var items = folder?.Items ?? new List<ShortcutItem>();

            var content = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            content.Children.Add(new TextBlock
            {
                Text = folder?.Name ?? "Jogos",
                FontFamily = new FontFamily("Nunito, Segoe UI Variable Display, Segoe UI"),
                FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(text), Margin = new Thickness(0, 0, 0, 10)
            });
            var grid = new UniformGrid { Columns = 4 };
            for (int i = 0; i < 4; i++)
            {
                var cell = new Border
                {
                    Padding = new Thickness(4), CornerRadius = new CornerRadius(10), Margin = new Thickness(1),
                    Background = i == 0 ? new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0x38)) : Brushes.Transparent // o "mouse" está no 1º
                };
                var col = new StackPanel();
                var ico = PreviewStage.Icon(i < items.Count ? items[i] : null, 40, i);
                ((FrameworkElement)ico).HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(ico);
                col.Children.Add(new TextBlock
                {
                    Text = i < items.Count ? items[i].Name : new[] { "App 1", "App 2", "App 3", "App 4" }[i],
                    FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI"),
                    FontSize = 10.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new SolidColorBrush(text), Margin = new Thickness(0, 4, 0, 0)
                });
                cell.Child = col;
                grid.Children.Add(cell);
            }
            content.Children.Add(grid);

            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            for (int i = 0; i < 3; i++)
                dots.Children.Add(new Border
                {
                    Width = i == 0 ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0),
                    Background = new SolidColorBrush(i == 0 ? accent : WindowsTheme.WithAlpha(text, 0x55))
                });
            content.Children.Add(dots);

            var body = new Border
            {
                Width = 240, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
                Background = fill, BorderBrush = stroke, Child = content,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.38, Color = Colors.Black }
            };

            // Ponta do balão apontando para o ícone da pasta.
            var tail = new Canvas { Width = 20, Height = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
            tail.Children.Add(new System.Windows.Shapes.Polygon { Points = new PointCollection { new Point(0, 0), new Point(10, 9), new Point(20, 0) }, Fill = fill });
            tail.Children.Add(new System.Windows.Shapes.Polyline { Points = new PointCollection { new Point(0, 0), new Point(10, 9), new Point(20, 0) }, Stroke = stroke, StrokeThickness = 1 });

            // O ícone da pasta (miniatura 2x2).
            var mini = new UniformGrid { Rows = 2, Columns = 2, Width = 30, Height = 30 };
            for (int i = 0; i < 4; i++)
            {
                var o = PreviewStage.Icon(i < items.Count ? items[i] : null, 13, i);
                ((FrameworkElement)o).Margin = new Thickness(1);
                mini.Children.Add(o);
            }
            var folderIcon = new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(WindowsTheme.WithAlpha(WindowsTheme.Mix(surface, accent, 0.18), 0xC4)),
                BorderBrush = new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0xA0)),
                Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = mini
            };

            _pop.Children.Add(body);
            _pop.Children.Add(tail);
            _pop.RenderTransformOrigin = new Point(0.5, 1);
            _pop.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) } };

            var root = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
            root.Children.Add(_pop);
            root.Children.Add(folderIcon);
            Child = root;
        }

        /// <summary>Toca a animação de abrir, se as animações estiverem ligadas; senão o balão já está aberto.</summary>
        public void Replay()
        {
            var g = (TransformGroup)_pop.RenderTransform;
            var scale = (ScaleTransform)g.Children[0];
            var move = (TranslateTransform)g.Children[1];
            _pop.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            if (!ConfigStore.Current.Animations) { _pop.Opacity = 1; scale.ScaleX = scale.ScaleY = 1; move.Y = 0; return; }

            var d = TimeSpan.FromMilliseconds(280);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _pop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.93, 1, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.93, 1, d) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, d) { EasingFunction = ease });
        }
    }
}
