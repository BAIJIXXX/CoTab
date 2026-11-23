using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoTab.Services;

public static class ThemeColorExtractor
{
    /// <summary>
    /// Extracts a dominant, reasonably vivid color from a bitmap and adjusts it so it
    /// remains readable on the supplied panel color.
    /// </summary>
    public static Color Extract(BitmapSource source, Color panelColor)
    {
        try
        {
            var scaled = Resize(source, 84);
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            var samples = new List<Sample>(width * height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * stride + x * 4;
                    var b = pixels[index] / 255.0;
                    var g = pixels[index + 1] / 255.0;
                    var r = pixels[index + 2] / 255.0;
                    var a = pixels[index + 3] / 255.0;
                    if (a < 0.5) continue;

                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));
                    var delta = max - min;
                    var saturation = max <= 0.0001 ? 0 : delta / max;
                    var weight = 0.22 + saturation * 0.78;
                    if (max < 0.06) weight *= 0.35;
                    samples.Add(new Sample(r, g, b, saturation, max, weight));
                }
            }

            if (samples.Count == 0) return Fallback(panelColor);

            const int k = 5;
            var centers = InitializeCenters(samples, k);
            var assignments = new int[samples.Count];

            for (var iteration = 0; iteration < 9; iteration++)
            {
                for (var i = 0; i < samples.Count; i++)
                {
                    var best = 0;
                    var bestDistance = double.MaxValue;
                    for (var c = 0; c < centers.Length; c++)
                    {
                        var dr = samples[i].R - centers[c].R;
                        var dg = samples[i].G - centers[c].G;
                        var db = samples[i].B - centers[c].B;
                        var distance = dr * dr + dg * dg + db * db;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = c;
                        }
                    }
                    assignments[i] = best;
                }

                var newCenters = new (double R, double G, double B, double Weight)[k];
                for (var i = 0; i < samples.Count; i++)
                {
                    var c = assignments[i];
                    var sample = samples[i];
                    newCenters[c].R += sample.R * sample.Weight;
                    newCenters[c].G += sample.G * sample.Weight;
                    newCenters[c].B += sample.B * sample.Weight;
                    newCenters[c].Weight += sample.Weight;
                }
                for (var c = 0; c < k; c++)
                {
                    if (newCenters[c].Weight <= 0.0001) continue;
                    centers[c] = (newCenters[c].R / newCenters[c].Weight,
                        newCenters[c].G / newCenters[c].Weight,
                        newCenters[c].B / newCenters[c].Weight);
                }
            }

            var clusterWeights = new double[k];
            var clusterSaturation = new double[k];
            var clusterValue = new double[k];
            for (var i = 0; i < samples.Count; i++)
            {
                var c = assignments[i];
                clusterWeights[c] += samples[i].Weight;
                clusterSaturation[c] += samples[i].Saturation * samples[i].Weight;
                clusterValue[c] += samples[i].Value * samples[i].Weight;
            }

            var bestCluster = -1;
            var bestScore = -1.0;
            for (var c = 0; c < k; c++)
            {
                if (clusterWeights[c] < 0.001) continue;
                var saturation = clusterSaturation[c] / clusterWeights[c];
                var value = clusterValue[c] / clusterWeights[c];
                // Favour a vivid, mid-tone accent over huge near-black or near-white areas:
                // a dark wallpaper otherwise wins by area and produces a near-black accent.
                var vivid = Math.Clamp((saturation - 0.12) / 0.88, 0, 1);
                var midTone = 1 - Math.Min(1, Math.Abs(value - 0.58) / 0.58);
                var score = Math.Pow(clusterWeights[c], 0.35) *
                            (0.25 + vivid) *
                            (0.35 + midTone * 0.65);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCluster = c;
                }
            }

            Color extracted;
            if (bestCluster < 0)
            {
                extracted = Fallback(panelColor);
            }
            else
            {
                var center = centers[bestCluster];
                extracted = Color.FromRgb(ToByte(center.R), ToByte(center.G), ToByte(center.B));
                var (hue, saturation, value) = Helpers.ColorUtil.ToHsv(extracted);
                if (saturation < 0.32)
                {
                    saturation = 0.32;
                    value = Math.Clamp(value, 0.32, 0.78);
                    extracted = Helpers.ColorUtil.FromHsv(hue, saturation, value);
                }
            }

            extracted = Helpers.ColorUtil.EnsureContrast(extracted, panelColor, 4.6);
            return extracted;
        }
        catch
        {
            return Fallback(panelColor);
        }
    }

    private static Color Fallback(Color panelColor)
    {
        var color = Color.FromRgb(0x8A, 0x6A, 0x2F);
        return Helpers.ColorUtil.EnsureContrast(color, panelColor, 4.6);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static (double R, double G, double B)[] InitializeCenters(List<Sample> samples, int k)
    {
        var centers = new (double R, double G, double B)[k];
        for (var c = 0; c < k; c++)
        {
            var sample = samples[(c * samples.Count) / k + samples.Count / (k * 2)];
            centers[c] = (sample.R, sample.G, sample.B);
        }
        return centers;
    }

    private static BitmapSource Resize(BitmapSource source, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale >= 0.999) return source;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private readonly record struct Sample(double R, double G, double B, double Saturation, double Value, double Weight);
}
