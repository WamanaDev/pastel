using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Pastel
{
    /// <summary>Extrai o ícone de qualquer arquivo/atalho usando o próprio Shell do Windows.</summary>
    public static class IconService
    {
        private static readonly Dictionary<string, BitmapSource?> Cache = new();

        private static readonly Dictionary<string, BitmapSource?> StyledCache = new();

        /// <summary>
        /// Ícone no estilo chapado arredondado, renderizado em alta qualidade a partir da maior
        /// versão disponível. Usar só na thread da interface.
        /// </summary>
        public static BitmapSource? GetStyled(string path, int size)
        {
            bool light = WindowsTheme.IsLight;
            var key = path.ToLowerInvariant() + "|" + size + "|" + (light ? "L" : "D");
            if (StyledCache.TryGetValue(key, out var hit)) return hit;

            var raw = Get(path, 256);
            BitmapSource? styled = null;
            if (raw != null)
            {
                try { styled = IconStyler.Style(raw, size, light); }
                catch { styled = raw; }
            }
            StyledCache[key] = styled;
            return styled;
        }

        /// <summary>Esquece os ícones já estilizados (quando o tema muda ou um arquivo é trocado).</summary>
        public static void ClearStyled() => StyledCache.Clear();

        public static BitmapSource? Get(string path, int size = 96)
        {
            var key = path.ToLowerInvariant() + "|" + size;
            if (Cache.TryGetValue(key, out var hit)) return hit;
            var img = FromShell(path, size) ?? FromAssociated(path);
            Cache[key] = img;
            return img;
        }

        private static BitmapSource? FromShell(string path, int size)
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                // ICONONLY (4) | BIGGERSIZEOK (1): sempre o ícone, nunca miniatura de conteúdo
                int hr = factory.GetImage(new SIZE { cx = size, cy = size }, 0x5, out var hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(
                        hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DeleteObject(hbm); }
            }
            catch { return null; }
        }

        private static BitmapSource? FromAssociated(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }
    }
}
