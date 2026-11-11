// The sea cameras' lens (Dungeon/SeaLens.cs): what shines past white spills into a soft halo round it, the edges
// of the frame sink toward the deep, and the brightest of it rolls off instead of clipping flat (in a battle into
// white, in the maze keeping its colour); in a battle, a blow's shock wave bends the picture along its front. Only
// light over the threshold blooms, which a lit surface stays under; the lights of the maze and the battle's
// effects push past it.
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
    float _Intensity, _WhiteHot;
    float4 _Vignette;  // x where it starts and y where it is full, from the middle of the frame's height; z how strong
    float4 _VignetteColor;
    float4 _Waves[3];  // shock waves (SeaLens.Wave): xy the middle in uv, z the radius in frame heights, w strength
    // The zone's grade (Dungeon/SeaAtmos.cs SeaAir): rgb gain and a 1 to grade at all (0 draws as before); saturation;
    // the dark tones tinted toward rgb by a.
    float4 _GradeGain;
    float _GradeSaturation;
    float4 _GradeShadow;

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

    // Up to 0.92 as it is; past that it closes in on white without reaching it, so light piled on light
    // still shows its shape.
    half3 Shoulder(half3 c)
    {
        const half k = 0.92;
        half3 over = max(c - k, 0);
        return min(c, k) + (1 - k) * (1 - exp(-over / (1 - k)));
    }

    // Each channel rolled off on its own turns anything bright enough white, which the battle's effects want for
    // their white-hot cores; the maze's coloured lights would all go white with it and lose their zone's colour.
    // This rolls off the brightest channel and scales the others with it, so a coloured light keeps its hue;
    // _WhiteHot is how much of the white-hot roll-off is mixed back in. What is nearly white already (the white
    // door, a light beam's white core, the pale lamps of the plaza) still rolls off channel by channel: scaled down
    // by its bluest channel it would turn blue.
    half3 Roll(half3 c)
    {
        half m = max(c.r, max(c.g, c.b));
        half sat = 1.0 - min(c.r, min(c.g, c.b)) / max(m, 1e-4);
        half3 hue = c * (Shoulder(m.xxx).x / max(m, 1e-4));
        return lerp(Shoulder(c), hue, smoothstep(0.25, 0.45, sat) * (1.0 - _WhiteHot));
    }

    // Capped, so one stray pixel far past white glows no wider than a bright effect would.
    half4 FragPrefilter(v2f i) : SV_Target { return half4(Over(min(Box(i.uv), 16)), 1); }
    half4 FragDown(v2f i) : SV_Target { return half4(Box(i.uv), 1); }
    half4 FragUp(v2f i) : SV_Target { return half4(tex2D(_BaseTex, i.uv).rgb + Tent(i.uv), 1); }
    float Hash(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    // Where a shock wave's front passes, the picture is pushed outward a few pixels.
    float2 Bend(float2 uv)
    {
        float aspect = _ScreenParams.x / _ScreenParams.y;
        float2 shift = 0;
        [unroll] for (int k = 0; k < 3; k++)
        {
            float4 w = _Waves[k];
            float2 d = (uv - w.xy) * float2(aspect, 1);
            float r = length(d);
            float f = (r - w.z) / 0.025;
            shift += d / max(r, 1e-4) * float2(1 / aspect, 1) * exp(-f * f) * w.w * 0.03;
        }
        return saturate(uv - shift);
    }

    half4 FragCombine(v2f i) : SV_Target
    {
        float2 uv = i.uv;
        if (_Waves[0].w + _Waves[1].w + _Waves[2].w > 0) uv = Bend(uv); // never in the maze
        half4 c = tex2D(_MainTex, uv);
        c.rgb += tex2D(_BloomTex, uv).rgb * _Intensity;
        // Measured against the frame's height, so a wider window only adds dark at the sides.
        float2 d = (i.uv - 0.5) * float2(_ScreenParams.x / _ScreenParams.y, 1);
        c.rgb = lerp(c.rgb, c.rgb * _VignetteColor.rgb, smoothstep(_Vignette.x, _Vignette.y, length(d)) * _Vignette.z);
        // The zone's grade, before the roll-off so it never pushes a light past it: the water's colour in the darks
        // (the Graveyard's red, the open shallows' blue), its saturation, a slight gain.
        [branch] if (_GradeGain.a > 0)
        {
            c.rgb *= _GradeGain.rgb;
            half luma = dot(c.rgb, half3(0.299, 0.587, 0.114));
            c.rgb = max(lerp(luma.xxx, c.rgb, _GradeSaturation), 0);
            c.rgb = lerp(c.rgb, _GradeShadow.rgb, _GradeShadow.a * (1.0 - smoothstep(0.0, 0.3, luma)));
        }
        c.rgb = Roll(c.rgb);
        // A triangular dither of a step either way, the same pattern every frame, against banding in the dark blues.
        float2 p = i.uv * _ScreenParams.xy;
        c.rgb += (Hash(p) + Hash(p + 17.31) - 1.0) / 255.0;
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
