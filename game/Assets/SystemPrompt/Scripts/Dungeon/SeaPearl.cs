using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // 数据珍珠, what there is to collect in the latent space: an open scallop shell on the seabed off the main
    // ways, a pearl turning over it in a soft glow, a thin pink beam to find it by from afar. Walking into one
    // takes it (DungeonScreen): the pearl flies up along a curve into the player's chest with a trail behind it,
    // where it goes into them (DungeonWorld lights them up), and the shell closes and dims. One taken on an earlier
    // dive is built taken.
    public sealed class SeaPearl
    {
        public const float Reach = 1.8f;
        public static readonly Color Nacre = new Color(1f, 0.82f, 0.95f);
        static readonly Color Sheen = new Color(0.6f, 0.9f, 1f), Pink = new Color(1f, 0.45f, 0.85f), BeamCore = new Color(1f, 0.8f, 0.94f, 0.1f);
        const float Height = 0.95f, Fly = 0.55f;

        public readonly Vector2Int Cell;
        public readonly int Index;
        public readonly Vector3 Position;
        public bool Taken { get; private set; }

        readonly Transform root, lid, pearl, bead, flare;
        readonly Material glow, halo, beam, pool;
        readonly TrailRenderer trail;
        readonly float seed;
        float takenAt = -1f;
        Transform takenBy;
        Vector3 takenFrom;
        bool landed;

        public SeaPearl(Vector2Int cell, int index, bool taken, DungeonMap map, Transform parent)
        {
            Cell = cell;
            Index = index;
            seed = index * 1.7f;
            Position = map.World(cell);
            root = Parts.Node("Pearl " + index, parent, Position);
            // Turned a little off the grid so the shells don't all face one way.
            root.localRotation = Quaternion.Euler(0f, 35f + index * 67f, 0f);

            var shell = SeaArt.Toon(Palette.Hex("#F3D9E6"), Palette.Hex("#8A5E86"), new Color(1f, 0.85f, 0.95f, 1f), Palette.Hex("#2A1630"), 0.012f);
            var mound = SeaArt.Toon(Palette.Hex("#2C4C78"), Palette.Hex("#132542"), new Color(0.6f, 0.85f, 1f, 1f), Palette.Hex("#0A1428"), 0.02f);
            Parts.Shape("Mound", root, SeaArt.Cylinder(0.85f, 0.6f, 0.14f, 12), mound);
            var cradle = Parts.Node("Cradle", root, new Vector3(0f, 0.14f, -0.42f));
            Parts.Shape("Under", cradle, SeaArt.Shell(0.85f, -0.2f), shell);
            lid = Parts.Node("Lid", root, new Vector3(0f, 0.16f, -0.42f));
            Parts.Shape("Over", lid, SeaArt.Shell(0.85f, 0.26f), shell);
            pool = SeaArt.Glow(SeaArt.Ring, Nacre * 0.55f);
            Parts.Shape("Pool", root, SeaArt.Decal(2.6f), pool, new Vector3(0f, 0.03f, 0f));

            // Up out of the open shell, clear of its lid. A shaded bead (mauve in its shade, a cold sheen on its rim)
            // in a soft glow: its own light under the lens's threshold, so it is never burnt out to a white disc.
            pearl = Parts.Node("Pearl", root, new Vector3(0f, Height, 0f));
            bead = Parts.Shape("Bead", pearl, SeaArt.Sphere(0.2f, 0f, 16, 10), SeaArt.Toon(Color.white, new Color(0.78f, 0.62f, 0.8f), Sheen, Nacre * 0.3f, 0.01f)).transform;
            glow = SeaArt.Glow(SeaArt.Flare, Nacre.WithAlpha(0.55f));
            flare = Parts.Shape("Glow", pearl, SeaArt.Quad(1.3f, 1.3f), glow).transform;
            flare.gameObject.AddComponent<Billboard>();
            halo = SeaArt.Glow(SeaArt.Ring, Sheen.WithAlpha(0.5f));
            Parts.Shape("Halo", pearl, SeaArt.Quad(0.9f, 0.9f), halo).gameObject.AddComponent<Billboard>();
            // Its core faint and pink-white: a white one as strong as the glow read lavender-white from across the hall.
            beam = SeaArt.Shaft(Pink.WithAlpha(0.5f), BeamCore, 0.15f, 2f, 0.4f, 1.4f, 0.3f);
            Parts.Shape("Beam", root, SeaArt.Ribbon(0.45f, 16), beam, new Vector3(0f, Height + 0.1f, 0f)).transform.localScale = new Vector3(1f, 8f, 1f);
            // Under the root, not the pearl, so it fades out behind it after the pearl has gone.
            trail = Parts.Node("Trail", root, new Vector3(0f, Height, 0f)).gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = SeaArt.Glow(SeaArt.Across, Nacre);
            trail.time = 0.25f;
            trail.widthMultiplier = 0.18f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;

            if (taken) Take(-10f);
        }

        // by: who took it, whose chest it flies into; none (built taken) and it just goes.
        public void Take(float time, Transform by = null)
        {
            Taken = true;
            takenAt = time;
            takenBy = by;
            takenFrom = pearl.position;
        }

        // True on the frame a taken pearl reaches the one who took it.
        public bool Animate(float time, Camera camera)
        {
            var since = takenAt < -5f ? 99f : Taken ? time - takenAt : -1f;
            // Open, breathing a little; closing over a second once the pearl is gone.
            var open = since < 0f ? 58f + 4f * Mathf.Sin(time * 0.9f + seed) : Mathf.Lerp(58f, 6f, Mathf.Clamp01((since - 0.3f) / 0.8f));
            lid.localRotation = Quaternion.Euler(-open, 0f, 0f);
            if (since >= 0f)
            {
                var fade = Mathf.Clamp01(1f - since / 0.8f);
                beam.SetColor("_Color", Pink.WithAlpha(0.5f * fade));
                beam.SetColor("_CoreColor", BeamCore.WithAlpha(BeamCore.a * fade));
                pool.SetColor("_Color", Nacre * (0.15f + 0.4f * fade));
            }
            if (since >= Fly)
            {
                pearl.gameObject.SetActive(false);
                trail.emitting = false;
                halo.SetColor("_Color", Sheen.WithAlpha(0.5f * Mathf.Clamp01(1f - since / 0.8f)));
                if (landed || takenBy == null) return false;
                landed = true;
                return true;
            }
            float size, light, swell;
            if (since < 0f)
            {
                pearl.localPosition = new Vector3(0f, Height + 0.08f * Mathf.Sin(time * 1.6f + seed), 0f);
                pearl.localRotation = Quaternion.Euler(0f, time * 40f, 0f);
                size = swell = 1f;
                light = 0.75f + 0.25f * Mathf.Sin(time * 3f + seed);
            }
            else
            {
                // Taken: along a curve up over the way between and down into the player's chest, wherever they have got
                // to, drawn in to half its size, its glow drawn in close round it. Not straight up past the head and
                // swelling, where a camera come in close over a rack had it a blob across the lens.
                var u = Mathf.SmoothStep(0f, 1f, since / Fly);
                var to = takenBy != null ? takenBy.position + Vector3.up * 1.1f : takenFrom + Vector3.up * 1.4f;
                var over = (takenFrom + to) / 2f + Vector3.up * 1.6f;
                pearl.position = Vector3.Lerp(Vector3.Lerp(takenFrom, over, u), Vector3.Lerp(over, to, u), u);
                size = 1f - 0.5f * u;
                swell = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(since / 0.15f));
                light = 0.8f;
                trail.emitting = true;
            }
            trail.transform.position = pearl.position;
            pearl.gameObject.SetActive(true);
            // Fading out as the lens comes within a couple of metres, the glow faster than the bead: from close by, the
            // flare quad would cover half the picture.
            var k = camera != null ? Mathf.Clamp01((Vector3.Distance(camera.transform.position, pearl.position) - 0.8f) / 1.7f) : 1f;
            bead.localScale = Vector3.one * (size * k);
            flare.localScale = Vector3.one * swell;
            glow.SetColor("_Color", Nacre.WithAlpha(0.55f * light * k * k));
            halo.SetColor("_Color", Sheen.WithAlpha(0.5f * Mathf.Clamp01(1f - Mathf.Max(0f, since) / 0.8f) * k * k));
            return false;
        }
    }
}
