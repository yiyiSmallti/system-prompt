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

        public static Material Plain(string shader) => new Material(Load(shader));

        // ------------------------------------------------------------------ text

        static Font font;
        static Material text;

        // Words in the sea (graves, signs, chat logs): additive like Glow, from the UI's Chinese font. One
        // material for all of it, its glyph texture swapped in whenever the font's dynamic atlas grows.
        public static TextMesh Text(string name, Transform parent, string words, float height, Color color, Vector3 local = default)
        {
            if (font == null)
            {
                font = Resources.Load<Font>("Fonts/NotoSansSC-Black");
                if (font == null) Debug.LogError("找不到字体 Resources/Fonts/NotoSansSC-Black");
                Font.textureRebuilt += f =>
                {
                    if (f == font && text != null) text.mainTexture = font.material.mainTexture;
                };
            }
            if (text == null) text = new Material(Load("Text"));
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = height * 10f / 64f; // a line is fontSize / 10 units tall at size 1
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

        static Texture2D atlas, beam, white, firewall, ring;

        // Four 128px cells: a chat bubble, an emoji face, a web card and a soft dot, white on clear.
        public static Texture2D Atlas => atlas ??= MakeAtlas();
        public static Texture2D Beam => beam ??= MakeBeam();
        public static Texture2D White => white ??= Solid();
        public static Texture2D Firewall => firewall ??= MakeFirewall();
        public static Texture2D Ring => ring ??= MakeRing();

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

        // ------------------------------------------------------------------ meshes

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

        // The rack walls: every block's sides where they show above a lower neighbour, and its top. The
        // vertex colour tints the LEDs and the top edge by zone (alpha: how much).
        public static Mesh Walls(Dictionary<Vector2Int, float> blocks, System.Func<Vector2Int, Vector3> centre, float cell,
            System.Func<Vector2Int, Color> tint)
        {
            var b = new Builder();
            var half = cell / 2f;
            Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            foreach (var kv in blocks)
            {
                var c = kv.Key;
                var h = kv.Value;
                var o = centre(c);
                var tc = tint(c);
                var seed = (c.x * 73 + c.y * 151) % 97 / 97f;
                foreach (var d in dirs)
                {
                    var below = blocks.TryGetValue(c + d, out var nh) ? nh : 0f;
                    if (below >= h) continue;
                    var n = MapDir(d);
                    var side = new Vector3(n.z, 0f, -n.x); // along the face
                    var mid = o + n * half;
                    var p0 = mid - side * half;
                    var p1 = mid + side * half;
                    float U(Vector3 p) => Mathf.Abs(n.x) > 0.5f ? p.z : p.x;
                    var info = new Vector4(seed, h, 0f, 0f);
                    var i0 = b.Add(p0 + Vector3.up * below, n, new Vector2(U(p0), below), info, tc);
                    var i1 = b.Add(p0 + Vector3.up * h, n, new Vector2(U(p0), h), info, tc);
                    var i2 = b.Add(p1 + Vector3.up * h, n, new Vector2(U(p1), h), info, tc);
                    var i3 = b.Add(p1 + Vector3.up * below, n, new Vector2(U(p1), below), info, tc);
                    b.Quad(i0, i1, i2, i3, n);
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

        static Vector3 MapDir(Vector2Int d) => new Vector3(d.x, 0f, -d.y);

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

        public static Mesh Box(Vector3 size)
        {
            var b = new Builder();
            var h = size / 2f;
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in normals)
            {
                var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(n, u);
                var centre = Vector3.Scale(n, h);
                var su = Vector3.Scale(u, h);
                var sv = Vector3.Scale(v, h);
                var a = b.Add(centre - su - sv, n, new Vector2(0, 0));
                var c = b.Add(centre + su - sv, n, new Vector2(1, 0));
                var d = b.Add(centre + su + sv, n, new Vector2(1, 1));
                var e = b.Add(centre - su + sv, n, new Vector2(0, 1));
                b.Quad(a, c, d, e, n);
            }
            return b.Build("Box");
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
