using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pastel
{
    /// <summary>
    /// Pergunta, na primeira abertura, se o Pastel pode arrumar a área de trabalho por tipo.
    /// Mostra só os tipos que existem lá agora; nada acontece antes do "Organizar".
    /// </summary>
    internal static class OrganizeOffer
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>Categoria → quantos itens da área de trabalho caem nela (cada item conta na primeira que casar).</summary>
        public static List<(RuleCatalog.Category Cat, int Count)> Scan(IEnumerable<string> files)
        {
            var counts = new Dictionary<string, int>();
            foreach (var f in files)
            {
                var cat = RuleCatalog.Classify(f);
                if (cat != null) counts[cat.Id] = counts.GetValueOrDefault(cat.Id) + 1;
            }
            return RuleCatalog.All.Where(c => counts.ContainsKey(c.Id)).Select(c => (c, counts[c.Id])).ToList();
        }

        /// <summary>Devolve os ids marcados, ou null se a pessoa escolheu "Agora não".</summary>
        public static List<string>? Ask(List<(RuleCatalog.Category Cat, int Count)> found)
        {
            bool light = WindowsTheme.IsLight;
            var text = new SolidColorBrush(WindowsTheme.Text(light));
            var subtle = new SolidColorBrush(WindowsTheme.WithAlpha(WindowsTheme.Text(light), 0xB0));
            List<string>? result = null;

            var win = new Window
            {
                Title = "Pastel – Organizar a área de trabalho",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 640,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(WindowsTheme.Surface(light)),
                Foreground = text,
                FontFamily = new FontFamily("Nunito, Segoe UI Variable Text, Segoe UI")
            };

            var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
            panel.Children.Add(new TextBlock
            {
                Text = "Quer que o Pastel organize sua área de trabalho?",
                FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = text
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Ele cria uma pasta para cada tipo marcado abaixo e move os atalhos e arquivos para dentro dela. " +
                       "Nada é apagado: para desfazer, remova o item da pasta ou exclua a pasta, e tudo volta para a área de trabalho. " +
                       "Depois disso, o que for chegando também vai para a pasta certa.",
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = subtle, Margin = new Thickness(0, 8, 0, 12)
            });

            var boxes = new List<(CheckBox Box, string Id)>();
            var list = new StackPanel();
            foreach (var (cat, count) in found)
            {
                var cb = new CheckBox
                {
                    IsChecked = true, Foreground = text, Margin = new Thickness(0, 3, 0, 3),
                    Content = $"{cat.Name}  ·  {(count == 1 ? "1 item" : count + " itens")}",
                    ToolTip = cat.Description
                };
                boxes.Add((cb, cat.Id));
                list.Children.Add(cb);
            }
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = "Dá para mudar isso depois em Configurações > Regras.",
                FontSize = 11.5, Foreground = subtle, Margin = new Thickness(0, 12, 0, 14), TextWrapping = TextWrapping.Wrap
            });

            var ok = new Button { Content = "Organizar", Width = 110, Padding = new Thickness(0, 5, 0, 5), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var no = new Button { Content = "Agora não", Width = 110, Padding = new Thickness(0, 5, 0, 5), IsCancel = true };
            ok.Click += (_, _) =>
            {
                result = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Id).ToList();
                win.DialogResult = true;
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(no);
            panel.Children.Add(buttons);
            win.Content = panel;

            void Grab() { win.Activate(); SetForegroundWindow(new WindowInteropHelper(win).Handle); }
            win.Loaded += (_, _) => Grab();
            win.ShowDialog();
            return result is { Count: > 0 } ? result : null;
        }
    }
}
