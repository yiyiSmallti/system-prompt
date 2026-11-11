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

        public static readonly Color Rested = new Color(0.45f, 1f, 0.85f, 1f); // a beacon after the player has rested at it
        public static readonly Color Gold = new Color(1f, 0.8f, 0.35f, 1f);    // a chest's light
        public const float LampHeight = 2.42f; // a beacon's lamp

        readonly Material glow;
        readonly Material beam;
        readonly Transform floater;
        readonly Transform lid;
        readonly Transform sweep, ripple;
        readonly Material sweepLight, bands, rippleLight;
        readonly Transform uplink;     // the terminal's shaft of light sent up as it is used
        readonly Material uplinkLight;
        readonly Material flare;       // a beacon lamp's halo
        readonly Transform burst;      // a chest's gold shaft and cards, thrown up as it opens
        readonly Transform[] cards;
        readonly Material burstLight, cardLook;
        float usedAt = -1f, litAt = -1f;
        readonly Color color;

        // 减弱闪光: the beacon's flash on resting goes half as bright.
        static bool Calm => GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;

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
                    // A data chest: a bevelled case on four feet, an arched lid hinged at the back, two steel straps
                    // over both, a lock plate with a lit keyhole in front, and a band of light where the lid closes.
                    color = new Color(1f, 0.76f, 0.3f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#2B3C66"), Palette.Hex("#131B33"), new Color(1f, 0.78f, 0.3f, 1f), Palette.Hex("#0A1020"));
                    var steel = SeaArt.Toon(Palette.Hex("#C9D6EE"), Palette.Hex("#6A7A9A"), new Color(1f, 0.85f, 0.5f, 1f), Palette.Hex("#141C2C"), 0.02f);
                    for (var i = 0; i < 4; i++)
                        Parts.Shape("Foot", Root, SeaArt.Box(new Vector3(0.18f, 0.1f, 0.18f)), steel, new Vector3((i & 1) * 0.92f - 0.46f, 0.05f, (i >> 1) * 0.56f - 0.28f));
                    Parts.Shape("Case", Root, SeaArt.Box(new Vector3(1.16f, 0.52f, 0.76f)), shell, new Vector3(0, 0.36f, 0));
                    lid = Parts.Node("Lid", Root, new Vector3(0, 0.62f, -0.39f));
                    Parts.Shape("Top", lid, SeaArt.Prism(SeaArt.Arch(0.8f, 0.26f), 1.2f), shell, new Vector3(0, 0f, 0.39f));
                    foreach (var x in new[] { -0.36f, 0.36f })
                    {
                        Parts.Shape("Strap", Root, SeaArt.Box(new Vector3(0.1f, 0.54f, 0.8f)), steel, new Vector3(x, 0.36f, 0));
                        Parts.Shape("Strap", lid, SeaArt.Prism(SeaArt.Arch(0.84f, 0.29f), 0.1f), steel, new Vector3(x, 0f, 0.39f));
                    }
                    Parts.Shape("Lock", Root, SeaArt.Box(new Vector3(0.24f, 0.22f, 0.06f)), steel, new Vector3(0, 0.5f, 0.4f));
                    glow = SeaArt.Glow(SeaArt.White, new Color(1f, 0.8f, 0.35f, 0.9f));
                    Parts.Shape("Keyhole", Root, SeaArt.Quad(0.06f, 0.11f), glow, new Vector3(0, 0.49f, 0.435f)).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    Parts.Shape("Seam", Root, SeaArt.Box(new Vector3(1.19f, 0.04f, 0.79f)), glow, new Vector3(0, 0.62f, 0));
                    Ring(2.2f, color);
                    // Thrown up out of it as it opens: a short gold shaft and three of the sea's cards rising in it.
                    burst = Parts.Node("Burst", Root, new Vector3(0, 0.62f, 0));
                    burstLight = SeaArt.Shaft(Gold.WithAlpha(0.45f), new Color(1f, 0.95f, 0.8f, 0.6f), 0.15f, 3f, 0.6f, 1.2f, 0.2f);
                    Parts.Shape("Shaft", burst, SeaArt.Ribbon(0.7f), burstLight).transform.localScale = new Vector3(1f, 2.8f, 1f);
                    cardLook = SeaArt.Glow(SeaArt.Atlas, Color.white);
                    cardLook.mainTextureScale = new Vector2(0.25f, 1f);
                    cardLook.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasCard, 0f);
                    cards = new Transform[3];
                    for (var i = 0; i < cards.Length; i++)
                    {
                        cards[i] = Parts.Shape("Card", burst, SeaArt.Quad(0.34f, 0.34f), cardLook).transform;
                        cards[i].gameObject.AddComponent<Billboard>();
                    }
                    burst.gameObject.SetActive(false);
                    break;
                }
                case PropKind.Terminal:
                {
                    // The maintenance terminal: an eight-sided plinth, a tapering column with a lit stripe, a monitor
                    // housing tipped back toward whoever walks up, and a keyboard ledge under it.
                    color = new Color(0.3f, 1f, 0.6f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#22324F"), Palette.Hex("#0F1729"), new Color(0.3f, 1f, 0.6f, 1f), Palette.Hex("#08101C"));
                    var trim = SeaArt.Toon(Palette.Hex("#C9D6EE"), Palette.Hex("#6A7A9A"), Color.white, Palette.Hex("#141C2C"), 0.02f);
                    Parts.Shape("Plinth", Root, SeaArt.Cylinder(0.56f, 0.5f, 0.16f, 8), shell);
                    Parts.Shape("Column", Root, SeaArt.Cylinder(0.22f, 0.16f, 0.86f, 6), shell, new Vector3(0, 0.16f, 0));
                    var head = Parts.Node("Head", Root, new Vector3(0, 1.24f, 0.04f));
                    head.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                    Parts.Shape("Housing", head, SeaArt.Box(new Vector3(0.78f, 0.56f, 0.2f)), shell);
                    Parts.Shape("Back", head, SeaArt.Box(new Vector3(0.5f, 0.36f, 0.16f)), shell, new Vector3(0, 0f, -0.16f));
                    Parts.Shape("Bezel", head, SeaArt.Box(new Vector3(0.68f, 0.46f, 0.03f)), trim, new Vector3(0, 0f, 0.1f));
                    glow = SeaArt.Glow(SeaArt.White, new Color(0.3f, 1f, 0.6f, 0.75f));
                    Parts.Shape("Screen", head, SeaArt.Quad(0.58f, 0.38f), glow, new Vector3(0, 0f, 0.12f)).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    var ledge = Parts.Shape("Keys", Root, SeaArt.Box(new Vector3(0.62f, 0.06f, 0.26f)), trim, new Vector3(0, 0.9f, 0.24f)).transform;
                    ledge.localRotation = Quaternion.Euler(14f, 0f, 0f);
                    Parts.Shape("Stripe", Root, SeaArt.Box(new Vector3(0.05f, 0.7f, 0.05f)), glow, new Vector3(0, 0.55f, 0.18f));
                    Ring(2.2f, color);
                    Beam(color, 0.8f, 12f, 0.1f);
                    uplinkLight = SeaArt.Shaft(new Color(0.45f, 0.85f, 1f, 0.5f), new Color(0.9f, 0.97f, 1f, 0.8f), 0.15f, 6f, 0.8f);
                    uplink = Parts.Shape("Uplink", Root, SeaArt.Ribbon(0.5f), uplinkLight, new Vector3(0, 1.6f, 0)).transform;
                    uplink.localScale = new Vector3(1f, 40f, 1f);
                    uplink.gameObject.SetActive(false);
                    break;
                }
                case PropKind.Console:
                {
                    // The reset console: a bevelled cabinet with a dark kick plate, a desk sloping down toward the
                    // player with the screen set into it, and a big round button beside the screen.
                    color = new Color(1f, 0.66f, 0.2f, 1f);
                    var shell = SeaArt.Toon(Palette.Hex("#22324F"), Palette.Hex("#0F1729"), color, Palette.Hex("#08101C"));
                    var dark = SeaArt.Toon(Palette.Hex("#0F1729"), Palette.Hex("#070C16"), Color.clear, Palette.Hex("#04070E"));
                    var trim = SeaArt.Toon(Palette.Hex("#C9D6EE"), Palette.Hex("#6A7A9A"), color, Palette.Hex("#141C2C"), 0.02f);
                    Parts.Shape("Cabinet", Root, SeaArt.Box(new Vector3(1.04f, 0.78f, 0.62f)), shell, new Vector3(0, 0.39f, -0.02f));
                    Parts.Shape("Kick", Root, SeaArt.Box(new Vector3(0.9f, 0.14f, 0.06f)), dark, new Vector3(0, 0.09f, 0.29f));
                    Parts.Shape("Desk", Root, SeaArt.Prism(new[] { new Vector2(-0.36f, 0f), new Vector2(0.36f, 0f), new Vector2(0.38f, 0.07f), new Vector2(-0.34f, 0.36f) }, 1.12f),
                        trim, new Vector3(0, 0.76f, 0f));
                    glow = SeaArt.Glow(SeaArt.White, new Color(1f, 0.66f, 0.2f, 0.7f));
                    // On the slope: it rises 0.29 m over 0.72 m toward the back, so the screen's top leans away.
                    var tilt = Mathf.Atan2(0.29f, 0.72f) * Mathf.Rad2Deg;
                    var slope = Quaternion.Euler(tilt, 180f, 0f);
                    var screen = Parts.Shape("Screen", Root, SeaArt.Quad(0.62f, 0.38f), glow, new Vector3(-0.14f, 0.983f, 0.023f));
                    screen.transform.localRotation = slope;
                    SeaArt.Text("Words", Root, "RESET", 0.14f, new Color(1f, 0.85f, 0.6f, 1f), new Vector3(-0.14f, 0.992f, 0.027f)).transform.localRotation = slope;
                    var button = Parts.Shape("Button", Root, SeaArt.Cylinder(0.11f, 0.09f, 0.06f, 12), SeaArt.Toon(color, color * 0.6f, Color.white, color * 1.6f, 0.01f),
                        new Vector3(0.36f, 0.94f, 0.1f));
                    button.transform.localRotation = Quaternion.Euler(tilt, 0f, 0f);
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
                    // The lamp a lit ball with its shading showing, its glow a halo over it (past the lens's threshold
                    // only at the halo's heart) rather than emission burning the ball out to a white disc.
                    glow = SeaArt.Toon(color * 0.85f, color * 0.45f, Color.white * 0.6f, color * 0.3f, 0f);
                    Parts.Shape("Lamp", Root, SeaArt.Sphere(0.2f), glow, new Vector3(0, LampHeight, 0));
                    flare = SeaArt.Glow(SeaArt.Flare, color * 0.7f);
                    Parts.Shape("Halo", Root, SeaArt.Quad(1.8f, 1.8f), flare, new Vector3(0, LampHeight, 0)).gameObject.AddComponent<Billboard>();
                    Parts.Shape("Roof", Root, SeaArt.Cone(0.48f, 0.4f, 8), trim, new Vector3(0, 2.67f, 0));
                    sweep = Parts.Node("Sweep", Root, new Vector3(0, LampHeight, 0));
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
                if (!Used) litAt = time;
                usedAt = time;
                Used = true;
                return;
            }
            Used = true;
            usedAt = time;
            if (Kind == PropKind.Chest) burst.gameObject.SetActive(true);
            if (Kind == PropKind.Terminal)
            {
                // Its log starts scrolling past, and it sends its light up to the walls it opens.
                glow.mainTexture = SeaArt.Log;
                glow.mainTextureScale = new Vector2(1f, 2f);
                glow.SetVector("_Scroll", new Vector4(0f, -0.6f, 0f, 0f));
                uplink.gameObject.SetActive(true);
            }
        }

        // The terminal's talk over: its log goes on scrolling, slowly.
        public void Settle()
        {
            if (Kind == PropKind.Terminal) glow.SetVector("_Scroll", new Vector4(0f, -0.1f, 0f, 0f));
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
                {
                    // Opened over 0.5 s: the lid pressed 4° down first, then up past its 105° and back a little. The seam
                    // flares as the lid parts and dies down to an ember over 0.6 s.
                    var seam = 0.6f + 0.3f * Mathf.Sin(time * 2.5f);
                    if (Used)
                    {
                        var u = since / 0.5f;
                        var angle = u < 0.16f ? -4f * Mathf.Sin(u / 0.16f * Mathf.PI) : 105f * Ease.OutBack(Mathf.Min(1f, (u - 0.16f) / 0.84f));
                        lid.localRotation = Quaternion.Euler(-angle, 0, 0);
                        seam = u < 0.16f ? 0.6f : Mathf.Lerp(1f, 0.15f, Mathf.Clamp01((since - 0.08f) / 0.6f));
                    }
                    glow.SetColor("_Color", Gold.WithAlpha(seam));
                    if (burst.gameObject.activeSelf)
                    {
                        // From 0.12 s, once the lid has come up off its press and parted (nothing before, or the cards
                        // show over a shut lid): the shaft in over 0.1 s and out from 0.7 s to 1.2 s; the cards rise
                        // 1.2 m over 0.9 s, fading.
                        var b = since - 0.12f;
                        var a = Mathf.Min(Mathf.Clamp01(b / 0.1f), 1f - Mathf.Clamp01((b - 0.7f) / 0.5f));
                        burstLight.SetColor("_Color", Gold.WithAlpha(0.45f * a));
                        burstLight.SetColor("_CoreColor", new Color(1f, 0.95f, 0.8f, 0.6f * a));
                        var k = Mathf.Clamp01(b / 0.9f);
                        var rise = Ease.OutCubic(k);
                        for (var i = 0; i < cards.Length; i++)
                            cards[i].localPosition = new Vector3((i - 1) * (0.2f + 0.25f * rise), 0.1f + (1.2f - 0.25f * Mathf.Abs(i - 1)) * rise, (i - 1) * 0.05f);
                        cardLook.SetColor("_Color", new Color(1f, 0.95f, 0.85f, 0.9f * Mathf.Min(Mathf.Clamp01(b / 0.1f), 1f - Mathf.Clamp01((b - 0.5f) / 0.4f))));
                        if (b >= 1.2f) burst.gameObject.SetActive(false);
                    }
                    break;
                }
                case PropKind.Terminal:
                    glow.SetColor("_Color", Used ? new Color(0.35f, 0.85f, 1f, 0.9f) : new Color(0.3f, 1f, 0.6f, 0.45f + 0.35f * Mathf.Abs(Mathf.Sin(time * 4f))));
                    if (uplink.gameObject.activeSelf)
                    {
                        // Up in 0.15 s, held, gone over 0.6 s.
                        var a = Mathf.Min(Mathf.Clamp01(since / 0.15f), 1f - Mathf.Clamp01((since - 0.6f) / 0.6f));
                        uplinkLight.SetColor("_Color", new Color(0.45f, 0.85f, 1f, 0.5f * a));
                        uplinkLight.SetColor("_CoreColor", new Color(0.9f, 0.97f, 1f, 0.8f * a));
                        if (since >= 1.2f) uplink.gameObject.SetActive(false);
                    }
                    break;
                case PropKind.Console:
                    glow.SetColor("_Color", new Color(1f, 0.66f, 0.2f, since >= 0f && since < 0.6f ? 1f : 0.5f + 0.2f * Mathf.Sin(time * 3f)));
                    break;
                case PropKind.Beacon:
                {
                    // Resting lights it up for a moment and sends a ring out at once; its colour turns to green over
                    // 0.8 s the first time, and stays.
                    var lit = Color.Lerp(color, Rested, Used ? Mathf.SmoothStep(0f, 1f, (time - litAt) / 0.8f) : 0f);
                    // Halved with reduced flashing (Calm), the beam and its core included.
                    var flash = (since >= 0f && since < 1.2f ? 1f - since / 1.2f : 0f) * (Calm ? 0.5f : 1f);
                    var pulse = 1f + 2f * flash;
                    var breath = Mathf.Max(flash, 0.5f + 0.5f * Mathf.Sin(time * 2f));
                    glow.SetColor("_Emission", lit * (0.22f + 0.12f * breath));
                    flare.SetColor("_Color", (lit * 0.7f).WithAlpha(Mathf.Lerp(0.45f, 0.75f, breath)));
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
    // used; amber, the gate to the terminal until its sockets are full. Opened, it washes out to white with its
    // data running fast, then breaks up in square cells from the top down (Shaders/Dissolve.shader) into pixels
    // drifting up, lighting the frame round it as it goes; seen from a cutaway (DungeonScreen.Reveal) a shaft of light
    // drops onto it first, the terminal's answer coming down.
    public sealed class Firewall
    {
        public readonly Vector3 Position;
        readonly Transform root, layerRoot, drop;
        readonly Material[] layers = new Material[2];
        readonly Material dropLight;
        readonly Color colour, edge, light;
        readonly bool alongX;
        SeaGlims glims;
        SeaWake wake;
        float openedAt = -1f;
        bool burst;

        const float Height = 7f, DropLength = 40f;

        public bool Opened => openedAt >= 0f;

        public Firewall(Vector3 position, bool alongX, Transform parent) : this(position, alongX, parent, new Color(1f, 0.22f, 0.25f)) { }

        public Firewall(Vector3 position, bool alongX, Transform parent, Color colour)
        {
            Position = position;
            this.colour = colour;
            this.alongX = alongX;
            var amber = colour.g > 0.5f;
            edge = amber ? new Color(1f, 0.8f, 0.45f) : new Color(0.66f, 1.05f, 1.1f);
            light = amber ? new Color(1f, 0.75f, 0.35f) : new Color(0.35f, 1f, 0.6f);
            root = Parts.Node("Firewall", parent, position);
            if (!alongX) root.localRotation = Quaternion.Euler(0, 90f, 0);
            layerRoot = Parts.Node("Layers", root);
            for (var i = 0; i < layers.Length; i++)
            {
                var m = layers[i] = SeaArt.Plain("Dissolve");
                m.mainTexture = SeaArt.Firewall;
                m.mainTextureScale = new Vector2(2f, 3.5f);
                m.SetColor("_Color", colour.WithAlpha(0.55f - i * 0.2f));
                m.SetVector("_Scroll", new Vector4(0f, i == 0 ? 0.12f : -0.07f, 0, 0));
                m.SetColor("_EdgeColor", edge);
                // The back layer's cells a little off the front's, so the two don't break up as one.
                m.SetVector("_Cells", new Vector4(16f + i * 3f, 28f + i * 5f, 0, 0));
                Parts.Shape("Layer", layerRoot, SeaArt.Quad(Dungeons.Cell, Height), m, new Vector3(0, Height / 2f, i * 0.3f - 0.15f));
            }
            // Hung upside down from high over the wall, it grows down onto the wall's top.
            dropLight = SeaArt.Shaft(new Color(0.45f, 0.85f, 1f, 0.5f), new Color(0.9f, 0.97f, 1f, 0.8f), 0.15f, 6f, 0.8f);
            drop = Parts.Shape("Drop", root, SeaArt.Ribbon(0.5f), dropLight, new Vector3(0, Height + DropLength, 0)).transform;
            drop.localRotation = Quaternion.Euler(0, 0, 180f);
            drop.gameObject.SetActive(false);
        }

        // From the maze's `time` on: glims and wake light its frame and take its pixels; beam: the shaft drops onto it first.
        public void Open(float time, SeaGlims glims, SeaWake wake, bool beam = false)
        {
            if (Opened) return;
            openedAt = time;
            this.glims = glims;
            this.wake = wake;
            drop.gameObject.SetActive(beam);
            if (beam) drop.localScale = new Vector3(1f, 0f, 1f);
        }

        public void Animate(float time)
        {
            if (!Opened || !root.gameObject.activeSelf) return;
            var since = time - openedAt;
            if (drop.gameObject.activeSelf)
            {
                // Down in a quarter second, held, gone after a second.
                drop.localScale = new Vector3(1f, DropLength * Ease.OutCubic(Mathf.Clamp01(since / 0.25f)), 1f);
                var a = 1f - Mathf.Clamp01((since - 0.9f) / 0.4f);
                dropLight.SetColor("_Color", new Color(0.45f, 0.85f, 1f, 0.5f * a));
                dropLight.SetColor("_CoreColor", new Color(0.9f, 0.97f, 1f, 0.8f * a));
                if (a <= 0f) drop.gameObject.SetActive(false);
            }
            if (layerRoot.gameObject.activeSelf)
            {
                // Washed out to white with its data rushing, then eaten away cell by cell. The rush runs up to four times
                // the drift as it whitens: the extra distance, integrated here, goes on the texture's offset, with the
                // shader's own scroll (_Scroll × _SeaTime) left alone; scaling that speed would jump the texture by
                // however far the clock has run.
                var white = Mathf.Clamp01(since / 0.25f);
                var rushed = 3f * (since < 0.25f ? since * since / 0.5f : since - 0.125f);
                var cut = Mathf.SmoothStep(0f, 1f, (since - 0.25f) / 0.85f);
                var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
                for (var i = 0; i < layers.Length; i++)
                {
                    var m = layers[i];
                    m.SetColor("_Color", Color.Lerp(colour, Color.white * 0.9f, white).WithAlpha(0.55f - i * 0.2f));
                    m.mainTextureOffset = new Vector2(0f, (i == 0 ? 0.12f : -0.07f) * rushed);
                    m.SetFloat("_Cut", cut);
                    m.SetColor("_EdgeColor", edge * (calm ? 0.55f : 1f));
                }
                if (since >= 1.1f) layerRoot.gameObject.SetActive(false);
            }
            if (!burst && since >= 0.25f)
            {
                burst = true;
                Burst(time);
            }
            if (since >= 2.6f && !drop.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        // The wall's pixels let go and drift up; its light sweeps over the frame round it and a ring runs out from its foot.
        void Burst(float time)
        {
            var across = alongX ? Vector3.right : Vector3.forward;
            if (wake != null)
                for (var i = 0; i < 90; i++)
                {
                    var at = Position + across * Random.Range(-Dungeons.Cell / 2f, Dungeons.Cell / 2f) + Vector3.up * Random.Range(0.2f, Height);
                    var c = Color.Lerp(Color.white, edge.WithAlpha(1f), Random.value);
                    wake.Bit(at, Vector3.up * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.3f, Random.Range(0.12f, 0.22f), Random.Range(0.9f, 1.4f), c.WithAlpha(0.9f));
                }
            glims?.Light(Position + Vector3.up * 2f, light, 7f, 1.2f, time);
            glims?.Pulse(Position, edge * 0.7f, time);
        }
    }
}
