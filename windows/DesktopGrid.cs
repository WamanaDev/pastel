using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pastel
{
    /// <summary>
    /// Grade de ícones da área de trabalho do Windows. Lê o espaçamento real da lista de ícones do
    /// Explorer, para que as barras flutuantes encaixem nas mesmas células dos atalhos nativos.
    /// Tudo em pixels físicos.
    /// </summary>
    internal static class DesktopGrid
    {
        public readonly record struct Metrics(int OriginX, int OriginY, int Cx, int Cy);

        private const uint LVM_GETITEMSPACING = 0x1033;
        private const int SM_CXICONSPACING = 38;
        private const int SM_CYICONSPACING = 39;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        public static Metrics Get()
        {
            int ox = 0, oy = 0, cx = 0, cy = 0;
            try
            {
                var lv = DesktopAnchor.FindDesktopListView();
                if (lv != IntPtr.Zero)
                {
                    long v = SendMessage(lv, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt64();
                    cx = (int)(v & 0xFFFF);
                    cy = (int)((v >> 16) & 0xFFFF);
                    if (GetWindowRect(lv, out var r)) { ox = r.left; oy = r.top; }
                }
            }
            catch { }

            if (cx < 16 || cy < 16)
            {
                cx = GetSystemMetrics(SM_CXICONSPACING);
                cy = GetSystemMetrics(SM_CYICONSPACING);
                ox = oy = 0;
            }
            cx = Math.Max(cx, 16);
            cy = Math.Max(cy, 16);

            // A grade do Explorer parte do canto da tela PRINCIPAL, não do canto da lista inteira (que
            // começa na tela mais à esquerda). Medido com os ícones reais: o alinhamento vem da posição
            // de um deles (resto da divisão pelo tamanho da célula).
            var any = DesktopAnchor.AnyItemRect();
            if (any != null)
            {
                ox = ((any.Value.Left % cx) + cx) % cx;
                oy = ((any.Value.Top % cy) + cy) % cy;
            }
            return new Metrics(ox, oy, cx, cy);
        }

        public static (int col, int row) CellAt(Metrics m, int x, int y) =>
            ((int)Math.Floor((x - m.OriginX) / (double)m.Cx + 0.5),
             (int)Math.Floor((y - m.OriginY) / (double)m.Cy + 0.5));

        public static (int x, int y) PixelOf(Metrics m, int col, int row) =>
            (m.OriginX + col * m.Cx, m.OriginY + row * m.Cy);
    }

    /// <summary>Faz uma janela se comportar como parte da área de trabalho (fora do Alt+Tab, no fundo).</summary>
    internal static class DesktopWindow
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;   // não aparece na barra de tarefas nem no Alt+Tab
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        /// <summary>Fora do Alt+Tab e da barra de tarefas, sempre atrás de qualquer app aberto.</summary>
        public static void MakeDesktopLike(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var src = HwndSource.FromHwnd(hwnd);
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW));
            src?.AddHook(Hook);
        }

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>Move a janela (pixels físicos) sem mudar tamanho, ordem ou foco.</summary>
        public static void MoveTo(Window w, int x, int y)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public static (int x, int y) CursorPixels()
        {
            GetCursorPos(out var p);
            return (p.x, p.y);
        }

        public static (int x, int y) PositionPixels(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            return GetWindowRect(hwnd, out var r) ? (r.left, r.top) : (0, 0);
        }
    }
}
