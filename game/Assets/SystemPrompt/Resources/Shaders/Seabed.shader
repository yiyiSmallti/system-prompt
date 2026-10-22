// The seabed under the maze: dark silt, the latent-space grid drawn on it (one line per maze cell,
// fainter metre lines between), light ripples from the surface and a pool of light around the avatar.
// Each zone of the area tints it its own colour (_ZoneTex: one texel per cell, blended at the borders).
Shader "SystemPrompt/Seabed"
{
    Properties
    {
        _Base ("Silt", Color) = (0.03,0.06,0.11,1)
        _Grid ("Grid", Color) = (0.2,0.55,0.9,1)
        _Cell ("Cell size", Float) = 4
        _ZoneTex ("Zone light (rgb, a = strength)", 2D) = "black" {}
        _ZoneRect ("Zone texture in world xz (x, z, width, depth)", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _Base, _Grid, _ZoneRect;
            float _Cell;
            sampler2D _ZoneTex;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            float Line(float2 p, float spacing, float width)
            {
                float2 g = abs(frac(p / spacing + 0.5) - 0.5) * spacing;
                float2 aa = fwidth(p) * 1.2;
                float2 l = 1.0 - smoothstep(width - aa, width + aa, g);
                return max(l.x, l.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.world.xz;
                float3 col = _Base.rgb * (0.8 + 0.4 * SeaHash(floor(p * 2.0)));
                float4 zone = tex2D(_ZoneTex, (p - _ZoneRect.xy) / _ZoneRect.zw);
                float3 grid = lerp(_Grid.rgb, zone.rgb, zone.a * 0.85);
                col += zone.rgb * zone.a * 0.13;
                // Cell lines sit on the cell borders, so they run along the foot of the walls.
                col += grid * Line(p, _Cell, 0.05) * (0.35 + zone.a * 0.2);
                col += grid * Line(p, _Cell / 4.0, 0.02) * 0.08;
                col += _SeaSurfaceColor.rgb * SeaCaustics(p * 0.35, _SeaTime) * 0.18;
                float glow = saturate(1.0 - distance(p, _SeaGlowPos.xz) / max(_SeaGlowPos.w, 0.01));
                col += _SeaGlowColor.rgb * glow * glow * 0.55;
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
