using System;
using System.Diagnostics;
using System.Windows;

namespace Pastel
{
    /// <summary>Abre um app da pasta (ou do flutuante) e mostra o aviso no tom da marca se falhar.</summary>
    internal static class Launcher
    {
        public static void Open(ShortcutItem item)
        {
            try
            {
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show($"Não consegui abrir \"{item.Name}\". Confere se o app ainda existe e tenta de novo.",
                    "Pastel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static void ShowInExplorer(ShortcutItem item)
        {
            try { Process.Start("explorer.exe", $"/select,\"{item.Path}\""); } catch { }
        }
    }
}
