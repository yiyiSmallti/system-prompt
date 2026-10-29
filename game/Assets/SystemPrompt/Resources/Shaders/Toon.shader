// Cel shading for the latent-space props, the player's avatar and the lighthouse: two flat tones split
// by the light, a rim that glows like the sea light behind, and an ink outline from an inverted hull
// (not on the character models, see Dungeon/SeaFigure.cs). Scenery can also take the avatar's lamp (_Lamp).
// The character models bring their base colour as _MainTex, with how much of each texel is skin in its
// alpha (Editor/ModelAssetImporter.cs) and _SkinMask on, so skin takes its own warmer shade; everything
// else leaves the texture white and the mask off. A figure with drawn expressions also brings them
// (_Faces, _FaceOn), laid over its face from the front.
// A VRoid model (Characters/<model>/) has a material per part instead, whose alpha is coverage: hair and
// cloth cut out at _Cutoff, eyes, brows and lashes blended over (_SrcBlend, _DstBlend, no _ZWrite), and
// the single-sided hair cards drawn from both sides (_Cull off), their back lit as its own face.
Shader "SystemPrompt/Toon"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Lit", Color) = (1,1,1,1)
        _ShadeColor ("Shade", Color) = (0.5,0.55,0.7,1)
        _SkinShade ("Skin shade", Color) = (0.93,0.78,0.75,1)
        _SkinMask ("Skin mask in albedo alpha", Float) = 0
        _CameraLight ("Lit from the camera's side", Float) = 0
        _Faces ("Expression atlas, 3 x 3", 2D) = "black" {}
        _FaceDepth ("Face depth from the front", 2D) = "white" {}
        _FaceOn ("Expressions", Float) = 0
        _FaceCells ("Eyes, mouth, blush cells (-1 none)", Vector) = (-1,-1,-1,0)
        _RimColor ("Rim", Color) = (0.4,0.9,1,1)
        _RimPower ("Rim power", Float) = 3
        _Emission ("Emission", Color) = (0,0,0,0)
        _Lamp ("Lit by the avatar's lamp", Float) = 0
        _OutlineColor ("Outline", Color) = (0.03,0.03,0.06,1)
        _OutlineWidth ("Outline width", Float) = 0.025
        _Cutoff ("Alpha cutoff", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _OutlineColor;
            float _OutlineWidth;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.world = world;
                // A width of 0 turns the outline off: the hull goes outside the view and is clipped.
                if (_OutlineWidth <= 0) { o.pos = float4(2, 2, 2, 1); return o; }
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                // Wider with distance a little, so far props keep a line without near ones getting fat.
                float d = distance(world, _WorldSpaceCameraPos);
                world += n * _OutlineWidth * (0.6 + d * 0.04);
                o.pos = UnityWorldToClipPos(world);
                o.world = world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(SeaFog(_OutlineColor.rgb, i.world), 1);
            }
            ENDCG
        }

        Pass
        {
            Name "TOON"
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color, _ShadeColor, _SkinShade, _RimColor, _Emission;
            float _RimPower, _SkinMask, _CameraLight, _Cutoff, _ZWrite, _Lamp;
            sampler2D _Faces, _FaceDepth;
            float _FaceOn;
            float4 _FaceCells, _FaceForward;
            float4x4 _FaceFromWorld;

            // A cell of the expression atlas at (u, v) of the face box; the atlas runs row by row from the top.
            float4 FaceCell(float2 uv, float index)
            {
                if (index < 0) return 0;
                float column = fmod(index, 3), row = floor(index / 3);
                return tex2D(_Faces, float2((column + uv.x) / 3, (2 - row + uv.y) / 3));
            }

            // The expression laid over the face from the front (Dungeon/SeaFigure.cs): only on surfaces facing
            // out of the face and as deep as the face was where the picture was taken, so neither a lock of hair
            // in front of the eyes nor the back of the head picks it up.
            float3 Expression(float3 col, float3 world, float3 n)
            {
                float3 f = mul(_FaceFromWorld, float4(world, 1)).xyz;
                if (any(f.xy < 0) || any(f.xy > 1) || dot(n, _FaceForward.xyz) < 0.2) return col;
                if (abs(f.z - tex2D(_FaceDepth, f.xy).r) > 0.015) return col;
                float4 blush = FaceCell(f.xy, _FaceCells.z), eyes = FaceCell(f.xy, _FaceCells.x), mouth = FaceCell(f.xy, _FaceCells.y);
                col = lerp(col, blush.rgb, blush.a);
                col = lerp(col, eyes.rgb, eyes.a);
                return lerp(col, mouth.rgb, mouth.a);
            }

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                // The sea's light comes from one side of the world, so a figure turned to the camera often had
                // its face in shade, with the terminator crossing it. Figures take their light from above the
                // camera and a little to its right instead (row 2 of the view matrix points back at the camera).
                float3 key = normalize(UNITY_MATRIX_V[2].xyz + UNITY_MATRIX_V[0].xyz * 0.4 + float3(0, 0.8, 0));
                float3 toLight = normalize(lerp(normalize(_SeaLightDir.xyz), key, _CameraLight));
                float ndl = dot(n, toLight);
                float4 albedo = tex2D(_MainTex, i.uv);
                clip(albedo.a - _Cutoff);
                if (_FaceOn > 0.5) albedo.rgb = Expression(albedo.rgb, i.world, n);
                // Skin turns to shade only well past the terminator, so the uneven normals of a generated face
                // don't break it into patches.
                float skin = albedo.a * _SkinMask;
                float lit = lerp(smoothstep(-0.05, 0.05, ndl), smoothstep(-0.45, -0.25, ndl), skin);
                float3 shade = lerp(_ShadeColor.rgb, _SkinShade.rgb, skin);
                float3 col = lerp(shade, _Color.rgb, lit) * albedo.rgb;
                // The scenery modelled in Blender (Dungeon/SeaScene.cs) takes the lamp the avatar carries, a
                // point light at _SeaGlowPos, as the racks built in code do (Rack.shader).
                if (_Lamp > 0)
                {
                    float3 toLamp = _SeaGlowPos.xyz + float3(0, 1.4, 0) - i.world;
                    float d = length(toLamp);
                    float reach = saturate(1.0 - d / max(_SeaGlowPos.w * 2.0, 0.01));
                    col += _Color.rgb * albedo.rgb * _SeaGlowColor.rgb * reach * reach * saturate(dot(n, toLamp / max(d, 0.001))) * 2.0 * _Lamp;
                }
                col += _Color.rgb * albedo.rgb * SeaFlash(i.world, n);
                float rim = pow(1.0 - saturate(dot(n, view)), _RimPower);
                col += _RimColor.rgb * smoothstep(0.35, 0.55, rim) * _RimColor.a;
                col += _Emission.rgb;
                // Blended parts take their coverage; the rest write 1, not the skin mask some keep in alpha.
                return fixed4(SeaFog(col, i.world), _ZWrite > 0.5 ? 1 : albedo.a);
            }
            ENDCG
        }
    }
}
