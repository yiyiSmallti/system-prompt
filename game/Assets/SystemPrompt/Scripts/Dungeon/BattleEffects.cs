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
    // the whale of the 集成. Nearly all of it is additive light on the Glow shader (the whale on Holo, the ice on
    // Ice, the can and the windows solid on Toon), made in code and gone once played. The field is drawn in HDR
    // and glows (BattleGlow): the hot middles of the effects go past white and bloom, their bodies stay under
    // it. Smoke is drawn over what is behind it, to stand the flames out against. The flares light the noise
    // bodies, the figures and the seabed round them (Light, SeaFlash in Shaders/SeaCommon.cginc).
    sealed class BattleEffects
    {
        readonly Transform parent;
        readonly Camera camera;
        readonly Material white, ring, beam, bubble, glint; // shared; arcs and lances fade by vertex colour
        readonly Material spark, trail, glare, wave, flame, smoke; // the HDR ones; glare, wave, smoke copied for each use
        readonly Mesh quad; // the rings' and flashes'
        // Made the first time they are wanted.
        Mesh lance, ice, shard, frame, arrow, can, rim, strip, adChrome, adPage, adBar, cardFace, cardStripe;
        Material canLook, rimLook, stripLook, chrome, page, bar, cardLook, cardInk;
        GameObject whale;

        public BattleEffects(Transform parent, Camera camera)
        {
            this.parent = parent;
            this.camera = camera;
            white = SeaArt.Glow(SeaArt.White, Color.white);
            ring = SeaArt.Glow(SeaArt.Ring, Color.white);
            beam = SeaArt.Glow(SeaArt.Beam, Color.white);
            bubble = SeaArt.Glow(SeaArt.Atlas, Color.white);
            glint = SeaArt.Glow(SeaArt.Flare, Color.white);
            spark = SeaArt.Glow(SeaArt.Spark, Color.white * 2.2f);
            trail = SeaArt.Glow(SeaArt.Across, Color.white * 1.2f);
            glare = SeaArt.Glow(SeaArt.Glare, Color.white);
            wave = SeaArt.Glow(SeaArt.Wave, Color.white);
            flame = SeaArt.Glow(SeaArt.Puff, Color.white * 1.8f);
            smoke = SeaArt.Glow(SeaArt.Puff, Color.white);
            smoke.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smoke.renderQueue = 2990; // under the light drawn with it
            quad = SeaArt.Quad();
        }

        // The shared materials and meshes, when the field is taken down (BattleStage.Drop); what is playing then
        // goes with the field, and so does its light.
        public void Dispose()
        {
            foreach (var o in new Object[] { white, ring, beam, bubble, glint, spark, trail, glare, wave, flame, smoke, quad, lance, ice, shard,
                         frame, arrow, can, rim, strip, adChrome, adPage, adBar, cardFace, cardStripe, canLook, rimLook, stripLook, chrome, page, bar,
                         cardLook, cardInk })
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
            var over = sum.maxColorComponent / MaxLight;
            if (over > 1f) sum /= over;
            Shader.SetGlobalColor("_SeaFlashColor", new Color(sum.r, sum.g, sum.b, 0f));
        }

        // ------------------------------------------------------------------ slash

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
                var width = size * 0.34f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f);
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
                var head = Mathf.Clamp01(t / 0.32f) * 1.3f;
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
            var shape = Parts.Shape("Ring", parent, quad, new Material(wave) { color = color });
            shape.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.localScale = Vector3.one * (radius * 2f * Mathf.Lerp(0.12f, 1f, Ease.OutCubic(t)));
                material.color = color * (heat * (1f - t) * (1f - t));
            }, () => Object.Destroy(material));
        }

        // A round flash at `at`, turned to the camera: a glare with a hot middle, past white by `heat`.
        public void Flash(Vector3 at, Color color, float size, float seconds = 0.28f, float heat = 1.5f)
        {
            var shape = Parts.Shape("Flash", parent, quad, new Material(glare) { color = color });
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.SetPositionAndRotation(at, camera.transform.rotation);
                shape.transform.localScale = Vector3.one * (size * Mathf.Lerp(0.6f, 1.15f, Ease.OutCubic(t)));
                material.color = color * (heat * (1f - t) * (1f - t));
            }, () => Object.Destroy(material));
        }

        // A four-pointed star of light at `at` turned to the camera and rolled by `roll`, its rays shrinking
        // back into it as it fades: the glint of a blow landing.
        public void Star(Vector3 at, Color color, float size, float seconds = 0.25f, float roll = 0f, float heat = 2.4f)
        {
            var shape = Parts.Shape("Star", parent, quad, new Material(glint) { color = color });
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.SetPositionAndRotation(at, camera.transform.rotation * Quaternion.Euler(0f, 0f, roll + 25f * t));
                shape.transform.localScale = Vector3.one * (size * Mathf.Lerp(1f, 0.35f, Ease.InCubic(t)) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(t * 8f)));
                material.color = color * (heat * (1f - t));
            }, () => Object.Destroy(material));
        }

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
            Bubbly(bubbles);
            bubbles.Play();
            Mist(at, color, 5, 1.1f, 0.18f, true);
        }

        // A blow on a weakness or a critical, in layers: the field lit up in its colour, a white-hot flash in a
        // wider glare of the colour, a star, lines of light flung out all round, embers left hanging and two shock
        // waves; for a critical, two cuts crossing over it and a third wave after.
        public void Impact(Vector3 at, Color color, bool critical)
        {
            var facing = (camera.transform.position - at).normalized;
            var hot = Color.Lerp(color, Color.white, 0.6f);
            Light(at, color * (critical ? 3.5f : 2.6f), 7f, 0.45f);
            Flash(at, Color.white, 1.2f, 0.12f, 2.1f);
            Flash(at, color, 2.4f, 0.3f, 0.8f);
            Star(at, hot, critical ? 4.4f : 3.4f, 0.26f, Random.Range(0f, 90f));
            var lines = Burst("Impact", at, 28, 0.12f, 0.26f, 28f, 0.18f, spark);
            var main = lines.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            Stretch(lines, 0.05f, 2f);
            Dying(lines);
            lines.Play();
            Embers(at, hot, 22, 7f);
            Ring(at, facing, color, 2.8f, 0.34f);
            Ring(at, facing, Color.white, 1.6f, 0.22f, 2f);
            if (!critical) return;
            Slash(at, Color.white, 1.5f, 40f, 0.3f);
            Slash(at, new Color(1f, 0.35f, 0.4f), 1.5f, -40f, 0.3f);
            Ring(at, facing, new Color(1f, 0.35f, 0.4f), 4f, 0.5f, 0.9f);
        }

        // A column of light standing up out of the seabed at `at`, a hotter core inside it, lighting what is round.
        public void Pillar(Vector3 at, Color color, float radius, float height, float seconds = 0.8f)
        {
            var foot = new Vector3(at.x, 0f, at.z);
            var tube = SeaArt.Tube(radius, radius * 0.6f, 1f, 24);
            var shape = Parts.Shape("Pillar", parent, tube, new Material(beam) { color = color });
            shape.transform.position = foot;
            var material = shape.sharedMaterial;
            var coreTube = SeaArt.Tube(radius * 0.32f, radius * 0.2f, 1f, 16);
            var core = Parts.Shape("Core", shape.transform, coreTube, new Material(beam) { color = Color.white });
            var coreLook = core.sharedMaterial;
            var hot = Color.Lerp(color, Color.white, 0.6f);
            Light(foot + Vector3.up * 1.2f, color * 2f, radius * 6f + 2f, seconds * 0.8f);
            Run(shape.gameObject, seconds, t =>
            {
                var rise = Ease.OutCubic(Mathf.Clamp01(t / 0.3f));
                var fade = 1f - Mathf.Clamp01((t - 0.35f) / 0.65f);
                shape.transform.localScale = new Vector3(1f + t * 0.4f, height * rise, 1f + t * 0.4f);
                material.color = color * (0.7f * fade);
                coreLook.color = hot * (1.3f * fade * fade);
            }, () =>
            {
                Object.Destroy(material);
                Object.Destroy(coreLook);
                Object.Destroy(tube);
                Object.Destroy(coreTube);
            });
        }

        // ------------------------------------------------------------------ ice

        // An ice lance thrown from `from` to `to`, a cold trail behind it; done when it lands.
        public async Awaitable Lance(Vector3 from, Vector3 to, Color color, float delay, float seconds = 0.3f)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var shape = Parts.Shape("Lance", parent, Lance(), white);
            var go = shape.gameObject;
            go.transform.position = from;
            Trail(go, color, 0.09f, 0.16f);
            // A little sideways on the way, so a volley spreads out and closes in.
            var side = Vector3.Cross(to - from, Vector3.up).normalized * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(0.2f, 0.9f);
            var spin = Random.Range(-720f, 720f);
            Vector3 At(float t) => Vector3.Lerp(from, to, t) + side * Mathf.Sin(t * Mathf.PI) * 0.6f;
            await Anim.Tween(seconds, t =>
            {
                if (go == null) return;
                var p = At(t);
                var ahead = At(Mathf.Min(1f, t + 0.05f)) - p;
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(ahead.sqrMagnitude > 1e-6f ? ahead : to - from) * Quaternion.Euler(0f, 0f, spin * t));
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, t);
            }, Ease.InCubic, () => go == null);
            if (go != null) Object.Destroy(go);
        }

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
            var headLook = new Material(glare) { color = Color.Lerp(color, Color.white, 0.6f) * 2.2f };
            var head = Parts.Shape("Head", root, quad, headLook).transform;
            var lamp = Lamp(from, color * 1.8f, 5f);
            var strands = new Transform[width > 0.5f ? 5 : 3];
            for (var i = 0; i < strands.Length; i++)
            {
                strands[i] = Parts.Node("Strand", root);
                Trail(strands[i].gameObject, i == 0 ? Color.Lerp(color, Color.white, 0.5f) : color, width * (i == 0 ? 0.6f : 0.32f), 0.24f);
            }
            var crystals = Shed(root, color, 60f);
            var run = to - from;
            var side = Vector3.Cross(run, Vector3.up).normalized;
            var up = Vector3.Cross(side, run.normalized);
            await Anim.Tween(seconds, t =>
            {
                if (root == null) return;
                var p = Vector3.Lerp(from, to, t) + up * (Mathf.Sin(t * Mathf.PI) * 0.3f);
                head.SetPositionAndRotation(p, camera.transform.rotation);
                head.localScale = Vector3.one * (width * (2.6f + 0.5f * Mathf.Sin(t * 47f)));
                crystals.transform.position = p;
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
                crystals.Stop();
                Object.Destroy(root.gameObject, 0.9f);
            }
            Object.Destroy(headLook);
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
            Shards(at, color, Mathf.RoundToInt(36f * size), 7.5f * size);
            Sparks(at, Color.white, 22, 9f, 0.08f);
            Embers(at, color, 18, 5f, 0.25f);
            Flash(at, Color.white, size * 1.1f, 0.14f, 1.8f);
            Flash(at, color, size * 2.8f, 0.3f, 1.1f);
            Star(at, Color.Lerp(color, Color.white, 0.6f), size * 3f, 0.25f, Random.Range(0f, 90f));
            Ring(at, (camera.transform.position - at).normalized, color, size * 2.2f, 0.4f);
            Light(at, color * 2.6f, 7f, 0.4f);
        }

        // Splinters of ice flung out of `at`, turning over as they fall.
        void Shards(Vector3 at, Color color, int count, float speed)
        {
            var ps = Burst("Shards", at, count, 0.5f, 1f, speed, 0.9f, white);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.startRotation3D = true;
            var any = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationX = any;
            main.startRotationY = any;
            main.startRotationZ = any;
            main.gravityModifier = 1.2f;
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
            var vertices = new List<Vector3> { new Vector3(0f, 0.18f, 0f), new Vector3(-0.06f, -0.08f, 0f), new Vector3(0.07f, -0.12f, 0f), new Vector3(0f, -0.02f, 0.04f) };
            var colors = new List<Color> { Color.white, new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0.8f) };
            return shard = Build("Shard", vertices, colors, new List<int> { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 });
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
            Mesh floorMesh = Spiral(radius, 6), spoutMesh = Spout(radius, 4.8f, 6);
            // Dimmer than the rest: seen through the spout the bands lie over one another many deep.
            var deep = new Color(0.12f, 0.35f, 0.95f);
            Material floorLook = new Material(white) { color = color }, spoutLook = new Material(white) { color = color * 0.5f };
            var underLook = new Material(glare) { color = deep };
            var under = Parts.Shape("Under", root, quad, underLook).transform;
            under.localRotation = Quaternion.Euler(90f, 0f, 0f);
            under.localScale = Vector3.zero;
            var lamp = Lamp(root.position + Vector3.up * 1.5f, Color.clear, radius * 2.2f);
            var floor = Parts.Shape("Whirl", root, floorMesh, floorLook).transform;
            var spout = Parts.Shape("Spout", root, spoutMesh, spoutLook).transform;
            floor.localScale = spout.localScale = Vector3.zero;
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
                Flash(centre + Vector3.up * 1.4f, Color.white, radius * 1.4f, 0.25f, 2.4f);
                Flash(centre + Vector3.up * 1.4f, color, radius * 2.6f, 0.45f, 1.2f);
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
                    floorLook.color = color * (0.8f * (1f - t));
                    spoutLook.color = color * (0.5f * (1f - t));
                    underLook.color = deep * (1f - t);
                }, Ease.OutCubic, () => root == null);
                await Anim.Wait(1.2f); // the last crystals and bubbles out
                if (root != null) Object.Destroy(root.gameObject);
                foreach (var o in new Object[] { floorMesh, spoutMesh, floorLook, spoutLook, underLook }) Object.Destroy(o);
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
                    var half = Vector3.up * Mathf.Lerp(0.3f, 0.75f, u);
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
            emission.rateOverTime = bubbles ? 70f : 140f;
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
            if (bubbles) Bubbly(ps);
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ 深度思考

        // What she says to herself as she thinks it through, DeepSeek's way (嗯……、等等、所以).
        static readonly string[] Musings = { "嗯……", "等等", "所以", "假设", "→", "？", "也就是说", "∴", "再想想", "0", "1", "如果" };

        // 深度思考: words of her reasoning in cold light going round `head` and up and fading, one after another
        // for `seconds`.
        public void Thoughts(Vector3 head, Color color, float seconds)
        {
            const float Life = 0.9f;
            const int Count = 12;
            var root = Parts.Node("Thought", parent, head);
            var words = new TextMesh[Count];
            var first = Random.Range(0, Musings.Length);
            var turn = Random.Range(0f, Mathf.PI * 2f);
            var tint = Color.Lerp(color, Color.white, 0.4f);
            for (var i = 0; i < Count; i++)
            {
                words[i] = SeaArt.Text("Word", root, Musings[(first + i * 5) % Musings.Length], 0.16f, Color.clear);
                words[i].gameObject.AddComponent<Billboard>();
            }
            Run(root.gameObject, seconds + Life, t =>
            {
                var now = t * (seconds + Life);
                for (var i = 0; i < Count; i++)
                {
                    var k = (now - i * seconds / Count) / Life;
                    if (k < 0f || k > 1f)
                    {
                        words[i].color = Color.clear;
                        continue;
                    }
                    var a = turn + i * 2.4f + k * Life * 2.2f;
                    words[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 0.6f, -0.15f + k * 0.8f, Mathf.Sin(a) * 0.6f);
                    words[i].color = new Color(tint.r, tint.g, tint.b, Mathf.Sin(k * Mathf.PI));
                }
            });
        }

        // The charge of 深度思考 held until her next skill: a dial of cold light turning slowly under `follow`'s
        // feet and motes rising round them. Gone once released.
        public Held Aura(Transform follow, Color color)
        {
            var root = Parts.Node("Charge", follow, new Vector3(0f, 0.06f, 0f));
            var look = SeaArt.Glow(SeaArt.Dial, color);
            var dial = Parts.Shape("Dial", root, quad, look).transform;
            dial.localScale = Vector3.one * 2f;
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
                    look.color = color * ((0.55f + 0.2f * Mathf.Sin(t * 3f)) * Mathf.Clamp01(t * 4f));
                    await Awaitable.NextFrameAsync();
                }
                if (root != null)
                {
                    motes.Stop();
                    await Anim.Tween(0.3f, k =>
                    {
                        if (root == null) return;
                        dial.localScale = Vector3.one * (2f + k);
                        look.color = color * (0.55f * (1f - k));
                    });
                    if (root != null) Object.Destroy(root.gameObject);
                }
                Object.Destroy(look);
            }
        }

        // Motes rising round `holder` all the time, until stopped.
        ParticleSystem Rising(Transform holder, Color color)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(holder, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = 20f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
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
            renderer.sharedMaterial = glint;
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
                    look.color = color * (0.85f + 0.1f * Mathf.Sin(t * 5f) + flare);
                    await Awaitable.NextFrameAsync();
                }
                if (root != null)
                {
                    await Anim.Tween(0.25f, k =>
                    {
                        if (root == null) return;
                        root.localScale = Vector3.one * (1f + 0.15f * k);
                        look.color = color * (0.85f * (1f - k));
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

        // A thrown 冰镇汽水 (or 辣条, alight) arcing from `from` to `to`, turning over as it goes, foam (or embers)
        // trailing it. Done when it lands, for the burst there (Fizz, Blaze).
        public async Awaitable Toss(bool fire, Vector3 from, Vector3 to, Color color, float seconds)
        {
            if (parent == null) return;
            var thing = Parts.Node(fire ? "Strip" : "Can", parent);
            thing.position = from;
            var body = Parts.Node("Body", thing);
            body.localScale = Vector3.one * 1.6f; // big enough to read across the field
            Material flame = null;
            Glim lamp = null;
            if (fire)
            {
                strip ??= SeaArt.Box(new Vector3(0.1f, 0.03f, 0.36f), 0.012f);
                stripLook ??= SeaArt.Toon(Palette.Hex("#E2421B"), Palette.Hex("#8A1E0A"), new Color(1f, 0.6f, 0.2f, 0.8f), Palette.Hex("#401000"), 0.01f);
                Parts.Shape("Strip", body, strip, stripLook);
                flame = new Material(glare) { color = color };
                Parts.Shape("Flame", thing, quad, flame).gameObject.AddComponent<Billboard>().transform.localScale = Vector3.one * 1.1f;
                var embers = Shed(thing, color, 30f, spark);
                var rise = embers.main;
                rise.gravityModifier = -0.4f;
                lamp = Lamp(from, color * 1.4f, 4.5f);
            }
            else
            {
                can ??= SeaArt.Cylinder(0.075f, 0.075f, 0.24f, 16);
                rim ??= SeaArt.Cylinder(0.068f, 0.068f, 0.014f, 16);
                canLook ??= SeaArt.Toon(Palette.Hex("#6FD3FF"), Palette.Hex("#2A7FBF"), new Color(1f, 1f, 1f, 0.6f), Palette.Hex("#103050"), 0.01f);
                rimLook ??= SeaArt.Toon(Palette.Hex("#E8EEF5"), Palette.Hex("#9AA6B6"), new Color(1f, 1f, 1f, 0.6f), default, 0.01f);
                Parts.Shape("Can", body, can, canLook, new Vector3(0f, -0.12f, 0f));
                Parts.Shape("Top", body, rim, rimLook, new Vector3(0f, 0.12f, 0f));
                Bubbly(Shed(thing, new Color(0.8f, 0.97f, 1f, 0.9f), 30f, bubble));
            }
            Trail(thing.gameObject, color, 0.14f, 0.2f);
            var run = to - from;
            var axis = Vector3.Cross(run, Vector3.up).normalized;
            var height = 1.2f + run.magnitude * 0.12f;
            await Anim.Tween(seconds, t =>
            {
                if (thing == null) return;
                thing.position = Vector3.Lerp(from, to, t) + Vector3.up * (height * 4f * t * (1f - t));
                body.localRotation = Quaternion.AngleAxis(t * 1080f, axis);
                if (flame != null) flame.color = color * (1.8f + 0.5f * Mathf.Sin(t * 60f));
                if (lamp != null) lamp.At = thing.position;
            }, Ease.Linear, () => thing == null);
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
            Bubbly(foam);
            foam.Play();
            Sparks(at, Color.white, 26, 6f, 0.07f);
            Flash(at, Color.white, 1.4f, 0.16f, 2.6f);
            Flash(at, color, 3.6f, 0.5f, 1.2f);
            Mist(at, new Color(0.85f, 0.97f, 1f), 10, 1.6f, 0.35f);
            Ring(at, (camera.transform.position - at).normalized, color, 1.8f, 0.35f);
            Light(at, color * 2.4f, 6f, 0.55f);
            Frost(at, color, 0.6f).Forget();
        }

        // The 辣条 bursting into a ball of flame where it hits: a white-hot flash, billows of flame rolling up and
        // out from yellow through orange to deep red, dark smoke rising behind them, embers going up, a ring of heat
        // across the seabed, and the field lit orange.
        public void Blaze(Vector3 at, Color color)
        {
            var fire = Burst("Blaze", at, 42, 0.35f, 0.8f, 3.2f, 1.1f, flame);
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
            Flash(at, color, 5f, 0.45f, 1.3f);
            Sparks(at, new Color(1f, 0.75f, 0.35f), 30, 7f, 0.07f);
            Embers(at, new Color(1f, 0.6f, 0.2f), 26, 3.5f, -0.5f);
            Ring(new Vector3(at.x, 0.05f, at.z), Vector3.up, color, 2.6f, 0.55f);
            Ring(at, (camera.transform.position - at).normalized, new Color(1f, 0.85f, 0.5f), 1.8f, 0.3f);
            Light(at, color * 3.2f, 8f, 0.8f);
        }

        // ------------------------------------------------------------------ the noise bodies' attacks

        static readonly string[] Bait = { "震惊！", "点击领取", "恭喜中奖", "限时免费" };

        // 标题党型's 震惊！: pop-up windows flung from `from` at `to` one after another, growing and turning as they
        // come, each bursting into the noise body's pixels (colours a and b) where it hits. Done as the last hits.
        public async Awaitable Popups(Vector3 from, Vector3 to, int count, Color a, Color b)
        {
            var flights = new List<Awaitable>();
            for (var i = 0; i < count; i++) flights.Add(Popup(from, to + Random.insideUnitSphere * 0.35f, i * 0.1f, Bait[i % Bait.Length], a, b));
            foreach (var flight in flights) await flight;
        }

        async Awaitable Popup(Vector3 from, Vector3 to, float delay, string words, Color a, Color b)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var window = Parts.Node("Popup", parent);
            window.position = from;
            Window(window, words);
            var tilt = Random.Range(-25f, 25f);
            var spin = Random.Range(-160f, 160f);
            var lift = Random.Range(0.2f, 0.7f);
            await Anim.Tween(0.32f, t =>
            {
                if (window == null) return;
                window.SetPositionAndRotation(Vector3.Lerp(from, to, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * lift),
                    camera.transform.rotation * Quaternion.Euler(0f, 0f, tilt + spin * t));
                window.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.6f, t);
            }, Ease.InCubic, () => window == null);
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

        // 冷知识型's 你知道吗？: fact cards dealt from `from` at `to` one after another, spinning flat as they come,
        // each bursting into the noise body's pixels (colours a and b) where it hits. Done as the last hits.
        public async Awaitable Cards(Vector3 from, Vector3 to, int count, Color a, Color b)
        {
            var flights = new List<Awaitable>();
            for (var i = 0; i < count; i++) flights.Add(CardFlight(from, to + Random.insideUnitSphere * 0.3f, i * 0.12f, Facts[i % Facts.Length], a, b));
            foreach (var flight in flights) await flight;
        }

        async Awaitable CardFlight(Vector3 from, Vector3 to, float delay, string words, Color a, Color b)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var card = Parts.Node("Card", parent);
            card.position = from;
            Card(card, words);
            var spin = Random.Range(400f, 720f) * (Random.value < 0.5f ? -1f : 1f);
            var lift = Random.Range(0.3f, 0.8f);
            await Anim.Tween(0.3f, t =>
            {
                if (card == null) return;
                card.SetPositionAndRotation(Vector3.Lerp(from, to, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * lift),
                    camera.transform.rotation * Quaternion.Euler(0f, 0f, spin * t));
                card.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.4f, t);
            }, Ease.InCubic, () => card == null);
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

        // 转发型's 转发保平安: forked arrows shot up out of `from` that each split in three at the top of their
        // flight, the copies coming down on `targets` (and the floor round them). Done when all have landed.
        public async Awaitable Forward(Vector3 from, IList<Vector3> targets, Color color)
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
                volleys.Add(Volley(from, apex, targets, i, color, look, i * 0.08f));
            }
            foreach (var volley in volleys) await volley;
            Object.Destroy(look);
        }

        // One arrow up to `apex`, where it flashes and splits into three that come down.
        async Awaitable Volley(Vector3 from, Vector3 apex, IList<Vector3> targets, int k, Color color, Material look, float delay)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            await Arrow(from, apex, look, color, 0.3f, Ease.OutCubic, false);
            if (parent == null) return;
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

        // DeepSeek's whale, made of light (Resources/Models/Props/Whale, a Tripo model, on the Holo shader),
        // swimming along a curve from `from` over `over` to `to`, `length` metres long nose to tail, with a wake
        // of light and bubbles; `passing` is called when it is halfway.
        public async Awaitable Whale(Vector3 from, Vector3 over, Vector3 to, float length, float seconds, Color color, System.Action passing)
        {
            if (whale == null) whale = SeaModels.Place("Whale", parent, length, SeaArt.Holo(color, SeaModels.Texture("Whale")));
            whale.SetActive(true);
            var body = whale.transform;
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
            Bubbly(ps);
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
        ParticleSystem Shed(Transform holder, Color color, float perMetre, Material look = null)
        {
            var go = new GameObject("Shed");
            go.transform.SetParent(holder, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
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

        // Particles drawn as the bubble in SeaArt.Atlas, for the `bubble` material.
        static void Bubbly(ParticleSystem ps)
        {
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve((SeaArt.AtlasBubble + 0.5f) / 4f);
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
