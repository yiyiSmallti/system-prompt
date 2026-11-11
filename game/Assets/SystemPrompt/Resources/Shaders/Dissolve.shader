// A firewall or gate breaking up (Dungeon/SeaProps.cs Firewall): the Glow sheet eaten away in square cells, mostly at
// random and a little from the top down, each cell's last moment a bright edge. _Cut 0 is the whole sheet, 1 none of it.
// Its own shader rather than a keyword on Glow: a material made at run time would lose a stripped shader_feature
// variant, and a multi_compile would double every Glow material's variants.
Shader "SystemPrompt/Dissolve"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Scroll ("UV scroll (x, y per second)", Vector) = (0,0,0,0)
        _Cut ("How much is gone", Range(0, 1)) = 0
        _EdgeColor ("Edge colour", Color) = (0.66,1.05,1.1,1)
        _EdgeWidth ("Edge width", Float) = 0.08
        _Cells ("Cells across, up", Vector) = (16,28,0,0)
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
            float4 _Color, _Scroll, _EdgeColor, _Cells;
            float _Cut, _EdgeWidth;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 sheet : TEXCOORD2; float4 color : COLOR; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + _Scroll.xy * _SeaTime;
                o.sheet = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float n = SeaHash(floor(i.sheet * _Cells.xy));
                float h = n * 0.7 + (1.0 - i.sheet.y) * 0.3;
                float keep = step(_Cut, h);
                // No edge before anything has gone, so the whole sheet doesn't light up at the start.
                float edge = keep * (1.0 - saturate((h - _Cut) / max(_EdgeWidth, 1e-3))) * saturate(_Cut * 20.0);
                fixed4 t = tex2D(_MainTex, i.uv);
                float fade = i.color.a * SeaClear(i.world) * SeaNearGlow(i.world);
                float3 col = t.rgb * t.a * _Color.rgb * _Color.a * keep + _EdgeColor.rgb * edge;
                return fixed4(col * i.color.rgb * fade, 0);
            }
            ENDCG
        }
    }
}
