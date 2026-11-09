using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Pastel
{
    /// <summary>Ícones já procurados e padronizados, por tamanho e tema (claro/escuro).</summary>
    internal static class IconCache
    {
        private static readonly Dictionary<string, SKBitmap?> Styled = new();
        private static readonly Dictionary<string, Bitmap?> Avalonia = new();

        public static void Clear()
        {
            lock (Styled) { Styled.Clear(); Avalonia.Clear(); }
        }

        /// <summary>O ícone do item no estilo chapado arredondado, em skia (para desenhar o ícone da pasta).</summary>
        public static SKBitmap? GetStyled(ShortcutItem item, int size)
        {
            bool light = PastelTheme.IsLight;
            string key = $"{item.Path}|{size}|{(light ? "L" : "D")}";
            lock (Styled)
            {
                if (Styled.TryGetValue(key, out var hit)) return hit;
                SKBitmap? result = null;
                try
                {
                    var file = IconResolver.ResolveForItem(item);
                    // carrega grande e reduz depois: nítido mesmo nos ícones pequenos
                    var raw = file == null ? null : IconResolver.Load(file, 256);
                    // ícone de baixa resolução / com grande margem (< 160px visíveis): tenta uma versão maior do tema
                    if (file != null && (raw == null || IconStyler.ContentExtent(raw) < 160))
                    {
                        int best = raw == null ? 0 : IconStyler.ContentExtent(raw);
                        foreach (var alt in IconResolver.Alternatives(file).Take(4))
                        {
                            var cand = IconResolver.Load(alt, 256);
                            if (cand == null) continue;
                            int ext = IconStyler.ContentExtent(cand);
                            if (ext > best) { raw?.Dispose(); raw = cand; best = ext; } else cand.Dispose();
                            if (best >= 160) break;
                        }
                    }
                    using var rawOwner = raw;
                    if (raw != null) result = IconStyler.Style(raw, size, light);
                }
                catch { }
                Styled[key] = result;
                return result;
            }
        }

        /// <summary>O mesmo ícone como imagem do Avalonia, para as janelas.</summary>
        public static Bitmap? GetBitmap(ShortcutItem item, int size)
        {
            bool light = PastelTheme.IsLight;
            string key = $"{item.Path}|{size}|{(light ? "L" : "D")}";
            lock (Styled)
            {
                if (Avalonia.TryGetValue(key, out var hit)) return hit;
            }
            var sk = GetStyled(item, size);
            Bitmap? bmp = null;
            if (sk != null)
            {
                try
                {
                    using var img = SKImage.FromBitmap(sk);
                    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    bmp = new Bitmap(new MemoryStream(data.ToArray()));
                }
                catch { }
            }
            lock (Styled) { Avalonia[key] = bmp; }
            return bmp;
        }
    }
}
