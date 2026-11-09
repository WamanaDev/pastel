using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pastel
{
    /// <summary>
    /// Padroniza o ícone de um app no estilo "chapado arredondado" do Android:
    ///  - ícone que já é um quadrado cheio (fundo opaco, às vezes com uma borda esquisita): recorta a
    ///    borda e arredonda os cantos;
    ///  - ícone com fundo transparente (só o desenho): centraliza o desenho num quadrado arredondado
    ///    de cor neutra, claro se o desenho é escuro e escuro se o desenho é claro.
    /// Tudo renderizado em alta qualidade a partir da maior versão do ícone.
    /// </summary>
    internal static class IconStyler
    {
        public static BitmapSource Style(BitmapSource src, int size, bool light)
        {
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);

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
                    int i = (y * w + x) * 4;
                    byte a = px[i + 3];
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
                    if (a > 128)
                    {
                        lumSum += 0.2126 * px[i + 2] + 0.7152 * px[i + 1] + 0.0722 * px[i];
                        lumCount++;
                    }
                    if (x < ring || y < ring || x >= w - ring || y >= h - ring)
                    {
                        ringTotal++;
                        if (a >= 200) { ringOpaque++; rr += px[i + 2]; rg += px[i + 1]; rb += px[i]; }
                    }
                }
            }

            // sombras/halos fracos não contam: prefere o miolo sólido do desenho
            if (cMaxX >= cMinX && cMaxY >= cMinY)
            {
                int padX = Math.Max(1, (cMaxX - cMinX + 1) / 50), padY = Math.Max(1, (cMaxY - cMinY + 1) / 50);
                minX = Math.Max(0, cMinX - padX); maxX = Math.Min(w - 1, cMaxX + padX);
                minY = Math.Max(0, cMinY - padY); maxY = Math.Min(h - 1, cMaxY + padY);
            }

            double lum = lumCount > 0 ? lumSum / lumCount : 128;
            bool hasContent = maxX >= minX && maxY >= minY;
            bool fullBleed = hasContent && ringTotal > 0 &&
                             ringOpaque / (double)ringTotal >= 0.6 &&
                             (maxX - minX + 1) >= w * 0.9 && (maxY - minY + 1) >= h * 0.9;

            // fundo opaco uniforme com um glifo pequeno no meio (ícone de baixa resolução / com grande margem):
            // acha o bbox do que difere do fundo, para o glifo poder ocupar o tile
            bool solidBg = false;
            Color solidColor = default;
            int gMinX = w, gMinY = h, gMaxX = -1, gMaxY = -1;
            if (ringOpaque > 0 && ringOpaque / (double)ringTotal >= 0.9)
            {
                int br = (int)(rr / ringOpaque), bg2 = (int)(rg / ringOpaque), bb = (int)(rb / ringOpaque);
                long dev = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = (y * w + x) * 4;
                        bool edge = x < ring || y < ring || x >= w - ring || y >= h - ring;
                        int d = Math.Abs(px[i + 2] - br) + Math.Abs(px[i + 1] - bg2) + Math.Abs(px[i] - bb);
                        if (edge) { if (d > 36) dev++; continue; }
                        if (px[i + 3] > 24 && d > 60)
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
                    solidColor = Color.FromRgb((byte)br, (byte)bg2, (byte)bb);
                    int gp = Math.Max(1, Math.Max(gMaxX - gMinX, gMaxY - gMinY) / 40);
                    gMinX = Math.Max(0, gMinX - gp); gMinY = Math.Max(0, gMinY - gp);
                    gMaxX = Math.Min(w - 1, gMaxX + gp); gMaxY = Math.Min(h - 1, gMaxY + gp);
                }
            }
            if (solidBg) fullBleed = false;

            // --- desenho ---
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            double radius = size * 0.22;
            var tile = new Rect(0, 0, size, size);

            using (var dc = dv.RenderOpen())
            {
                if (fullBleed)
                {
                    // tira ~4% de cada lado (borda/sombra desenhada no próprio ícone) e arredonda
                    int inset = (int)Math.Round(Math.Min(w, h) * 0.04);
                    var crop = new CroppedBitmap(src, new Int32Rect(inset, inset, w - 2 * inset, h - 2 * inset));
                    dc.PushClip(new RectangleGeometry(tile, radius, radius));
                    dc.DrawImage(crop, tile);
                    dc.Pop();
                }
                else if (solidBg)
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(solidColor), null, tile, radius, radius);
                    int cw = gMaxX - gMinX + 1, ch = gMaxY - gMinY + 1;
                    var crop = new CroppedBitmap(src, new Int32Rect(gMinX, gMinY, cw, ch));
                    double scale = size * 0.66 / Math.Max(cw, ch);
                    double dw = cw * scale, dh = ch * scale;
                    dc.DrawImage(crop, new Rect((size - dw) / 2, (size - dh) / 2, dw, dh));
                }
                else
                {
                    Color bg = lum >= 170 ? Color.FromRgb(0x2B, 0x2F, 0x3B)
                             : lum <= 90 ? Color.FromRgb(0xF2, 0xF0, 0xF8)
                             : (light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x3A, 0x3E, 0x4C));
                    dc.DrawRoundedRectangle(new SolidColorBrush(bg), null, tile, radius, radius);

                    if (hasContent)
                    {
                        int cw = maxX - minX + 1, ch = maxY - minY + 1;
                        var crop = new CroppedBitmap(src, new Int32Rect(minX, minY, cw, ch));
                        double scale = size * 0.66 / Math.Max(cw, ch);
                        double dw = cw * scale, dh = ch * scale;
                        dc.DrawImage(crop, new Rect((size - dw) / 2, (size - dh) / 2, dw, dh));
                    }
                }
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
