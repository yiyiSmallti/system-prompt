using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // Everything the latent space is drawn with, made in code: the meshes (only the characters are models,
    // see SeaFigure.cs), the shape textures and the materials on the Resources/Shaders shaders.
    public static class SeaArt
    {
        // ------------------------------------------------------------------ materials

        static Shader Load(string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + name);
            if (s == null) Debug.LogError($"找不到着色器 Resources/Shaders/{name}");
            return s;
        }

        public static Material Toon(Color lit, Color shade, Color rim, Color emission = default, float outline = 0.025f)
        {
            var m = new Material(Load("Toon"));
            m.SetColor("_Color", lit);
            m.SetColor("_ShadeColor", shade);
            m.SetColor("_RimColor", rim);
            m.SetColor("_Emission", emission);
            m.SetFloat("_OutlineWidth", outline);
            return m;
        }

        public static Material Glow(Texture texture, Color color)
        {
            var m = new Material(Load("Glow"));
            m.mainTexture = texture;
            m.SetColor("_Color", color);
            return m;
        }

        public static Material Glitch(Color a, Color b, float block = 0.22f)
        {
            var m = new Material(Load("Glitch"));
            m.SetColor("_ColorA", a);
            m.SetColor("_ColorB", b);
            m.SetFloat("_Block", block);
            return m;
        }

        // See-through light with a bright edge and scan lines, the texture showing faintly inside: the 集成's whale.
        public static Material Holo(Color color, Texture texture)
        {
            var m = new Material(Load("Holo"));
            m.SetColor("_Color", color);
            m.mainTexture = texture;
            return m;
        }

        public static Material Plain(string shader) => new Material(Load(shader));

        // A shaft of light (Shaders/Shaft.shader) for a Ribbon: glow round a core of the core colour, coreWidth
        // of the half width across, streaks running flow metres a second along it; it fades toward the far end
        // by the power fade and in over the first baseFade metres.
        public static Material Shaft(Color glow, Color core, float coreWidth = 0.15f, float flow = 3f, float streaks = 0.6f, float fade = 0f, float baseFade = 0f)
        {
            var m = new Material(Load("Shaft"));
            m.SetColor("_Color", glow);
            m.SetColor("_CoreColor", core);
            m.SetFloat("_Core", coreWidth);
            m.SetFloat("_Flow", flow);
            m.SetFloat("_Streaks", streaks);
            m.SetFloat("_Fade", fade);
            m.SetFloat("_Base", baseFade);
            return m;
        }

        // Drifting billboards from the atlas (tiles firstTile..lastTile) in a box above parent: the plankton
        // and the lost things of the sea.
        public static void Particles(Transform parent, string name, int firstTile, int lastTile, int count, Vector3 box, float lifetime,
            float minSize, float maxSize, Color color, float rise, float height = 4f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, height, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = lifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = count / lifetime;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            vel.y = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(firstTile / 4f, (lastTile + 0.99f) / 4f);
            sheet.cycleCount = 1;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SeaArt.Glow(SeaArt.Atlas, Color.white);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        // ------------------------------------------------------------------ text

        const int TextSize = 64;

        static Font font;
        static Material text;

        // The first TextMesh made takes half a second, Unity starting its old text system up then; GameRoot puts
        // some words up at start-up, before the first frame, so that a dive never stops for it.
        public static void WarmText() => UnityEngine.Object.Destroy(Text("Warm", null, "缓存", 1f, Color.clear).gameObject);

        static Font TextFace
        {
            get
            {
                if (font != null) return font;
                font = Resources.Load<Font>("Fonts/NotoSansSC-Black");
                if (font == null) Debug.LogError("找不到字体 Resources/Fonts/NotoSansSC-Black");
                Font.textureRebuilt += f =>
                {
                    if (f == font && text != null) text.mainTexture = font.material.mainTexture;
                };
                return font;
            }
        }

        // Words in the sea (graves, signs, chat logs): additive like Glow, from the UI's Chinese font. One
        // material for all of it, its glyph texture swapped in whenever the font's dynamic atlas grows.
        public static TextMesh Text(string name, Transform parent, string words, float height, Color color, Vector3 local = default)
        {
            // Kept for the whole run: Parts.Free leaves it to the next words.
            if (text == null) text = new Material(Load("Text")) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = TextFace;
            mesh.fontSize = TextSize;
            mesh.characterSize = height * 10f / TextSize; // a line is fontSize / 10 units tall at size 1
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            mesh.text = words;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = text;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            text.mainTexture = font.material.mainTexture;
            return mesh;
        }

        // ------------------------------------------------------------------ textures

        public const int AtlasBubble = 0, AtlasEmoji = 1, AtlasCard = 2, AtlasDot = 3;

        static Texture2D atlas, beam, white, firewall, ring, flare, cells, socket, sheen, dial;

        // Four 128px cells: a chat bubble, an emoji face, a web card and a soft dot, white on clear.
        public static Texture2D Atlas => atlas ??= MakeAtlas();
        public static Texture2D Beam => beam ??= MakeBeam();
        public static Texture2D White => white ??= Solid();
        public static Texture2D Firewall => firewall ??= MakeFirewall();
        public static Texture2D Ring => ring ??= MakeRing();
        public static Texture2D Flare => flare ??= MakeFlare();
        public static Texture2D Cells => cells ??= MakeCells();
        public static Texture2D SocketMark => socket ??= MakeSocket();
        public static Texture2D Sheen => sheen ??= MakeSheen();
        public static Texture2D Dial => dial ??= MakeDial();

        // The atlas rectangle of one cell, for a material's scale and offset.
        public static Vector4 AtlasCell(int i) => new Vector4(0.25f, 1f, 0.25f * i, 0f);

        static Texture2D New(int w, int h, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = wrap, filterMode = FilterMode.Bilinear, name = "Sea" };
        }

        static Texture2D Solid()
        {
            var t = New(4, 4);
            var px = new Color32[16];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static float Box(Vector2 p, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static float Fill(float d) => Mathf.Clamp01(0.5f - d / 1.5f);

        static Texture2D MakeAtlas()
        {
            const int S = 128;
            var t = New(S * 4, S);
            var px = new Color32[S * 4 * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S * 4; x++)
            {
                var cell = x / S;
                var p = new Vector2(x % S - S / 2f + 0.5f, y - S / 2f + 0.5f); // y up
                float a;
                switch (cell)
                {
                    case AtlasBubble:
                    {
                        var body = Box(p - new Vector2(0, 8), new Vector2(52, 34), 14);
                        var tail = Box(p - new Vector2(-30, -30), new Vector2(9, 9), 3);
                        a = Fill(Mathf.Min(body, tail)) * 0.85f;
                        // Two lines of "text" knocked out of the bubble.
                        var line1 = Box(p - new Vector2(-6, 18), new Vector2(34, 4), 4);
                        var line2 = Box(p - new Vector2(-16, 2), new Vector2(24, 4), 4);
                        a *= 1f - Fill(Mathf.Min(line1, line2)) * 0.75f;
                        break;
                    }
                    case AtlasEmoji:
                    {
                        var face = p.magnitude - 46;
                        a = Fill(face) * 0.8f;
                        var eyes = Mathf.Min((p - new Vector2(-16, 12)).magnitude - 6, (p - new Vector2(16, 12)).magnitude - 6);
                        var smile = Mathf.Max(Mathf.Abs((p - new Vector2(0, 2)).magnitude - 24) - 4, p.y + 4);
                        a *= 1f - Fill(Mathf.Min(eyes, smile)) * 0.85f;
                        a = Mathf.Max(a, Fill(Mathf.Abs(face) - 2.5f));
                        break;
                    }
                    case AtlasCard:
                    {
                        var card = Box(p, new Vector2(44, 56), 6);
                        a = Fill(card) * 0.55f;
                        var picture = Box(p - new Vector2(0, 22), new Vector2(36, 24), 3);
                        var lines = Mathf.Min(Box(p - new Vector2(-4, -18), new Vector2(32, 3.5f), 3), Box(p - new Vector2(-12, -32), new Vector2(24, 3.5f), 3));
                        a = Mathf.Max(a, Fill(picture) * 0.9f);
                        a = Mathf.Max(a, Fill(lines) * 0.95f);
                        a = Mathf.Max(a, Fill(Mathf.Abs(card) - 2f));
                        break;
                    }
                    default:
                    {
                        var r = p.magnitude / 60f;
                        a = Mathf.Clamp01(1f - r);
                        a *= a;
                        break;
                    }
                }
                px[y * S * 4 + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // Around the beam (u) a few brighter streaks; along it (v from 0 at the lamp) it fades out.
        static Texture2D MakeBeam()
        {
            const int W = 64, H = 64;
            var t = New(W, H, TextureWrapMode.Repeat);
            var px = new Color32[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var v = (float)y / (H - 1);
                var u = (float)x / W;
                var streak = 0.55f + 0.45f * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * Mathf.PI * 5f + Mathf.Sin(u * 19f))), 3f);
                var a = Mathf.Pow(1f - v, 1.6f) * streak;
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // A wall of data blocks for the firewall, tiled: bright block edges, a few blocks lit up.
        static Texture2D MakeFirewall()
        {
            const int S = 128, Blocks = 8;
            var t = New(S, S, TextureWrapMode.Repeat);
            var px = new Color32[S * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var bx = x * Blocks / S;
                var by = y * Blocks / S;
                var fx = (x * Blocks % S) / (float)S;
                var fy = (y * Blocks % S) / (float)S;
                var edge = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(fy, 1f - fy));
                var line = Mathf.Clamp01(1f - edge / 0.08f);
                var lit = (bx * 7 + by * 13) % 5 == 0 ? 0.45f : 0.12f;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(line, lit)) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // A thin bright ring with a soft inside, for the beacons' and fragments' ground marks.
        static Texture2D MakeRing()
        {
            const int S = 128;
            var t = New(S, S);
            var px = new Color32[S * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var r = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f).magnitude / (S / 2f);
                var ringA = Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.06f);
                var inner = Mathf.Clamp01(1f - r) * 0.35f;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ringA, inner)) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // A soft dot with a four-pointed star of thin rays through it, and fainter diagonal ones: where light
        // strikes something.
        static Texture2D MakeFlare()
        {
            const int S = 128;
            var t = New(S, S);
            var px = new Color32[S * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var p = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f) / (S / 2f);
                var fall = Mathf.Clamp01(1f - p.magnitude);
                var dot = Mathf.Pow(fall, 3f);
                var cross = Mathf.Exp(-Mathf.Abs(p.x) * 45f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(p.y)), 2f)
                    + Mathf.Exp(-Mathf.Abs(p.y) * 45f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(p.x)), 2f);
                var diagonal = (Mathf.Exp(-Mathf.Abs(p.x - p.y) * 30f) + Mathf.Exp(-Mathf.Abs(p.x + p.y) * 30f)) * Mathf.Pow(fall, 2.5f);
                var a = dot + 0.9f * cross + 0.35f * diagonal;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // A cache block's face: a frame round four by four memory cells, a few of them full.
        static Texture2D MakeCells()
        {
            const int S = 128, N = 4;
            var t = New(S, S);
            var px = new Color32[S * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var p = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f);
                var frame = Fill(Mathf.Abs(Box(p, new Vector2(60, 60), 6)) - 1.5f);
                const float pitch = 104f / N;
                int cx = Mathf.Clamp(Mathf.FloorToInt((p.x + 52f) / pitch), 0, N - 1), cy = Mathf.Clamp(Mathf.FloorToInt((p.y + 52f) / pitch), 0, N - 1);
                var centre = new Vector2(-52f + pitch * (cx + 0.5f), -52f + pitch * (cy + 0.5f));
                var cell = Box(p - centre, new Vector2(pitch / 2f - 3f, pitch / 2f - 3f), 3f);
                var full = (cx * 5 + cy * 3) % 7 < 2;
                var a = Mathf.Max(frame, Fill(Mathf.Abs(cell) - 0.8f) * 0.8f);
                a = Mathf.Max(a, Fill(cell) * (full ? 0.85f : 0.12f));
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // A socket's mark on the seabed: an outline with heavier corners, a chevron in from each side pointing
        // at the middle, and a faint fill.
        static Texture2D MakeSocket()
        {
            const int S = 128;
            var t = New(S, S);
            var px = new Color32[S * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var p = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f);
                var edge = Box(p, new Vector2(58, 58), 4);
                var a = Fill(Mathf.Abs(edge) - 1.2f) * 0.7f + Fill(edge) * 0.12f;
                // Corners: the outline thickened for 18 px either way from each corner.
                var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
                if ((q.x > 40f || q.y > 40f) && q.x > 50f && q.y > 50f || (q.x > 52f && q.y > 40f) || (q.y > 52f && q.x > 40f))
                    a = Mathf.Max(a, Fill(Mathf.Abs(edge + 3f) - 3f));
                // A chevron on each side: |along| + depth in from the edge.
                for (var k = 0; k < 4; k++)
                {
                    var r = k == 0 ? p : k == 1 ? new Vector2(-p.y, p.x) : k == 2 ? -p : new Vector2(p.y, -p.x);
                    var v = Mathf.Abs(Mathf.Abs(r.x) * 0.9f + (r.y - 44f)) - 3f;
                    if (Mathf.Abs(r.x) < 12f && r.y < 48f && r.y > 28f) a = Mathf.Max(a, Fill(v));
                }
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // A mirror's sheen, to scroll across it: brighter toward the top, with a broad and a thin glint slanting
        // across; repeats along u.
        static Texture2D MakeSheen()
        {
            const int W = 128, H = 64;
            var t = New(W, H, TextureWrapMode.Repeat);
            var px = new Color32[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                float u = (float)x / W, v = (float)y / (H - 1);
                var slant = Mathf.Repeat(u + v * 0.35f, 1f);
                var broad = Mathf.Exp(-Mathf.Pow((slant - 0.3f) / 0.06f, 2f));
                var thin = Mathf.Exp(-Mathf.Pow((slant - 0.42f) / 0.015f, 2f));
                var a = 0.18f + 0.22f * v + 0.55f * broad + 0.7f * thin;
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // Round a mirror's pedestal: a ring with a tick every eighth of a turn (the four square ones longer)
        // and arrowheads between them, all pointing clockwise seen from above, the way it turns.
        static Texture2D MakeDial()
        {
            const int S = 256;
            var t = New(S, S);
            var px = new Color32[S * S];
            for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var p = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f);
                var r = p.magnitude / (S / 2f);
                var ang = Mathf.Atan2(p.y, p.x);
                var ringA = Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.012f);
                // Ticks: distance to the nearest eighth, in pixels along the ring.
                var eighth = Mathf.PI / 4f;
                var k = Mathf.Round(ang / eighth);
                var off = Mathf.Abs(ang - k * eighth) * r * S / 2f;
                var square = ((int)k & 1) == 0;
                var tick = r > (square ? 0.74f : 0.8f) && r < 0.97f ? Mathf.Clamp01(1.6f - off) : 0f;
                // Arrowheads halfway between ticks, on the ring, pointing clockwise (angle falling): a chevron.
                var mid = Mathf.Floor(ang / eighth) * eighth + eighth / 2f;
                var along = (ang - mid) * r * S / 2f;    // px along the ring, + anticlockwise
                var across = (r - 0.9f) * S / 2f;        // px out from the ring
                var arrow = along > -6f && along < 4f ? Mathf.Clamp01(1.4f - Mathf.Abs(Mathf.Abs(across) * 0.9f - (along + 6f) * 0.9f)) : 0f;
                var a = Mathf.Max(ringA * 0.8f, Mathf.Max(tick, arrow * 0.9f));
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ meshes

        // What a Shaft is drawn on: a strip width metres across, along y from 0 to 1 (scale y to the length).
        // The shader turns it about y to the camera, so its bounds take in the width every way round.
        public static Mesh Ribbon(float width, int rows = 12)
        {
            var b = new Builder();
            for (var i = 0; i <= rows; i++)
            {
                var y = (float)i / rows;
                b.Add(new Vector3(-width / 2f, y, 0), Vector3.back, new Vector2(-1f, y));
                b.Add(new Vector3(width / 2f, y, 0), Vector3.back, new Vector2(1f, y));
            }
            for (var i = 0; i < rows; i++)
            {
                var k = i * 2;
                b.T.Add(k); b.T.Add(k + 2); b.T.Add(k + 3);
                b.T.Add(k); b.T.Add(k + 3); b.T.Add(k + 1);
            }
            var mesh = b.Build("Ribbon");
            mesh.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(width, 1f, width));
            return mesh;
        }

        // A closed cylinder from radius r0 at the bottom to r1 at the top, y from 0 to height, with flat sides:
        // a few sides make a prism.
        public static Mesh Cylinder(float r0, float r1, float height, int sides = 16)
        {
            var b = new Builder();
            var top = Vector3.up * height;
            var slope = (r0 - r1) / height;
            for (var j = 0; j < sides; j++)
            {
                float a0 = 2f * Mathf.PI * j / sides, a1 = 2f * Mathf.PI * (j + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var n = ((d0 + d1).normalized + Vector3.up * slope).normalized;
                b.Quad(b.Add(d0 * r0, n, new Vector2(0, 0)), b.Add(d1 * r0, n, new Vector2(1, 0)),
                    b.Add(d1 * r1 + top, n, new Vector2(1, 1)), b.Add(d0 * r1 + top, n, new Vector2(0, 1)), n);
                b.Tri(b.Add(top, Vector3.up, Vector2.zero), b.Add(d0 * r1 + top, Vector3.up, Vector2.zero), b.Add(d1 * r1 + top, Vector3.up, Vector2.zero), Vector3.up);
                b.Tri(b.Add(Vector3.zero, Vector3.down, Vector2.zero), b.Add(d0 * r0, Vector3.down, Vector2.zero), b.Add(d1 * r0, Vector3.down, Vector2.zero), Vector3.down);
            }
            return b.Build("Cylinder");
        }

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Vector4> UV2 = new List<Vector4>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public int Add(Vector3 v, Vector3 n, Vector2 uv, Vector4 uv2 = default, Color? c = null)
            {
                V.Add(v);
                N.Add(n);
                UV.Add(uv);
                UV2.Add(uv2);
                C.Add(c ?? Color.white);
                return V.Count - 1;
            }

            // Two triangles facing n, whatever order the corners come in around the quad.
            public void Quad(int a, int b, int c, int d, Vector3 n)
            {
                // The diagonals' cross product still points the right way when two corners meet (a pole).
                if (Vector3.Dot(Vector3.Cross(V[c] - V[a], V[d] - V[b]), n) < 0f) (b, d) = (d, b);
                T.Add(a); T.Add(b); T.Add(c);
                T.Add(a); T.Add(c); T.Add(d);
            }

            public void Tri(int a, int b, int c, Vector3 n)
            {
                if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), n) < 0f) (b, c) = (c, b);
                T.Add(a); T.Add(b); T.Add(c);
            }

            // One face of a box: corner o, edges u and v, facing n, uv from uv0 over (uSpan, vSpan).
            public void Face(Vector3 o, Vector3 u, Vector3 v, Vector3 n, Vector4 uv2, Color c, Vector2 uv0 = default, float uSpan = 1f, float vSpan = 1f)
            {
                var a = Add(o, n, uv0, uv2, c);
                var b = Add(o + u, n, uv0 + new Vector2(uSpan, 0f), uv2, c);
                var d = Add(o + u + v, n, uv0 + new Vector2(uSpan, vSpan), uv2, c);
                var e = Add(o + v, n, uv0 + new Vector2(0f, vSpan), uv2, c);
                Quad(a, b, d, e, n);
            }

            // A box of half size h round centre with every edge cut back `bevel` at 45°: six faces, a strip along
            // each edge and a facet at each corner, all flat, so the edges catch the light and the outline the way a
            // made thing's do. Each face has uv 0..1 over it; the strips and facets uv (0.5, 0), the seam.
            public void Chamfered(Vector3 centre, Vector3 h, float bevel, Vector4 uv2, Color c)
            {
                bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
                var seam = new Vector2(0.5f, 0f);
                // Corner s's point on the face across `axis`.
                Vector3 At(int axis, Vector3 s)
                {
                    var p = new Vector3(s.x * (h.x - bevel), s.y * (h.y - bevel), s.z * (h.z - bevel));
                    p[axis] = s[axis] * h[axis];
                    return centre + p;
                }
                for (var axis = 0; axis < 3; axis++)
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var n = Vector3.zero;
                    n[axis] = sign;
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    var q = new int[4];
                    for (var k = 0; k < 4; k++)
                    {
                        var s = n;
                        float su = k == 0 || k == 3 ? -1f : 1f, sv = k < 2 ? -1f : 1f;
                        s[u] = su;
                        s[v] = sv;
                        q[k] = Add(At(axis, s), n, new Vector2((su + 1f) / 2f, (sv + 1f) / 2f), uv2, c);
                    }
                    Quad(q[0], q[1], q[2], q[3], n);
                }
                if (bevel <= 0f) return;
                for (var w = 0; w < 3; w++)
                {
                    int a = (w + 1) % 3, b = (w + 2) % 3;
                    for (var sa = -1; sa <= 1; sa += 2)
                    for (var sb = -1; sb <= 1; sb += 2)
                    {
                        var n = Vector3.zero;
                        n[a] = sa;
                        n[b] = sb;
                        n.Normalize();
                        var q = new int[4];
                        for (var k = 0; k < 2; k++)
                        {
                            var s = Vector3.zero;
                            s[a] = sa;
                            s[b] = sb;
                            s[w] = k * 2 - 1;
                            q[k * 2] = Add(At(a, s), n, seam, uv2, c);
                            q[k * 2 + 1] = Add(At(b, s), n, seam, uv2, c);
                        }
                        Quad(q[0], q[1], q[3], q[2], n);
                    }
                }
                for (var i = 0; i < 8; i++)
                {
                    var s = new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1);
                    var n = s.normalized;
                    Tri(Add(At(0, s), n, seam, uv2, c), Add(At(1, s), n, seam, uv2, c), Add(At(2, s), n, seam, uv2, c), n);
                }
            }

            // A box from corner o along three edges at right angles, without its bottom (-up): litter on the seabed.
            public void Block(Vector3 o, Vector3 x, Vector3 up, Vector3 z, Color c)
            {
                Face(o + x, z, up, x.normalized, default, c);
                Face(o, z, up, -x.normalized, default, c);
                Face(o + z, x, up, z.normalized, default, c);
                Face(o, x, up, -z.normalized, default, c);
                Face(o + up, x, z, up.normalized, default, c);
            }

            // A piece of rack trim standing out of a wall face: from corner o, `along` the face, `up` it and
            // `outward`. The front gets uv in metres (along from uvStart, up from the bottom) and `front`, the
            // other faces `rest`; the back against the wall is left out, and the bottom unless asked for.
            public void Trim(Vector3 o, Vector3 along, Vector3 up, Vector3 outward, Vector4 front, Vector4 rest, Color c,
                float uvStart = 0f, bool bottom = false)
            {
                var n = outward.normalized;
                var a = along.normalized;
                Face(o + outward, along, up, n, front, c, new Vector2(uvStart, 0f), along.magnitude, up.magnitude);
                Face(o, outward, up, -a, rest, c);
                Face(o + along, outward, up, a, rest, c);
                Face(o + up, along, outward, Vector3.up, rest, c);
                if (bottom) Face(o, along, outward, Vector3.down, rest, c);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, UV);
                m.SetUVs(1, UV2);
                m.SetColors(C);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        // The racks' frame, in metres: a post at the start of every rack column (as wide as Rack.shader draws
        // them), the cap overhanging the top and the plinth along the foot.
        const float RackColumn = 4f / 3f, PostWidth = 0.1f, PostDepth = 0.07f;
        const float CapHeight = 0.16f, CapDepth = 0.11f, CapRise = 0.02f, PlinthHeight = 0.18f, PlinthDepth = 0.05f;
        static readonly Vector2Int[] Sides = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        // The rack walls: every block's sides where they show above a lower neighbour, with their frame, and
        // its top. The vertex colour tints the LEDs and the top edge by zone (alpha: how much).
        public static Mesh Walls(Dictionary<Vector2Int, float> blocks, System.Func<Vector2Int, Vector3> centre, float cell,
            System.Func<Vector2Int, Color> tint)
        {
            var b = new Builder();
            var half = cell / 2f;
            foreach (var kv in blocks)
            {
                var c = kv.Key;
                var h = kv.Value;
                var o = centre(c);
                var tc = tint(c);
                var seed = (c.x * 73 + c.y * 151) % 97 / 97f;
                foreach (var d in Sides)
                {
                    var below = blocks.TryGetValue(c + d, out var nh) ? nh : 0f;
                    if (below >= h) continue;
                    var n = MapDir(d);
                    var side = new Vector3(n.z, 0f, -n.x); // along the face; uv.x counts metres from its start p0
                    var p0 = o + n * half - side * half;
                    b.Face(p0 + Vector3.up * below, side * cell, Vector3.up * (h - below), n, new Vector4(seed, h, 0f, 0f), tc,
                        new Vector2(0f, below), cell, h - below);
                    // The frame wraps the corner where the face starts at an outside corner.
                    var corner = !blocks.ContainsKey(c + new Vector2Int(d.y, -d.x));
                    RackFrame(b, p0, side, n, cell, below, h, seed, tc, corner);
                }
                var top = new Vector4(seed, h, 1f, 0f);
                var t0 = b.Add(o + new Vector3(-half, h, -half), Vector3.up, new Vector2(o.x - half, o.z - half), top, tc);
                var t1 = b.Add(o + new Vector3(-half, h, half), Vector3.up, new Vector2(o.x - half, o.z + half), top, tc);
                var t2 = b.Add(o + new Vector3(half, h, half), Vector3.up, new Vector2(o.x + half, o.z + half), top, tc);
                var t3 = b.Add(o + new Vector3(half, h, -half), Vector3.up, new Vector2(o.x + half, o.z - half), top, tc);
                b.Quad(t0, t1, t2, t3, Vector3.up);
            }
            return b.Build("Rack Walls");
        }

        // One face's frame. uv2.w tells Rack.shader the part: 1 post, 2 cap, 3 plinth, + 0.5 off the front.
        static void RackFrame(Builder b, Vector3 p0, Vector3 side, Vector3 n, float length, float below, float h, float seed, Color tc, bool corner)
        {
            var up = Vector3.up;
            Vector4 Part(float part) => new Vector4(seed, h, 0f, part);
            var plinthTop = below + PlinthHeight;
            var capBottom = h - CapHeight;
            var wrap = corner ? 1f : 0f;
            b.Trim(p0 - side * (PlinthDepth * wrap) + up * below, side * (length + PlinthDepth * wrap), up * PlinthHeight, n * PlinthDepth,
                Part(3f), Part(3.5f), tc);
            b.Trim(p0 - side * (CapDepth * wrap) + up * capBottom, side * (length + CapDepth * wrap), up * (CapHeight + CapRise), n * CapDepth,
                Part(2f), Part(2.5f), tc, 0f, true);
            if (capBottom - plinthTop < 0.2f) return;
            for (var s = 0f; s < length - 0.01f; s += RackColumn)
            {
                var start = s == 0f ? -PostDepth * wrap : s;
                b.Trim(p0 + side * start + up * plinthTop, side * (s + PostWidth - start), up * (capBottom - plinthTop), n * PostDepth,
                    Part(1f), Part(1.5f), tc, start - s);
            }
        }

        // What lies about at the foot of the racks where they face open seabed (`open`): bits of broken
        // hardware, a few glowing data shards, and cables coming out of the plinth, along the seabed and back
        // in. Vertex colour for the Scatter shader: rgb the colour, alpha how much of it glows.
        public static Mesh Scatter(Dictionary<Vector2Int, float> blocks, System.Func<Vector2Int, Vector3> centre, float cell,
            System.Func<Vector2Int, Color> tint, System.Func<Vector2Int, bool> open)
        {
            var b = new Builder();
            var half = cell / 2f;
            var metal = new Color(0.12f, 0.15f, 0.22f, 0f);
            var rubber = new Color(0.04f, 0.05f, 0.075f, 0f);
            var ledDefault = new Color(0.25f, 0.95f, 0.85f);
            foreach (var kv in blocks)
            {
                var c = kv.Key;
                var o = centre(c);
                var tc = tint(c);
                var led = Color.Lerp(ledDefault, tc, tc.a);
                led.a = 1f;
                foreach (var d in Sides)
                {
                    if (!open(c + d)) continue;
                    var n = MapDir(d);
                    var side = new Vector3(n.z, 0f, -n.x);
                    var p0 = o + n * half - side * half;
                    var r = new System.Random(c.x * 7919 + c.y * 104729 + d.x * 31 + d.y * 17 + 5);
                    float R() => (float)r.NextDouble();
                    var pieces = r.Next(0, 4);
                    for (var k = 0; k < pieces; k++)
                    {
                        var at = p0 + side * (0.3f + R() * (cell - 0.6f)) + n * (PlinthDepth + 0.08f + R() * 0.4f);
                        var turn = Quaternion.Euler(0f, R() * 90f, 0f);
                        if (R() < 0.3f)
                        {
                            // A shard of data stuck in the silt, leaning.
                            var lean = turn * Quaternion.Euler(R() * 30f - 15f, 0f, R() * 30f - 15f);
                            var w = 0.03f + R() * 0.03f;
                            var tall = 0.12f + R() * 0.22f;
                            b.Block(at - lean * new Vector3(w / 2f, 0f, w / 2f), lean * Vector3.right * w, lean * Vector3.up * tall, lean * Vector3.forward * w, led);
                        }
                        else
                        {
                            var size = new Vector3(0.08f + R() * 0.2f, 0.04f + R() * 0.12f, 0.06f + R() * 0.16f);
                            var shade = metal * (0.7f + R() * 0.6f);
                            shade.a = 0f;
                            b.Block(at - turn * new Vector3(size.x / 2f, 0f, size.z / 2f), turn * Vector3.right * size.x, Vector3.up * size.y, turn * Vector3.forward * size.z, shade);
                        }
                    }
                    if (R() < 0.45f)
                    {
                        var s0 = 0.2f + R() * (cell * 0.5f);
                        var s1 = Mathf.Min(cell - 0.2f, s0 + 0.8f + R() * (cell * 0.6f));
                        var glowing = R() < 0.3f;
                        var colour = glowing ? new Color(led.r * 0.8f, led.g * 0.8f, led.b * 0.8f, 0.7f) : rubber;
                        Cable(b, p0, side, n, s0, s1, 0.025f + R() * 0.015f, 0.18f + R() * 0.25f, R() * 6f, colour);
                    }
                }
            }
            return b.Build("Rack Litter");
        }

        // A cable out of the plinth at s0 along the face, sagging onto the seabed `reach` out from it and back
        // into the plinth at s1: a thin tube with a little wander.
        static void Cable(Builder b, Vector3 p0, Vector3 side, Vector3 n, float s0, float s1, float radius, float reach, float phase, Color colour)
        {
            const int Segments = 16, Around = 6;
            var points = new Vector3[Segments + 1];
            for (var i = 0; i <= Segments; i++)
            {
                var t = (float)i / Segments;
                var arc = Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)); // sin(π) comes out just below 0
                var outward = PlinthDepth * 0.5f + reach * Mathf.Sqrt(arc) + Mathf.Sin(t * 9f + phase) * 0.04f * arc;
                var y = radius + 0.1f * Mathf.Pow(1f - arc, 4f);
                points[i] = p0 + side * Mathf.Lerp(s0, s1, t) + n * outward + Vector3.up * y;
            }
            var start = b.V.Count;
            for (var i = 0; i <= Segments; i++)
            {
                var ahead = (points[Mathf.Min(i + 1, Segments)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var right = Vector3.Cross(Vector3.up, ahead).normalized;
                var upward = Vector3.Cross(ahead, right);
                for (var j = 0; j < Around; j++)
                {
                    var a = 2f * Mathf.PI * j / Around;
                    var dir = right * Mathf.Cos(a) + upward * Mathf.Sin(a);
                    b.Add(points[i] + dir * radius, dir, Vector2.zero, default, colour);
                }
            }
            for (var i = 0; i < Segments; i++)
            for (var j = 0; j < Around; j++)
            {
                int a = start + i * Around + j, c = start + i * Around + (j + 1) % Around;
                int d = a + Around, e = c + Around;
                b.Quad(a, c, e, d, b.N[a] + b.N[c] + b.N[d] + b.N[e]);
            }
        }

        static Vector3 MapDir(Vector2Int d) => new Vector3(d.x, 0f, -d.y);

        // Cubes of `size` at the given cells (cell (x, y, z) spans x·size to (x + 1)·size), each `gap` short of
        // its cell on every side so the seams show, their edges bevelled like keycaps. For the Glitch shader's voxel
        // mode: uv runs 0..1 over each face (the bevels sit in the dark seam), uv2 holds the cube's centre and an
        // id in (0, 1], negative for the cubes in `still`.
        public static Mesh Voxels(IList<Vector3Int> cells, float size, float gap, System.Predicate<Vector3Int> still = null)
        {
            var b = new Builder();
            var set = new HashSet<Vector3Int>(cells);
            foreach (var c in cells)
            {
                // Out of sight inside the others.
                if (set.Contains(c + Vector3Int.right) && set.Contains(c + Vector3Int.left) && set.Contains(c + Vector3Int.up)
                    && set.Contains(c + Vector3Int.down) && set.Contains(c + Vector3Int.forward) && set.Contains(c + Vector3Int.back)) continue;
                var centre = ((Vector3)c + Vector3.one * 0.5f) * size;
                var id = 0.05f + 0.95f * DungeonWorld.Hash(c.x * 7 + c.z * 13, c.y * 5 - c.z * 3);
                var info = new Vector4(centre.x, centre.y, centre.z, still != null && still(c) ? -id : id);
                var h = size / 2f - gap;
                b.Chamfered(centre, Vector3.one * h, h * 0.24f, info, Color.white);
            }
            return b.Build("Voxels");
        }

        // Meshes put together as one, each placed by its matrix.
        public static Mesh Combine(string name, IList<(Mesh mesh, Matrix4x4 at)> parts)
        {
            var list = new CombineInstance[parts.Count];
            for (var i = 0; i < parts.Count; i++) list[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].at };
            var m = new Mesh { name = name };
            m.CombineMeshes(list, true, true);
            m.RecalculateBounds();
            return m;
        }

        static Mesh shadowMesh;
        static Material shadow;

        // A soft dark spot on the seabed under something standing or floating there (the sea has no real
        // shadows), `size` across.
        public static MeshRenderer Shadow(Transform parent, float size)
        {
            // Shared by every shadow in the maze and the battle field, so kept for the whole run (Parts.Free).
            if (shadowMesh == null) shadowMesh = Decal(1f);
            if (shadow == null) shadow = Plain("Shadow");
            shadowMesh.hideFlags = shadow.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var r = Parts.Shape("Shadow", parent, shadowMesh, shadow, new Vector3(0f, 0.02f, 0f));
            r.transform.localScale = new Vector3(size, 1f, size);
            return r;
        }

        public static Mesh Plane(float size)
        {
            var b = new Builder();
            var h = size / 2f;
            var a = b.Add(new Vector3(-h, 0, -h), Vector3.up, new Vector2(0, 0));
            var c = b.Add(new Vector3(-h, 0, h), Vector3.up, new Vector2(0, 1));
            var d = b.Add(new Vector3(h, 0, h), Vector3.up, new Vector2(1, 1));
            var e = b.Add(new Vector3(h, 0, -h), Vector3.up, new Vector2(1, 0));
            b.Quad(a, c, d, e, Vector3.up);
            return b.Build("Plane");
        }

        // A quad in the xy plane facing -z (toward a camera behind it), centred.
        public static Mesh Quad(float w = 1f, float h = 1f)
        {
            var b = new Builder();
            var n = Vector3.back;
            var a = b.Add(new Vector3(-w / 2, -h / 2, 0), n, new Vector2(0, 0));
            var c = b.Add(new Vector3(-w / 2, h / 2, 0), n, new Vector2(0, 1));
            var d = b.Add(new Vector3(w / 2, h / 2, 0), n, new Vector2(1, 1));
            var e = b.Add(new Vector3(w / 2, -h / 2, 0), n, new Vector2(1, 0));
            b.Quad(a, c, d, e, n);
            return b.Build("Quad");
        }

        // A flat quad lying on the ground, facing up, centred.
        public static Mesh Decal(float size)
        {
            var b = new Builder();
            var h = size / 2f;
            var a = b.Add(new Vector3(-h, 0, -h), Vector3.up, new Vector2(0, 0));
            var c = b.Add(new Vector3(-h, 0, h), Vector3.up, new Vector2(0, 1));
            var d = b.Add(new Vector3(h, 0, h), Vector3.up, new Vector2(1, 1));
            var e = b.Add(new Vector3(h, 0, -h), Vector3.up, new Vector2(1, 0));
            b.Quad(a, c, d, e, Vector3.up);
            return b.Build("Decal");
        }

        // A box with its edges bevelled (Builder.Chamfered): by default an eighth of its thinnest side, at most
        // 7 cm, so no bare cube shows; bevel 0 for a sharp one.
        public static Mesh Box(Vector3 size, float bevel = -1f)
        {
            var b = new Builder();
            if (bevel < 0f) bevel = Mathf.Min(0.07f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.125f);
            b.Chamfered(Vector3.zero, size / 2f, bevel, default, Color.white);
            return b.Build("Box");
        }

        // A scallop's half shell hinged at the origin and opening towards +z, `radius` out to its rim: ribs fanning
        // out to a scalloped edge, domed up by `height`, with an inside as well as an outside `thickness` under it
        // and the band of its rim between them (it is seen from both sides once it opens).
        public static Mesh Shell(float radius, float height, float thickness = 0.04f, int ribs = 9)
        {
            const int Rows = 8, PerRib = 4;
            const float Spread = 1.2f; // radians either side of +z
            var cols = ribs * PerRib;
            var p = new Vector3[Rows + 1, cols + 1];
            for (var i = 0; i <= Rows; i++)
            for (var j = 0; j <= cols; j++)
            {
                var s = (float)i / Rows;
                var a = Mathf.Lerp(-Spread, Spread, (float)j / cols);
                var rib = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * j / PerRib); // 0 in a groove, 1 on a ridge
                var r = radius * s * (1f + 0.07f * rib * s);
                p[i, j] = new Vector3(Mathf.Sin(a) * r, height * Mathf.Sin(Mathf.PI * 0.85f * s) * (1f + 0.18f * rib * s), Mathf.Cos(a) * r);
            }
            var n = new Vector3[Rows + 1, cols + 1];
            for (var i = 0; i <= Rows; i++)
            for (var j = 0; j <= cols; j++)
            {
                var across = p[i, Mathf.Min(cols, j + 1)] - p[i, Mathf.Max(0, j - 1)];
                var out_ = p[Mathf.Min(Rows, i + 1), j] - p[Mathf.Max(0, i - 1), j];
                var normal = i == 0 ? Vector3.up : Vector3.Cross(across, out_).normalized;
                n[i, j] = normal.y < 0f ? -normal : normal;
            }
            var b = new Builder();
            var outer = new int[Rows + 1, cols + 1];
            var inner = new int[Rows + 1, cols + 1];
            for (var i = 0; i <= Rows; i++)
            for (var j = 0; j <= cols; j++)
            {
                var uv = new Vector2((float)j / cols, (float)i / Rows);
                outer[i, j] = b.Add(p[i, j], n[i, j], uv);
                inner[i, j] = b.Add(p[i, j] - n[i, j] * thickness, -n[i, j], uv);
            }
            for (var i = 0; i < Rows; i++)
            for (var j = 0; j < cols; j++)
            {
                var up = (n[i, j] + n[i + 1, j + 1]).normalized;
                b.Quad(outer[i, j], outer[i + 1, j], outer[i + 1, j + 1], outer[i, j + 1], up);
                b.Quad(inner[i, j], inner[i + 1, j], inner[i + 1, j + 1], inner[i, j + 1], -up);
            }
            // The rim, and the two straight edges up from the hinge.
            for (var j = 0; j < cols; j++)
            {
                var a = Mathf.Lerp(-Spread, Spread, (j + 0.5f) / cols);
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                b.Quad(b.Add(p[Rows, j], o, Vector2.zero), b.Add(p[Rows, j + 1], o, Vector2.zero),
                    b.Add(p[Rows, j + 1] - n[Rows, j + 1] * thickness, o, Vector2.zero), b.Add(p[Rows, j] - n[Rows, j] * thickness, o, Vector2.zero), o);
            }
            foreach (var (j, side) in new[] { (0, -1f), (cols, 1f) })
            {
                var a = side * Spread;
                var o = new Vector3(Mathf.Cos(a) * side, 0f, -Mathf.Sin(a) * side);
                for (var i = 0; i < Rows; i++)
                    b.Quad(b.Add(p[i, j], o, Vector2.zero), b.Add(p[i + 1, j], o, Vector2.zero),
                        b.Add(p[i + 1, j] - n[i + 1, j] * thickness, o, Vector2.zero), b.Add(p[i, j] - n[i, j] * thickness, o, Vector2.zero), o);
            }
            return b.Build("Shell");
        }

        // outline (z, y), convex, drawn out along x from -length/2 to length/2 with flat sides and caps: an arched
        // lid, a sloping desk, a wedge.
        public static Mesh Prism(IList<Vector2> outline, float length)
        {
            var b = new Builder();
            var hx = length / 2f;
            var n = outline.Count;
            var mid = Vector2.zero;
            foreach (var p in outline) mid += p / n;
            for (var i = 0; i < n; i++)
            {
                Vector2 p0 = outline[i], p1 = outline[(i + 1) % n];
                var edge = p1 - p0;
                if (edge.sqrMagnitude < 1e-8f) continue;
                var out2 = new Vector2(edge.y, -edge.x).normalized;
                if (Vector2.Dot(out2, (p0 + p1) / 2f - mid) < 0f) out2 = -out2;
                var nrm = new Vector3(0f, out2.y, out2.x);
                b.Quad(b.Add(new Vector3(-hx, p0.y, p0.x), nrm, new Vector2(0, 0)), b.Add(new Vector3(hx, p0.y, p0.x), nrm, new Vector2(1, 0)),
                    b.Add(new Vector3(hx, p1.y, p1.x), nrm, new Vector2(1, 1)), b.Add(new Vector3(-hx, p1.y, p1.x), nrm, new Vector2(0, 1)), nrm);
            }
            foreach (var side in new[] { -1f, 1f })
            {
                var nrm = Vector3.right * side;
                var centre = b.Add(new Vector3(side * hx, mid.y, mid.x), nrm, new Vector2(0.5f, 0.5f));
                for (var i = 0; i < n; i++)
                {
                    Vector2 p0 = outline[i], p1 = outline[(i + 1) % n];
                    b.Tri(centre, b.Add(new Vector3(side * hx, p0.y, p0.x), nrm, Vector2.zero), b.Add(new Vector3(side * hx, p1.y, p1.x), nrm, Vector2.zero), nrm);
                }
            }
            return b.Build("Prism");
        }

        // A half-ellipse `depth` across in z and `height` high over y = 0, as a Prism outline: an arched lid.
        public static Vector2[] Arch(float depth, float height, int steps = 8)
        {
            var outline = new Vector2[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var a = Mathf.PI * i / steps;
                outline[i] = new Vector2(Mathf.Cos(a) * depth / 2f, Mathf.Sin(a) * height);
            }
            return outline;
        }

        // A sphere, or a capsule along y when stretch > 0 (the hemispheres are pushed apart by it).
        public static Mesh Sphere(float radius, float stretch = 0f, int segments = 20, int rings = 14)
        {
            var b = new Builder();
            var ringStarts = new List<int>();
            for (var i = 0; i <= rings; i++)
            {
                var lat = Mathf.PI * i / rings;
                var y = Mathf.Cos(lat);
                var r = Mathf.Sin(lat);
                var copies = stretch > 0f && i == rings / 2 ? 2 : 1;
                for (var copy = 0; copy < copies; copy++)
                {
                    var shift = y > 1e-4f || (copies == 2 && copy == 0) ? stretch / 2f : -stretch / 2f;
                    if (stretch <= 0f) shift = 0f;
                    ringStarts.Add(b.V.Count);
                    for (var j = 0; j <= segments; j++)
                    {
                        var lon = 2f * Mathf.PI * j / segments;
                        var n = new Vector3(r * Mathf.Cos(lon), y, r * Mathf.Sin(lon));
                        b.Add(n * radius + Vector3.up * shift, n, new Vector2((float)j / segments, (float)i / rings));
                    }
                }
            }
            for (var k = 0; k < ringStarts.Count - 1; k++)
            {
                var a0 = ringStarts[k];
                var b0 = ringStarts[k + 1];
                for (var j = 0; j < segments; j++)
                {
                    var n = b.N[a0 + j] + b.N[a0 + j + 1] + b.N[b0 + j] + b.N[b0 + j + 1];
                    b.Quad(a0 + j, a0 + j + 1, b0 + j + 1, b0 + j, n);
                }
            }
            return b.Build(stretch > 0f ? "Capsule" : "Sphere");
        }

        // A cone along +y, base at y = 0.
        public static Mesh Cone(float radius, float height, int segments = 16)
        {
            var b = new Builder();
            var tip = Vector3.up * height;
            var slope = radius / height;
            for (var j = 0; j < segments; j++)
            {
                var a0 = 2f * Mathf.PI * j / segments;
                var a1 = 2f * Mathf.PI * (j + 1) / segments;
                var p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                var n0 = new Vector3(Mathf.Cos(a0), slope, Mathf.Sin(a0)).normalized;
                var n1 = new Vector3(Mathf.Cos(a1), slope, Mathf.Sin(a1)).normalized;
                var nm = (n0 + n1).normalized;
                var i0 = b.Add(p0, n0, Vector2.zero);
                var i1 = b.Add(p1, n1, Vector2.zero);
                var it = b.Add(tip, nm, Vector2.one);
                b.Tri(i0, it, i1, nm);
                var c0 = b.Add(p0, Vector3.down, Vector2.zero);
                var c1 = b.Add(p1, Vector3.down, Vector2.zero);
                var cc = b.Add(Vector3.zero, Vector3.down, Vector2.zero);
                b.Tri(c0, c1, cc, Vector3.down);
            }
            return b.Build("Cone");
        }

        // An open tube widening from r0 at y = 0 to r1 at y = length, v running along it: light beams.
        public static Mesh Tube(float r0, float r1, float length, int segments = 20)
        {
            var b = new Builder();
            for (var j = 0; j <= segments; j++)
            {
                var a = 2f * Mathf.PI * j / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Add(dir * r0, dir, new Vector2((float)j / segments, 0f));
                b.Add(dir * r1 + Vector3.up * length, dir, new Vector2((float)j / segments, 1f));
            }
            for (var j = 0; j < segments; j++)
            {
                var i = j * 2;
                b.T.Add(i); b.T.Add(i + 1); b.T.Add(i + 3);
                b.T.Add(i); b.T.Add(i + 3); b.T.Add(i + 2);
            }
            return b.Build("Beam");
        }

        // A strand of kelp (or a hanging cable): two crossed quads up +y from the origin, the vertex alpha
        // going from `bottom` to `top`, for the Glow shader.
        public static Mesh Strand(float width, float height, float bottom, float top)
        {
            var b = new Builder();
            for (var k = 0; k < 2; k++)
            {
                var side = k == 0 ? Vector3.right : Vector3.forward;
                var n = k == 0 ? Vector3.forward : Vector3.right;
                var lo = new Color(1, 1, 1, bottom);
                var hi = new Color(1, 1, 1, top);
                var a = b.Add(-side * width / 2, n, new Vector2(0, 0), default, lo);
                var c = b.Add(-side * width / 2 + Vector3.up * height, n, new Vector2(0, 1), default, hi);
                var d = b.Add(side * width / 2 + Vector3.up * height, n, new Vector2(1, 1), default, hi);
                var e = b.Add(side * width / 2, n, new Vector2(1, 0), default, lo);
                b.Quad(a, c, d, e, n);
            }
            return b.Build("Strand");
        }

        // A flat fan on the ground pointing +z: the noise bodies' field of view. Bright at the body, gone
        // at the far edge (vertex alpha).
        public static Mesh Fan(float radius, float degrees, int segments = 24)
        {
            var b = new Builder();
            var centre = b.Add(Vector3.zero, Vector3.up, new Vector2(0.5f, 0.5f), default, new Color(1, 1, 1, 0.7f));
            var ring = new List<int>();
            for (var j = 0; j <= segments; j++)
            {
                var a = Mathf.Deg2Rad * (-degrees / 2f + degrees * j / segments);
                var p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
                ring.Add(b.Add(p, Vector3.up, new Vector2(0.5f, 0.5f), default, new Color(1, 1, 1, 0f)));
            }
            for (var j = 0; j < segments; j++) b.Tri(centre, ring[j], ring[j + 1], Vector3.up);
            return b.Build("Fan");
        }
    }
}
