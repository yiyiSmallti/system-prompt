// The battle camera's glow (Dungeon/BattleGlow.cs): what the effects light past white spills into a soft halo
// round it, and the brightest of it rolls off into white instead of clipping flat. Only light over the
// threshold blooms, which the scene itself stays under; only the effects push past it.
Shader "SystemPrompt/Bloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _BaseTex ("Larger level", 2D) = "black" {}
        _BloomTex ("Bloom", 2D) = "black" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex, _BaseTex, _BloomTex;
    float4 _MainTex_TexelSize;
    float4 _Curve;     // threshold - knee, knee * 2, 0.25 / knee, threshold
    float _Intensity;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    // Four bilinear taps round the point: a box over four by four texels.
    half3 Box(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        half3 s = tex2D(_MainTex, uv + d.xy).rgb + tex2D(_MainTex, uv + d.zy).rgb
                + tex2D(_MainTex, uv + d.xw).rgb + tex2D(_MainTex, uv + d.zw).rgb;
        return s * 0.25;
    }

    // A tent over three by three texels, for going back up.
    half3 Tent(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);
        half3 s = tex2D(_MainTex, uv - d.xy).rgb;
        s += tex2D(_MainTex, uv - d.wy).rgb * 2;
        s += tex2D(_MainTex, uv - d.zy).rgb;
        s += tex2D(_MainTex, uv + d.zw).rgb * 2;
        s += tex2D(_MainTex, uv).rgb * 4;
        s += tex2D(_MainTex, uv + d.xw).rgb * 2;
        s += tex2D(_MainTex, uv + d.zy).rgb;
        s += tex2D(_MainTex, uv + d.wy).rgb * 2;
        s += tex2D(_MainTex, uv + d.xy).rgb;
        return s * (1.0 / 16);
    }

    // What of a colour is over the threshold, eased in over the knee.
    half3 Over(half3 c)
    {
        half bright = max(c.r, max(c.g, c.b));
        half soft = clamp(bright - _Curve.x, 0, _Curve.y);
        soft = _Curve.z * soft * soft;
        return c * max(soft, bright - _Curve.w) / max(bright, 1e-4);
    }

    // Up to 0.9 as it is; past that it closes in on white without reaching it, so light piled on light
    // still shows its shape.
    half3 Shoulder(half3 c)
    {
        const half k = 0.9;
        half3 over = max(c - k, 0);
        return min(c, k) + (1 - k) * (1 - exp(-over / (1 - k)));
    }

    // Capped, so one stray pixel far past white glows no wider than a bright effect would.
    half4 FragPrefilter(v2f i) : SV_Target { return half4(Over(min(Box(i.uv), 16)), 1); }
    half4 FragDown(v2f i) : SV_Target { return half4(Box(i.uv), 1); }
    half4 FragUp(v2f i) : SV_Target { return half4(tex2D(_BaseTex, i.uv).rgb + Tent(i.uv), 1); }
    half4 FragCombine(v2f i) : SV_Target
    {
        half4 c = tex2D(_MainTex, i.uv);
        c.rgb = Shoulder(c.rgb + tex2D(_BloomTex, i.uv).rgb * _Intensity);
        return c;
    }
    ENDCG

    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragPrefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragDown
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragUp
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragCombine
            ENDCG }
    }
}
