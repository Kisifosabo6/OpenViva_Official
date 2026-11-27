#ifndef ANIME_COMMON_INCLUDED
#define ANIME_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_GlobalDirtTex);
SAMPLER(sampler_GlobalDirtTex);

float ScreenChannel(float a, float b)
{
    return 1.0 - (1.0 - a) * (1.0 - b);
}

float3 ScreenColor(float3 a, float3 b)
{
    return float3(ScreenChannel(a.r, b.r), ScreenChannel(a.g, b.g), ScreenChannel(a.b, b.b));
}

float3 ApplyColorFromLight(float3 a, float3 mainLightColor, float sun, float camRim, float worldRim)
{
    float lightRim = saturate(3.0 - camRim * 6.0) * worldRim;
    camRim = 1.0 - saturate(camRim + 0.3);
    camRim *= camRim;
    a *= saturate(1.0 - camRim); // darkening
    a = ScreenColor(a, mainLightColor * sun); // lights
    a += mainLightColor * lightRim; // lights
    return a;
}

float3 ApplyGradualDirt(float3 col, float amount, float2 uv)
{
    float4 dirt = SAMPLE_TEXTURE2D(_GlobalDirtTex, sampler_LinearRepeat, uv * 7.0);
    return lerp(col, dirt.rgb, smoothstep(0.0, dirt.a, amount) * 0.9);
}

float3 AnimeCalculateAmbience(float3 worldPos, float3 worldNorm, float3 ambient, float3 mainLightColor, float sun, float3 toonAmbience)
{
    float3 additionalLightsColor = 0;
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
    {
        Light light = GetAdditionalLight(lightIndex, worldPos);
        float3 toLight = light.direction;
        float ndotl = max(0.0, dot(toLight, worldNorm));
        float diff = (0.7 + ndotl * 0.3) * (light.distanceAttenuation * light.shadowAttenuation);
        additionalLightsColor += light.color * diff;
    }
#endif

    float3 ambience = additionalLightsColor;
    ambience += lerp(ambient, mainLightColor, sun) + toonAmbience;
    return ambience;
}

#endif
