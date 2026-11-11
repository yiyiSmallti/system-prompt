// Ice a noise body is frozen into, and its splinters (Dungeon/BattleEffects.Freeze, Shards): see-through and
// tinted so the body shows inside, each flat facet lit at its own angle by the light from the surface, bright
// toward the edges and with a glint where a facet turns the light at the eye, frost cracks on its faces
// (_FrostTex, alpha, laid on from all three sides of the block) and lit by the battle's flares. Alpha-blended,
// not additive, so it reads as a solid; the far wall is drawn first, darker, so the block has a thickness.
Shader "SystemPrompt/Ice"
{
    Properties
    {
        _Color ("Color", Color) = (0.55,0.9,1,1)
        _FrostTex ("Frost (alpha)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        CGINCLUDE
        #include "SeaCommon.cginc"

        float4 _Color;
        sampler2D _FrostTex;

        struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
        struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 obj : TEXCOORD2; float3 objNormal : TEXCOORD3; };

        v2f vert(appdata v)
        {
            v2f o;
            o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
            o.obj = v.vertex.xyz;
            o.objNormal = v.normal;
            o.pos = UnityWorldToClipPos(o.world);
            return o;
        }
        ENDCG

        // The far wall, seen through the near one.
        Pass
        {
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(SeaFog(_Color.rgb * 0.5, i.world), 0.35 * _Color.a * SeaClear(i.world));
            }
            ENDCG
        }

        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float3 light = normalize(_SeaLightDir.xyz + float3(0.0, 0.6, 0.0));
                float edge = pow(1.0 - saturate(dot(n, view)), 2.0);
                float lit = saturate(dot(n, light));
                float glint = pow(saturate(dot(reflect(-view, n), light)), 20.0);
                float3 w = abs(i.objNormal);
                w /= w.x + w.y + w.z + 1e-4;
                float frost = tex2D(_FrostTex, i.obj.yz * 1.6).a * w.x + tex2D(_FrostTex, i.obj.xz * 1.6).a * w.y + tex2D(_FrostTex, i.obj.xy * 1.6).a * w.z;
                float3 col = _Color.rgb * (0.45 + 0.55 * lit) + (edge * 0.7 + glint * 1.4) * float3(0.85, 0.97, 1.0)
                    + frost * float3(0.85, 0.95, 1.0) * 0.55 * (0.4 + edge) + SeaFlash(i.world, n) * 0.5;
                float a = saturate(0.5 + 0.2 * lit + 0.5 * edge + glint + frost * 0.35) * _Color.a;
                return fixed4(SeaFog(col, i.world), a * SeaClear(i.world));
            }
            ENDCG
        }
    }
}
