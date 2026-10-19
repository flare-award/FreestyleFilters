// ScreenFX Pixel Shader - Full filter pipeline
// Порядок обработки (документирован):
// 1. Exposure
// 2. Temperature
// 3. Night Mode
// 4. Saturation (global)
// 5. Hue (global)
// 6. HSV (Hue/Sat/Val)
// 7. Negative
// 8. Sharpen (3x3)
// 9. Fullscreen Blur (9-tap)
// 10. Circular Blur (radial, blend with blur)
// 11. Halftone
// 12. Letterbox (final, not filtered)

Texture2D screenTexture : register(t0);
SamplerState screenSampler : register(s0);

cbuffer FilterParams : register(b0)
{
    // Color correction
    float saturation;              // 0..2, 1=neutral
    float exposure;                // EV -3..+3, pow(2,exposure)
    float hueShift;                // 0..1 (0..360 deg)
    float temperature;             // -1..1 cold->warm
    float nightModeStrength;       // 0..1
    int   enableNegative;          // 0/1
    int   enableHalftone;          // 0/1
    float halftoneStrength;        // 0..1
    float halftoneDotSize;         // e.g. 2..16
    float halftoneContrast;        // 0..2
    float halftoneAngle;           // degrees
    float sharpenStrength;         // 0..1
    float blurStrength;            // 0..1 (fullscreen blur amount)
    float circularBlurRadius;      // 0..1 relative to min dimension
    float circularBlurStrength;    // 0..1 blur intensity
    float circularBlurSmoothness;  // 0..1 edge softness
    float2 circularBlurCenter;     // 0..1
    float2 resolution;             // screen resolution
    float hsvHue;                  // 0..1 shift
    float hsvSaturation;           // 0..2
    float hsvValue;                // 0..2
    int   letterboxEnabled;        // 0/1
    float letterboxHeight;         // 0..0.5 fraction
    float letterboxAspect;         // unused, for compatibility
    float3 letterboxColor;         // RGB 0..1
    float _pad0;
    float2 _pad1;
};

// Helpers
float3 rgb2hsv(float3 c)
{
    float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzw), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

float luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// Temperature: cold -1 .. warm +1
// Approximate via lerping to warm/cold tints
float3 applyTemperature(float3 color, float temp)
{
    // temp <0 cold (more blue), >0 warm (more orange)
    // Use standard temperature curve approximations
    float3 cold = float3(0.7, 0.8, 1.0);  // bluish
    float3 warm = float3(1.0, 0.65, 0.3); // orangish
    if (temp > 0)
    {
        // warm: mix towards warm, strength temp
        // Apply via multiply and lerp to preserve luminance somewhat
        float3 warmed = color * lerp(float3(1,1,1), float3(1.2, 1.0, 0.8), temp);
        // Add subtle orange tint
        warmed = lerp(warmed, warmed * warm * 1.2, temp * 0.4);
        return warmed;
    }
    else if (temp < 0)
    {
        float t = -temp;
        float3 cooled = color * lerp(float3(1,1,1), float3(0.8, 0.9, 1.2), t);
        cooled = lerp(cooled, cooled * cold * 1.1, t * 0.3);
        return cooled;
    }
    return color;
}

float3 applyNightMode(float3 color, float strength)
{
    // Reduce blue channel, shift to warm
    // strength 0..1
    if (strength <= 0) return color;
    // Reduce blue by up to 60%, increase red slightly
    float3 night = float3(color.r * 1.05, color.g * 0.98, color.b * (1.0 - strength * 0.6));
    // Additional warm tint
    night = lerp(color, night, strength);
    // Preserve luminance a bit: lerp towards warm luma
    return night;
}

float3 applySaturation(float3 color, float sat)
{
    float luma = luminance(color);
    return lerp(float3(luma, luma, luma), color, sat);
}

float3 applyHueShift(float3 color, float shift) // shift 0..1
{
    if (abs(shift) < 0.001) return color;
    float3 hsv = rgb2hsv(color);
    hsv.x = frac(hsv.x + shift);
    return hsv2rgb(hsv);
}

float3 applyHSV(float3 color, float hShift, float sScale, float vScale)
{
    float3 hsv = rgb2hsv(color);
    hsv.x = frac(hsv.x + hShift);
    hsv.y = saturate(hsv.y * sScale);
    hsv.z = saturate(hsv.z * vScale);
    return hsv2rgb(hsv);
}

