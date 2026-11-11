using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // What floats in the water and the light coming down through it, beyond the motes and debris the maze and the field
    // already had: the plankton motes drifting on a current and swaying instead of hanging still; big soft motes passing
    // right in front of the lens, out of focus, for parallax; bubbles rising off the deep's seabed; in the deep, shafts of
    // light from the surface anchored to the world near the camera (ShaftPool), the old ones having hung 62 m up where no
    // camera saw them; in a battle, three shafts behind the noise bodies. With 背景动态 off nothing drifts that was not
    // there before: no near motes or bubbles, half the motes and no sway. Everything hangs under the maze or the field
    // and goes with it (Parts.Free), particle looks included.
    static class SeaFloaters
    {
        static readonly Vector3 Current = new Vector3(0.18f, 0f, 0.1f); // metres a second, the way the water moves

        static bool Moving => GameRoot.Instance == null || GameRoot.Instance.Settings.backgroundMotion;

        // The maze's: its motes ("Motes" under `motes`, DungeonWorld.BuildAmbience) given the current, the near motes
        // under its camera, and in the deep the rising bubbles.
        public static void Explore(Transform motes, Camera camera, bool deep)
        {
            var move = Moving;
            var plankton = motes.Find("Motes");
            if (plankton != null && plankton.TryGetComponent<ParticleSystem>(out var ps))
            {
                var main = ps.main;
                var count = move ? 320 : 130;
                main.maxParticles = count;
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
                var emission = ps.emission;
                emission.rateOverTime = count / main.duration;
                Drift(ps, Current, 0.08f, move);
            }
            if (!move) return;
            Near(camera, 40, 0.25f, 0.6f, 0.08f);
            if (deep)
            {
                SeaArt.Particles(motes, "Rising", 0, 0, 60, new Vector3(50f, 0.2f, 50f), 8f, 0.05f, 0.12f, new Color(0.7f, 0.9f, 1f, 0.5f), 1.2f, 0.1f, SeaArt.Bubble);
                if (motes.Find("Rising").TryGetComponent<ParticleSystem>(out var rising)) Drift(rising, Vector3.zero, 0.08f, true);
            }
        }

        // The battle field's: a few near motes, fainter and smaller so the nameplates and numbers stay clear, and three
        // shafts behind the noise bodies, leaning toward the side the camera mostly stands on.
        public static void Stage(Transform field, Camera camera)
        {
            var glow = SeaAtmos.Current.Surface * 0.3f;
            glow.a = 1f;
            var look = SeaArt.Glow(SeaArt.Beam, glow);
            look.mainTextureScale = new Vector2(1f, 0.6f);
            look.SetFloat("_FogReach", 0.5f);
            var tube = SeaArt.Tube(1.6f, 4.5f, 40f, 14);
            Vector3[] at = { new Vector3(-6f, 0f, 14f), new Vector3(2f, 0f, 16f), new Vector3(8f, 0f, 12f) };
            foreach (var p in at)
            {
                var down = new Vector3(-p.x * 0.012f, -1f, -0.18f).normalized;
                var t = Parts.Shape("Shaft", field, tube, look, p - down * 40f).transform;
                t.localRotation = Quaternion.FromToRotation(Vector3.up, down);
            }
            if (Moving) Near(camera, 16, 0.15f, 0.35f, 0.05f);
        }

        // Soft discs drifting just in front of the lens (Glow fades them out nearer than that, SeaNearGlow), in the world,
        // so the camera moving past them shows them sliding by.
        static void Near(Camera camera, int count, float min, float max, float alpha)
        {
            var surface = SeaAtmos.Current.Surface;
            SeaArt.Particles(camera.transform, "Near Motes", 0, 0, count, new Vector3(6f, 4f, 5f), 6f, min, max, new Color(surface.r, surface.g, surface.b, alpha), 0.02f, 0f, SeaArt.Glare, 0.6f);
            var near = camera.transform.Find("Near Motes");
            near.localPosition = new Vector3(0f, 0f, 3.5f);
            if (near.TryGetComponent<ParticleSystem>(out var ps)) Drift(ps, Current, 0.05f, true);
        }

        // On the water's current, give or take `spread`, swaying slowly as it goes (when `sway`).
        static void Drift(ParticleSystem ps, Vector3 current, float spread, bool sway)
        {
            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(current.x - spread, current.x + spread);
            vel.z = new ParticleSystem.MinMaxCurve(current.z - spread, current.z + spread);
            var noise = ps.noise;
            noise.enabled = sway;
            if (!sway) return;
            noise.strength = 0.15f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.1f;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        // The deep's shafts of light: the world in 14 m cells, about half of them holding a shaft somewhere in it, and
        // the ten nearest the camera within 32 m (none behind it) drawn by a fixed set of ten, each brightening in from
        // 32 to 22 m and fading out again closer than 8 m, breathing slowly (less under 减弱闪光) and swaying a few
        // degrees (not with 背景动态 off). One mesh and one look for all, each its own brightness (a property block).
        public sealed class ShaftPool
        {
            const float Cell = 14f, Reach = 32f, Length = 42f;
            const int Count = 10;
            readonly Renderer[] shafts = new Renderer[Count];
            readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            readonly Vector3[] found = new Vector3[Count];
            readonly float[] foundD = new float[Count], foundH = new float[Count];
            static readonly int ColorId = Shader.PropertyToID("_Color");

            public ShaftPool(Transform parent)
            {
                var look = SeaArt.Glow(SeaArt.Beam, Color.clear); // each one's colour is its property block's (Update)
                look.mainTextureScale = new Vector2(1f, 0.6f);
                look.SetFloat("_FogReach", 0.5f);
                var tube = SeaArt.Tube(1.4f, 3.6f, Length, 14);
                for (var i = 0; i < Count; i++)
                {
                    shafts[i] = Parts.Shape("Shaft", parent, tube, look, Vector3.zero);
                    shafts[i].gameObject.SetActive(false);
                }
            }

            public void Update(Camera camera, float time, Color surface)
            {
                var eye = camera.transform.position;
                var ahead = camera.transform.forward;
                ahead.y = 0f;
                ahead.Normalize();
                int cx = Mathf.FloorToInt(eye.x / Cell), cz = Mathf.FloorToInt(eye.z / Cell), n = 0;
                for (var dz = -3; dz <= 3; dz++)
                for (var dx = -3; dx <= 3; dx++)
                {
                    int gx = cx + dx, gz = cz + dz;
                    var h = DungeonWorld.Hash(gx * 7 + 3, gz * 13 + 5);
                    if (h >= 0.45f) continue;
                    var foot = new Vector3((gx + 0.2f + 0.6f * DungeonWorld.Hash(gx, gz + 31)) * Cell, 0f, (gz + 0.2f + 0.6f * DungeonWorld.Hash(gx + 17, gz)) * Cell);
                    var to = foot - eye;
                    to.y = 0f;
                    var d = to.magnitude;
                    if (d >= Reach || Vector3.Dot(to, ahead) < -0.2f * d) continue;
                    // Kept nearest first, at most Count.
                    var k = Mathf.Min(n, Count);
                    if (n >= Count && d >= foundD[Count - 1]) continue;
                    if (k == Count) k--;
                    while (k > 0 && foundD[k - 1] > d)
                    {
                        found[k] = found[k - 1];
                        foundD[k] = foundD[k - 1];
                        foundH[k] = foundH[k - 1];
                        k--;
                    }
                    found[k] = foot;
                    foundD[k] = d;
                    foundH[k] = h;
                    n = Mathf.Min(n + 1, Count);
                }
                var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
                var sway = Moving ? 3f : 0f;
                var colour = surface * 0.22f;
                for (var i = 0; i < Count; i++)
                {
                    var r = shafts[i];
                    if (i >= n)
                    {
                        if (r.gameObject.activeSelf) r.gameObject.SetActive(false);
                        continue;
                    }
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    var h = foundH[i];
                    var d = foundD[i];
                    var breath = calm ? 0.85f + 0.15f * Mathf.Sin(time * 0.6f + h * 9f) : 0.6f + 0.4f * Mathf.Sin(time * 0.6f + h * 9f);
                    var c = colour * (Mathf.SmoothStep(0f, 1f, (Reach - d) / 10f) * Mathf.SmoothStep(0f, 1f, (d - 3f) / 5f) * breath);
                    c.a = 1f;
                    block.SetColor(ColorId, c);
                    r.SetPropertyBlock(block);
                    // Down along the sea's light (as the caustics fall), swaying round its top.
                    var down = -new Vector3(0.35f, 1f, 0.25f).normalized;
                    var swing = Quaternion.Euler(sway * Mathf.Sin(time * 0.44f + h * 20f), 0f, sway * Mathf.Cos(time * 0.37f + h * 13f));
                    down = swing * down;
                    r.transform.SetPositionAndRotation(found[i] - down * Length, Quaternion.FromToRotation(Vector3.up, down));
                }
            }
        }
    }
}
