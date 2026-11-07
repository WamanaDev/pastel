using System;
using System.Linq;
using SkiaSharp;

namespace Pastel
{
    /// <summary>O ícone da pasta na área de trabalho: vidro translúcido no tema do KDE com a grade 2x2 dos apps de dentro.</summary>
    internal static class FolderIcon
    {
        private static SKColor ToSk(Avalonia.Media.Color c, byte? alpha = null) => new(c.R, c.G, c.B, alpha ?? c.A);

        /// <summary>PNG da pasta (o KDE reduz o que for preciso; 256 px fica nítido em qualquer escala).</summary>
        public static byte[] RenderPng(FolderModel f, int size = 256)
        {
            using var bmp = Render(f, size);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        public static SKBitmap Render(FolderModel f, int size = 256)
        {
            float s = size / 256f;
            bool light = PastelTheme.IsLight;
            var accent = PastelTheme.Visible(PastelTheme.Accent, light);
            var baseColor = light ? Avalonia.Media.Color.FromRgb(0xFF, 0xFF, 0xFF) : Avalonia.Media.Color.FromRgb(0x1E, 0x22, 0x2C);
            var glass = PastelTheme.Mix(baseColor, accent, 0.18);

            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var rect = new SKRect(12 * s, 12 * s, (256 - 12) * s, (256 - 12) * s);
            using (var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = ToSk(glass, light ? (byte)0xC4 : (byte)0xB4) })
                canvas.DrawRoundRect(rect, 36 * s, 36 * s, fill);
            using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5 * s, Color = ToSk(accent, 0xA0) })
                canvas.DrawRoundRect(rect, 36 * s, 36 * s, edge);

            using var paint = new SKPaint { IsAntialias = true };
            var smooth = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            int i = 0;
            foreach (var item in f.Items.Take(4))
            {
                var icon = IconCache.GetStyled(item, 192); // já arredondado e padronizado
                if (icon != null)
                {
                    using var img = SKImage.FromBitmap(icon);
                    float x = (29 + (i % 2) * 104) * s, y = (29 + (i / 2) * 104) * s;
                    canvas.DrawImage(img, new SKRect(0, 0, icon.Width, icon.Height), new SKRect(x, y, x + 94 * s, y + 94 * s), smooth, paint);
                }
                i++;
            }
            return SKBitmap.FromImage(surface.Snapshot());
        }
    }
}
