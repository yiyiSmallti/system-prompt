// 展示片的地面：深色底上一米一格的亮线和四分之一米的细线，离中心越远越融进天幕的地平线色。
// 每个人物脚下一圈发光的圆台（_Rings：xy 圆心，z 半径），身下一团接地阴影（_Shadows：xy 圆心，z 半径，w 浓度），
// 阴影跟着人物的腰走。_Scroll 把网格往后挪，人物原地走跑时看起来在前进。
Shader "Showcase/Floor"
{
    Properties
    {
        _Base ("Base", Color) = (0.03,0.05,0.12,1)
        _Line ("Grid", Color) = (0.3,0.6,1,1)
        _Edge ("Far colour (the horizon)", Color) = (0.2,0.45,1,1)
        _Glow ("Ring glow", Color) = (0.5,0.85,1,1)
        _Fade ("Fade radius", Float) = 22
        _Center ("Fade centre (xz)", Vector) = (0,0,0,0)
        _Scroll ("Grid scroll (xz)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _Base, _Line, _Edge, _Glow, _Center, _Scroll;
            float _Fade;
            float4 _Rings[8];
            float4 _Shadows[8];

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            // Anti-aliased lines every `step` metres, `width` pixels wide.
            float Grid(float2 p, float step, float width)
            {
                float2 q = p / step;
                float2 g = abs(frac(q - 0.5) - 0.5) / max(fwidth(q), 1e-5);
                return 1.0 - saturate(min(g.x, g.y) - width);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.world.xz + _Scroll.xy;
                float far = distance(i.world.xz, _Center.xy);
                // Lines thin out with distance so the far grid does not turn into a grey moiré.
                float near = 1.0 - smoothstep(_Fade * 0.25, _Fade * 0.8, far);
                float major = Grid(p, 1.0, 0.6) * (0.55 + 0.25 * near);
                float minor = Grid(p, 0.25, 0.3) * 0.16 * near;
                float3 col = _Base.rgb + _Line.rgb * max(major, minor);

                for (int k = 0; k < 8; k++)
                {
                    float4 s = _Shadows[k];
                    if (s.z > 0)
                    {
                        float d = distance(i.world.xz, s.xy) / s.z;
                        col *= 1.0 - s.w * exp(-d * d * 2.5);
                    }
                    float4 r = _Rings[k];
                    if (r.z > 0)
                    {
                        float d = distance(i.world.xz, r.xy);
                        float disc = 1.0 - smoothstep(r.z - 0.01, r.z + 0.01, d);
                        col = lerp(col, col * 0.85 + _Glow.rgb * 0.1, disc);
                        col += _Glow.rgb * (exp(-pow((d - r.z) / 0.018, 2.0)) * 0.95 + exp(-pow((d - r.z) / 0.12, 2.0)) * 0.25);
                    }
                }

                col = lerp(col, _Edge.rgb, smoothstep(_Fade * 0.35, _Fade, far));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
