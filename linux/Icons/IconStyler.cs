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
        // para ampliar glifos pequenos sem serrilhado nem borrão demais
        private static readonly SKSamplingOptions Sharp = new(SKCubicResampler.Mitchell);

        /// <summary>Maior lado (em px) da área sólida (alfa >= 128) da imagem: mede quanta resolução útil ela tem.</summary>
        public static int ContentExtent(SKBitmap src)
        {
            int w = src.Width, h = src.Height;
            var px = src.Pixels;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].Alpha >= 128)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return maxX < minX ? 0 : Math.Max(maxX - minX + 1, maxY - minY + 1);
        }

        public static SKBitmap Style(SKBitmap src, int size, bool light)
        {
            int w = src.Width, h = src.Height;
            var px = src.Pixels; // SKColor sem pré-multiplicação: serve para medir cor e transparência

            // --- análise: borda opaca? onde está o desenho? claro ou escuro? ---
            int ring = Math.Max(1, Math.Min(w, h) / 20);
            long ringTotal = 0, ringOpaque = 0, lumCount = 0;
            double lumSum = 0;
            int minX = w, minY = h, maxX = -1, maxY = -1;       // qualquer pixel visível (a > 24)
            int cMinX = w, cMinY = h, cMaxX = -1, cMaxY = -1;   // só o miolo sólido (a >= 128)
            long rr = 0, rg = 0, rb = 0;
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
                    if (a >= 128)
                    {
                        if (x < cMinX) cMinX = x;
                        if (x > cMaxX) cMaxX = x;
                        if (y < cMinY) cMinY = y;
                        if (y > cMaxY) cMaxY = y;
                    }
                    if (a > 128) { lumSum += 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue; lumCount++; }
                    if (x < ring || y < ring || x >= w - ring || y >= h - ring)
                    {
                        ringTotal++;
                        if (a >= 200) { ringOpaque++; rr += c.Red; rg += c.Green; rb += c.Blue; }
                    }
                }
            }

            // sombras/halos fracos não contam: prefere o miolo sólido do desenho (+2% de folga)
            if (cMaxX >= cMinX && cMaxY >= cMinY)
            {
                int padX = Math.Max(1, (cMaxX - cMinX + 1) / 50), padY = Math.Max(1, (cMaxY - cMinY + 1) / 50);
                minX = Math.Max(0, cMinX - padX); maxX = Math.Min(w - 1, cMaxX + padX);
                minY = Math.Max(0, cMinY - padY); maxY = Math.Min(h - 1, cMaxY + padY);
            }

            double lum = lumCount > 0 ? lumSum / lumCount : 128;
            bool hasContent = maxX >= minX && maxY >= minY;
            bool fullBleed = hasContent && ringTotal > 0 && ringOpaque / (double)ringTotal >= 0.6 &&
                             (maxX - minX + 1) >= w * 0.9 && (maxY - minY + 1) >= h * 0.9;

            // fundo opaco uniforme com um glifo pequeno no meio (ícone de baixa resolução / com grande margem):
            // acha o bbox do que difere do fundo, para o glifo poder ocupar o tile
            bool solidBg = false;
            SKColor solidColor = default;
            int gMinX = w, gMinY = h, gMaxX = -1, gMaxY = -1;
            if (ringOpaque > 0 && ringOpaque / (double)ringTotal >= 0.9)
            {
                int br = (int)(rr / ringOpaque), bg2 = (int)(rg / ringOpaque), bb = (int)(rb / ringOpaque);
                long dev = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = px[y * w + x];
                        bool edge = x < ring || y < ring || x >= w - ring || y >= h - ring;
                        int d = Math.Abs(c.Red - br) + Math.Abs(c.Green - bg2) + Math.Abs(c.Blue - bb);
                        if (edge) { if (d > 36) dev++; continue; }
                        if (c.Alpha > 24 && d > 60)
                        {
                            if (x < gMinX) gMinX = x;
                            if (x > gMaxX) gMaxX = x;
                            if (y < gMinY) gMinY = y;
                            if (y > gMaxY) gMaxY = y;
                        }
                    }
                if (dev < ringTotal * 0.05 && gMaxX >= gMinX && gMaxY >= gMinY &&
                    ((gMaxX - gMinX + 1) < w * 0.7 || (gMaxY - gMinY + 1) < h * 0.7))
                {
                    solidBg = true;
                    solidColor = new SKColor((byte)br, (byte)bg2, (byte)bb);
                    int gp = Math.Max(1, Math.Max(gMaxX - gMinX, gMaxY - gMinY) / 40);
                    gMinX = Math.Max(0, gMinX - gp); gMinY = Math.Max(0, gMinY - gp);
                    gMaxX = Math.Min(w - 1, gMaxX + gp); gMaxY = Math.Min(h - 1, gMaxY + gp);
                }
            }
            if (solidBg) fullBleed = false;

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
            else if (solidBg)
            {
                paint.Color = solidColor;
                canvas.DrawRoundRect(tile, radius, radius, paint);
                paint.Color = SKColors.White;
                int cw = gMaxX - gMinX + 1, ch = gMaxY - gMinY + 1;
                float scale = size * 0.66f / Math.Max(cw, ch);
                float dw = cw * scale, dh = ch * scale;
                canvas.DrawImage(image, new SKRect(gMinX, gMinY, gMaxX + 1, gMaxY + 1),
                    new SKRect((size - dw) / 2f, (size - dh) / 2f, (size + dw) / 2f, (size + dh) / 2f), Sharp, paint);
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
                        new SKRect((size - dw) / 2f, (size - dh) / 2f, (size + dw) / 2f, (size + dh) / 2f), Sharp, paint);
                }
            }

            return SKBitmap.FromImage(surface.Snapshot());
        }
    }
}
