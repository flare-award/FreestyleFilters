using System;
using System.Runtime.InteropServices;

namespace ScreenFX.Engine.Filters
{
    /// <summary>
    /// Управляет всеми параметрами фильтров. Нейтральные значения не искажают изображение.
    /// Порядок обработки документирован в HLSL и здесь.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FilterParamsGpu
    {
        public float Saturation;
        public float Exposure;
        public float HueShift;
        public float Temperature;
        public float NightModeStrength;
        public int EnableNegative;
        public int EnableHalftone;
        public float HalftoneStrength;
        public float HalftoneDotSize;
        public float HalftoneContrast;
        public float HalftoneAngle;
        public float SharpenStrength;
        public float BlurStrength;
        public float CircularBlurRadius;
        public float CircularBlurStrength;
        public float CircularBlurSmoothness;
        public float CircularBlurCenterX;
        public float CircularBlurCenterY;
        public float ResolutionX;
        public float ResolutionY;
        public float HsvHue;
        public float HsvSaturation;
        public float HsvValue;
        public int LetterboxEnabled;
        public float LetterboxHeight;
        public float LetterboxAspect;
        public float LetterboxColorR;
        public float LetterboxColorG;
        public float LetterboxColorB;
        public float Pad0;
        public float Pad1X;
        public float Pad1Y;
    }

    public class FilterParams : ICloneable
    {
        // 3.1 Saturation 0..2, default 1
        private float _saturation = 1.0f;
        public float Saturation
        {
            get => _saturation;
            set => _saturation = Math.Clamp(value, 0f, 3f);
        }

        // 3.2 Exposure -3..+3, default 0
        private float _exposure = 0f;
        public float Exposure
        {
            get => _exposure;
            set => _exposure = Math.Clamp(value, -3f, 3f);
        }

        // 3.3 Negative
        public bool NegativeEnabled { get; set; } = false;

        // 3.4 Letterbox
        public bool LetterboxEnabled { get; set; } = false;
        private float _letterboxHeight = 0.08f; // 8% each side ~ 16:9 to 2.39:1?
        public float LetterboxHeight
        {
            get => _letterboxHeight;
            set => _letterboxHeight = Math.Clamp(value, 0.01f, 0.4f);
        }
        public float LetterboxAspect { get; set; } = 2.39f; // for UI presets
        // 0 = 16:9 (no bars), 2.39, 2.0, 1.85, 4:3 etc - handled via height
        public string LetterboxPreset { get; set; } = "Off";
        private float _letterboxColorR = 0f, _letterboxColorG = 0f, _letterboxColorB = 0f;
        public float LetterboxColorR { get => _letterboxColorR; set => _letterboxColorR = Math.Clamp(value, 0, 1); }
        public float LetterboxColorG { get => _letterboxColorG; set => _letterboxColorG = Math.Clamp(value, 0, 1); }
        public float LetterboxColorB { get => _letterboxColorB; set => _letterboxColorB = Math.Clamp(value, 0, 1); }

        // 3.5 Hue 0..1 (0..360)
        private float _hue = 0f;
        public float Hue
        {
            get => _hue;
            set { _hue = value % 1f; if (_hue < 0) _hue += 1f; }
        }
        // UI helper degrees
        public float HueDegrees
        {
            get => Hue * 360f;
            set => Hue = (value % 360f) / 360f;
        }

        // 3.6 Temperature -1..1, 0 neutral
        private float _temperature = 0f;
        public float Temperature
        {
            get => _temperature;
            set => _temperature = Math.Clamp(value, -1f, 1f);
        }

        // 3.7 Night Mode 0..1, bool + strength
        public bool NightModeEnabled { get; set; } = false;
        private float _nightModeStrength = 0.5f;
        public float NightModeStrength
        {
            get => _nightModeStrength;
            set => _nightModeStrength = Math.Clamp(value, 0f, 1f);
        }

        // 3.8 Halftone
        public bool HalftoneEnabled { get; set; } = false;
        private float _halftoneStrength = 0.6f;
        public float HalftoneStrength { get => _halftoneStrength; set => _halftoneStrength = Math.Clamp(value, 0f, 1f); }
        private float _halftoneDotSize = 6f;
        public float HalftoneDotSize { get => _halftoneDotSize; set => _halftoneDotSize = Math.Clamp(value, 1f, 24f); }
        private float _halftoneContrast = 1.0f;
        public float HalftoneContrast { get => _halftoneContrast; set => _halftoneContrast = Math.Clamp(value, 0.2f, 3f); }
        private float _halftoneAngle = 45f;
        public float HalftoneAngle { get => _halftoneAngle; set => _halftoneAngle = value % 360f; }

