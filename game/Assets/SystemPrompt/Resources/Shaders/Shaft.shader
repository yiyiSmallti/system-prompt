// A shaft of light (SeaArt.Shaft's ribbon): turned about its own axis to face the camera wherever it is seen
// from, a bright core in a soft glow, with streaks running along it. The ribbon runs along the object's y
// from 0 to 1 (scaled to its length); its width is in the mesh, in metres. Additive, like Glow, and like it fading
// out near the lens in the maze.
Shader "SystemPrompt/Shaft"
{
    Properties
    {
        _Color ("Glow", Color) = (1,1,1,1)
        _CoreColor ("Core", Color) = (1,1,1,1)
        _Core ("Core width (fraction of the half width)", Float) = 0.15
        _Flow ("Streaks, metres per second along the shaft", Float) = 3
        _Streaks ("Streak strength", Float) = 0.6
        _Fade ("Fade toward the far end (power, 0 none)", Float) = 0
        _Base ("Fade in from the near end, metres", Float) = 0
        _FogReach ("How far into the zone's fog it carries (1 as what it lights; SeaCommon.cginc SeaReach)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _Color, _CoreColor;
            float _Core, _Flow, _Streaks, _Fade, _Base, _FogReach;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 along = mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0));
                float len = length(along);
                float3 axis = along / max(len, 1e-4);
                float3 p = origin + along * v.vertex.y;
                float3 view = _WorldSpaceCameraPos - p;
                float3 side = cross(axis, view);
                // Seen straight down its axis there is no side to turn to; any will do.
                if (dot(side, side) < 1e-6) side = cross(axis, abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0));
                p += normalize(side) * v.vertex.x;
                o.world = p;
                o.pos = UnityWorldToClipPos(p);
                o.uv = float3(v.uv.x, v.vertex.y, len);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float x = i.uv.x;          // -1 to 1 across
                float metres = i.uv.y * i.uv.z;
                float glow = exp(-x * x * 5.0);
                float core = exp(-x * x / max(_Core * _Core, 1e-4));
                // Bright streaks travelling along, broken up a little across the width.
                float s = metres - _SeaTime * _Flow;
                float band = pow(0.5 + 0.5 * sin(s * 1.7 + sin(s * 0.41) * 2.3), 3.0);
                float threads = 0.8 + 0.2 * sin(x * 11.0 + s * 0.9);
                float shine = (1.0 + _Streaks * (band - 0.35)) * threads;
                float ends = pow(saturate(1.0 - i.uv.y), _Fade) * saturate(metres / max(_Base, 1e-3));
                float3 col = _Color.rgb * _Color.a * glow * shine + _CoreColor.rgb * _CoreColor.a * core * (0.85 + 0.3 * band);
                return fixed4(col * ends * SeaClear(i.world, SeaReach(_FogReach)) * SeaNearGlow(i.world), 1);
            }
            ENDCG
        }
    }
}
