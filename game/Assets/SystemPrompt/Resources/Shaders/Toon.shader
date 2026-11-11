// Cel shading for the latent-space props, the player's avatar and the lighthouse: two flat tones split
// by the light, a rim that glows like the sea light behind, and an ink outline from an inverted hull
// (not on the character models, see Dungeon/SeaFigure.cs). Scenery can also take the avatar's lamp (_Lamp, _Gloss)
// and thin out in front of the lens and the avatar (_Fade). A figure's rim comes from its depth while a sea camera draws
// (DepthRim, Dungeon/SeaShade.cs), and its SEADEPTH pass gives that depth and its shadow on the seabed.
// The character models bring their base colour as _MainTex, with how much of each texel is skin in its
// alpha (Editor/ModelAssetImporter.cs) and _SkinMask on, so skin takes its own warmer shade; everything
// else leaves the texture white and the mask off. A figure with drawn expressions also brings them
// (_Faces, _FaceOn), laid over its face from the front.
// A VRoid model (Characters/<model>/) has a material per part instead, whose alpha is coverage: hair and
// cloth cut out at _Cutoff (under MSAA as coverage, _AlphaToMask), eyes, brows and lashes blended over
// (_SrcBlend, _DstBlend, no _ZWrite), and the single-sided hair cards drawn from both sides (_Cull off),
// their back lit as its own face.
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
        _Fade ("Thins out near the lens and in front of the avatar", Float) = 0
        _FootDark ("Darkness at the foot, standing on the seabed", Float) = 1
        _Gloss ("Glint of the avatar's lamp", Float) = 0
        _Caustic ("The surface's light on it", Float) = 0
        _Blink ("Blinks as a rack's LED", Float) = 0
        _Breathe ("Breathes slowly as a crack", Float) = 0
        _Wrap ("How far the light's edge spreads either side of the terminator", Float) = 0.05
        _Inner ("Lighter toward the middle, facing the eye", Float) = 0
        _InnerColor ("That lighter colour", Color) = (1,1,1,1)
        _Sheen ("Soft highlight of the sea's light", Float) = 0
        _SwayAmp ("Sways in the water, metres", Float) = 0
        _SwayAnchor ("Height it sways from", Float) = 0
        _SwayLength ("Over how much height the sway grows (below 0: hangs from the anchor)", Float) = 1
        _OutlineColor ("Outline", Color) = (0.03,0.03,0.06,1)
        _OutlineWidth ("Outline width", Float) = 0.025
        _Cutoff ("Alpha cutoff", Float) = 0
        _AlphaToMask ("Cut-out edge as MSAA coverage", Float) = 0
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
            float _OutlineWidth, _Fade, _SwayAmp, _SwayAnchor, _SwayLength;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = SeaSway(mul(unity_ObjectToWorld, v.vertex).xyz, _SwayAmp, _SwayAnchor, _SwayLength);
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
                // Thinned as the surface it outlines, or it would stay behind as a dark shell.
                if (_Fade > 0.5) clip(SeaFadeKeep(i.world) - SeaBayer(i.pos.xy));
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
            AlphaToMask [_AlphaToMask]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color, _ShadeColor, _SkinShade, _RimColor, _Emission;
            float _RimPower, _SkinMask, _CameraLight, _Cutoff, _AlphaToMask, _ZWrite, _Lamp, _Fade, _FootDark, _Gloss, _Caustic, _Blink, _Breathe;
            float _Wrap, _Inner, _Sheen, _SwayAmp, _SwayAnchor, _SwayLength;
            float4 _InnerColor;
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

            // The figures' depth from the camera, only while a sea camera draws (Dungeon/SeaShade.cs; 0 otherwise).
            sampler2D _SeaFigureDepth;
            float _SeaFigureRim;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; float4 screen : TEXCOORD3; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                o.world = SeaSway(mul(unity_ObjectToWorld, v.vertex).xyz, _SwayAmp, _SwayAnchor, _SwayLength);
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            // A figure's rim from its depth (SeaShade): lit where the pixel three pixels out from it, across the outline
            // (along the normal as seen on screen), lies well behind it or shows nothing of the figures at all, so a band
            // of even width runs round the outline against what is behind, and none falls on the folds of a hoodie, inside
            // the hair or across the face, where what lies next to a pixel is the figure itself, a few centimetres off.
            // Most on the edges turned up and away, as from the sea's light behind and above.
            float DepthRim(v2f i, float3 n)
            {
                float2 suv = i.screen.xy / i.screen.w;
                // Up on screen is up in view space: ComputeScreenPos has already undone a flipped projection, and the
                // figures' depth is drawn into a texture the same way as the picture.
                float2 nv = mul((float3x3)UNITY_MATRIX_V, n).xy;
                float2 dir = nv / max(length(nv), 1e-3);
                float2 texel = (_ScreenParams.zw - 1.0) * (_ScreenParams.y / 900.0);
                float eye = -mul(UNITY_MATRIX_V, float4(i.world, 1)).z;
                float behind = tex2Dlod(_SeaFigureDepth, float4(suv + dir * texel * 3.0, 0, 0)).r - eye;
                float edge = saturate((behind - 0.3) / 0.3);
                float3 rimDir = normalize(-UNITY_MATRIX_V[2].xyz * 0.6 + float3(0, 1, 0));
                return edge * saturate(dot(n, rimDir) * 0.8 + 0.4);
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                // The maze's scenery makes way for the lens and the avatar as a screen door (SeaCommon.cginc).
                if (_Fade > 0.5) clip(SeaFadeKeep(i.world) - SeaBayer(i.pos.xy));
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                // The sea's light comes from one side of the world, so a figure turned to the camera often had
                // its face in shade, with the terminator crossing it. Figures take their light from above the
                // camera and a little to its right instead (row 2 of the view matrix points back at the camera).
                float3 key = normalize(UNITY_MATRIX_V[2].xyz + UNITY_MATRIX_V[0].xyz * 0.4 + float3(0, 0.8, 0));
                float3 toLight = normalize(lerp(normalize(_SeaLightDir.xyz), key, _CameraLight));
                float ndl = dot(n, toLight);
                float4 albedo = tex2D(_MainTex, i.uv);
                // A cut-out part of a VRoid figure (hair, cloth; Dungeon/SeaFigure.cs) hands its edge to MSAA as
                // coverage, its alpha sharpened to a ramp a pixel wide round _Cutoff, instead of a stair-stepped cut.
                float cover = saturate((albedo.a - _Cutoff) / max(fwidth(albedo.a), 1e-4) + 0.5);
                clip(_AlphaToMask > 0.5 ? cover - 0.01 : albedo.a - _Cutoff);
                if (_FaceOn > 0.5) albedo.rgb = Expression(albedo.rgb, i.world, n);
                // Skin turns to shade only well past the terminator, so the uneven normals of a generated face
                // don't break it into patches.
                float skin = albedo.a * _SkinMask;
                float lit = lerp(smoothstep(-_Wrap, _Wrap, ndl), smoothstep(-0.45, -0.25, ndl), skin);
                float3 shade = lerp(_ShadeColor.rgb, _SkinShade.rgb, skin);
                // A figure takes the colour of the zone it stands in: the seabed's light from below and the side, the
                // surface's from above, as a hue at the shade's own brightness (the seabed's light is dim, and as a
                // plain multiply it only darkened the shade), strongly in the shade and less on the lit side (the
                // figures are lit from the camera's side, so from the game's camera they are mostly lit), with a
                // little of the seabed's light thrown up under the lit side too. Skin keeps its own colours.
                float figure = step(0.5, _CameraLight), cloth = figure * (1.0 - skin * 0.85);
                float3 ground = _SeaGroundColor.rgb * 3.0 / max(dot(_SeaGroundColor.rgb, 1.0), 1e-3);
                float3 sky = _SeaSurfaceColor.rgb * 3.0 / max(dot(_SeaSurfaceColor.rgb, 1.0), 1e-3);
                float3 hue = lerp(ground, sky, saturate(n.y * 0.8 + 0.3));
                shade *= lerp(1.0, hue, 0.45 * cloth);
                float3 lift = lerp(1.0, hue, 0.2 * cloth) + _SeaGroundColor.rgb * saturate(0.2 - n.y) * 0.3 * cloth;
                float3 col = lerp(shade, _Color.rgb * lift, lit) * albedo.rgb;
                // Scenery standing on the seabed is darker toward its foot, the seabed's contact shade carried up it.
                col *= lerp(_FootDark, 1.0, smoothstep(0.0, 3.5, i.world.y));
                // The scenery modelled in Blender (Dungeon/SeaScene.cs) takes the lamp the avatar carries, a
                // point light at _SeaGlowPos, as the racks built in code do (Rack.shader), and the lamp glints on it:
                // a cel highlight with a hard edge where the face turns halfway between lamp and eye, not a soft spot.
                if (_Lamp > 0 || _Gloss > 0)
                {
                    float3 toLamp = _SeaGlowPos.xyz + float3(0, 1.4, 0) - i.world;
                    float d = length(toLamp);
                    float3 l = toLamp / max(d, 0.001);
                    float reach = saturate(1.0 - d / max(_SeaGlowPos.w * 2.0, 0.01));
                    col += _Color.rgb * albedo.rgb * _SeaGlowColor.rgb * reach * reach * saturate(dot(n, l)) * 2.0 * _Lamp;
                    float glint = smoothstep(0.86, 0.97, dot(n, normalize(l + view)));
                    col += _SeaGlowColor.rgb * glint * reach * reach * 0.35 * _Gloss;
                }
                // The maze's lights nearest the player (SeaPoints) on the scenery that takes the lamp, and at half on a
                // figure walking past: a beacon's cyan on the rack beside it, the rift's red on a hoodie.
                [branch] if (_SeaPoints > 0 && (_Lamp > 0 || figure > 0))
                    col += _Color.rgb * albedo.rgb * SeaPoints(i.world, n) * (figure > 0 ? 0.5 : 1.0);
                // The surface's light reaches the scenery a little more toward its top, and its net (SeaCaustic) plays
                // over it, most on what faces up. Not on the figures, whose light comes from the camera's side.
                if (_Caustic > 0)
                    col += _SeaSurfaceColor.rgb * (0.06 * saturate(i.world.y / 12.0) + albedo.rgb * SeaCaustic(i.world) * 0.35 * saturate(n.y * 0.7 + 0.3)) * _Caustic;
                // Something soft (the reef's chat pillows, Dungeon/SeaScene.cs) is lighter toward its middle as it
                // turns to the eye, and the sea's light sits on it as a soft highlight where it turns halfway between.
                if (_Inner > 0 || _Sheen > 0)
                {
                    float face = saturate(dot(n, view));
                    col = lerp(col, _InnerColor.rgb, face * face * _Inner);
                    col += _Color.rgb * smoothstep(0.92, 0.95, saturate(dot(n, normalize(toLight + view)))) * 0.5 * _Sheen;
                }
                // The lighthouse's beams passing over (SeaBeam), more on the side turned to the lamp and half in the
                // thing's own hue, so the dark racks it crosses turn a lighter blue, not a flat grey; on a figure, lit
                // from the camera's side already, less, or a dark hoodie turns pale as it goes by.
                float3 own = _Color.rgb * albedo.rgb;
                float3 hueOf = own * 3.0 / max(dot(own, 1.0), 1e-3);
                col += SeaBeam(i.world) * lerp(0.25, 1.0, saturate(dot(n, normalize(_SeaBeamApex.xyz - i.world)) * 0.8 + 0.2))
                    * lerp(1.0, hueOf, 0.5) * lerp(0.25, 0.15, figure);
                float rim = pow(1.0 - saturate(dot(n, view)), _RimPower);
                // On a figure only a narrow band at the outline, fading toward the ground and none on the back of a
                // hair card, so the folds of a hoodie and the inside of the hair don't catch specks of it.
                rim = figure > 0 ? smoothstep(0.5, 0.62, rim) * saturate(n.y * 0.6 + 0.4) * step(0, facing) : smoothstep(0.35, 0.55, rim);
                // While a sea camera draws, a figure's rim comes from its depth instead (DepthRim), less on the skin.
                [branch] if (figure > 0 && _SeaFigureRim > 0) col += _RimColor.rgb * DepthRim(i, n) * 0.6 * (1.0 - skin * 0.5) * step(0, facing);
                else col += _RimColor.rgb * rim * _RimColor.a;
                // The racks' LEDs blink and carry packets (SeaBlink); the rift's cracks breathe.
                float3 emission = _Emission.rgb;
                if (_Blink > 0) emission *= SeaBlink(i.world, n, _Blink);
                if (_Breathe > 0) emission *= lerp(1.0, 0.8 + 0.2 * sin(_SeaTime * 1.3 + i.world.y), _Breathe);
                // Light falling on a surface leaves it at white at most, under the lens's threshold (Dungeon/SeaLens.cs):
                // only what shines of itself glows, and a battle's flare on whatever it lights.
                col = saturate(col) + _Color.rgb * albedo.rgb * SeaFlash(i.world, n);
                // Where the zone's air lets lights carry (SeaAtmos), what shines is fogged apart from what it lights and
                // less (SeaFogGlow): a far rack sinks into the water while its LEDs still hang there.
                float3 fogged;
                [branch] if (_SeaAir.z > 0) fogged = SeaFog(col, i.world) + SeaFogGlow(emission, i.world);
                else fogged = SeaFog(col + emission, i.world);
                // Blended parts take their coverage; the rest write 1, not the skin mask some keep in alpha.
                return fixed4(fogged, _AlphaToMask > 0.5 ? cover : (_ZWrite > 0.5 ? 1 : albedo.a));
            }
            ENDCG
        }

        // A figure's or noise body's depth, in metres from the eye, for Dungeon/SeaShade.cs alone: from the camera for the
        // figures' rim, from above for their shadows on the seabed. The tag keeps the Built-in pipeline from drawing it with
        // the picture (it would only take it for a light's shadows or a camera's depth texture, and the sea has neither).
        // Cut-out hair and cloth cut as in the picture; the parts blended over (eyes, brows, lashes) leave nothing.
        Pass
        {
            Name "SEADEPTH"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff, _ZWrite, _SwayAmp, _SwayAnchor, _SwayLength;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float depth : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = SeaSway(mul(unity_ObjectToWorld, v.vertex).xyz, _SwayAmp, _SwayAnchor, _SwayLength);
                o.pos = UnityWorldToClipPos(world);
                o.depth = -mul(UNITY_MATRIX_V, float4(world, 1)).z;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                clip(_ZWrite - 0.5);
                clip(tex2D(_MainTex, i.uv).a - _Cutoff);
                return i.depth;
            }
            ENDCG
        }
    }
}
