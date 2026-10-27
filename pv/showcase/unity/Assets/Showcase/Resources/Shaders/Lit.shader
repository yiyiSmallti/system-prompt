// 写实一点的光照，替换三渲二（Figure.Toon 关掉时）：主光是场景里的平行光（Stage.Light，跟着镜头摆在机位右上方，
// 带软阴影），明暗按 Lambert 平滑过渡，皮肤的明暗交界包得宽一点、偏暖，像光透进皮肤；镜头左侧一盏冷色补光、
// 上亮下暗的天光、身后的轮廓光和主光的高光在着色器里按镜头算。贴图和三渲二用的是同一张（皮肤占比在 alpha）。
// 生成模型的脸是一块块平面，照实打光棱角全露出来，所以脸上（Figure 量的取景框里的皮肤）的法线换成脸后方一点
// （_FaceCentre，Figure.Commit）为心的球面。
Shader "Showcase/Lit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SkinMask ("Skin mask in albedo alpha", Float) = 0
        _SkinTint ("Skin tint", Color) = (1,0.88,0.83,1)
        _Fill ("Fill light", Color) = (0.2,0.22,0.27,1)
        _Sky ("Ambient from above", Color) = (0.27,0.28,0.31,1)
        _Ground ("Ambient from below", Color) = (0.13,0.12,0.12,1)
        _RimColor ("Rim", Color) = (0.6,0.85,1,0.4)
        _RimPower ("Rim power", Float) = 3
        _Spec ("Specular", Float) = 0.06
        _Gloss ("Specular power", Float) = 30
        _SkinSpec ("Skin specular", Float) = 0.035
        _SkinGloss ("Skin specular power", Float) = 12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color, _SkinTint, _Fill, _Sky, _Ground, _RimColor, _LightColor0;
            float4x4 _FaceFromWorld;
            float4 _FaceCentre;
            float _SkinMask, _RimPower, _Spec, _Gloss, _SkinSpec, _SkinGloss;
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                SHADOW_COORDS(3)
            };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityWorldToClipPos(o.world);
                TRANSFER_SHADOW(o);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float3 right = UNITY_MATRIX_V[0].xyz, back = UNITY_MATRIX_V[2].xyz;
                float4 albedo = tex2D(_MainTex, i.uv);
                float skin = saturate(albedo.a * _SkinMask);
                albedo.rgb *= _Color.rgb * lerp(1, _SkinTint.rgb, skin);
                float face = 0;
                if (_SkinMask > 0)
                {
                    float3 f = mul(_FaceFromWorld, float4(i.world, 1)).xyz;
                    float2 edge = min(f.xy, 1 - f.xy);
                    face = skin * smoothstep(0, 0.12, edge.x) * smoothstep(0, 0.12, edge.y) * step(0, f.z) * step(f.z, 1);
                    n = normalize(lerp(n, normalize(i.world - _FaceCentre.xyz), 0.85 * face));
                }

                float3 toKey = normalize(_WorldSpaceLightPos0.xyz);
                // 生成模型的刘海投在脸上是一块块硬边：皮肤上的投影压淡，脸上不要。
                float shadow = lerp(1, SHADOW_ATTENUATION(i), lerp(1, 0.35, skin) * (1 - face));
                float ndl = dot(n, toKey);
                float wrap = lerp(0.15, 0.45, skin);
                float key = saturate((ndl + wrap) / (1 + wrap)) * shadow;
                // 皮肤在明暗交界处偏红一点。
                float3 keyTint = lerp(1, lerp(float3(1, 0.72, 0.64), 1, key), skin);
                float3 light = _LightColor0.rgb * key * keyTint;

                float3 fillDir = normalize(-right * 0.8 + back * 0.6 + float3(0, 0.15, 0));
                float fill = saturate(dot(n, fillDir) * 0.5 + 0.5);
                light += _Fill.rgb * fill * fill;
                light += lerp(_Ground.rgb, _Sky.rgb, n.y * 0.5 + 0.5);
                float3 col = albedo.rgb * light;

                float3 h = normalize(toKey + view);
                float spec = pow(saturate(dot(n, h)), lerp(_Gloss, _SkinGloss, skin)) * lerp(_Spec, _SkinSpec, skin);
                col += _LightColor0.rgb * spec * shadow;

                // 轮廓光从身后上方打来，只亮背光一侧的边。
                float3 backDir = normalize(-back + float3(0, 0.6, 0) - right * 0.3);
                float rim = pow(1 - saturate(dot(n, view)), _RimPower) * saturate(dot(n, backDir) * 0.6 + 0.4);
                col += _RimColor.rgb * _RimColor.a * rim;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    // 投影用 VertexLit 的 ShadowCaster。
    Fallback "Legacy Shaders/VertexLit"
}
