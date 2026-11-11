// The figures' and noise bodies' shadows on the seabed, made soft (Dungeon/SeaShade.cs): from the map of their depth
// seen along the shadows' light, how much of the seabed's light each texel of the seabed loses, blurred. The seabed is
// the furthest thing along that light, so whatever was drawn into a texel stands over the seabed there; how high over
// it (the gap) is the seabed's own depth, from its plane, less the map's. What stands low (feet, shins) casts a
// close, tight shadow (red), what is high over the seabed (a trunk, a head, a noise body flung up) a wider and fainter
// one (green). Read once, filtered, by SeaCommon.cginc's SeaShadow: compared tap by tap at the seabed it came out in
// steps a texel wide.
Shader "Hidden/SystemPrompt/ShadeSoft"
{
    Properties
    {
        _MainTex ("Map", 2D) = "white" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    float4 _ShadeFloor; // the seabed's depth along the light at a point (x, y) of the map's view, in metres: x * x + y * y + z; w: the map's width in metres

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    static const float weights[9] = { 0.0276, 0.0663, 0.1238, 0.1802, 0.2042, 0.1802, 0.1238, 0.0663, 0.0276 };
    static const float Wide = 2.0; // texels between the wide shadow's taps

    // What stands over the seabed at a texel: how much of its light it takes (less the higher it is), split by height.
    float2 Cast(float2 uv)
    {
        float2 at = (uv - 0.5) * _ShadeFloor.w;
        float gap = dot(_ShadeFloor.xy, at) + _ShadeFloor.z - tex2Dlod(_MainTex, float4(uv, 0, 0)).r;
        float shade = step(0.05, gap) * saturate(1.2 - gap * 0.2);
        float high = saturate((gap - 0.5) / 1.2);
        return float2(shade * (1.0 - high), shade * high);
    }

    half4 FragAcross(v2f i) : SV_Target
    {
        float2 sum = 0;
        for (int k = 0; k < 9; k++)
        {
            float step = (k - 4) * _MainTex_TexelSize.x;
            sum.r += weights[k] * Cast(i.uv + float2(step, 0)).r;
            sum.g += weights[k] * Cast(i.uv + float2(step * Wide, 0)).g;
        }
        return half4(sum, 0, 1);
    }

    half4 FragDown(v2f i) : SV_Target
    {
        float2 sum = 0;
        for (int k = 0; k < 9; k++)
        {
            float step = (k - 4) * _MainTex_TexelSize.y;
            sum.r += weights[k] * tex2Dlod(_MainTex, float4(i.uv + float2(0, step), 0, 0)).r;
            sum.g += weights[k] * tex2Dlod(_MainTex, float4(i.uv + float2(0, step * Wide), 0, 0)).g;
        }
        return half4(sum, 0, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragAcross
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragDown
            ENDCG
        }
    }
}
