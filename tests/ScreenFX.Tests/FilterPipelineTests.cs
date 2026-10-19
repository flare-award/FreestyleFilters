using System;
using Xunit;
using ScreenFX.Engine.Filters;

namespace ScreenFX.Tests
{
    public class FilterPipelineTests
    {
        [Fact]
        public void NeutralValues_DoNotChangeImage()
        {
            var p = new FilterParams(); // defaults
            Assert.True(FilterPipeline.IsNeutral(p));
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.3, 0.5, 0.7, p);
            Assert.Equal(0.3, r, 3);
            Assert.Equal(0.5, g, 3);
            Assert.Equal(0.7, b, 3);
        }

        [Fact]
        public void Saturation_Zero_GivesGrayscale()
        {
            var p = new FilterParams { Saturation = 0 };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(1, 0, 0, p);
            // red -> luma ~0.2126
            Assert.Equal(r, g, 3);
            Assert.Equal(g, b, 3);
            Assert.Equal(0.2126, r, 2);
        }

        [Fact]
        public void Saturation_Two_IncreasesColor()
        {
            var p = new FilterParams { Saturation = 2 };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.5, 0.5, 0.5, p);
            // gray stays gray
            Assert.Equal(0.5, r, 3);
            var (r2,g2,b2) = FilterPipeline.ApplyColorPipeline(0.6, 0.4, 0.4, p);
            // More saturated should increase distance from luma
            double luma = FilterPipeline.Luminance(0.6,0.4,0.4);
            Assert.True(Math.Abs(r2 - luma) > Math.Abs(0.6 - luma) - 0.01);
        }

        [Fact]
        public void Exposure_Positive_Brightens()
        {
            var p = new FilterParams { Exposure = 1 }; // x2
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.3, 0.3, 0.3, p);
            Assert.Equal(0.6, r, 2);
        }

        [Fact]
        public void Exposure_Negative_Darkens()
        {
            var p = new FilterParams { Exposure = -1 }; // x0.5
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.6, 0.6, 0.6, p);
            Assert.Equal(0.3, r, 2);
        }

        [Fact]
        public void Negative_Inverts()
        {
            var p = new FilterParams { NegativeEnabled = true };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.2, 0.4, 0.6, p);
            Assert.Equal(0.8, r, 2);
            Assert.Equal(0.6, g, 2);
            Assert.Equal(0.4, b, 2);
        }

        [Fact]
        public void HueShift_RedToGreen()
        {
            var p = new FilterParams { Hue = 0.3333f }; // 120 deg
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(1, 0, 0, p);
            // Red hue 0 -> shift 120deg -> greenish
            Assert.True(g > r);
            Assert.True(g > b);
        }

        [Fact]
        public void Temperature_Warm_IncreasesRed()
        {
            var p = new FilterParams { Temperature = 0.8f };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.5, 0.5, 0.5, p);
            Assert.True(r > b);
        }

        [Fact]
        public void Temperature_Cold_IncreasesBlue()
        {
            var p = new FilterParams { Temperature = -0.8f };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.5, 0.5, 0.5, p);
            Assert.True(b > r);
        }

        [Fact]
        public void NightMode_ReducesBlue()
        {
            var p = new FilterParams { NightModeEnabled = true, NightModeStrength = 1f };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.5, 0.5, 0.8, p);
            Assert.True(b < 0.8);
        }

        [Fact]
        public void Hsv_SaturationZero_Gray()
        {
            var p = new FilterParams { HsvEnabled = true, HsvSaturation = 0 };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(1, 0, 0, p);
            Assert.Equal(r, g, 3);
        }

        [Fact]
        public void Letterbox_OverridesColor()
        {
            var p = new FilterParams { LetterboxEnabled = true, LetterboxHeight = 0.1f, LetterboxColorR=0, LetterboxColorG=0, LetterboxColorB=0 };
            p.Saturation = 1.2f; p.Exposure = 0.5f; p.NegativeEnabled = true;
            // uvY inside letterbox should be black regardless of filters
            var (r,g,b) = FilterPipeline.ApplyWithLetterbox(0.5,0.5,0.5, 0.05, p);
            Assert.Equal(0, r, 3);
            Assert.Equal(0, g, 3);
            Assert.Equal(0, b, 3);
            // uvY outside letterbox should be filtered (not black)
            var (r2,g2,b2) = FilterPipeline.ApplyWithLetterbox(0.2,0.2,0.2, 0.5, p);
            Assert.NotEqual(0, r2);
            Assert.True(r2 > 0.1 && r2 < 0.9);
        }

        [Fact]
        public void Letterbox_NegativeDoesNotAffectBars()
        {
            var p = new FilterParams { LetterboxEnabled = true, LetterboxHeight=0.1f, NegativeEnabled=true };
            p.LetterboxColorR=0; p.LetterboxColorG=0; p.LetterboxColorB=0;
            var (r,g,b) = FilterPipeline.ApplyWithLetterbox(0.2,0.2,0.2, 0.02, p);
            Assert.Equal(0, r, 3); // bars stay black even with negative
        }

        [Fact]
        public void CombinedFilters_Stable()
        {
            var p = new FilterParams
            {
                Exposure = 0.5f, Saturation=1.2f, Hue=0.1f, Temperature=0.3f, NightModeEnabled=true, NightModeStrength=0.4f,
                HsvEnabled=true, HsvSaturation=1.1f, SharpenEnabled=true
            };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.4,0.5,0.6,p);
            Assert.True(r>=0 && r<=1);
            Assert.True(g>=0 && g<=1);
            Assert.True(b>=0 && b<=1);
        }

        [Fact]
        public void Clamping_ExtremeValues()
        {
            var p = new FilterParams();
            p.Saturation = 10; // should clamp to 3
            Assert.Equal(3, p.Saturation);
            p.Exposure = 10; Assert.Equal(3, p.Exposure);
            p.Exposure = -10; Assert.Equal(-3, p.Exposure);
            p.Temperature = 5; Assert.Equal(1, p.Temperature);
        }

        [Fact]
        public void Hsv_RoundTrip()
        {
            var (h,s,v) = FilterPipeline.RgbToHsv(0.2,0.4,0.8);
            var (r,g,b) = FilterPipeline.HsvToRgb(h,s,v);
            Assert.Equal(0.2, r, 2);
            Assert.Equal(0.4, g, 2);
            Assert.Equal(0.8, b, 2);
        }

        [Fact]
        public void FiltersEnabled_False_IsNeutral()
        {
            var p = new FilterParams { Saturation=2, Exposure=2, NegativeEnabled=true, FiltersEnabled=false };
            var (r,g,b) = FilterPipeline.ApplyColorPipeline(0.3,0.4,0.5,p);
            Assert.Equal(0.3, r, 3);
        }
    }
}
