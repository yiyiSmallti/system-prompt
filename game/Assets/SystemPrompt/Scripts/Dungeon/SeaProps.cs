using UnityEngine;

namespace SystemPrompt.Dungeon
{
    public enum PropKind { Fragment, Chest, Terminal, Beacon, Console, Goal }

    // Something on the map the player walks up to: a story fragment, a chest, the maintenance terminal,
    // a beacon, the console that puts the cache blocks back, or the goal. Index counts the props of one
    // kind in map reading order.
    public sealed class SeaProp
    {
        public readonly PropKind Kind;
        public readonly Vector2Int Cell;
        public readonly int Index;
        public readonly Vector3 Position;
        public readonly Transform Root;
        public bool Used;

        static readonly Color Rested = new Color(0.45f, 1f, 0.85f, 1f); // a beacon after the player has rested at it

        readonly Material glow;
        readonly Material beam;
        readonly Transform floater;
        readonly Transform lid;
        readonly Transform sweep, ripple;
        readonly Material sweepLight, bands, rippleLight;
        float usedAt = -1f;
        readonly Color color;

        public float Reach => Kind == PropKind.Goal ? 0f : Kind == PropKind.Beacon ? 3f : 2.6f;

        public string Verb => Kind switch
        {
            PropKind.Fragment => "调查",
            PropKind.Chest => "打开",
            PropKind.Terminal => "接入",
            PropKind.Beacon => "休息",
            PropKind.Console => "缓存块复位",
            _ => "",
        };

