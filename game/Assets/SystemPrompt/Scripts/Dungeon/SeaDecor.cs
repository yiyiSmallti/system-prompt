using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // What makes each zone of an area (DungeonData.cs ZoneStyle) a place of its own rather than more rack
    // corridor: its light on the seabed and the racks, and what stands there instead of racks or between
    // them. Stacked chat bubbles in the reef, emoji coral, the graves of deleted posts, the sunken phone in
    // the main channel, kelp (cables in the canyon), row labels in the server room, the chat log drifting
    // over the long-context corridor, the exit's column of light. DungeonWorld builds the racks with Look,
    // sways what Build put up and shows the exit once it is found.
    public sealed class SeaDecor
    {
        public readonly struct Look
        {
            public readonly Color Floor;      // light on the seabed (alpha: strength)
            public readonly Color Rack;       // LEDs and top edges of the racks (alpha: how much)
            public readonly float Low, High;  // rack heights inside the area; the outer edge stands taller

            public Look(string floor, float floorAlpha, string rack, float rackAlpha, float low, float high)
            {
                Floor = Palette.Hex(floor).WithAlpha(floorAlpha);
                Rack = Palette.Hex(rack).WithAlpha(rackAlpha);
                Low = low;
                High = high;
            }
        }

        public static Look For(ZoneStyle style) => style switch
        {
            ZoneStyle.Reef => new Look("#4CE6B4", 0.6f, "#7BFFD2", 0.6f, 5f, 7f),
            ZoneStyle.Coral => new Look("#FF6F9A", 0.6f, "#FF8AB4", 0.8f, 3.2f, 4.6f),
            ZoneStyle.Graveyard => new Look("#B8404E", 0.55f, "#FF5A64", 0.75f, 4f, 6f),
            ZoneStyle.Ridge => new Look("#9CC2FF", 0.5f, "#BFD8FF", 0.5f, 6f, 9f),
            ZoneStyle.Canyon => new Look("#26C8FF", 0.45f, "#5CE1FF", 0.6f, 11f, 16f),
            ZoneStyle.Plaza => new Look("#9FC4FF", 0.45f, "#E8F0FF", 0.7f, 7f, 10f), // the seabed blue, under the lamp's white
            ZoneStyle.Rift => new Look("#FF5A3C", 0.55f, "#FF6A4A", 0.8f, 9f, 13f),
            ZoneStyle.Servers => new Look("#FFA733", 0.5f, "#FFB347", 0.8f, 4.6f, 5.2f),
            ZoneStyle.RecycleBin => new Look("#8A98B2", 0.5f, "#C2CCE0", 0.6f, 4f, 7f),
            ZoneStyle.Context => new Look("#9A73FF", 0.6f, "#B596FF", 0.8f, 7f, 9f),
            ZoneStyle.WhiteHall => new Look("#EEF4FF", 0.85f, "#FFFFFF", 0.85f, 9f, 12f),
            _ => new Look("#3380FF", 0.45f, "#40F2D9", 0f, 5.6f, 8.2f),
        };

        struct Sway
        {
            public Transform T;
            public Quaternion Rest;
            public float Phase, Degrees;
        }

        const float LowWall = 1.3f;

        static readonly string[] Chat =
        {
            "在吗", "你好呀", "我想问个问题", "嗯", "让我想想", "……", "然后呢？", "谢谢你", "不客气",
            "还在吗", "刚才说到哪了", "再想一会儿", "这个我记得", "晚安",
        };

        readonly DungeonDef def;
        readonly DungeonMap map;
        readonly Transform root;
        readonly List<Sway> sway = new List<Sway>();
        readonly HashSet<Vector2Int> islands = new HashSet<Vector2Int>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        Mesh capsule, sphere, stalk, kelp, cable;
        Material kelpLight, cableLight;

        public Transform Exit { get; private set; }
        public SeaColumn ExitColumn { get; private set; }

        // A span of cables hung across the canyon (Cables), swinging on the line between its ends (Axis, from T), and
        // its packets of data running along its middle strand (Path, Along: metres to each point).
        struct Span
        {
            public Transform T;
            public Vector3 Axis;
            public Vector3[] Path;
            public float[] Along;
            public Transform[] Packets;
            public float Phase;
        }

        const int MaxSpans = 12;
        const float Breath = 4f; // seconds to a breath of the lighthouse's lamp (DungeonWorld.LightBeams)
        static readonly Color RingColor = new Color(0.8f, 0.88f, 1f);
        static readonly Color ScreenColor = new Color(0.6f, 0.8f, 1f, 0.6f);
        static readonly Color WordsColor = new Color(0.9f, 0.95f, 1f);

        readonly List<Span> spans = new List<Span>();
        readonly List<(Transform t, Material look, float size)> rings = new List<(Transform, Material, float)>();
        Transform hologram;
        Vector3 hologramAt;
        Material screenLook;
        Material[] holoLooks; // the message's backing, light, badge and its 1, thinned together (Veil)
        Color[] holoColours;
        TextMesh holoWords;
        float veil, veilShown = -1f;

        // Where the sunken phone's message floats, for DungeonWorld to tell when someone stands in front of it; null
        // without one.
        public Vector3? Message => hologram != null ? hologram.position : null;

        // Someone stands between the message and the camera (veiled): it thins to a quarter over 0.3 s, so they and
        // what shows over them stay in front of it, and comes back as they move off.
        public void Veil(bool veiled, float dt) => veil = Mathf.MoveTowards(veil, veiled ? 1f : 0f, dt / 0.3f);

        public SeaDecor(DungeonDef def, DungeonMap map, Transform parent)
        {
            this.def = def;
            this.map = map;
            root = Parts.Node("Decor", parent);
            FindIslands();
        }

        public ZoneStyle StyleAt(Vector2Int c) => def.ZoneAt(c)?.Style ?? ZoneStyle.Channel;

        // Rack cells and low walls this draws itself; the rest go into DungeonWorld's rack mesh.
        public bool Draws(Vector2Int c)
        {
            var style = StyleAt(c);
            switch (map[c])
            {
                case '#':
                    return style == ZoneStyle.Reef || (islands.Contains(c) && (style == ZoneStyle.Coral || style == ZoneStyle.Graveyard));
                case '=':
                    return style == ZoneStyle.Channel || style == ZoneStyle.Reef || style == ZoneStyle.Graveyard
                        || style == ZoneStyle.RecycleBin || style == ZoneStyle.Coral;
                default:
                    return false;
            }
        }

        public float LowWallHeight => LowWall;

        // How high what stands at c reaches: a rack (as DungeonWorld.BuildWalls and the Blender scenes have it), what
        // stands in the reef, the coral wood or the graveyard instead of one, a low wall, or nothing.
        public float WallTop(Vector2Int c)
        {
            if (!map.Inside(c.x, c.y)) return 0f;
            var style = StyleAt(c);
            switch (map[c])
            {
                case '#' when Draws(c):
                    return style == ZoneStyle.Reef ? 5.4f : style == ZoneStyle.Coral ? 6.4f : 7.3f;
                case '#':
                {
                    var look = For(style);
                    var h = Hash(c.x, c.y);
                    var edge = c.x == 0 || c.y == 0 || c.x == map.Width - 1 || c.y == map.Height - 1;
                    return edge ? look.High + 2f + h * 4f : look.Low + h * (look.High - look.Low);
                }
                case '=':
                    return LowWall;
                default:
                    return 0f;
            }
        }

        // Racks that stand free of the outer wall: coral clumps, monuments, server rows.
        void FindIslands()
        {
            var seen = new HashSet<Vector2Int>();
            var steps = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var start = new Vector2Int(x, y);
                if (map[start] != '#' || seen.Contains(start)) continue;
                var group = new List<Vector2Int>();
                var edge = false;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    group.Add(c);
                    if (c.x == 0 || c.y == 0 || c.x == map.Width - 1 || c.y == map.Height - 1) edge = true;
                    foreach (var s in steps)
                    {
                        var n = c + s;
                        if (!map.Inside(n.x, n.y) || map[n] != '#' || seen.Contains(n)) continue;
                        seen.Add(n);
                        queue.Enqueue(n);
                    }
                }
                if (!edge) islands.UnionWith(group);
            }
        }

        // ------------------------------------------------------------------ building

        // modelled: the area's scenery is a Blender model (SeaScene), which has the bubbles, coral, graves,
        // kelp and the sunken phone already; only the zones' words and light (Dress) are built here then, and what
        // goes over the phone's screen (at screen, SeaScene.Screen).
        public void Build(bool modelled, Pose? screen = null)
        {
            if (modelled)
            {
                foreach (var zone in def.Zones)
                    if (zone.Style != ZoneStyle.Channel) Dress(zone);
                    else if (screen is Pose at) PhoneScreen(at);
                return;
            }

            capsule = SeaArt.Sphere(0.95f, 2.1f, 18, 12);
            sphere = SeaArt.Sphere(1f, 0f, 18, 12);
            stalk = SeaArt.Sphere(0.2f, 1f, 8, 6);
            kelp = SeaArt.Strand(0.42f, 3.2f, 0.9f, 0.1f);
            cable = SeaArt.Strand(0.12f, 8f, 0.9f, 0f);
            kelpLight = SeaArt.Glow(SeaArt.White, new Color(0.35f, 1f, 0.68f, 0.55f));
            cableLight = SeaArt.Glow(SeaArt.White, new Color(0.45f, 0.85f, 1f, 0.6f));
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var c = new Vector2Int(x, y);
                var style = StyleAt(c);
                switch (map[c])
                {
                    case '#' when Draws(c):
                        if (style == ZoneStyle.Reef) Bubbles(c, 3, 0f);
                        else if (style == ZoneStyle.Coral) Coral(c);
                        else Monument(c);
                        break;
                    case '=' when Draws(c):
                        if (style == ZoneStyle.Graveyard) Graves(c);
                        else if (style == ZoneStyle.RecycleBin) Files(c);
                        else if (style != ZoneStyle.Channel) Bubbles(c, 1, -0.35f);
                        break;
                    case '%':
                        Kelp(c, style);
                        break;
                }
            }
            foreach (var zone in def.Zones) Dress(zone);
        }

        public void Animate(float time)
        {
            foreach (var s in sway)
                s.T.localRotation = s.Rest * Quaternion.Euler(Mathf.Sin(time * 0.9f + s.Phase) * s.Degrees, 0f, Mathf.Cos(time * 0.7f + s.Phase * 1.3f) * s.Degrees);
            var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
            // The plaza's rings go out from the lamp at its brightest, a third of a breath apart.
            for (var i = 0; i < rings.Count; i++)
            {
                var (t, look, size) = rings[i];
                var p = calm ? 1f : Mathf.Repeat(time - 1f + i * Breath / rings.Count, Breath) / Breath;
                t.localScale = Vector3.one * size * Mathf.Lerp(0.6f, 1f, p);
                look.SetColor("_Color", RingColor.WithAlpha(calm ? 0.2f : 0.35f * (1f - p)));
            }
            if (hologram != null)
            {
                hologram.localPosition = hologramAt + Vector3.up * (0.15f * Mathf.Sin(time * 0.8f * Mathf.PI));
                // A message coming in: up in 0.3 s, back down over 1.5 s.
                var p = Mathf.Repeat(time, Breath);
                screenLook.SetColor("_Color", ScreenColor.WithAlpha(calm ? 0.7f : p < 0.3f ? Mathf.Lerp(0.6f, 0.9f, p / 0.3f) : Mathf.Lerp(0.9f, 0.6f, (p - 0.3f) / 1.5f)));
                // Only as it changes: the words' colour rebuilds their mesh.
                if (veil != veilShown)
                {
                    veilShown = veil;
                    var keep = 1f - 0.75f * veil;
                    for (var i = 0; i < holoLooks.Length; i++) holoLooks[i].SetColor("_Color", holoColours[i].WithAlpha(holoColours[i].a * keep));
                    holoWords.color = WordsColor.WithAlpha(keep);
                }
            }
            foreach (var s in spans)
            {
                s.T.localRotation = Quaternion.AngleAxis(5f * Mathf.Sin(time * 0.5f + s.Phase) + 1.5f * Mathf.Sin(time * 1.1f + s.Phase * 2f), s.Axis);
                var length = s.Along[s.Along.Length - 1];
                for (var k = 0; k < s.Packets.Length; k++)
                {
                    var d = Mathf.Repeat(time * 1.5f + s.Phase + k * length / s.Packets.Length, length);
                    var j = 0;
                    while (j < s.Along.Length - 2 && s.Along[j + 1] < d) j++;
                    var packet = s.Packets[k];
                    packet.localPosition = Vector3.Lerp(s.Path[j], s.Path[j + 1], (d - s.Along[j]) / (s.Along[j + 1] - s.Along[j]));
                    packet.localScale = Vector3.one * Mathf.Clamp01(Mathf.Min(d, length - d) / 0.6f); // in and out at the plugs
                }
            }
        }

        // The seabed's light per cell, a texel each with a clear border, and where it lies in the world.
        public Texture2D FloorLight(out Vector4 rect)
        {
            var t = new Texture2D(map.Width + 2, map.Height + 2, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Zone Light",
            };
            var px = new Color[t.width * t.height];
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
                px[(map.Height - y) * t.width + x + 1] = def.ZoneAt(new Vector2Int(x, y)) is ZoneDef z ? For(z.Style).Floor : Color.clear;
            t.SetPixels(px);
            t.Apply();
            rect = new Vector4(-Dungeons.Cell, -Dungeons.Cell, t.width * Dungeons.Cell, t.height * Dungeons.Cell);
            return t;
        }

        Material Toon(string lit, string shade, string emission = "#000000", float outline = 0.03f)
        {
            var key = lit + shade + emission;
            if (!materials.TryGetValue(key, out var m))
            {
                materials[key] = m = SeaArt.Toon(Palette.Hex(lit), Palette.Hex(shade), new Color(0.55f, 0.9f, 1f, 1f), Palette.Hex(emission), outline);
                m.SetFloat("_Fade", 1f); // makes way for the lens and the avatar (Toon.shader)
                m.SetFloat("_Caustic", 1f); // and takes the surface's light net
            }
            return m;
        }

        Material Glow(Texture texture, Color color, int atlasCell = -1)
        {
            var m = SeaArt.Glow(texture, color);
            if (atlasCell >= 0)
            {
                m.mainTextureScale = new Vector2(0.25f, 1f);
                m.mainTextureOffset = new Vector2(0.25f * atlasCell, 0f);
            }
            return m;
        }

        static float Hash(int x, int y) => DungeonWorld.Hash(x, y);

        // ------------------------------------------------------------------ cells

        static readonly string[][] BubbleColours =
        {
            new[] { "#EEF3FF", "#97A8C8", "#1A2238" }, // white: someone else
            new[] { "#6EE08F", "#2F8F55", "#0E2A1A" }, // green: you
            new[] { "#4A8BFF", "#2448A8", "#0C1838" }, // blue
        };

        // Chat bubbles lying in a stack: the reef's walls (three high) and low walls (one).
        void Bubbles(Vector2Int c, int layers, float baseY)
        {
            var node = Parts.Node("Bubbles", root, map.World(c));
            for (var i = 0; i < layers; i++)
            {
                var h = Hash(c.x * 3 + i, c.y * 7 - i);
                var colours = BubbleColours[(int)(h * 2.99f)];
                var along = (c.x + c.y + i) % 2 == 0;
                var at = new Vector3((h - 0.5f) * 0.5f, baseY + 0.95f + i * 1.8f, (Hash(c.y + i, c.x) - 0.5f) * 0.5f);
                var bubble = Parts.Shape("Bubble", node, capsule, Toon(colours[0], colours[1], colours[2]), at).transform;
                bubble.localRotation = Quaternion.Euler(0f, along ? 0f : 90f, 90f);
                if (i == layers - 1 && h > 0.4f)
                {
                    // The tail of the speech bubble.
                    var tail = Parts.Shape("Tail", node, SeaArt.Cone(0.32f, 0.7f, 10), Toon(colours[0], colours[1], colours[2]),
                        at + (along ? Vector3.right : Vector3.forward) * (h > 0.7f ? 1.3f : -1.3f) + Vector3.down * 0.6f).transform;
                    tail.localRotation = Quaternion.Euler(180f, 0f, 0f);
                }
            }
        }

        static readonly string[][] CoralColours =
        {
            new[] { "#FFD23F", "#C98A12" }, new[] { "#FF6FA8", "#B3326A" }, new[] { "#FF8A3D", "#B84A12" }, new[] { "#5CE1FF", "#1E7FA8" },
        };

        // A clump of emoji coral: a mound filling the cell, stalks with round faces looking every way.
        void Coral(Vector2Int c)
        {
            var node = Parts.Node("Coral", root, map.World(c));
            var mound = Parts.Shape("Mound", node, sphere, Toon("#5A2A4A", "#2A1028", "#12060E", 0.04f), new Vector3(0, 0.1f, 0)).transform;
            mound.localScale = new Vector3(2.3f, 1.5f, 2.3f);
            var heads = 2 + (int)(Hash(c.x, c.y) * 1.99f);
            for (var i = 0; i < heads; i++)
            {
                var h = Hash(c.x * 5 + i, c.y * 3 - i);
                var colours = CoralColours[(int)(Hash(c.y * 11 + i, c.x) * 3.99f)];
                var foot = new Vector3((h - 0.5f) * 2f, 0f, (Hash(c.y + i * 7, c.x * 2) - 0.5f) * 2f);
                var height = 2.6f + h * 3.4f;
                var stem = Parts.Shape("Stalk", node, stalk, Toon("#7A3A62", "#3A1430"), foot + Vector3.up * height / 2f).transform;
                stem.localScale = new Vector3(1f, height / 1.4f, 1f);
                var r = 0.75f + h * 0.55f;
                var head = Parts.Node("Head", node, foot + Vector3.up * (height + r * 0.5f));
                head.localRotation = Quaternion.Euler(0f, h * 360f + i * 120f, 0f);
                Parts.Shape("Ball", head, sphere, Toon(colours[0], colours[1], colours[0] == "#5CE1FF" ? "#0A3A50" : "#3A1A00", 0.04f)).transform.localScale = Vector3.one * r;
                Parts.Shape("Face", head, SeaArt.Quad(r * 1.5f, r * 1.5f), Glow(SeaArt.Atlas, new Color(1f, 0.95f, 0.8f, 0.7f), SeaArt.AtlasEmoji), new Vector3(0, 0, r * 1.01f))
                    .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                sway.Add(new Sway { T = head, Rest = head.localRotation, Phase = h * 6f, Degrees = 4f });
            }
        }

        // A tall gravestone in the graveyard: 此帖已删除 down both faces.
        void Monument(Vector2Int c)
        {
            var node = Parts.Node("Monument", root, map.World(c));
            Parts.Shape("Slab", node, SeaArt.Box(new Vector3(2.6f, 7.2f, 1.5f)), Toon("#3A4256", "#1A1F2C", "#0A0C12", 0.05f), new Vector3(0, 3.6f, 0));
            Parts.Shape("Cap", node, SeaArt.Box(new Vector3(3f, 0.5f, 1.9f)), Toon("#4A5470", "#22283A", "#0A0C12", 0.05f), new Vector3(0, 7.45f, 0));
            for (var side = 0; side < 2; side++)
            {
                var face = Parts.Node("Face", node, new Vector3(0, 4f, side == 0 ? -0.8f : 0.8f));
                face.localRotation = Quaternion.Euler(0f, side == 0 ? 0f : 180f, 0f);
                SeaArt.Text("Words", face, "此\n帖\n已\n删\n除", 0.9f, new Color(1f, 0.32f, 0.36f, 0.95f));
            }
        }

        // A low wall in the graveyard: two headstones, each 已删除.
        void Graves(Vector2Int c)
        {
            var node = Parts.Node("Graves", root, map.World(c));
            for (var i = 0; i < 2; i++)
            {
                var h = Hash(c.x * 13 + i, c.y * 5);
                var stone = Parts.Node("Stone", node, new Vector3(i == 0 ? -1f : 1f, 0f, (h - 0.5f) * 0.6f));
                stone.localRotation = Quaternion.Euler((h - 0.5f) * 10f, (h - 0.5f) * 20f, (Hash(c.y, c.x + i) - 0.5f) * 8f);
                Parts.Shape("Slab", stone, SeaArt.Box(new Vector3(1.5f, 1.8f, 0.4f)), Toon("#4A5268", "#22283A", "#08090E", 0.04f), new Vector3(0, 0.9f, 0));
                Parts.Shape("Top", stone, SeaArt.Sphere(0.75f, 0f, 12, 6), Toon("#4A5268", "#22283A", "#08090E", 0.04f), new Vector3(0, 1.8f, 0))
                    .transform.localScale = new Vector3(1f, 0.35f, 0.27f);
                for (var side = 0; side < 2; side++)
                {
                    var face = Parts.Node("Face", stone, new Vector3(0, 1.15f, side == 0 ? -0.22f : 0.22f));
                    face.localRotation = Quaternion.Euler(0f, side == 0 ? 0f : 180f, 0f);
                    SeaArt.Text("Words", face, "[已删除]", 0.26f, new Color(1f, 0.35f, 0.4f, 0.9f));
                }
            }
        }

        // A low wall in the recycle bin: a heap of files and folders thrown out.
        void Files(Vector2Int c)
        {
            var node = Parts.Node("Files", root, map.World(c));
            for (var i = 0; i < 7; i++)
            {
                var h = Hash(c.x * 7 + i, c.y * 9 + i);
                var folder = i % 3 == 0;
                var size = folder ? new Vector3(1.6f, 0.25f, 1.2f) : new Vector3(1.1f, 0.06f, 1.5f);
                var at = new Vector3((h - 0.5f) * 2.6f, 0.2f + (i % 4) * 0.32f, (Hash(c.y + i, c.x - i) - 0.5f) * 2.6f);
                var sheet = Parts.Shape(folder ? "Folder" : "Sheet", node, SeaArt.Box(size),
                    folder ? Toon("#F2C14E", "#A87A1A", "#2A1A00") : Toon("#E8EEF8", "#8A98B2", "#10141C"), at).transform;
                sheet.localRotation = Quaternion.Euler((h - 0.5f) * 50f, h * 360f, (Hash(i, c.x) - 0.5f) * 50f);
            }
            Parts.Shape("Bin", node, SeaArt.Box(new Vector3(1.2f, 1.3f, 1.2f)), Toon("#5A6680", "#2A3044", "#0A0C12"), new Vector3(0.9f, 0.65f, 0.9f))
                .transform.localRotation = Quaternion.Euler(0f, 30f, 12f);
        }

        // Kelp: glowing strands swaying over a patch of light. In the canyon and the server room they are
        // cables hanging from above.
        void Kelp(Vector2Int c, ZoneStyle style)
        {
            var node = Parts.Node("Kelp", root, map.World(c));
            var cables = style == ZoneStyle.Canyon || style == ZoneStyle.Servers;
            var material = cables ? cableLight : kelpLight;
            Parts.Shape("Patch", node, SeaArt.Decal(Dungeons.Cell * 1.15f), Glow(SeaArt.Atlas, material.GetColor("_Color") * 0.45f, SeaArt.AtlasDot), new Vector3(0, 0.05f, 0));
            var strand = cables ? cable : kelp;
            for (var i = 0; i < 9; i++)
            {
                var h = Hash(c.x * 17 + i, c.y * 23 - i);
                var at = new Vector3((h - 0.5f) * 3.2f, cables ? 0.5f : 0f, (Hash(c.y * 19 - i, c.x + i) - 0.5f) * 3.2f);
                var t = Parts.Shape("Strand", node, strand, material, at).transform;
                t.localScale = new Vector3(1f, 0.75f + h * 0.5f, 1f);
                t.localRotation = Quaternion.Euler(0f, h * 90f, 0f);
                sway.Add(new Sway { T = t, Rest = t.localRotation, Phase = h * 9f, Degrees = cables ? 2f : 7f });
            }
        }

        // ------------------------------------------------------------------ zones

        void Dress(ZoneDef zone)
        {
            switch (zone.Style)
            {
                case ZoneStyle.Channel: Phone(zone); break;
                case ZoneStyle.Graveyard:
                    Drifting(zone, "[已删除]", new Color(1f, 0.35f, 0.4f, 0.35f), 6);
                    Offerings(zone);
                    Mist(zone);
                    break;
                case ZoneStyle.Canyon: Cables(zone); break;
                case ZoneStyle.RecycleBin: Cards(zone); break;
                case ZoneStyle.Servers: RowLabels(zone); break;
                case ZoneStyle.Context: ChatLog(zone); break;
                case ZoneStyle.WhiteHall: ExitLight(); break;
                case ZoneStyle.Plaza: Plaza(zone); break;
            }
        }

        IEnumerable<Vector2Int> Cells(ZoneDef zone, char mark)
        {
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            for (var x = zone.Cells.xMin; x < zone.Cells.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (map.Inside(x, y) && map[c] == mark && def.ZoneAt(c) == zone) yield return c;
            }
        }

        // The landmark of the main channel: a phone the size of a room sunk into the seabed over the
        // channel's low walls, the message on its screen unanswered.
        void Phone(ZoneDef zone)
        {
            var cells = new List<Vector2Int>(Cells(zone, '='));
            if (cells.Count == 0) return;
            var min = cells[0];
            var max = cells[0];
            foreach (var c in cells)
            {
                min = Vector2Int.Min(min, c);
                max = Vector2Int.Max(max, c);
            }
            var centre = (map.World(min) + map.World(max)) / 2f;
            var w = (max.x - min.x + 1) * Dungeons.Cell - 0.6f;
            var d = (max.y - min.y + 1) * Dungeons.Cell - 0.6f;
            var phone = Parts.Node("Sunken Phone", root, centre + Vector3.up * 0.3f);
            phone.localRotation = Quaternion.Euler(-7f, 4f, 3f);
            Parts.Shape("Body", phone, SeaArt.Box(new Vector3(w, 1f, d)), Toon("#1B2436", "#0B101C", "#050810", 0.06f), new Vector3(0, 0.5f, 0));
            Parts.Shape("Screen", phone, SeaArt.Decal(1f), Glow(SeaArt.White, new Color(0.25f, 0.55f, 0.9f, 0.35f)), new Vector3(0, 1.02f, 0))
                .transform.localScale = new Vector3(w - 1f, 1f, d - 1.4f);
            var bubble = Parts.Shape("Message", phone, SeaArt.Decal(1f), Glow(SeaArt.Atlas, new Color(0.3f, 0.5f, 0.85f, 0.7f), SeaArt.AtlasBubble), new Vector3(-w * 0.12f, 1.05f, d * 0.12f)).transform;
            bubble.localScale = new Vector3(w * 0.55f, 1f, w * 0.55f);
            var words = SeaArt.Text("Words", phone, "在吗", 1.9f, new Color(0.92f, 0.97f, 1f, 1f), new Vector3(-w * 0.14f, 1.08f, d * 0.16f)).transform;
            words.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var status = SeaArt.Text("Status", phone, "未读", 0.55f, new Color(0.6f, 0.8f, 1f, 0.8f), new Vector3(w * 0.3f, 1.08f, -d * 0.3f)).transform;
            status.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // Words drifting above a zone, turned to the camera.
        void Drifting(ZoneDef zone, string words, Color color, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var x = zone.Cells.xMin + Hash(i * 3, zone.Cells.yMin) * zone.Cells.width;
                var y = zone.Cells.yMin + Hash(zone.Cells.xMin, i * 5) * zone.Cells.height;
                var at = map.World(x - 0.5f, y - 0.5f) + Vector3.up * (4.5f + Hash(i, i * 2) * 4f);
                var node = Parts.Node("Drift", root, at);
                node.gameObject.AddComponent<Billboard>();
                SeaArt.Text("Words", node, words, 0.8f + Hash(i * 7, 3) * 0.6f, color);
            }
        }

        // Files thrown away, floating up out of the bin.
        void Cards(ZoneDef zone)
        {
            var card = Glow(SeaArt.Atlas, new Color(0.75f, 0.82f, 1f, 0.45f), SeaArt.AtlasCard);
            for (var i = 0; i < 14; i++)
            {
                var x = zone.Cells.xMin + 1 + Hash(i * 3, 7) * (zone.Cells.width - 2);
                var y = zone.Cells.yMin + 2 + Hash(11, i * 5) * (zone.Cells.height - 4);
                var at = map.World(x - 0.5f, y - 0.5f) + Vector3.up * (3f + Hash(i, 2) * 6f);
                var node = Parts.Node("Card", root, at);
                node.gameObject.AddComponent<Billboard>();
                Parts.Shape("Card", node, SeaArt.Quad(1.6f, 1.6f), card);
            }
            Drifting(zone, "将在 7 天后永久删除", new Color(0.75f, 0.82f, 1f, 0.3f), 3);
        }

        // A label at both ends of every rack row in the server room, like any machine room: amber words on a dark plate
        // in an amber frame, so they read against the racks' cyan strips (added on them they read as noise). One plate
        // and one frame look for the zone; near the lens the frame and words fade out (Glow, Text: SeaNearGlow) and the
        // plate thins out as the scenery does (Toon _Fade).
        void RowLabels(ZoneDef zone)
        {
            var plate = Toon("#1A140A", "#1A140A", "#000000", 0f);
            plate.SetFloat("_Cull", 0f); // seen from either side of the quad
            // Flat and dark: no cyan rim (on a quad seen aslant it lit the whole plate) and no light net over it.
            plate.SetColor("_RimColor", Color.clear);
            plate.SetFloat("_Caustic", 0f);
            var frame = Glow(SeaArt.White, new Color(1f, 0.62f, 0.2f, 0.5f));
            var row = 0;
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            {
                var x = zone.Cells.xMin;
                while (x < zone.Cells.xMax)
                {
                    var c = new Vector2Int(x, y);
                    if (!islands.Contains(c))
                    {
                        x++;
                        continue;
                    }
                    var start = x;
                    while (x < zone.Cells.xMax && islands.Contains(new Vector2Int(x, y))) x++;
                    row++;
                    var label = $"B{(row + 1) / 2}-{(row % 2 == 1 ? "W" : "E")}";
                    foreach (var (cell, dir) in new[] { (new Vector2Int(start, y), Vector3.left), (new Vector2Int(x - 1, y), Vector3.right) })
                    {
                        var node = Parts.Node("Label", root, map.World(cell) + dir * (Dungeons.Cell / 2f + 0.05f) + Vector3.up * 3.6f);
                        node.localRotation = Quaternion.LookRotation(-dir);
                        Parts.Shape("Frame", node, SeaArt.Quad(1.56f, 0.56f), frame, new Vector3(0, 0, 0.03f));
                        Parts.Shape("Plate", node, SeaArt.Quad(1.5f, 0.5f), plate, new Vector3(0, 0, 0.02f));
                        SeaArt.Text("Words", node, label, 0.36f, Palette.Hex("#FFC870"));
                    }
                }
            }
        }

        // Over the long-context corridor, the whole conversation hangs in the water, one message per cell,
        // the two sides taking turns: the newest at the way in from the rift (the south end), older and older further
        // in, each fainter and greyer, as a long context loses its oldest first, down to the first message, already
        // gone: 「…（已截断）」. Each bubble's own fade is a property block on the two shared looks.
        void ChatLog(ZoneDef zone)
        {
            var mine = Glow(SeaArt.Atlas, new Color(0.45f, 0.95f, 0.6f, 0.55f), SeaArt.AtlasBubble);
            var theirs = Glow(SeaArt.Atlas, new Color(0.8f, 0.85f, 1f, 0.55f), SeaArt.AtlasBubble);
            var n = 0;
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            for (var x = zone.Cells.xMin; x < zone.Cells.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (map.Inside(x, y) && !map.Solid(c) && def.ZoneAt(c) == zone) n++;
            }
            var block = new MaterialPropertyBlock();
            var i = 0;
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            for (var x = zone.Cells.xMin; x < zone.Cells.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!map.Inside(x, y) || map.Solid(c) || def.ZoneAt(c) != zone) continue;
                var left = i % 2 == 0;
                var old = n > 1 ? 1f - (float)i / (n - 1) : 0f; // 1 the first message (the north end), 0 the newest
                // The two sides lie across the corridor where it runs: on an east-west stretch (the north end, where the
                // oldest hang) sides along it would put the messages one behind another, their words over each other.
                var eastWest = !map.Solid(new Vector2Int(x - 1, y)) && !map.Solid(new Vector2Int(x + 1, y));
                var side = (left ? -0.8f : 0.8f) * (eastWest ? Vector3.forward : Vector3.right);
                var node = Parts.Node("Message", root, map.World(c) + side + new Vector3(0f, 4.4f + (i % 3) * 0.5f, 0f));
                node.gameObject.AddComponent<Billboard>();
                var look = left ? theirs : mine;
                var bubble = Parts.Shape("Bubble", node, SeaArt.Quad(2.6f, 1.5f), look);
                bubble.transform.localScale = new Vector3(left ? 1f : -1f, 1f, 1f);
                var tone = look.color;
                tone.a *= Mathf.Lerp(1f, 0.3f, old);
                block.SetColor("_Color", tone);
                bubble.SetPropertyBlock(block);
                var ink = left ? new Color(0.85f, 0.9f, 1f, 1f) : new Color(0.6f, 1f, 0.7f, 1f);
                ink = Color.Lerp(ink, Color.white * ink.grayscale, 0.6f * old);
                ink.a = Mathf.Lerp(1f, 0.3f, old);
                SeaArt.Text("Words", node, i == 0 && n > 2 ? "…（已截断）" : Chat[i % Chat.Length], 0.42f, ink, new Vector3(0, 0.12f, -0.02f));
                i++;
            }
        }

        // The way out: a column of light in the lighthouse's colour (只有灯塔的光，找得到它) rising from the goal to the
        // surface, its sign on a dark plate so it reads against the white hall (SeaColumn). It comes up once the light
        // has run to the goal (DungeonWorld.RiseExit), which animates it.
        void ExitLight()
        {
            var goals = map.Find('G');
            if (goals.Count == 0) return;
            ExitColumn = new SeaColumn(root, map.World(goals[0]), def.Lighthouse, 5f, "出口");
            Exit = ExitColumn.Root;
            Exit.name = "Exit";
        }

        // Round the lighthouse, three rings of light going out over the seabed, 10, 16 and 22 m across at the full,
        // each as the lamp breathes (Animate); crystals grown round the foot of its island; and lamps on posts along
        // the low walls that close the plaza, on every other one, two of them either side of the way in: the posts one
        // mesh, the lamps one look, each a bright bulb in a wide soft halo so the ring of them reads from across the sea.
        void Plaza(ZoneDef zone)
        {
            var at = map.World(def.LighthouseCell.x, def.LighthouseCell.y) + Vector3.up * 0.05f;
            var disc = SeaArt.Decal(1f);
            for (var i = 0; i < 3; i++)
            {
                var look = SeaArt.Glow(SeaArt.Ring, RingColor.WithAlpha(0.2f));
                rings.Add((Parts.Shape("Ring", root, disc, look, at).transform, look, (10f + i * 6f) * 2f / 0.88f)); // SeaArt.Ring's line lies at 0.88
            }
            IslandCrystals();
            var feet = new List<Vector3>();
            foreach (var c in Cells(zone, '='))
                if (c.x % 2 == 1) feet.Add(map.World(c) + Vector3.up * LowWall);
            if (feet.Count == 0) return;
            var post = SeaArt.Cylinder(0.07f, 0.05f, 2.8f, 8);
            var collar = SeaArt.Cylinder(0.13f, 0.13f, 0.1f, 8);
            var parts = new List<(Mesh, Matrix4x4)>();
            var lamp = SeaArt.Glow(SeaArt.Disc, Palette.Hex("#E8F0FF"));
            var halo = Glow(SeaArt.Atlas, Palette.Hex("#BFD6FF").WithAlpha(0.35f), SeaArt.AtlasDot);
            var bulb = SeaArt.Quad(0.35f, 0.35f);
            var glow = SeaArt.Quad(2.6f, 2.6f);
            foreach (var foot in feet)
            {
                parts.Add((post, Matrix4x4.Translate(foot)));
                parts.Add((collar, Matrix4x4.Translate(foot + Vector3.up * 2.7f)));
                var light = Parts.Node("Lamp", root, foot + Vector3.up * 2.95f);
                light.gameObject.AddComponent<Billboard>();
                Parts.Shape("Halo", light, glow, halo);
                Parts.Shape("Bulb", light, bulb, lamp, new Vector3(0f, 0f, -0.01f));
            }
            Parts.Shape("Lamp Posts", root, SeaArt.Combine("Lamp Posts", parts), Toon("#3A4C6E", "#1A2540", "#000000", 0.02f));
            Object.Destroy(post);
            Object.Destroy(collar);
        }

        // Clusters of crystals, two and a half times the size of those at the racks' feet, on the seabed just off each
        // open side of the lighthouse's island ('L'), jittered along it, in a cold pale blue: white would light up
        // past the lens's threshold and lose its facets.
        void IslandCrystals()
        {
            var at = new List<Vector3>();
            foreach (var c in map.Find('L'))
                foreach (var d in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
                {
                    var n = c + d;
                    if (!map.Inside(n.x, n.y) || map.Solid(n) || map[n] == 'L') continue;
                    var side = new Vector3(d.y, 0f, d.x); // along the island's edge
                    var step = map.World(n) - map.World(c);
                    at.Add(map.World(c) + step * 0.68f + side * ((Hash(c.x * 7 + d.x, c.y * 5 + d.y) - 0.5f) * 2.4f));
                }
            if (at.Count == 0) return;
            Parts.Shape("Island Crystals", root, SeaArt.CrystalBed(at, new Color(0.55f, 0.7f, 0.9f), 2.5f, 17), SeaArt.Plain("Scatter"));
        }

        // Before every other grave, on the seabed on its south side: a smiley lying face up and three little lights
        // standing round it like flowers.
        void Offerings(ZoneDef zone)
        {
            var graves = new List<Vector2Int>(Cells(zone, '='));
            if (graves.Count == 0) return;
            var face = Glow(SeaArt.Atlas, new Color(1f, 0.85f, 0.45f, 0.8f), SeaArt.AtlasEmoji);
            var flower = Glow(SeaArt.Atlas, new Color(1f, 0.45f, 0.55f, 0.9f), SeaArt.AtlasDot);
            var decal = SeaArt.Decal(0.35f);
            var bud = SeaArt.Quad(0.14f, 0.14f);
            for (var i = 0; i < graves.Count; i += 2)
            {
                var c = graves[i];
                var h = Hash(c.x * 3, c.y * 11);
                var at = map.World(c) + new Vector3(h < 0.5f ? -0.96f : 0.96f, 0.03f, -0.62f);
                Parts.Shape("Offering", root, decal, face, at).transform.localRotation = Quaternion.Euler(0f, (h - 0.5f) * 60f, 0f);
                for (var k = 0; k < 3; k++)
                {
                    var a = (k * 120f + h * 90f) * Mathf.Deg2Rad;
                    Parts.Shape("Flower", root, bud, flower, at + new Vector3(Mathf.Cos(a) * 0.28f, 0.1f + k * 0.05f, Mathf.Sin(a) * 0.2f))
                        .gameObject.AddComponent<Billboard>();
                }
            }
        }

        // A low red haze over the graveyard, at the height of the shins: big soft puffs, each a slow 20 s in and out.
        void Mist(ZoneDef zone)
        {
            var node = Parts.Node("Mist", root, map.World(zone.Cells.center.x - 0.5f, zone.Cells.center.y - 0.5f));
            SeaArt.Particles(node, "Mist", SeaArt.AtlasDot, SeaArt.AtlasDot, 55, new Vector3(zone.Cells.width * Dungeons.Cell, 0.4f, zone.Cells.height * Dungeons.Cell),
                20f, 4f, 6.5f, Palette.Hex("#FF5A64").WithAlpha(0.32f), 0f, 0.45f);
        }

        // Cables hung across the canyon's north-south corridors from rack top to rack top, over about half the open
        // cells with racks east and west of them (not the gate's or a firewall's; up to MaxSpans), so they come
        // irregularly, not as the rungs of a ladder: one to three strands to a span, each span a metre either way
        // along the corridor and sagging to its own depth, its lowest strand 6.35 to 7.8 m up, over the camera however
        // it is tipped (6.2 m at most), so the default view (its top edge rises 0.1 m a metre out from the lens, from
        // 4 m) takes the lowest in from some 25 m down the corridor, and looking up all of them. Plugs lit in the
        // canyon's light where it leaves the racks; two packets of data, a bright bead in a soft glow, running along it
        // at 1.5 m/s (Animate).
        void Cables(ZoneDef zone)
        {
            var ends = new List<(Vector2Int, Vector2Int)>();
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            for (var x = zone.Cells.xMin; x < zone.Cells.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                var mark = map[c];
                if ((mark != '.' && !char.IsLower(mark) && !char.IsDigit(mark)) || def.ZoneAt(c) != zone) continue;
                var (a, b) = (c + Vector2Int.left, c + Vector2Int.right);
                if (map[a] == '#' && map[b] == '#' && !Draws(a) && !Draws(b) && Hash(c.x * 13, c.y * 7) < 0.55f) ends.Add((a, b));
            }
            if (ends.Count == 0 || spans.Count >= MaxSpans) return;
            var dark = Toon("#050A14", "#03060C", "#000000", 0.02f);
            var led = Palette.Hex("#5CE1FF");
            var lit = SeaArt.Toon(led * 0.35f, led * 0.35f, Color.clear, led * 1.9f, 0f);
            lit.SetFloat("_Fade", 1f);
            var bead = SeaArt.Glow(SeaArt.Disc, new Color(0.6f, 0.95f, 1f));
            var glow = Glow(SeaArt.Atlas, led.WithAlpha(0.6f), SeaArt.AtlasDot);
            var plug = SeaArt.Box(new Vector3(0.32f, 0.24f, 0.32f));
            var (core, halo) = (SeaArt.Quad(0.22f, 0.22f), SeaArt.Quad(1.1f, 1.1f));
            foreach (var (a, b) in ends)
                if (spans.Count < MaxSpans) Stretch(a, b, dark, lit, plug, (bead, glow, core, halo));
        }

        void Stretch(Vector2Int a, Vector2Int b, Material dark, Material lit, Mesh plug, (Material bead, Material glow, Mesh core, Mesh halo) packet)
        {
            var i = spans.Count;
            var flat = (map.World(b) - map.World(a)).normalized;
            var across = Vector3.Cross(Vector3.up, flat);
            var along = across * ((Hash(i, 43) - 0.5f) * 2f); // a metre either way along the corridor
            var start = map.World(a) + flat * (Dungeons.Cell / 2f + 0.12f) + Vector3.up * WallTop(a) + along;
            var end = map.World(b) - flat * (Dungeons.Cell / 2f + 0.12f) + Vector3.up * WallTop(b) + along;
            var node = Parts.Node("Cables", root, start);
            var paths = new Vector3[1 + (int)(Hash(47, i) * 2.99f)][];
            var lowest = 7.2f + (Hash(i, 41) - 0.5f) * 1.2f;
            for (var k = 0; k < paths.Length; k++)
            {
                var off = k - (paths.Length - 1) / 2f;
                var shift = across * (off * 0.22f);
                paths[k] = Hang(shift, end - start + shift, lowest + off * 0.25f - start.y);
            }
            Parts.Shape("Strands", node, SeaArt.Strands(paths, 0.04f), dark);
            Parts.Shape("Plug", node, plug, lit);
            Parts.Shape("Plug", node, plug, lit, end - start);
            var packets = new Transform[2];
            for (var k = 0; k < packets.Length; k++)
            {
                packets[k] = Parts.Node("Packet", node);
                packets[k].gameObject.AddComponent<Billboard>();
                Parts.Shape("Glow", packets[k], packet.halo, packet.glow);
                Parts.Shape("Bead", packets[k], packet.core, packet.bead, new Vector3(0f, 0f, -0.01f));
            }
            var path = paths[paths.Length / 2];
            var metres = new float[path.Length];
            for (var j = 1; j < path.Length; j++) metres[j] = metres[j - 1] + Vector3.Distance(path[j - 1], path[j]);
            spans.Add(new Span { T = node, Axis = (end - start).normalized, Path = path, Along = metres, Packets = packets, Phase = Hash(41, i) * 6.283f });
        }

        // A cable hanging from `from` to `to` (heights may differ) whose lowest point is at `lowest`, in 12 pieces: a
        // parabola, near enough a catenary for a cable this slack, its sag found from where it bottoms out.
        static Vector3[] Hang(Vector3 from, Vector3 to, float lowest)
        {
            var rise = to.y - from.y;
            var below = (from.y + to.y) / 2f - lowest;
            var sag = (below + Mathf.Sqrt(Mathf.Max(below * below - rise * rise / 4f, 0f))) / 2f;
            var points = new Vector3[13];
            for (var j = 0; j < points.Length; j++)
            {
                var s = j / 12f;
                points[j] = Vector3.Lerp(from, to, s) - Vector3.up * (4f * sag * s * (1f - s));
            }
            return points;
        }

        // Over the modelled sunken phone (SeaScene.Screen): the chat on its screen, lighting up every breath of the lamp
        // as a message comes in (Animate; steady under 减弱闪光), a pool of its light on the seabed round it, and floating
        // over it, turned to the camera, the message itself, 在吗, one unread. The message hangs over the phone's east
        // side 4.5 m up, off the line from the start up the channel past the firewall to the lighthouse, so from the
        // way in neither the firewall's frame nor the lamp stands behind its words.
        void PhoneScreen(Pose screen)
        {
            screenLook = SeaArt.Glow(SeaArt.PhoneScreen, ScreenColor);
            var overlay = Parts.Shape("Screen", root, SeaArt.Decal(1f), screenLook, screen.position + screen.up * 0.02f).transform;
            overlay.rotation = screen.rotation;
            overlay.localScale = new Vector3(10f, 1f, 9.4f);
            Parts.Shape("Pool", root, SeaArt.Decal(19f), Glow(SeaArt.Atlas, Palette.Hex("#DCEBFF").WithAlpha(0.35f), SeaArt.AtlasDot),
                new Vector3(screen.position.x, 0.04f, screen.position.z));
            hologramAt = screen.position + Vector3.right * 3.5f + Vector3.up * 4.5f;
            hologram = Parts.Node("Message", root, hologramAt);
            hologram.gameObject.AddComponent<Billboard>();
            // In layers drawn in order (their queues): dark glass in the bubble's shape laid over whatever is behind, so
            // the words read against the firewall's red or a rack's lights; the bubble's light and the words added on
            // it; the badge laid on top in red at 0.78, under the lens's threshold so it stays a red spot round its 1,
            // and the 1 dark on it. The atlas bubble's body is 0.81 of the quad wide, its middle 0.06 up and its top
            // corner at (0.41, 0.33): 1.7 m words, 40-odd px from the start, fit inside it.
            var bubble = SeaArt.Quad(5.2f, 5.2f);
            var back = Laid(Glow(SeaArt.Atlas, new Color(0.03f, 0.07f, 0.16f, 0.82f), SeaArt.AtlasBubble), 0);
            var light = Glow(SeaArt.Atlas, new Color(0.35f, 0.6f, 1f, 0.35f), SeaArt.AtlasBubble);
            light.renderQueue = 3001;
            var badge = Laid(SeaArt.Glow(SeaArt.Disc, new Color(0.78f, 0.2f, 0.28f, 1f)), 2);
            Parts.Shape("Backing", hologram, bubble, back, new Vector3(0f, 0f, 0.02f));
            Parts.Shape("Bubble", hologram, bubble, light);
            holoWords = SeaArt.Text("Words", hologram, "在吗", 1.7f, WordsColor, new Vector3(0f, 0.33f, -0.02f));
            var words = holoWords.GetComponent<MeshRenderer>();
            words.sharedMaterial = new Material(words.sharedMaterial) { hideFlags = HideFlags.None, renderQueue = 3001 }; // freed with the maze
            Parts.Shape("Badge", hologram, SeaArt.Quad(1f, 1f), badge, new Vector3(2.05f, 1.65f, -0.03f));
            var one = SeaArt.Text("Unread", hologram, "1", 0.6f, Color.white, new Vector3(2.05f, 1.65f, -0.04f));
            var ink = Laid(SeaArt.Glow(one.font.material.mainTexture, new Color(0.12f, 0f, 0.04f, 1f)), 3);
            one.GetComponent<MeshRenderer>().sharedMaterial = ink;
            holoLooks = new[] { back, light, badge, ink };
            holoColours = new Color[holoLooks.Length];
            for (var i = 0; i < holoLooks.Length; i++) holoColours[i] = holoLooks[i].GetColor("_Color");
        }

        // A Glow laid over what is behind it instead of added (premultiplied), drawn `order` places after the rest.
        static Material Laid(Material glow, int order)
        {
            glow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glow.renderQueue = 3000 + order;
            return glow;
        }
    }
}
