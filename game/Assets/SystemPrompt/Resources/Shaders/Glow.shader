// Additive light for everything that shines in the sea: floating fragments, beams, motes, the vision
// fans of the noise bodies and the firewall. Fades into the fog instead of tinting toward it.
Shader "SystemPrompt/Glow"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Scroll ("UV scroll (x, y per second)", Vector) = (0,0,0,0)
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
            float4 _MainTex_ST;
            float4 _Color, _Scroll;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + _Scroll.xy * _SeaTime;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                float a = t.a * i.color.a * _Color.a * SeaClear(i.world);
                return fixed4(t.rgb * i.color.rgb * _Color.rgb * a, 1);
            }
            ENDCG
        }
    }
}
