// The noise bodies: a skin of flickering blocks in two loud colours over black, vertices jumping a
// little now and then, an ink outline. _Alert pushes everything toward red. In voxel mode (_Voxels 1, on a
// Dungeon/SeaArt.Voxels mesh) each cube flickers on its own with bevelled seams, and now and then one
// jumps out of the body; uv2 holds the cube's centre and its id, negative for the cubes that stay put.
Shader "SystemPrompt/Glitch"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.9,0.15,0.2,1)
        _ColorB ("Color B", Color) = (1,0.85,0.2,1)
        _Dark ("Dark", Color) = (0.04,0.04,0.07,1)
        _Block ("Block size", Float) = 0.22
        _Alert ("Alert", Range(0,1)) = 0
        _Jitter ("Jitter", Float) = 0.04
        _Voxels ("Voxel mode", Float) = 0
        _OutlineColor ("Outline", Color) = (0.02,0.02,0.04,1)
        _OutlineWidth ("Outline width", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGINCLUDE
        #include "SeaCommon.cginc"
        float4 _ColorA, _ColorB, _Dark, _OutlineColor;
        float _Block, _Alert, _Jitter, _Voxels, _OutlineWidth;

        struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 voxel : TEXCOORD1; };

        float3 Jump(float3 objPos)
        {
            float step8 = floor(_SeaTime * 9.0);
            float band = floor(objPos.y * 6.0);
            float h = SeaHash(float2(band, step8));
            return float3((h - 0.5) * 2.0 * _Jitter * step(0.82, h), 0, 0);
        }

        // A cube popping out of the body for a beat, more often when alerted.
        float3 Pop(float4 voxel)
        {
            float tick = floor(_SeaTime * (4.0 + _Alert * 6.0) + voxel.w * 17.0);
            float h = SeaHash(float2(voxel.w * 53.1, tick * 0.71));
            float popped = step(0.9 - _Alert * 0.12, h) * step(0.0001, voxel.w) * _Voxels;
            return normalize(voxel.xyz + float3(0, 0.0001, 0)) * popped * 0.14;
        }

        float3 Shape(appdata v)
        {
            float3 p = v.vertex.xyz + Pop(v.voxel);
            return p + Jump(p);
        }
        ENDCG

        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, float4(Shape(v), 1)).xyz;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                world += n * _OutlineWidth * (0.6 + distance(world, _WorldSpaceCameraPos) * 0.04);
                o.pos = UnityWorldToClipPos(world);
                o.world = world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(SeaFog(_OutlineColor.rgb, i.world), 1); }
            ENDCG
        }

        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float3 obj : TEXCOORD0; float3 world : TEXCOORD1; float3 normal : TEXCOORD2; float2 uv : TEXCOORD3; float id : TEXCOORD4; };

            v2f vert(appdata v)
            {
                v2f o;
                o.obj = Shape(v);
                o.world = mul(unity_ObjectToWorld, float4(o.obj, 1)).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = v.uv;
                o.id = abs(v.voxel.w);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 cell = floor(i.obj / _Block);
                float2 key = _Voxels > 0.5 ? float2(i.id * 113.0, 7.7) : cell.xy + cell.z * 7.1;
                float2 offset = _Voxels > 0.5 ? key : cell.xy + cell.z;
                float tick = floor(_SeaTime * 6.0 + SeaHash(offset) * 6.0);
                float h = SeaHash(key + tick * 0.37);
                float3 col = h < 0.42 ? _Dark.rgb : (h < 0.72 ? _ColorA.rgb : _ColorB.rgb);
                col = lerp(col, float3(1, 0.12, 0.15), _Alert * step(0.42, h) * 0.7);
                if (_Voxels > 0.5)
                {
                    // Bevelled cubes: dark seams round each face, a lit corner, now and then a white flash.
                    float2 e = min(i.uv, 1.0 - i.uv);
                    col *= lerp(0.4, 1.0, smoothstep(0.03, 0.14, min(e.x, e.y)));
                    col += step(0.42, h) * smoothstep(0.3, 0.0, length(i.uv - float2(0.22, 0.78))) * 0.25;
                    col = lerp(col, float3(1, 1, 1), step(0.975, h) * 0.85);
                }
                col += (col * 0.6 + 0.1) * SeaFlash(i.world, normalize(i.normal));
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float rim = pow(1.0 - saturate(dot(normalize(i.normal), view)), 3.0);
                col += lerp(_ColorB.rgb, float3(1, 0.2, 0.2), _Alert) * smoothstep(0.4, 0.6, rim) * 0.6;
                return fixed4(SeaFog(col, i.world), 1);
            }
            ENDCG
        }
    }
}
