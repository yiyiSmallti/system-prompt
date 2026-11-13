// Cel shading for the latent-space props, the player's avatar and the lighthouse: two flat tones split
// by the light, a rim that glows like the sea light behind, and an ink outline from an inverted hull
// (on the character models a thin line of even width on the picture, _OutlinePx, see Dungeon/SeaFigure.cs). Scenery can also take the avatar's lamp (_Lamp, _Gloss)
// and thin out in front of the lens and the avatar (_Fade). A figure's rim comes from its depth while a sea camera draws
// (DepthRim, Dungeon/SeaShade.cs), and its SEADEPTH pass gives that depth and its shadow on the seabed.
// A figure (_CameraLight) is drawn as the anime games draw theirs: lit from the camera's upper right, two flat tones
// with a drawn edge, its shade deeper and purer in colour (_ShadePure), its face shaded as a ball round the head
// (_FaceRound, _HeadAt) and its own shadows thrown on itself from the figures' depth (CastShade, _CastOwn).
// The character models bring their base colour as _MainTex, with how much of each texel is skin in its
// alpha (Editor/ModelAssetImporter.cs) and _SkinMask on, so skin takes its own warmer shade; everything
// else leaves the texture white and the mask off. A figure with drawn expressions also brings them
// (_Faces, _FaceOn), laid over its face from the front.
// A VRoid model (Characters/<model>/) has a material per part instead, whose alpha is coverage: hair and
// cloth cut out at _Cutoff (under MSAA as coverage, _AlphaToMask), eyes, brows and lashes blended over
// (_SrcBlend, _DstBlend, no _ZWrite), and the single-sided hair cards drawn from both sides (_Cull off),
// their back lit as its own face. Its skin, modelled with clean normals, turns to shade round the terminator itself
// (_SkinEdge), which is what gives a hand its fingers and a leg its roundness; and what it wears under something else
// (a shirt under an open hoodie) is drawn a little behind where it is (_Sink), so it doesn't show through the cloth
// over it where the two meshes cross as the body bends. A loose top that hangs over the thighs is kept out of them
// (_Legs, the two legs as rounded cones SeaFigure sets each frame): its hem is weighted to the hips and stood still
// while a lifted thigh came through it, shorts and all; and the inside of such a top, seen at the collar's corners
// and through its eyelets, is lit as the outside is (_BackLit) instead of showing as dark wedges and dots.
Shader "SystemPrompt/Toon"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Lit", Color) = (1,1,1,1)
        _ShadeColor ("Shade", Color) = (0.5,0.55,0.7,1)
        _SkinShade ("Skin shade", Color) = (0.93,0.78,0.75,1)
        _SkinMask ("Skin mask in albedo alpha", Float) = 0
        _SkinEdge ("Skin turns from shade to lit between these (dot of normal and light)", Vector) = (-0.45,-0.25,0,0)
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
        _Sink ("Drawn this far behind where it is, metres (under another layer)", Float) = 0
        _Lift ("Brighter by this much where the mesh's vertex colour is red 0 (a generated tail's dark underside)", Float) = 0
        _BackLit ("The inside of a two-sided part lit as its outside", Float) = 0
        _Legs ("Kept out of the legs under it (_LegTop0.._LegEnd1)", Float) = 0
        _Inner ("Lighter toward the middle, facing the eye", Float) = 0
        _InnerColor ("That lighter colour", Color) = (1,1,1,1)
        _Sheen ("Soft highlight of the sea's light", Float) = 0
        _SwayAmp ("Sways in the water, metres", Float) = 0
        _SwayAnchor ("Height it sways from", Float) = 0
        _SwayLength ("Over how much height the sway grows (below 0: hangs from the anchor)", Float) = 1
        _OutlineColor ("Outline", Color) = (0.03,0.03,0.06,1)
        _OutlineWidth ("Outline width", Float) = 0.025
        _OutlinePx ("Outline as wide on the picture, pixels at 900 high (a figure's; 0: the width above, metres)", Float) = 0
        _OutlineTint ("That line in the part's own colour, darkened", Float) = 0
        _OutlineBare ("No line on skin (a generated model)", Float) = 0
        _Chin ("The chin's shade on the neck (_ChinAt, _ChinUp, _ChinAhead)", Float) = 0
        _FaceRound ("Shaded as a ball round the head (_HeadAt): 1 all of it (a face's own parts), 2 its skin near the head (a generated model)", Float) = 0
        _ShadePure ("A figure's cloth this much purer in colour in its shade", Float) = 0.2
        _CastOwn ("How much of the figure's own shadows it takes (hair on the brow, a hem on the thighs)", Float) = 1
        _HairGloss ("Band of light round hair", Float) = 0
        _HairGlossColor ("Its colour", Color) = (0.5,0.6,0.8,1)
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
            AlphaToMask [_AlphaToMask]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SeaCommon.cginc"

            float4 _OutlineColor, _Color, _MainTex_ST, _LegTop0, _LegEnd0, _LegTop1, _LegEnd1;
            float _OutlineWidth, _OutlinePx, _OutlineTint, _OutlineBare, _SkinMask, _Cutoff, _AlphaToMask, _Sink, _Legs, _Fade, _SwayAmp, _SwayAnchor, _SwayLength;
            sampler2D _MainTex;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = SeaSway(mul(unity_ObjectToWorld, v.vertex).xyz, _SwayAmp, _SwayAnchor, _SwayLength);
                if (_Legs > 0.5) world = SeaOutOf(SeaOutOf(world, _LegTop0, _LegEnd0), _LegTop1, _LegEnd1);
                o.world = world;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                // A width of 0 turns the outline off: the hull goes outside the view and is clipped.
                if (_OutlineWidth <= 0 && _OutlinePx <= 0) { o.pos = float4(2, 2, 2, 1); return o; }
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                // Wider with distance a little, so far props keep a line without near ones getting fat.
                float d = distance(world, _WorldSpaceCameraPos);
                float width = _OutlineWidth * (0.6 + d * 0.04);
                // A figure's line is drawn as wide on the picture wherever the figure stands (_OutlinePx, pixels of a
                // picture 900 high): an ink line, not a shell. Held between a hair's and a finger's width in the world,
                // so a close-up's line is a little heavier and a far figure's does not swallow its hands.
                if (_OutlinePx > 0)
                {
                    // (By how deep it is in the view, not how far from the eye: by distance the line grew a third heavier
                    // toward the picture's corners.)
                    width = clamp(_OutlinePx * UnityWorldToClipPos(world).w * 2.0 / (abs(UNITY_MATRIX_P[1][1]) * 900.0), 0.0008, 0.011);
                    // None on a generated model's skin (_OutlineBare): the hull of its jaw and brow showed through
                    // as strokes across the face.
                    if (_OutlineBare > 0.5) width *= 1.0 - saturate(tex2Dlod(_MainTex, float4(o.uv, 0, 0)).a * _SkinMask * 2.0);
                }
                world += n * width;
                o.pos = UnityWorldToClipPos(world);
                if (_Sink > 0)
                {
                    float4 behind = UnityWorldToClipPos(world + normalize(world - _WorldSpaceCameraPos) * _Sink);
                    o.pos.z = behind.z / behind.w * o.pos.w;
                }
                o.world = world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Thinned as the surface it outlines, or it would stay behind as a dark shell.
                if (_Fade > 0.5) clip(SeaFadeKeep(i.world) - SeaBayer(i.pos.xy));
                float3 ink = _OutlineColor.rgb;
                float cover = 1;
                if (_OutlinePx > 0)
                {
                    float4 albedo = tex2D(_MainTex, i.uv);
                    // A cut-out part's line follows its cut edge, and as the part does: as coverage under MSAA.
                    if (_Cutoff > 0)
                    {
                        cover = saturate((albedo.a - _Cutoff) / max(fwidth(albedo.a), 1e-4) + 0.5);
                        clip(_AlphaToMask > 0.5 ? cover - 0.01 : albedo.a - _Cutoff);
                        if (_AlphaToMask < 0.5) cover = 1;
                    }
                    // The part's own colour, darkened: charcoal round the grey top, navy round the blue hair, and
                    // a warm brown round skin, as a drawing's line is.
                    float3 own = albedo.rgb * _Color.rgb;
                    float skin = albedo.a * _SkinMask;
                    ink = lerp(ink, lerp(own * own * 0.42 + own * 0.05, float3(0.5, 0.29, 0.27), skin), _OutlineTint);
                }
                return fixed4(SeaFog(ink, i.world), cover);
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
            float _Wrap, _Sink, _Lift, _BackLit, _Legs, _Inner, _Sheen, _HairGloss, _Chin, _SwayAmp, _SwayAnchor, _SwayLength;
            float4 _InnerColor, _SkinEdge, _LegTop0, _LegEnd0, _LegTop1, _LegEnd1, _HairGlossColor, _ChinAt, _ChinUp, _ChinAhead;
            float4 _HeadAt;
            float _FaceRound, _CastOwn, _ShadePure;
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
            // in front of the eyes nor the back of the head picks it up. With the colour, how much of the texel the eyes
            // and mouth drawn there cover: a shut eye is skin drawn over the eye, and is to be lit as the skin round it
            // (lit as the eye under it, which is no skin, the lid took the zone's hue the skin is spared and the cloth's
            // shade, and showed as a grey-violet patch on the face).
            float4 Expression(float3 col, float3 world, float3 n)
            {
                float3 f = mul(_FaceFromWorld, float4(world, 1)).xyz;
                if (any(f.xy < 0) || any(f.xy > 1) || dot(n, _FaceForward.xyz) < 0.2) return float4(col, 0);
                if (abs(f.z - tex2D(_FaceDepth, f.xy).r) > 0.015) return float4(col, 0);
                float4 blush = FaceCell(f.xy, _FaceCells.z), eyes = FaceCell(f.xy, _FaceCells.x), mouth = FaceCell(f.xy, _FaceCells.y);
                col = lerp(col, blush.rgb, blush.a);
                col = lerp(col, eyes.rgb, eyes.a);
                return float4(lerp(col, mouth.rgb, mouth.a), max(eyes.a, mouth.a));
            }

            // The figures' depth from the camera, only while a sea camera draws (Dungeon/SeaShade.cs; 0 otherwise).
            sampler2D _SeaFigureDepth;
            float _SeaFigureRim;

            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; float4 screen : TEXCOORD3; float lift : TEXCOORD4; };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                // A mesh with no colours of its own reads as white: no lift.
                o.lift = _Lift * (1.0 - v.color.r);
                o.world = SeaSway(mul(unity_ObjectToWorld, v.vertex).xyz, _SwayAmp, _SwayAnchor, _SwayLength);
                if (_Legs > 0.5) o.world = SeaOutOf(SeaOutOf(o.world, _LegTop0, _LegEnd0), _LegTop1, _LegEnd1);
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(o.world);
                // An under-layer keeps its place on screen and takes the depth of a point further along the eye's ray.
                if (_Sink > 0)
                {
                    float4 behind = UnityWorldToClipPos(o.world + normalize(o.world - _WorldSpaceCameraPos) * _Sink);
                    o.pos.z = behind.z / behind.w * o.pos.w;
                }
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

            // A figure's own shadows on itself, from the same depth: a few steps from the surface toward the light, and
            // in shadow where the figures' depth at a step lies in front of it, by more than the depth's own grain
            // (bias, wider on a surface seen edge on) and by less than a body is thick (so what stands a pace in front
            // of her throws nothing on her). What a drawing puts shadow under: the fringe on the brow, the jaw on the
            // neck, a collar, a sleeve and a skirt's hem on what they hang over. The depth is a point per pixel, so
            // each step is weighed between its four nearest, or the shadow's edge comes out in stairs; and the steps
            // are even and close (a shadow is every step's copy of what throws it, laid over one another: six steps
            // growing to 5 cm apart left its far edge in scallops as wide). A face takes them from a seventh as far
            // (reach): its fringe's shadow under the fringe, not every lock round the head drawn across the cheeks.
            float CastShade(v2f i, float3 n, float3 toLight, float size, float slope, float face)
            {
                float2 texels = _ScreenParams.xy, texel = 1.0 / texels;
                // (On a face only by what stands 8 mm to 5 cm off it: the fringe, not the nose on the cheek beside it
                // nor a hood's brim, which put the whole face in shade.)
                float reach = lerp(1.0, 0.15, face);
                float bias = lerp(0.004, 0.008, face) * size + slope * 1.5, thick = lerp(0.14, 0.05, face) * size, soft = bias + 0.002 * size;
                float3 from = i.world + n * 0.003 * size;
                float3 pace = toLight * 0.0135 * size * reach;
                float own = UnityWorldToClipPos(from).w;
                float cast = 0, entry = -1, before = own, ahead = own;
                for (int k = 0; k < 12; k++)
                {
                    float4 clip = UnityWorldToClipPos(from + pace * (k + 0.6));
                    float4 screen = ComputeScreenPos(clip);
                    float2 t = screen.xy / screen.w * texels - 0.5, f = frac(t), base = (floor(t) + 0.5) * texel;
                    float4 depth = float4(
                        tex2Dlod(_SeaFigureDepth, float4(base, 0, 0)).r,
                        tex2Dlod(_SeaFigureDepth, float4(base + float2(texel.x, 0), 0, 0)).r,
                        tex2Dlod(_SeaFigureDepth, float4(base + float2(0, texel.y), 0, 0)).r,
                        tex2Dlod(_SeaFigureDepth, float4(base + texel, 0, 0)).r);
                    float4 gap = clip.w - depth;
                    float4 s = smoothstep(bias, soft, gap) * (1.0 - smoothstep(thick * 0.6, thick, gap));
                    cast = max(cast, lerp(lerp(s.x, s.y, f.x), lerp(s.z, s.w, f.x), f.y));
                    // The first step to come under something the ray was still behind a step ago.
                    float2 pick = step(0.5, f);
                    float nearest = lerp(lerp(depth.x, depth.y, pick.x), lerp(depth.z, depth.w, pick.x), pick.y);
                    if (entry < 0 && before - nearest > bias)
                    {
                        entry = k + 0.6;
                        ahead = before;
                    }
                    before = clip.w;
                }
                // The steps find what the ray passes behind only where a step lands behind it: what hangs a
                // centimetre off the surface is behind the ray for less than a step, and its shadow's far edge came
                // out in stairs, one for each step that still caught it. So where the ray first comes under something
                // it was behind at the step before, the place between the two steps where it comes under it is
                // searched out to an eighth of a step, and the ray tried against it there.
                if (entry >= 0)
                {
                    float lo = max(entry - 1.0, 0.0), hi = entry;
                    for (int j = 0; j < 3; j++)
                    {
                        float mid = (lo + hi) * 0.5;
                        float4 screen = ComputeScreenPos(UnityWorldToClipPos(from + pace * mid));
                        if (ahead - tex2Dlod(_SeaFigureDepth, float4(screen.xy / screen.w, 0, 0)).r > bias) hi = mid;
                        else lo = mid;
                    }
                    float4 clip = UnityWorldToClipPos(from + pace * hi);
                    float4 screen = ComputeScreenPos(clip);
                    float2 t = screen.xy / screen.w * texels - 0.5, f = frac(t), base = (floor(t) + 0.5) * texel;
                    float4 gap = clip.w - float4(
                        tex2Dlod(_SeaFigureDepth, float4(base, 0, 0)).r,
                        tex2Dlod(_SeaFigureDepth, float4(base + float2(texel.x, 0), 0, 0)).r,
                        tex2Dlod(_SeaFigureDepth, float4(base + float2(0, texel.y), 0, 0)).r,
                        tex2Dlod(_SeaFigureDepth, float4(base + texel, 0, 0)).r);
                    float4 s = smoothstep(bias, soft, gap) * (1.0 - smoothstep(thick * 0.6, thick, gap));
                    cast = max(cast, lerp(lerp(s.x, s.y, f.x), lerp(s.z, s.w, f.x), f.y));
                }
                return cast;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                // The maze's scenery makes way for the lens and the avatar as a screen door (SeaCommon.cginc).
                if (_Fade > 0.5) clip(SeaFadeKeep(i.world) - SeaBayer(i.pos.xy));
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                // The sea's light comes from one side of the world, so a figure turned to the camera often had
                // its face in shade, with the terminator crossing it. Figures take their light from the camera's side
                // instead (row 2 of the view matrix points back at the camera): from above it and well to its right, as
                // a drawing's key light stands, so each form has a lit side and a side in shade whichever way the figure
                // turns; and from steeper overhead for the shadows they throw on themselves (CastShade), which then
                // fall down the figure, under what throws them, rather than away behind it.
                float3 key = normalize(UNITY_MATRIX_V[2].xyz * 0.55 + UNITY_MATRIX_V[0].xyz * 0.6 + float3(0, 0.75, 0));
                float3 overhead = normalize(UNITY_MATRIX_V[2].xyz * 0.3 + UNITY_MATRIX_V[0].xyz * 0.42 + float3(0, 0.9, 0));
                float3 toLight = normalize(lerp(normalize(_SeaLightDir.xyz), key, _CameraLight));
                float ndl = dot(n, toLight);
                float4 albedo = tex2D(_MainTex, i.uv);
                albedo.rgb *= 1.0 + i.lift;
                // A cut-out part of a VRoid figure (hair, cloth; Dungeon/SeaFigure.cs) hands its edge to MSAA as
                // coverage, its alpha sharpened to a ramp a pixel wide round _Cutoff, instead of a stair-stepped cut.
                float cover = saturate((albedo.a - _Cutoff) / max(fwidth(albedo.a), 1e-4) + 0.5);
                clip(_AlphaToMask > 0.5 ? cover - 0.01 : albedo.a - _Cutoff);
                float drawn = 0;
                if (_FaceOn > 0.5)
                {
                    float4 face = Expression(albedo.rgb, i.world, n);
                    albedo.rgb = face.rgb;
                    drawn = face.a;
                }
                // A generated model's skin turns to shade only well past the terminator, so the uneven normals of its
                // face don't break it into patches; a VRoid model's does at the terminator, with a drawn edge (_SkinEdge).
                float skin = max(albedo.a, drawn) * _SkinMask;
                float figure = step(0.5, _CameraLight);
                // Two flat tones with a drawn edge between them: as wide as _Wrap, and never finer than a pixel.
                float wrap = max(_Wrap, fwidth(ndl) * 0.7);
                float lit = lerp(smoothstep(-wrap, wrap, ndl), smoothstep(_SkinEdge.x, _SkinEdge.y, ndl), skin);
                // A face is shaded as a ball round the head, not by its own surface: the nose, the lips and the eye
                // sockets would each take their own patch of shade, and a generated face's uneven normals break it into
                // blotches. The ball is flattened from above and below, so the edge of the shade runs down the face
                // and comes in from the cheek turned from the light as the head turns, as it is drawn (and as the
                // anime games' face shadow maps have it); with the face to the camera only that cheek's rim is in it.
                float size = _HeadAt.w > 0 ? _HeadAt.w / 1.6 : 1.0, round = 0;
                if (_FaceRound > 0.5)
                {
                    float3 d = (i.world - _HeadAt.xyz) / max(_HeadAt.w, 1e-4) - _ChinUp.xyz * 0.035 - _ChinAhead.xyz * 0.01;
                    float3 ball = normalize(d - _ChinUp.xyz * dot(d, _ChinUp.xyz) * 0.7);
                    round = _FaceRound > 1.5 ? skin * (1.0 - smoothstep(0.08, 0.1, length(d))) : 1.0;
                    float3 faceLight = normalize(toLight - _ChinUp.xyz * dot(toLight, _ChinUp.xyz) * 0.6);
                    float turn = dot(ball, faceLight) + 0.14;
                    float edge = max(0.025, fwidth(turn) * 0.7);
                    lit = lerp(lit, smoothstep(-edge, edge, turn), round);
                }
                float slope = fwidth(i.screen.w);
                // (None on a generated model's face: its fringe is one lump with the brow, and its nose and the
                // unevenness of the surface threw ragged patches across the cheeks.)
                [branch] if (figure > 0 && _SeaFigureRim > 0 && _CastOwn > 0)
                    lit = min(lit, 1.0 - CastShade(i, n, overhead, size, slope, round) * _CastOwn * (_FaceRound > 1.5 ? 1.0 - round : 1.0));
                lit = lerp(lit, 1.0, _BackLit * step(facing, 0));
                // The head's shade on the neck (the body's skin of a VRoid figure; SeaFigure gives the head bone, at the
                // top of the neck, some 6 cm above the chin, and which way is up and ahead of it): the front of the neck
                // from under the jaw down to 9 cm below that bone, deepest under the chin and rising to the sides, with a
                // drawn edge. No light casts a shadow
                // on the figures, and without this one the head sat on the neck like a cut-out.
                if (_Chin > 0)
                {
                    float3 rel = (i.world - _ChinAt.xyz) / max(_ChinAt.w, 1e-4);
                    float up = dot(rel, _ChinUp.xyz), ahead = dot(rel, _ChinAhead.xyz);
                    float3 across = rel - _ChinUp.xyz * up - _ChinAhead.xyz * ahead;
                    float edge = -0.09 + 6.0 * dot(across, across);
                    // Only the neck: this skin is the hands' too, and held up before the face they are above that edge.
                    float neck = (1.0 - smoothstep(-0.005, 0.01, up)) * (1.0 - smoothstep(0.08, 0.11, ahead)) * (1.0 - smoothstep(0.06, 0.08, length(across)));
                    lit = min(lit, 1.0 - _Chin * neck * smoothstep(edge - 0.004, edge + 0.004, up) * smoothstep(-0.03, 0.005, ahead));
                }
                float3 shade = lerp(_ShadeColor.rgb, _SkinShade.rgb, skin);
                // A figure takes the colour of the zone it stands in: the seabed's light from below and the side, the
                // surface's from above, as a hue at the shade's own brightness (the seabed's light is dim, and as a
                // plain multiply it only darkened the shade), strongly in the shade and less on the lit side (the
                // figures are lit from the camera's side, so from the game's camera they are mostly lit), with a
                // little of the seabed's light thrown up under the lit side too. Skin keeps its own colours.
                float cloth = figure * (1.0 - skin * 0.85);
                float3 ground = _SeaGroundColor.rgb * 3.0 / max(dot(_SeaGroundColor.rgb, 1.0), 1e-3);
                float3 sky = _SeaSurfaceColor.rgb * 3.0 / max(dot(_SeaSurfaceColor.rgb, 1.0), 1e-3);
                float3 hue = lerp(ground, sky, saturate(n.y * 0.8 + 0.3));
                shade *= lerp(1.0, hue, 0.45 * cloth);
                float3 lift = lerp(1.0, hue, 0.2 * cloth) + _SeaGroundColor.rgb * saturate(0.2 - n.y) * 0.3 * cloth;
                // A figure's shade keeps more of its own colour than its lit side: a drawing's shadow colour is deeper
                // and purer, not the same paint with less light on it.
                float3 deep = lerp(albedo.rgb, saturate(lerp(dot(albedo.rgb, float3(0.3, 0.59, 0.11)), albedo.rgb, 1.0 + _ShadePure)), figure * (1.0 - skin));
                float3 col = lerp(shade * deep, _Color.rgb * lift * albedo.rgb, lit);
                // Skin is warmest where it turns from the light: a line of red along the shade's edge, as it is drawn.
                col *= 1.0 + float3(0.07, -0.03, -0.07) * (lit * (1.0 - lit) * 4.0) * skin * figure;
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
                // Hair takes a band of light round the head, where it turns from facing up to facing the eye, broken
                // into strands along the hair's own run (its texture's u) and only on its lit side: a flat black or
                // navy mass otherwise, with nothing to say it is hair.
                if (_HairGloss > 0)
                {
                    float turn = dot(n, normalize(view + float3(0, 1.25, 0)));
                    float band = smoothstep(0.72, 0.84, turn) * (1.0 - smoothstep(0.88, 0.97, turn));
                    // About one light strand to each of a VRoid model's hair strips (a strip is a quarter of u wide), and
                    // evened out where that is finer than the picture's pixels, or the band crawls as she moves.
                    float run = i.uv.x * 26.0 + 2.5 * sin(i.uv.y * 11.0);
                    float strands = lerp(0.8, saturate(0.7 + 0.4 * sin(run)), saturate(1.0 - fwidth(run) / 2.2));
                    col += _HairGlossColor.rgb * band * strands * lit * _HairGloss;
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
            float _Cutoff, _ZWrite, _Legs, _SwayAmp, _SwayAnchor, _SwayLength;
            float4 _LegTop0, _LegEnd0, _LegTop1, _LegEnd1;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float depth : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = SeaSway(mul(unity_ObjectToWorld, v.vertex).xyz, _SwayAmp, _SwayAnchor, _SwayLength);
                if (_Legs > 0.5) world = SeaOutOf(SeaOutOf(world, _LegTop0, _LegEnd0), _LegTop1, _LegEnd1);
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
