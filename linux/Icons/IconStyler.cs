using System;
using SkiaSharp;

namespace Pastel
{
    /// <summary>
    /// Padroniza o ícone de um app no estilo "chapado arredondado" do Android (o mesmo do Pastel do Windows):
    ///  - ícone que já é um quadrado cheio (fundo opaco, às vezes com uma borda esquisita): recorta a borda e
    ///    arredonda os cantos;
    ///  - ícone com fundo transparente (só o desenho): centraliza o desenho num quadrado arredondado neutro,
    ///    claro se o desenho é escuro e escuro se o desenho é claro.
    /// </summary>
    internal static class IconStyler
    {
        private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        public static SKBitmap Style(SKBitmap src, int size, bool light)
        {
            int w = src.Width, h = src.Height;
            var px = src.Pixels; // SKColor sem pré-multiplicação: serve para medir cor e transparência

            // --- análise: borda opaca? onde está o desenho? claro ou escuro? ---
            int ring = Math.Max(1, Math.Min(w, h) / 20);
            long ringTotal = 0, ringOpaque = 0, lumCount = 0;
            double lumSum = 0;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    byte a = c.Alpha;
                    if (a > 24)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                    if (a > 128) { lumSum += 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue; lumCount++; }
                    if (x < ring || y < ring || x >= w - ring || y >= h - ring) { ringTotal++; if (a >= 200) ringOpaque++; }
                }
            }

            double lum = lumCount > 0 ? lumSum / lumCount : 128;
            bool hasContent = maxX >= minX && maxY >= minY;
            bool fullBleed = hasContent && ringTotal > 0 && ringOpaque / (double)ringTotal >= 0.6 &&
                             (maxX - minX + 1) >= w * 0.9 && (maxY - minY + 1) >= h * 0.9;

            // --- desenho ---
            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            float radius = size * 0.22f;
            var tile = new SKRect(0, 0, size, size);
            using var image = SKImage.FromBitmap(src);
            using var paint = new SKPaint { IsAntialias = true };

            if (fullBleed)
            {
                // tira ~4% de cada lado (borda/sombra desenhada no próprio ícone) e arredonda
                int inset = (int)Math.Round(Math.Min(w, h) * 0.04);
                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(tile, radius, radius), SKClipOperation.Intersect, true);
                canvas.DrawImage(image, new SKRect(inset, inset, w - inset, h - inset), tile, Smooth, paint);
                canvas.Restore();
            }
            else
            {
                var bg = lum >= 170 ? new SKColor(0x2B, 0x2F, 0x3B)
                       : lum <= 90 ? new SKColor(0xF2, 0xF0, 0xF8)
                       : (light ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0x3A, 0x3E, 0x4C));
                paint.Color = bg;
                canvas.DrawRoundRect(tile, radius, radius, paint);
                paint.Color = SKColors.White;

                if (hasContent)
                {
                    int cw = maxX - minX + 1, ch = maxY - minY + 1;
                    float scale = size * 0.66f / Math.Max(cw, ch);
                    float dw = cw * scale, dh = ch * scale;
                    canvas.DrawImage(image, new SKRect(minX, minY, maxX + 1, maxY + 1),
                        new SKRect((size - dw) / 2f, (size - dh) / 2f, (size + dw) / 2f, (size + dh) / 2f), Smooth, paint);
                }
            }

            return SKBitmap.FromImage(surface.Snapshot());
        }
    }
}
