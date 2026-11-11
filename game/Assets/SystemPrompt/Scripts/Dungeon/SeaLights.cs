using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The maze's own lights reaching the racks and the figures round them (SeaCommon.cginc SeaPoints, Toon.shader): a
    // beacon's cyan (green once rested at), a pearl's pink, a terminal's green, a firewall's red until it opens, the way
    // out's white once open, the rift's cracks' red. Their pools on the seabed were there already; what they lacked was
    // the upright faces beside them and whoever walks past. The four nearest the player within Range are shown, each
    // fading in over the last 5 m of it, and the fourth fading out as a fifth comes as near (by how much nearer the fourth
    // still is), so none pops in or out. The arrays are always four long: Unity warns when a global array grows.
    sealed class SeaLights
    {
        const int Slots = 4;
        const float Range = 25f, Edge = 5f, Swap = 3f;

        static readonly Color Cyan = Palette.Hex("#4FE3FF"), Rested = Palette.Hex("#5CFF9A"), Pink = Palette.Hex("#FF8AD0"),
            Green = Palette.Hex("#5CFF9A"), Red = Palette.Hex("#FF4A4A"), White = Palette.Hex("#DDEBFF"), Crack = Palette.Hex("#FF5A3C");
        static readonly Vector4[] none = new Vector4[Slots];

        struct Source
        {
            public Func<Vector3> At;
            public Func<Color> Colour; // clear when off
            public float Radius;
        }

        readonly List<Source> sources = new List<Source>();
        readonly Vector4[] positions = new Vector4[Slots], colours = new Vector4[Slots];
        readonly int[] best = new int[Slots + 1];
        readonly float[] bestD = new float[Slots + 1];

        // The lights of a built maze: its props, pearls and firewalls, and three along the rift.
        public static SeaLights Of(DungeonWorld world)
        {
            var l = new SeaLights();
            foreach (var p in world.Props)
            {
                var prop = p;
                var at = prop.Position;
                switch (prop.Kind)
                {
                    case PropKind.Beacon:
                        l.Add(() => at + Vector3.up * SeaProp.LampHeight, () => prop.Used ? Rested : Cyan, 7f);
                        break;
                    case PropKind.Terminal:
                        l.Add(() => at + Vector3.up * 1.2f, () => Green * 0.8f, 4f);
                        break;
                    case PropKind.Goal:
                        l.Add(() => at + Vector3.up * 1.5f, () => prop.Root.gameObject.activeInHierarchy ? White : Color.clear, 8f);
                        break;
                }
            }
            foreach (var p in world.Pearls)
            {
                var pearl = p;
                l.Add(() => pearl.Position + Vector3.up * 0.8f, () => pearl.Taken ? Color.clear : Pink * 0.6f, 3.5f);
            }
            foreach (var f in world.Firewalls)
            {
                var wall = f;
                l.Add(() => wall.Position + Vector3.up * 1.5f, () => wall.Opened ? Color.clear : Red * 0.8f, 6f);
            }
            // The rift's cracks glow up from the floor: three lights evenly along its walkable cells.
            var rift = Array.Find(world.Def.Zones, z => z.Style == ZoneStyle.Rift);
            if (rift != null)
            {
                var cells = new List<Vector2Int>();
                foreach (var c in rift.Cells.allPositionsWithin)
                    if (!world.Map.Solid(c)) cells.Add(c);
                cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
                for (var k = 0; k < 3 && cells.Count > 0; k++)
                {
                    var at = world.Map.World(cells[(cells.Count - 1) * (2 * k + 1) / 6]) + Vector3.up * 0.4f;
                    l.Add(() => at, () => Crack * 0.7f, 6f);
                }
            }
            return l;
        }

        void Add(Func<Vector3> at, Func<Color> colour, float radius) => sources.Add(new Source { At = at, Colour = colour, Radius = radius });

        // None shown (DungeonWorld.ApplySea writes this every frame; the maze then shows its own).
        public static void Clear()
        {
            Shader.SetGlobalVectorArray("_SeaPointPos", none);
            Shader.SetGlobalVectorArray("_SeaPointColor", none);
            Shader.SetGlobalFloat("_SeaPoints", 0f);
        }

        public void Show(Vector3 player)
        {
            if (SeaAtmos.Off) return; // the lab's switch: the sea as it was (ApplySea has cleared them)
            // The nearest Slots + 1 that are on, nearest first.
            var count = 0;
            for (var i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                var d = Vector3.Distance(s.At(), player);
                if (d >= Range || s.Colour().maxColorComponent <= 0f) continue;
                var k = Mathf.Min(count, Slots);
                if (count > Slots && d >= bestD[Slots]) continue;
                while (k > 0 && bestD[k - 1] > d)
                {
                    best[k] = best[k - 1];
                    bestD[k] = bestD[k - 1];
                    k--;
                }
                best[k] = i;
                bestD[k] = d;
                count = Mathf.Min(count + 1, Slots + 1);
            }
            var lit = 0;
            for (var k = 0; k < Slots; k++)
            {
                if (k >= count)
                {
                    positions[k] = colours[k] = Vector4.zero;
                    continue;
                }
                var s = sources[best[k]];
                var weight = Mathf.Clamp01((Range - bestD[k]) / Edge);
                if (count > Slots) weight *= Mathf.Clamp01((bestD[Slots] - bestD[k]) / Swap);
                var at = s.At();
                positions[k] = new Vector4(at.x, at.y, at.z, s.Radius);
                colours[k] = (Vector4)(s.Colour() * weight);
                lit++;
            }
            Shader.SetGlobalVectorArray("_SeaPointPos", positions);
            Shader.SetGlobalVectorArray("_SeaPointColor", colours);
            Shader.SetGlobalFloat("_SeaPoints", lit);
        }
    }
}
