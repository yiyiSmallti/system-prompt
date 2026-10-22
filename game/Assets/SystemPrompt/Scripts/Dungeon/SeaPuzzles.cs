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
        readonly Dictionary<Vector2Int, Material> socketLights = new Dictionary<Vector2Int, Material>();
        readonly Dictionary<Vector2Int, Material> cables = new Dictionary<Vector2Int, Material>(); // by socket
        readonly List<Transform> segments = new List<Transform>();
        readonly Vector2Int? source;
        Firewall gate;
        Material core, glow;
        Transform end;
        Vector2Int goal;

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

        void Socket(Vector2Int c)
        {
            Sockets.Add(c);
            var node = Parts.Node("Socket", root, map.World(c));
            var frame = SeaArt.Toon(Palette.Hex("#1C2A48"), Palette.Hex("#0C1426"), new Color(1f, 0.7f, 0.3f, 1f), Palette.Hex("#060C18"));
            Parts.Shape("Frame", node, SeaArt.Box(new Vector3(3f, 0.16f, 3f)), frame, new Vector3(0, 0.08f, 0));
            var light = SeaArt.Glow(SeaArt.Ring, new Color(1f, 0.66f, 0.2f, 0.7f));
            Parts.Shape("Light", node, SeaArt.Decal(3.4f), light, new Vector3(0, 0.18f, 0));
            socketLights[c] = light;
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

        void BuildLight(Vector2Int at)
        {
            var colour = def.Lighthouse;
            var node = Parts.Node("Light Fall", root, map.World(at));
            Parts.Shape("Column", node, SeaArt.Tube(1.2f, 2.2f, 70f, 20), SeaArt.Glow(SeaArt.Beam, colour * 0.45f), new Vector3(0, 0.1f, 0));
            Parts.Shape("Pool", node, SeaArt.Decal(7f), Dot(colour * 0.5f), new Vector3(0, 0.06f, 0));
            // The prism the light turns on.
            var prism = Parts.Shape("Prism", node, SeaArt.Sphere(0.55f, 0f, 4, 2), SeaArt.Toon(colour, colour * 0.6f, Color.white, colour * 0.9f, 0.02f), new Vector3(0, BeamHeight, 0));
            prism.transform.localScale = new Vector3(1f, 1.5f, 1f);
            core = SeaArt.Glow(SeaArt.White, new Color(0.9f, 0.96f, 1f, 0.85f));
            glow = SeaArt.Glow(SeaArt.Beam, new Color(colour.r, colour.g, colour.b, 0.6f));
            end = Parts.Node("Light End", root);
            Parts.Shape("Spark", end, SeaArt.Quad(2.6f, 2.6f), Dot(new Color(0.9f, 0.96f, 1f, 0.9f))).gameObject.AddComponent<Billboard>();
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
            foreach (var kv in socketLights)
            {
                var full = map.Blocks.Contains(kv.Key);
                kv.Value.SetColor("_Color", full ? new Color(0.35f, 1f, 0.6f, 0.95f) : new Color(1f, 0.66f, 0.2f, 0.7f));
                if (cables.TryGetValue(kv.Key, out var cable))
                    cable.SetColor("_Color", full ? new Color(0.35f, 1f, 0.6f, 0.6f) : new Color(1f, 0.66f, 0.2f, 0.25f));
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
        }

        Transform Segment()
        {
            var t = Parts.Node("Beam", root);
            Parts.Shape("Core", t, SeaArt.Tube(0.09f, 0.09f, 1f, 10), core);
            Parts.Shape("Glow", t, SeaArt.Tube(0.42f, 0.42f, 1f, 14), glow);
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
            if (core != null)
            {
                var pulse = 0.85f + 0.15f * Mathf.Sin(time * 5f);
                core.SetColor("_Color", new Color(0.9f, 0.96f, 1f, 0.85f * pulse));
                end.localScale = Vector3.one * (0.8f + 0.25f * Mathf.Sin(time * 7f));
            }
        }
    }

    // A cube of cache the size of a person, 缓存 on every side, sliding a cell when pushed.
    public sealed class CacheBlock
    {
        public readonly Vector2Int Start;
        public Vector2Int Cell { get; private set; }

        readonly DungeonMap map;
        readonly Transform root;
        readonly Material edge;
        Vector3 from;
        float movedAt = -1f;
        bool filled;

        public CacheBlock(Vector2Int cell, DungeonMap map, Transform parent)
        {
            Start = Cell = cell;
            this.map = map;
            root = Parts.Node("Cache Block", parent, map.World(cell));
            var shell = SeaArt.Toon(Palette.Hex("#2B4A7A"), Palette.Hex("#132544"), new Color(0.5f, 0.85f, 1f, 1f), Palette.Hex("#0A1630"), 0.04f);
            Parts.Shape("Cube", root, SeaArt.Box(new Vector3(2.7f, 2.5f, 2.7f)), shell, new Vector3(0, 1.25f, 0));
            edge = SeaArt.Glow(SeaArt.White, new Color(0.45f, 0.85f, 1f, 0.8f));
            Parts.Shape("Band", root, SeaArt.Box(new Vector3(2.76f, 0.1f, 2.76f)), edge, new Vector3(0, 2.1f, 0));
            Parts.Shape("Foot", root, SeaArt.Box(new Vector3(2.76f, 0.1f, 2.76f)), edge, new Vector3(0, 0.25f, 0));
            for (var side = 0; side < 4; side++)
            {
                var face = Parts.Node("Face", root, Quaternion.Euler(0, side * 90f, 0) * new Vector3(0, 1.25f, -1.37f));
                face.localRotation = Quaternion.Euler(0, side * 90f, 0);
                SeaArt.Text("Words", face, "缓存", 0.7f, new Color(0.7f, 0.92f, 1f, 0.95f));
            }
        }

        public void Slide(Vector2Int to, float time)
        {
            from = root.position;
            Cell = to;
            movedAt = time;
        }

        public void SetFilled(bool on) => filled = on;

        public void Animate(float time)
        {
            var target = map.World(Cell);
            if (movedAt >= 0f)
            {
                var t = Mathf.Clamp01((time - movedAt) / 0.28f);
                root.position = Vector3.Lerp(from, target, 1f - (1f - t) * (1f - t));
                if (t >= 1f) movedAt = -1f;
            }
            else root.position = target;
            edge.SetColor("_Color", filled ? new Color(0.35f, 1f, 0.6f, 0.9f) : new Color(0.45f, 0.85f, 1f, 0.55f + 0.25f * Mathf.Sin(time * 2.2f + Start.x)));
        }
    }

    // A mirror plate on a pedestal that the light bounces off: | and - stand edge on to some light and stop
    // it, / and \ turn it a right angle. Each turn is a quarter of a right angle clockwise seen from above.
    public sealed class LightPlate
    {
        public const string Shapes = "|/-\\";

        public readonly Vector2Int Cell;
        public readonly int Index;
        public int Facing { get; private set; } // index into Shapes
        public char Shape => Shapes[Facing];

        readonly Transform pivot;
        readonly Material face;
        float turnedAt = -1f;
        float fromYaw;

        public LightPlate(Vector2Int cell, int index, int facing, DungeonMap map, Transform parent)
        {
            Cell = cell;
            Index = index;
            Facing = facing;
            var root = Parts.Node("Light Plate " + index, parent, map.World(cell));
            var stone = SeaArt.Toon(Palette.Hex("#22324F"), Palette.Hex("#0F1729"), new Color(0.6f, 0.85f, 1f, 1f), Palette.Hex("#08101C"), 0.04f);
            Parts.Shape("Pedestal", root, SeaArt.Sphere(1f, 0f, 16, 6), stone, new Vector3(0, 0.05f, 0)).transform.localScale = new Vector3(1.3f, 0.35f, 1.3f);
            Parts.Shape("Ring", root, SeaArt.Decal(3.6f), SeaArt.Glow(SeaArt.Ring, new Color(0.6f, 0.85f, 1f, 0.45f)), new Vector3(0, 0.05f, 0));
            pivot = Parts.Node("Pivot", root, new Vector3(0, 0.35f, 0));
            pivot.localRotation = Quaternion.Euler(0, Yaw(facing), 0);
            // Dark glass in a pale frame, centred on the beam's height, with two glints across it: a white
            // slab this size filled the camera whenever the avatar stood by one.
            var frame = SeaArt.Toon(Palette.Hex("#C9D6EE"), Palette.Hex("#6A7A9A"), Color.white, Palette.Hex("#141C2C"), 0.03f);
            Parts.Shape("Frame", pivot, SeaArt.Box(new Vector3(2.5f, 2.2f, 0.18f)), frame, new Vector3(0, 1.35f, 0));
            var glass = SeaArt.Toon(Palette.Hex("#4A74B0"), Palette.Hex("#1C2E52"), new Color(0.75f, 0.92f, 1f, 1f), Palette.Hex("#0E1A30"), 0f);
            Parts.Shape("Glass", pivot, SeaArt.Box(new Vector3(2.2f, 1.9f, 0.24f)), glass, new Vector3(0, 1.35f, 0));
            face = SeaArt.Glow(SeaArt.White, new Color(0.75f, 0.9f, 1f, 0.5f));
            Parts.Shape("Glint", pivot, SeaArt.Box(new Vector3(0.2f, 1.6f, 0.27f)), face, new Vector3(-0.45f, 1.35f, 0)).transform.localRotation = Quaternion.Euler(0, 0, -25f);
            Parts.Shape("Glint Thin", pivot, SeaArt.Box(new Vector3(0.08f, 1.3f, 0.27f)), face, new Vector3(-0.05f, 1.35f, 0)).transform.localRotation = Quaternion.Euler(0, 0, -25f);
        }

        // Shape | runs north-south, - east-west; / from south-west to north-east.
        static float Yaw(int facing) => -90f + 45f * facing;

        public void Turn(float time)
        {
            fromYaw = Yaw(Facing);
            Facing = (Facing + 1) % Shapes.Length;
            turnedAt = time;
        }

        public void Animate(float time)
        {
            if (turnedAt >= 0f)
            {
                var t = Mathf.Clamp01((time - turnedAt) / 0.3f);
                pivot.localRotation = Quaternion.Euler(0, Mathf.Lerp(fromYaw, fromYaw + 45f, 1f - (1f - t) * (1f - t)), 0);
                if (t >= 1f) turnedAt = -1f;
            }
            face.SetColor("_Color", new Color(0.75f, 0.9f, 1f, 0.4f + 0.15f * Mathf.Sin(time * 1.7f + Index)));
        }
    }
}
