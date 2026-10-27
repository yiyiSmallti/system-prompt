// Light in the shape of a model (Dungeon/SeaArt.Holo): bright toward its edges, faint in the middle where its
// texture shows through, with scan lines running up it. Additive, so it shines in the dark of the sea.
Shader "SystemPrompt/Holo"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Color ("Color", Color) = (0.4,0.85,1,1)
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

            sampler2D _MainTex;
            float4 _Color;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float edge = pow(1.0 - saturate(abs(dot(normalize(i.normal), view))), 2.5);
                float detail = Luminance(tex2D(_MainTex, i.uv).rgb);
                float lines = 0.7 + 0.3 * sin(i.world.y * 26.0 - _SeaTime * 9.0);
                float3 col = _Color.rgb * (edge * 1.8 + 0.1 + detail * 0.45) * lines;
                return fixed4(col * _Color.a * SeaClear(i.world), 1);
            }
            ENDCG
        }
    }
}
