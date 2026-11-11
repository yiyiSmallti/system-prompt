using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // What the two leave on the seabed as they go: each footfall a faint data ripple spreading over the floor and a
    // little silt stirred up, a few bubbles too at a run; a sprint trails bubbles off the hips; pulling up hard out of a
    // run kicks a ring of silt out round the feet. Also the maze's one pool of sparks, square bits and bubbles for its
    // set pieces (a wall breaking up into pixels, a beacon's motes, a chest's): Spark, Bit and Bubble. Particle systems
    // made with the maze and only ever emitted into, so nothing is made while playing; they go with the maze
    // (Parts.Free takes their materials).
    public sealed class SeaWake
    {
        static readonly Color RippleColor = new Color(0.45f, 0.85f, 1f, 0.5f);
        static readonly Color BubbleColor = new Color(0.62f, 0.91f, 1f, 0.6f);
        static readonly Color SiltColor = new Color(0.42f, 0.72f, 0.88f, 0.5f); // pale: added over the lit seabed
        const float WakeEvery = 0.07f; // seconds between a sprint's bubbles

        readonly ParticleSystem ripples, bubbles, silt, sparks, bits;
        float wakeIn;

        public SeaWake(Transform parent)
        {
            // A ring lying on the floor, out from a third of its size in an ease-out, fading as the square of what is left.
            ripples = Make(parent, "Ripples", SeaArt.Ring, 32, true, new AnimationCurve(new Keyframe(0f, 0.3f, 2.2f, 2.2f), new Keyframe(1f, 1f, 0f, 0f)),
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 0.5f), new GradientAlphaKey(0f, 1f) });
            bubbles = Make(parent, "Bubbles", SeaArt.Bubble, 96, false, AnimationCurve.Linear(0f, 0.8f, 1f, 1.15f),
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            var wobble = bubbles.noise;
            wobble.enabled = true;
            wobble.strength = 0.35f;
            wobble.frequency = 1.6f;
            wobble.scrollSpeed = 0.6f;
            silt = Make(parent, "Silt", SeaArt.Puff, 64, false, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f),
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            var settle = silt.limitVelocityOverLifetime;
            settle.enabled = true;
            settle.limit = 0f;
            settle.dampen = 0.06f;
            // A point of light that shrinks a little as it goes; a square pixel that holds its size, then winks out.
            sparks = Make(parent, "Sparks", SeaArt.Spark, 256, false, AnimationCurve.Linear(0f, 1f, 1f, 0.45f),
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            bits = Make(parent, "Bits", SeaArt.White, 160, false, AnimationCurve.Linear(0f, 1f, 1f, 0.7f),
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            var drag = sparks.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0f;
            drag.dampen = 0.03f;
        }

        // The set pieces' particles: a point of light, a square pixel, a bubble, each on its own from `at`.
        public void Spark(Vector3 at, Vector3 velocity, float size, float life, Color color) => Emit(sparks, at, velocity, size, life, color);

        public void Bit(Vector3 at, Vector3 velocity, float size, float life, Color color) => Emit(bits, at, velocity, size, life, color, false);

        public void Bubble(Vector3 at, Vector3 velocity, float size, float life) => Emit(bubbles, at, velocity, size, life, BubbleColor);

        // A foot down at `at` (on the seabed), the figure going `speed` metres a second.
        public void Footfall(Vector3 at, float speed)
        {
            Emit(ripples, at + Vector3.up * 0.03f, Vector3.zero, Random.Range(1.0f, 1.25f), 0.7f, RippleColor);
            for (var i = 0; i < 2; i++)
            {
                var away = Flat() * 0.3f;
                Emit(silt, at + away * 0.3f + Vector3.up * 0.08f, away + Vector3.up * 0.08f, 0.25f, Random.Range(0.45f, 0.6f), SiltColor);
            }
            if (speed < 4f) return;
            var n = Random.Range(2, 4);
            for (var i = 0; i < n; i++)
                Emit(bubbles, at + Vector3.up * 0.06f + Flat() * 0.08f, Vector3.up * Random.Range(0.6f, 1.2f) + Flat() * 0.2f,
                    Random.Range(0.05f, 0.11f), Random.Range(0.6f, 1f), BubbleColor);
        }

        // Each frame for the player going `speed` metres a second, `back` the way behind them: a sprint's trail of bubbles.
        public void Sprint(Vector3 hips, Vector3 back, float speed, float dt)
        {
            if (speed < 7.5f)
            {
                wakeIn = 0f;
                return;
            }
            wakeIn -= dt;
            if (wakeIn > 0f) return;
            wakeIn += WakeEvery;
            Emit(bubbles, hips + back * 0.3f + Flat() * 0.1f, Vector3.up * Random.Range(0.3f, 0.5f) + Flat() * 0.1f,
                Random.Range(0.05f, 0.1f), Random.Range(0.8f, 1.2f), BubbleColor);
        }

        // Pulled up hard out of a run, at `at` on the seabed: silt kicked out all round.
        public void Stop(Vector3 at)
        {
            for (var i = 0; i < 10; i++)
            {
                var a = (i + Random.value * 0.6f) * Mathf.PI / 5f;
                var away = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Emit(silt, at + away * 0.25f + Vector3.up * 0.12f, away * Random.Range(0.9f, 1.3f) + Vector3.up * 0.12f, 0.45f, Random.Range(0.6f, 0.8f), SiltColor);
            }
        }

        static Vector3 Flat()
        {
            var r = Random.insideUnitCircle;
            return new Vector3(r.x, 0f, r.y);
        }

        static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, float life, Color color, bool spin = true)
        {
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = spin ? Random.Range(0f, 360f) : 0f,
                applyShapeToPosition = false,
            }, 1);
        }

        // Playing with nothing of its own to emit, so what is emitted into it lives out its life: in the world, on the
        // game's own clock, drawn with the sea's additive glow (faint by the fog and near the lens).
        static ParticleSystem Make(Transform parent, string name, Texture texture, int most, bool flat, AnimationCurve grow, GradientAlphaKey[] fade)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startSpeed = 0f;
            main.maxParticles = most;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, fade);
            color.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SeaArt.Glow(texture, Color.white);
            renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
