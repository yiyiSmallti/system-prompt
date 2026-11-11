// Soft dark spots on the seabed under whatever stands or floats there (Dungeon/SeaArt.Shadow; the sea has
// no real shadows): darkens what is behind it, less the further into the fog. Near the player, where the seabed takes
// the figures' and noise bodies' own shadows (Dungeon/SeaShade.cs), it is only a lighter contact shade.
Shader "SystemPrompt/Shadow"
{
    Properties
    {
        _Strength ("Strength", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "IgnoreProjector"="True" }
        Blend Zero OneMinusSrcAlpha
        ZWrite Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float _Strength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = saturate(1.0 - length(i.uv * 2.0 - 1.0));
                a = a * a * (3.0 - 2.0 * a);
                // Where the seabed takes the shaped shadows (SeaShade), only the contact shade under the feet: 0.35 of 0.6.
                a *= 1.0 - 0.42 * SeaShadeCover(i.world);
                return fixed4(0, 0, 0, a * _Strength * SeaClear(i.world));
            }
            ENDCG
        }
    }
}
