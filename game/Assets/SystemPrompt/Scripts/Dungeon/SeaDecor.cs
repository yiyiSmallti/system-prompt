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
            ZoneStyle.Plaza => new Look("#DCE8FF", 0.7f, "#E8F0FF", 0.7f, 7f, 10f),
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

        public void Build()
        {
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
                materials[key] = m = SeaArt.Toon(Palette.Hex(lit), Palette.Hex(shade), new Color(0.55f, 0.9f, 1f, 1f), Palette.Hex(emission), outline);
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
                case ZoneStyle.Graveyard: Drifting(zone, "[已删除]", new Color(1f, 0.35f, 0.4f, 0.35f), 6); break;
                case ZoneStyle.RecycleBin: Cards(zone); break;
                case ZoneStyle.Servers: RowLabels(zone); break;
                case ZoneStyle.Context: ChatLog(zone); break;
                case ZoneStyle.WhiteHall: ExitLight(); break;
                case ZoneStyle.Rift: Cracks(zone); break;
                case ZoneStyle.Plaza: PlazaRing(); break;
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

        // A label at both ends of every rack row in the server room, like any machine room.
        void RowLabels(ZoneDef zone)
        {
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
                        Parts.Shape("Plate", node, SeaArt.Quad(2.2f, 0.9f), Glow(SeaArt.White, new Color(1f, 0.62f, 0.2f, 0.35f)), new Vector3(0, 0, 0.02f));
                        SeaArt.Text("Words", node, label, 0.6f, new Color(1f, 0.85f, 0.6f, 1f));
                    }
                }
            }
        }

        // Over the long-context corridor, the whole conversation hangs in the water, one message per cell,
        // the two sides taking turns.
        void ChatLog(ZoneDef zone)
        {
            var mine = Glow(SeaArt.Atlas, new Color(0.45f, 0.95f, 0.6f, 0.55f), SeaArt.AtlasBubble);
            var theirs = Glow(SeaArt.Atlas, new Color(0.8f, 0.85f, 1f, 0.55f), SeaArt.AtlasBubble);
            var i = 0;
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            for (var x = zone.Cells.xMin; x < zone.Cells.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!map.Inside(x, y) || map.Solid(c) || def.ZoneAt(c) != zone) continue;
                var left = i % 2 == 0;
                var node = Parts.Node("Message", root, map.World(c) + new Vector3(left ? -0.8f : 0.8f, 4.4f + (i % 3) * 0.5f, 0f));
                node.gameObject.AddComponent<Billboard>();
                Parts.Shape("Bubble", node, SeaArt.Quad(2.6f, 1.5f), left ? theirs : mine).transform.localScale = new Vector3(left ? 1f : -1f, 1f, 1f);
                SeaArt.Text("Words", node, Chat[i % Chat.Length], 0.42f, left ? new Color(0.85f, 0.9f, 1f, 1f) : new Color(0.6f, 1f, 0.7f, 1f), new Vector3(0, 0.12f, -0.02f));
                i++;
            }
        }

        // The way out: a column of light rising from the goal to the surface, streaks running up it.
        void ExitLight()
        {
            var goals = map.Find('G');
            if (goals.Count == 0) return;
            var node = Exit = Parts.Node("Exit", root, map.World(goals[0]));
            var column = Parts.Shape("Column", node, SeaArt.Ribbon(5f, 24), SeaArt.Shaft(new Color(0.75f, 0.9f, 1f, 0.55f), new Color(0.95f, 0.98f, 1f, 0.7f), 0.25f, 3f, 0.5f, 1.2f, 0.6f)).transform;
            column.localScale = new Vector3(1f, 70f, 1f);
            var halo = Parts.Shape("Halo", node, SeaArt.Ribbon(13f, 24), SeaArt.Shaft(new Color(0.7f, 0.85f, 1f, 0.16f), Color.clear, 0.2f, 1.5f, 0.3f, 1.8f, 1f)).transform;
            halo.localScale = new Vector3(1f, 45f, 1f);
            Parts.Shape("Pool", node, SeaArt.Decal(9f), Glow(SeaArt.Atlas, new Color(0.7f, 0.85f, 1f, 0.5f), SeaArt.AtlasDot), new Vector3(0, 0.06f, 0));
            var sign = Parts.Node("Sign", node, new Vector3(0, 5.2f, 0));
            sign.gameObject.AddComponent<Billboard>();
            SeaArt.Text("Words", sign, "出口", 1.3f, new Color(0.85f, 0.95f, 1f, 0.9f));
        }

        // Red light seeping up through the floor of the rift.
        void Cracks(ZoneDef zone)
        {
            var glow = Glow(SeaArt.White, new Color(1f, 0.32f, 0.2f, 0.5f));
            var i = 0;
            for (var y = zone.Cells.yMin; y < zone.Cells.yMax; y++)
            for (var x = zone.Cells.xMin; x < zone.Cells.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!map.Inside(x, y) || map.Solid(c)) continue;
                for (var k = 0; k < 2; k++)
                {
                    var h = Hash(x * 5 + k, y * 3 + i);
                    var crack = Parts.Shape("Crack", root, SeaArt.Decal(1f), glow, map.World(c) + new Vector3((h - 0.5f) * 2.4f, 0.05f, (Hash(y, x + k) - 0.5f) * 2.4f)).transform;
                    crack.localScale = new Vector3(0.12f, 1f, 1.6f + h * 2f);
                    crack.localRotation = Quaternion.Euler(0f, h * 180f, 0f);
                }
                i++;
            }
        }

        // A ring of light on the seabed round the lighthouse.
        void PlazaRing()
        {
            var ring = SeaArt.Glow(SeaArt.Ring, new Color(0.8f, 0.88f, 1f, 0.4f));
            Parts.Shape("Ring", root, SeaArt.Decal(26f), ring, map.World(def.LighthouseCell.x, def.LighthouseCell.y) + Vector3.up * 0.05f);
        }
    }
}
