using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // A column of light standing up out of the seabed to the surface, streaks running up it, a halo round it, a pool
    // under it with rings going out over the seabed and motes going up: the deep's way out (SeaDecor.ExitLight, with its
    // 「出口」 sign on a dark plate) and the light that takes the two up from in front of the white door
    // (DungeonWorld.Depart). In the lighthouse's colour: only its light finds the way out. It stands there from the
    // start, or rises (Rise).
    public sealed class SeaColumn
    {
        public readonly Transform Root;
        readonly Transform column, halo, pool, ring, sign;
        readonly Material columnLight, haloLight, poolLight, ringLight, plate;
        readonly TextMesh words;
        readonly ParticleSystem motes;
        readonly Color color;
        float roseAt = -100f, riseSeconds = 1.2f;
        bool moving = true;

        const float Height = 70f, HaloHeight = 45f;
        static readonly Color Core = new Color(0.95f, 0.98f, 1f, 0.7f), Ink = new Color(0.04f, 0.06f, 0.12f, 0.85f);

        public SeaColumn(Transform parent, Vector3 at, Color color, float width = 5f, string label = null)
        {
            this.color = color;
            Root = Parts.Node("Column", parent, at);
            columnLight = SeaArt.Shaft(color.WithAlpha(0.55f), Core, 0.25f, 3f, 0.5f, 1.2f, 0.6f);
            column = Parts.Shape("Light", Root, SeaArt.Ribbon(width, 24), columnLight).transform;
            haloLight = SeaArt.Shaft(color.WithAlpha(0.16f), Color.clear, 0.2f, 1.5f, 0.3f, 1.8f, 1f);
            halo = Parts.Shape("Halo", Root, SeaArt.Ribbon(width * 2.6f, 24), haloLight).transform;
            poolLight = SeaArt.Glow(SeaArt.Atlas, color.WithAlpha(0.5f));
            poolLight.mainTextureScale = new Vector2(0.25f, 1f);
            poolLight.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            pool = Parts.Shape("Pool", Root, SeaArt.Decal(width * 1.8f), poolLight, new Vector3(0, 0.06f, 0)).transform;
            ringLight = SeaArt.Glow(SeaArt.Ring, color * 0.6f);
            ring = Parts.Shape("Ring", Root, SeaArt.Decal(1f), ringLight, new Vector3(0, 0.07f, 0)).transform;
            SeaArt.Particles(Root, "Motes", SeaArt.AtlasDot, SeaArt.AtlasDot, 24, new Vector3(width * 0.6f, 0.2f, width * 0.6f), 3.5f, 0.05f, 0.12f,
                color * 0.9f, 1.6f, 0.2f);
            motes = Root.Find("Motes").GetComponent<ParticleSystem>();
            if (label != null)
            {
                // The sign on a dark plate, so it reads against the white hall.
                sign = Parts.Node("Sign", Root, new Vector3(0, 5.2f, 0));
                sign.gameObject.AddComponent<Billboard>();
                plate = SeaArt.Glow(SeaArt.Atlas, Ink);
                plate.mainTextureScale = new Vector2(0.25f, 1f);
                plate.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasCard, 0f);
                plate.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                Parts.Shape("Plate", sign, SeaArt.Quad(2.2f, 1.1f), plate, new Vector3(0, 0, 0.02f));
                words = SeaArt.Text("Words", sign, label, 1.3f, new Color(0.85f, 0.95f, 1f, 0.95f));
            }
            Stand(1f);
        }

        // Up out of the seabed from the maze's `time` over `seconds`: the column grows to the surface, the halo and pool
        // come up, the sign half a second in.
        public void Rise(float time, float seconds = 1.2f)
        {
            roseAt = time;
            riseSeconds = seconds;
            Root.gameObject.SetActive(true);
            Stand(0f);
        }


        void Stand(float u)
        {
            // Slow out of the seabed, then away up: the few metres a camera sees of it take half the rise, so it reads
            // as coming up rather than being there.
            var up = u * u * u;
            var grown = Ease.OutCubic(u);
            column.localScale = new Vector3(1f, Mathf.Max(Height * up, 0.01f), 1f);
            halo.localScale = new Vector3(1f, Mathf.Max(HaloHeight * up, 0.01f), 1f);
            haloLight.SetColor("_Color", color.WithAlpha(0.16f * u));
            pool.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, grown);
            if (sign == null) return;
            var shown = Mathf.Clamp01((u * riseSeconds - 0.5f) / 0.4f);
            sign.gameObject.SetActive(shown > 0f);
            plate.SetColor("_Color", Ink.WithAlpha(Ink.a * shown));
            words.color = new Color(0.85f, 0.95f, 1f, 0.95f * shown);
        }

        public void Animate(float time)
        {
            if (!Root.gameObject.activeInHierarchy) return;
            var since = time - roseAt;
            if (since < riseSeconds + 0.5f) Stand(Mathf.Clamp01(since / riseSeconds));
            var k = Mathf.Repeat(time / 2.2f, 1f);
            ring.localScale = Vector3.one * (2f + 6f * k);
            ringLight.SetColor("_Color", color * (0.6f * (1f - k)));
            // Motes are background motion (设置: 背景动态).
            var move = GameRoot.Instance == null || GameRoot.Instance.Settings.backgroundMotion;
            if (move != moving)
            {
                moving = move;
                var emission = motes.emission;
                emission.enabled = move;
            }
        }
    }
}