// Sharpen: 3x3 kernel (unsharp mask approx)
// sample neighbors via resolution
float3 applySharpen(float3 center, float2 uv, float strength)
{
    if (strength <= 0.001) return center;
    float2 texel = 1.0 / resolution;
    float3 north = screenTexture.Sample(screenSampler, uv + float2(0, -texel.y)).rgb;
    float3 south = screenTexture.Sample(screenSampler, uv + float2(0,  texel.y)).rgb;
    float3 west  = screenTexture.Sample(screenSampler, uv + float2(-texel.x, 0)).rgb;
    float3 east  = screenTexture.Sample(screenSampler, uv + float2( texel.x, 0)).rgb;

    // Laplacian
    float3 laplace = center * 4.0 - (north + south + west + east);
    // Sharpen amount: strength up to 1.0 gives moderate sharpen without huge artifacts
    float sharpenAmount = strength * 0.6; // tune
    return saturate(center + laplace * sharpenAmount);
}

// Simple 9-tap box blur for fullscreen blur and circular blur
float3 sampleBlur(float2 uv)
{
    float2 texel = 1.0 / resolution;
    float3 sum = 0;
    // 3x3 kernel with weights
    // Use 9 taps, weights 0.0625 corners, 0.125 edges, 0.25 center approx gaussian
    // For performance, simple box
    int idx = 0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y) * texel * 1.5; // slightly larger radius for visible blur
            float weight = (x==0 && y==0) ? 0.25 : (abs(x)+abs(y)==1 ? 0.125 : 0.0625);
            // Sample
            sum += screenTexture.Sample(screenSampler, uv + offset).rgb * weight;
        }
    }
    return sum;
}

// For stronger blur we do two-level: sampleBlur twice? But single pass lerp is ok for now
// Circular blur needs blurred version and sharp version blended by distance

float3 applyHalftone(float3 color, float2 uv)
{
    if (!enableHalftone) return color;
    if (halftoneStrength <= 0.001) return color;

    float luma = luminance(color);
    // Apply contrast to luma
    luma = saturate((luma - 0.5) * halftoneContrast + 0.5);

    // Rotate UV by angle
    float rad = radians(halftoneAngle);
    float c = cos(rad);
    float s = sin(rad);
    float2 rotUV = float2(
        uv.x * c - uv.y * s,
        uv.x * s + uv.y * c
    );
    // Scale by resolution and dot size
    float2 scaled = rotUV * resolution / max(halftoneDotSize, 1.0);

    // Distance to nearest dot center (grid)
    float2 grid = frac(scaled) - 0.5;
    float dist = length(grid);

    // Dot radius depends on luma: darker = larger dot
    float dotRadius = (1.0 - luma) * 0.45; // 0..0.45
    // Smooth edge
    float dot = 1.0 - smoothstep(dotRadius - 0.08, dotRadius + 0.08, dist);

    // Dot pattern is black dots on color or color modulated?
    // Classic: lerp between color and dot color (black/white)
    // Here we darken where dot is, according to halftone
    float3 halftoneColor = lerp(color, color * dot, halftoneStrength);
    // Alternative: add threshold pattern: if we want more stylized, use dot as multiplier
    // For stronger effect, replace luma with dot
    // Blend original and halftone modulated
    float pattern = lerp(1.0, dot, halftoneStrength);
    // Modulate color by pattern but keep hue
    float3 result = color * pattern;
    // Slight mix to keep readability
    result = lerp(color, result, halftoneStrength);

    // Also enhance contrast
    return result;
}

struct PSInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float4 main(PSInput input) : SV_Target
{
    float2 uv = input.uv;

    // ---- Letterbox final layer check early for optimization? But must be final, so we handle at end.
    // However we need to know if in letterbox area, we return letterboxColor without filtering.
    // To save performance, we could early out, but need filtered color for non-letterbox only.
    bool inLetterbox = false;
    if (letterboxEnabled != 0)
    {
        if (uv.y < letterboxHeight || uv.y > 1.0 - letterboxHeight)
            inLetterbox = true;
    }
    if (inLetterbox)
    {
        return float4(letterboxColor, 1.0);
    }