        public SeaProp(PropKind kind, Vector2Int cell, int index, DungeonMap map, Transform parent)
        {
            Kind = kind;
            Cell = cell;
            Index = index;
            Position = map.World(cell);
            Root = Parts.Node(kind + " " + index, parent, Position);
            switch (kind)
            {
                case PropKind.Fragment:
                {
                    // A chat bubble afloat over a ring on the seabed, with a thin beam to find it from afar.
                    color = new Color(0.45f, 0.75f, 1f, 1f);
                    floater = Parts.Node("Float", Root, new Vector3(0, 1.7f, 0));
                    glow = SeaArt.Glow(SeaArt.Atlas, color);
                    glow.mainTextureScale = new Vector2(0.25f, 1f);
                    glow.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasBubble, 0f);
                    Parts.Shape("Bubble", floater, SeaArt.Quad(1.7f, 1.7f), glow);
                    var halo = SeaArt.Glow(SeaArt.Atlas, color * 0.35f);
                    halo.mainTextureScale = new Vector2(0.25f, 1f);
                    halo.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
                    Parts.Shape("Halo", floater, SeaArt.Quad(4f, 4f), halo, new Vector3(0, 0, 0.05f));
                    Ring(1.8f, color);
                    Beam(color, 0.7f, 10f, 0.1f);
                    break;
                }
                case PropKind.Chest:
                {
                    color = new Color(1f, 0.76f, 0.3f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#2B3C66"), Palette.Hex("#131B33"), new Color(1f, 0.78f, 0.3f, 1f), Palette.Hex("#0A1020"));
                    Parts.Shape("Base", Root, SeaArt.Box(new Vector3(1.2f, 0.62f, 0.8f)), shell, new Vector3(0, 0.31f, 0));
                    lid = Parts.Node("Lid", Root, new Vector3(0, 0.62f, -0.4f));
                    Parts.Shape("Top", lid, SeaArt.Box(new Vector3(1.24f, 0.2f, 0.84f)), shell, new Vector3(0, 0.1f, 0.4f));
                    glow = SeaArt.Glow(SeaArt.White, new Color(1f, 0.8f, 0.35f, 0.9f));
                    Parts.Shape("Seam", Root, SeaArt.Box(new Vector3(1.26f, 0.05f, 0.86f)), glow, new Vector3(0, 0.62f, 0));
                    Ring(2.2f, color);
                    break;
                }
                case PropKind.Terminal:
                {
                    color = new Color(0.3f, 1f, 0.6f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#22324F"), Palette.Hex("#0F1729"), new Color(0.3f, 1f, 0.6f, 1f), Palette.Hex("#08101C"));
                    Parts.Shape("Pillar", Root, SeaArt.Box(new Vector3(0.7f, 1.5f, 0.5f)), shell, new Vector3(0, 0.75f, 0));
                    glow = SeaArt.Glow(SeaArt.White, new Color(0.3f, 1f, 0.6f, 0.75f));
                    var screen = Parts.Shape("Screen", Root, SeaArt.Quad(0.56f, 0.4f), glow, new Vector3(0, 1.25f, 0.26f));
                    screen.transform.localRotation = Quaternion.Euler(-20f, 180f, 0f);
                    Ring(2.2f, color);
                    Beam(color, 0.8f, 12f, 0.1f);
                    break;
                }
                case PropKind.Console:
                {
                    color = new Color(1f, 0.66f, 0.2f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#22324F"), Palette.Hex("#0F1729"), color, Palette.Hex("#08101C"));
                    Parts.Shape("Desk", Root, SeaArt.Box(new Vector3(1.1f, 1f, 0.7f)), shell, new Vector3(0, 0.5f, 0));
                    glow = SeaArt.Glow(SeaArt.White, new Color(1f, 0.66f, 0.2f, 0.7f));
                    var screen = Parts.Shape("Screen", Root, SeaArt.Quad(0.9f, 0.5f), glow, new Vector3(0, 1.15f, 0.1f));
                    screen.transform.localRotation = Quaternion.Euler(-35f, 180f, 0f);
                    SeaArt.Text("Words", Root, "RESET", 0.18f, new Color(1f, 0.85f, 0.6f, 1f), new Vector3(0, 1.16f, 0.08f)).transform.localRotation = Quaternion.Euler(-35f, 180f, 0f);
                    Ring(2.2f, color);
                    break;
                }
                case PropKind.Beacon:
                {
                    // A small lighthouse, the safe point of the area: an eight-sided tower on a plinth with bands
                    // of light round it, a lamp in a glass room under a pointed roof, two beams sweeping round
                    // from the lamp, a shaft of light standing up out of the roof and rings going out over the
                    // seabed.
                    color = new Color(0.35f, 0.7f, 1f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#1C2A48"), Palette.Hex("#0C1426"), new Color(0.4f, 0.8f, 1f, 1f), Palette.Hex("#060C18"));
                    var trim = SeaArt.Toon(Palette.Hex("#C9D6EE"), Palette.Hex("#6A7A9A"), Color.white, Palette.Hex("#141C2C"), 0.03f);
                    Parts.Shape("Plinth", Root, SeaArt.Cylinder(1.05f, 0.95f, 0.32f, 8), shell);
                    Parts.Shape("Tower", Root, SeaArt.Cylinder(0.46f, 0.32f, 1.75f, 8), shell, new Vector3(0, 0.32f, 0));
                    Parts.Shape("Gallery", Root, SeaArt.Cylinder(0.5f, 0.5f, 0.1f, 8), trim, new Vector3(0, 2.07f, 0));
                    bands = SeaArt.Glow(SeaArt.White, color.WithAlpha(0.7f));
                    Parts.Shape("Band", Root, SeaArt.Tube(0.98f, 0.98f, 0.06f, 8), bands, new Vector3(0, 0.24f, 0));
                    Parts.Shape("Band", Root, SeaArt.Tube(0.425f, 0.425f, 0.06f, 8), bands, new Vector3(0, 0.92f, 0));
                    Parts.Shape("Band", Root, SeaArt.Tube(0.375f, 0.375f, 0.06f, 8), bands, new Vector3(0, 1.52f, 0));
                    Parts.Shape("Glass", Root, SeaArt.Tube(0.33f, 0.33f, 0.5f, 8), SeaArt.Glow(SeaArt.White, new Color(0.6f, 0.85f, 1f, 0.18f)), new Vector3(0, 2.17f, 0));
                    glow = SeaArt.Toon(color, color, Color.white, color * 0.6f, 0.01f);
                    Parts.Shape("Lamp", Root, SeaArt.Sphere(0.2f), glow, new Vector3(0, 2.42f, 0));
                    Parts.Shape("Roof", Root, SeaArt.Cone(0.48f, 0.4f, 8), trim, new Vector3(0, 2.67f, 0));
                    sweep = Parts.Node("Sweep", Root, new Vector3(0, 2.42f, 0));
                    sweepLight = SeaArt.Shaft(color.WithAlpha(0.4f), Color.white.WithAlpha(0.35f), 0.12f, 4f, 0.4f, 1.6f, 0.3f);
                    for (var k = 0; k < 2; k++)
                    {
                        var arm = Parts.Shape("Arm", sweep, SeaArt.Ribbon(0.9f), sweepLight).transform;
                        arm.localRotation = Quaternion.Euler(0, k * 180f, -90f);
                        arm.localScale = new Vector3(1f, 6f, 1f);
                    }
                    beam = Beam(color, 1.5f, 18f, 3f);
                    Ring(3f, color);
                    rippleLight = SeaArt.Glow(SeaArt.Ring, color.WithAlpha(0.6f));
                    ripple = Parts.Shape("Ripple", Root, SeaArt.Decal(1f), rippleLight, new Vector3(0, 0.05f, 0)).transform;
                    break;
                }
                default:
                {
                    color = new Color(1f, 1f, 1f, 1f);
                    Ring(3.6f, color * 0.8f);
                    break;
                }
            }
        }

        void Ring(float size, Color c)
        {
            var m = SeaArt.Glow(SeaArt.Ring, c * 0.6f);
            Parts.Shape("Ring", Root, SeaArt.Decal(size), m, new Vector3(0, 0.04f, 0));
        }

        // A shaft of light standing up from the prop to find it by from afar.
        Material Beam(Color c, float width, float length, float from)
        {
            var m = SeaArt.Shaft(c.WithAlpha(0.35f), Color.white.WithAlpha(0.2f), 0.15f, 2f, 0.4f, 1.4f, 0.3f);
            Parts.Shape("Beam", Root, SeaArt.Ribbon(width, 16), m, new Vector3(0, from, 0)).transform.localScale = new Vector3(1f, length, 1f);
            return m;
        }

        public void Use(float time)
        {
            if (Kind == PropKind.Beacon || Kind == PropKind.Console)
            {
                // A beacon or console stays usable: using it again just lights it up again.
                usedAt = time;
                Used = true;
                return;
            }
            Used = true;
            usedAt = time;
        }

        public void Animate(float time, Camera camera)
        {
            var since = usedAt < 0f ? -1f : time - usedAt;
            switch (Kind)
            {
                case PropKind.Fragment:
                    floater.localPosition = new Vector3(0, 1.7f + Mathf.Sin(time * 1.3f + Index) * 0.18f, 0);
                    if (camera != null)
                    {
                        var to = camera.transform.position - floater.position;
                        to.y = 0f;
                        if (to.sqrMagnitude > 0.01f) floater.rotation = Quaternion.LookRotation(-to.normalized);
                    }
                    glow.SetColor("_Color", Used ? color * 0.3f : color * (0.8f + 0.2f * Mathf.Sin(time * 3f)));
                    if (Used && since < 1f) floater.localScale = Vector3.one * (1f + 0.6f * (1f - since));
                    break;
                case PropKind.Chest:
                    if (Used) lid.localRotation = Quaternion.Euler(-Mathf.Min(1f, since / 0.45f) * 105f, 0, 0);
                    glow.SetColor("_Color", Used ? new Color(1f, 0.8f, 0.35f, 0.15f) : new Color(1f, 0.8f, 0.35f, 0.6f + 0.3f * Mathf.Sin(time * 2.5f)));
                    break;
                case PropKind.Terminal:
                    glow.SetColor("_Color", Used ? new Color(0.35f, 0.85f, 1f, 0.9f) : new Color(0.3f, 1f, 0.6f, 0.45f + 0.35f * Mathf.Abs(Mathf.Sin(time * 4f))));
                    break;
                case PropKind.Console:
                    glow.SetColor("_Color", new Color(1f, 0.66f, 0.2f, since >= 0f && since < 0.6f ? 1f : 0.5f + 0.2f * Mathf.Sin(time * 3f)));
                    break;
                case PropKind.Beacon:
                {
                    // Resting lights it up for a moment and sends a ring out at once; after that it stays green.
                    var lit = Used ? Rested : color;
                    var flash = since >= 0f && since < 1.2f ? 1f - since / 1.2f : 0f;
                    var pulse = 1f + flash * 2f;
                    glow.SetColor("_Emission", lit * 0.7f * pulse);
                    beam.SetColor("_Color", lit.WithAlpha((Used ? 0.55f : 0.45f) * (1f + flash)));
                    beam.SetColor("_CoreColor", Color.white.WithAlpha((Used ? 0.4f : 0.3f) * (1f + flash)));
                    sweep.localRotation = Quaternion.Euler(0, time * 50f, 0);
                    sweepLight.SetColor("_Color", lit.WithAlpha(0.4f * pulse));
                    bands.SetColor("_Color", lit.WithAlpha(0.55f + 0.2f * Mathf.Sin(time * 2f) + 0.3f * flash));
                    var k = since >= 0f && since < 2.4f ? since / 2.4f : Mathf.Repeat(time / 2.4f, 1f);
                    ripple.localScale = new Vector3(2f + 7f * k, 1f, 2f + 7f * k);
                    rippleLight.SetColor("_Color", lit.WithAlpha(0.7f * (1f - k) * (1f - k)));
                    break;
                }
            }
        }
    }

    // A wall of data blocks across a corridor: red, the firewall to the lighthouse until the terminal is
    // used; amber, the gate to the terminal until its sockets are full.
    public sealed class Firewall
    {
        readonly Transform root;
        readonly Material[] layers = new Material[2];
        readonly Color colour;
        float openedAt = -1f;

        public Firewall(Vector3 position, bool alongX, Transform parent) : this(position, alongX, parent, new Color(1f, 0.22f, 0.25f)) { }

        public Firewall(Vector3 position, bool alongX, Transform parent, Color colour)
        {
            this.colour = colour;
            root = Parts.Node("Firewall", parent, position);
            if (!alongX) root.localRotation = Quaternion.Euler(0, 90f, 0);
            for (var i = 0; i < layers.Length; i++)
            {
                layers[i] = SeaArt.Glow(SeaArt.Firewall, colour.WithAlpha(0.55f - i * 0.2f));
                layers[i].mainTextureScale = new Vector2(2f, 3.5f);
                layers[i].SetVector("_Scroll", new Vector4(0f, i == 0 ? 0.12f : -0.07f, 0, 0));
                Parts.Shape("Layer", root, SeaArt.Quad(Dungeons.Cell, 7f), layers[i], new Vector3(0, 3.5f, i * 0.3f - 0.15f));
            }
        }

        public void Open(float time) => openedAt = time;

        public void Animate(float time)
        {
            if (openedAt < 0f) return;
            var t = Mathf.Clamp01((time - openedAt) / 0.9f);
            for (var i = 0; i < layers.Length; i++)
                layers[i].SetColor("_Color", Color.Lerp(colour, Color.white, t * 0.75f).WithAlpha((0.55f - i * 0.2f) * (1f - t)));
            root.localScale = new Vector3(1f, 1f - t * 0.95f, 1f);
            if (t >= 1f) root.gameObject.SetActive(false);
        }
    }
}
