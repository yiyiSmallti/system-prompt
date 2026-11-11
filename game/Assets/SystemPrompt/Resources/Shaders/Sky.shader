// Inside of the sphere around the camera: the deep below, the surface far above, light ripples on it.
Shader "SystemPrompt/Sky"
{
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" }
        Cull Front
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float up = i.dir.y;
                float3 col = lerp(_SeaFogColor.rgb * 0.6, _SeaFogColor.rgb, smoothstep(-0.4, 0.05, up));
                float surface = smoothstep(0.1, 0.95, up) * 0.75;
                col = lerp(col, _SeaSurfaceColor.rgb, surface);
                // The zone's open water (SeaCommon.cginc SeaWater, as the fog takes it looking the same way): lighter
                // above the horizon, so what stands far off melts into it; never under it, which stays the deep's black.
                float water = SeaWater(up);
                if (water > surface) col = lerp(_SeaFogColor.rgb, _SeaSurfaceColor.rgb, water);
                // The surface seen from below, the same net of light as on the seabed, brightest straight up, faint, and
                // brighter and dimmer in broad patches that the net drifts through, so it reads as a swell rather than a
                // tiled ceiling.
                float2 p = i.dir.xz / max(up, 0.08) * 18.0;
                float swell = sin(p.x * 0.16) * sin(p.y * 0.13 + 1.1) + sin((p.x - p.y) * 0.07 + 0.4);
                col += _SeaSurfaceColor.rgb * SeaCausticAt(p) * smoothstep(0.35, 0.9, up) * 0.32 * saturate(0.55 + 0.45 * swell);
                // Where the zone's water lets the surface's light through (SeaAtmos Lift), the net carries on lower, fading
                // with how far off over the camera it lies, so a camera looking up from low down (a battle's opening) still
                // sees it overhead; and straight up the surface opens to a bright window.
                float low = smoothstep(0.08, 0.35, up) * (1.0 - smoothstep(0.35, 0.6, up)) * exp(-length(p) * 0.004);
                col += _SeaSurfaceColor.rgb * (SeaCausticAt(p) * low * 0.2 * saturate(0.55 + 0.45 * swell) + pow(saturate(up), 24.0) * 0.5) * _SeaAir.x;
                return fixed4(SeaInk(col), 1);
            }
            ENDCG
        }
    }
}
