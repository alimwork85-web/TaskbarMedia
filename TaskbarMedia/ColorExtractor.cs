using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarMedia;

/// <summary>Finds the dominant, most "colorful" color of a cover and turns it into a readable background.</summary>
internal static class ColorExtractor
{
    private const int SampleSize = 24;
    private const int HueBins = 12;

    /// <summary>Returns the cover's dominant color, or null for covers that are essentially grey/black/white.</summary>
    public static Color? Dominant(BitmapSource cover)
    {
        try
        {
            var small = new TransformedBitmap(cover,
                new ScaleTransform((double)SampleSize / cover.PixelWidth, (double)SampleSize / cover.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            // Weighted color sums per hue bin; vivid pixels count more than dull ones.
            var weight = new double[HueBins];
            var sumR = new double[HueBins];
            var sumG = new double[HueBins];
            var sumB = new double[HueBins];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                if (a < 128) continue;

                ToHsv(r, g, b, out double hue, out double sat, out double val);
                if (val < 0.08) continue;                // near black
                if (val > 0.95 && sat < 0.10) continue;  // near white

                double wgt = sat * val + 0.02;
                int bin = (int)(hue / 360.0 * HueBins) % HueBins;
                weight[bin] += wgt;
                sumR[bin] += r * wgt;
                sumG[bin] += g * wgt;
                sumB[bin] += b * wgt;
            }

            int best = 0;
            for (int i = 1; i < HueBins; i++)
                if (weight[i] > weight[best]) best = i;
            if (weight[best] <= 0) return null;

            var color = Color.FromRgb(
                (byte)(sumR[best] / weight[best]),
                (byte)(sumG[best] / weight[best]),
                (byte)(sumB[best] / weight[best]));

            ToHsv(color.R, color.G, color.B, out _, out double s, out _);
            return s < 0.12 ? null : color;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Tones a cover color down so it works as a panel background (not too bright, not too saturated).</summary>
    public static Color ToBackground(Color c)
    {
        ToHsv(c.R, c.G, c.B, out double h, out double s, out double v);
        return FromHsv(h, Math.Min(s, 0.65), Math.Clamp(v, 0.28, 0.50));
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    public static Color TextOn(Color background)
    {
        double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        return luminance > 0.55 ? Colors.Black : Colors.White;
    }

    private static void ToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        double d = max - min;

        h = d == 0 ? 0
          : max == rd ? 60 * (((gd - bd) / d + 6) % 6)
          : max == gd ? 60 * ((bd - rd) / d + 2)
          : 60 * ((rd - gd) / d + 4);
        s = max == 0 ? 0 : d / max;
        v = max;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
