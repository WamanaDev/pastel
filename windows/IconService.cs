using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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
            // ícone de baixa resolução / com grande margem: tenta a maior imagem do .ico/.exe do alvo
            try
            {
                if (raw == null || ContentExtent(raw) < 160)
                {
                    var big = FromExtracted(path);
                    if (big != null && (raw == null || ContentExtent(big) > ContentExtent(raw))) raw = big;
                }
            }
            catch { }
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

        /// <summary>Maior lado (em px) da área visível da imagem; mede o quanto de resolução útil ela tem.</summary>
        private static int ContentExtent(BitmapSource src)
        {
            var conv = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[(y * w + x) * 4 + 3] >= 128)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return maxX < minX ? 0 : Math.Max(maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>Extrai a maior imagem (até 256px) do local do ícone do atalho/.url/exe/ico.</summary>
        private static BitmapSource? FromExtracted(string path)
        {
            string? file = null;
            int index = 0;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".url")
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) file = t.Substring(9).Trim();
                        else if (t.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)) int.TryParse(t.Substring(10).Trim(), out index);
                    }
                }
                else if (ext == ".lnk")
                {
                    var link = (IShellLinkW)new ShellLinkCom();
                    ((IPersistFile)link).Load(path, 0);
                    var sb = new System.Text.StringBuilder(1024);
                    link.GetIconLocation(sb, sb.Capacity, out index);
                    file = sb.ToString();
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        var tp = new System.Text.StringBuilder(1024);
                        link.GetPath(tp, tp.Capacity, IntPtr.Zero, 0);
                        file = tp.ToString();
                        index = 0;
                    }
                }
                else if (ext is ".exe" or ".dll" or ".ico") file = path;

                if (string.IsNullOrWhiteSpace(file)) return null;
                file = Environment.ExpandEnvironmentVariables(file.Trim('"'));
                if (!File.Exists(file)) return null;
                if (Path.GetExtension(file).Equals(".url", StringComparison.OrdinalIgnoreCase)) return null;

                var icons = new IntPtr[1];
                var ids = new int[1];
                int n = PrivateExtractIcons(file, index, 256, 256, icons, ids, 1, 0);
                if (n < 1 || icons[0] == IntPtr.Zero) return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(icons[0]); }
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
            IntPtr[] phicon, int[] piconid, int nIcons, int flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCom { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        }

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
