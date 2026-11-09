using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Pastel
{
    /// <summary>Caixinha simples para pedir um texto (usada em "Renomear").</summary>
    public static class Prompt
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static string? Ask(string title, string initial)
        {
            string? result = null;

            var win = new Window
            {
                Title = title,
                Width = 340,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var box = new TextBox { Text = initial, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 12) };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancelar", Width = 80, IsCancel = true };
            ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            win.Content = panel;

            // Aberta pelo menu de contexto (outro processo), a janela nasce sem foco: força ativação.
            void Grab()
            {
                win.Activate();
                SetForegroundWindow(new WindowInteropHelper(win).Handle);
                Keyboard.Focus(box);
                box.Focus();
                box.SelectAll();
            }
            win.Loaded += (_, _) => Grab();
            win.ContentRendered += (_, _) => Grab();
            win.ShowDialog();
            return result;
        }
    }
}