        // 3.9 Sharpen 0..1
        public bool SharpenEnabled { get; set; } = false;
        private float _sharpenStrength = 0.35f;
        public float SharpenStrength { get => _sharpenStrength; set => _sharpenStrength = Math.Clamp(value, 0f, 1f); }

        // 3.10 Circular Blur
        public bool CircularBlurEnabled { get; set; } = false;
        private float _circularRadius = 0.25f; // relative to min dimension
        public float CircularBlurRadius { get => _circularRadius; set => _circularRadius = Math.Clamp(value, 0.05f, 0.8f); }
        private float _circularBlurStrength = 0.8f;
        public float CircularBlurStrength { get => _circularBlurStrength; set => _circularBlurStrength = Math.Clamp(value, 0f, 1f); }
        private float _circularSmoothness = 0.12f;
        public float CircularBlurSmoothness { get => _circularSmoothness; set => _circularSmoothness = Math.Clamp(value, 0.01f, 0.5f); }
        private float _circularCenterX = 0.5f;
        public float CircularBlurCenterX { get => _circularCenterX; set => _circularCenterX = Math.Clamp(value, 0f, 1f); }
        private float _circularCenterY = 0.5f;
        public float CircularBlurCenterY { get => _circularCenterY; set => _circularCenterY = Math.Clamp(value, 0f, 1f); }

        // 3.11 Fullscreen Blur
        public bool BlurEnabled { get; set; } = false;
        private float _blurStrength = 0.5f;
        public float BlurStrength { get => _blurStrength; set => _blurStrength = Math.Clamp(value, 0f, 1f); }

        // 3.12 HSV
        public bool HsvEnabled { get; set; } = false;
        private float _hsvHue = 0f;
        public float HsvHue { get => _hsvHue; set { _hsvHue = value % 1f; if (_hsvHue < 0) _hsvHue += 1; } }
        public float HsvHueDegrees { get => HsvHue * 360f; set => HsvHue = (value % 360f) / 360f; }
        private float _hsvSaturation = 1f;
        public float HsvSaturation { get => _hsvSaturation; set => _hsvSaturation = Math.Clamp(value, 0f, 3f); }
        private float _hsvValue = 1f;
        public float HsvValue { get => _hsvValue; set => _hsvValue = Math.Clamp(value, 0f, 3f); }

        // Global enable
        public bool FiltersEnabled { get; set; } = true;

        public FilterParamsGpu ToGpu(float resX, float resY)
        {
            // Если FiltersEnabled == false, все эффекты в нейтраль
            if (!FiltersEnabled)
            {
                return new FilterParamsGpu
                {
                    Saturation = 1, Exposure = 0, HueShift = 0, Temperature = 0,
                    NightModeStrength = 0, EnableNegative = 0, EnableHalftone = 0,
                    HalftoneStrength = 0, HalftoneDotSize = 4, HalftoneContrast = 1, HalftoneAngle = 45,
                    SharpenStrength = 0, BlurStrength = 0, CircularBlurRadius = 0.25f, CircularBlurStrength = 0,
                    CircularBlurSmoothness = 0.1f, CircularBlurCenterX = 0.5f, CircularBlurCenterY = 0.5f,
                    ResolutionX = resX, ResolutionY = resY,
                    HsvHue = 0, HsvSaturation = 1, HsvValue = 1,
                    LetterboxEnabled = 0, LetterboxHeight = 0, LetterboxAspect = 2.39f,
                    LetterboxColorR = 0, LetterboxColorG = 0, LetterboxColorB = 0
                };
            }

            return new FilterParamsGpu
            {
                Saturation = Saturation,
                Exposure = Exposure,
                HueShift = Hue,
                Temperature = Temperature,
                NightModeStrength = NightModeEnabled ? NightModeStrength : 0f,
                EnableNegative = NegativeEnabled ? 1 : 0,
                EnableHalftone = HalftoneEnabled ? 1 : 0,
                HalftoneStrength = HalftoneEnabled ? HalftoneStrength : 0f,
                HalftoneDotSize = HalftoneDotSize,
                HalftoneContrast = HalftoneContrast,
                HalftoneAngle = HalftoneAngle,
                SharpenStrength = SharpenEnabled ? SharpenStrength : 0f,
                BlurStrength = BlurEnabled ? BlurStrength : 0f,
                CircularBlurRadius = CircularBlurEnabled ? CircularBlurRadius : 0f,
                CircularBlurStrength = CircularBlurEnabled ? CircularBlurStrength : 0f,
                CircularBlurSmoothness = CircularBlurSmoothness,
                CircularBlurCenterX = CircularBlurCenterX,
                CircularBlurCenterY = CircularBlurCenterY,
                ResolutionX = resX,
                ResolutionY = resY,
                HsvHue = HsvEnabled ? HsvHue : 0f,
                HsvSaturation = HsvEnabled ? HsvSaturation : 1f,
                HsvValue = HsvEnabled ? HsvValue : 1f,
                LetterboxEnabled = LetterboxEnabled ? 1 : 0,
                LetterboxHeight = LetterboxHeight,
                LetterboxAspect = LetterboxAspect,
                LetterboxColorR = LetterboxColorR,
                LetterboxColorG = LetterboxColorG,
                LetterboxColorB = LetterboxColorB
            };
        }

