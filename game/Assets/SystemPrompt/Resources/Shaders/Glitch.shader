// The noise bodies: a skin of flickering blocks in two loud colours over black, vertices jumping a
// little now and then, an ink outline. _Alert pushes everything toward red.
Shader "SystemPrompt/Glitch"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.9,0.15,0.2,1)
        _ColorB ("Color B", Color) = (1,0.85,0.2,1)
        _Dark ("Dark", Color) = (0.04,0.04,0.07,1)
        _Block ("Block size", Float) = 0.22
        _Alert ("Alert", Range(0,1)) = 0
        _Jitter ("Jitter", Float) = 0.04
        _OutlineColor ("Outline", Color) = (0.02,0.02,0.04,1)
        _OutlineWidth ("Outline width", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGINCLUDE
        #include "SeaCommon.cginc"
        float4 _ColorA, _ColorB, _Dark, _OutlineColor;
        float _Block, _Alert, _Jitter, _OutlineWidth;

        float3 Jump(float3 objPos)
        {
            float step8 = floor(_SeaTime * 9.0);
            float band = floor(objPos.y * 6.0);
            float h = SeaHash(float2(band, step8));
            return float3((h - 0.5) * 2.0 * _Jitter * step(0.82, h), 0, 0);
        }
        ENDCG

        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 objPos = v.vertex.xyz + Jump(v.vertex.xyz);
                float3 world = mul(unity_ObjectToWorld, float4(objPos, 1)).xyz;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                world += n * _OutlineWidth * (0.6 + distance(world, _WorldSpaceCameraPos) * 0.04);
                o.pos = UnityWorldToClipPos(world);
                o.world = world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(SeaFog(_OutlineColor.rgb, i.world), 1); }
            ENDCG
        }

        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float3 obj : TEXCOORD0; float3 world : TEXCOORD1; float3 normal : TEXCOORD2; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.obj = v.vertex.xyz + Jump(v.vertex.xyz);
                o.world = mul(unity_ObjectToWorld, float4(o.obj, 1)).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 cell = floor(i.obj / _Block);
                float tick = floor(_SeaTime * 6.0 + SeaHash(cell.xy + cell.z) * 6.0);
                float h = SeaHash(cell.xy + cell.z * 7.1 + tick * 0.37);
                float3 col = h < 0.42 ? _Dark.rgb : (h < 0.72 ? _ColorA.rgb : _ColorB.rgb);
                col = lerp(col, float3(1, 0.12, 0.15), _Alert * step(0.42, h) * 0.7);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float rim = pow(1.0 - saturate(dot(normalize(i.normal), view)), 3.0);
                col += lerp(_ColorB.rgb, float3(1, 0.2, 0.2), _Alert) * smoothstep(0.4, 0.6, rim) * 0.6;
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
