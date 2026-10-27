// 展示片的天幕（天空盒）：地平线一圈亮色往上过渡到深色，地平线上有一道光晕，上半部分叠几道很淡的斜条纹。
// 不加雾：颜色都是饱和的，人物从背景里跳出来，不发灰。
Shader "Showcase/Backdrop"
{
    Properties
    {
        _Top ("Top", Color) = (0.04,0.08,0.2,1)
        _Horizon ("Horizon", Color) = (0.2,0.45,1,1)
        _Glow ("Horizon glow (a: strength)", Color) = (0.5,0.8,1,0.5)
        _Stripes ("Stripes per screen height", Float) = 3
        _Phase ("Stripe phase", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Top, _Horizon, _Glow;
            float _Stripes, _Phase;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; float4 screen : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;
                float3 col = lerp(_Horizon.rgb, _Top.rgb, smoothstep(-0.02, 0.5, y));
                col += _Glow.rgb * exp(-abs(y) * 10.0) * _Glow.a;
                float2 s = i.screen.xy / i.screen.w;
                s.x *= _ScreenParams.x / _ScreenParams.y;
                float band = frac((s.x * 0.6 + s.y) * _Stripes + _Phase);
                col *= 1.0 + 0.06 * smoothstep(0.48, 0.5, band) * (1.0 - smoothstep(0.98, 1.0, band)) * smoothstep(0.08, 0.4, y);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