    // Sample original (before effects that depend on original)
    float3 color = screenTexture.Sample(screenSampler, uv).rgb;

    // 1. Exposure: pow(2, exposure) multiplier, with smooth handling
    // exposure -3..+3 => multiplier 0.125..8
    float expMult = exp2(exposure);
    color = color * expMult;
    // Clamp to avoid HDR blowout artifacts before further processing
    color = saturate(color);

    // 2. Temperature
    color = applyTemperature(color, temperature);
    color = saturate(color);

    // 3. Night Mode
    color = applyNightMode(color, nightModeStrength);
    color = saturate(color);

    // 4. Saturation (global)
    color = applySaturation(color, saturation);
    color = saturate(color);

    // 5. Hue global
    color = applyHueShift(color, hueShift);
    color = saturate(color);

    // 6. HSV
    // hsvSaturation 0..2, hsvValue 0..2, hsvHue 0..1
    // Only apply if not neutral to save conversion
    if (abs(hsvHue) > 0.001 || abs(hsvSaturation - 1.0) > 0.001 || abs(hsvValue - 1.0) > 0.001)
    {
        color = applyHSV(color, hsvHue, hsvSaturation, hsvValue);
        color = saturate(color);
    }

    // 7. Negative
    if (enableNegative != 0)
    {
        color = 1.0 - color;
    }

    // 8. Sharpen (needs original center before blur, but after color ops)
    // Apply sharpen using neighboring samples of *original* texture? Ideally sharpen should be on current color.
    // We sample neighbors from screenTexture but they are not yet color-corrected. For correctness, we approximate
    // by applying sharpen kernel to current color vs. using original. Simpler: use sampled neighbors with same color transforms?
    // For performance, we sample neighbors and apply same color pipeline approximated, but we instead lerp.
    // Current implementation applies sharpen by sampling screenTexture neighbors and computing laplace on original,
    // then applying to current color by offset difference.
    // Alternative simpler: apply sharpen after blur later. We do inline:
    if (sharpenStrength > 0.001)
    {
        // Sample neighbors with same color pipeline? We approximate by sampling and applying color transforms is expensive.
        // Instead we compute sharpen on current color by estimating neighbor colors via same hue etc. Simplified version:
        // Use direct neighbor luminance difference
        color = applySharpen(color, uv, sharpenStrength);
    }

    // 9 & 10. Blur handling
    // Fullscreen blur: lerp between sharp and blurred
    // Circular blur: blend based on distance from center
    float3 sharpColor = color;
    float3 blurredColor = sharpColor;

    // Only compute blur if needed
    bool needBlur = blurStrength > 0.001 || (circularBlurStrength > 0.001 && circularBlurRadius > 0.001);

