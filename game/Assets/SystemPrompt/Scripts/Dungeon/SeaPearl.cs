using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // 数据珍珠, what there is to collect in the latent space: an open scallop shell on the seabed off the main
    // ways, a pearl of light turning over it in a glow, a thin beam to find it by from afar. Walking into one
    // takes it (DungeonScreen): the pearl rises, flares and goes, the shell closes and dims. One taken on an
    // earlier dive is built taken.
    public sealed class SeaPearl
    {
        public const float Reach = 1.8f;
        static readonly Color Nacre = new Color(1f, 0.82f, 0.95f), Sheen = new Color(0.6f, 0.9f, 1f);

        public readonly Vector2Int Cell;
        public readonly int Index;
        public readonly Vector3 Position;
        public bool Taken { get; private set; }

        readonly Transform root, lid, pearl;
        readonly Material glow, halo, beam, pool;
        readonly float seed;
        float takenAt = -1f;

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

            pearl = Parts.Node("Pearl", root, new Vector3(0f, 0.75f, 0f));
            Parts.Shape("Bead", pearl, SeaArt.Sphere(0.2f, 0f, 16, 10), SeaArt.Toon(Color.white, Nacre, Sheen, Nacre * 0.7f, 0.01f));
            glow = SeaArt.Glow(SeaArt.Flare, Nacre.WithAlpha(0.9f));
            Parts.Shape("Glow", pearl, SeaArt.Quad(1.3f, 1.3f), glow).gameObject.AddComponent<Billboard>();
            halo = SeaArt.Glow(SeaArt.Ring, Sheen.WithAlpha(0.5f));
            Parts.Shape("Halo", pearl, SeaArt.Quad(0.9f, 0.9f), halo).gameObject.AddComponent<Billboard>();
            beam = SeaArt.Shaft(Nacre.WithAlpha(0.3f), Color.white.WithAlpha(0.18f), 0.15f, 2f, 0.4f, 1.4f, 0.3f);
            Parts.Shape("Beam", root, SeaArt.Ribbon(0.45f, 16), beam, new Vector3(0f, 0.9f, 0f)).transform.localScale = new Vector3(1f, 8f, 1f);

            if (taken) Take(-10f);
        }

        public void Take(float time)
        {
            Taken = true;
            takenAt = time;
        }

        public void Animate(float time)
        {
            var since = takenAt < -5f ? 99f : Taken ? time - takenAt : -1f;
            // Open, breathing a little; closing over a second once the pearl is gone.
            var open = since < 0f ? 58f + 4f * Mathf.Sin(time * 0.9f + seed) : Mathf.Lerp(58f, 6f, Mathf.Clamp01((since - 0.3f) / 0.8f));
            lid.localRotation = Quaternion.Euler(-open, 0f, 0f);
            if (since < 0f)
            {
                pearl.localPosition = new Vector3(0f, 0.75f + 0.08f * Mathf.Sin(time * 1.6f + seed), 0f);
                pearl.localRotation = Quaternion.Euler(0f, time * 40f, 0f);
                var pulse = 0.75f + 0.25f * Mathf.Sin(time * 3f + seed);
                glow.SetColor("_Color", Nacre.WithAlpha(0.9f * pulse));
                return;
            }
            // Taken: up and out in a flare, then the glow under it and the beam fade.
            var up = Mathf.Clamp01(since / 0.45f);
            pearl.localPosition = new Vector3(0f, 0.75f + 1.4f * up * up, 0f);
            pearl.localScale = Vector3.one * Mathf.Max(0f, 1f + 0.8f * up - 1.8f * Mathf.Clamp01((since - 0.35f) / 0.2f));
            pearl.gameObject.SetActive(since < 0.6f);
            glow.SetColor("_Color", Nacre.WithAlpha(Mathf.Clamp01(1f - since / 0.6f)));
            var fade = Mathf.Clamp01(1f - since / 0.8f);
            beam.SetColor("_Color", Nacre.WithAlpha(0.3f * fade));
            beam.SetColor("_CoreColor", Color.white.WithAlpha(0.18f * fade));
            pool.SetColor("_Color", Nacre * (0.15f + 0.4f * fade));
            halo.SetColor("_Color", Sheen.WithAlpha(0.5f * fade));
        }
    }
}
