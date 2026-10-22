// The maze walls: stacks of sunken server racks. Sides are cut into rack columns and 1U slots, with
// status LEDs blinking and activity bars sliding; the top edge glows in the sea light. Everything is
// drawn from the mesh's metre UVs (Dungeon/SeaMeshes.Walls), no textures.
//   uv  = side: (metres along the wall, metres up) / top: world (x, z)
//   uv2 = (seed per cell, wall height, 1 on the top face)
//   colour = the zone's light for the LEDs and the top edge, alpha how much of it
Shader "SystemPrompt/Rack"
{
    Properties
    {
        _Base ("Metal", Color) = (0.05,0.08,0.14,1)
        _Panel ("Blank panel", Color) = (0.08,0.12,0.2,1)
        _Led ("LED", Color) = (0.25,0.95,0.85,1)
        _Amber ("LED (warning)", Color) = (1,0.62,0.2,1)
        _Edge ("Top edge", Color) = (0.3,0.75,1,1)
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

            float4 _Base, _Panel, _Led, _Amber, _Edge;

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

            float3 Side(v2f i)
            {
                const float colW = 1.3333;
                const float unitH = 0.44;
                float seed = i.info.x;
                float height = i.info.y;
                float cu = frac(i.uv.x / colW);
                float ru = frac(i.uv.y / unitH);
                float colId = floor(i.uv.x / colW);
                float rowId = floor(i.uv.y / unitH);
                float h = SeaHash(float2(colId + seed * 17.0, rowId + seed * 3.0));

                float seamX = smoothstep(0.0, 0.035, cu) * smoothstep(1.0, 0.965, cu);
                float seamY = smoothstep(0.0, 0.08, ru) * smoothstep(1.0, 0.92, ru);
                float3 col = lerp(_Base.rgb, _Panel.rgb, step(0.78, h)) * (0.55 + 0.45 * seamX * seamY);

                // A status LED near the left of each server slot, some of them blinking.
                float2 ledP = float2((cu - 0.1) * colW, (ru - 0.5) * unitH);
                float led = smoothstep(0.045, 0.02, length(ledP)) * step(h, 0.78);
                float blink = step(0.35, frac(h * 7.31 + _SeaTime * (0.2 + h * 1.6)));
                col += led * blink * lerp(Led(i), _Amber.rgb, step(0.7, frac(h * 13.7))) * 1.6;

                // Activity bars sliding across the busier slots.
                float busy = step(0.45, h) * step(h, 0.78);
                float bar = smoothstep(0.035, 0.0, abs(ru - 0.5)) * step(0.22, cu) * step(cu, 0.9);
                float slide = step(0.55, frac(cu * 5.0 - _SeaTime * (0.4 + h)));
                col += busy * bar * (0.25 + 0.75 * slide) * Led(i) * 0.45;

                // Darker toward the seabed, the sea light catching the top edge.
                col *= lerp(0.45, 1.0, saturate(i.uv.y / 3.5));
                col += _SeaSurfaceColor.rgb * 0.1 * saturate(i.uv.y / max(height, 0.01));
                col += Edge(i) * smoothstep(0.35, 0.0, height - i.uv.y) * 0.9;
                col *= 0.85 + 0.25 * saturate(dot(normalize(i.normal), normalize(_SeaLightDir.xyz)));
                return col;
            }

            float3 Top(v2f i)
            {
                float2 cell = frac(i.uv / 4.0);
                float edge = min(min(cell.x, 1.0 - cell.x), min(cell.y, 1.0 - cell.y)) * 4.0;
                float3 col = _Base.rgb * 0.8;
                col += Edge(i) * smoothstep(0.18, 0.0, edge) * 0.8;
                col += _SeaSurfaceColor.rgb * SeaCaustics(i.uv * 0.5, _SeaTime) * 0.25;
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 col = i.info.z > 0.5 ? Top(i) : Side(i);
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
