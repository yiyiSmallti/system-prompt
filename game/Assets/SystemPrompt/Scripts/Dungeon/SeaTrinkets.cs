using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // Light worn on a figure (SeaFigure makes one for each, in the maze and in battle), so the two of them read as
    // things of the latent space and not people in a server room: a band of light round each wrist; motes going
    // slowly round the waist, trailing (the player's square, pixels; DeepSeek's starlight); and over DeepSeek's
    // head a halo of data, a thin ring with a ring of marks inside it turning the other way, as in the PV when she
    // changes (pv/README.md S11). All of it additive on the Glow shader, so the figure's depth and its shadows
    // leave it out (SeaShade draws only what has a depth pass) and it shines in the 集成's ink too.
    sealed class SeaTrinkets
    {
        static readonly Color PlayerGlow = new Color(0.4f, 1f, 0.8f), DeepSeekGlow = new Color(0.55f, 0.88f, 1f);
        const float HaloTurn = 14f;     // degrees a second, the ring of marks twice that the other way
        const float MoteTurn = 0.55f;    // radians a second round the waist

        readonly Transform root, head;
        readonly Vector3 headUp; // up as the model stands in its file, in the head bone's own space
        readonly float above;    // from the head bone to the top of the head
        readonly (Transform arm, Transform hand, Transform band)[] wrists;
        readonly Transform halo, dial;
        readonly Material bandLook, haloLook, dialLook, moteLook;
        readonly Mesh hoop, ring;
        readonly Color color;
        float time;

        public SeaTrinkets(Transform root, Animator animator, float height, bool deepSeek)
        {
            this.root = root;
            var s = height / 1.6f;
            color = deepSeek ? DeepSeekGlow : PlayerGlow;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            headUp = head.InverseTransformDirection(root.up);
            above = height - root.InverseTransformPoint(head.position).y;

            // The wrists: a soft band round each forearm just short of the hand.
            hoop = Hoop(0.042f * s, 0.009f * s, false);
            bandLook = SeaArt.Glow(SeaArt.White, color * 0.8f);
            wrists = new[]
            {
                Wrist(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                Wrist(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
            };

            if (deepSeek)
            {
                ring = Hoop(0.12f * s, 0.012f * s, true);
                haloLook = SeaArt.Glow(SeaArt.White, color * 1.4f);
                halo = Parts.Shape("Halo", root, ring, haloLook).transform;
                dialLook = SeaArt.Glow(SeaArt.Dial, color);
                dial = Parts.Shape("Halo Marks", halo, SeaArt.Quad(), dialLook).transform;
                dial.localScale = Vector3.one * (0.2f * s);
            }

            moteLook = SeaArt.Glow(deepSeek ? SeaArt.Flare : SeaArt.White, Color.white * 1.2f);
            Motes(height, s, deepSeek);
        }

        (Transform, Transform, Transform) Wrist(Animator animator, HumanBodyBones arm, HumanBodyBones hand)
        {
            var band = Parts.Shape("Wrist Light", root, hoop, bandLook).transform;
            return (animator.GetBoneTransform(arm), animator.GetBoneTransform(hand), band);
        }

        // Motes going round the waist, each living a few seconds and trailing a little light, in the figure's own
        // space so they go where it goes.
        void Motes(float height, float s, bool deepSeek)
        {
            var go = new GameObject("Waist Motes");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.up * (height * 0.55f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve((deepSeek ? 0.05f : 0.013f) * s, (deepSeek ? 0.08f : 0.02f) * s);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.5f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = ps.emission;
            emission.rateOverTime = 3.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.36f * s;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var flow = ps.velocityOverLifetime;
            flow.enabled = true;
            flow.space = ParticleSystemSimulationSpace.Local;
            flow.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.y = new ParticleSystem.MinMaxCurve(-0.04f * s, 0.07f * s);
            flow.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            flow.orbitalY = new ParticleSystem.MinMaxCurve(MoteTurn);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var trails = ps.trails;
            trails.enabled = true;
            trails.lifetime = 0.14f;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.45f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            trails.minVertexDistance = 0.01f * s;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = true;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = moteLook;
            renderer.trailMaterial = bandLook;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        // After the figure is posed (SeaFigure.Animate): the bands round the wrists, the halo over the head, tipped
        // most of the way the head is, breathing and turning.
        public void Step(float dt)
        {
            time += dt;
            foreach (var (arm, hand, band) in wrists)
            {
                var along = hand.position - arm.position;
                if (along.sqrMagnitude < 1e-8f) continue;
                band.SetPositionAndRotation(arm.position + along * 0.86f, Quaternion.FromToRotation(Vector3.up, along));
            }
            bandLook.color = color * (0.75f + 0.12f * Mathf.Sin(time * 2.1f)); // under the glow's threshold: on her white gloves more bloomed to a ball
            if (halo == null) return;
            var up = head.TransformDirection(headUp);
            var tilt = Vector3.Slerp(root.up, up, 0.7f);
            halo.SetPositionAndRotation(head.position + up * (above * 0.95f + 0.02f * Mathf.Sin(time * 1.3f)),
                Quaternion.FromToRotation(Vector3.up, tilt) * Quaternion.AngleAxis(time * HaloTurn, Vector3.up));
            dial.localRotation = Quaternion.Euler(90f, -time * HaloTurn * 3f, 0f);
            haloLook.color = color * (1.3f + 0.2f * Mathf.Sin(time * 1.7f));
        }

        public void Dispose()
        {
            foreach (var o in new Object[] { bandLook, haloLook, dialLook, moteLook, hoop, ring })
                if (o != null) Object.Destroy(o);
        }

        // A band of light round the y axis, `radius` out and `width` across: standing up as a sleeve (flat false,
        // across along y) or lying flat as a ring (across outward). Bright along its middle, nothing at its edges
        // (vertex alpha, which the Glow shader takes).
        static Mesh Hoop(float radius, float width, bool flat, int sides = 32)
        {
            var vertices = new Vector3[(sides + 1) * 3];
            var colors = new Color[vertices.Length];
            var uv = new Vector2[vertices.Length];
            for (var j = 0; j <= sides; j++)
            for (var k = 0; k < 3; k++)
            {
                var a = 2f * Mathf.PI * j / sides;
                var round = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var off = (k - 1) * width * 0.5f;
                var i = j * 3 + k;
                vertices[i] = flat ? round * (radius + off) : round * radius + Vector3.up * off;
                colors[i] = new Color(1f, 1f, 1f, k == 1 ? 1f : 0f);
                uv[i] = new Vector2((float)j / sides, k * 0.5f);
            }
            var triangles = new int[sides * 2 * 6];
            var t = 0;
            for (var j = 0; j < sides; j++)
            for (var k = 0; k < 2; k++)
            {
                int a = j * 3 + k, b = a + 1, c = a + 3, d = a + 4;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
            var mesh = new Mesh { name = flat ? "Halo" : "Wrist Light", vertices = vertices, colors = colors, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
