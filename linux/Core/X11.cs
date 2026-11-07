using System;
using System.Runtime.InteropServices;

namespace Pastel
{
    /// <summary>
    /// Pedidos ao gerenciador de janelas pelo X11 (EWMH): deixar a janela no fundo, fora da barra de
    /// tarefas e em todas as áreas de trabalho, e ler a posição do mouse. Roda também no Plasma com
    /// Wayland, onde o Pastel usa XWayland (o KWin respeita esses pedidos para janelas X11).
    /// Qualquer falha é ignorada: sem X11 só perde o "fundo" e a posição do mouse.
    /// </summary>
    internal static class X11
    {
        private const string Lib = "libX11.so.6";

        [DllImport(Lib)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(Lib)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
        [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(Lib)] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, IntPtr ev);
        [DllImport(Lib)] private static extern int XFlush(IntPtr display);
        [DllImport(Lib)]
        private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr data, int nelements);
        [DllImport(Lib)]
        private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
            out int rootX, out int rootY, out int winX, out int winY, out uint mask);

        // XClientMessageEvent em Linux 64 bits (long = 8 bytes); o XEvent inteiro tem 192 bytes.
        [StructLayout(LayoutKind.Sequential)]
        private struct ClientMessage
        {
            public int type; public IntPtr serial; public int send_event; public IntPtr display;
            public IntPtr window; public IntPtr message_type; public int format;
            public IntPtr l0, l1, l2, l3, l4;
        }

        private const int ClientMessageType = 33;
        private static readonly IntPtr SubstructureMask = new((1 << 20) | (1 << 19)); // Redirect | Notify

        public static bool Available => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

        private static void Send(IntPtr display, IntPtr root, IntPtr window, string message, long a, long b, long c, long d)
        {
            var ev = new ClientMessage
            {
                type = ClientMessageType, send_event = 1, display = display, window = window,
                message_type = XInternAtom(display, message, false), format = 32,
                l0 = (IntPtr)a, l1 = (IntPtr)b, l2 = (IntPtr)c, l3 = (IntPtr)d, l4 = IntPtr.Zero
            };
            IntPtr buf = Marshal.AllocHGlobal(192);
            try
            {
                for (int i = 0; i < 192; i += 8) Marshal.WriteInt64(buf, i, 0);
                Marshal.StructureToPtr(ev, buf, false);
                XSendEvent(display, root, false, SubstructureMask, buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Janela da área de trabalho: no fundo, fora da barra de tarefas e do Alt+Tab, em todas as áreas.</summary>
        public static bool MakeDesktopLike(IntPtr xid)
        {
            if (!Available || xid == IntPtr.Zero) return false;
            try
            {
                IntPtr d = XOpenDisplay(IntPtr.Zero);
                if (d == IntPtr.Zero) return false;
                try
                {
                    IntPtr root = XDefaultRootWindow(d);
                    const int Add = 1;
                    IntPtr below = XInternAtom(d, "_NET_WM_STATE_BELOW", false);
                    IntPtr skipTask = XInternAtom(d, "_NET_WM_STATE_SKIP_TASKBAR", false);
                    IntPtr skipPager = XInternAtom(d, "_NET_WM_STATE_SKIP_PAGER", false);
                    // o pedido aceita 2 propriedades por vez
                    Send(d, root, xid, "_NET_WM_STATE", Add, (long)below, (long)skipTask, 1);
                    Send(d, root, xid, "_NET_WM_STATE", Add, (long)skipPager, 0, 1);
                    Send(d, root, xid, "_NET_WM_DESKTOP", 0xFFFFFFFFL, 1, 0, 0); // em todas as áreas de trabalho
                    XFlush(d);
                    return true;
                }
                finally { XCloseDisplay(d); }
            }
            catch { return false; } // sem libX11 / sem servidor X
        }

        /// <summary>
        /// Estado inicial de uma janela AINDA NÃO ABERTA (é assim que o padrão EWMH manda para o gerenciador ler ao mapear):
        /// no fundo, fora da barra de tarefas e do Alt+Tab, em todas as áreas de trabalho. Mais confiável do que pedir depois.
        /// </summary>
        public static bool SetInitialDesktopState(IntPtr xid)
        {
            if (!Available || xid == IntPtr.Zero) return false;
            try
            {
                IntPtr d = XOpenDisplay(IntPtr.Zero);
                if (d == IntPtr.Zero) return false;
                try
                {
                    var atoms = new[]
                    {
                        XInternAtom(d, "_NET_WM_STATE_BELOW", false),
                        XInternAtom(d, "_NET_WM_STATE_SKIP_TASKBAR", false),
                        XInternAtom(d, "_NET_WM_STATE_SKIP_PAGER", false),
                        XInternAtom(d, "_NET_WM_STATE_STICKY", false)
                    };
                    IntPtr buf = Marshal.AllocHGlobal(atoms.Length * IntPtr.Size);
                    try
                    {
                        for (int i = 0; i < atoms.Length; i++) Marshal.WriteIntPtr(buf, i * IntPtr.Size, atoms[i]);
                        XChangeProperty(d, xid, XInternAtom(d, "_NET_WM_STATE", false), (IntPtr)4 /*XA_ATOM*/, 32, 0 /*Replace*/, buf, atoms.Length);
                    }
                    finally { Marshal.FreeHGlobal(buf); }

                    IntPtr desk = Marshal.AllocHGlobal(IntPtr.Size);
                    try
                    {
                        Marshal.WriteIntPtr(desk, (IntPtr)0xFFFFFFFFL); // todas as áreas de trabalho
                        XChangeProperty(d, xid, XInternAtom(d, "_NET_WM_DESKTOP", false), (IntPtr)6 /*XA_CARDINAL*/, 32, 0, desk, 1);
                    }
                    finally { Marshal.FreeHGlobal(desk); }
                    XFlush(d);
                    return true;
                }
                finally { XCloseDisplay(d); }
            }
            catch { return false; }
        }

        /// <summary>Posição do mouse em pixels da tela (no Wayland puro não existe; no XWayland pode estar defasada).</summary>
        public static bool TryGetPointer(out int x, out int y)
        {
            x = y = 0;
            if (!Available) return false;
            try
            {
                IntPtr d = XOpenDisplay(IntPtr.Zero);
                if (d == IntPtr.Zero) return false;
                try
                {
                    bool ok = XQueryPointer(d, XDefaultRootWindow(d), out _, out _, out x, out y, out _, out _, out _);
                    return ok;
                }
                finally { XCloseDisplay(d); }
            }
            catch { return false; }
        }
    }
}
