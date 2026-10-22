using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace SystemPrompt.UI
{
    // Particles drawn as textured quads, one mesh per sprite. Each particle drifts at its own speed, sways,
    // spins and wraps around the edges, so the field never runs out. It stops updating while hidden and
    // shows nothing when 背景动态 is off in the settings.
    public sealed class ParticleField : VisualElement
    {
        public sealed class Emitter
        {
            public Texture2D Sprite;
            public int Count;
            public Vector2 Size;                 // px, min..max
            public Vector2 SpeedX, SpeedY;       // px/s, min..max
            public float Sway;                   // px of sideways wobble
            public Vector2 Spin;                 // deg/s, min..max
            public Vector2 Alpha = new Vector2(0.5f, 1f);
            public float Twinkle;                // 0..1: how far alpha pulses
            public bool Flutter;                 // petals: the width breathes as if turning over
            public Color[] Colors = { Color.white };
        }

        struct Particle
        {
            public Vector2 Pos, Vel;
            public float Size, Angle, Spin, Alpha, Phase, PhaseSpeed;
            public Color Color;
        }

        readonly Emitter[] emitters;
        readonly Particle[][] particles;
        readonly Vertex[][] vertices;
        readonly ushort[][] indices;
        Vector2 seededFor;

        public ParticleField(params Emitter[] emitters)
        {
            this.emitters = emitters;
            particles = new Particle[emitters.Length][];
            vertices = new Vertex[emitters.Length][];
            indices = new ushort[emitters.Length][];
            for (var e = 0; e < emitters.Length; e++)
            {
                var n = emitters[e].Count;
                particles[e] = new Particle[n];
                vertices[e] = new Vertex[n * 4];
                indices[e] = new ushort[n * 6];
                for (var i = 0; i < n; i++)
                {
                    var v = (ushort)(i * 4);
                    var k = i * 6;
                    indices[e][k] = v; indices[e][k + 1] = (ushort)(v + 1); indices[e][k + 2] = (ushort)(v + 2);
                    indices[e][k + 3] = v; indices[e][k + 4] = (ushort)(v + 2); indices[e][k + 5] = (ushort)(v + 3);
                }
            }
            AddToClassList("sp-particles");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            schedule.Execute(Tick).Every(16);
        }

        static bool Enabled => GameRoot.Instance == null || GameRoot.Instance.Settings.backgroundMotion;

        public static ParticleField Create(Ambient ambient, bool front = false)
        {
            switch (ambient)
            {
                case Ambient.Petals:
                    return new ParticleField(new Emitter
                    {
                        Sprite = Sprites.Petal, Count = front ? 9 : 44, Size = front ? new Vector2(30, 46) : new Vector2(12, 26),
                        SpeedX = new Vector2(30, 80), SpeedY = new Vector2(40, 90), Sway = 22, Spin = new Vector2(-110, 110),
                        Alpha = front ? new Vector2(0.75f, 0.95f) : new Vector2(0.55f, 0.9f), Flutter = true,
                        Colors = new[] { Palette.Hex("#FBD3E0"), Palette.Hex("#F6B8CC"), Palette.Hex("#FFE6EE"), Palette.Hex("#F2A7C0") },
                    });
                case Ambient.Motes:
                    return new ParticleField(new Emitter
                    {
                        Sprite = Sprites.Glow, Count = front ? 6 : 34, Size = front ? new Vector2(22, 34) : new Vector2(6, 16),
                        SpeedX = new Vector2(-6, 12), SpeedY = new Vector2(-14, -3), Sway = 12,
                        Alpha = new Vector2(0.25f, 0.6f), Twinkle = 0.7f,
                        Colors = new[] { Palette.Hex("#FFF2D2"), Palette.Hex("#FFE3A8") },
                    });
                case Ambient.Night:
                    return new ParticleField(new Emitter
                    {
                        Sprite = Sprites.Glow, Count = front ? 0 : 26, Size = new Vector2(4, 11),
                        SpeedX = new Vector2(-4, 4), SpeedY = new Vector2(-8, -2), Sway = 8,
                        Alpha = new Vector2(0.15f, 0.45f), Twinkle = 0.9f,
                        Colors = new[] { Palette.Hex("#C8D8FF"), Palette.Cold },
                    });
                case Ambient.Latent:
                    return new ParticleField(
                        new Emitter
                        {
                            Sprite = Sprites.Bubble, Count = front ? 7 : 30, Size = front ? new Vector2(40, 70) : new Vector2(10, 38),
                            SpeedX = new Vector2(-8, 8), SpeedY = new Vector2(-70, -22), Sway = 16,
                            Alpha = front ? new Vector2(0.3f, 0.5f) : new Vector2(0.3f, 0.7f),
                            Colors = new[] { Palette.Cold, Palette.Hex("#9CCBFF") },
                        },
                        new Emitter
                        {
                            Sprite = Sprites.Diamond, Count = front ? 0 : 34, Size = new Vector2(5, 13),
                            SpeedX = new Vector2(-12, 12), SpeedY = new Vector2(-34, -8), Sway = 6, Spin = new Vector2(-140, 140),
                            Alpha = new Vector2(0.35f, 0.85f), Twinkle = 0.8f,
                            Colors = new[] { Palette.Green, Palette.Sea, Palette.Cold },
                        });
                case Ambient.Data:
                    return new ParticleField(
                        new Emitter
                        {
                            Sprite = Sprites.Diamond, Count = 38, Size = new Vector2(6, 15),
                            SpeedX = new Vector2(12, 36), SpeedY = new Vector2(-42, -12), Sway = 8, Spin = new Vector2(-160, 160),
                            Alpha = new Vector2(0.25f, 0.75f), Twinkle = 0.7f,
                            Colors = new[] { Palette.Green, Palette.Green, Palette.Paper },
                        },
                        new Emitter
                        {
                            Sprite = Sprites.Glow, Count = 18, Size = new Vector2(8, 20),
                            SpeedX = new Vector2(8, 24), SpeedY = new Vector2(-30, -10), Sway = 10,
                            Alpha = new Vector2(0.15f, 0.45f), Twinkle = 0.9f,
                            Colors = new[] { Palette.Green },
                        });
                default:
                    return null;
            }
        }

        // Slow drift and breathing zoom for a background picture; the zoom keeps its edges off screen.
        public static void Drift(VisualElement e, float pan = 16f, float zoom = 1.05f)
        {
            var seed = Random.value * 100f;
            e.schedule.Execute(() =>
            {
                if (!Enabled)
                {
                    e.style.translate = StyleKeyword.Null;
                    e.style.scale = StyleKeyword.Null;
                    return;
                }
                var t = Time.unscaledTime + seed;
                e.style.translate = new Translate(Mathf.Sin(t * 0.07f) * pan, Mathf.Cos(t * 0.09f) * pan * 0.5f);
                Ui.Zoom(e, zoom + Mathf.Sin(t * 0.11f) * 0.012f);
            }).Every(33);
        }

        void Seed(Rect r)
        {
            seededFor = r.size;
            for (var e = 0; e < emitters.Length; e++)
                for (var i = 0; i < particles[e].Length; i++)
                    particles[e][i] = Spawn(emitters[e], new Vector2(Random.Range(r.xMin, r.xMax), Random.Range(r.yMin, r.yMax)));
        }

        static Particle Spawn(Emitter em, Vector2 pos) => new Particle
        {
            Pos = pos,
            Vel = new Vector2(Random.Range(em.SpeedX.x, em.SpeedX.y), Random.Range(em.SpeedY.x, em.SpeedY.y)),
            Size = Random.Range(em.Size.x, em.Size.y),
            Angle = Random.Range(0f, 360f),
            Spin = Random.Range(em.Spin.x, em.Spin.y),
            Alpha = Random.Range(em.Alpha.x, em.Alpha.y),
            Phase = Random.Range(0f, Mathf.PI * 2f),
            PhaseSpeed = Random.Range(0.6f, 1.6f),
            Color = em.Colors[Random.Range(0, em.Colors.Length)],
        };

        bool Shown()
        {
            if (panel == null) return false;
            for (VisualElement p = this; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        void Tick(TimerState timer)
        {
            if (!Enabled)
            {
                // Clear what was drawn once; turning the setting back on reseeds.
                if (seededFor != Vector2.zero)
                {
                    seededFor = Vector2.zero;
                    MarkDirtyRepaint();
                }
                return;
            }
            if (!Shown()) return;
            var r = contentRect;
            if (float.IsNaN(r.width) || r.width <= 0 || r.height <= 0) return;
            if (seededFor != r.size) Seed(r);
            var dt = Mathf.Min(timer.deltaTime / 1000f, 0.1f);
            for (var e = 0; e < emitters.Length; e++)
            {
                var em = emitters[e];
                var ps = particles[e];
                for (var i = 0; i < ps.Length; i++)
                {
                    ref var p = ref ps[i];
                    p.Pos += p.Vel * dt;
                    p.Angle += p.Spin * dt;
                    p.Phase += p.PhaseSpeed * dt;
                    var m = p.Size + em.Sway;
                    // Leaving one edge: come back in at the opposite edge somewhere else along it.
                    if (p.Pos.x < r.xMin - m) p = Spawn(em, new Vector2(r.xMax + m, Random.Range(r.yMin, r.yMax)));
                    else if (p.Pos.x > r.xMax + m) p = Spawn(em, new Vector2(r.xMin - m, Random.Range(r.yMin, r.yMax)));
                    else if (p.Pos.y < r.yMin - m) p = Spawn(em, new Vector2(Random.Range(r.xMin, r.xMax), r.yMax + m));
                    else if (p.Pos.y > r.yMax + m) p = Spawn(em, new Vector2(Random.Range(r.xMin, r.xMax), r.yMin - m));
                }
            }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            if (!Enabled || seededFor == Vector2.zero) return;
            for (var e = 0; e < emitters.Length; e++)
            {
                var em = emitters[e];
                var ps = particles[e];
                if (ps.Length == 0) continue;
                var mesh = mgc.Allocate(ps.Length * 4, ps.Length * 6, em.Sprite);
                var verts = vertices[e];
                for (var i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    var center = p.Pos + new Vector2(Mathf.Sin(p.Phase) * em.Sway, 0f);
                    var half = p.Size * 0.5f;
                    var halfX = em.Flutter ? half * (0.35f + 0.65f * Mathf.Abs(Mathf.Cos(p.Phase * 1.3f))) : half;
                    var alpha = p.Alpha * (1f - em.Twinkle * 0.5f * (1f + Mathf.Sin(p.Phase * 2.3f)));
                    var tint = (Color32)p.Color.WithAlpha(alpha);
                    var rad = p.Angle * Mathf.Deg2Rad;
                    var ax = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    var ay = new Vector2(-ax.y, ax.x);
                    var dx = ax * halfX;
                    var dy = ay * half;
                    var v = i * 4;
                    // Clockwise in UI space (y down): top-left, top-right, bottom-right, bottom-left.
                    // UVs span the whole sprite; the renderer remaps them if it atlases the texture.
                    verts[v] = Corner(center - dx - dy, tint, 0f, 1f);
                    verts[v + 1] = Corner(center + dx - dy, tint, 1f, 1f);
                    verts[v + 2] = Corner(center + dx + dy, tint, 1f, 0f);
                    verts[v + 3] = Corner(center - dx + dy, tint, 0f, 0f);
                }
                mesh.SetAllVertices(verts);
                mesh.SetAllIndices(indices[e]);
            }
        }

        static Vertex Corner(Vector2 pos, Color32 tint, float u, float v) => new Vertex
        {
            position = new Vector3(pos.x, pos.y, Vertex.nearZ),
            tint = tint,
            uv = new Vector2(u, v),
        };

        // Sprites drawn once at startup: white shapes in the alpha channel, tinted per particle.
        static class Sprites
        {
            public static readonly Texture2D Petal = Make("petal", (x, y) =>
            {
                // A teardrop with a notch at the wide end, shaded darker toward the stem.
                var py = y * 1.05f;
                var w = 0.62f * Mathf.Sqrt(Mathf.Max(0f, 1f - py * py)) * (0.75f + 0.25f * py);
                var inside = Mathf.Clamp01((w - Mathf.Abs(x)) * 18f);
                var notch = py > 0.62f ? Mathf.Clamp01((Mathf.Abs(x) - (py - 0.62f) * 0.55f) * 30f) : 1f;
                return inside * notch;
            }, (x, y) => Mathf.Lerp(0.82f, 1f, (y + 1f) * 0.5f));

            public static readonly Texture2D Glow = Make("glow", (x, y) =>
            {
                var d = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y));
                return d * d;
            });

            public static readonly Texture2D Bubble = Make("bubble", (x, y) =>
            {
                var d = Mathf.Sqrt(x * x + y * y);
                if (d > 1f) return 0f;
                var ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) * 9f);
                var hx = x + 0.38f;
                var hy = y - 0.38f;
                var highlight = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) * 5f);
                return Mathf.Max(0.1f, Mathf.Max(ring, highlight));
            });

            public static readonly Texture2D Diamond = Make("diamond", (x, y) => Mathf.Clamp01((1f - Mathf.Abs(x) - Mathf.Abs(y)) * 10f));

            static Texture2D Make(string name, Func<float, float, float> alpha, Func<float, float, float> shade = null)
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Particle " + name,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[size * size];
                for (var py = 0; py < size; py++)
                for (var px = 0; px < size; px++)
                {
                    var x = (px + 0.5f) / size * 2f - 1f;
                    var y = (py + 0.5f) / size * 2f - 1f;
                    var s = shade?.Invoke(x, y) ?? 1f;
                    pixels[py * size + px] = new Color(s, s, s, alpha(x, y));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                return tex;
            }
        }
    }

    // Holds the particle layers for a background and swaps them when the place changes.
    public sealed class AmbientLayer
    {
        readonly VisualElement host;
        readonly bool front;
        Ambient current = Ambient.None;

        public AmbientLayer(VisualElement parent, bool front)
        {
            host = Ui.El("sp-ambient", parent);
            Ui.Fill(host);
            host.pickingMode = PickingMode.Ignore;
            this.front = front;
        }

        public void Set(Ambient ambient)
        {
            if (ambient == current) return;
            current = ambient;
            host.Clear();
            var field = ParticleField.Create(ambient, front);
            if (field == null) return;
            Ui.Fill(field);
            host.Add(field);
        }
    }
}
