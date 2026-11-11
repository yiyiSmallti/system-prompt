// Additive light for everything that shines in the sea: floating fragments, beams, motes, the vision
// fans of the noise bodies and the firewall. Fades into the fog instead of tinting toward it, and in the maze out
// near the lens (SeaNearGlow). Drawn over what is behind (premultiplied: _DstBlend OneMinusSrcAlpha) it is smoke
// instead (Dungeon/BattleEffects.cs).
Shader "SystemPrompt/Glow"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Scroll ("UV scroll (x, y per second)", Vector) = (0,0,0,0)
        _FogReach ("How far into the zone's fog it carries (1 as what it lights; SeaCommon.cginc SeaReach)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color, _Scroll;
            float _FogReach;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + _Scroll.xy * _SeaTime;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                float a = t.a * i.color.a * _Color.a * SeaClear(i.world, SeaReach(_FogReach)) * SeaNearGlow(i.world);
                return fixed4(t.rgb * i.color.rgb * _Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
