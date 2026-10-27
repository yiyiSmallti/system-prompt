using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The light of the fighting on the battle's 3D field (Dungeon/BattleStage.cs): slash arcs, sparks, shock
    // rings and flashes at a hit, ice lances and the frost they leave, the pixels a screenshot knocks off a noise
    // body, light trails behind whoever dashes, pillars of light, and the whale of the 集成. All of it is
    // additive light on the Glow shader (the whale on Holo), made in code and gone once played.
    sealed class BattleEffects
    {
        readonly Transform parent;
        readonly Camera camera;
        readonly Material white, dot, ring, beam; // shared; arcs and lances fade by vertex colour
        Mesh lance;
        GameObject whale;

        public BattleEffects(Transform parent, Camera camera)
        {
            this.parent = parent;
            this.camera = camera;
            white = SeaArt.Glow(SeaArt.White, Color.white);
            dot = SeaArt.Glow(SeaArt.Atlas, Color.white);
            var cell = SeaArt.AtlasCell(SeaArt.AtlasDot);
            dot.mainTextureScale = new Vector2(cell.x, cell.y);
            dot.mainTextureOffset = new Vector2(cell.z, cell.w);
            ring = SeaArt.Glow(SeaArt.Ring, Color.white);
            beam = SeaArt.Glow(SeaArt.Beam, Color.white);
        }

        // ------------------------------------------------------------------ slash

        // A crescent swept across `at` in the plane facing the camera, rolled by `roll` degrees: the light runs
        // along it from one end to the other in a tenth of a second and fades from its tail.
        public void Slash(Vector3 at, Color color, float size, float roll, float seconds = 0.34f)
        {
            const int Steps = 28;
            var facing = (camera.transform.position - at).normalized;
            var basis = Quaternion.LookRotation(-facing) * Quaternion.Euler(0f, 0f, roll);
            var mesh = new Mesh { name = "Slash" };
            var vertices = new Vector3[(Steps + 1) * 3];
            var colors = new Color[vertices.Length];
            var triangles = new List<int>();
            for (var i = 0; i <= Steps; i++)
            {
                var u = (float)i / Steps;
                var angle = Mathf.Lerp(-105f, 105f, u) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle) * 0.55f, 0f);
                // Sin(π) comes out a hair below 0 in floats, and a negative to the power 0.7 is NaN.
                var width = size * 0.3f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f);
                vertices[i * 3] = dir * (size - width);
                vertices[i * 3 + 1] = dir * (size - width * 0.35f);
                vertices[i * 3 + 2] = dir * size;
                if (i == Steps) continue;
                for (var row = 0; row < 2; row++)
                {
                    int a = i * 3 + row, b = a + 1, c = a + 3, d = a + 4;
                    triangles.AddRange(new[] { a, c, d, a, d, b });
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            var shape = Parts.Shape("Slash", parent, mesh, white);
            shape.transform.SetPositionAndRotation(at, basis);
            var core = Color.Lerp(color, Color.white, 0.65f);
            Run(shape.gameObject, seconds, t =>
            {
                var head = Mathf.Clamp01(t / 0.32f) * 1.3f;
                var fade = 1f - Mathf.Clamp01((t - 0.55f) / 0.45f);
                for (var i = 0; i <= Steps; i++)
                {
                    var u = (float)i / Steps;
                    var lit = u > head ? 0f : (1f - Mathf.Clamp01((head - u) / 0.85f)) * fade;
                    colors[i * 3] = Color.clear;
                    colors[i * 3 + 1] = core * lit;
                    colors[i * 3 + 2] = color * (lit * 0.45f);
                }
                mesh.colors = colors;
            }, () => Object.Destroy(mesh));
        }

        // ------------------------------------------------------------------ bursts

        // Short bright streaks flying out of `at` and falling.
        public void Sparks(Vector3 at, Color color, int count, float speed, float size = 0.08f)
        {
            var ps = Burst("Sparks", at, count, 0.22f, 0.5f, speed, size, dot);
            var main = ps.main;
            main.startColor = color;
            main.gravityModifier = 0.7f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.5f;
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

        // A ring of light spreading from `at` across the plane facing `normal`, out to `radius`.
        public void Ring(Vector3 at, Vector3 normal, Color color, float radius, float seconds = 0.45f)
        {
            var shape = Parts.Shape("Ring", parent, SeaArt.Quad(), new Material(ring) { color = color });
            shape.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.localScale = Vector3.one * (radius * 2f * Mathf.Lerp(0.15f, 1f, Ease.OutCubic(t)));
                material.color = color * (1f - t * t);
            }, () => Object.Destroy(material));
        }

        // A round flash at `at`, turned to the camera.
        public void Flash(Vector3 at, Color color, float size, float seconds = 0.28f)
        {
            var shape = Parts.Shape("Flash", parent, SeaArt.Quad(), new Material(dot) { color = color });
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                shape.transform.SetPositionAndRotation(at, camera.transform.rotation);
                shape.transform.localScale = Vector3.one * (size * Mathf.Lerp(0.5f, 1.2f, Ease.OutCubic(t)));
                material.color = color * (1f - t);
            }, () => Object.Destroy(material));
        }

        // A column of light standing up out of the seabed at `at`.
        public void Pillar(Vector3 at, Color color, float radius, float height, float seconds = 0.8f)
        {
            var shape = Parts.Shape("Pillar", parent, SeaArt.Tube(radius, radius * 0.6f, 1f, 24), new Material(beam) { color = color });
            shape.transform.position = new Vector3(at.x, 0f, at.z);
            var material = shape.sharedMaterial;
            Run(shape.gameObject, seconds, t =>
            {
                var rise = Ease.OutCubic(Mathf.Clamp01(t / 0.3f));
                shape.transform.localScale = new Vector3(1f + t * 0.4f, height * rise, 1f + t * 0.4f);
                material.color = color * (1f - Mathf.Clamp01((t - 0.35f) / 0.65f));
            }, () => Object.Destroy(material));
        }

        // ------------------------------------------------------------------ ice

        // An ice lance thrown from `from` to `to`, a cold trail behind it; done when it lands.
        public async Awaitable Lance(Vector3 from, Vector3 to, Color color, float delay, float seconds = 0.3f)
        {
            await Anim.Wait(delay);
            if (parent == null) return;
            var shape = Parts.Shape("Lance", parent, Lance(), white);
            var go = shape.gameObject;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = white;
            trail.time = 0.16f;
            trail.widthMultiplier = 0.09f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.colorGradient = Fade(color);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

        // ------------------------------------------------------------------ trails and the whale

        // A trail of light behind `follow` at chest height, until the returned trail is dropped.
        public TrailRenderer Streak(Transform follow, Color color, float width, float seconds = 0.35f)
        {
            var go = new GameObject("Streak");
            go.transform.SetParent(follow, false);
            go.transform.localPosition = Vector3.up * 1f;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = white;
            trail.time = seconds;
            trail.widthMultiplier = width;
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
            var passed = false;
            Vector3 At(float t) => Vector3.Lerp(Vector3.Lerp(from, over, t), Vector3.Lerp(over, to, t), t);
            await Anim.Tween(seconds, t =>
            {
                if (body == null) return;
                var p = At(t);
                var ahead = At(Mathf.Min(1f, t + 0.02f)) - At(Mathf.Max(0f, t - 0.02f));
                body.SetPositionAndRotation(p, Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 2f) * 10f));
                if (!passed && t >= 0.5f)
                {
                    passed = true;
                    passing?.Invoke();
                }
            }, Ease.Linear, () => body == null);
            if (body == null) return;
            bubbles.Stop();
            Object.Destroy(wake.gameObject, 0.6f);
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
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve((SeaArt.AtlasBubble + 0.5f) / 4f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SeaArt.Glow(SeaArt.Atlas, Color.white);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ helpers

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
