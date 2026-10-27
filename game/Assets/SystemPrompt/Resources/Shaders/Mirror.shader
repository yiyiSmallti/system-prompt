// The white door's face when it turns into a mirror (Dungeon/SeaMirror.cs): the reflection camera's picture,
// looked up where each point of the glass falls in that camera's view, with a soft white rim. _Show fades
// from the plain door to the reflection, _Flash whitens it while the reflection changes.
Shader "SystemPrompt/Mirror"
{
    Properties
    {
        _MainTex ("Reflection", 2D) = "black" {}
        _Door ("Door", Color) = (0.96,0.97,1,1)
        _Tint ("Glass tint", Color) = (0.88,0.96,1,1)
        _Show ("Reflection shown", Range(0,1)) = 0
        _Flash ("Flash", Range(0,1)) = 0
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
            float _Show, _Flash;

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
                float2 uv = i.seen.xy / i.seen.w * 0.5 + 0.5;
                float3 seen = tex2D(_MainTex, uv).rgb * _Tint.rgb;
                float2 e = min(i.uv, 1 - i.uv);
                float rim = 1 - smoothstep(0.0, 0.05, min(e.x * 1.4, e.y));
                float3 col = lerp(_Door.rgb, seen, _Show * (1 - rim));
                col += _Show * rim * float3(0.5, 0.85, 1.0) * 0.6;
                col = lerp(col, float3(1, 1, 1), _Flash);
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
