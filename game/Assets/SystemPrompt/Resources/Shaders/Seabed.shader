// The seabed under the maze: dark silt laid in metre tiles like a machine room's raised floor (grooved
// edges, each tile a little lighter or darker, now and then a perforated one with cold light rising
// through), the latent-space grid drawn on it (one line per maze cell, fainter metre lines between), light
// ripples from the surface and a pool of light around the avatar, whose lamp catches the tiles' bevelled
// edges. Each zone of the area tints it its own colour (_ZoneTex: one texel per cell, blended at the
// borders), and the floor darkens at the foot of the walls (_WallTex: the same cells, red where one stands).
// In a zone's water (Dungeon/SeaAtmos.cs Floor) the walls' light spills at their feet, the far floor takes a wet sheen
// and mirrors the battle's flares and the lamp (Wet), and the far grid is quieter; the figures' and noise bodies'
// shadows fall on it (SeaCommon.cginc SeaShadow); the rift's floor (_WallTex blue) is cracked through, lit from below
// (Cracks).
Shader "SystemPrompt/Seabed"
{
    Properties
    {
        _Base ("Silt", Color) = (0.03,0.06,0.11,1)
        _Grid ("Grid", Color) = (0.2,0.55,0.9,1)
        _Cell ("Cell size", Float) = 4
        _ZoneTex ("Zone light (rgb, a = strength)", 2D) = "black" {}
        _ZoneRect ("Zone texture in world xz (x, z, width, depth)", Vector) = (0,0,1,1)
        _WallTex ("Walls (r where one stands, b the rift's floor; cells as _ZoneTex)", 2D) = "black" {}
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

            // The seabed's look in the zone's water (Dungeon/SeaAtmos.cs Floor and Wet, set by DungeonWorld.ApplySea; all 0
            // is the seabed as it was): x the walls' light spilling at their feet, y a wet sheen toward the far floor, z
            // the far grid lines fainter than those round the player, w silt in broad patches; and (_SeaFloorSpec) x the
            // battle's flares and the avatar's lamp mirrored in the wet floor, y fewer vents, z the rift's crack network,
            // w how much it breathes.
            float4 _SeaFloor, _SeaFloorSpec;

            // Value noise, 0 to 1, a cell per unit.
            float Silt(float2 q)
            {
                float2 c = floor(q), f = frac(q);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(SeaHash(c), SeaHash(c + float2(1, 0)), f.x), lerp(SeaHash(c + float2(0, 1)), SeaHash(c + 1.0), f.x), f.y);
            }

            // The silt, the sheen and the mirrored lights on what the seabed already is (`onLine`: on a cell line, where
            // the mirrored light is mostly lost).
            float3 Wet(float3 col, float3 world, float onLine)
            {
                [branch] if (!any(_SeaFloor.yw != 0) && _SeaFloorSpec.x <= 0) return col;
                col *= lerp(1.0, lerp(0.92, 1.06, Silt(world.xz / 9.0)), _SeaFloor.w);
                float3 v = normalize(_WorldSpaceCameraPos - world);
                col = lerp(col, _SeaSurfaceColor.rgb * 0.6, pow(1.0 - saturate(v.y), 4.0) * _SeaFloor.y);
                [branch] if (_SeaFloorSpec.x > 0)
                {
                    // A light mirrored on water: sharp, and stronger toward grazing (Schlick), fading as SeaFlash does.
                    float3 r = reflect(-v, float3(0, 1, 0));
                    float fresnel = 0.04 + 0.96 * pow(1.0 - saturate(v.y), 5.0);
                    float3 toFlash = _SeaFlashPos.xyz - world;
                    float df = max(length(toFlash), 1e-3);
                    float rf = saturate(1.0 - df / max(_SeaFlashPos.w, 0.01));
                    float3 spec = _SeaFlashColor.rgb * pow(saturate(dot(r, toFlash / df)), 24.0) * rf * rf * 0.6;
                    float3 toLamp = _SeaGlowPos.xyz + float3(0, 1.4, 0) - world;
                    float dl = max(length(toLamp), 1e-3);
                    float rl = saturate(1.0 - dl / max(_SeaGlowPos.w * 2.0, 0.01));
                    spec += _SeaGlowColor.rgb * pow(saturate(dot(r, toLamp / dl)), 24.0) * rl * rl * 0.25;
                    col += spec * fresnel * (1.0 - onLine * 0.7) * _SeaFloorSpec.x;
                }
                return col;
            }

            // How much of the rift's floor is under a point, 0 to 1: the wall mask's blue, blended between the cells'
            // middles, so the cracks thin out over the last cell where the rift opens on to another zone.
            float RiftAt(float2 p)
            {
                float2 c = (p - _ZoneRect.xy) / _Cell - 0.5;
                float2 b = floor(c), f = frac(c), t = _WallTex_TexelSize.xy;
                float s00 = tex2Dlod(_WallTex, float4((b + 0.5) * t, 0, 0)).b, s10 = tex2Dlod(_WallTex, float4((b + float2(1.5, 0.5)) * t, 0, 0)).b;
                float s01 = tex2Dlod(_WallTex, float4((b + float2(0.5, 1.5)) * t, 0, 0)).b, s11 = tex2Dlod(_WallTex, float4((b + 1.5) * t, 0, 0)).b;
                return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
            }

            // The rift's floor cracked through (scene-deep-arena DA-13): a network of cells about 1.8 m across, bent by
            // the silt's noise and broken open in places, so the cracks run and branch rather than close into tiles, each
            // a thin red-orange core of light from below with a dark lip either side, the light breathing slowly (0.2 Hz,
            // by _SeaFloorSpec.w).
            float3 Cracks(float3 col, float2 p)
            {
                [branch] if (_SeaFloorSpec.z <= 0) return col;
                float rift = RiftAt(p) * _SeaFloorSpec.z;
                [branch] if (rift <= 0) return col;
                float2 q = p * 0.55 + (float2(Silt(p * 0.3), Silt(p * 0.3 + 7.1)) - 0.5) * 0.8;
                float2 c = floor(q);
                float f1 = 9.0, f2 = 9.0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    float2 k = c + float2(dx, dy);
                    float d = length(q - k - float2(SeaHash(k), SeaHash(k + 19.7)));
                    f2 = d < f1 ? f1 : min(f2, d);
                    f1 = min(f1, d);
                }
                float edge = (f2 - f1) * 0.5 / 0.55; // about the metres to the edge
                float open = smoothstep(0.32, 0.5, Silt(p * 0.45 + 11.3));
                float core = (1.0 - smoothstep(0.0, 0.03, edge)) * open;
                float lip = (1.0 - smoothstep(0.03, 0.15, edge)) * open;
                float breathe = 1.0 + _SeaFloorSpec.w * sin(_SeaTime * 1.2566);
                col *= 1.0 - lip * 0.6 * rift;
                return col + float3(1.0, 0.353, 0.235) * 1.6 * core * breathe * rift;
            }

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
                float vent = step(th, 0.07 - _SeaFloorSpec.y) * step(0.08, bevel) * saturate(1.0 - fw * 18.0);
                float hole = smoothstep(0.24, 0.16, length(frac(p * 8.0) - 0.5)) * vent;
                col = lerp(col, _Grid.rgb * (0.35 + 0.15 * sin(_SeaTime * 0.8 + th * 90.0)), hole * 0.7);
                // The walls' contact shade, soft out to a metre and a half from their foot (half a metre where their light
                // spills on the floor, _SeaFloor.x).
                float wall = WallDistance(p);
                float contact = lerp(0.35, 1.0, smoothstep(0.0, lerp(1.6, 0.5, _SeaFloor.x), wall));
                col *= contact;
                float4 zone = tex2D(_ZoneTex, (p - _ZoneRect.xy) / _ZoneRect.zw);
                float3 grid = lerp(_Grid.rgb, zone.rgb, zone.a * 0.85);
                col += zone.rgb * zone.a * 0.13;
                // The zone's light spilling on the floor from 0.3 to 1.5 m off the walls' feet.
                col += zone.rgb * 0.56 * smoothstep(1.5, 0.3, wall) * smoothstep(0.0, 0.3, wall) * _SeaFloor.x;
                // The far grid quieter: whole within 6 m of the player, 60% past 14.
                float lines = lerp(1.0, lerp(0.6, 1.0, 1.0 - smoothstep(6.0, 14.0, distance(p, _SeaGlowPos.xz))), _SeaFloor.z);
                // Cell lines sit on the cell borders, so they run along the foot of the walls.
                float cellLine = Line(p, _Cell, 0.05);
                col += grid * cellLine * (0.35 + zone.a * 0.2) * lerp(0.6, 1.0, contact) * lines;
                col += grid * Line(p, _Cell / 4.0, 0.02) * 0.08 * lines;
                col += _SeaSurfaceColor.rgb * SeaCaustic(i.world) * 0.4 * contact;
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
                col += SeaFlash(i.world, float3(0, 1, 0)) * 0.5;
                col += SeaPulse(i.world);
                // The lighthouse's beams passing over, less at the foot of the walls; scaled by what is left below
                // white, so the pool stands out plainly on the dark tiles without burning the lit grid lines out.
                col += SeaBeam(i.world) * 0.4 * lerp(0.4, 1.0, contact) * (1.0 - saturate(col));
                col = Wet(col, i.world, cellLine);
                col = Cracks(col, p);
                // The figures' and noise bodies' shadows (SeaCommon.cginc SeaShadow; 1 with no map).
                col *= SeaShadow(i.world);
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
