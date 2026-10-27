// The seabed under the maze: dark silt laid in metre tiles like a machine room's raised floor (grooved
// edges, each tile a little lighter or darker, now and then a perforated one with cold light rising
// through), the latent-space grid drawn on it (one line per maze cell, fainter metre lines between), light
// ripples from the surface and a pool of light around the avatar, whose lamp catches the tiles' bevelled
// edges. Each zone of the area tints it its own colour (_ZoneTex: one texel per cell, blended at the
// borders), and the floor darkens at the foot of the walls (_WallTex: the same cells, white where one stands).
Shader "SystemPrompt/Seabed"
{
    Properties
    {
        _Base ("Silt", Color) = (0.03,0.06,0.11,1)
        _Grid ("Grid", Color) = (0.2,0.55,0.9,1)
        _Cell ("Cell size", Float) = 4
        _ZoneTex ("Zone light (rgb, a = strength)", 2D) = "black" {}
        _ZoneRect ("Zone texture in world xz (x, z, width, depth)", Vector) = (0,0,1,1)
        _WallTex ("Walls (white where one stands, cells as _ZoneTex)", 2D) = "black" {}
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
            sampler2D _ZoneTex, _WallTex;
            float4 _WallTex_TexelSize;

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

            // Metres to the nearest wall among the cells round this one; 9 when none is that near.
            float WallDistance(float2 p)
            {
                float2 c = (p - _ZoneRect.xy) / _Cell;
                float2 base = floor(c);
                float best = 9.0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    float2 k = base + float2(dx, dy);
                    float solid = tex2Dlod(_WallTex, float4((k + 0.5) * _WallTex_TexelSize.xy, 0, 0)).r;
                    float2 q = c - k;
                    float d = length(max(max(-q, q - 1.0), 0.0)) * _Cell;
                    best = min(best, lerp(9.0, d, step(0.5, solid)));
                }
                return best;
            }

            // The tiles' grooved edges in metres: 0 on a tile, down to -0.02 in the groove.
            float TileRelief(float2 p)
            {
                float2 f = frac(p);
                return -0.02 * (1.0 - smoothstep(0.0, 0.035, min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y))));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.world.xz;
                // The tiles, their grooves and vents fading out where they would only shimmer.
                float2 f = frac(p);
                float th = SeaHash(floor(p) + 0.37);
                float fw = max(fwidth(p.x), fwidth(p.y));
                float near = saturate(1.0 - fw * 6.0);
                float bevel = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
                float3 col = _Base.rgb * (0.8 + 0.4 * th);
                col *= lerp(1.0, lerp(0.6, 1.0, smoothstep(0.0, 0.035, bevel)), near);
                float vent = step(th, 0.07) * step(0.08, bevel) * saturate(1.0 - fw * 18.0);
                float hole = smoothstep(0.24, 0.16, length(frac(p * 8.0) - 0.5)) * vent;
                col = lerp(col, _Grid.rgb * (0.35 + 0.15 * sin(_SeaTime * 0.8 + th * 90.0)), hole * 0.7);
                // The walls' contact shade, soft out to a metre and a half from their foot.
                float wall = WallDistance(p);
                float contact = lerp(0.35, 1.0, smoothstep(0.0, 1.6, wall));
                col *= contact;
                float4 zone = tex2D(_ZoneTex, (p - _ZoneRect.xy) / _ZoneRect.zw);
                float3 grid = lerp(_Grid.rgb, zone.rgb, zone.a * 0.85);
                col += zone.rgb * zone.a * 0.13;
                // Cell lines sit on the cell borders, so they run along the foot of the walls.
                col += grid * Line(p, _Cell, 0.05) * (0.35 + zone.a * 0.2) * lerp(0.6, 1.0, contact);
                col += grid * Line(p, _Cell / 4.0, 0.02) * 0.08;
                col += _SeaSurfaceColor.rgb * SeaCaustics(p * 0.35, _SeaTime) * 0.18 * contact;
                float glow = saturate(1.0 - distance(p, _SeaGlowPos.xz) / max(_SeaGlowPos.w, 0.01));
                col += _SeaGlowColor.rgb * glow * glow * 0.55 * lerp(0.5, 1.0, contact);
                // The lamp's glint along the bevels near the avatar.
                const float e = 0.01;
                float r0 = TileRelief(p);
                float2 slope = float2(TileRelief(p + float2(e, 0)) - r0, TileRelief(p + float2(0, e)) - r0) / e * near;
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));
                float3 toLamp = _SeaGlowPos.xyz + float3(0, 1.4, 0) - i.world;
                float3 h = normalize(normalize(toLamp) + normalize(_WorldSpaceCameraPos - i.world));
                float reach = saturate(1.0 - length(toLamp) / max(_SeaGlowPos.w * 2.0, 0.01));
                col += _SeaGlowColor.rgb * pow(saturate(dot(n, h)), 60.0) * reach * reach * 0.5 * (1.0 - saturate(dot(n, float3(0, 1, 0)) * 40.0 - 39.0));
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
