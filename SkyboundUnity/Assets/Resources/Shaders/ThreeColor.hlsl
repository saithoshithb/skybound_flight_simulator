// The end of every three.js material. Opaque surfaces and the sky get this all at once in
// ToneMap.shader; transparent ones, drawn after that pass, call it themselves.
//
// The browser game has two orders, and they look different:
//   no effects: tone mapping, sRGB encoding, then fog mixed in the encoded colour.
//   effects on (EffectComposer, High tier only): fog mixed in linear, then bloom, then vignette,
//   then tone mapping over the whole image. So the far hills are greyer there.
#ifndef SKYBOUND_THREE_COLOR
#define SKYBOUND_THREE_COLOR

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

float4 _SkyboundFog; // x = near, y = far, set by WorldBuilder
float _SkyboundComposer; // 1 with effects on, set by FlightEffects

static const float THREE_EXPOSURE = 0.95;
// Rows here are the columns of three.js's mat3s.
static const float3x3 THREE_ACES_INPUT = float3x3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777);
static const float3x3 THREE_ACES_OUTPUT = float3x3(
    1.60475, -0.53108, -0.07367,
    -0.10208, 1.10813, -0.00605,
    -0.00327, -0.07276, 1.07602);
// The haze, #c9d5df, as sRGB.
static const float3 THREE_HAZE = float3(0.78824, 0.83529, 0.87451);

float3 ThreeRRTAndODTFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 ThreeAces(float3 color)
{
    color *= THREE_EXPOSURE / 0.6;
    color = mul(THREE_ACES_INPUT, color);
    color = ThreeRRTAndODTFit(color);
    color = mul(THREE_ACES_OUTPUT, color);
    return saturate(color);
}

// Takes linear scene colour. The result is linear too: URP encodes it at the very end.
float3 ThreeFinish(float3 color, float eyeDepth)
{
    float amount = _SkyboundFog.y > _SkyboundFog.x ? smoothstep(_SkyboundFog.x, _SkyboundFog.y, eyeDepth) : 0;
    if (_SkyboundComposer > 0.5) return lerp(color, SRGBToLinear(THREE_HAZE), amount);
    return SRGBToLinear(lerp(LinearToSRGB(ThreeAces(color)), THREE_HAZE, amount));
}

#endif