        public void Reset()
        {
            Saturation = 1f;
            Exposure = 0f;
            NegativeEnabled = false;
            LetterboxEnabled = false;
            LetterboxHeight = 0.08f;
            LetterboxPreset = "Off";
            LetterboxColorR = LetterboxColorG = LetterboxColorB = 0f;
            Hue = 0f;
            Temperature = 0f;
            NightModeEnabled = false;
            NightModeStrength = 0.5f;
            HalftoneEnabled = false;
            HalftoneStrength = 0.6f;
            HalftoneDotSize = 6f;
            HalftoneContrast = 1f;
            HalftoneAngle = 45f;
            SharpenEnabled = false;
            SharpenStrength = 0.35f;
            CircularBlurEnabled = false;
            CircularBlurRadius = 0.25f;
            CircularBlurStrength = 0.8f;
            CircularBlurSmoothness = 0.12f;
            CircularBlurCenterX = 0.5f;
            CircularBlurCenterY = 0.5f;
            BlurEnabled = false;
            BlurStrength = 0.5f;
            HsvEnabled = false;
            HsvHue = 0f;
            HsvSaturation = 1f;
            HsvValue = 1f;
            FiltersEnabled = true;
        }

        public void ResetFilter(string filterName)
        {
            switch (filterName.ToLowerInvariant())
            {
                case "saturation": Saturation = 1f; break;
                case "exposure": Exposure = 0f; break;
                case "negative": NegativeEnabled = false; break;
                case "letterbox": LetterboxEnabled = false; LetterboxHeight = 0.08f; break;
                case "hue": Hue = 0f; break;
                case "temperature": Temperature = 0f; break;
                case "nightmode": NightModeEnabled = false; NightModeStrength = 0.5f; break;
                case "halftone": HalftoneEnabled = false; HalftoneStrength = 0.6f; HalftoneDotSize = 6f; HalftoneContrast = 1f; HalftoneAngle = 45f; break;
                case "sharpen": SharpenEnabled = false; SharpenStrength = 0.35f; break;
                case "circularblur": CircularBlurEnabled = false; CircularBlurRadius = 0.25f; CircularBlurStrength = 0.8f; CircularBlurSmoothness = 0.12f; CircularBlurCenterX = 0.5f; CircularBlurCenterY = 0.5f; break;
                case "blur": BlurEnabled = false; BlurStrength = 0.5f; break;
                case "hsv": HsvEnabled = false; HsvHue = 0f; HsvSaturation = 1f; HsvValue = 1f; break;
            }
        }

        public object Clone()
        {
            return MemberwiseClone();
        }

        public FilterParams DeepClone()
        {
            return (FilterParams)MemberwiseClone();
        }

        // Валидация после загрузки из JSON
        public void Validate()
        {
            // Триггеры сеттеров для clamp
            Saturation = Saturation;
            Exposure = Exposure;
            Hue = Hue;
            Temperature = Temperature;
            NightModeStrength = NightModeStrength;
            HalftoneStrength = HalftoneStrength;
            HalftoneDotSize = HalftoneDotSize;
            HalftoneContrast = HalftoneContrast;
            LetterboxHeight = LetterboxHeight;
            SharpenStrength = SharpenStrength;
            CircularBlurRadius = CircularBlurRadius;
            CircularBlurStrength = CircularBlurStrength;
            CircularBlurSmoothness = CircularBlurSmoothness;
            CircularBlurCenterX = CircularBlurCenterX;
            CircularBlurCenterY = CircularBlurCenterY;
            BlurStrength = BlurStrength;
            HsvHue = HsvHue;
            HsvSaturation = HsvSaturation;
            HsvValue = HsvValue;
        }
    }
}
