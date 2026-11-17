// The whale of light the big moves call up (Dungeon/SeaArt.Phantom, Models/Props/WhalePhantom; pv/README.md S12): a
// pale see-through body, its outline white-hot all round, the pleats of its throat in lines of light, stars
// scattered over its back that twinkle, and a slow wave of light running from its head to its tail. Called up out
// of sparks (_Shown 0 to 1: it comes in from its head with a bright seam at the edge of what has come, the rest
// of it a scatter of glints) and let go the same way; _Fade dims all of it. Its shape is read in the model's own
// space, nose at +z and tail at −z (whale_phantom.py), in shares of its length, so it needs no texture. Additive.
Shader "SystemPrompt/Phantom"
{
    Properties
    {
        _Color ("Color", Color) = (0.45,0.85,1,1)
        _Shown ("How much of it has come (0 none, 1 all)", Range(0, 1)) = 1
        _Fade ("Its light, all of it", Range(0, 1)) = 1
        _Length ("Its length nose to tail in its mesh's units (SeaArt.Phantom sets it from the mesh)", Float) = 1
        _Mid ("Its middle along z in the mesh", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _Color;
            float _Shown, _Fade, _Length, _Mid;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 own : TEXCOORD2; float3 ownNormal : TEXCOORD3; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                o.own = v.vertex.xyz;
                o.ownNormal = v.normal;
                return o;
            }

            float Hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }

            float Noise(float3 p)
            {
                float3 c = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(c), Hash(c + float3(1, 0, 0)), f.x), lerp(Hash(c + float3(0, 1, 0)), Hash(c + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(c + float3(0, 0, 1)), Hash(c + float3(1, 0, 1)), f.x), lerp(Hash(c + float3(0, 1, 1)), Hash(c + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float3 n = normalize(i.normal);
                float3 own = (i.own - float3(0, 0, _Mid)) / _Length;
                float facing = saturate(abs(dot(n, view)));
                float edge = pow(1.0 - facing, 2.2);
                float along = saturate(0.5 - own.z); // 0 at the nose, 1 at the tail
                float t = _Time.y;

                // The body: faint in its middle, white-hot at its outline.
                float3 col = _Color.rgb * (0.09 + edge * 1.5) + float3(0.85, 0.95, 1.0) * pow(edge, 5.0) * 0.9;
                // The throat's pleats: lines along it under the head, paler than the body.
                float throat = smoothstep(-0.15, -0.55, i.ownNormal.y) * smoothstep(0.62, 0.25, along);
                float pleat = smoothstep(0.75, 1.0, abs(sin(own.x * 150.0)));
                col += float3(0.7, 0.9, 1.0) * throat * (0.12 + pleat * 0.35);
                // Stars over its back and flippers, each twinkling on its own.
                float3 g = own * 70.0;
                float3 cell = floor(g);
                float h = Hash(cell);
                float star = step(0.9, h) * smoothstep(0.4, 0.05, length(frac(g) - 0.5)) * (0.5 + 0.5 * sin(t * (1.5 + h * 3.0) + h * 30.0));
                star *= saturate(1.5 - fwidth(g.x) * 2.0) * (0.4 + 0.6 * smoothstep(-0.2, 0.4, i.ownNormal.y));
                col += float3(0.85, 0.97, 1.0) * star * 1.6;
                // A wave of light running head to tail.
                float wave = saturate(1.0 - abs(frac(t * 0.45) * 1.6 - 0.3 - along) * 4.0);
                col += _Color.rgb * wave * wave * 0.35;

                // Coming out of sparks from the head: what has not come yet is only glints, the seam at its edge burns.
                float grain = Noise(own * 40.0) * 0.25;
                float front = _Shown * 1.3 - 0.15;
                float come = smoothstep(front - 0.02, front - 0.08, along + grain);
                float seam = smoothstep(0.08, 0.0, abs(along + grain - front + 0.05)) * step(_Shown, 0.999);
                float glint = step(0.93, Hash(floor(own * 90.0) + floor(t * 12.0))) * smoothstep(front + 0.35, front, along) * (1.0 - come);
                col = col * come + _Color.rgb * (seam * 2.2 + glint * 1.5) + float3(1, 1, 1) * seam * 0.6;

                return fixed4(col * _Color.a * _Fade * SeaClear(i.world), 1);
            }
            ENDCG
        }
    }
}
