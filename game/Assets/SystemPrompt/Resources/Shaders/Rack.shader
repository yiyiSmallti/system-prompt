// The maze walls: stacks of sunken server racks. Sides are cut into rack columns and 1U slots holding
// servers (status LEDs blinking, activity bars sliding, a handle at each end), drive bays, patch panels,
// vents and blanks; the frame (posts, cap, plinth) is its own geometry, the cap's lip lit in the zone's
// light; tops have fans. Everything is drawn from the mesh's metre UVs (Dungeon/SeaArt.Walls), no textures.
// Each part also has a relief in metres (the seams and recesses, the trays and handles standing out): its
// slopes bend the normal, which takes the sea's light from above, the lamp the avatar carries (a point light
// at _SeaGlowPos) and a sheen on the metal, and the deep places are darker.
//   uv  = side: (metres along the face from its start, metres up) / frame front: (metres along, metres up
//         from the part's bottom) / top: world (x, z)
//   uv2 = (seed per cell, wall height, 1 on the top face, frame part: 1 post, 2 cap, 3 plinth, +0.5 off the front)
//   colour = the zone's light for the LEDs and the top edge, alpha how much of it
Shader "SystemPrompt/Rack"
{
    Properties
    {
        _Base ("Metal", Color) = (0.06,0.09,0.16,1)
        _Panel ("Blank panel", Color) = (0.09,0.13,0.22,1)
        _Led ("LED", Color) = (0.25,0.95,0.85,1)
        _Amber ("LED (warning)", Color) = (1,0.62,0.2,1)
        _Edge ("Top edge", Color) = (0.3,0.75,1,1)
        _Trim ("Frame", Color) = (0.1,0.14,0.22,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "SeaCommon.cginc"

            float4 _Base, _Panel, _Led, _Amber, _Edge, _Trim;

            #define COL_W 1.3333
            #define UNIT_H 0.44
            #define SEAM 0.035 // metres the seams between slots go in

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; float4 info : TEXCOORD3; float4 tint : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = v.uv;
                o.info = v.uv2;
                o.tint = v.color;
                return o;
            }

            float3 Led(v2f i) { return lerp(_Led.rgb, i.tint.rgb, i.tint.a); }
            float3 Edge(v2f i) { return lerp(_Edge.rgb, i.tint.rgb, i.tint.a); }

            // ------------------------------------------------------------------ light

            // The sea's light from above with some wrapped round, the avatar's lamp, a sheen on the metal
            // from both, and ao for how deep the place is.
            float3 Shade(float3 albedo, float3 n, float3 world, float ao, float gloss)
            {
                float3 l = normalize(_SeaLightDir.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - world);
                float key = saturate(dot(n, l) * 0.6 + 0.4);
                float3 col = albedo * (0.3 + 1.25 * key * key);

                float3 toLamp = _SeaGlowPos.xyz + float3(0, 1.4, 0) - world;
                float d = length(toLamp);
                float3 lp = toLamp / max(d, 0.001);
                float reach = saturate(1.0 - d / max(_SeaGlowPos.w * 2.0, 0.01));
                reach *= reach;
                col += albedo * _SeaGlowColor.rgb * reach * saturate(dot(n, lp)) * 2.0;

                float sheen = pow(saturate(dot(n, normalize(l + v))), 40.0) * 0.22
                            + pow(saturate(dot(n, normalize(lp + v))), 28.0) * reach * 0.7;
                col += lerp(_SeaSurfaceColor.rgb, _SeaGlowColor.rgb, 0.5) * sheen * gloss;
                return col * ao;
            }

            // How much a pixel is wider than the relief's detail: 0 near, 1 where the detail would only shimmer.
            float Far(float2 uv)
            {
                float2 fw = fwidth(uv);
                return saturate(max(fw.x, fw.y) * 9.0 - 0.1);
            }

            // The normal bent by a relief's slopes, along t (uv.x) and b (uv.y).
            float3 Bend(float3 n, float3 t, float3 b, float r0, float rx, float ry, float e, float far)
            {
                float2 g = float2(rx - r0, ry - r0) / e * (1.0 - far);
                return normalize(n - t * g.x - b * g.y);
            }

            // ------------------------------------------------------------------ sides

            struct Slot { float cu, ru, colId, rowId, h, kind, inside; float2 m; };

            Slot SlotAt(float2 uv, float seed)
            {
                Slot s;
                s.cu = frac(uv.x / COL_W);
                s.ru = frac(uv.y / UNIT_H);
                s.colId = floor(uv.x / COL_W);
                s.rowId = floor(uv.y / UNIT_H);
                s.h = SeaHash(float2(s.colId + seed * 17.0, s.rowId + seed * 3.0));
                s.kind = SeaHash(float2(s.rowId * 1.7 + seed * 5.0, s.colId * 2.3 + 11.0)); // what fills the slot
                s.m = float2(s.cu * COL_W, s.ru * UNIT_H); // metres into the slot
                s.inside = step(0.07, s.cu) * step(s.cu, 0.95) * step(0.1, s.ru) * step(s.ru, 0.9);
                return s;
            }

            float Seams(Slot s)
            {
                float x = smoothstep(0.0, 0.035, s.cu) * smoothstep(1.0, 0.965, s.cu);
                float y = smoothstep(0.0, 0.08, s.ru) * smoothstep(1.0, 0.92, s.ru);
                return x * y;
            }

            float Tray(Slot s, out float bx, out float bayId)
            {
                const float bayW = COL_W / 9.0;
                bx = frac((s.m.x - 0.08) / bayW);
                bayId = floor((s.m.x - 0.08) / bayW);
                return smoothstep(0.0, 0.1, bx) * smoothstep(1.0, 0.9, bx) * s.inside;
            }

            float Port(Slot s, out float id)
            {
                const float portW = COL_W / 18.0;
                float px = frac(s.m.x / portW);
                float py = frac(s.ru * 2.0);
                id = SeaHash(float2(floor(s.m.x / portW) + s.colId * 17.0, floor(s.ru * 2.0) + s.rowId * 3.0));
                return smoothstep(0.3, 0.24, abs(px - 0.5)) * smoothstep(0.22, 0.16, abs(py - 0.5)) * s.inside;
            }

            float Slits(Slot s) { return smoothstep(0.2, 0.14, abs(frac(s.ru * 7.0) - 0.5)) * s.inside; }

            // A handle at each end of a server's front.
            float Handles(Slot s)
            {
                float x = min(abs(s.cu - 0.105), abs(s.cu - 0.915)) * COL_W;
                return smoothstep(0.016, 0.008, x) * smoothstep(0.32, 0.26, abs(s.ru - 0.5));
            }

            float ActivityBar(Slot s) { return smoothstep(0.035, 0.0, abs(s.ru - 0.5)) * step(0.22, s.cu) * step(s.cu, 0.86); }

            // Metres the face stands out at uv: 0 the front of a panel, the seams SEAM deep.
            float Relief(float2 uv, float seed)
            {
                Slot s = SlotAt(uv, seed);
                float r = -SEAM * (1.0 - Seams(s));
                if (s.h >= 0.78) return r + 0.006 * Seams(s); // a blank stands a little proud
                float a, b;
                if (s.kind < 0.28) return r - 0.012 * s.inside + 0.016 * Tray(s, a, b);
                if (s.kind < 0.46) return r - 0.02 * Port(s, a);
                if (s.kind < 0.58) return r - 0.024 * Slits(s);
                return r + 0.018 * Handles(s) - 0.006 * ActivityBar(s);
            }

            float3 SideColour(v2f i, Slot s, out float3 glow)
            {
                float height = i.info.y;
                float3 col = lerp(_Base.rgb, _Panel.rgb, step(0.78, s.h));
                glow = 0;
                if (s.h < 0.78)
                {
                    if (s.kind < 0.28)
                    {
                        // Drive bays: a row of trays, each with its own tiny light.
                        float bx, bayId;
                        float tray = Tray(s, bx, bayId);
                        col = lerp(col * 0.7, col * 1.35, tray);
                        float hb = SeaHash(float2(bayId + s.colId * 9.0, s.rowId + i.info.x * 7.0));
                        float led = smoothstep(0.012, 0.006, length(float2((bx - 0.5) * COL_W / 9.0, s.m.y - UNIT_H * 0.76))) * s.inside;
                        float on = step(0.45, frac(hb * 5.3 + _SeaTime * (0.6 + hb * 3.0)));
                        glow += led * on * step(0.15, hb) * lerp(Led(i), _Amber.rgb, step(0.9, hb)) * 1.8;
                    }
                    else if (s.kind < 0.46)
                    {
                        // A patch panel: two rows of ports, a few of them lit.
                        float hp;
                        float port = Port(s, hp);
                        col *= 1.0 - port * 0.6;
                        glow += port * step(0.82, hp) * Led(i) * 0.9;
                    }
                    else if (s.kind < 0.58)
                    {
                        // Vents, the warmth of the machine behind showing through.
                        glow += _Amber.rgb * 0.1 * (0.6 + 0.4 * sin(_SeaTime * 1.3 + s.h * 20.0)) * Slits(s);
                    }
                    else
                    {
                        // A server: a status LED near its left, some blinking, and activity bars on the busier ones.
                        float2 ledP = float2((s.cu - 0.16) * COL_W, (s.ru - 0.5) * UNIT_H);
                        float led = smoothstep(0.045, 0.02, length(ledP));
                        float blink = step(0.35, frac(s.h * 7.31 + _SeaTime * (0.2 + s.h * 1.6)));
                        glow += led * blink * lerp(Led(i), _Amber.rgb, step(0.7, frac(s.h * 13.7))) * 1.6;
                        float busy = step(0.45, s.h);
                        float slide = step(0.55, frac(s.cu * 5.0 - _SeaTime * (0.4 + s.h)));
                        glow += busy * ActivityBar(s) * (0.25 + 0.75 * slide) * Led(i) * 0.45;
                        col *= 1.0 + Handles(s) * 0.5;
                    }
                }
                // Darker toward the seabed, the sea light catching the top edge.
                col *= lerp(0.5, 1.0, saturate(i.uv.y / 3.5));
                col += _SeaSurfaceColor.rgb * 0.06 * saturate(i.uv.y / max(height, 0.01));
                glow += Edge(i) * smoothstep(0.35, 0.0, height - i.uv.y) * 0.9;
                return col;
            }

            float3 Side(v2f i)
            {
                float seed = i.info.x;
                Slot s = SlotAt(i.uv, seed);
                float3 glow;
                float3 albedo = SideColour(i, s, glow);
                float far = Far(i.uv);
                const float e = 0.006;
                float r0 = Relief(i.uv, seed);
                float rx = Relief(i.uv + float2(e, 0), seed);
                float ry = Relief(i.uv + float2(0, e), seed);
                float3 n0 = normalize(i.normal);
                float3 n = Bend(n0, float3(n0.z, 0, -n0.x), float3(0, 1, 0), r0, rx, ry, e, far);
                float ao = lerp(1.0, lerp(0.5, 1.0, saturate(r0 / SEAM + 1.0)), 1.0 - far * 0.6);
                return Shade(albedo, n, i.world, ao, 1.0) + glow;
            }

            // ------------------------------------------------------------------ frame

            // The posts' groove and screw holes down the front, the plinth's vent slits.
            float FrameRelief(float2 uv, float part)
            {
                if (part < 1.5)
                {
                    float2 q = float2(uv.x - 0.05, (frac(uv.y / 0.22) - 0.5) * 0.22);
                    float hole = smoothstep(0.016, 0.008, length(q));
                    float groove = smoothstep(0.009, 0.003, abs(uv.x - 0.05));
                    return -0.012 * max(groove, hole);
                }
                if (part > 2.5)
                    return -0.014 * smoothstep(0.34, 0.26, abs(frac(uv.x / 0.16) - 0.5)) * smoothstep(0.035, 0.022, abs(uv.y - 0.09));
                return 0;
            }

            // The frame, lit like the faces: posts with a groove and screw holes down the front, the cap with
            // a strip of the zone's light under its lip, the plinth with vent slits.
            float3 Frame(v2f i)
            {
                float part = floor(i.info.w);
                float front = step(frac(i.info.w), 0.25);
                float3 n0 = normalize(i.normal);
                float3 n = n0;
                float ao = 1.0;
                if (front > 0.5 && part != 2)
                {
                    const float e = 0.004;
                    float r0 = FrameRelief(i.uv, part);
                    n = Bend(n0, float3(n0.z, 0, -n0.x), float3(0, 1, 0), r0, FrameRelief(i.uv + float2(e, 0), part), FrameRelief(i.uv + float2(0, e), part), e, Far(i.uv));
                    ao = 1.0 + r0 / 0.012 * 0.55;
                }
                float3 albedo = _Trim.rgb * (part > 2.5 ? 0.75 : 1.0) * lerp(0.55, 1.0, saturate(i.world.y / 3.5));
                float3 col = Shade(albedo, n, i.world, ao, 1.4);
                if (part > 1.5 && part < 2.5)
                {
                    col += Edge(i) * front * (smoothstep(0.025, 0.008, abs(i.uv.y - 0.035)) * 1.4 + 0.12);
                    col += _SeaSurfaceColor.rgb * SeaCaustic(i.world) * 0.3 * saturate(n0.y);
                }
                return col;
            }

            // ------------------------------------------------------------------ top

            float FanRelief(float2 uv, float seed)
            {
                float2 slot = floor(uv / COL_W);
                float2 q = (frac(uv / COL_W) - 0.5) * COL_W;
                float r = length(q);
                float spin = _SeaTime * (1.5 + SeaHash(slot + seed) * 3.0);
                float blades = step(0.5, frac(atan2(q.y, q.x) / 6.2832 * 5.0 + spin)) * smoothstep(0.08, 0.11, r) * smoothstep(0.43, 0.4, r);
                float housing = smoothstep(0.46, 0.43, r);
                float rim = smoothstep(0.035, 0.0, abs(r - 0.465));
                float hub = smoothstep(0.1, 0.07, r);
                return -0.05 * housing + 0.02 * blades * housing + 0.012 * rim + 0.035 * hub;
            }

            // Fans in a grid of rack columns, turning, and the cell's glowing edge.
            float3 Top(v2f i)
            {
                float2 cell = frac(i.uv / 4.0);
                float edge = min(min(cell.x, 1.0 - cell.x), min(cell.y, 1.0 - cell.y)) * 4.0;
                const float e = 0.008;
                float r0 = FanRelief(i.uv, i.info.x);
                float3 n = Bend(float3(0, 1, 0), float3(1, 0, 0), float3(0, 0, 1), r0, FanRelief(i.uv + float2(e, 0), i.info.x), FanRelief(i.uv + float2(0, e), i.info.x), e, Far(i.uv));
                float ao = lerp(0.45, 1.0, saturate(r0 / 0.05 + 1.0));
                float3 col = Shade(_Base.rgb * 0.85, n, i.world, ao, 1.0);
                col += Edge(i) * smoothstep(0.18, 0.0, edge) * 0.8;
                col += _SeaSurfaceColor.rgb * SeaCaustic(i.world) * 0.2;
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 col = i.info.w > 0.5 ? Frame(i) : i.info.z > 0.5 ? Top(i) : Side(i);
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
