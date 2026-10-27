using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The puzzles of a latent-space area (design/systems.md 潜空间探索·机关), built from the map:
    //   cache blocks (K) pushed a cell at a time into the sockets (O); once every socket holds one, the gate
    //   (Y) opens for good, and the console (R) puts the blocks back where they started until then;
    //   the lighthouse's light coming down at Q and running north, turned by light plates (M) a quarter of a
    //   right angle at a time, passing over low walls and stopped by racks, until it reaches the goal.
    // DungeonWorld asks it what is in reach and passes the player's Enter on.
    public sealed class SeaPuzzles
    {
        const float BeamHeight = 1.7f;

        public readonly List<CacheBlock> Blocks = new List<CacheBlock>();
        public readonly List<LightPlate> Plates = new List<LightPlate>();
        public readonly List<Vector2Int> Sockets = new List<Vector2Int>();
        public readonly List<Vector2Int> BeamCells = new List<Vector2Int>(); // every cell the light crosses
        public bool Solved { get; private set; }  // the gate is open
        public bool Lit { get; private set; }     // the light has reached the goal (it stays found)
        public bool HasLight => source.HasValue;

        readonly DungeonDef def;
        readonly DungeonMap map;
        readonly Transform root;
        static readonly Color Amber = new Color(1f, 0.66f, 0.2f), Full = new Color(0.35f, 1f, 0.6f);

        readonly Dictionary<Vector2Int, SocketLook> sockets = new Dictionary<Vector2Int, SocketLook>();
        readonly Dictionary<Vector2Int, Material> cables = new Dictionary<Vector2Int, Material>(); // by socket
        readonly List<Transform> segments = new List<Transform>();
        readonly Vector2Int? source;
        Firewall gate;
        Material beam, endLight, ripple;
        Mesh ribbon;
        Transform end, prism, rippleRing;
        Vector2Int goal;

        // A socket's mark on the seabed and the ghost of a block over it, showing where one goes.
        sealed class SocketLook
        {
            public Material Mark, Lines, Scan, Band;
            public Transform Ghost, ScanPlane;
        }

        public SeaPuzzles(DungeonDef def, DungeonMap map, Transform parent)
        {
            this.def = def;
            this.map = map;
            root = Parts.Node("Puzzles", parent);
            var q = map.Find('Q');
            if (q.Count > 0) source = q[0];
            var g = map.Find('G');
            goal = g.Count > 0 ? g[0] : new Vector2Int(-1, -1);
        }

        public void Build()
        {
            foreach (var c in map.BlockStarts) Blocks.Add(new CacheBlock(c, map, root));
            foreach (var c in map.Find('O')) Socket(c);
            var gates = map.Find('Y');
            if (gates.Count > 0)
            {
                var y = gates[0];
                var alongX = !(map.Opaque(y.x, y.y - 1) && map.Opaque(y.x, y.y + 1));
                gate = new Firewall(map.World(y), alongX, root, new Color(1f, 0.66f, 0.2f));
                foreach (var s in Sockets) Cable(s, y);
            }

            var plates = map.Find('M');
            for (var i = 0; i < plates.Count; i++)
            {
                var shape = i < def.Plates.Length ? def.Plates[i] : '|';
                Plates.Add(new LightPlate(plates[i], i, Mathf.Max(0, LightPlate.Shapes.IndexOf(shape)), map, root));
            }
            if (source.HasValue) BuildLight(source.Value);
            Refresh();
        }

        // ------------------------------------------------------------------ cache blocks

        // The block the player stands right behind, and which way it would go.
        public CacheBlock BlockInReach(Vector3 player, out Vector2Int direction)
        {
            direction = default;
            var here = map.CellAt(player);
            CacheBlock best = null;
            var bestD = 3.3f;
            foreach (var b in Blocks)
            {
                var d = b.Cell - here;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1) continue;
                var metres = Vector3.Distance(map.World(b.Cell), player);
                if (metres > bestD) continue;
                best = b;
                bestD = metres;
                direction = d;
            }
            return Solved ? null : best;
        }

        // One cell on, unless something is there (taken: props and noise bodies). True when it moved.
        public bool Push(CacheBlock block, Vector2Int direction, Func<Vector2Int, bool> taken, float time)
        {
            var to = block.Cell + direction;
            if (Solved || map.Solid(to) || taken(to) || map[to] == 'X' || map[to] == 'Y') return false;
            map.Blocks.Remove(block.Cell);
            map.Blocks.Add(to);
            block.Slide(to, time);
            Refresh();
            if (Sockets.Count > 0 && Sockets.TrueForAll(map.Blocks.Contains))
            {
                Solved = true;
                map.GateOpen = true;
                gate?.Open(time);
            }
            return true;
        }

        // The console: every block back to its start (not once the gate is open).
        public void ResetBlocks(float time)
        {
            if (Solved) return;
            map.Blocks.Clear();
            map.Blocks.UnionWith(map.BlockStarts);
            foreach (var b in Blocks) b.Slide(b.Start, time);
            Refresh();
        }

        public bool InSocket(CacheBlock b) => Sockets.Contains(b.Cell);

        // A low plate with a mark on it pointing in from every side and a band of light round its edge, and over
        // it the outline of a block, a scan line passing up through it: where a cache block goes. The outline
        // goes once one is in, and the mark and band turn green.
        void Socket(Vector2Int c)
        {
            Sockets.Add(c);
            var node = Parts.Node("Socket", root, map.World(c));
            // No rim: seen along the floor the whole top of the plate is rim, and lit up pale.
            var frame = SeaArt.Toon(Palette.Hex("#1C2A48"), Palette.Hex("#0C1426"), Color.clear, Palette.Hex("#060C18"));
            Parts.Shape("Frame", node, SeaArt.Box(new Vector3(3f, 0.16f, 3f)), frame, new Vector3(0, 0.08f, 0));
            var look = new SocketLook
            {
                Mark = SeaArt.Glow(SeaArt.SocketMark, Amber.WithAlpha(0.9f)),
                Lines = SeaArt.Glow(SeaArt.White, Amber.WithAlpha(0.5f)),
                Scan = SeaArt.Glow(SeaArt.SocketMark, Amber.WithAlpha(0.35f)),
                Band = SeaArt.Glow(SeaArt.White, Amber.WithAlpha(0.8f)),
            };
            // A four-sided tube turned to square round the plate's sides.
            Parts.Shape("Band", node, SeaArt.Tube(2.14f, 2.14f, 0.05f, 4), look.Band, new Vector3(0, 0.09f, 0)).transform.localRotation = Quaternion.Euler(0, 45f, 0);
            Parts.Shape("Mark", node, SeaArt.Decal(2.9f), look.Mark, new Vector3(0, 0.17f, 0));
            look.Ghost = Parts.Node("Ghost", node, new Vector3(0, CacheBlock.Hover, 0));
            CacheBlock.Edges(look.Ghost, 0.045f, look.Lines);
            look.ScanPlane = Parts.Shape("Scan", look.Ghost, SeaArt.Decal(CacheBlock.Size), look.Scan).transform;
            sockets[c] = look;
        }

        // A line of light along the seabed from a socket to the gate it powers.
        void Cable(Vector2Int from, Vector2Int to)
        {
            var a = map.World(from);
            var b = map.World(to);
            var m = SeaArt.Glow(SeaArt.White, new Color(1f, 0.66f, 0.2f, 0.25f));
            var node = Parts.Node("Cable", root, (a + b) / 2f + Vector3.up * 0.04f);
            node.localRotation = Quaternion.LookRotation(b - a);
            Parts.Shape("Line", node, SeaArt.Decal(1f), m).transform.localScale = new Vector3(0.14f, 1f, Vector3.Distance(a, b));
            cables[from] = m;
        }

        // ------------------------------------------------------------------ light

        public LightPlate PlateInReach(Vector3 player)
        {
            LightPlate best = null;
            var bestD = 3.4f;
            foreach (var p in Plates)
            {
                var metres = Vector3.Distance(map.World(p.Cell), player);
                if (metres > bestD) continue;
                best = p;
                bestD = metres;
            }
            return best;
        }

        // A quarter of a right angle clockwise. True the first time the light reaches the goal.
        public bool Turn(LightPlate plate, float time)
        {
            plate.Turn(time);
            var wasLit = Lit;
            Refresh();
            return Lit && !wasLit;
        }

        // The lighthouse's light coming down at Q: a shaft from far overhead, streaks running down it, a pool
        // on the seabed with rings going out over it, and the prism it turns on into the beam.
        void BuildLight(Vector2Int at)
        {
            var colour = def.Lighthouse;
            var node = Parts.Node("Light Fall", root, map.World(at));
            Parts.Shape("Column", node, SeaArt.Ribbon(3.4f, 24), SeaArt.Shaft(colour.WithAlpha(0.55f), new Color(0.9f, 0.97f, 1f, 0.45f), 0.22f, -4f, 0.5f, 1.1f),
                new Vector3(0, 0.05f, 0)).transform.localScale = new Vector3(1f, 70f, 1f);
            Parts.Shape("Halo", node, SeaArt.Ribbon(10f, 24), SeaArt.Shaft(colour.WithAlpha(0.16f), Color.clear, 0.1f, -2f, 0.3f, 1.8f),
                new Vector3(0, 0.05f, 0)).transform.localScale = new Vector3(1f, 45f, 1f);
            Parts.Shape("Pool", node, SeaArt.Decal(7f), Dot(colour * 0.5f), new Vector3(0, 0.06f, 0));
            ripple = SeaArt.Glow(SeaArt.Ring, colour * 0.5f);
            rippleRing = Parts.Shape("Ripple", node, SeaArt.Decal(1f), ripple, new Vector3(0, 0.07f, 0)).transform;
            prism = Parts.Shape("Prism", node, SeaArt.Sphere(0.55f, 0f, 4, 2), SeaArt.Toon(colour, colour * 0.6f, Color.white, colour * 0.9f, 0.02f), new Vector3(0, BeamHeight, 0)).transform;
            prism.localScale = new Vector3(1f, 1.5f, 1f);
            // The beam: a white core in the lighthouse's colour, streaks running the way the light goes.
            ribbon = SeaArt.Ribbon(1.1f, 2);
            beam = SeaArt.Shaft(colour.WithAlpha(0.75f), new Color(0.95f, 0.98f, 1f, 0.95f), 0.12f, 7f, 0.7f);
            endLight = SeaArt.Glow(SeaArt.Flare, new Color(0.9f, 0.96f, 1f, 0.95f));
            end = Parts.Node("Light End", root);
            Parts.Shape("Spark", end, SeaArt.Quad(3.2f, 3.2f), endLight).gameObject.AddComponent<Billboard>();
        }

        static Material Dot(Color c)
        {
            var m = SeaArt.Glow(SeaArt.Atlas, c);
            m.mainTextureScale = new Vector2(0.25f, 1f);
            m.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            return m;
        }

        // Follows the light from Q, and moves the beam's pieces to match; sockets light with their blocks.
        void Refresh()
        {
            foreach (var kv in sockets)
            {
                var full = map.Blocks.Contains(kv.Key);
                kv.Value.Mark.SetColor("_Color", full ? Full.WithAlpha(0.95f) : Amber.WithAlpha(0.9f));
                kv.Value.Band.SetColor("_Color", full ? Full.WithAlpha(0.9f) : Amber.WithAlpha(0.8f));
                kv.Value.Ghost.gameObject.SetActive(!full);
                if (cables.TryGetValue(kv.Key, out var cable))
                    cable.SetColor("_Color", full ? Full.WithAlpha(0.6f) : Amber.WithAlpha(0.25f));
            }
            foreach (var b in Blocks) b.SetFilled(InSocket(b));
            if (!source.HasValue) return;

            var points = new List<Vector3>();
            BeamCells.Clear();
            var reached = Trace(source.Value, points);
            Lit |= reached;
            for (var i = 0; i < points.Count - 1; i++)
            {
                if (i >= segments.Count) segments.Add(Segment());
                var a = points[i];
                var b = points[i + 1];
                var t = segments[i];
                t.gameObject.SetActive(true);
                t.position = a;
                t.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                t.localScale = new Vector3(1f, Vector3.Distance(a, b), 1f);
            }
            for (var i = Mathf.Max(0, points.Count - 1); i < segments.Count; i++) segments[i].gameObject.SetActive(false);
            end.position = points[points.Count - 1];
            foreach (var p in Plates) p.SetStruck(BeamCells.Contains(p.Cell));
        }

        Transform Segment()
        {
            var t = Parts.Node("Beam", root);
            Parts.Shape("Shaft", t, ribbon, beam);
            return t;
        }

        // The light's way in world points at beam height; true when it ends on the goal.
        bool Trace(Vector2Int from, List<Vector3> points)
        {
            Vector3 At(Vector2Int c) => map.World(c) + Vector3.up * BeamHeight;
            var pos = from;
            var dir = new Vector2Int(0, -1); // north
            points.Add(At(pos));
            BeamCells.Add(pos);
            for (var step = 0; step < 400; step++)
            {
                var next = pos + dir;
                var mark = map[next];
                if (!map.Inside(next.x, next.y) || mark == '#' || mark == 'L' || mark == 'D' || (mark == 'X' && !map.FirewallOpen)
                    || (mark == 'Y' && !map.GateOpen) || map.Blocks.Contains(next))
                {
                    points.Add(At(pos) + DungeonMap.Direction(dir) * (Dungeons.Cell / 2f));
                    return false;
                }
                BeamCells.Add(next);
                var plate = Plates.Find(p => p.Cell == next);
                if (plate != null)
                {
                    points.Add(At(next));
                    switch (plate.Shape)
                    {
                        case '|' when dir.x != 0:
                        case '-' when dir.y != 0:
                            return false; // edge on to the light: it stops there
                        case '/': dir = new Vector2Int(-dir.y, -dir.x); break;
                        case '\\': dir = new Vector2Int(dir.y, dir.x); break;
                    }
                }
                else if (next == goal)
                {
                    points.Add(At(next));
                    return true;
                }
                pos = next;
            }
            points.Add(At(pos));
            return false;
        }

        public void Animate(float time)
        {
            foreach (var b in Blocks) b.Animate(time);
            foreach (var p in Plates) p.Animate(time);
            gate?.Animate(time);
            foreach (var look in sockets.Values)
            {
                if (!look.Ghost.gameObject.activeSelf) continue;
                var k = Mathf.Repeat(time * 0.45f, 1f);
                look.ScanPlane.localPosition = new Vector3(0, CacheBlock.Size * k, 0);
                look.Scan.SetColor("_Color", Amber.WithAlpha(0.4f * Mathf.Sin(k * Mathf.PI)));
                look.Lines.SetColor("_Color", Amber.WithAlpha(0.35f + 0.2f * Mathf.Sin(time * 3f)));
                look.Ghost.localPosition = new Vector3(0, CacheBlock.Hover + 0.06f * Mathf.Sin(time * 1.6f), 0);
            }
            if (beam != null)
            {
                var pulse = 0.85f + 0.15f * Mathf.Sin(time * 5f);
                beam.SetColor("_CoreColor", new Color(0.95f, 0.98f, 1f, 0.95f * pulse));
                end.localScale = Vector3.one * (0.85f + 0.2f * Mathf.Sin(time * 7f));
                prism.localRotation = Quaternion.Euler(0, time * 40f, 0);
                var k = Mathf.Repeat(time / 2.2f, 1f);
                rippleRing.localScale = Vector3.one * (2f + 6f * k);
                ripple.SetColor("_Color", def.Lighthouse * 0.6f * (1f - k));
            }
        }
    }

    // A cube of cache the size of a person, sliding a cell when pushed: a dark case in a pale frame with lit
    // corners, a panel of memory cells on every side under the word 缓存, hovering a hand's breadth over its
    // own light on the seabed. It rocks back as it is pushed off and settles; green once in a socket.
    public sealed class CacheBlock
    {
        public const float Size = 2.5f, Hover = 0.14f;
        const float Bar = 0.2f; // the frame's edges
        static readonly Color Cyan = new Color(0.45f, 0.85f, 1f), Full = new Color(0.35f, 1f, 0.6f);

        public readonly Vector2Int Start;
        public Vector2Int Cell { get; private set; }

        readonly DungeonMap map;
        readonly Transform root, body;
        readonly Material corners, panels, under;
        Vector3 from, heading;
        float movedAt = -1f;
        bool filled;

        public CacheBlock(Vector2Int cell, DungeonMap map, Transform parent)
        {
            Start = Cell = cell;
            this.map = map;
            root = Parts.Node("Cache Block", parent, map.World(cell));
            body = Parts.Node("Body", root, new Vector3(0, Hover, 0));
            var shell = SeaArt.Toon(Palette.Hex("#1B2F55"), Palette.Hex("#0B1630"), new Color(0.5f, 0.85f, 1f, 1f), Palette.Hex("#08122A"), 0.03f);
            var steel = SeaArt.Toon(Palette.Hex("#8FA9D6"), Palette.Hex("#3C5282"), new Color(0.7f, 0.9f, 1f, 1f), Palette.Hex("#101A30"), 0.03f);
            Parts.Shape("Case", body, SeaArt.Box(Vector3.one * (Size - 0.08f)), shell, new Vector3(0, Size / 2f, 0));
            Edges(body, Bar, steel);
            corners = SeaArt.Toon(Cyan, Cyan, Color.white, Cyan * 0.8f, 0.01f);
            var knob = SeaArt.Box(Vector3.one * (Bar + 0.06f));
            for (var i = 0; i < 8; i++)
            {
                var at = new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1) * (Size - Bar) / 2f;
                Parts.Shape("Corner", body, knob, corners, at + Vector3.up * Size / 2f);
            }
            panels = SeaArt.Glow(SeaArt.Cells, Cyan.WithAlpha(0.8f));
            var panel = SeaArt.Quad(1.6f, 1.6f);
            for (var side = 0; side < 4; side++)
            {
                var face = Parts.Node("Face", body, Quaternion.Euler(0, side * 90f, 0) * new Vector3(0, 0, -Size / 2f - 0.01f));
                face.localRotation = Quaternion.Euler(0, side * 90f, 0);
                Parts.Shape("Cells", face, panel, panels, new Vector3(0, 1.0f, 0));
                SeaArt.Text("Words", face, "缓存", 0.34f, new Color(0.7f, 0.92f, 1f, 0.95f), new Vector3(0, 2.07f, 0));
            }
            Parts.Shape("Top", body, panel, panels, new Vector3(0, Size + 0.01f, 0)).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            under = SeaArt.Glow(SeaArt.Atlas, Cyan.WithAlpha(0.55f));
            under.mainTextureScale = new Vector2(0.25f, 1f);
            under.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            Parts.Shape("Under", root, SeaArt.Decal(3.8f), under, new Vector3(0, 0.04f, 0));
        }

        // A bar along each of the twelve edges of a block-sized box standing on parent's origin: the frame of
        // a block, or a socket's ghost of one.
        public static void Edges(Transform parent, float bar, Material look)
        {
            var off = (Size - bar) / 2f;
            for (var axis = 0; axis < 3; axis++)
            for (var i = 0; i < 4; i++)
            {
                float a = (i & 1) * 2 - 1, b = (i >> 1) * 2 - 1;
                var size = axis == 0 ? new Vector3(Size, bar, bar) : axis == 1 ? new Vector3(bar, Size, bar) : new Vector3(bar, bar, Size);
                var at = axis == 0 ? new Vector3(0, a, b) : axis == 1 ? new Vector3(a, 0, b) : new Vector3(a, b, 0);
                Parts.Shape("Edge", parent, SeaArt.Box(size), look, at * off + Vector3.up * Size / 2f);
            }
        }

        public void Slide(Vector2Int to, float time)
        {
            from = root.position;
            heading = (map.World(to) - from).normalized;
            Cell = to;
            movedAt = time;
        }

        public void SetFilled(bool on) => filled = on;

        public void Animate(float time)
        {
            var target = map.World(Cell);
            var lean = 0f;
            if (movedAt >= 0f)
            {
                var t = Mathf.Clamp01((time - movedAt) / 0.28f);
                root.position = Vector3.Lerp(from, target, 1f - (1f - t) * (1f - t));
                // Rocked back by the push, then forward past upright as it stops, then settled.
                var since = time - movedAt;
                lean = since < 0.28f ? -5f * Mathf.Sin(t * Mathf.PI) : 3f * Mathf.Exp(-(since - 0.28f) * 9f) * Mathf.Sin((since - 0.28f) * 22f);
                if (since > 0.8f) movedAt = -1f;
            }
            else root.position = target;
            var axis = Vector3.Cross(Vector3.up, heading.sqrMagnitude > 0f ? heading : Vector3.forward);
            body.localRotation = Quaternion.AngleAxis(lean, axis);
            body.localPosition = new Vector3(0, Hover + 0.05f * Mathf.Sin(time * 1.6f + Start.x), 0);
            var pulse = 0.75f + 0.25f * Mathf.Sin(time * 2.2f + Start.x);
            var colour = filled ? Full : Cyan;
            panels.SetColor("_Color", colour.WithAlpha(filled ? 0.95f : 0.8f * pulse));
            corners.SetColor("_Emission", colour * (filled ? 1f : 0.6f + 0.3f * pulse));
            corners.SetColor("_Color", colour);
            under.SetColor("_Color", colour.WithAlpha(filled ? 0.7f : 0.5f));
        }
    }

    // A mirror plate on a pedestal that the light bounces off: | and - stand edge on to some light and stop
    // it, / and \ turn it a right angle. Each turn is a quarter of a right angle clockwise seen from above,
    // the way the dial round the pedestal points. The mirror hangs in a fork, dark glass in a pale frame with
    // a sheen running across it; struck by the light it brightens and flares where the light meets it.
    public sealed class LightPlate
    {
        public const string Shapes = "|/-\\";
        const float Centre = 1.38f; // the mirror's middle over the pivot, at the beam's height

        public readonly Vector2Int Cell;
        public readonly int Index;
        public int Facing { get; private set; } // index into Shapes
        public char Shape => Shapes[Facing];

        readonly Transform pivot, flare;
        readonly Material sheen, band, flareLight;
        float turnedAt = -1f;
        float fromYaw;
        bool struck;

        public LightPlate(Vector2Int cell, int index, int facing, DungeonMap map, Transform parent)
        {
            Cell = cell;
            Index = index;
            Facing = facing;
            var root = Parts.Node("Light Plate " + index, parent, map.World(cell));
            var stone = SeaArt.Toon(Palette.Hex("#22324F"), Palette.Hex("#0F1729"), new Color(0.6f, 0.85f, 1f, 1f), Palette.Hex("#08101C"), 0.04f);
            var steel = SeaArt.Toon(Palette.Hex("#C9D6EE"), Palette.Hex("#6A7A9A"), Color.white, Palette.Hex("#141C2C"), 0.03f);
            Parts.Shape("Pedestal", root, SeaArt.Cylinder(1.05f, 0.9f, 0.32f, 8), stone);
            band = SeaArt.Glow(SeaArt.White, new Color(0.6f, 0.85f, 1f, 0.6f));
            Parts.Shape("Band", root, SeaArt.Tube(0.92f, 0.92f, 0.05f, 8), band, new Vector3(0, 0.29f, 0));
            Parts.Shape("Dial", root, SeaArt.Decal(4.4f), SeaArt.Glow(SeaArt.Dial, new Color(0.6f, 0.85f, 1f, 0.5f)), new Vector3(0, 0.04f, 0));
            pivot = Parts.Node("Pivot", root, new Vector3(0, 0.32f, 0));
            pivot.localRotation = Quaternion.Euler(0, Yaw(facing), 0);
            // The fork: a foot across the pedestal and an arm up either side of the mirror.
            Parts.Shape("Foot", pivot, SeaArt.Box(new Vector3(2.9f, 0.18f, 0.36f)), steel, new Vector3(0, 0.09f, 0));
            for (var k = -1; k <= 1; k += 2)
            {
                Parts.Shape("Arm", pivot, SeaArt.Box(new Vector3(0.16f, 2.5f, 0.3f)), steel, new Vector3(1.37f * k, 1.25f, 0));
                Parts.Shape("Hinge", pivot, SeaArt.Cylinder(0.14f, 0.14f, 0.2f, 8), steel, new Vector3(1.37f * k - 0.1f * k, Centre, 0)).transform.localRotation = Quaternion.Euler(0, 0, 90f * k);
            }
            // Dark glass in a pale frame: a white slab this size filled the camera whenever the avatar stood by one.
            Parts.Shape("Frame", pivot, SeaArt.Box(new Vector3(2.5f, 2.2f, 0.16f)), steel, new Vector3(0, Centre, 0));
            var glass = SeaArt.Toon(Palette.Hex("#3E6AAE"), Palette.Hex("#16284C"), new Color(0.75f, 0.92f, 1f, 1f), Palette.Hex("#0E1A30"), 0f);
            Parts.Shape("Glass", pivot, SeaArt.Box(new Vector3(2.26f, 1.96f, 0.2f)), glass, new Vector3(0, Centre, 0));
            sheen = SeaArt.Glow(SeaArt.Sheen, new Color(0.75f, 0.9f, 1f, 0.45f));
            sheen.SetVector("_Scroll", new Vector4(0.07f, 0f, 0f, 0f));
            var face = SeaArt.Quad(2.26f, 1.96f);
            Parts.Shape("Sheen", pivot, face, sheen, new Vector3(0, Centre, -0.105f));
            Parts.Shape("Sheen Back", pivot, face, sheen, new Vector3(0, Centre, 0.105f)).transform.localRotation = Quaternion.Euler(0, 180f, 0);
            flareLight = SeaArt.Glow(SeaArt.Flare, new Color(0.95f, 0.98f, 1f, 0.9f));
            flare = Parts.Node("Flare", root, new Vector3(0, 0.32f + Centre, 0));
            Parts.Shape("Spark", flare, SeaArt.Quad(2.4f, 2.4f), flareLight).gameObject.AddComponent<Billboard>();
            flare.gameObject.SetActive(false);
        }

        // Shape | runs north-south, - east-west; / from south-west to north-east.
        static float Yaw(int facing) => -90f + 45f * facing;

        public void Turn(float time)
        {
            fromYaw = Yaw(Facing);
            Facing = (Facing + 1) % Shapes.Length;
            turnedAt = time;
        }

        // Whether the light reaches this plate.
        public void SetStruck(bool on)
        {
            struck = on;
            flare.gameObject.SetActive(on);
        }

        public void Animate(float time)
        {
            if (turnedAt >= 0f)
            {
                var t = Mathf.Clamp01((time - turnedAt) / 0.3f);
                pivot.localRotation = Quaternion.Euler(0, Mathf.Lerp(fromYaw, fromYaw + 45f, 1f - (1f - t) * (1f - t)), 0);
                if (t >= 1f) turnedAt = -1f;
            }
            var shimmer = Mathf.Sin(time * 1.7f + Index);
            sheen.SetColor("_Color", struck ? new Color(0.85f, 0.95f, 1f, 0.75f + 0.1f * shimmer) : new Color(0.75f, 0.9f, 1f, 0.4f + 0.1f * shimmer));
            band.SetColor("_Color", new Color(0.6f, 0.85f, 1f, struck ? 0.9f : 0.5f));
            if (struck) flare.localScale = Vector3.one * (0.85f + 0.15f * Mathf.Sin(time * 8f + Index));
        }
    }
}
