using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Pastel
{
    /// <summary>
    /// Descobre onde, na tela, está o ícone de um atalho na área de trabalho (via UI Automation),
    /// para o popup abrir junto dele mesmo que o mouse já tenha se afastado.
    /// </summary>
    internal static class DesktopAnchor
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string? title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);

        internal static IntPtr FindDesktopListView()
        {
            IntPtr defView = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                // Com wallpaper animado/slideshow, a lista de ícones fica sob um WorkerW.
                EnumWindows((hwnd, _) =>
                {
                    var sb = new StringBuilder(32);
                    GetClassName(hwnd, sb, sb.Capacity);
                    if (sb.ToString() != "WorkerW") return true;
                    var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (child == IntPtr.Zero) return true;
                    defView = child;
                    return false;
                }, IntPtr.Zero);
            }
            return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        /// <summary>Retângulo de qualquer ícone da área de trabalho (serve para descobrir o alinhamento da grade).</summary>
        public static Rectangle? AnyItemRect()
        {
            try
            {
                var lv = FindDesktopListView();
                if (lv == IntPtr.Zero) return null;
                var item = AutomationElement.FromHandle(lv).FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                if (item == null) return null;
                var r = item.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width < 8 || r.Height < 8) return null;
                return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            }
            catch { return null; }
        }

        /// <summary>Retângulo do ícone em pixels da tela, ou null se não achar.</summary>
        public static Rectangle? IconRect(string displayName)
        {
            try
            {
                var lv = FindDesktopListView();
                if (lv == IntPtr.Zero) return null;

                var root = AutomationElement.FromHandle(lv);
                var item = root.FindFirst(TreeScope.Children, new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, displayName),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));
                if (item == null) return null;

                var r = item.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width < 8 || r.Height < 8) return null;
                return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            }
            catch { return null; }
        }
    }
}
