// The noise bodies: a skin of flickering blocks in two loud colours over black, vertices jumping a
// little now and then, an ink outline. _Alert pushes everything toward red. In voxel mode (_Voxels 1, on a
// Dungeon/SeaArt.Voxels mesh) each cube flickers on its own with bevelled seams, and now and then one
// jumps out of the body; uv2 holds the cube's centre and its id, negative for the cubes that stay put.
// A cube with a clear vertex colour keeps one colour instead of flickering: red × 3 picks _Dark, _ColorA,
// _ColorB or white (0 to 3; the 标题党型's title bar, close box, fangs and mouth).
// Each body runs on its own clock and seed (_Clock, _Seed, set by Dungeon/SeaActors.cs NoiseLook), so two of
// a kind never flicker in step. A hit flashes it (_Hit, in rgb, by a), knocks cubes loose (_Loose) and takes
// the coloured cells into the blow's colour (_Tint, giving them back cell by cell); knocked down it dims (_Dim);
// dying, its cubes fly apart (never at the camera), tumbling and shrinking, and land on the seabed (_Burst 0
// to 1, _BurstDir the way the blow went, _Floor); with 减少闪烁 (_Calm) the cells swap slower and closer in
// colour, with no white cubes and a softer flash. Frozen in ice (_Frost, set by Dungeon/BattleStage while its
// clock is held) the alert goes out of it and it greys toward the ice's blue, its vertices still.
// In card mode (_Card 1, 冷知识型's stack of index cards, each a thin box about its own centre) the faces are
// pale paper ruled in _ColorA under a red head line, the edges _ColorA, and only a few cells at a time rot into
// the glitch's colours (more when alerted), where the blocks over a flat face read as a chessboard.
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
        _Card ("Index card mode", Float) = 0
        _OutlineColor ("Outline", Color) = (0.02,0.02,0.04,1)
        _OutlineWidth ("Outline width", Float) = 0.03
        _Clock ("Clock", Float) = 0
        _Seed ("Seed", Float) = 0
        _Hit ("Hit flash (rgb, a amount)", Color) = (1.6,1.6,1.6,0)
        _Tint ("Tint (rgb, a weight)", Color) = (1,1,1,0)
        _Loose ("Loose", Float) = 0
        _Dim ("Dim", Float) = 1
        _Calm ("Reduce flashing", Float) = 0
        _Burst ("Burst", Float) = 0
        _BurstDir ("Burst direction (xyz, w strength)", Vector) = (0,0,0,0)
        _Floor ("Seabed height", Float) = 0
        _Frost ("Frozen", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGINCLUDE
        #include "SeaCommon.cginc"
        float4 _ColorA, _ColorB, _Dark, _OutlineColor, _Hit, _Tint, _BurstDir;
        float _Block, _Alert, _Jitter, _Voxels, _Card, _OutlineWidth, _Clock, _Seed, _Loose, _Dim, _Calm, _Burst, _Floor, _Frost;

        struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 voxel : TEXCOORD1; float4 color : COLOR; };

        // How fast it ticks: a quarter as fast with 减少闪烁.
        float Rate() { return 1.0 - 0.75 * _Calm; }

        // How alerted it shows: not at all frozen in ice.
        float Alert() { return _Alert * (1.0 - _Frost); }

        float3 Jump(float3 objPos)
        {
            float step8 = floor(_Clock * 9.0 * Rate());
            float band = floor(objPos.y * 6.0);
            float h = SeaHash(float2(band + _Seed * 7.3, step8));
            return float3((h - 0.5) * 2.0 * _Jitter * (1.0 - 0.5 * _Calm) * (1.0 - _Frost) * step(0.82, h), 0, 0);
        }

        // A cube popping out of the body for a beat, more often when alerted, and a third of them at once when
        // a blow knocks them loose.
        float3 Pop(float4 voxel)
        {
            float tick = floor(_Clock * (4.0 + Alert() * 6.0) * Rate() + voxel.w * 17.0);
            float h = SeaHash(float2(voxel.w * 53.1 + _Seed, tick * 0.71));
            float popped = step(0.9 - Alert() * 0.12 - _Loose * 0.6, h) * step(0.0001, voxel.w) * _Voxels;
            return normalize(voxel.xyz + float3(0, 0.0001, 0)) * popped * (0.14 + 0.25 * _Loose);
        }

        float3 Spin(float3 v, float3 axis, float a)
        {
            float s, c;
            sincos(a, s, c);
            return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
        }

        // The vertex where it is now, and its normal (turned with its cube when the body bursts).
        void Shape(appdata v, out float3 p, out float3 n)
        {
            p = v.vertex.xyz;
            n = v.normal;
            float b = _Burst * _Voxels;
            if (b > 0.0)
            {
                // Each cube flung out from the middle and the way the blow went, tumbling about its own axis,
                // shrinking, and falling (down in the world, whichever way the body lies).
                float3 c = v.voxel.xyz;
                float id = abs(v.voxel.w);
                float3 r = float3(SeaHash(float2(id * 91.7, _Seed)), SeaHash(float2(id * 37.3, _Seed + 3.1)), SeaHash(float2(id * 13.9, _Seed + 7.7))) - 0.5;
                float3 axis = normalize(r + float3(0.001, 0.002, 0.003));
                float a = (id - 0.5) * 12.0 * b;
                float3 down = normalize(mul((float3x3)unity_WorldToObject, float3(0, -1, 0)));
                float3 fly = normalize(c + float3(0, 0.0001, 0)) * (0.6 + id) + r * 0.8 - down * 1.2 + _BurstDir.xyz * _BurstDir.w;
                // Past the lens rather than into it, where a near cube would cover the picture.
                float3 lens = normalize(mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz - c);
                fly -= lens * max(0.0, dot(fly, lens)) * 0.85;
                p = c + Spin(p - c, axis, a) * (1.0 - b * b) + fly * (1.4 * b) + down * (3.2 * b * b);
                n = Spin(n, axis, a);
            }
            p += Pop(v.voxel) * (1.0 - b);
            p += Jump(p);
        }

        // Bursting cubes come to rest on the seabed rather than sinking through it.
        float3 Landed(float3 world)
        {
            world.y = lerp(world.y, max(world.y, _Floor + 0.03), step(0.0001, _Burst));
            return world;
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
                float3 p, n;
                Shape(v, p, n);
                float3 world = Landed(mul(unity_ObjectToWorld, float4(p, 1)).xyz);
                n = normalize(mul((float3x3)unity_ObjectToWorld, n));
                // Thinned as the bursting cubes shrink, or each would leave a dark speck.
                float b = _Burst * _Voxels;
                world += n * _OutlineWidth * (1.0 - b * b) * (0.6 + distance(world, _WorldSpaceCameraPos) * 0.04);
                o.pos = UnityWorldToClipPos(world);
                o.world = world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(SeaFog(_OutlineColor.rgb, i.world, SeaReach(0.7)), 1); }
            ENDCG
        }

        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float3 obj : TEXCOORD0; float3 world : TEXCOORD1; float3 normal : TEXCOORD2; float2 uv : TEXCOORD3; float id : TEXCOORD4; float role : TEXCOORD5; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 n;
                Shape(v, o.obj, n);
                o.world = Landed(mul(unity_ObjectToWorld, float4(o.obj, 1)).xyz);
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, n));
                o.pos = UnityWorldToClipPos(o.world);
                o.uv = v.uv;
                o.id = abs(v.voxel.w);
                // A cube with a colour of its own (SeaArt.Voxels' role, in a clear vertex colour): 0 to 3, else -1.
                o.role = _Voxels > 0.5 && v.color.a < 0.5 ? v.color.r * 3.0 : -1.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 cell = floor(i.obj / _Block);
                float2 key = _Voxels > 0.5 ? float2(i.id * 113.0 + _Seed * 1.37, 7.7) : cell.xy + cell.z * 7.1 + _Seed * 1.37;
                float2 offset = _Voxels > 0.5 ? key : cell.xy + cell.z + _Seed;
                float tick = floor(_Clock * 6.0 * Rate() + SeaHash(offset) * 6.0);
                float h = SeaHash(key + tick * 0.37);
                float3 col = h < 0.42 ? _Dark.rgb : (h < 0.72 ? _ColorA.rgb : _ColorB.rgb);
                float coloured = step(0.42, h);
                if (i.role > -0.5)
                {
                    // The 标题党型's title bar, close box, fangs and the dark of its mouth hold still in colour.
                    col = i.role < 0.5 ? _Dark.rgb : (i.role < 1.5 ? _ColorA.rgb : (i.role < 2.5 ? _ColorB.rgb : float3(0.94, 0.95, 1.0)));
                    coloured = step(0.5, i.role);
                }
                col = lerp(col, (_ColorA.rgb + _ColorB.rgb + _Dark.rgb) / 3.0, 0.35 * _Calm);
                col = lerp(col, float3(1, 0.12, 0.15), Alert() * coloured * 0.7);
                if (_Card > 0.5)
                {
                    float rot = step(0.88 - Alert() * 0.25, h);
                    float3 paper = lerp(_ColorB.rgb, float3(1, 1, 1), 0.4) * 0.86;
                    float ruled = step(frac((i.obj.y + 0.4) / 0.1), 0.09);
                    paper = lerp(paper, _ColorA.rgb * 0.8, ruled * 0.75);
                    paper = lerp(paper, float3(0.95, 0.33, 0.38), step(abs(i.obj.y - 0.25), 0.012));
                    paper = lerp(paper, _ColorA.rgb * 0.7, step(abs(i.obj.z), 0.03)); // the edges
                    col = lerp(paper, h < 0.94 ? _Dark.rgb : _ColorA.rgb, rot);
                    col = lerp(col, float3(1, 0.12, 0.15), Alert() * (1.0 - rot) * 0.25);
                    coloured = max(rot, 0.5);
                }
                // Each coloured cell in the blow's colour or out of it, given back one by one as _Tint.a falls.
                col = lerp(col, _Tint.rgb * (0.55 + 0.6 * h), step(SeaHash(key + 5.3), _Tint.a) * coloured);
                if (_Voxels > 0.5)
                {
                    // Bevelled cubes: dark seams round each face, a lit corner, now and then a white flash.
                    float2 e = min(i.uv, 1.0 - i.uv);
                    col *= lerp(0.4, 1.0, smoothstep(0.03, 0.14, min(e.x, e.y)));
                    col += coloured * smoothstep(0.3, 0.0, length(i.uv - float2(0.22, 0.78))) * 0.25;
                    col = lerp(col, float3(1, 1, 1), step(0.975, h) * 0.85 * (1.0 - _Calm));
                }
                col += (col * 0.6 + 0.1) * SeaFlash(i.world, normalize(i.normal));
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float rim = pow(1.0 - saturate(dot(normalize(i.normal), view)), 3.0);
                col += lerp(lerp(_ColorB.rgb, float3(1, 0.2, 0.2), Alert()), float3(0.8, 0.94, 1), _Frost) * smoothstep(0.4, 0.6, rim) * 0.6;
                col *= _Dim * lerp(1.0, 0.45, saturate((_Burst - 0.25) / 0.75));
                col = lerp(col, _Hit.rgb, _Hit.a * (1.0 - 0.75 * _Calm));
                float grey = dot(col, float3(0.3, 0.59, 0.11));
                col = lerp(col, grey * float3(0.5, 0.75, 0.95) + float3(0.08, 0.14, 0.2), _Frost * 0.8);
                // Its loud colours carry through the zone's fog further than the scenery (SeaReach), so one patrolling
                // 25-30 m off is still seen coming.
                return fixed4(SeaFog(col, i.world, SeaReach(0.7)), 1);
            }
            ENDCG
        }

        // Its depth from above for its shadow on the seabed (Dungeon/SeaShade.cs alone; the tag keeps it out of the
        // picture, as Toon.shader's): the same shape, popping cubes, burst and landing as it is drawn with.
        Pass
        {
            Name "SEADEPTH"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float depth : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 p, n;
                Shape(v, p, n);
                float3 world = Landed(mul(unity_ObjectToWorld, float4(p, 1)).xyz);
                o.pos = UnityWorldToClipPos(world);
                o.depth = -mul(UNITY_MATRIX_V, float4(world, 1)).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target { return i.depth; }
            ENDCG
        }
    }
}
