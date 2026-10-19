using System;

namespace ScreenFX.Engine.Filters
{
    /// <summary>
    /// Математика фильтров для CPU-валидации и тестов.
    /// Повторяет логику HLSL на C# для unit-тестов.
    /// </summary>
    public static class FilterPipeline
    {
        public static double Luminance(double r, double g, double b)
            => 0.2126 * r + 0.7152 * g + 0.0722 * b;

        public static (double r, double g, double b) ApplySaturation(double r, double g, double b, double sat)
        {
            double l = Luminance(r, g, b);
            return (l + (r - l) * sat, l + (g - l) * sat, l + (b - l) * sat);
        }

        public static (double r, double g, double b) ApplyExposure(double r, double g, double b, double exposure)
        {
            double mult = Math.Pow(2.0, exposure);
            return (r * mult, g * mult, b * mult);
        }

        public static (double r, double g, double b) ApplyNegative(double r, double g, double b)
            => (1.0 - r, 1.0 - g, 1.0 - b);

        public static (double h, double s, double v) RgbToHsv(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            double h = 0;
            if (d > 1e-10)
            {
                if (Math.Abs(max - r) < 1e-10) h = ((g - b) / d) % 6;
                else if (Math.Abs(max - g) < 1e-10) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6.0;
                if (h < 0) h += 1;
            }
            double s = max < 1e-10 ? 0 : d / max;
            double v = max;
            return (h, s, v);
        }

        public static (double r, double g, double b) HsvToRgb(double h, double s, double v)
        {
            h = h % 1.0; if (h < 0) h += 1;
            double c = v * s;
            double x = c * (1 - Math.Abs((h * 6) % 2 - 1));
            double m = v - c;
            double r=0,g=0,b=0;
            double hh = h * 6;
            if (hh < 1) { r = c; g = x; }
            else if (hh < 2) { r = x; g = c; }
            else if (hh < 3) { g = c; b = x; }
            else if (hh < 4) { g = x; b = c; }
            else if (hh < 5) { r = x; b = c; }
            else { r = c; b = x; }
            return (r + m, g + m, b + m);
        }

        public static (double r, double g, double b) ApplyHueShift(double r, double g, double b, double shift)
        {
            if (Math.Abs(shift) < 0.001) return (r, g, b);
            var (h, s, v) = RgbToHsv(r, g, b);
            h = (h + shift) % 1.0;
            return HsvToRgb(h, s, v);
        }

        public static (double r, double g, double b) ApplyTemperature(double r, double g, double b, double temp)
        {
            if (temp > 0)
            {
                double t = temp;
                double rw = r * (1 + 0.2 * t);
                double gw = g * (1 + 0.0 * t);
                double bw = b * (1 - 0.2 * t);
                // warm tint
                rw = rw * (1 + 0.1 * t);
                bw = bw * (1 - 0.1 * t);
                return (Math.Clamp(rw,0,1), Math.Clamp(gw,0,1), Math.Clamp(bw,0,1));
            }
            else if (temp < 0)
            {
                double t = -temp;
                double rw = r * (1 - 0.2 * t);
                double gw = g * (1 - 0.05 * t);
                double bw = b * (1 + 0.2 * t);
                return (Math.Clamp(rw,0,1), Math.Clamp(gw,0,1), Math.Clamp(bw,0,1));
            }
            return (r,g,b);
        }

        public static (double r, double g, double b) ApplyNightMode(double r, double g, double b, double strength)
        {
            if (strength <= 0) return (r,g,b);
            double nr = r * 1.05;
            double ng = g * 0.98;
            double nb = b * (1 - strength * 0.6);
            nr = r * (1 - strength) + nr * strength;
            ng = g * (1 - strength) + ng * strength;
            nb = b * (1 - strength) + nb * strength;
            return (Math.Clamp(nr,0,1), Math.Clamp(ng,0,1), Math.Clamp(nb,0,1));
        }

        public static (double r,double g,double b) ApplyHsv(double r,double g,double b,double hShift,double sScale,double vScale)
        {
            var (h,s,v) = RgbToHsv(r,g,b);
            h = (h + hShift) % 1.0; if (h<0) h+=1;
            s = Math.Clamp(s * sScale, 0,1);
            v = Math.Clamp(v * vScale, 0,1);
            return HsvToRgb(h,s,v);
        }

        /// <summary>
        /// Полный пайплайн в порядке: Exposure -> Temperature -> Night -> Saturation -> Hue -> HSV -> Negative
        /// (без spatial эффектов). Для тестов.
        /// </summary>
        public static (double r,double g,double b) ApplyColorPipeline(double r,double g,double b, FilterParams p)
        {
            if (!p.FiltersEnabled) return (r,g,b);
            var (cr,cg,cb) = ApplyExposure(r,g,b,p.Exposure);
            cr = Math.Clamp(cr,0,1); cg=Math.Clamp(cg,0,1); cb=Math.Clamp(cb,0,1);
            (cr,cg,cb) = ApplyTemperature(cr,cg,cb,p.Temperature);
            if (p.NightModeEnabled) (cr,cg,cb)=ApplyNightMode(cr,cg,cb,p.NightModeStrength);
            (cr,cg,cb)=ApplySaturation(cr,cg,cb,p.Saturation);
            (cr,cg,cb)=ApplyHueShift(cr,cg,cb,p.Hue);
            if (p.HsvEnabled) (cr,cg,cb)=ApplyHsv(cr,cg,cb,p.HsvHue,p.HsvSaturation,p.HsvValue);
            if (p.NegativeEnabled) (cr,cg,cb)=ApplyNegative(cr,cg,cb);
            cr=Math.Clamp(cr,0,1); cg=Math.Clamp(cg,0,1); cb=Math.Clamp(cb,0,1);
            return (cr,cg,cb);
        }

        public static bool IsNeutral(FilterParams p)
        {
            return Math.Abs(p.Saturation-1)<0.001
                && Math.Abs(p.Exposure)<0.001
                && !p.NegativeEnabled
                && !p.LetterboxEnabled
                && Math.Abs(p.Hue)<0.001
                && Math.Abs(p.Temperature)<0.001
                && !p.NightModeEnabled
                && !p.HalftoneEnabled
                && !p.SharpenEnabled
                && !p.CircularBlurEnabled
                && !p.BlurEnabled
                && !p.HsvEnabled;
        }

        /// <summary>
        /// Проверка letterbox финального слоя: полосы не фильтруются
        /// </summary>
        public static (double r,double g,double b) ApplyWithLetterbox(double r,double g,double b, double uvY, FilterParams p)
        {
            if (p.LetterboxEnabled && (uvY < p.LetterboxHeight || uvY > 1 - p.LetterboxHeight))
            {
                return (p.LetterboxColorR, p.LetterboxColorG, p.LetterboxColorB);
            }
            return ApplyColorPipeline(r,g,b,p);
        }
    }
}
