// Ice a noise body is frozen into (Dungeon/BattleEffects.Freeze): see-through and tinted so the body shows
// inside, each flat facet lit at its own angle by the light from the surface, bright toward the edges and with
// a glint where a facet turns the light at the eye. Alpha-blended, not additive, so it reads as a solid.
Shader "SystemPrompt/Ice"
{
    Properties
    {
        _Color ("Color", Color) = (0.55,0.9,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _Color;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float3 light = normalize(_SeaLightDir.xyz + float3(0.0, 0.6, 0.0));
                float edge = pow(1.0 - saturate(dot(n, view)), 2.0);
                float lit = saturate(dot(n, light));
                float glint = pow(saturate(dot(reflect(-view, n), light)), 20.0);
                float3 col = _Color.rgb * (0.45 + 0.55 * lit) + (edge * 0.7 + glint * 1.4) * float3(0.85, 0.97, 1.0);
                float a = saturate(0.3 + 0.2 * lit + 0.55 * edge + glint) * _Color.a;
                return fixed4(SeaFog(col, i.world), a * SeaClear(i.world));
            }
            ENDCG
        }
    }
}
