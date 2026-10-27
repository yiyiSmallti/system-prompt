// What lies about at the foot of the racks (Dungeon/SeaArt.Scatter): broken hardware, data shards and
// cables. Vertex colour: rgb the colour, alpha how much of it glows; the rest is lit by the sea's light and
// the pool of light around the avatar.
Shader "SystemPrompt/Scatter"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float lit = 0.6 + 0.5 * saturate(dot(n, normalize(_SeaLightDir.xyz))) + 0.3 * saturate(n.y);
                float pool = saturate(1.0 - distance(i.world.xz, _SeaGlowPos.xz) / max(_SeaGlowPos.w, 0.01));
                float3 col = i.color.rgb * lit + _SeaGlowColor.rgb * pool * pool * 0.35;
                col = lerp(col, i.color.rgb * 1.8, i.color.a);
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
