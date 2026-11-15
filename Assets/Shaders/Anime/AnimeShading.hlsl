#ifndef ANIME_SHADING_INCLUDED
#define ANIME_SHADING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

inline float screenChannel(float a, float b)
{
    return 1.0 - (1.0 - a) * (1.0 - b);
}

inline float3 screenColor(float3 a, float3 b)
{
    return float3(screenChannel(a.r, b.r), screenChannel(a.g, b.g), screenChannel(a.b, b.b));
}

inline float3 ApplyColorFromLight(float3 a, float3 shadeColor, float sun, float camRim, float worldRim, float3 lightColor)
{
    float lightRim = saturate(3.0 - camRim * 6.0) * worldRim;
    camRim = 1.0 - saturate(camRim + 0.3);
    camRim *= camRim;
    a *= saturate(1.0 - camRim); // darkening
    a = screenColor(a, lightColor * sun); // lights
    a += lightColor * lightRim; // lights
    return a;
}

inline float3 CalculateAdditionalLights(float3 worldPos, float3 worldNorm)
{
    float3 ambience = float3(0.0, 0.0, 0.0);
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
    {
        Light light = GetAdditionalLight(lightIndex, worldPos);
        float ndotl = max(0.0, dot(light.direction, worldNorm));
        float diff = (0.7 + ndotl * 0.3) * light.distanceAttenuation * light.shadowAttenuation;
        ambience += light.color * diff;
    }
#endif
    return ambience;
}

inline float3 ApplyGradualDirt(float3 col, Texture2D dirtTex, SamplerState dirtSampler, float amount, float2 uv)
{
    float4 dirt = dirtTex.Sample(dirtSampler, uv * 7.0);
    col = lerp(col, dirt.rgb, smoothstep(0.0, dirt.a, amount) * 0.9);
    return col;
}

#endif // ANIME_SHADING_INCLUDED
