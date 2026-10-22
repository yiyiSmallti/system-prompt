// Shared by the latent-space shaders (Dungeon/DungeonWorld.cs sets the globals). The sea has its own
// fog instead of Unity's, so it survives the build's fog-variant stripping and can tint by depth.
#ifndef SEA_COMMON_INCLUDED
#define SEA_COMMON_INCLUDED

#include "UnityCG.cginc"

float4 _SeaFogColor;    // what everything fades into
float4 _SeaSurfaceColor; // the light coming down from the surface
float4 _SeaLightDir;    // xyz: toward the light
float _SeaFogDensity;   // per metre
float _SeaTime;         // unscaled seconds
float4 _SeaGlowPos;     // xyz: the player's avatar, which lights the seabed around it; w: radius
float4 _SeaGlowColor;

// 1 near the camera, 0 deep in the fog.
inline float SeaClear(float3 worldPos)
{
    float d = distance(worldPos, _WorldSpaceCameraPos);
    return exp(-pow(max(d - 4.0, 0.0) * _SeaFogDensity, 1.5));
}

inline float3 SeaFog(float3 col, float3 worldPos)
{
    // Higher up the fog takes the surface light's colour.
    float3 fog = lerp(_SeaFogColor.rgb, _SeaSurfaceColor.rgb, saturate(worldPos.y / 60.0) * 0.5);
    return lerp(fog, col, SeaClear(worldPos));
}

inline float SeaHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// Moving light ripples on whatever faces up.
inline float SeaCaustics(float2 p, float t)
{
    float a = sin(p.x * 0.9 + t * 0.7 + sin(p.y * 0.5 + t * 0.3) * 1.7);
    float b = sin(p.y * 1.1 - t * 0.6 + sin(p.x * 0.4 - t * 0.4) * 1.9);
    float c = sin((p.x + p.y) * 0.6 + t * 0.5);
    float v = (a + b + c) / 3.0;
    return pow(saturate(1.0 - abs(v) * 2.2), 3.0);
}

#endif
