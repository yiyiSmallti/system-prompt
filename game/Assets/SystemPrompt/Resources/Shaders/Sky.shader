// Inside of the sphere around the camera: the deep below, the surface far above, light ripples on it.
Shader "SystemPrompt/Sky"
{
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" }
        Cull Front
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float up = i.dir.y;
                float3 col = lerp(_SeaFogColor.rgb * 0.6, _SeaFogColor.rgb, smoothstep(-0.4, 0.05, up));
                col = lerp(col, _SeaSurfaceColor.rgb, smoothstep(0.1, 0.95, up) * 0.75);
                // The surface: ripples seen from below, brightest straight up.
                float2 p = i.dir.xz / max(up, 0.08) * 6.0;
                col += _SeaSurfaceColor.rgb * SeaCaustics(p, _SeaTime * 0.6) * smoothstep(0.35, 0.9, up) * 0.6;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
