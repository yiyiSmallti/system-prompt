using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The maze as a grid: what blocks, where things are, sight lines and shortest paths. Cell (x, y) is
    // column x of map row y; the world has x east and z north, row 0 the northmost, one cell Dungeons.Cell
    // metres wide, its centre at the cell's middle. Cache blocks move, so they are kept apart from the
    // cells (their start cells read as seabed).
    public sealed class DungeonMap
    {
        public readonly int Width, Height;
        readonly char[,] cells;
        public bool FirewallOpen;
        public bool GateOpen;
        public readonly HashSet<Vector2Int> Blocks = new HashSet<Vector2Int>();
        public readonly List<Vector2Int> BlockStarts = new List<Vector2Int>();

        public DungeonMap(string[] rows)
        {
            Height = rows.Length;
            Width = rows[0].Length;
            cells = new char[Width, Height];
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var c = x < rows[y].Length ? rows[y][x] : '#';
                if (c == 'K')
                {
                    BlockStarts.Add(new Vector2Int(x, y));
                    c = '.';
                }
                cells[x, y] = c;
            }
            Blocks.UnionWith(BlockStarts);
        }

        public char this[int x, int y] => Inside(x, y) ? cells[x, y] : '#';
        public char this[Vector2Int c] => this[c.x, c.y];

        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        // Walls, low walls, the lighthouse, the sealed door, light plates and cache blocks are solid; so are
        // the firewalls until the terminal opens them, and the gate until its sockets are full.
        public bool Solid(int x, int y)
        {
            var c = this[x, y];
            return c == '#' || c == '=' || c == 'L' || c == 'D' || c == 'M'
                || (c == 'X' && !FirewallOpen) || (c == 'Y' && !GateOpen) || Blocks.Contains(new Vector2Int(x, y));
        }

        public bool Solid(Vector2Int c) => Solid(c.x, c.y);

        // What sight can't pass: everything solid but what is no taller than a person (low walls, plates,
        // cache blocks), which only stops the feet.
        public bool Opaque(int x, int y)
        {
            var c = this[x, y];
            return Solid(x, y) && c != '=' && c != 'M' && !Blocks.Contains(new Vector2Int(x, y));
        }

        public bool Opaque(Vector2Int c) => Opaque(c.x, c.y);

        public Vector3 World(Vector2Int c) => World(c.x, c.y);

        public Vector3 World(float x, float y) => new Vector3((x + 0.5f) * Dungeons.Cell, 0f, (Height - y - 0.5f) * Dungeons.Cell);

        public Vector2Int CellAt(Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt(world.x / Dungeons.Cell), Height - 1 - Mathf.FloorToInt(world.z / Dungeons.Cell));

        // North in map rows is +z in the world.
        public static Vector3 Direction(Vector2Int d) => new Vector3(d.x, 0f, -d.y);

        public List<Vector2Int> Find(char mark)
        {
            var found = new List<Vector2Int>();
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (cells[x, y] == mark) found.Add(new Vector2Int(x, y));
            return found;
        }

        // A circle of this radius at this point overlaps no solid cell (no tall one, for the camera).
        public bool Free(Vector3 p, float radius, bool tallOnly = false)
        {
            var c = CellAt(p);
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var n = new Vector2Int(c.x + dx, c.y + dy);
                if (tallOnly ? !Opaque(n) : !Solid(n)) continue;
                if (Overlap(p, radius, n, out _)) return false;
            }
            return true;
        }

        // Moves a circle by delta and slides it along the walls it touches.
        public Vector3 Slide(Vector3 from, Vector3 delta, float radius)
        {
            var p = from + delta;
            for (var pass = 0; pass < 3; pass++)
            {
                var c = CellAt(p);
                var moved = false;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var n = new Vector2Int(c.x + dx, c.y + dy);
                    if (!Solid(n) || !Overlap(p, radius, n, out var push)) continue;
                    p += push;
                    moved = true;
                }
                if (!moved) break;
            }
            return p;
        }

        bool Overlap(Vector3 p, float radius, Vector2Int cell, out Vector3 push)
        {
            var centre = World(cell);
            var half = Dungeons.Cell / 2f;
            var nearest = new Vector3(Mathf.Clamp(p.x, centre.x - half, centre.x + half), 0f, Mathf.Clamp(p.z, centre.z - half, centre.z + half));
            var away = new Vector3(p.x - nearest.x, 0f, p.z - nearest.z);
            var d = away.magnitude;
            push = Vector3.zero;
            if (d >= radius) return false;
            if (d < 1e-4f)
            {
                // The centre is inside the cell: out through the nearest side.
                var ox = p.x - centre.x;
                var oz = p.z - centre.z;
                push = Mathf.Abs(ox) > Mathf.Abs(oz)
                    ? new Vector3(Mathf.Sign(ox) * (half - Mathf.Abs(ox) + radius), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(oz) * (half - Mathf.Abs(oz) + radius));
                return true;
            }
            push = away / d * (radius - d);
            return true;
        }

        // Nothing solid between two points (walkable in a straight line), sampled every half metre.
        public bool Clear(Vector3 a, Vector3 b) => Line(a, b, false);

        // Nothing opaque between two points: one can see the other, low walls or not.
        public bool Sight(Vector3 a, Vector3 b) => Line(a, b, true);

        bool Line(Vector3 a, Vector3 b, bool sight)
        {
            a.y = b.y = 0f;
            var d = Vector3.Distance(a, b);
            var steps = Mathf.CeilToInt(d / 0.5f);
            for (var i = 1; i < steps; i++)
            {
                var c = CellAt(Vector3.Lerp(a, b, (float)i / steps));
                if (sight ? Opaque(c) : Solid(c)) return false;
            }
            return true;
        }

        static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        // Shortest path of cells from one cell to another, both ends included; empty when there is none.
        public List<Vector2Int> Path(Vector2Int from, Vector2Int to)
        {
            var path = new List<Vector2Int>();
            if (Solid(to) || !Inside(to.x, to.y)) return path;
            var came = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == to) break;
                foreach (var s in Steps)
                {
                    var n = c + s;
                    if (came.ContainsKey(n) || Solid(n)) continue;
                    came[n] = c;
                    queue.Enqueue(n);
                }
            }
            if (!came.ContainsKey(to)) return path;
            for (var c = to; c != from; c = came[c]) path.Add(c);
            path.Add(from);
            path.Reverse();
            return path;
        }
    }
}
