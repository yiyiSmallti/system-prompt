using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The puzzles of a latent-space area (design/systems.md 潜空间探索·机关), built from the map:
    //   cache blocks (K) pushed a cell at a time into the sockets (O); once every socket holds one, the gate
    //   (Y) opens for good, and the console (R) puts the blocks back where they started until then; each push
    //   can be taken back, and a block pushed into a corner of walls (where it can never come out) turns red;
    //   the lighthouse's light coming down at Q and running north, turned by light plates (M) a quarter of a
    //   right angle at a time either way, passing over low walls and stopped by racks, until it reaches the goal.
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
        // The light runs out along its way at LightSpeed (Animate) rather than being there at once: held at a plate
        // until it has turned, cut back to it when its way changes beyond. Arrived: it has run all the way to the goal.
        public bool LightArrived { get; private set; }
        public float LightReach => reach;
        public float LightLength => beamLength;

        readonly DungeonDef def;
        readonly DungeonMap map;
        readonly Transform root;
        readonly SeaGlims glims;
        static readonly Color Amber = new Color(1f, 0.66f, 0.2f), Full = new Color(0.35f, 1f, 0.6f);

        readonly Dictionary<Vector2Int, SocketLook> sockets = new Dictionary<Vector2Int, SocketLook>();
        readonly Stack<(CacheBlock Block, Vector2Int From)> pushes = new Stack<(CacheBlock, Vector2Int)>(); // since the last reset
        readonly Dictionary<Vector2Int, Material> cables = new Dictionary<Vector2Int, Material>(); // by socket
        readonly List<Transform> segments = new List<Transform>();
        readonly Vector2Int? source;
        Firewall gate;
        Material beam, endLight, ripple;
        Mesh ribbon;
        Transform end, prism, rippleRing;
        Vector2Int goal;
        readonly List<Vector3> beamPoints = new List<Vector3>();
        readonly List<float> beamAlong = new List<float>(); // metres along the way to each point
        float beamLength, reach, holdAt, holdUntil = -1f, lastTime;
        bool reachesGoal;
        const float LightSpeed = 40f;

        // A socket's mark on the seabed and the ghost of a block over it, showing where one goes; when a block goes in
        // (Full, from Changed) the ghost folds down as it slides over, and as it seats the band flashes white, a ring
        // runs out over the seabed and a spark runs along the cable to the gate.
        sealed class SocketLook
        {
            public Material Mark, Lines, Scan, Band;
            public Transform Ghost, ScanPlane, Spark;
            public Vector3 At, GateAt;
            public bool Full, Rang = true;
            public float Changed = -100f;
        }

        // A block's slide (CacheBlock.Slide) and its seating after; the cable's spark in metres a second.
        const float Seat = 0.3f, SparkSpeed = 12f;

        public SeaPuzzles(DungeonDef def, DungeonMap map, Transform parent, SeaGlims glims)
        {
            this.def = def;
            this.map = map;
            this.glims = glims;
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
            if (!source.HasValue) return;
            reach = beamLength; // there from the start
            ShowLight();
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
            if (Solved) return false;
            if (map.Solid(to) || taken(to) || map[to] == 'X' || map[to] == 'Y')
            {
                block.Bump(direction, time);
                return false;
            }
            pushes.Push((block, block.Cell));
            Move(block, to, time);
            return true;
        }

        public bool CanUndo => !Solved && pushes.Count > 0;

        // The last push taken back: the block slides back where it was, unless something stands there now
        // (taken: props, noise bodies, the player). True when it moved.
        public bool Undo(Func<Vector2Int, bool> taken, float time)
        {
            if (!CanUndo) return false;
            var (block, from) = pushes.Peek();
            if (map.Solid(from) || taken(from)) return false;
            pushes.Pop();
            Move(block, from, time);
            return true;
        }

        void Move(CacheBlock block, Vector2Int to, float time)
        {
            map.Blocks.Remove(block.Cell);
            map.Blocks.Add(to);
            block.Slide(to, time);
            Refresh(time);
            // Open at once; the gate itself goes when the player is shown it (OpenGate, DungeonScreen.GateOpened).
            if (Sockets.Count > 0 && Sockets.TrueForAll(map.Blocks.Contains))
            {
                Solved = true;
                map.GateOpen = true;
            }
        }

        public Vector3? GatePosition => gate?.Position;

        public void OpenGate(float time, SeaGlims glims, SeaWake wake) => gate?.Open(time, glims, wake);

        // In a corner of walls and not in a socket: no push can ever bring it out again.
        public bool Stuck(CacheBlock b)
        {
            if (Solved || InSocket(b)) return false;
            bool Wall(Vector2Int c) => map.Solid(c) && !map.Blocks.Contains(c);
            return (Wall(b.Cell + Vector2Int.up) || Wall(b.Cell + Vector2Int.down)) && (Wall(b.Cell + Vector2Int.left) || Wall(b.Cell + Vector2Int.right));
        }

        public bool AnyStuck => Blocks.Exists(Stuck);

        // The console: every block back to its start (not once the gate is open).
        public void ResetBlocks(float time)
        {
            if (Solved) return;
            map.Blocks.Clear();
            map.Blocks.UnionWith(map.BlockStarts);
            foreach (var b in Blocks) b.Slide(b.Start, time);
            pushes.Clear();
            Refresh(time);
        }

        public bool InSocket(CacheBlock b) => Sockets.Contains(b.Cell);

        // A beacon save read back (DungeonScreen.Resume): each block on its cell and each plate turned to its facing, at
        // once (a time long past: no slide, no turn, nothing for Undo to take back), the gate open if every socket is
        // full and the light found if it reaches the goal. Lists from another layout of the area are left alone.
        public void Restore(IReadOnlyList<Vector2Int> blocks, IReadOnlyList<int> facings)
        {
            if (blocks.Count == Blocks.Count)
            {
                map.Blocks.Clear();
                for (var i = 0; i < Blocks.Count; i++)
                {
                    map.Blocks.Add(blocks[i]);
                    Blocks[i].Slide(blocks[i], -100f);
                }
                pushes.Clear();
                if (Sockets.Count > 0 && Sockets.TrueForAll(map.Blocks.Contains))
                {
                    Solved = true;
                    map.GateOpen = true;
                }
            }
            if (facings.Count == Plates.Count)
                for (var i = 0; i < Plates.Count; i++)
                {
                    var step = ((facings[i] - Plates[i].Facing) % LightPlate.Shapes.Length + LightPlate.Shapes.Length) % LightPlate.Shapes.Length;
                    if (step != 0) Plates[i].Turn(-100f, step);
                }
            Refresh();
            reach = beamLength; // the light all the way along at once
            LightArrived = Lit && reachesGoal;
            if (source.HasValue) ShowLight();
        }

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
                At = map.World(c),
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

        // A line of light along the seabed from a socket to the gate it powers, and the spark that runs along it
        // when a block seats (one look for all of them).
        void Cable(Vector2Int from, Vector2Int to)
        {
            var a = map.World(from);
            var b = map.World(to);
            var m = SeaArt.Glow(SeaArt.White, new Color(1f, 0.66f, 0.2f, 0.25f));
            var node = Parts.Node("Cable", root, (a + b) / 2f + Vector3.up * 0.04f);
            node.localRotation = Quaternion.LookRotation(b - a);
            Parts.Shape("Line", node, SeaArt.Decal(1f), m).transform.localScale = new Vector3(0.14f, 1f, Vector3.Distance(a, b));
            cables[from] = m;
            sparkLook ??= SeaArt.Glow(SeaArt.Flare, Full);
            var look = sockets[from];
            look.GateAt = b;
            look.Spark = Parts.Node("Spark", root, a);
            Parts.Shape("Flare", look.Spark, SeaArt.Quad(0.6f, 0.6f), sparkLook).gameObject.AddComponent<Billboard>();
            look.Spark.gameObject.SetActive(false);
        }

        Material sparkLook;

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

        // A quarter of a right angle clockwise (step 1) or back (-1). True the first time the light reaches the goal
        // (at once; the light itself gets there a moment later, LightArrived). The light waits at the plate, if it gets
        // that far, until the plate has turned.
        public bool Turn(LightPlate plate, float time, int step = 1)
        {
            // The way up to the plate stays as it was; past it, the light goes back to the plate and waits there.
            var at = Along(plate.Cell);
            if (at >= 0f)
            {
                holdAt = at;
                holdUntil = time + 0.3f;
                reach = Mathf.Min(reach, at);
            }
            plate.Turn(time, step);
            var wasLit = Lit;
            Refresh();
            return Lit && !wasLit;
        }

        // Metres along the light's way to a plate on it, or -1.
        float Along(Vector2Int cell)
        {
            var p = map.World(cell) + Vector3.up * BeamHeight;
            for (var i = 0; i < beamPoints.Count; i++)
                if ((beamPoints[i] - p).sqrMagnitude < 0.01f) return beamAlong[i];
            return -1f;
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
            prism = Parts.Shape("Prism", node, SeaArt.Sphere(0.55f, 0f, 4, 2), SeaArt.Toon(colour, colour * 0.6f, Color.white, colour * 1.8f, 0.02f), new Vector3(0, BeamHeight, 0)).transform;
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

        // Follows the light from Q, and moves the beam's pieces to match; sockets light with their blocks (from `time`,
        // Animate; before it, as they are).
        void Refresh(float time = -100f)
        {
            foreach (var kv in sockets)
            {
                var look = kv.Value;
                var full = map.Blocks.Contains(kv.Key);
                if (full == look.Full) continue;
                look.Full = full;
                look.Changed = time;
                look.Rang = !full || time < 0f;
            }
            foreach (var b in Blocks)
            {
                b.SetFilled(InSocket(b), time);
                b.SetStuck(Stuck(b));
            }
            if (!source.HasValue) return;

            beamPoints.Clear();
            BeamCells.Clear();
            reachesGoal = Trace(source.Value, beamPoints);
            Lit |= reachesGoal;
            // Laid along the way; how much of it shows is the light's (ShowLight).
            beamAlong.Clear();
            beamLength = 0f;
            for (var i = 0; i < beamPoints.Count; i++)
            {
                if (i > 0) beamLength += Vector3.Distance(beamPoints[i - 1], beamPoints[i]);
                beamAlong.Add(beamLength);
            }
            for (var i = 0; i < beamPoints.Count - 1; i++)
            {
                if (i >= segments.Count) segments.Add(Segment());
                var t = segments[i];
                t.position = beamPoints[i];
                t.rotation = Quaternion.FromToRotation(Vector3.up, (beamPoints[i + 1] - beamPoints[i]).normalized);
            }
            for (var i = Mathf.Max(0, beamPoints.Count - 1); i < segments.Count; i++) segments[i].gameObject.SetActive(false);
            reach = Mathf.Min(reach, beamLength);
            ShowLight();
        }

        // The beam up to where the light has got, its flare there, and the plates it has got to lit.
        void ShowLight()
        {
            for (var i = 0; i < beamPoints.Count - 1; i++)
            {
                var length = Mathf.Clamp(reach - beamAlong[i], 0f, beamAlong[i + 1] - beamAlong[i]);
                var t = segments[i];
                t.gameObject.SetActive(length > 0.01f);
                t.localScale = new Vector3(1f, Mathf.Max(length, 0.01f), 1f);
            }
            end.position = At(reach);
            foreach (var p in Plates)
            {
                var at = Along(p.Cell);
                p.SetStruck(at >= 0f && BeamCells.Contains(p.Cell) && reach >= at - 0.01f);
            }
        }

        Vector3 At(float metres)
        {
            for (var i = 0; i < beamPoints.Count - 1; i++)
                if (metres <= beamAlong[i + 1])
                    return Vector3.Lerp(beamPoints[i], beamPoints[i + 1], Mathf.InverseLerp(beamAlong[i], beamAlong[i + 1], metres));
            return beamPoints[beamPoints.Count - 1];
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
            var dt = Mathf.Max(0f, time - lastTime);
            lastTime = time;
            if (source.HasValue)
            {
                var target = time < holdUntil ? Mathf.Min(beamLength, holdAt) : beamLength;
                if (!Mathf.Approximately(reach, target))
                {
                    reach = reach > target ? target : Mathf.MoveTowards(reach, target, LightSpeed * dt);
                    ShowLight();
                }
                if (Lit && reachesGoal && reach >= beamLength - 0.01f) LightArrived = true;
            }
            foreach (var b in Blocks) b.Animate(time);
            foreach (var p in Plates) p.Animate(time);
            gate?.Animate(time);
            foreach (var kv in sockets)
            {
                var look = kv.Value;
                ShowSocket(look, time);
                if (cables.TryGetValue(kv.Key, out var cable))
                    cable.SetColor("_Color", look.Full && time - look.Changed >= Seat ? Full.WithAlpha(0.6f) : Amber.WithAlpha(0.25f));
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
        // A socket after a block went in or came out (look.Changed): the ghost folding down over the block as it slides
        // in (unfolding as it is taken back), and once it seats the mark and band going green, the band through white,
        // with a ring out over the seabed and a spark along the cable to the gate.
        void ShowSocket(SocketLook look, float time)
        {
            var since = time - look.Changed;
            var folded = look.Full ? Ease.InCubic(Mathf.Clamp01((since - 0.05f) / 0.2f)) : 1f - Ease.OutCubic(Mathf.Clamp01(since / 0.2f));
            look.Ghost.gameObject.SetActive(folded < 1f);
            look.Ghost.localScale = new Vector3(1f, Mathf.Max(1f - folded, 0.01f), 1f);
            var seated = look.Full && since >= Seat;
            look.Mark.SetColor("_Color", seated ? Full.WithAlpha(0.95f) : Amber.WithAlpha(0.9f));
            look.Band.SetColor("_Color", seated ? Color.Lerp(Color.white, Full.WithAlpha(0.9f), (since - Seat) / 0.35f) : Amber.WithAlpha(0.8f));
            if (seated && !look.Rang)
            {
                look.Rang = true;
                glims.Pulse(look.At, Full * 0.6f, time);
            }
            if (look.Spark == null) return;
            var run = Vector3.Distance(look.At, look.GateAt);
            var along = seated ? (since - Seat) * SparkSpeed / Mathf.Max(run, 0.01f) : 1f;
            look.Spark.gameObject.SetActive(along < 1f);
            if (along < 1f) look.Spark.position = Vector3.Lerp(look.At, look.GateAt, along) + Vector3.up * 0.3f;
        }
    }

    // A cube of cache the size of a person, sliding a cell when pushed: a dark case in a pale frame with lit
    // corners, a panel of memory cells on every side under the word 缓存, hovering a hand's breadth over its
    // own light on the seabed. It rocks back as it is pushed off and settles; green once in a socket.
    public sealed class CacheBlock
    {
        public const float Size = 2.5f, Hover = 0.14f;
        const float Bar = 0.2f; // the frame's edges
        static readonly Color Cyan = new Color(0.45f, 0.85f, 1f), Full = new Color(0.35f, 1f, 0.6f), Jammed = new Color(1f, 0.36f, 0.3f);

        public readonly Vector2Int Start;
        public Vector2Int Cell { get; private set; }

        readonly DungeonMap map;
        readonly Transform root, body;
        readonly Material corners, panels, under;
        Vector3 from, heading, bumpTo;
        float movedAt = -1f, seatedAt = -100f, bumpedAt = -1f;
        bool filled, stuck;
        // Sat down into a socket: this far up, settling in after its slide.
        const float Seated = 0.04f, SlideSeconds = 0.28f;
        static readonly Color Knock = new Color(1f, 0.55f, 0.45f);

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
            corners = SeaArt.Toon(Cyan, Cyan, Color.white, Cyan * 1.6f, 0.01f);
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

        // In a socket from `time` (it seats as its slide ends); before any (time < 0), seated already.
        public void SetFilled(bool on, float time)
        {
            if (on && !filled) seatedAt = time < 0f ? -100f : time + SlideSeconds;
            filled = on;
        }

        // Pushed against something it can't go into: it knocks against it and its lights flash red, then back to what
        // they were (still red if it is stuck).
        public void Bump(Vector2Int direction, float time)
        {
            bumpedAt = time;
            bumpTo = DungeonMap.Direction(direction);
        }

        public void SetStuck(bool on) => stuck = on;

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
            var hover = Hover + 0.05f * Mathf.Sin(time * 1.6f + Start.x);
            var seat = time - seatedAt;
            if (filled && seat >= 0f)
                hover = seat < 0.12f ? Mathf.Lerp(hover, Seated, Ease.InCubic(seat / 0.12f))
                    : Seated + 0.05f * Mathf.Exp(-12f * (seat - 0.12f)) * Mathf.Sin(18f * (seat - 0.12f));
            var shove = Vector3.zero;
            var knock = 0f;
            if (bumpedAt >= 0f)
            {
                var since = time - bumpedAt;
                if (since < 0.16f) shove = bumpTo * (0.16f * Mathf.Sin(Mathf.PI * since / 0.16f));
                knock = 1f - Mathf.Clamp01(since / 0.25f);
                if (since >= 0.25f) bumpedAt = -1f;
            }
            body.localPosition = new Vector3(0, hover, 0) + shove;
            var pulse = 0.75f + 0.25f * Mathf.Sin(time * 2.2f + Start.x);
            var colour = Color.Lerp(filled ? Full : stuck ? Jammed : Cyan, Knock, knock);
            panels.SetColor("_Color", colour.WithAlpha(filled ? 0.95f : 0.8f * pulse));
            // The corners glow past the lens's threshold; 减弱闪光 holds their halo steady instead of pulsing it.
            var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
            corners.SetColor("_Emission", colour * (filled ? 2f : calm ? 1.65f : 1.2f + 0.6f * pulse));
            corners.SetColor("_Color", colour);
            under.SetColor("_Color", colour.WithAlpha(filled ? 0.7f : 0.5f));
        }
    }

    // A mirror plate on a pedestal that the light bounces off: | and - stand edge on to some light and stop
    // it, / and \ turn it a right angle. Each turn is a quarter of a right angle clockwise seen from above,
    // the way the dial round the pedestal points. The mirror hangs in a fork, dark glass in a pale frame with
    // a sheen running across it; struck by the light it brightens and flares where the light meets it. It turns
    // back the other way too.
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
        float fromYaw, turnBy;
        bool struck;
        float lit, lastTime; // how far its flare is up (0 to 1, in 0.15 s)

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

        public void Turn(float time, int step = 1)
        {
            fromYaw = Yaw(Facing);
            turnBy = 45f * step;
            Facing = ((Facing + step) % Shapes.Length + Shapes.Length) % Shapes.Length;
            turnedAt = time;
        }

        // Whether the light has got to this plate: its flare comes up (and goes) over 0.15 s.
        public void SetStruck(bool on)
        {
            struck = on;
            if (on) flare.gameObject.SetActive(true);
        }

        public void Animate(float time)
        {
            if (turnedAt >= 0f)
            {
                var t = Mathf.Clamp01((time - turnedAt) / 0.3f);
                pivot.localRotation = Quaternion.Euler(0, Mathf.Lerp(fromYaw, fromYaw + turnBy, 1f - (1f - t) * (1f - t)), 0);
                if (t >= 1f) turnedAt = -1f;
            }
            var dt = Mathf.Max(0f, time - lastTime);
            lastTime = time;
            lit = Mathf.MoveTowards(lit, struck ? 1f : 0f, dt / 0.15f);
            var shimmer = Mathf.Sin(time * 1.7f + Index);
            sheen.SetColor("_Color", Color.Lerp(new Color(0.75f, 0.9f, 1f, 0.4f + 0.1f * shimmer), new Color(0.85f, 0.95f, 1f, 0.75f + 0.1f * shimmer), lit));
            band.SetColor("_Color", new Color(0.6f, 0.85f, 1f, Mathf.Lerp(0.5f, 0.9f, lit)));
            if (!flare.gameObject.activeSelf) return;
            flareLight.SetColor("_Color", new Color(0.95f, 0.98f, 1f, 0.9f * lit));
            flare.localScale = Vector3.one * (0.85f + 0.15f * Mathf.Sin(time * 8f + Index));
            if (lit <= 0f && !struck) flare.gameObject.SetActive(false);
        }
    }
}
