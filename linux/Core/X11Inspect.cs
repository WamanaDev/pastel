using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Pastel
{
    internal sealed record XWinInfo(IntPtr Id, string? Name, int X, int Y, int W, int H, int Depth, string[] State);

    /// <summary>Lê de volta, pelo X11, o que o gerenciador de janelas realmente fez com as janelas (usado só nos testes).</summary>
    internal static class X11Inspect
    {
        private const string Lib = "libX11.so.6";

        [DllImport(Lib)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr d);
        [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr d);
        [DllImport(Lib)] private static extern IntPtr XInternAtom(IntPtr d, string name, bool onlyIfExists);
        [DllImport(Lib)] private static extern IntPtr XGetAtomName(IntPtr d, IntPtr atom);
        [DllImport(Lib)] private static extern int XFree(IntPtr p);
        [DllImport(Lib)] private static extern int XQueryTree(IntPtr d, IntPtr w, out IntPtr root, out IntPtr parent, out IntPtr children, out uint n);
        [DllImport(Lib)] private static extern int XFetchName(IntPtr d, IntPtr w, out IntPtr name);
        [DllImport(Lib)] private static extern int XGetGeometry(IntPtr d, IntPtr w, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
        [DllImport(Lib)] private static extern bool XTranslateCoordinates(IntPtr d, IntPtr src, IntPtr dst, int sx, int sy, out int dx, out int dy, out IntPtr child);
        [DllImport(Lib)]
        private static extern int XGetWindowProperty(IntPtr d, IntPtr w, IntPtr prop, IntPtr offset, IntPtr length, bool delete, IntPtr reqType,
            out IntPtr actualType, out int actualFormat, out IntPtr nitems, out IntPtr bytesAfter, out IntPtr data);

        private static string[] ReadAtoms(IntPtr d, IntPtr w, string property)
        {
            var result = new List<string>();
            IntPtr prop = XInternAtom(d, property, false);
            if (XGetWindowProperty(d, w, prop, IntPtr.Zero, (IntPtr)64, false, IntPtr.Zero, out _, out int fmt, out IntPtr n, out _, out IntPtr data) != 0 || data == IntPtr.Zero)
                return Array.Empty<string>();
            try
            {
                if (fmt == 32)
                    for (long i = 0; i < (long)n; i++)
                    {
                        IntPtr atom = Marshal.ReadIntPtr(data, (int)i * IntPtr.Size);
                        IntPtr name = XGetAtomName(d, atom);
                        if (name != IntPtr.Zero) { result.Add(Marshal.PtrToStringAnsi(name) ?? ""); XFree(name); }
                    }
            }
            finally { XFree(data); }
            return result.ToArray();
        }

        /// <summary>O _NET_WM_STATE gravado em uma janela (vazio se não houver).</summary>
        public static string[] StateOf(IntPtr xid)
        {
            IntPtr d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) return Array.Empty<string>();
            try { return ReadAtoms(d, xid, "_NET_WM_STATE"); }
            finally { XCloseDisplay(d); }
        }

        public static List<XWinInfo> TopLevels()
        {
            var list = new List<XWinInfo>();
            IntPtr d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero) return list;
            try
            {
                IntPtr root = XDefaultRootWindow(d);
                void Visit(IntPtr w, int depth)
                {
                    if (XQueryTree(d, w, out _, out _, out IntPtr kids, out uint n) == 0) return;
                    try
                    {
                        for (int i = 0; i < n; i++)
                        {
                            IntPtr c = Marshal.ReadIntPtr(kids, i * IntPtr.Size);
                            string? name = null;
                            if (XFetchName(d, c, out IntPtr np) != 0 && np != IntPtr.Zero) { name = Marshal.PtrToStringAnsi(np); XFree(np); }
                            if (name != null && XGetGeometry(d, c, out _, out _, out _, out uint wd, out uint ht, out _, out uint dp) != 0)
                            {
                                XTranslateCoordinates(d, c, root, 0, 0, out int rx, out int ry, out _);
                                list.Add(new XWinInfo(c, name, rx, ry, (int)wd, (int)ht, (int)dp, ReadAtoms(d, c, "_NET_WM_STATE")));
                            }
                            if (depth < 2) Visit(c, depth + 1);
                        }
                    }
                    finally { if (kids != IntPtr.Zero) XFree(kids); }
                }
                Visit(root, 0);
            }
            finally { XCloseDisplay(d); }
            return list;
        }
    }
}
