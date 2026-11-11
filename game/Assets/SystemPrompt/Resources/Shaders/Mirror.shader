// The white door's face when it turns into a mirror (Dungeon/SeaMirror.cs): the reflection camera's picture,
// looked up where each point of the glass falls in that camera's view, with a soft blue rim. _Show spreads it
// over the door from the middle as a ripple (the door shows where it hasn't reached); _Scan runs a band of light
// down it while the reflection settles.
Shader "SystemPrompt/Mirror"
{
    Properties
    {
        _MainTex ("Reflection", 2D) = "black" {}
        _Door ("Door at the rim", Color) = (0.9,0.925,0.973,1)
        _Tint ("Glass tint", Color) = (0.88,0.96,1,1)
        _Aspect ("Width / height", Float) = 1
        _Show ("Ripple's way out", Range(0,1)) = 0
        _Edge ("Ripple's ring", Range(0,1)) = 1
        _Scan ("Band's height", Float) = 2
        _ScanPeak ("Band's brightness", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float4x4 _MirrorVP; // the reflection camera's projection × view
            float4 _Door, _Tint;
            float _Aspect, _Show, _Edge, _Scan, _ScanPeak;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float4 seen : TEXCOORD1; float2 uv : TEXCOORD2; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.seen = mul(_MirrorVP, float4(o.world, 1));
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Round from the middle in metres' proportion (a circle, not the glass's ellipse): 0 there, about 1.27
                // at a corner, which the front passes, less its soft 0.15, as _Show reaches 1.
                float2 c = (i.uv - 0.5) * float2(_Aspect, 1);
                float r = length(c) * 2.0;
                float front = _Show * 1.45;
                float m = smoothstep(front, front - 0.15, r);
                clip(m - 0.001);
                // Rings running out behind the front bend the reflection, and still as it settles.
                float2 uv = i.seen.xy / i.seen.w * 0.5 + 0.5;
                uv += c / max(length(c), 1e-4) * sin(r * 20.0 - _Show * 29.0) * 0.01 * (1 - _Show);
                float3 seen = tex2D(_MainTex, uv).rgb * _Tint.rgb;
                float2 e = min(i.uv, 1 - i.uv);
                float rim = 1 - smoothstep(0.0, 0.05, min(e.x * 1.4, e.y));
                // From the door's white at the front into the reflection, the rim glowing where the glass has come.
                float3 col = lerp(_Door.rgb, seen, m * (1 - rim)) + m * rim * float3(0.5, 0.85, 1.0) * 0.6;
                // The ring at the front, over its edge, fading as the ripple spreads; the band settling it. Squared by
                // hand: pow of a negative is NaN.
                float ring = (r - front + 0.04) / 0.04, band = (i.uv.y - _Scan) / 0.03;
                col += exp(-ring * ring) * float3(0.5, 0.85, 1) * 0.8 * (1 - _Show) * _Edge;
                col += exp(-band * band) * float3(0.6, 0.9, 1) * _ScanPeak;
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