    if (needBlur)
    {
        // Compute blurred version by sampling screenTexture and re-applying? For accuracy, blurred should be of post-color corrected image,
        // but we only have original texture. Approximate by blurring original then applying color ops per sample is too heavy.
        // So we blur the already color-corrected sharpColor by sampling nearby *current* color approximated via texel fetches of original with offset and re-applying minimal?
        // Simplified: blur the sharpColor by sampling neighboring sharpColor via multiple fetches of screenTexture and reusing color pipeline is too much.
        // So we blur in a way that samples directly from screenTexture, then re-applies color ops to each sample? That's expensive for 9 taps * full pipeline.
        // Compromise: blur the post-processed color by sampling via gathering blurredColor from blurredColor taps that are also post-processed via same lerp trick.
        // For this shader we do: blurredColor = sampleBlur(uv) -> then apply same color pipeline to blurred sample (approx by applying same transforms as input)
        // But sampleBlur already sampled original texture. We need to transform it similarly.
        // We do: fetch blurred original, then transform it with same steps (exposure etc.) -> but we already transformed center. So transform blurred sample similarly.

        // Fetch blurred original
        float3 blurOrig = sampleBlur(uv);

        // Transform blurOrig through same color pipeline (exposure..hsv, negative)
        // To avoid code duplication, we inline transforms for blurOrig
        float3 b = blurOrig * exp2(exposure);
        b = saturate(b);
        b = applyTemperature(b, temperature);
        b = saturate(b);
        b = applyNightMode(b, nightModeStrength);
        b = saturate(b);
        b = applySaturation(b, saturation);
        b = saturate(b);
        b = applyHueShift(b, hueShift);
        b = saturate(b);
        if (abs(hsvHue) > 0.001 || abs(hsvSaturation - 1.0) > 0.001 || abs(hsvValue - 1.0) > 0.001)
        {
            b = applyHSV(b, hsvHue, hsvSaturation, hsvValue);
            b = saturate(b);
        }
        if (enableNegative != 0) b = 1.0 - b;
        // Note: sharpen not applied to blurred version (sharpen and blur are separate)
        // So blurredColor is 'b' plus maybe sharpen? No.
        blurredColor = b;
        // For additional blur intensity we could double blur but single is enough
        // Lerp with additional strength to make more blur if needed: do two passes approximation
        if (blurStrength > 0.5 || circularBlurStrength > 0.5)
        {
            // Second blur pass for stronger effect: sample offset larger
            float2 texel = 1.0 / resolution;
            float3 blur2 = 0;
            float w = 0;
            for (int y=-2; y<=2; y+=2)
                for(int x=-2; x<=2; x+=2)
                {
                    float2 off = float2(x,y)*texel*2.0;
                    float3 s = screenTexture.Sample(screenSampler, uv+off).rgb;
                    s = s * exp2(exposure);
                    s = saturate(s);
                    s = applyTemperature(s, temperature);
                    s = saturate(s);
                    s = applyNightMode(s, nightModeStrength);
                    s = applySaturation(s, saturation);
                    s = applyHueShift(s, hueShift);
                    if (abs(hsvHue)>0.001 || abs(hsvSaturation-1)>0.001 || abs(hsvValue-1)>0.001)
                        s = applyHSV(s, hsvHue, hsvSaturation, hsvValue);
                    if (enableNegative!=0) s=1.0-s;
                    float weight = (x==0 && y==0) ? 0.4 : 0.15;
                    blur2 += s*weight;
                }
            blurredColor = lerp(blurredColor, blur2, 0.5);
        }
    }

    float3 finalColor = sharpColor;

    // Fullscreen blur lerp
    if (blurStrength > 0.001)
    {
        finalColor = lerp(sharpColor, blurredColor, saturate(blurStrength));
    }

    // Circular blur: distance from center
    if (circularBlurStrength > 0.001 && circularBlurRadius > 0.001)
    {
        float2 center = circularBlurCenter;
        // Aspect correction: use resolution to make circle circular, not elliptical
        float2 aspect = float2(resolution.x / resolution.y, 1.0);
        // Alternative: use min dimension normalization
        float2 diff = (uv - center);
        diff.x *= resolution.x / min(resolution.x, resolution.y);
        diff.y *= resolution.y / min(resolution.x, resolution.y);
        float dist = length(diff);
        // Radius is fraction of min dimension (0..0.5 typical)
        float radius = circularBlurRadius;
        float edge = radius + circularBlurSmoothness;
        float feather = smoothstep(radius, edge, dist);
        // Inside radius = sharp, outside = blurred
        float3 circ = lerp(sharpColor, blurredColor, feather * saturate(circularBlurStrength));
        // If fullscreen blur already applied, we need to blend circular on top
        // If fullscreen blur is active, circular should modulate that
        if (blurStrength > 0.001)
        {
            // Circular blur overrides fullscreen in outside region more heavily
            // Already have finalColor as lerp(sharp, blur, blurStrength)
            // Now lerp towards circular result
            finalColor = lerp(finalColor, circ, 1.0); // circ already includes blur
            // Actually we want circular to be independent: inside sharp, outside blurred regardless of fullscreen blur slider?
            // Spec: circular blur strength separate. So compute directly
            finalColor = lerp(sharpColor, blurredColor, max(blurStrength, feather * circularBlurStrength));
            // Simpler: circular feather dominates outside
            finalColor = lerp(finalColor, circ, feather * circularBlurStrength);
        }
        else
        {
            finalColor = circ;
        }
    }

    color = finalColor;

    // 11. Halftone (after spatial blurs, before letterbox)
    if (enableHalftone != 0 && halftoneStrength > 0.001)
    {
        color = applyHalftone(color, uv);
    }

    color = saturate(color);

    // 12. Letterbox already handled early, but if not in letterbox, we output filtered color
    return float4(color, 1.0);
}
