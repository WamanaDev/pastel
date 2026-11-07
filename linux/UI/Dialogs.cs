using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Pastel
{
    /// <summary>Caixas de diálogo no visual do Pastel (pedir um nome, confirmar, avisar). Não bloqueiam: devolvem uma Task.</summary>
    internal static class Dialogs
    {
        private static Window Base(string title, Palette p, double width = 380) => new()
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Palette.B(p.Surface),
            RequestedThemeVariant = p.Light ? ThemeVariant.Light : ThemeVariant.Dark, FontFamily = Ui.Body
        };

        /// <summary>Pergunta um texto (nome da pasta). null = cancelou.</summary>
        public static Task<string?> AskText(string title, string initial)
        {
            var p = new Palette();
            var tcs = new TaskCompletionSource<string?>();
            var win = Base(title, p);

            var box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(8, 6) };
            void Ok() { tcs.TrySetResult(box.Text); win.Close(); }
            void Cancel() { win.Close(); }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Ui.Button(p, "Cancelar", Cancel));
            buttons.Children.Add(Ui.Button(p, "OK", Ok, "primary"));

            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(Ui.Text(title, p.Text, 15, FontWeight.SemiBold));
            panel.Children.Add(new Border { Height = 10 });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            win.Content = panel;

            win.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Ok(); e.Handled = true; } else if (e.Key == Key.Escape) { Cancel(); e.Handled = true; } };
            win.Closed += (_, _) => tcs.TrySetResult(null);
            // aberta por outro processo (menu de contexto), a janela nasce sem foco: pede ativação e foca o campo
            win.Opened += (_, _) => { win.Activate(); box.Focus(); box.SelectAll(); };
            win.Show();
            return tcs.Task;
        }

        public static Task<bool> Confirm(string title, string message, string yes = "Excluir")
        {
            var p = new Palette();
            var tcs = new TaskCompletionSource<bool>();
            var win = Base(title, p);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Ui.Button(p, "Cancelar", () => win.Close()));
            buttons.Children.Add(Ui.Button(p, yes, () => { tcs.TrySetResult(true); win.Close(); }, "primary"));
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(Ui.Text(title, p.Text, 15, FontWeight.SemiBold));
            panel.Children.Add(new Border { Height = 8 });
            panel.Children.Add(Ui.Text(message, p.Subtle, 13));
            panel.Children.Add(new Border { Height = 16 });
            panel.Children.Add(buttons);
            win.Content = panel;
            win.Closed += (_, _) => tcs.TrySetResult(false);
            win.Opened += (_, _) => win.Activate();
            win.Show();
            return tcs.Task;
        }

        public static void Info(string title, string message)
        {
            var p = new Palette();
            var win = Base(title, p);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Ui.Button(p, "OK", () => win.Close(), "primary"));
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(Ui.Text(title, p.Text, 15, FontWeight.SemiBold));
            panel.Children.Add(new Border { Height = 8 });
            panel.Children.Add(Ui.Text(message, p.Subtle, 13));
            panel.Children.Add(new Border { Height = 16 });
            panel.Children.Add(buttons);
            win.Content = panel;
            win.Opened += (_, _) => win.Activate();
            win.Show();
        }
    }
}
