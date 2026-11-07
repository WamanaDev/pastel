using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Pastel
{
    /// <summary>Animações por tempo (sem depender de recursos do Avalonia que variam entre versões).</summary>
    internal static class Anim
    {
        public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
        public static double EaseIn(double t) => t * t;

        /// <summary>Roda step(0..1) durante ms milissegundos. Com as animações desligadas, vai direto ao fim.</summary>
        public static void Run(double ms, Action<double> step, Func<double, double>? ease = null, Action? done = null)
        {
            if (!ConfigStore.Current.Animations || ms <= 0) { step(1); done?.Invoke(); return; }
            var sw = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(14) };
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
                step((ease ?? EaseOut)(t));
                if (t >= 1) { timer.Stop(); done?.Invoke(); }
            };
            timer.Start();
        }
    }

    /// <summary>Cores da interface no tema atual (claro/escuro e cor de destaque).</summary>
    internal sealed class Palette
    {
        public bool Light { get; }
        public Color Text, Surface, Card, Nav, Border, Hover, Subtle, NavActive, Violet, Danger, Accent;

        public Palette()
        {
            Light = PastelTheme.IsLight;
            Text = PastelTheme.Text(Light);
            Surface = PastelTheme.Surface(Light);
            Card = Light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x25, 0x20, 0x33);
            Nav = Light ? Color.FromRgb(0xF7, 0xF0, 0xEA) : Color.FromRgb(0x16, 0x12, 0x20);
            Border = PastelTheme.WithAlpha(Text, 0x22);
            Hover = PastelTheme.WithAlpha(Text, 0x14);
            Subtle = PastelTheme.WithAlpha(Text, 0xB0);
            Violet = PastelTheme.Violet;
            NavActive = PastelTheme.WithAlpha(PastelTheme.Violet, Light ? (byte)0x24 : (byte)0x40);
            Danger = Light ? Color.FromRgb(0xC2, 0x2B, 0x4A) : Color.FromRgb(0xFF, 0x8F, 0xA3);
            Accent = PastelTheme.Visible(PastelTheme.Accent, Light);
        }

        public static IBrush B(Color c) => new SolidColorBrush(c);
    }

    internal static class Ui
    {
        public static readonly FontFamily Body = new("Nunito, Noto Sans, Cantarell, DejaVu Sans, sans-serif");
        public static readonly FontFamily Title = new("Fredoka, Nunito, Noto Sans, Cantarell, DejaVu Sans, sans-serif");

        public static TextBlock Text(string text, Color color, double size = 13, FontWeight weight = FontWeight.Normal, bool wrap = true, FontFamily? family = null) =>
            new()
            {
                Text = text, Foreground = Palette.B(color), FontSize = size, FontWeight = weight, FontFamily = family ?? Body,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
            };

        public static Border Card(Palette p, Control child, double bottom = 12) => new()
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 15), Margin = new Thickness(0, 0, 0, bottom),
            BorderThickness = new Thickness(1), Background = Palette.B(p.Card), BorderBrush = Palette.B(p.Border), Child = child
        };

        /// <summary>Botão desenhado à mão (o tema padrão do Avalonia mudaria o visual do Pastel).</summary>
        public static Border Button(Palette p, string label, Action onClick, string kind = "ghost")
        {
            var color = kind == "primary" ? Colors.White : kind == "danger" ? p.Danger : p.Text;
            var normal = kind == "primary" ? p.Violet : Colors.Transparent;
            var hover = kind == "primary" ? PastelTheme.Mix(p.Violet, Colors.Black, 0.12) : p.Hover;
            var b = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(kind == "primary" ? 16 : 13, kind == "primary" ? 8 : 7),
                Margin = new Thickness(6, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Background = Palette.B(normal), BorderThickness = new Thickness(kind == "primary" ? 0 : 1), BorderBrush = Palette.B(p.Border),
                Child = Text(label, color, 13, kind == "primary" ? FontWeight.SemiBold : FontWeight.Normal, wrap: false)
            };
            b.PointerEntered += (_, _) => b.Background = Palette.B(hover);
            b.PointerExited += (_, _) => b.Background = Palette.B(normal);
            b.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) onClick(); };
            return b;
        }

        /// <summary>Linha "título + descrição" à esquerda e um controle à direita.</summary>
        public static Grid Row(Palette p, string title, string? desc, Control control)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(Text(title, p.Text, 14, FontWeight.SemiBold));
            if (desc != null) left.Children.Add(Text(desc, p.Subtle, 12));
            g.Children.Add(left);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        /// <summary>Grupo de opções lado a lado (ex.: Seguir o sistema | Claro | Escuro).</summary>
        public static Control Segmented(Palette p, string[] labels, int selected, Action<int> onPick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var items = new Border[labels.Length];
            void Paint(int sel)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    bool on = i == sel;
                    items[i].Background = Palette.B(on ? p.Violet : Colors.Transparent);
                    ((TextBlock)items[i].Child!).Foreground = Palette.B(on ? Colors.White : p.Text);
                    ((TextBlock)items[i].Child!).FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(13, 6), Margin = new Thickness(2),
                    Cursor = new Cursor(StandardCursorType.Hand), Child = Text(labels[i], p.Text, 13, wrap: false)
                };
                b.PointerEntered += (_, _) => { if (b.Background is SolidColorBrush s && s.Color.A == 0) b.Background = Palette.B(p.Hover); };
                b.PointerExited += (_, _) => Paint(selected);
                b.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    selected = idx; Paint(selected); onPick(idx);
                };
                items[i] = b;
                row.Children.Add(b);
            }
            Paint(selected);
            return new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(2), BorderThickness = new Thickness(1), BorderBrush = Palette.B(p.Border), Child = row, HorizontalAlignment = HorizontalAlignment.Right };
        }

        /// <summary>Botão liga/desliga no estilo dos controles do KDE/Windows 11.</summary>
        public static Control Switch(Palette p, bool on, Action<bool> onChange)
        {
            var thumb = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Cursor = new Cursor(StandardCursorType.Hand), Child = thumb, HorizontalAlignment = HorizontalAlignment.Right };
            void Paint(bool animate)
            {
                track.Background = Palette.B(on ? p.Violet : PastelTheme.WithAlpha(p.Text, 0x40));
                double to = on ? 23 : 3;
                double from = thumb.Margin.Left;
                if (animate) Anim.Run(140, t => thumb.Margin = new Thickness(from + (to - from) * t, 0, 0, 0));
                else thumb.Margin = new Thickness(to, 0, 0, 0);
            }
            Paint(false);
            track.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                on = !on; Paint(true); onChange(on);
            };
            return track;
        }
    }
}
