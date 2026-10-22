// Cel shading for the latent-space props, the player's avatar and the lighthouse: two flat tones split
// by the light, a rim that glows like the sea light behind, and an ink outline from an inverted hull.
// The character models bring their base colour as _MainTex; everything else leaves it white.
Shader "SystemPrompt/Toon"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Lit", Color) = (1,1,1,1)
        _ShadeColor ("Shade", Color) = (0.5,0.55,0.7,1)
        _RimColor ("Rim", Color) = (0.4,0.9,1,1)
        _RimPower ("Rim power", Float) = 3
        _Emission ("Emission", Color) = (0,0,0,0)
        _OutlineColor ("Outline", Color) = (0.03,0.03,0.06,1)
        _OutlineWidth ("Outline width", Float) = 0.025
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _OutlineColor;
            float _OutlineWidth;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                // Wider with distance a little, so far props keep a line without near ones getting fat.
                float d = distance(world, _WorldSpaceCameraPos);
                world += n * _OutlineWidth * (0.6 + d * 0.04);
                o.pos = UnityWorldToClipPos(world);
                o.world = world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(SeaFog(_OutlineColor.rgb, i.world), 1);
            }
            ENDCG
        }

        Pass
        {
            Name "TOON"
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color, _ShadeColor, _RimColor, _Emission;
            float _RimPower;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float lit = smoothstep(-0.05, 0.05, dot(n, normalize(_SeaLightDir.xyz)));
                float3 col = lerp(_ShadeColor.rgb, _Color.rgb, lit) * tex2D(_MainTex, i.uv).rgb;
                float rim = pow(1.0 - saturate(dot(n, view)), _RimPower);
                col += _RimColor.rgb * smoothstep(0.35, 0.55, rim) * _RimColor.a;
                col += _Emission.rgb;
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
