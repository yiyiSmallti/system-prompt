using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The light of the fighting on the battle's 3D field (Dungeon/BattleStage.cs), one look for each thing done
    // (design/skills.md 战斗特效): slash arcs, splashes, sparks, shock rings and flashes at a hit; the screenshot's
    // frame of light and the pixels it pulls into the phone; 寒流's current and 深海潜流's whirlpool, the ice they
    // freeze a noise body into and its shards; the thoughts and the charge of 深度思考; a guard's shield of cells;
    // a thrown can of soda bursting into foam and a 辣条 into flame; the 标题党型's pop-up windows and the 转发型's
    // splitting arrows; lines of light at a weakness; light trails behind whoever dashes, pillars of light, and
    // the whale of light of the 集成 and 深海潜流. Nearly all of it is additive light on the Glow shader (the whale on Phantom, the ice on
    // Ice, the can and the windows solid on Toon), made in code and gone once played. The field is drawn in HDR
    // and glows (SeaLens): the hot middles of the effects go past white and bloom, their bodies stay under
    // it. Smoke is drawn over what is behind it, to stand the flames out against. The flares light the noise
    // bodies, the figures and the seabed round them (Light, SeaFlash in Shaders/SeaCommon.cginc). Glow multiplies
    // its colour by its own alpha, so a colour times k shines k² as bright: Hot sets brightness and fading apart.
    sealed class BattleEffects
    {
        readonly Transform parent;
        readonly Camera camera;
        readonly Material white, ring, bubble, glint, spikes, flake; // shared; arcs and lances fade by vertex colour
        readonly Material spark, trail, glare, glareCore, wave, flame, flameSheet, fog, smoke; // the HDR ones; glares, wave, smoke copied for each use
        readonly Mesh quad; // the rings' and flashes'
        // Made the first time they are wanted.
        Mesh lance, ice, shard, frame, arrow, can, rim, strip, adChrome, adPage, adBar, cardFace, cardStripe;
        Material canLook, rimLook, stripLook, chrome, page, bar, cardLook, cardInk, shardLook, swipeLook, mote;
        MaterialPropertyBlock gain; // the thoughts' words lit past white (Text.shader _Gain)
        static readonly int GainId = Shader.PropertyToID("_Gain");
        GameObject whale;     // the whale of light (Phantom), made the first time it is called up
        Material whaleLook;

        public BattleEffects(Transform parent, Camera camera)
        {
            this.parent = parent;
            this.camera = camera;
            white = SeaArt.Glow(SeaArt.White, Color.white);
            ring = SeaArt.Glow(SeaArt.Ring, Color.white);
            bubble = SeaArt.Glow(SeaArt.Bubble, Color.white);
            glint = SeaArt.Glow(SeaArt.Flare, Color.white);
            spikes = SeaArt.Glow(SeaArt.Spikes, Color.white);
            flake = SeaArt.Glow(SeaArt.Snow, Color.white * 1.4f);
            spark = SeaArt.Glow(SeaArt.Spark, Color.white * 2.2f);
            trail = SeaArt.Glow(SeaArt.Across, Color.white * 1.2f);
            glare = SeaArt.Glow(SeaArt.Glare, Color.white);
            glareCore = SeaArt.Glow(SeaArt.GlareCore, Color.white);
            wave = SeaArt.Glow(SeaArt.Wave, Color.white);
            flame = SeaArt.Glow(SeaArt.Puff, Color.white * 1.8f);
            flameSheet = SeaArt.Glow(SeaArt.PuffSheet, Color.white * 1.8f); // made here, not when the 辣条 lands: it takes a moment
            fog = SeaArt.Glow(SeaArt.Puff, Color.white * 0.8f); // a glow of mist that stays under the glow's threshold, however thick
            smoke = SeaArt.Glow(SeaArt.Puff, Color.white);
            smoke.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smoke.renderQueue = 2990; // under the light drawn with it
            quad = SeaArt.Quad();
        }

        // The shared materials and meshes, when the field is taken down (BattleStage.Drop); what is playing then
        // goes with the field, and so does its light.
        public void Dispose()
        {
            foreach (var o in new Object[] { white, ring, bubble, glint, spikes, flake, spark, trail, glare, glareCore, wave, flame, flameSheet, fog, smoke, quad, lance, ice, shard,
                         frame, arrow, can, rim, strip, adChrome, adPage, adBar, cardFace, cardStripe, canLook, rimLook, stripLook, chrome, page, bar,
                         cardLook, cardInk, shardLook, swipeLook, mote, whaleLook })
                if (o != null) Object.Destroy(o);
            glims.Clear();
            Shader.SetGlobalColor("_SeaFlashColor", Color.clear);
        }

        // ------------------------------------------------------------------ light

        // A light an effect throws on the field, at At out to Radius metres.
        sealed class Glim
        {
            public Vector3 At;
            public Color Color;
            public float Radius;
        }

        readonly List<Glim> glims = new List<Glim>();
        const float MaxLight = 0.8f;

        // 减弱闪光 in the settings (「降低命中、转场时闪白的亮度」), read at every flash so a change mid-battle holds at
        // once: the glares, glints and shock waves dimmed (a Flash's heat 0.55, which the glow shader squares to
        // about a third, like the 2D Fx.Flash's 0.25 against 0.9) and kept short of burning past white, and the light
        // they throw on the field kept to half.
        internal static bool Calm => GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;

        // A flare of light at `at` (its colour past 1 for a bright one), reaching `radius` metres: there at
        // once and dying away over `seconds`.
        public void Light(Vector3 at, Color color, float radius, float seconds)
        {
            var glim = new Glim { At = at, Color = color, Radius = radius };
            glims.Add(glim);
            Fade().Forget();

            async Awaitable Fade()
            {
                await Anim.Tween(seconds, t => glim.Color = color * ((1f - t) * (1f - t)));
                glims.Remove(glim);
            }
        }

        // A light carried along with something until the returned Glim is put out (Out).
        Glim Lamp(Vector3 at, Color color, float radius)
        {
            var glim = new Glim { At = at, Color = color, Radius = radius };
            glims.Add(glim);
            return glim;
        }

        void Out(Glim glim) => glims.Remove(glim);

        // Every frame (BattleStage.Update): the shaders take one light, so those burning are taken as one, as
        // bright as all of them together (up to MaxLight, so what it lights stays itself) and where the
        // brightest are.
        public void Shine()
        {
            var sum = Color.black;
            var at = Vector3.zero;
            float radius = 0f, weight = 0f;
            foreach (var g in glims)
            {
                var w = g.Color.maxColorComponent;
                if (w <= 0f) continue;
                sum += g.Color;
                at += g.At * w;
                radius += g.Radius * w;
                weight += w;
            }
            if (weight > 0f)
            {
                at /= weight;
                radius /= weight;
            }
            Shader.SetGlobalVector("_SeaFlashPos", new Vector4(at.x, at.y, at.z, Mathf.Max(radius, 0.01f)));
            var over = sum.maxColorComponent / (Calm ? MaxLight * 0.5f : MaxLight);
            if (over > 1f) sum /= over;
            Shader.SetGlobalColor("_SeaFlashColor", new Color(sum.r, sum.g, sum.b, 0f));
        }

        // ------------------------------------------------------------------ slash

        // The 饭勺's blade of water (BattleStage.Dash): a ribbon swept along the path the spoon swings through, from
        // its bowl (`inner`) out past its tip (`outer`) by `reach`, the freshest of it bright and its outer edge the
        // brightest, streaked along the swing. Kept in the space of the one swinging it (`body`) and laid only while
        // the spoon moves fast in that space: a run, a turn or a jump carries the ribbon along instead of smearing it
        // out behind her as a white sheet, and draws none of its own. Each part of it is gone 0.14 s after it was
        // swept, by `clock` (the field's, so a hit-stop holds it). Once released it ends as the last of it ages out.
        public Held Swipe(Transform inner, Transform outer, Transform body, Color color, System.Func<float> clock, float reach = 0.6f)
        {
            const int Samples = 24, Steps = 3; // Steps: rows per pair of samples, two of them curved in between
            // A sample kept once per Gap of the clock, whatever the frame rate, so Samples always reach past Life.
            const float Life = 0.14f, Gap = Life / (Samples - 3);
            // A spoon come apart or renamed would otherwise just draw nothing.
            if (inner == null || outer == null || body == null) Debug.LogError("Swipe: no bowl, tip or body to sweep with");
            swipeLook ??= SeaArt.Glow(SeaArt.Swipe, Color.white * 1.6f); // its outer edge past the threshold while fresh
            var held = new Held();
            var mesh = new Mesh { name = "Swipe" };
            mesh.MarkDynamic();
            var shape = Parts.Shape("Swipe", parent, mesh, swipeLook);
            var rim = new Vector3[Samples];
            var edge = new Vector3[Samples];
            var born = new float[Samples];
            var power = new float[Samples];
            var rows = (Samples - 1) * Steps + 1;
            var vertices = new Vector3[rows * 2];
            var colors = new Color[rows * 2];
            var uvs = new Vector2[rows * 2];
            var triangles = new int[(rows - 1) * 6];
            var fresh = Color.Lerp(color, Color.white, 0.4f);
            var old = color * 0.8f;
            Sweep().Forget();
            return held;

            async Awaitable Sweep()
            {
                int head = 0, count = 0;
                var last = clock();
                var kept = 0f;
                Vector3 lastEdge = default;
                var started = false;
                while (shape != null && inner != null && outer != null && body != null)
                {
                    var now = clock();
                    var dt = now - last;
                    if (!held.Released && dt > 0f)
                    {
                        var a = body.InverseTransformPoint(inner.position);
                        var b = body.InverseTransformPoint(outer.position + (outer.position - inner.position).normalized * reach);
                        var speed = started ? (b - lastEdge).magnitude / dt : 0f;
                        started = true;
                        lastEdge = b;
                        // A new sample once Gap has gone by since the last was kept; till then the newest follows the
                        // spoon, so the ribbon still reaches it every frame.
                        if (count == 0 || now - kept >= Gap)
                        {
                            kept = now;
                            head = (head + 1) % Samples;
                            count = Mathf.Min(count + 1, Samples);
                        }
                        var i = (head - 1 + Samples) % Samples;
                        rim[i] = a;
                        edge[i] = b;
                        born[i] = now;
                        power[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 9f, speed));
                    }
                    last = now;
                    // The samples still alive, newest back; they age in order.
                    var alive = 0;
                    while (alive < count && now - born[(head - 1 - alive + Samples * 2) % Samples] < Life) alive++;
                    if (held.Released && alive == 0) break;
                    Lay(alive, head, now);
                    await Awaitable.NextFrameAsync();
                }
                if (shape != null) Object.Destroy(shape.gameObject);
                Object.Destroy(mesh);
            }

            // The ribbon through the `alive` newest samples, oldest first, curved through them (Catmull-Rom).
            void Lay(int alive, int head, float now)
            {
                mesh.Clear();
                if (alive < 2) return;
                int Index(int k) => (head - alive + Mathf.Clamp(k, 0, alive - 1) + Samples * 2) % Samples;
                var n = 0;
                for (var k = 0; k < alive; k++)
                for (var q = 0; q < (k == alive - 1 ? 1 : Steps); q++)
                {
                    var t = (float)q / Steps;
                    int i0 = Index(k - 1), i1 = Index(k), i2 = Index(k + 1), i3 = Index(k + 2);
                    var a = Curve(rim[i0], rim[i1], rim[i2], rim[i3], t);
                    var b = Curve(edge[i0], edge[i1], edge[i2], edge[i3], t);
                    var age = Mathf.Clamp01((now - Mathf.Lerp(born[i1], born[i2], t)) / Life);
                    var c = Color.Lerp(fresh, old, age);
                    c.a = Mathf.Lerp(power[i1], power[i2], t) * Mathf.Pow(1f - age, 1.5f);
                    vertices[n * 2] = parent.InverseTransformPoint(body.TransformPoint(a));
                    vertices[n * 2 + 1] = parent.InverseTransformPoint(body.TransformPoint(b));
                    colors[n * 2] = colors[n * 2 + 1] = c;
                    uvs[n * 2] = new Vector2(age, 0f);
                    uvs[n * 2 + 1] = new Vector2(age, 1f);
                    if (n > 0)
                    {
                        var j = (n - 1) * 6;
                        int p = (n - 1) * 2;
                        triangles[j] = p;
                        triangles[j + 1] = p + 2;
                        triangles[j + 2] = p + 3;
                        triangles[j + 3] = p;
                        triangles[j + 4] = p + 3;
                        triangles[j + 5] = p + 1;
                    }
                    n++;
                }
                mesh.SetVertices(vertices, 0, n * 2);
                mesh.SetColors(colors, 0, n * 2);
                mesh.SetUVs(0, uvs, 0, n * 2);
                mesh.SetTriangles(triangles, 0, (n - 1) * 6, 0);
            }

            static Vector3 Curve(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
                0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
        }

        // A crescent swept across `at` in the plane facing the camera, rolled by `roll` degrees: the light runs
        // along it from one end to the other in a tenth of a second and fades from its tail. Across it, from the
        // inside out: a soft body of the colour, then a hot edge past white, sharp on its outer side; the head
        // burns brightest and throws sparks off along the way it is going.
        public void Slash(Vector3 at, Color color, float size, float roll, float seconds = 0.34f)
        {
            const int Steps = 32, Rows = 4;
            var facing = (camera.transform.position - at).normalized;
            var basis = Quaternion.LookRotation(-facing) * Quaternion.Euler(0f, 0f, roll);
            var mesh = new Mesh { name = "Slash" };
            var vertices = new Vector3[(Steps + 1) * Rows];
            var colors = new Color[vertices.Length];
            var triangles = new List<int>();
            Vector3 Dir(float u)
            {
                var angle = Mathf.Lerp(-105f, 105f, u) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle) * 0.55f, 0f);
            }
            for (var i = 0; i <= Steps; i++)
            {
                var u = (float)i / Steps;
                var dir = Dir(u);
                // Sin(π) comes out a hair below 0 in floats, and a negative to the power 0.7 is NaN.
                var width = size * 0.42f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f);
                vertices[i * Rows] = dir * (size - width);
                vertices[i * Rows + 1] = dir * (size - width * 0.45f);
                vertices[i * Rows + 2] = dir * (size - width * 0.06f);
                vertices[i * Rows + 3] = dir * (size + width * 0.07f);
                if (i == Steps) continue;
                for (var row = 0; row < Rows - 1; row++)
                {
                    int a = i * Rows + row, b = a + 1, c = a + Rows, d = c + 1;
                    triangles.AddRange(new[] { a, c, d, a, d, b });
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            var shape = Parts.Shape("Slash", parent, mesh, white);
            shape.transform.SetPositionAndRotation(at, basis);
            var hot = Color.Lerp(color, Color.white, 0.55f);
            var thrown = Spray(Color.Lerp(color, Color.white, 0.4f), seconds);
            Run(shape.gameObject, seconds, t =>
            {
                var head = Mathf.Clamp01(t / 0.26f) * 1.3f;
                var fade = 1f - Mathf.Clamp01((t - 0.55f) / 0.45f);
                for (var i = 0; i <= Steps; i++)
                {
                    var u = (float)i / Steps;
                    var lit = u > head ? 0f : (1f - Mathf.Clamp01((head - u) / 0.85f)) * fade;
                    var burn = u > head ? 0f : 1f + 1.5f * Mathf.Exp(-Mathf.Pow((head - u) / 0.09f, 2f));
                    colors[i * Rows] = Color.clear;
                    colors[i * Rows + 1] = color * (lit * 0.55f);
                    colors[i * Rows + 2] = hot * (lit * 1.9f * burn);
                    colors[i * Rows + 3] = Color.clear;
                }
                mesh.colors = colors;
                if (thrown == null || head > 1f || fade < 1f) return;
                var tip = shape.transform.TransformPoint(Dir(head) * size);
                var ahead = shape.transform.TransformDirection(Dir(Mathf.Min(1f, head + 0.02f)) - Dir(Mathf.Max(0f, head - 0.02f))).normalized;
                for (var k = 0; k < 2; k++)
                    thrown.Emit(new ParticleSystem.EmitParams
                    {
                        position = tip,
                        velocity = ahead * Random.Range(3f, 9f) + Random.insideUnitSphere * 2f,
                        startSize = Random.Range(0.06f, 0.13f),
                        startLifetime = Random.Range(0.2f, 0.45f),
                    }, 1);
            }, () => Object.Destroy(mesh));
        }

        // Sparks for an effect to throw off itself as it goes (ParticleSystem.Emit), there until `seconds` are
        // up and the last has died.
        ParticleSystem Spray(Color color, float seconds)
        {
            var ps = Burst("Spray", Vector3.zero, 0, 0.2f, 0.45f, 0f, 0.1f, spark);
            var main = ps.main;
            main.duration = seconds;
            main.maxParticles = 120;
            main.startColor = color;
            main.gravityModifier = 0.5f;
            Stretch(ps, 0.04f, 1.2f);
            Dying(ps);
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ bursts

        // Short streaks flying out of `at` and falling, white-hot in their middles.
        public void Sparks(Vector3 at, Color color, int count, float speed, float size = 0.08f)
        {
            var ps = Burst("Sparks", at, count, 0.22f, 0.55f, speed, size * 1.6f, spark);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
            main.gravityModifier = 0.7f;
            Stretch(ps, 0.05f, 1f);
            Dying(ps);
            ps.Play();
        }

        // Sparks that hang in the water a while after a blow, slowing and sinking (or, `rise` below 0, going up
        // like embers), flickering out.
        public void Embers(Vector3 at, Color color, int count, float speed, float rise = 0.35f)
        {
            var ps = Burst("Embers", at, count, 0.5f, 1.2f, speed, 0.1f, spark);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.5f));
            main.gravityModifier = rise;
            var drag = ps.limitVelocityOverLifetime;
            drag.dampen = 0.12f;
            var wander = ps.noise;
            wander.enabled = true;
            wander.strength = 0.6f;
            wander.frequency = 1.5f;
            Stretch(ps, 0.03f, 1f);
            Dying(ps);
            ps.Play();
        }

        // Square bits of a noise body tumbling away from `at`, in its two colours: what a screenshot takes off it.
        public void Pixels(Vector3 at, Color a, Color b, int count, float speed)
        {
            var ps = Burst("Pixels", at, count, 0.5f, 0.9f, speed, 0.13f, white);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.25f;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            ps.Play();
        }

        // A noise body deleted at `at` (BattleStage.Vanish): its pixels (colours a and b) thrown out in a burst
        // with white ones among them, a flash and a glint, a shock wave and the field lit in its colour.
        public void Crash(Vector3 at, Color a, Color b)
        {
            Pixels(at, a, b, 48, 6f);
            Pixels(at, Color.white, Color.white, 14, 6f);
            Flash(at, Color.Lerp(a, Color.white, 0.5f), 1.3f, 0.18f, 1.6f);
            Star(at, Color.white, 2f, 0.2f, 0f, 2f);
            Ring(at, (camera.transform.position - at).normalized, a, 1.6f, 0.35f);
            Light(at, a * 2.5f, 6f, 0.4f);
        }

        // What became of a noise body (「已删除」), on a pop-up window turned to the camera over it (above its plate,
        // the damage and its tag in the 2D layer): springing open, held a moment, closed like a window. Its parts are
        // the pop-ups' own, made once for all of them. `stamp`: where a stamp is up over the blow (Aside).
        public void Notice(Vector3 at, string words, Rect? stamp)
        {
            if (parent == null) return;
            var window = Parts.Node("Notice", parent);
            Window(window, words);
            at += Vector3.up * 0.9f;
            if (stamp is Rect area) at += camera.transform.right * Aside(at, area);
            const float Open = 0.12f, Held = 0.3f, Shut = 0.1f, Seconds = Open + Held + Shut;
            Run(window.gameObject, Seconds, t =>
            {
                var s = t * Seconds;
                var size = 1.25f * (s < Open ? Ease.OutBack(s / Open) : 1f);
                var shut = s > Open + Held ? Mathf.Clamp01((s - Open - Held) / Shut) : 0f;
                window.SetPositionAndRotation(at, camera.transform.rotation);
                window.localScale = new Vector3(size, size * (1f - shut), size);
            });
        }

        // How far along the camera's right a Notice opening at `at` slides to keep out from under the stamp over the
        // blow (对抗样本！ beside the body it hit, 集成！ over the middle of the top; `stamp` in the viewport), 0 when
        // it is clear: just past the stamp's near edge on the side it is on, or past its far edge when that side
        // has no room, and on the picture either way.
        float Aside(Vector3 at, Rect stamp)
        {
            const float Half = 0.44f, HalfHigh = 0.3f; // the window's, in metres (0.7 × 0.48 at 1.25)
            var right = camera.transform.right;
            var p = camera.WorldToViewportPoint(at);
            if (p.z <= 0f) return 0f;
            var wide = camera.WorldToViewportPoint(at + right * Half).x - p.x;
            var high = wide * camera.aspect * HalfHigh / Half;
            if (wide <= 0f || p.y + high < stamp.yMin || p.y - high > stamp.yMax || p.x + wide < stamp.xMin || p.x - wide > stamp.xMax) return 0f;
            float before = stamp.xMin - wide - 0.01f, after = stamp.xMax + wide + 0.01f;
            bool roomBefore = before >= wide + 0.02f, roomAfter = after <= 1f - wide - 0.02f;
            var x = p.x < stamp.center.x ? (roomBefore || !roomAfter ? before : after) : (roomAfter || !roomBefore ? after : before);
            x = Mathf.Clamp(x, wide + 0.02f, 1f - wide - 0.02f);
            return (x - p.x) / wide * Half;
        }

        ParticleSystem Burst(string name, Vector3 at, int count, float minLife, float maxLife, float speed, float size, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0f;
            drag.dampen = 0.08f;
            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        // A shock wave spreading from `at` across the plane facing `normal`, out to `radius`: a sharp bright
        // front with a fading wake inside it, thinning as it goes.
        public void Ring(Vector3 at, Vector3 normal, Color color, float radius, float seconds = 0.45f, float heat = 1.5f)
        {
            if (Calm) heat = Mathf.Min(heat * 0.75f, 1f);
            var shape = Parts.Shape("Ring", parent, quad, new Material(wave) { color = color });
            shape.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.localScale = Vector3.one * (radius * 2f * Mathf.Lerp(0.12f, 1f, Ease.OutCubic(t)));
                material.color = Hot(color, heat * heat, Mathf.Pow(1f - t, 1.6f)); // still there as it reaches its edge
            }, () => Object.Destroy(material));
        }

        // A round flash at `at`, turned to the camera: a glare with a hot middle, past white by `heat`. From 2 m
        // across it is a hot core with only a breath of glare round it: a wide glare over the deep blue reads as a
        // grey disc washing the field out. One filling the view from up close (wider than twice its distance from
        // the lens) stays soft, so it covers the view to its edges.
        public void Flash(Vector3 at, Color color, float size, float seconds = 0.28f, float heat = 1.5f)
        {
            var core = size >= 2f && size < 2f * Vector3.Distance(at, camera.transform.position);
            if (Calm)
            {
                heat = Mathf.Min(heat * 0.55f, 0.8f);
                size *= 0.8f;
            }
            var shape = Parts.Shape("Flash", parent, quad, new Material(core ? glareCore : glare) { color = color });
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.SetPositionAndRotation(at, camera.transform.rotation);
                shape.transform.localScale = Vector3.one * (size * Mathf.Lerp(0.6f, 1.15f, Ease.OutCubic(t)));
                material.color = Hot(color, heat * heat, Mathf.Pow(1f - t, 3f));
            }, () => Object.Destroy(material));
        }

        // A four-pointed star of light at `at` turned to the camera and rolled by `roll`, its rays shrinking
        // back into it as it fades: the glint of a blow landing. `needles`: eight needle rays with no glow between
        // them (SeaArt.Spikes), for a big one that must stay a shape and not become a disc.
        public void Star(Vector3 at, Color color, float size, float seconds = 0.25f, float roll = 0f, float heat = 2.4f, bool needles = false)
        {
            if (Calm) heat = Mathf.Min(heat * 0.55f, 0.9f);
            var shape = Parts.Shape("Star", parent, quad, new Material(needles ? spikes : glint) { color = color });
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.SetPositionAndRotation(at, camera.transform.rotation * Quaternion.Euler(0f, 0f, roll + 25f * t));
                shape.transform.localScale = Vector3.one * (size * Mathf.Lerp(1f, 0.35f, Ease.InCubic(t)) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(t * 8f)));
                material.color = Hot(color, heat * heat, (1f - t) * (1f - t)); // steep: a glint, gone at once
            }, () => Object.Destroy(material));
        }

        // A colour `gain` times as bright, `fade` of it shown: Glow multiplies the colour by its alpha, so a plain
        // colour times k would be k² as bright and fade twice as fast as meant.
        static Color Hot(Color c, float gain, float fade) => new Color(c.r * gain, c.g * gain, c.b * gain, c.a * fade);

        // Soft puffs at `at` drifting up and spreading, `alpha` thick: smoke drawn over what is behind, or
        // (`lit`) a glow of mist.
        public void Mist(Vector3 at, Color color, int count, float size, float alpha, bool lit = false)
        {
            var ps = Burst("Mist", at, count, 0.7f, 1.4f, 1.6f, size, lit ? flame : smoke);
            var main = ps.main;
            main.startColor = new Color(color.r, color.g, color.b, alpha);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.08f;
            var shape = ps.shape;
            shape.radius = size * 0.25f;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            var grow = ps.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.5f));
            Fading(ps);
            ps.Play();
        }

        // Water thrown off a blow: drops flung out and falling, a few bubbles and a breath of mist.
        public void Splash(Vector3 at, Color color)
        {
            var drops = Burst("Splash", at, 30, 0.35f, 0.7f, 8f, 0.16f, spark);
            var main = drops.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.gravityModifier = 1.6f;
            Stretch(drops, 0.04f, 1f);
            Dying(drops);
            drops.Play();
            var bubbles = Burst("Bubbles", at, 12, 0.6f, 1.1f, 2.5f, 0.3f, bubble);
            var rise = bubbles.main;
            rise.startColor = new Color(0.75f, 0.95f, 1f, 0.8f);
            rise.gravityModifier = -0.2f;
            bubbles.Play();
            Mist(at, color, 5, 1.1f, 0.18f, true);
        }

        // A blow on a weakness or a critical, in layers: the field lit up in its colour; a small white-hot core in a
        // tight glow of the colour; a big star of needle rays in the colour, a small white one turned half a point
        // inside it; thin lines of light flung out all round, embers left hanging and two thin shock waves; for a
        // critical, two cuts crossing over it and a third wave after. Its shape, not its size, tells it from a plain
        // hit: spikes and thin rings round a small core, the body and its damage seen through it. Calm (减弱闪光) keeps
        // only what is coloured: no white star, white wave or white lines, and fewer, finer lines.
        public void Impact(Vector3 at, Color color, bool critical)
        {
            var facing = (camera.transform.position - at).normalized;
            var hot = Color.Lerp(color, Color.white, 0.6f);
            var calm = Calm;
            var star = critical ? 5.2f : 4.2f;
            var roll = Random.Range(0f, 90f);
            Light(at, color * (critical ? 3.5f : 2.6f), 7f, 0.45f);
            Flash(at, calm ? hot : Color.white, 0.9f, 0.12f, 2.1f);
            Flash(at, color, 2.4f, 0.3f, 0.8f);
            Star(at, color, star, 0.3f, roll, 1.6f, true); // white only near the middle, even for the pale ice
            if (!calm) Star(at, Color.white, star * 0.35f, 0.18f, roll + 45f, 1.5f, true);
            var lines = Burst("Impact", at, calm ? 14 : 28, 0.12f, 0.26f, 28f, calm ? 0.08f : 0.12f, spark);
            var main = lines.main;
            // Calm: under a quarter of the spark's light, so not even the green of a 弱点 goes white.
            main.startColor = calm ? new ParticleSystem.MinMaxGradient(color.WithAlpha(0.22f)) : new ParticleSystem.MinMaxGradient(color, Color.white);
            Stretch(lines, 0.05f, 2f);
            Dying(lines);
            lines.Play();
            Embers(at, hot, calm ? 12 : 22, 7f);
            // The wide wave stays its colour (white-hot round its whole edge it would be a white circle the size of the
            // body); a small white one inside it.
            Ring(at, facing, color, 2.8f, 0.34f, 1.2f);
            if (!calm) Ring(at, facing, Color.white, 1.1f, 0.2f, 1.5f);
            if (!critical) return;
            // Pale, not white, and 1.3 m (the crescent grew wider and quicker for the 饭勺): only the thin hot edge
            // goes white, so the cuts over a body flashing near-white (a kill) do not join it into one white blot.
            Slash(at, Color.Lerp(color, Color.white, 0.7f), 1.3f, 40f, 0.3f);
            Slash(at, new Color(1f, 0.35f, 0.4f), 1.3f, -40f, 0.3f);
            Ring(at, facing, new Color(1f, 0.35f, 0.4f), 4f, 0.5f, 0.9f);
        }

        // A column of light standing up out of the seabed at `at`: a soft-edged band of the colour turned to the
        // camera round its own axis (Shaft), a white-hot thread up its middle and streaks running up it, fading in
        // off the seabed and soft at the top; a shock wave on the seabed at its foot, and what is round lit up.
        public void Pillar(Vector3 at, Color color, float radius, float height, float seconds = 0.8f)
        {
            var foot = new Vector3(at.x, 0f, at.z);
            var ribbon = SeaArt.Ribbon(radius * 2.8f, 12);
            var look = SeaArt.Shaft(color.WithAlpha(1f), Color.white, 0.16f, 7f, 0.75f, 1.3f, 0.2f);
            var shape = Parts.Shape("Pillar", parent, ribbon, look);
            shape.transform.position = foot;
            var hot = Color.Lerp(color, Color.white, 0.6f);
            Light(foot + Vector3.up * 1.2f, color * 2f, radius * 6f + 2f, seconds * 0.8f);
            Ring(foot + Vector3.up * 0.05f, Vector3.up, color, radius * 2.4f, seconds * 0.6f);
            Run(shape.gameObject, seconds, t =>
            {
                var rise = Ease.OutCubic(Mathf.Clamp01(t / 0.25f));
                var fade = 1f - Mathf.Clamp01((t - 0.35f) / 0.65f);
                shape.transform.localScale = new Vector3(1f, height * rise, 1f);
                look.SetColor("_Color", (color * 1.3f).WithAlpha(fade)); // its middle past the glow's threshold
                look.SetColor("_CoreColor", hot.WithAlpha(fade * fade));
            }, () =>
            {
                Object.Destroy(look);
                Object.Destroy(ribbon);
            });
        }

        // Water thrown up round the feet of a blow driven down into the seabed (饭勺·跃劈): a crown of big drops flung
        // up and out off a ring round the column of light and falling back, bubbles rising out of it. A beat after
        // the blow, once its flash is going, and in the water's own blue, under white: it shows beside the column,
        // not lost in its light.
        public void WaterCrown(Vector3 feet, Color color)
        {
            const float After = 0.07f;
            var drops = Burst("Crown", feet + Vector3.up * 0.05f, 30, 0.55f, 0.85f, 7f, 0.4f, spark);
            drops.emission.SetBursts(new[] { new ParticleSystem.Burst(After, (short)30) });
            var main = drops.main;
            // A third of the spark's light: bright blue, its red and green never past white.
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, 0.32f), new Color(0.2f, 0.55f, 1f, 0.42f));
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.gravityModifier = 1.6f;
            var shape = drops.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = 0.85f;
            shape.radiusThickness = 0f; // off its rim, round the column
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Stretch(drops, 0.05f, 1f);
            Dying(drops);
            drops.Play();
            var bubbles = Burst("Crown", feet + Vector3.up * 0.1f, 10, 0.6f, 1.1f, 1.5f, 0.24f, bubble);
            bubbles.emission.SetBursts(new[] { new ParticleSystem.Burst(After, (short)10) });
            var rise = bubbles.main;
            rise.startColor = new Color(0.75f, 0.95f, 1f, 0.8f);
            rise.gravityModifier = -0.3f;
            var ring = bubbles.shape;
            ring.radius = 0.6f;
            bubbles.Play();
        }

        // ------------------------------------------------------------------ ice

        // Ice bursting out of the seabed round `at`, shattering a moment later.
        public async Awaitable Frost(Vector3 at, Color color, float size)
        {
            if (parent == null) return;
            var ground = new Vector3(at.x, 0f, at.z);
            var spikes = new List<Transform>();
            for (var i = 0; i < 8; i++)
            {
                var around = Quaternion.Euler(0f, i * 45f + Random.Range(-15f, 15f), 0f) * Vector3.forward;
                var spike = Parts.Shape("Frost", parent, Lance(), white).transform;
                spike.position = ground + around * size * Random.Range(0.25f, 0.55f);
                spike.rotation = Quaternion.LookRotation(Vector3.Slerp(Vector3.up, around, Random.Range(0.25f, 0.55f)));
                spikes.Add(spike);
            }
            Ring(ground + Vector3.up * 0.05f, Vector3.up, color, size * 1.4f, 0.5f);
            var lengths = spikes.ConvertAll(_ => size * Random.Range(1.6f, 3.2f));
            await Anim.Tween(0.14f, t =>
            {
                for (var i = 0; i < spikes.Count; i++)
                    if (spikes[i] != null) spikes[i].localScale = new Vector3(size * 2.4f, size * 2.4f, lengths[i]) * t;
            }, Ease.OutBack);
            await Anim.Wait(0.32f);
            if (parent == null) return;
            foreach (var spike in spikes)
            {
                if (spike == null) continue;
                Sparks(spike.position + spike.forward * 0.4f, color, 8, 4f, 0.1f);
                Object.Destroy(spike.gameObject);
            }
            Flash(at, color, size * 2.5f, 0.25f);
        }

        // A long octahedron along +z, 1 m, white at the tips and the colour of ice in between.
        Mesh Lance()
        {
            if (lance != null) return lance;
            var cold = new Color(0.55f, 0.9f, 1f, 1f);
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var ring = new Vector3[6];
            for (var i = 0; i < 6; i++) ring[i] = new Vector3(Mathf.Cos(i * Mathf.PI / 3f), Mathf.Sin(i * Mathf.PI / 3f), 0f) * 0.06f + Vector3.forward * 0.15f;
            foreach (var tip in new[] { Vector3.forward * 0.5f, Vector3.back * 0.5f })
            for (var i = 0; i < 6; i++)
            {
                var start = vertices.Count;
                vertices.Add(tip);
                vertices.Add(ring[i]);
                vertices.Add(ring[(i + 1) % 6]);
                colors.Add(Color.white * 0.9f);
                colors.Add(cold * 0.55f);
                colors.Add(cold * 0.55f);
                if (tip.z > 0f) triangles.AddRange(new[] { start, start + 1, start + 2 });
                else triangles.AddRange(new[] { start, start + 2, start + 1 });
            }
            lance = new Mesh { name = "Lance" };
            lance.SetVertices(vertices);
            lance.SetColors(colors);
            lance.SetTriangles(triangles, 0);
            lance.RecalculateBounds();
            return lance;
        }

        // ------------------------------------------------------------------ 截图

        // The shutter's frame of light round `at`, snapping in on it and then flown into the phone at `phone`,
        // shrinking on the way, the bits it took off the noise body (its colours a and b) drawn in after it.
        public void Snap(Vector3 at, Vector3 phone, Color color, Color a, Color b)
        {
            var look = new Material(white) { color = color };
            var snap = Parts.Shape("Snap", parent, Frame(), look).transform;
            Run(snap.gameObject, 0.6f, t =>
            {
                var shut = Ease.OutCubic(Mathf.Clamp01(t / 0.25f));
                var away = Ease.InCubic(Mathf.Clamp01((t - 0.3f) / 0.7f));
                snap.SetPositionAndRotation(Vector3.Lerp(at, phone, away), camera.transform.rotation);
                snap.localScale = Vector3.one * (Mathf.Lerp(1.6f, 1f, shut) * Mathf.Lerp(1f, 0.08f, away));
                look.color = color * Mathf.Lerp(2.4f, 1.3f, shut);
            }, () => Object.Destroy(look));
            Light(at, color * 2.2f, 6f, 0.4f);
            Siphon(at, phone, a, b);
        }

        // Bits of a noise body knocked off at `at` and, after a moment, drawn into `to` (the phone).
        void Siphon(Vector3 at, Vector3 to, Color a, Color b)
        {
            var ps = Burst("Siphon", at, 36, 0.55f, 0.8f, 4.5f, 0.13f, white);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ps.Play();
            Pull().Forget();

            async Awaitable Pull()
            {
                var bits = new ParticleSystem.Particle[36];
                while (ps != null && ps.IsAlive())
                {
                    var n = ps.GetParticles(bits);
                    var pull = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
                    for (var i = 0; i < n; i++)
                    {
                        var k = Mathf.Clamp01((bits[i].startLifetime - bits[i].remainingLifetime - 0.12f) / 0.25f);
                        bits[i].position = Vector3.Lerp(bits[i].position, to, pull * k);
                        bits[i].velocity *= 1f - k;
                    }
                    ps.SetParticles(bits, n);
                    await Awaitable.NextFrameAsync();
                }
            }
        }

        // A camera's frame two metres by one and a half in the plane facing -z: a faint outline, bright corners
        // and a cross at the focus point.
        Mesh Frame()
        {
            if (frame != null) return frame;
            const float W = 1f, H = 0.75f, Line = 0.025f, Arm = 0.3f, Thick = 0.06f;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            void Bar(float x0, float y0, float x1, float y1, float lit)
            {
                var s = vertices.Count;
                vertices.Add(new Vector3(Mathf.Min(x0, x1), Mathf.Min(y0, y1)));
                vertices.Add(new Vector3(Mathf.Max(x0, x1), Mathf.Min(y0, y1)));
                vertices.Add(new Vector3(Mathf.Max(x0, x1), Mathf.Max(y0, y1)));
                vertices.Add(new Vector3(Mathf.Min(x0, x1), Mathf.Max(y0, y1)));
                for (var i = 0; i < 4; i++) colors.Add(new Color(1f, 1f, 1f, lit));
                triangles.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Bar(-W, H - Line, W, H, 0.45f);
            Bar(-W, -H, W, -H + Line, 0.45f);
            Bar(-W, -H + Line, -W + Line, H - Line, 0.45f);
            Bar(W - Line, -H + Line, W, H - Line, 0.45f);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sy in new[] { -1f, 1f })
            {
                Bar(sx * W, sy * H, sx * (W - Arm), sy * (H - Thick), 1f);
                Bar(sx * W, sy * H, sx * (W - Thick), sy * (H - Arm), 1f);
            }
            Bar(-0.09f, -Line / 2f, 0.09f, Line / 2f, 0.8f);
            Bar(-Line / 2f, -0.09f, Line / 2f, 0.09f, 0.8f);
            return frame = Build("Frame", vertices, colors, triangles);
        }

        // ------------------------------------------------------------------ 寒流 and 深海潜流

        // 寒流 (pv/README.md S15): a current of cold light from `from` to `to`, `width` across, strands of it
        // winding round a bright head and ice crystals glinting off it as it goes. Done when it lands.
        public async Awaitable Stream(Vector3 from, Vector3 to, Color color, float width, float seconds)
        {
            if (parent == null) return;
            var root = Parts.Node("Stream", parent);
            root.position = from;
            // The head: a small hot middle in a wide soft cloud of the cold, ice-cyan rather than white. The battle's
            // lens turns white whatever is past white in all three channels, so its red is held down: where the head,
            // its cloud and the strand down the middle pile up, only green and blue go past, but for a pin-point.
            var ice = new Color(color.r * 0.4f, color.g, color.b);
            var headLook = new Material(glareCore) { color = ice * 1.15f };
            var haloLook = new Material(glare) { color = color * 0.45f };
            var head = Parts.Shape("Head", root, quad, headLook).transform;
            var halo = Parts.Shape("Halo", root, quad, haloLook).transform;
            var lamp = Lamp(from, color * 1.8f, 5f);
            var strands = new Transform[width > 0.5f ? 5 : 3];
            for (var i = 0; i < strands.Length; i++)
            {
                strands[i] = Parts.Node("Strand", root);
                // Ice-blue, not white, their red held down like the head's, so where they wind over one another only
                // the head's middle goes white (at 0.85 the middle one drew a white thread the length of the current).
                Trail(strands[i].gameObject, (ice * (i == 0 ? 0.7f : 0.6f)).WithAlpha(1f), width * (i == 0 ? 0.45f : 0.3f), 0.24f);
            }
            // Snowflakes glinting along the way, and a mist of cold hanging behind it. The flakes in the ice-cyan too:
            // white ones, as thick as they fall, ran together into a white tube the length of the current.
            var crystals = Shed(root, ice, 30f, flake, 1.3f, false);
            var mist = Shed(root, new Color(color.r, color.g, color.b, 0.16f), 10f, fog, 2.6f, false);
            var run = to - from;
            var side = Vector3.Cross(run, Vector3.up).normalized;
            var up = Vector3.Cross(side, run.normalized);
            await Anim.Tween(seconds, t =>
            {
                if (root == null) return;
                var p = Vector3.Lerp(from, to, t) + up * (Mathf.Sin(t * Mathf.PI) * 0.3f);
                head.SetPositionAndRotation(p, camera.transform.rotation);
                head.localScale = Vector3.one * (width * (1.7f + 0.3f * Mathf.Sin(t * 47f)));
                halo.SetPositionAndRotation(p, camera.transform.rotation);
                halo.localScale = Vector3.one * (width * (3.6f + 0.5f * Mathf.Sin(t * 29f)));
                haloLook.color = color * (0.42f + 0.1f * Mathf.Sin(t * 53f));
                crystals.transform.position = mist.transform.position = p;
                lamp.At = p;
                for (var i = 0; i < strands.Length; i++)
                {
                    // The first down the middle, the rest round it, closing in toward the target.
                    var turn = (t * 3f + (float)i / strands.Length) * Mathf.PI * 2f;
                    var r = i == 0 ? 0f : width * (1f - 0.5f * t);
                    strands[i].position = p + (side * Mathf.Cos(turn) + up * Mathf.Sin(turn)) * r;
                }
            }, t => t * (0.6f + 0.4f * t), () => root == null);
            Out(lamp);
            if (root != null)
            {
                Object.Destroy(head.gameObject);
                Object.Destroy(halo.gameObject);
                crystals.Stop();
                mist.Stop();
                Object.Destroy(root.gameObject, 0.9f);
            }
            Object.Destroy(headLook);
            Object.Destroy(haloLook);
        }

        // A noise body frozen solid where it stands (S15): a lump of clear ice with crystals standing out of it,
        // grown round `at` in a blink, frost spreading on the seabed. It stays until Shatter.
        public Transform Freeze(Vector3 at, Color color, float size)
        {
            var shell = Parts.Shape("Ice", parent, IceMesh(), SeaArt.Ice(new Color(color.r, color.g, color.b, 0.85f))).transform;
            shell.SetPositionAndRotation(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            shell.localScale = Vector3.zero;
            Anim.Tween(0.14f, t =>
            {
                if (shell != null) shell.localScale = Vector3.one * (size * t);
            }, Ease.OutBack).Forget();
            Ring(new Vector3(at.x, 0.05f, at.z), Vector3.up, color, size * 1.8f, 0.5f);
            Flash(at, Color.Lerp(color, Color.white, 0.5f), size * 2.4f, 0.22f);
            Sparks(at, Color.white, 16, 3.5f, 0.07f);
            Mist(at + Vector3.down * 0.5f, color, 6, size * 1.4f, 0.22f, true);
            Light(at, color * 2.2f, 6f, 0.5f);
            return shell;
        }

        // The ice round a frozen noise body burst apart: splinters flung out tumbling, glints, a flash and a ring.
        public void Shatter(Transform shell, Color color)
        {
            if (shell == null) return;
            var at = shell.position;
            var size = Mathf.Max(0.5f, shell.localScale.x);
            var look = shell.GetComponent<MeshRenderer>().sharedMaterial;
            Object.Destroy(shell.gameObject);
            Object.Destroy(look);
            Shards(at, Mathf.RoundToInt(36f * size), 7.5f * size);
            Sparks(at, Color.white, 22, 9f, 0.08f);
            Embers(at, color, 18, 5f, 0.25f);
            // The body inside flashes white as it breaks, and the blow's own Impact lands with it: this keeps to a
            // small white heart, its colour round it and needles that go white only near the middle.
            Flash(at, Color.white, size * 0.8f, 0.14f, 1.6f);
            Flash(at, color, size * 2.8f, 0.3f, 0.9f);
            Star(at, color, size * 3f, 0.25f, Random.Range(0f, 90f), 1.6f, true);
            Ring(at, (camera.transform.position - at).normalized, color, size * 2.2f, 0.4f, 1.2f);
            Light(at, color * 2.6f, 7f, 0.4f);
        }

        // Splinters of the ice itself flung out of `at` (clear, catching the light facet by facet; the sparks with
        // them are the glints), turning over as they fall and shrinking away at the end.
        void Shards(Vector3 at, int count, float speed)
        {
            shardLook ??= SeaArt.Ice(new Color(0.55f, 0.9f, 1f, 0.9f));
            var ps = Burst("Shards", at, count, 0.6f, 1.1f, speed, 0.9f, shardLook);
            var main = ps.main;
            main.startRotation3D = true;
            var any = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationX = any;
            main.startRotationY = any;
            main.startRotationZ = any;
            main.gravityModifier = 1.6f;
            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            var turn = new ParticleSystem.MinMaxCurve(-9f, 9f);
            spin.x = turn;
            spin.y = turn;
            spin.z = turn;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Shard();
            renderer.alignment = ParticleSystemRenderSpace.World;
            ps.Play();
        }

        // A splinter of ice a third of a metre long, white at its point.
        Mesh Shard()
        {
            if (shard != null) return shard;
            // Each face its own three corners, so each is lit flat and catches the light on its own.
            Vector3 a = new Vector3(0f, 0.18f, 0f), b = new Vector3(-0.06f, -0.08f, 0f), c = new Vector3(0.07f, -0.12f, 0f), d = new Vector3(0f, -0.02f, 0.04f);
            var vertices = new List<Vector3> { a, b, c, a, c, d, a, d, b, b, d, c };
            var triangles = new List<int>();
            for (var i = 0; i < vertices.Count; i++) triangles.Add(i);
            return shard = Build("Shard", vertices, null, triangles);
        }

        // A lump of ice a metre and a half across with flat facets: a ball knocked about, each point pushed in or
        // out by where it is (so its seams stay shut), and seven crystals standing out of it at angles.
        Mesh IceMesh()
        {
            if (ice != null) return ice;
            var vertices = new List<Vector3>();
            void Add(Mesh part, Matrix4x4 at, System.Func<Vector3, Vector3> bend)
            {
                var points = part.vertices;
                foreach (var i in part.triangles) vertices.Add(at.MultiplyPoint3x4(bend(points[i])));
                Object.Destroy(part);
            }
            Add(SeaArt.Sphere(0.78f, 0f, 9, 6), Matrix4x4.Scale(new Vector3(1f, 1.1f, 0.95f)),
                p => p * (0.86f + 0.28f * DungeonWorld.Hash(Mathf.RoundToInt(p.x * 50f) + 200, Mathf.RoundToInt(p.y * 50f) * 3 + Mathf.RoundToInt(p.z * 50f) + 400)));
            for (var i = 0; i < 7; i++)
            {
                var yaw = i * 360f / 7f + DungeonWorld.Hash(i, 11) * 30f;
                var pitch = Mathf.Lerp(-15f, 65f, DungeonWorld.Hash(i, 23));
                var dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                var crystal = SeaArt.Cone(Mathf.Lerp(0.14f, 0.24f, DungeonWorld.Hash(i, 37)), Mathf.Lerp(0.55f, 0.9f, DungeonWorld.Hash(i, 41)), 6);
                Add(crystal, Matrix4x4.TRS(dir * 0.42f, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one), p => p);
            }
            var triangles = new List<int>(vertices.Count);
            for (var i = 0; i < vertices.Count; i++) triangles.Add(i);
            return ice = Build("Ice", vertices, null, triangles);
        }

        // 深海潜流 (S16): a whirlpool opening on the seabed round `centre`, `radius` out: arms of light winding in on
        // the floor, currents wound up out of it in a spout, crystals and bubbles carried round and up. `whirl`
        // hears how far it has gone (0 to 1) every frame, for the stage to carry the noise bodies round in it.
        // Done after `seconds`, as it bursts into light; it falls away by itself after that.
        public async Awaitable Maelstrom(Vector3 centre, float radius, Color color, float seconds, System.Action<float> whirl)
        {
            if (parent == null) return;
            var root = Parts.Node("Vortex", parent, new Vector3(centre.x, 0.04f, centre.z));
            Mesh floorMesh = Spiral(radius, 6), spoutMesh = Spout(radius, 4.8f, 8), wallMesh = Funnel(radius, 4.2f);
            // Dimmer than the rest: seen through the spout the bands lie over one another many deep.
            var deep = new Color(0.12f, 0.35f, 0.95f);
            Material floorLook = new Material(white) { color = color }, spoutLook = new Material(white) { color = color * 0.6f };
            var underLook = new Material(glare) { color = deep };
            // The wall of water wound up round it all: bands of current winding up it, the tile scrolling.
            var wallLook = SeaArt.Glow(SeaArt.Bands, color * 0.22f);
            wallLook.SetVector("_Scroll", new Vector4(0.3f, -1.1f, 0f, 0f));
            wallLook.mainTextureScale = new Vector2(2f, 1.5f);
            var under = Parts.Shape("Under", root, quad, underLook).transform;
            under.localRotation = Quaternion.Euler(90f, 0f, 0f);
            under.localScale = Vector3.zero;
            var lamp = Lamp(root.position + Vector3.up * 1.5f, Color.clear, radius * 2.2f);
            var floor = Parts.Shape("Whirl", root, floorMesh, floorLook).transform;
            var spout = Parts.Shape("Spout", root, spoutMesh, spoutLook).transform;
            var wall = Parts.Shape("Wall", root, wallMesh, wallLook).transform;
            floor.localScale = spout.localScale = wall.localScale = Vector3.zero;
            var crystals = Swirl(root, radius, color, false);
            var bubbles = Swirl(root, radius, color, true);
            Ring(root.position, Vector3.up, color, radius * 1.2f, 0.6f);
            var turned = 0f;
            await Anim.Tween(seconds, t =>
            {
                if (root == null) return;
                var open = Ease.OutCubic(Mathf.Clamp01(t / 0.25f));
                turned += Time.unscaledDeltaTime * Mathf.Lerp(160f, 480f, t);
                floor.localRotation = Quaternion.Euler(0f, turned, 0f);
                floor.localScale = new Vector3(open, 1f, open);
                spout.localRotation = Quaternion.Euler(0f, turned * 1.4f, 0f);
                spout.localScale = new Vector3(open, Ease.OutCubic(Mathf.Clamp01((t - 0.1f) / 0.4f)), open);
                wall.localRotation = Quaternion.Euler(0f, turned * 0.6f, 0f);
                wall.localScale = new Vector3(open, Ease.OutCubic(Mathf.Clamp01((t - 0.05f) / 0.45f)), open);
                wallLook.color = color * (0.22f + 0.05f * Mathf.Sin(t * 41f));
                floorLook.color = color * (0.8f + 0.2f * Mathf.Sin(t * 37f));
                under.localScale = Vector3.one * (radius * 3f * open);
                underLook.color = deep * (0.9f + 0.15f * Mathf.Sin(t * 23f));
                lamp.Color = color * (open * (0.8f + 0.15f * Mathf.Sin(t * 31f)));
                whirl?.Invoke(t);
            }, Ease.Linear, () => root == null);
            Out(lamp);
            if (root != null)
            {
                crystals.Stop();
                bubbles.Stop();
                Flash(centre + Vector3.up * 1.4f, Color.white, radius * 0.9f, 0.25f, 2.4f);
                Flash(centre + Vector3.up * 1.4f, color, radius * 1.6f, 0.45f, 1.2f);
                Ring(root.position, Vector3.up, Color.white, radius * 1.6f, 0.5f);
                Light(centre + Vector3.up * 1.4f, color * 3f, radius * 2.5f, 0.6f);
            }
            Fall().Forget();

            async Awaitable Fall()
            {
                await Anim.Tween(0.45f, t =>
                {
                    if (root == null) return;
                    floor.localScale = new Vector3(1f + 0.3f * t, 1f, 1f + 0.3f * t);
                    spout.localScale = new Vector3(1f + 0.4f * t, 1f - t, 1f + 0.4f * t);
                    wall.localScale = new Vector3(1f + 0.5f * t, 1f - t, 1f + 0.5f * t);
                    floorLook.color = color * (0.8f * (1f - t));
                    spoutLook.color = color * (0.6f * (1f - t));
                    wallLook.color = color * (0.22f * (1f - t));
                    underLook.color = deep * (1f - t);
                }, Ease.OutCubic, () => root == null);
                await Anim.Wait(1.2f); // the last crystals and bubbles out
                if (root != null) Object.Destroy(root.gameObject);
                foreach (var o in new Object[] { floorMesh, spoutMesh, wallMesh, floorLook, spoutLook, wallLook, underLook }) Object.Destroy(o);
            }
        }

        // Arms of light winding in to the middle of a whirlpool on the seabed, `radius` out, for the Glow shader:
        // bright along their middles, gone at their tips and edges.
        static Mesh Spiral(float radius, int arms)
        {
            const int Steps = 36;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (var a = 0; a < arms; a++)
            {
                var start = vertices.Count;
                for (var i = 0; i <= Steps; i++)
                {
                    var u = (float)i / Steps;
                    var angle = ((float)a / arms + u * 0.85f) * Mathf.PI * 2f;
                    var r = radius * (0.06f + 0.94f * u);
                    var width = radius * 0.2f * Mathf.Sin(u * Mathf.PI) + 0.04f;
                    var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(dir * Mathf.Max(0f, r - width * 0.5f));
                    vertices.Add(dir * r);
                    vertices.Add(dir * (r + width * 0.5f));
                    colors.Add(Color.clear);
                    // Sin(π) comes out a hair below 0, and a negative to a fractional power is NaN (a speck the glow
                    // would blow up into a disc).
                    colors.Add(new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f) * (1f - 0.4f * u)));
                    colors.Add(Color.clear);
                    if (i == Steps) continue;
                    for (var row = 0; row < 2; row++)
                    {
                        var k = start + i * 3 + row;
                        triangles.AddRange(new[] { k, k + 3, k + 4, k, k + 4, k + 1 });
                    }
                }
            }
            return Build("Whirl", vertices, colors, triangles);
        }

        // Currents wound up out of a whirlpool: `bands` of light going round and up, `radius` across at the
        // seabed and narrowing toward `height`, gone at both ends and at their edges.
        static Mesh Spout(float radius, float height, int bands)
        {
            const int Steps = 48;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (var b = 0; b < bands; b++)
            {
                var start = vertices.Count;
                for (var i = 0; i <= Steps; i++)
                {
                    var u = (float)i / Steps;
                    var angle = ((float)b / bands + u * 1.5f) * Mathf.PI * 2f;
                    var middle = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius * Mathf.Lerp(0.9f, 0.4f, u)) + Vector3.up * (height * u);
                    var half = Vector3.up * Mathf.Lerp(0.4f, 0.9f, u);
                    vertices.Add(middle - half);
                    vertices.Add(middle);
                    vertices.Add(middle + half);
                    colors.Add(Color.clear);
                    colors.Add(new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.6f)));
                    colors.Add(Color.clear);
                    if (i == Steps) continue;
                    for (var row = 0; row < 2; row++)
                    {
                        var k = start + i * 3 + row;
                        triangles.AddRange(new[] { k, k + 3, k + 4, k, k + 4, k + 1 });
                    }
                }
            }
            return Build("Spout", vertices, colors, triangles);
        }

        // The wall of a whirlpool: a funnel `radius` wide at the seabed narrowing toward `height`, gone at its
        // foot and its top, its u once round and v up it for the bands of current to scroll along.
        static Mesh Funnel(float radius, float height)
        {
            const int Round = 40, Rows = 12;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var j = 0; j <= Rows; j++)
            {
                var v = (float)j / Rows;
                var r = radius * Mathf.Lerp(1.05f, 0.5f, Ease.OutCubic(v));
                var a = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), 0.8f) * 0.45f;
                for (var i = 0; i <= Round; i++)
                {
                    var angle = (float)i / Round * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * r, height * v, Mathf.Sin(angle) * r));
                    colors.Add(new Color(1f, 1f, 1f, a));
                    uvs.Add(new Vector2((float)i / Round, v));
                    if (i == Round || j == Rows) continue;
                    var k = j * (Round + 1) + i;
                    triangles.AddRange(new[] { k, k + Round + 1, k + Round + 2, k, k + Round + 2, k + 1 });
                }
            }
            var mesh = Build("Funnel", vertices, colors, triangles);
            mesh.SetUVs(0, uvs);
            return mesh;
        }

        // Ice crystals (or bubbles) carried round and up by a whirlpool `radius` out under `holder`.
        ParticleSystem Swirl(Transform holder, float radius, Color color, bool bubbles)
        {
            var go = new GameObject(bubbles ? "Bubbles" : "Glints");
            go.transform.SetParent(holder, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = 0f;
            main.startSize = bubbles ? new ParticleSystem.MinMaxCurve(0.15f, 0.45f) : new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startColor = bubbles ? new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.95f, 1f, 0.8f)) : new ParticleSystem.MinMaxGradient(color, Color.white);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = bubbles ? 90f : 140f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.85f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            // The velocity module wants its curves all of one kind.
            var flow = ps.velocityOverLifetime;
            flow.enabled = true;
            flow.space = ParticleSystemSimulationSpace.Local;
            flow.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.y = new ParticleSystem.MinMaxCurve(0.8f, 3.5f);
            flow.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.orbitalY = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            flow.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.radial = new ParticleSystem.MinMaxCurve(-1.2f, -0.3f);
            Fading(ps);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = bubbles ? bubble : glint;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ 深度思考

        // What she says to herself as she thinks it through, DeepSeek's way (嗯……、等等、所以).
        static readonly string[] Musings = { "嗯……", "等等", "所以", "假设", "→", "？", "也就是说", "∴", "再想想", "0", "1", "如果" };

        // 深度思考: words of her reasoning in cold light round `head`, one after another for `seconds`, each coming up
        // in its own place on either side of her (in the picture's plane, so none is hidden behind another), rising
        // a little and fading; lit past the glow's threshold, so they shine.
        public void Thoughts(Vector3 head, Color color, float seconds)
        {
            const float Life = 0.7f;
            const int Count = 12;
            var root = Parts.Node("Thought", parent, head);
            var words = new TextMesh[Count];
            var first = Random.Range(0, Musings.Length);
            var tint = Color.Lerp(color, Color.white, 0.4f);
            gain ??= new MaterialPropertyBlock();
            gain.SetFloat(GainId, 1.7f);
            for (var i = 0; i < Count; i++)
            {
                words[i] = SeaArt.Text("Word", root, Musings[(first + i * 5) % Musings.Length], 0.2f, Color.clear);
                words[i].gameObject.AddComponent<Billboard>();
                words[i].GetComponent<MeshRenderer>().SetPropertyBlock(gain);
            }
            Run(root.gameObject, seconds + Life, t =>
            {
                var now = t * (seconds + Life);
                var right = camera.transform.right;
                var up = camera.transform.up;
                for (var i = 0; i < Count; i++)
                {
                    var k = (now - i * seconds / Count) / Life;
                    if (k < 0f || k > 1f)
                    {
                        words[i].color = Color.clear;
                        continue;
                    }
                    var spot = ThoughtSpots[i % ThoughtSpots.Length];
                    words[i].transform.position = root.position + right * spot.x + up * (spot.y + k * 0.2f);
                    words[i].color = new Color(tint.r, tint.g, tint.b, Mathf.Sin(k * Mathf.PI));
                }
            });
        }

        // Where the thoughts come up round her (metres across and up the picture from over her head): one side, then
        // the other, from her head down to her waist, as many as are up at once (Life over the gap between them),
        // each clear of the others as they rise, none over her face or above the thinking shot's frame.
        static readonly Vector2[] ThoughtSpots =
        {
            new Vector2(0.8f, -0.1f), new Vector2(-0.85f, -0.3f), new Vector2(0.95f, -0.5f),
            new Vector2(-1f, -0.7f), new Vector2(0.8f, -0.9f), new Vector2(-0.85f, -1.1f),
        };

        // The charge of 深度思考 held until her next skill: two dials of cold light turning slowly either way under
        // `follow`'s feet, their marks past the glow's threshold, and motes rising round them. Gone once released.
        public Held Aura(Transform follow, Color color)
        {
            var root = Parts.Node("Charge", follow, new Vector3(0f, 0.06f, 0f));
            var look = SeaArt.Glow(SeaArt.Dial, color);
            var dial = Parts.Shape("Dial", root, quad, look).transform;
            dial.localScale = Vector3.one * 2.4f;
            var inner = Parts.Shape("Dial", root, quad, look).transform;
            inner.localScale = Vector3.one * 1.3f;
            var motes = Rising(root, color);
            var held = new Held();
            Hold().Forget();
            return held;

            async Awaitable Hold()
            {
                var t = 0f;
                while (!held.Released && root != null)
                {
                    t += Time.unscaledDeltaTime;
                    dial.localRotation = Quaternion.Euler(90f, 0f, t * 30f);
                    inner.localRotation = Quaternion.Euler(90f, 0f, -t * 45f);
                    look.color = color * ((1.15f + 0.2f * Mathf.Sin(t * 3f)) * Mathf.Clamp01(t * 4f));
                    await Awaitable.NextFrameAsync();
                }
                if (root != null)
                {
                    motes.Stop();
                    await Anim.Tween(0.3f, k =>
                    {
                        if (root == null) return;
                        dial.localScale = Vector3.one * (2.4f + k);
                        inner.localScale = Vector3.one * (1.3f + k);
                        look.color = color * (1.15f * (1f - k));
                    });
                    if (root != null) Object.Destroy(root.gameObject);
                }
                Object.Destroy(look);
            }
        }

        // Whose turn it is (BattleStage.Ready): a soft disc of light with a bright rim under `follow`'s feet,
        // breathing, a ripple going out from it every RippleEvery seconds, and a few motes rising round it (flakes of
        // ice for DeepSeek, `ice`). Quieter than 深度思考's dials, which can stand on it at once: under the glow's
        // threshold but for the ripple's leading edge. Fades in, and out once released.
        const float RippleEvery = 1.6f;
        public Held Turn(Transform follow, Color color, bool ice)
        {
            var root = Parts.Node("Turn", follow, new Vector3(0f, 0.04f, 0f));
            var look = SeaArt.Glow(SeaArt.Ring, color);
            var disc = Parts.Shape("Disc", root, quad, look).transform;
            disc.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.localScale = Vector3.one * 1.5f;
            var rippleLook = SeaArt.Glow(SeaArt.Ring, color);
            var ripple = Parts.Shape("Ripple", root, quad, rippleLook).transform;
            ripple.localRotation = disc.localRotation;
            var motes = ice ? Rising(root, color, 6f, flake, 0.5f, 0.35f) : Rising(root, color, 9f, null, 0.5f);
            var held = new Held();
            Hold().Forget();
            return held;

            async Awaitable Hold()
            {
                var t = 0f;
                var gain = Calm ? 0.7f : 1f;
                while (!held.Released && root != null)
                {
                    t += Time.unscaledDeltaTime;
                    var shown = Mathf.Clamp01(t * 3f);
                    look.color = Hot(color, gain * (0.75f + 0.15f * Mathf.Sin(t * 2.4f)), shown);
                    var k = t % RippleEvery / RippleEvery;
                    ripple.localScale = Vector3.one * Mathf.Lerp(1.5f, 2.6f, Ease.OutCubic(k));
                    rippleLook.color = Hot(color, gain * 1.3f, shown * Mathf.Pow(1f - k, 1.5f));
                    await Awaitable.NextFrameAsync();
                }
                if (root != null)
                {
                    motes.Stop();
                    var from = look.color.a;
                    var rippled = rippleLook.color.a;
                    await Anim.Tween(0.25f, k =>
                    {
                        if (root == null) return;
                        look.color = new Color(look.color.r, look.color.g, look.color.b, from * (1f - k));
                        rippleLook.color = new Color(rippleLook.color.r, rippleLook.color.g, rippleLook.color.b, rippled * (1f - k));
                    });
                    if (root != null) Object.Destroy(root.gameObject);
                }
                Object.Destroy(look);
                Object.Destroy(rippleLook);
            }
        }

        // Motes rising round `holder` all the time, until stopped: `rate` a second, drawn with `look` (glints of
        // light if none) `size` times their usual size, off a ring `radius` metres out.
        ParticleSystem Rising(Transform holder, Color color, float rate = 34f, Material look = null, float radius = 0.6f, float size = 1f)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(holder, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * size, 0.24f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var flow = ps.velocityOverLifetime;
            flow.enabled = true;
            flow.space = ParticleSystemSimulationSpace.Local;
            flow.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.y = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            flow.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fading(ps);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = look != null ? look : mote ??= SeaArt.Glow(SeaArt.Flare, Color.white * 1.5f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ 防御

        // 防御: a bowed shield of hexagonal cells of light standing at `at` toward `facing`, raised in a flash;
        // faint while it is held, flaring where a blow lands on it (Flare). Gone once released.
        public Held Barrier(Vector3 at, Vector3 facing, Color color)
        {
            var root = Parts.Node("Shield", parent);
            root.SetPositionAndRotation(at, Quaternion.LookRotation(facing));
            root.localScale = Vector3.zero;
            var look = new Material(white) { color = color };
            var mesh = Cells(0.95f, 1.15f, 0.17f);
            Parts.Shape("Cells", root, mesh, look);
            var held = new Held();
            var flare = 0.8f;
            held.OnFlare = () =>
            {
                flare = 1.1f;
                if (root == null) return;
                Ring(root.position, facing, color, 1.3f, 0.35f);
                Sparks(root.position, Color.Lerp(color, Color.white, 0.5f), 16, 5f);
                Light(root.position, color * 1.6f, 4f, 0.3f);
            };
            Hold().Forget();
            return held;

            async Awaitable Hold()
            {
                var t = 0f;
                while (!held.Released && root != null)
                {
                    var dt = Time.unscaledDeltaTime;
                    t += dt;
                    flare = Mathf.MoveTowards(flare, 0f, dt * 2.5f);
                    root.localScale = Vector3.one * Ease.OutBack(Mathf.Clamp01(t / 0.25f));
                    look.color = color * (1.2f + 0.1f * Mathf.Sin(t * 5f) + flare); // its lines' hearts past the glow's threshold
                    await Awaitable.NextFrameAsync();
                }
                if (root != null)
                {
                    await Anim.Tween(0.25f, k =>
                    {
                        if (root == null) return;
                        root.localScale = Vector3.one * (1f + 0.15f * k);
                        look.color = color * (1.2f * (1f - k));
                    });
                    if (root != null) Object.Destroy(root.gameObject);
                }
                Object.Destroy(look);
                Object.Destroy(mesh);
            }
        }

        // Hexagonal cells of light filling an oval `halfWidth` by `halfHeight` in the plane facing +z, bowed out
        // toward +z like a shield: each an outline and a faint fill, some brighter than others, dimmer to the rim.
        static Mesh Cells(float halfWidth, float halfHeight, float cell)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            Vector3 Bow(Vector2 p) => new Vector3(p.x, p.y, -0.4f * ((p.x / halfWidth) * (p.x / halfWidth) + (p.y / halfHeight) * (p.y / halfHeight)));
            Vector2 Corner(int k) => new Vector2(Mathf.Cos((k * 60f + 30f) * Mathf.Deg2Rad), Mathf.Sin((k * 60f + 30f) * Mathf.Deg2Rad));
            float dx = cell * Mathf.Sqrt(3f), dy = cell * 1.5f;
            for (var row = -12; row <= 12; row++)
            for (var col = -12; col <= 12; col++)
            {
                var c = new Vector2(col * dx + (row & 1) * dx / 2f, row * dy);
                var e = (c.x / halfWidth) * (c.x / halfWidth) + (c.y / halfHeight) * (c.y / halfHeight);
                if (e > 1f) continue;
                var lit = (0.55f + 0.45f * DungeonWorld.Hash(row + 50, col + 80)) * (0.35f + 0.65f * (1f - e));
                var centre = vertices.Count;
                vertices.Add(Bow(c));
                colors.Add(new Color(1f, 1f, 1f, lit * 0.18f));
                for (var k = 0; k < 6; k++)
                {
                    vertices.Add(Bow(c + Corner(k) * (cell * 0.84f)));
                    colors.Add(new Color(1f, 1f, 1f, lit * 0.1f));
                }
                for (var k = 0; k < 6; k++) triangles.AddRange(new[] { centre, centre + 1 + k, centre + 1 + (k + 1) % 6 });
                var band = vertices.Count;
                for (var k = 0; k < 6; k++)
                {
                    vertices.Add(Bow(c + Corner(k) * (cell * 0.84f)));
                    colors.Add(new Color(1f, 1f, 1f, lit * 0.15f));
                    vertices.Add(Bow(c + Corner(k) * (cell * 0.95f)));
                    colors.Add(new Color(1f, 1f, 1f, lit));
                }
                for (var k = 0; k < 6; k++)
                {
                    int i0 = band + k * 2, j0 = band + (k + 1) % 6 * 2;
                    triangles.AddRange(new[] { i0, j0, j0 + 1, i0, j0 + 1, i0 + 1 });
                }
            }
            return Build("Cells", vertices, colors, triangles);
        }

        // ------------------------------------------------------------------ thrown things

        // A 冰镇汽水 (or 辣条, alight) in the thrower's `hand` for `hold` seconds, held out along the forearm, then
        // thrown: arcing from there to `to`, turning over as it goes, foam (or embers) trailing it, growing as it
        // leaves the hand big enough to read across the field. Named for what it is (Can, Strip) only once thrown.
        // Done when it lands, for the burst there (Fizz, Blaze).
        public async Awaitable Toss(bool fire, Transform hand, float hold, Vector3 to, Color color, float seconds)
        {
            if (parent == null) return;
            var thing = Parts.Node("Held", parent);
            var body = Parts.Node("Body", thing);
            Vector3 Along() => (hand.position - hand.parent.position).normalized;
            Vector3 InHand() => hand.position + Along() * 0.07f;
            thing.position = InHand();
            Material flame = null;
            Transform tail = null;
            Glim lamp = null;
            if (fire)
            {
                strip ??= SeaArt.Box(new Vector3(0.1f, 0.03f, 0.36f), 0.012f);
                stripLook ??= SeaArt.Toon(Palette.Hex("#E2421B"), Palette.Hex("#8A1E0A"), new Color(1f, 0.6f, 0.2f, 0.8f), Palette.Hex("#401000"), 0.01f);
                Parts.Shape("Stick", body, strip, stripLook);
                // A flame behind it as it flies, small enough for the strip to show in front of it; in the hand a
                // small dim one, so the thrower isn't lost in its glare and light while winding up.
                flame = new Material(glare) { color = color * 0.6f };
                tail = Parts.Shape("Flame", thing, quad, flame).transform;
                tail.gameObject.AddComponent<Billboard>();
                tail.localScale = Vector3.one * 0.25f;
            }
            else
            {
                can ??= SeaArt.Cylinder(0.075f, 0.075f, 0.24f, 16);
                rim ??= SeaArt.Cylinder(0.068f, 0.068f, 0.014f, 16);
                canLook ??= SeaArt.Toon(Palette.Hex("#6FD3FF"), Palette.Hex("#2A7FBF"), new Color(1f, 1f, 1f, 0.6f), Palette.Hex("#103050"), 0.01f);
                rimLook ??= SeaArt.Toon(Palette.Hex("#E8EEF5"), Palette.Hex("#9AA6B6"), new Color(1f, 1f, 1f, 0.6f), default, 0.01f);
                Parts.Shape("Tin", body, can, canLook, new Vector3(0f, -0.12f, 0f));
                Parts.Shape("Top", body, rim, rimLook, new Vector3(0f, 0.12f, 0f));
            }
            // In the hand as the arm winds back and comes over: upright across the fist, the strip along the forearm.
            await Anim.Tween(hold, t =>
            {
                if (thing == null || hand == null) return;
                thing.position = InHand();
                body.rotation = Quaternion.LookRotation(Along());
                if (tail != null) tail.position = thing.position - Along() * 0.12f;
            }, Ease.Linear, () => thing == null || hand == null);
            if (thing != null && hand != null)
            {
                // Out of the hand: the flame (or foam) takes hold, embers and light with it.
                thing.name = fire ? "Strip" : "Can";
                if (fire)
                {
                    tail.localScale = Vector3.one * 0.55f;
                    var rise = Shed(thing, color, 30f, spark).main;
                    rise.gravityModifier = -0.4f;
                    lamp = Lamp(thing.position, color * 1.4f, 4.5f);
                }
                else Shed(thing, new Color(0.8f, 0.97f, 1f, 0.9f), 30f, bubble);
                Trail(thing.gameObject, color, 0.14f, 0.2f);
                var from = thing.position;
                var held = body.rotation;
                var run = to - from;
                var axis = Vector3.Cross(run, Vector3.up).normalized;
                // A lob low enough to stay in the frame from down beside the thrower.
                var height = 0.45f + run.magnitude * 0.07f;
                await Anim.Tween(seconds, t =>
                {
                    if (thing == null) return;
                    thing.position = Vector3.Lerp(from, to, t) + Vector3.up * (height * 4f * t * (1f - t));
                    body.rotation = Quaternion.AngleAxis(t * 1080f, axis) * held;
                    body.localScale = Vector3.one * Mathf.Lerp(1f, 1.6f, Mathf.Clamp01(t / 0.15f));
                    if (flame != null)
                    {
                        var going = run + Vector3.up * (height * 4f * (1f - 2f * t)); // the arc's way at t
                        tail.position = thing.position - going.normalized * 0.22f;
                        flame.color = color * (1.15f + 0.25f * Mathf.Sin(t * 60f));
                    }
                    if (lamp != null) lamp.At = thing.position;
                }, Ease.Linear, () => thing == null);
            }
            if (lamp != null) Out(lamp);
            if (thing != null) Object.Destroy(thing.gameObject);
            if (flame != null) Object.Destroy(flame);
        }

        // The can bursting where it hits: soda foaming up out of it, spray, a cold mist and frost on the seabed.
        public void Fizz(Vector3 at, Color color)
        {
            var foam = Burst("Fizz", at, 50, 0.6f, 1.2f, 7.5f, 0.35f, bubble);
            var main = foam.main;
            main.startColor = new Color(0.8f, 0.97f, 1f, 0.9f);
            main.gravityModifier = 0.9f;
            var shape = foam.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            foam.Play();
            Sparks(at, Color.white, 26, 6f, 0.07f);
            Flash(at, Color.white, 1.4f, 0.16f, 2.6f);
            Flash(at, color, 2.4f, 0.4f, 1.2f);
            Mist(at, new Color(0.85f, 0.97f, 1f), 10, 1.6f, 0.35f);
            Ring(at, (camera.transform.position - at).normalized, color, 1.8f, 0.35f);
            Light(at, color * 2.4f, 6f, 0.55f);
            Frost(at, color, 0.6f).Forget();
        }

        // The 辣条 bursting into a ball of flame where it hits: a white-hot flash, billows of flame rolling over
        // (SeaArt.PuffSheet) up and out from yellow through orange to deep red, dark smoke rising behind them,
        // embers going up, a ring of heat across the seabed, and the field lit orange.
        public void Blaze(Vector3 at, Color color)
        {
            var fire = Burst("Blaze", at, 34, 0.35f, 0.8f, 3.2f, 1.3f, flameSheet);
            var main = fire.main;
            main.startColor = Color.white;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.7f;
            var shape = fire.shape;
            shape.radius = 0.3f;
            var spin = fire.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var grow = fire.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.3f));
            var shade = fire.colorOverLifetime;
            shade.enabled = true;
            var burn = new Gradient();
            burn.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0f), new GradientColorKey(new Color(1f, 0.7f, 0.25f), 0.2f),
                    new GradientColorKey(color, 0.5f), new GradientColorKey(new Color(0.6f, 0.08f, 0.04f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.45f), new GradientAlphaKey(0f, 1f) });
            shade.color = new ParticleSystem.MinMaxGradient(burn);
            var roll = fire.textureSheetAnimation;
            roll.enabled = true;
            roll.numTilesX = 4;
            roll.numTilesY = 4;
            roll.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.99f));
            roll.cycleCount = 1;
            fire.Play();
            var soot = Burst("Smoke", at + Vector3.up * 0.3f, 14, 0.9f, 1.6f, 2f, 1.5f, smoke);
            var dark = soot.main;
            dark.startColor = new Color(0.07f, 0.05f, 0.06f, 0.6f);
            dark.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dark.gravityModifier = -0.25f;
            var swell = soot.sizeOverLifetime;
            swell.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.6f));
            Fading(soot);
            soot.Play();
            Flash(at, new Color(1f, 0.92f, 0.7f), 2f, 0.18f, 3f);
            Flash(at, color, 3.4f, 0.4f, 1.3f);
            Sparks(at, new Color(1f, 0.75f, 0.35f), 30, 7f, 0.07f);
            Embers(at, new Color(1f, 0.6f, 0.2f), 26, 3.5f, -0.5f);
            Ring(new Vector3(at.x, 0.05f, at.z), Vector3.up, color, 2.6f, 0.55f);
            Ring(at, (camera.transform.position - at).normalized, new Color(1f, 0.85f, 0.5f), 1.8f, 0.3f);
            Light(at, color * 3.2f, 8f, 0.8f);
        }

        // ------------------------------------------------------------------ the noise bodies' attacks

        static readonly string[] Bait = { "震惊！", "点击领取", "恭喜中奖", "限时免费" };

        // 标题党型's 震惊！: pop-up windows flung at `to` one after another off the noise body, each from where `leave`
        // has it come off as it goes (its own pop-ups, NoiseLook.Throw), carried along with the body a moment before
        // it breaks away, then growing and tumbling as it comes, bursting into the noise body's pixels (colours a and
        // b) where it hits. Done as the last hits.
        public async Awaitable Popups(System.Func<int, NoiseLook.Thrown> leave, Vector3 to, int count, Color a, Color b)
        {
            var flights = new List<Awaitable>();
            for (var i = 0; i < count; i++) flights.Add(Popup(leave, i, to + Random.insideUnitSphere * 0.35f, i * 0.1f, a, b));
            foreach (var flight in flights) await flight;
        }

        async Awaitable Popup(System.Func<int, NoiseLook.Thrown> leave, int k, Vector3 to, float delay, Color a, Color b)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var start = leave(k);
            var from = start.Position;
            var window = Parts.Node("Popup", parent);
            window.SetPositionAndRotation(from, start.Rotation);
            window.localScale = Vector3.one * start.Scale;
            Window(window, start.Words ?? Bait[k % Bait.Length]);
            var tilt = Random.Range(-25f, 25f);
            var spin = Random.Range(-160f, 160f);
            var lift = Random.Range(0.2f, 0.7f);
            await Anim.Tween(0.32f, t =>
            {
                if (window == null) return;
                if (start.Anchor != null) from = start.Position;
                var p = t * t * t; // slow off the body, fast at the member
                var tumble = camera.transform.rotation * Quaternion.Euler(0f, 0f, tilt + spin * p);
                window.SetPositionAndRotation(Vector3.Lerp(from, to, p) + Vector3.up * (Mathf.Sin(p * Mathf.PI) * lift),
                    Quaternion.Slerp(start.Rotation, tumble, Mathf.Clamp01(t / 0.3f)));
                window.localScale = Vector3.one * Mathf.Lerp(start.Scale, 1.6f, p);
            }, Ease.Linear, () => window == null);
            if (window == null) return;
            Object.Destroy(window.gameObject);
            Pixels(to, a, b, 22, 4.5f);
            Flash(to, a, 1.8f, 0.2f);
            Ring(to, (camera.transform.position - to).normalized, b, 1f, 0.3f);
            Light(to, a * 1.8f, 4f, 0.3f);
        }

        // A pop-up window like those round a 标题党型 (NoiseLook): its chrome, a red title bar, a dark page and
        // the bait on it in yellow.
        void Window(Transform holder, string words)
        {
            adChrome ??= SeaArt.Box(new Vector3(0.7f, 0.48f, 0.02f));
            adPage ??= SeaArt.Box(new Vector3(0.62f, 0.3f, 0.02f));
            adBar ??= SeaArt.Box(new Vector3(0.7f, 0.09f, 0.03f));
            chrome ??= SeaArt.Toon(Palette.Hex("#F2F4FA"), Palette.Hex("#B8BFD0"), new Color(1f, 1f, 1f, 0.3f), Palette.Hex("#40444F"), 0.008f);
            page ??= SeaArt.Toon(Palette.Hex("#1B1230"), Palette.Hex("#120A22"), default, Palette.Hex("#0E0820"), 0f);
            bar ??= SeaArt.Toon(Palette.Red, Palette.Red, default, Palette.Red * 0.6f, 0f);
            Parts.Shape("Chrome", holder, adChrome, chrome);
            Parts.Shape("Page", holder, adPage, page, new Vector3(0f, -0.05f, -0.012f));
            Parts.Shape("Bar", holder, adBar, bar, new Vector3(0f, 0.195f, -0.006f));
            SeaArt.Text("Words", holder, words, 0.13f, Palette.Hex("#FFE14A"), new Vector3(0f, -0.05f, -0.03f));
        }

        static readonly string[] Facts = { "99.7%", "据统计", "你知道吗", "第 3 条" };

        // 冷知识型's 你知道吗？: fact cards dealt at `to` one after another off the noise body's stack (`leave`, as
        // Popups), carried along a moment, then spinning flat as they come, each bursting into the noise body's
        // pixels (colours a and b) where it hits. Done as the last hits.
        public async Awaitable Cards(System.Func<int, NoiseLook.Thrown> leave, Vector3 to, int count, Color a, Color b)
        {
            var flights = new List<Awaitable>();
            for (var i = 0; i < count; i++) flights.Add(CardFlight(leave, i, to + Random.insideUnitSphere * 0.3f, i * 0.12f, Facts[i % Facts.Length], a, b));
            foreach (var flight in flights) await flight;
        }

        async Awaitable CardFlight(System.Func<int, NoiseLook.Thrown> leave, int k, Vector3 to, float delay, string words, Color a, Color b)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var start = leave(k);
            var from = start.Position;
            var card = Parts.Node("Card", parent);
            card.SetPositionAndRotation(from, start.Rotation);
            card.localScale = Vector3.one * start.Scale;
            Card(card, words);
            var spin = Random.Range(400f, 720f) * (Random.value < 0.5f ? -1f : 1f);
            var lift = Random.Range(0.3f, 0.8f);
            await Anim.Tween(0.3f, t =>
            {
                if (card == null) return;
                if (start.Anchor != null) from = start.Position;
                var p = t * t * t;
                card.SetPositionAndRotation(Vector3.Lerp(from, to, p) + Vector3.up * (Mathf.Sin(p * Mathf.PI) * lift),
                    Quaternion.Slerp(start.Rotation, camera.transform.rotation * Quaternion.Euler(0f, 0f, spin * p), Mathf.Clamp01(t / 0.3f)));
                card.localScale = Vector3.one * Mathf.Lerp(start.Scale, 1.4f, p);
            }, Ease.Linear, () => card == null);
            if (card == null) return;
            Object.Destroy(card.gameObject);
            Pixels(to, a, b, 18, 4f);
            Flash(to, b, 1.6f, 0.2f);
            Ring(to, (camera.transform.position - to).normalized, b, 0.9f, 0.3f);
            Light(to, b * 1.6f, 4f, 0.3f);
        }

        // An index card like those round a 冷知识型 (NoiseLook): pale, a cold stripe along the top, the fact in ink.
        void Card(Transform holder, string words)
        {
            cardFace ??= SeaArt.Box(new Vector3(0.66f, 0.42f, 0.02f), 0.03f);
            cardStripe ??= SeaArt.Box(new Vector3(0.66f, 0.07f, 0.025f));
            cardLook ??= SeaArt.Toon(Palette.Hex("#F4F8FF"), Palette.Hex("#C3D3E8"), new Color(1f, 1f, 1f, 0.3f), Palette.Hex("#3A4A66"), 0.008f);
            cardInk ??= SeaArt.Toon(Palette.Sea, Palette.Sea, default, Palette.Sea * 0.6f, 0f);
            Parts.Shape("Face", holder, cardFace, cardLook);
            Parts.Shape("Stripe", holder, cardStripe, cardInk, new Vector3(0f, 0.165f, -0.004f));
            SeaArt.Text("Words", holder, words, 0.13f, Palette.Hex("#1E2A44"), new Vector3(0f, -0.04f, -0.03f));
        }

        // 转发型's 转发保平安: three of its own forked arrows shot off it one after another (`leave` gives each as it
        // goes, NoiseLook.Throw), out along the way it pointed and bent up over the field from `from` (the noise body)
        // toward `targets`, each splitting in three at the top of its flight, the copies coming down on them (and the
        // floor round them). Done when all have landed.
        public async Awaitable Forward(Vector3 from, System.Func<int, NoiseLook.Thrown> leave, IList<Vector3> targets, Color color)
        {
            if (parent == null || targets.Count == 0) return;
            var look = new Material(white) { color = Color.Lerp(color, Color.white, 0.25f) };
            var middle = Vector3.zero;
            foreach (var t in targets) middle += t;
            middle /= targets.Count;
            var across = Vector3.Cross(middle - from, Vector3.up).normalized;
            var volleys = new List<Awaitable>();
            for (var i = 0; i < 3; i++)
            {
                var apex = Vector3.Lerp(from, middle, 0.45f) + Vector3.up * (3f + 0.4f * i) + across * ((i - 1) * 1.4f);
                volleys.Add(Volley(leave, apex, targets, i, color, look, i * 0.08f));
            }
            foreach (var volley in volleys) await volley;
            Object.Destroy(look);
        }

        // One arrow off the noise body, a moment straight on the way it pointed, then up to `apex`, where it flashes
        // and splits into three that come down.
        async Awaitable Volley(System.Func<int, NoiseLook.Thrown> leave, Vector3 apex, IList<Vector3> targets, int k, Color color, Material look, float delay)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var start = leave(k);
            var from = start.Position;
            var lead = from + start.Rotation * Vector3.up * 0.6f;
            var go = Parts.Shape("Forward", parent, arrow ??= NoiseLook.ForkedArrow(-0.28f), look).gameObject;
            go.transform.SetPositionAndRotation(from, start.Rotation);
            Trail(go, color, 0.12f, 0.14f);
            var up = Quaternion.FromToRotation(Vector3.up, apex - lead);
            await Anim.Tween(0.38f, t =>
            {
                if (go == null) return;
                const float Out = 0.08f / 0.38f;
                var s = Mathf.Clamp01((t - Out) / (1f - Out));
                go.transform.SetPositionAndRotation(t < Out ? Vector3.Lerp(from, lead, t / Out) : Vector3.Lerp(lead, apex, Ease.OutCubic(s)),
                    Quaternion.Slerp(start.Rotation, up, Mathf.Clamp01(s * 3f)));
            }, Ease.Linear, () => go == null);
            if (go == null) return;
            Object.Destroy(go);
            Flash(apex, color, 1.2f, 0.2f);
            Ring(apex, (camera.transform.position - apex).normalized, color, 0.7f, 0.25f);
            var copies = new List<Awaitable>();
            for (var j = 0; j < 3; j++)
            {
                var target = targets[(k + j) % targets.Count];
                // The third of each three goes into the floor beside them.
                var at = j < 2 ? target + Random.insideUnitSphere * 0.3f : new Vector3(target.x + Random.Range(-1.2f, 1.2f), 0.05f, target.z + Random.Range(-0.8f, 0.8f));
                copies.Add(Arrow(apex, at, look, color, 0.26f, Ease.InCubic, true));
            }
            foreach (var copy in copies) await copy;
        }

        async Awaitable Arrow(Vector3 from, Vector3 to, Material look, Color color, float seconds, System.Func<float, float> ease, bool lands)
        {
            var go = Parts.Shape("Forward", parent, arrow ??= NoiseLook.ForkedArrow(-0.28f), look).gameObject;
            go.transform.SetPositionAndRotation(from, Quaternion.FromToRotation(Vector3.up, to - from));
            Trail(go, color, 0.12f, 0.14f);
            await Anim.Tween(seconds, t =>
            {
                if (go != null) go.transform.position = Vector3.Lerp(from, to, t);
            }, ease, () => go == null);
            if (go == null) return;
            Object.Destroy(go);
            if (!lands) return;
            Flash(to, color, 1.1f, 0.2f);
            Sparks(to, color, 8, 4f);
            Light(to, color * 1.2f, 3.5f, 0.25f);
        }

        // 转发型's 转发到十个群: `count` of its forked arrows shot off it one after another (`leave` gives each as it
        // goes, NoiseLook.Throw), half the size of 转发保平安's, each in a 1.5 m arc over to somewhere within 0.6 m of
        // `to` (where the new noise body comes together), trailing its colour, sparks where it gets there: a new one
        // forwarded into ten groups. Done when the last has got there.
        public async Awaitable Relay(System.Func<int, NoiseLook.Thrown> leave, Vector3 to, int count, Color color)
        {
            if (parent == null) return;
            var look = new Material(white) { color = Color.Lerp(color, Color.white, 0.25f) };
            var hops = new List<Awaitable>();
            for (var k = 0; k < count; k++)
            {
                // Spread evenly round it on a golden-angle spiral, a little up and down.
                var a = k * 2.39996f;
                var r = 0.6f * Mathf.Sqrt((k + 0.5f) / count);
                hops.Add(Hop(leave, k, to + new Vector3(Mathf.Cos(a) * r, ((k * 7) % 5 - 2) * 0.12f, Mathf.Sin(a) * r), color, look, k * 0.03f));
            }
            foreach (var hop in hops) await hop;
            Object.Destroy(look);
        }

        async Awaitable Hop(System.Func<int, NoiseLook.Thrown> leave, int k, Vector3 to, Color color, Material look, float delay)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var from = leave(k).Position;
            var go = Parts.Shape("Relay", parent, arrow ??= NoiseLook.ForkedArrow(-0.28f), look).gameObject;
            go.transform.localScale = Vector3.one * 0.5f;
            Vector3 At(float u) => Vector3.Lerp(from, to, u) + Vector3.up * (1.5f * Mathf.Sin(Mathf.PI * u));
            go.transform.SetPositionAndRotation(from, Quaternion.FromToRotation(Vector3.up, At(0.05f) - from));
            Trail(go, color, 0.06f, 0.14f);
            await Anim.Tween(0.4f, u =>
            {
                if (go == null) return;
                var p = At(u);
                go.transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, At(Mathf.Min(1f, u + 0.05f)) - At(Mathf.Max(0f, u - 0.05f))));
            }, Ease.Linear, () => go == null);
            if (go == null) return;
            Object.Destroy(go);
            Sparks(to, color, 6, 3f);
        }

        // ------------------------------------------------------------------ trails and the whale

        // A trail of light behind `follow` at chest height, until the returned trail is dropped.
        public TrailRenderer Streak(Transform follow, Color color, float width, float seconds = 0.35f, string name = "Streak")
        {
            var go = new GameObject(name);
            go.transform.SetParent(follow, false);
            go.transform.localPosition = Vector3.up * 1f;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = this.trail;
            trail.time = seconds;
            trail.widthMultiplier = width * 1.2f; // the cross-section is soft out to its edges
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f);
            trail.colorGradient = Fade(color);
            trail.minVertexDistance = 0.1f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return trail;
        }

        // DeepSeek's whale of light, as the PV has it (pv/README.md S12): a humpback (Models/Props/WhalePhantom,
        // assets/characters/whale/whale_phantom.py) on the Phantom shader, `length` metres nose to tail, its holder
        // at the middle of its belly. Named `name` for the E2E's watches, so the 集成's whale is not taken for the one
        // 深海潜流 calls up.
        Transform CallWhale(string name, float length, Color color)
        {
            if (whale == null)
            {
                whaleLook = SeaArt.Phantom(color);
                whale = SeaModels.Place("WhalePhantom", parent, 1f, whaleLook);
                var bounds = whale.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
                whaleLook.SetFloat("_Length", bounds.size.z);
                whaleLook.SetFloat("_Mid", bounds.center.z);
            }
            whale.name = name;
            whaleLook.SetColor("_Color", color);
            whaleLook.SetFloat("_Shown", 0f);
            whaleLook.SetFloat("_Fade", 1f);
            whale.transform.localScale = Vector3.one * length;
            whale.SetActive(true);
            return whale.transform;
        }

        // How much of the whale has come and how bright it is, `t` of the way through its swim: called up out of
        // sparks over the first `coming` of it, gone over the last `going`.
        void WhaleShown(float t, float coming, float going)
        {
            whaleLook.SetFloat("_Shown", Mathf.Clamp01(t / coming));
            whaleLook.SetFloat("_Fade", Mathf.Clamp01((1f - t) / going));
        }

        // 深海潜流 (BattleStage.Maelstrom), in two parts either side of the cut to the whirlpool. First (`rising`
        // seconds, the side shot) it is called up out of sparks at `rise`, beyond her, and comes up slowly toward
        // `risen`, side on to the shot's camera at `eye` as the PV has it behind her (headed away from that camera it
        // was a sliver), its nose lifted toward the noise bodies; then, after the cut, it comes again out of the sky over
        // the far side of the whirlpool at `pool` and dives into it (`diving` seconds) toward the cameras there,
        // going out as it reaches it. Flown in one sweep from her to it, it went over the whirlpool's camera and
        // filled the picture white. Not waited for.
        public async Awaitable Summon(Vector3 eye, Vector3 rise, Vector3 risen, Vector3 pool, float length, float rising, float diving, Color color)
        {
            var body = CallWhale("Whale Rising", length, color);
            var lamp = Lamp(rise, color * 1.6f, length);
            var seen = rise - eye;
            seen.y = 0f;
            var across = Vector3.Cross(Vector3.up, seen.normalized);
            if (Vector3.Dot(across, pool - rise) < 0f) across = -across;
            var up = Quaternion.LookRotation(across) * Quaternion.Euler(-18f, 0f, 0f);
            await Anim.Tween(rising, t =>
            {
                if (body == null) return;
                var p = Vector3.LerpUnclamped(rise, risen, Ease.OutCubic(t));
                body.SetPositionAndRotation(p, up * Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI) * 8f));
                lamp.At = p;
                whaleLook.SetFloat("_Shown", Mathf.Clamp01(t / 0.6f));
                whaleLook.SetFloat("_Fade", 1f - Mathf.Clamp01((t - 0.85f) / 0.15f) * 0.6f);
            }, Ease.Linear, () => body == null);
            if (body == null) return;
            Sparks(body.position + Vector3.up * length * 0.15f, color, 16, 3f);
            body.localScale = Vector3.one * (length * 0.7f); // smaller from the whirlpool's cameras, nearer to it than hers
            Vector3 from = pool + new Vector3(1.5f, 8f, 9f), over = pool + new Vector3(0.5f, 5f, 3f), to = pool + Vector3.up * 0.3f;
            Vector3 At(float t) => Vector3.Lerp(Vector3.Lerp(from, over, t), Vector3.Lerp(over, to, t), t);
            await Anim.Tween(diving, t =>
            {
                if (body == null) return;
                var p = At(t);
                var way = At(Mathf.Min(1f, t + 0.02f)) - At(Mathf.Max(0f, t - 0.02f));
                body.SetPositionAndRotation(p, Quaternion.LookRotation(way) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI) * 14f));
                lamp.At = p;
                WhaleShown(t, 0.2f, 0.3f);
            }, Ease.InCubic, () => body == null);
            Out(lamp);
            if (body == null) return;
            Sparks(to + Vector3.up * 0.5f, color, 14, 4f);
            whale.SetActive(false);
        }

        // The 集成's whale swimming along a curve from `from` over `over` to `to`, with a wake of light and bubbles;
        // `passing` is called when it is halfway.
        public async Awaitable Whale(Vector3 from, Vector3 over, Vector3 to, float length, float seconds, Color color, System.Action passing)
        {
            var body = CallWhale("Whale", length, color);
            var wake = Streak(body, color, length * 0.35f, 0.5f);
            wake.transform.localPosition = Vector3.zero;
            var bubbles = BubbleTrail(body, length);
            var lamp = Lamp(from, color * 2.4f, length * 1.4f);
            var passed = false;
            Vector3 At(float t) => Vector3.Lerp(Vector3.Lerp(from, over, t), Vector3.Lerp(over, to, t), t);
            await Anim.Tween(seconds, t =>
            {
                if (body == null) return;
                var p = At(t);
                var ahead = At(Mathf.Min(1f, t + 0.02f)) - At(Mathf.Max(0f, t - 0.02f));
                body.SetPositionAndRotation(p, Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 2f) * 10f));
                lamp.At = p;
                WhaleShown(t, 0.2f, 0.12f);
                if (!passed && t >= 0.5f)
                {
                    passed = true;
                    passing?.Invoke();
                }
            }, Ease.Linear, () => body == null);
            Out(lamp);
            if (body == null) return;
            bubbles.Stop();
            Object.Destroy(wake.gameObject, 0.6f);
            Object.Destroy(bubbles.gameObject, 0.6f);
            whale.SetActive(false);
        }

        ParticleSystem BubbleTrail(Transform follow, float length)
        {
            var go = new GameObject("Bubbles");
            go.transform.SetParent(follow, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            main.startColor = new Color(0.7f, 0.95f, 1f, 0.8f);
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.gravityModifier = -0.15f;
            var emission = ps.emission;
            emission.rateOverTime = 160f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = length * 0.2f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = bubble;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ helpers

        // Something held on the field until let go (Aura, Barrier): Release takes it down, Flare lights it up
        // where a blow lands on it.
        public sealed class Held
        {
            public bool Released { get; private set; }
            internal System.Action OnFlare;
            public void Release() => Released = true;
            public void Flare() => OnFlare?.Invoke();
        }

        // A trail of light `width` across behind `go`, fading over `seconds`.
        TrailRenderer Trail(GameObject go, Color color, float width, float seconds)
        {
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = this.trail;
            trail.time = seconds;
            trail.widthMultiplier = width * 1.2f;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            trail.colorGradient = Fade(color);
            trail.minVertexDistance = 0.05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return trail;
        }

        // Glints (or what `look` draws) left behind `holder` as it moves, `perMetre` of them, in the world so they
        // hang where they were shed.
        // `size` scales them; `pale` draws them between the colour and white, else in the colour as given (with
        // its alpha).
        ParticleSystem Shed(Transform holder, Color color, float perMetre, Material look = null, float size = 1f, bool pale = true)
        {
            var go = new GameObject("Shed");
            go.transform.SetParent(holder, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * size, 0.24f * size);
            main.startColor = pale ? new ParticleSystem.MinMaxGradient(color, Color.white) : new ParticleSystem.MinMaxGradient(color);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = perMetre;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;
            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = look != null ? look : glint;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // Particles coming up out of nothing and going back into it, rather than popping.
        static void Fading(ParticleSystem ps)
        {
            var shade = ps.colorOverLifetime;
            shade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            shade.color = new ParticleSystem.MinMaxGradient(g);
        }

        // Particles drawn stretched along the way they fly, longer the faster.
        static void Stretch(ParticleSystem ps, float speed, float length)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = speed;
            renderer.lengthScale = length;
        }

        // Particles going out in their last third rather than only shrinking to nothing.
        static void Dying(ParticleSystem ps)
        {
            var shade = ps.colorOverLifetime;
            shade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            shade.color = new ParticleSystem.MinMaxGradient(g);
        }

        // A mesh from points, their colours (or none) and triangles, lit by its own facets' normals.
        static Mesh Build(string name, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if (colors != null) mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Gradient Fade(Color color)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.5f), 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // Plays `step` over `seconds` (t from 0 to 1) on `go`, then destroys it and calls `done`; stops early if
        // the field goes first.
        static void Run(GameObject go, float seconds, System.Action<float> step, System.Action done = null)
        {
            Go().Forget();

            async Awaitable Go()
            {
                await Anim.Tween(seconds, t => { if (go != null) step(t); }, Ease.Linear, () => go == null);
                if (go != null) Object.Destroy(go);
                done?.Invoke();
            }
        }
    }
}
