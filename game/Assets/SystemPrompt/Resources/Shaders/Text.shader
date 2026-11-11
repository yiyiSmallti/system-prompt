// Words in the latent space (Dungeon/SeaArt.Text): the dynamic font's glyph alpha times the vertex colour,
// added like Glow so the text shines and fades into the fog, and out near the lens. _Gain (by property block,
// never below 1) lifts words past the vertex colour's white, to glow (BattleEffects.Thoughts).
Shader "SystemPrompt/Text"
{
    Properties
    {
        _MainTex ("Font", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float _Gain; // not a property: 0 where unset, read as 1

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a * i.color.a * SeaClear(i.world) * SeaNearGlow(i.world);
                return fixed4(i.color.rgb * a * max(_Gain, 1.0), 1);
            }
            ENDCG
        }
    }
}
