using SystemPrompt.Dungeon;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // The corner map of the latent-space maze, north up: the cells the player has seen in their zone's
    // colour, low walls marked, what is on them (cache blocks, sockets, the gate, light plates), the
    // lighthouse's light all the way, noise bodies close by as red dots and the player as a green arrow.
    // The goal shows from the start, unless the light has to find it first.
    public sealed class Minimap : VisualElement
    {
        const float EnemyRange = 22f;
        static readonly Color Floor = Palette.Hex("#1F4C7A");
        static readonly Color Edge = Palette.Hex("#4A8BFF");
        static readonly Color LowWall = Palette.Hex("#7FA2CC");
        static readonly Color Amber = Palette.Hex("#FFA833");
        static readonly Color Light = Palette.Hex("#DDEEFF");

        DungeonWorld world;

        public Minimap()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Attach(DungeonWorld w)
        {
            world = w;
            MarkDirtyRepaint();
        }

        public void Refresh() => MarkDirtyRepaint();

        void Draw(MeshGenerationContext mgc)
        {
            if (world == null) return;
            var map = world.Map;
            var r = contentRect;
            var cell = Mathf.Min(r.width / map.Width, r.height / map.Height);
            var origin = new Vector2(r.x + (r.width - cell * map.Width) / 2f, r.y + (r.height - cell * map.Height) / 2f);
            Vector2 At(float x, float y) => origin + new Vector2(x, y) * cell;
            Vector2 OfWorld(Vector3 p) => At(p.x / Dungeons.Cell, map.Height - p.z / Dungeons.Cell);

            bool Wall(int x, int y)
            {
                var c = map[x, y];
                return c == '#' || c == 'L' || c == 'D';
            }

            var p2 = mgc.painter2D;
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (!world.Revealed[x, y] || Wall(x, y)) continue;
                var zone = world.Def.ZoneAt(new Vector2Int(x, y));
                var light = zone != null ? SeaDecor.For(zone.Style).Floor : Color.clear;
                p2.fillColor = Color.Lerp(Floor, light, light.a * 0.45f).WithAlpha(0.85f);
                Rect(p2, At(x, y), cell + 0.5f);
                if (map[x, y] == '=')
                {
                    p2.fillColor = LowWall;
                    Rect(p2, At(x + 0.18f, y + 0.18f), cell * 0.64f);
                }
            }
            // Outline where seen floor meets wall.
            p2.strokeColor = Edge.WithAlpha(0.9f);
            p2.lineWidth = 1.6f;
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (!world.Revealed[x, y] || Wall(x, y)) continue;
                if (Wall(x, y - 1)) Line(p2, At(x, y), At(x + 1, y));
                if (Wall(x, y + 1)) Line(p2, At(x, y + 1), At(x + 1, y + 1));
                if (Wall(x - 1, y)) Line(p2, At(x, y), At(x, y + 1));
                if (Wall(x + 1, y)) Line(p2, At(x + 1, y), At(x + 1, y + 1));
            }

            foreach (var x in map.Find('X'))
            {
                if (!world.Revealed[x.x, x.y] || map.FirewallOpen) continue;
                p2.fillColor = Palette.Red;
                Rect(p2, At(x.x + 0.1f, x.y + 0.38f), cell * 0.8f, cell * 0.24f);
            }

            var puzzles = world.Puzzles;
            foreach (var y in map.Find('Y'))
            {
                if (!world.Revealed[y.x, y.y] || map.GateOpen) continue;
                p2.fillColor = Amber;
                Rect(p2, At(y.x + 0.1f, y.y + 0.38f), cell * 0.8f, cell * 0.24f);
            }
            p2.lineWidth = 1.4f;
            foreach (var o in puzzles.Sockets)
            {
                if (!world.Revealed[o.x, o.y]) continue;
                p2.strokeColor = map.Blocks.Contains(o) ? Palette.Green : Amber;
                Square(p2, At(o.x + 0.12f, o.y + 0.12f), cell * 0.76f);
            }
            foreach (var b in puzzles.Blocks)
            {
                if (!world.Revealed[b.Cell.x, b.Cell.y]) continue;
                p2.fillColor = puzzles.InSocket(b) ? Palette.Green : Palette.Hex("#5AB2FF");
                Rect(p2, At(b.Cell.x + 0.24f, b.Cell.y + 0.24f), cell * 0.52f);
            }
            // The light, seen from anywhere: it is what shows the way.
            if (puzzles.BeamCells.Count > 1)
            {
                p2.strokeColor = Light.WithAlpha(0.9f);
                p2.lineWidth = 2f;
                p2.BeginPath();
                p2.MoveTo(At(puzzles.BeamCells[0].x + 0.5f, puzzles.BeamCells[0].y + 0.5f));
                for (var i = 1; i < puzzles.BeamCells.Count; i++) p2.LineTo(At(puzzles.BeamCells[i].x + 0.5f, puzzles.BeamCells[i].y + 0.5f));
                p2.Stroke();
            }
            p2.strokeColor = Palette.Paper;
            p2.lineWidth = 2.4f;
            foreach (var plate in puzzles.Plates)
            {
                var c = At(plate.Cell.x + 0.5f, plate.Cell.y + 0.5f);
                var h = cell * 0.4f;
                var d = plate.Shape switch
                {
                    '|' => new Vector2(0, h),
                    '/' => new Vector2(h, -h) * 0.75f,
                    '-' => new Vector2(h, 0),
                    _ => new Vector2(h, h) * 0.75f,
                };
                Line(p2, c - d, c + d);
            }

            foreach (var prop in world.Props)
            {
                if (!world.Revealed[prop.Cell.x, prop.Cell.y] && prop.Kind != PropKind.Goal) continue;
                if (prop.Kind == PropKind.Goal && !world.GoalOpen) continue;
                var c = At(prop.Cell.x + 0.5f, prop.Cell.y + 0.5f);
                var s = cell * 0.32f;
                switch (prop.Kind)
                {
                    case PropKind.Fragment:
                        p2.fillColor = prop.Used ? Palette.Gray.WithAlpha(0.6f) : Palette.Hex("#8CC4FF");
                        Poly(p2, c + new Vector2(0, -s), c + new Vector2(s, 0), c + new Vector2(0, s), c + new Vector2(-s, 0));
                        break;
                    case PropKind.Chest:
                        p2.fillColor = prop.Used ? Palette.Gray.WithAlpha(0.6f) : Palette.Hex("#FFC24A");
                        Rect(p2, c - new Vector2(s, s * 0.75f), s * 2f, s * 1.5f);
                        break;
                    case PropKind.Terminal:
                        p2.fillColor = prop.Used ? Palette.Sea : Palette.Green;
                        Rect(p2, c - new Vector2(s * 0.7f, s), s * 1.4f, s * 2f);
                        break;
                    case PropKind.Beacon:
                        p2.fillColor = prop.Used ? Palette.Hex("#73FFD9") : Palette.Hex("#5AB2FF");
                        Circle(p2, c, s);
                        break;
                    case PropKind.Console:
                        p2.fillColor = Amber;
                        Rect(p2, c - new Vector2(s * 0.7f, s * 0.7f), s * 1.4f, s * 1.4f);
                        break;
                    case PropKind.Goal:
                        p2.fillColor = Palette.Paper;
                        Star(p2, c, s * 1.5f);
                        break;
                }
            }

            p2.fillColor = Palette.Red;
            foreach (var e in world.Enemies)
            {
                if (!e.Active || Vector3.Distance(e.Position, world.PlayerPosition) > EnemyRange) continue;
                Circle(p2, OfWorld(e.Position), cell * (e.Alerted ? 0.36f : 0.26f));
            }

            // The player: an arrow pointing where the avatar faces (yaw 0 is north, up on the map).
            var me = OfWorld(world.PlayerPosition);
            var a = world.PlayerYaw * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            var side = new Vector2(-fwd.y, fwd.x);
            var size = cell * 0.55f;
            p2.fillColor = Palette.Green;
            p2.strokeColor = Palette.Ink;
            p2.lineWidth = 2f;
            p2.BeginPath();
            p2.MoveTo(me + fwd * size);
            p2.LineTo(me - fwd * size * 0.7f + side * size * 0.7f);
            p2.LineTo(me - fwd * size * 0.35f);
            p2.LineTo(me - fwd * size * 0.7f - side * size * 0.7f);
            p2.ClosePath();
            p2.Fill();
            p2.Stroke();
        }

        static void Rect(Painter2D p, Vector2 at, float size) => Rect(p, at, size, size);

        static void Rect(Painter2D p, Vector2 at, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(at);
            p.LineTo(at + new Vector2(w, 0));
            p.LineTo(at + new Vector2(w, h));
            p.LineTo(at + new Vector2(0, h));
            p.ClosePath();
            p.Fill();
        }

        static void Square(Painter2D p, Vector2 at, float size)
        {
            p.BeginPath();
            p.MoveTo(at);
            p.LineTo(at + new Vector2(size, 0));
            p.LineTo(at + new Vector2(size, size));
            p.LineTo(at + new Vector2(0, size));
            p.ClosePath();
            p.Stroke();
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        static void Poly(Painter2D p, params Vector2[] pts)
        {
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        static void Circle(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        static void Star(Painter2D p, Vector2 c, float r)
        {
            var pts = new Vector2[10];
            for (var i = 0; i < 10; i++)
            {
                var a = (i * 36f - 90f) * Mathf.Deg2Rad;
                var rr = i % 2 == 0 ? r : r * 0.45f;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            }
            Poly(p, pts);
        }
    }
}
