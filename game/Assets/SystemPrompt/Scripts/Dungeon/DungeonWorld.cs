using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // One latent-space area in 3D (P5-style third person): the rack maze on the seabed and what each zone
    // has instead (SeaDecor.cs; an area modelled in Blender has SeaScene.cs for both), the lighthouse, the
    // props, the wreckage (SeaModels.cs), puzzles (SeaPuzzles.cs) and noise bodies, the
    // player's avatar, DeepSeek beside it and the camera that follows. The UI on top (UI/Dungeon/DungeonScreen.cs) drives it a step
    // at a time and runs the story around it.
    public sealed class DungeonWorld : MonoBehaviour
    {
        const float RunSpeed = 6.2f, SprintSpeed = 9f, Acceleration = 42f;
        const float CameraDistance = 7f, CameraHeight = 1.55f, KeyTurnSpeed = 110f;
        public static readonly Color FogColor = Palette.Hex("#061428");
        static readonly Color SurfaceColor = Palette.Hex("#2F7FB8");

        public DungeonDef Def { get; private set; }
        public DungeonMap Map { get; private set; }
        public Camera Camera { get; private set; }
        public SeaAvatar Avatar { get; private set; }
        public SeaCompanion Companion { get; private set; } // DeepSeek
        public SeaPuzzles Puzzles { get; private set; }
        public readonly List<SeaNoise> Enemies = new List<SeaNoise>();
        public readonly List<SeaProp> Props = new List<SeaProp>();
        public bool[,] Revealed { get; private set; }

        public Vector3 PlayerPosition { get; private set; }
        public float PlayerYaw { get; private set; }
        public float CameraYaw { get; private set; }
        public float Travelled { get; private set; } // metres walked, for the maintenance progress
        public float SafeUntil;                    // noise bodies ignore the player until then

        // Raised during Step.
        public event Action<SeaNoise> Spotted;
        public event Action<SeaNoise> Touched;

        Vector3 velocity;
        float pitch = 20f;
        float cameraDistance = CameraDistance;
        float time;
        SeaDecor decor;
        SeaScene scene; // the area's scenery modelled in Blender, if it has one
        readonly List<Firewall> firewalls = new List<Firewall>();
        Transform sky, motes, beams;
        Material lamp, beamA, beamB;
        Transform lampBeams;
        bool shown = true;
        string gender;
        Transform door, doorSeam, doorMist; // the white door at the deep exit, if the area has one
        SeaMirror mirror;

        public float Time => time;

        // gender: the player's (GameState.playerGender), which picks the avatar's model; deepSeekAwakened
        // picks DeepSeek's (her hoodie dress before the lighthouse, her battle dress after).
        public static DungeonWorld Create(DungeonDef def, string gender, bool deepSeekAwakened)
        {
            var go = new GameObject("Latent Space · " + def.Id);
            var world = go.AddComponent<DungeonWorld>();
            world.Build(def, gender, deepSeekAwakened);
            return world;
        }

        void OnDestroy()
        {
            Avatar?.Dispose();
            Companion?.Dispose();
        }

        void Build(DungeonDef def, string gender, bool deepSeekAwakened)
        {
            Def = def;
            this.gender = gender;
            Map = new DungeonMap(def.Map);
            Revealed = new bool[Map.Width, Map.Height];

            var camGo = new GameObject("Sea Camera");
            camGo.transform.SetParent(transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = FogColor;
            Camera.fieldOfView = 52f;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 600f;
            Camera.depth = 10f;

            decor = new SeaDecor(def, Map, transform);
            scene = SeaScene.Load(def, transform);
            BuildSea();
            if (scene == null) BuildWalls();
            decor.Build(scene != null);
            BuildLighthouse();
            BuildProps();
            BuildWreckage();
            Puzzles = new SeaPuzzles(def, Map, transform);
            Puzzles.Build();
            ShowGoal(GoalOpen);
            BuildAmbience();

            Avatar = new SeaAvatar(transform, gender);
            Companion = new SeaCompanion(transform, deepSeekAwakened);
            var start = Map.Find('S');
            var startCell = start.Count > 0 ? start[0] : new Vector2Int(1, 1);
            // Facing into the maze: north unless the start sits on the north edge.
            Place(startCell, startCell.y > Map.Height / 2 ? 0f : 180f);
            ApplyGlobals();
        }

        // ------------------------------------------------------------------ building

        void BuildSea()
        {
            var size = Mathf.Max(Map.Width, Map.Height) * Dungeons.Cell + 260f;
            var floor = Parts.Shape("Seabed", transform, SeaArt.Plane(size), SeaArt.Plain("Seabed"), Centre);
            floor.sharedMaterial.SetFloat("_Cell", Dungeons.Cell);
            floor.sharedMaterial.SetTexture("_ZoneTex", decor.FloorLight(out var rect));
            floor.sharedMaterial.SetVector("_ZoneRect", rect);
            floor.sharedMaterial.SetTexture("_WallTex", WallMask());
            sky = Parts.Shape("Sky", transform, SeaArt.Sphere(1f, 0f, 32, 20), SeaArt.Plain("Sky")).transform;
            sky.localScale = Vector3.one * 420f;
        }

        // The cells where a wall, low wall or the lighthouse stands, white, in SeaDecor.FloorLight's layout: the
        // seabed darkens at their foot.
        Texture2D WallMask()
        {
            var t = new Texture2D(Map.Width + 2, Map.Height + 2, TextureFormat.R8, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "Walls",
            };
            var px = new Color[t.width * t.height];
            for (var y = 0; y < Map.Height; y++)
            for (var x = 0; x < Map.Width; x++)
                if ("#=L".IndexOf(Map[x, y]) >= 0) px[(Map.Height - y) * t.width + x + 1] = Color.white;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        Vector3 Centre => new Vector3(Map.Width * Dungeons.Cell / 2f, 0f, Map.Height * Dungeons.Cell / 2f);

        // The maze's racks (each zone's own height and light; low walls are knee-high racks where the zone
        // has nothing else for them), and a ring of taller stacks around it that the fog swallows.
        void BuildWalls()
        {
            var blocks = new Dictionary<Vector2Int, float>();
            for (var y = -6; y < Map.Height + 6; y++)
            for (var x = -6; x < Map.Width + 6; x++)
            {
                var c = new Vector2Int(x, y);
                var h = Hash(x, y);
                if (Map.Inside(x, y))
                {
                    var mark = Map[x, y];
                    if ((mark != '#' && mark != '=') || decor.Draws(c)) continue;
                    var look = SeaDecor.For(decor.StyleAt(c));
                    var edge = x == 0 || y == 0 || x == Map.Width - 1 || y == Map.Height - 1;
                    blocks[c] = mark == '=' ? decor.LowWallHeight : edge ? look.High + 2f + h * 4f : look.Low + h * (look.High - look.Low);
                }
                else
                {
                    // Not where the lighthouse stands outside the maze.
                    if (Vector2.Distance(new Vector2(x, y), Def.LighthouseCell) < 3.2f) continue;
                    var ring = Mathf.Max(Mathf.Max(-x, x - Map.Width + 1), Mathf.Max(-y, y - Map.Height + 1));
                    if (h < 0.35f + ring * 0.06f) continue;
                    blocks[c] = 9f + Hash(y, x) * (6f + ring * 2.5f);
                }
            }
            Color Tint(Vector2Int c) => SeaDecor.For(decor.StyleAt(c)).Rack;
            Parts.Shape("Racks", transform, SeaArt.Walls(blocks, c => Map.World(c), Dungeons.Cell, Tint), SeaArt.Plain("Rack"));
            // Litter at their foot, only on the plain seabed of the maze's corridors.
            bool Plain(Vector2Int c) => Map.Inside(c.x, c.y) && (Map[c] == '.' || char.IsLower(Map[c])) && !decor.Draws(c) && !Map.BlockStarts.Contains(c);
            Parts.Shape("Rack Litter", transform, SeaArt.Scatter(blocks, c => Map.World(c), Dungeons.Cell, Tint, Plain), SeaArt.Plain("Scatter"));
        }

        public static float Hash(int x, int y)
        {
            var n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        // The lighthouse of stacked server cabinets, its lamp and two beams sweeping the sea. A modelled one
        // (SeaScene) stands already; its lamp takes the light's colour.
        void BuildLighthouse()
        {
            var root = Parts.Node("Lighthouse", transform, Map.World(Def.LighthouseCell.x, Def.LighthouseCell.y));
            lamp = SeaArt.Toon(Def.Lighthouse, Def.Lighthouse, Color.white, Def.Lighthouse * 0.8f, 0.02f);
            Vector3 light; // the lamp's centre in root
            if (scene != null)
            {
                scene.Light(lamp);
                light = root.InverseTransformPoint(scene.LampAt);
            }
            else
            {
                var shell = SeaArt.Toon(Palette.Hex("#1A2944"), Palette.Hex("#0A1222"), Def.Lighthouse, Palette.Hex("#050A14"), 0.04f);
                var y = 0f;
                float[] widths = { 7.6f, 6.8f, 6f, 5.2f, 4.4f, 3.6f };
                float[] heights = { 5f, 4.6f, 4.4f, 4.2f, 4f, 3.6f };
                for (var i = 0; i < widths.Length; i++)
                {
                    var r = Parts.Shape("Cabinet", root, SeaArt.Box(new Vector3(widths[i], heights[i], widths[i])), shell, new Vector3(0, y + heights[i] / 2f, 0));
                    r.transform.localRotation = Quaternion.Euler(0, i * 9f, 0);
                    y += heights[i];
                }
                light = new Vector3(0, y + 1.6f, 0);
                Parts.Shape("Lamp", root, SeaArt.Sphere(1.6f), lamp, light);
            }
            var halo = SeaArt.Glow(SeaArt.Atlas, Def.Lighthouse * 0.5f);
            halo.mainTextureScale = new Vector2(0.25f, 1f);
            halo.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            var haloQuad = Parts.Shape("Halo", root, SeaArt.Quad(18f, 18f), halo, light).transform;
            haloQuad.gameObject.AddComponent<Billboard>();

            lampBeams = Parts.Node("Beams", root, light);
            beamA = SeaArt.Glow(SeaArt.Beam, Def.Lighthouse * 0.22f);
            beamB = SeaArt.Glow(SeaArt.Beam, Def.Lighthouse * 0.22f);
            var tube = SeaArt.Tube(0.8f, 9f, 90f, 24);
            Parts.Shape("BeamA", lampBeams, tube, beamA).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            Parts.Shape("BeamB", lampBeams, tube, beamB).transform.localRotation = Quaternion.Euler(-90f, 0, 0);
        }

        void BuildProps()
        {
            var counts = new Dictionary<PropKind, int>();
            for (var y = 0; y < Map.Height; y++)
            for (var x = 0; x < Map.Width; x++)
            {
                PropKind kind;
                switch (Map[x, y])
                {
                    case 'F': kind = PropKind.Fragment; break;
                    case 'C': kind = PropKind.Chest; break;
                    case 'T': kind = PropKind.Terminal; break;
                    case 'B': kind = PropKind.Beacon; break;
                    case 'R': kind = PropKind.Console; break;
                    case 'G': kind = PropKind.Goal; break;
                    case 'X':
                        // Across the corridor: spanning east-west unless the corridor itself runs east-west.
                        var alongX = !(Map.Opaque(x, y - 1) && Map.Opaque(x, y + 1));
                        firewalls.Add(new Firewall(Map.World(x, y), alongX, transform));
                        continue;
                    default:
                        if (char.IsDigit(Map[x, y])) SpawnEnemy(Map[x, y], new Vector2Int(x, y));
                        continue;
                }
                counts.TryGetValue(kind, out var n);
                counts[kind] = n + 1;
                Props.Add(new SeaProp(kind, new Vector2Int(x, y), n, Map, transform));
            }
            if (Map.Find('D').Count > 0) BuildDoor();
        }

        // Longest side in metres of each wreck (Resources/Models/Props).
        static readonly Dictionary<string, float> WreckSizes = new Dictionary<string, float>
        {
            ["FallenRack"] = 3.8f, ["CableDrum"] = 2.6f, ["BoardCoral"] = 3.2f, ["DataCrystals"] = 3f,
            ["OldMonitor"] = 2.2f, ["CableHeap"] = 3f, ["DrivePile"] = 2.6f,
        };

        static string[] BigWrecks(ZoneStyle style) => style switch
        {
            ZoneStyle.Coral or ZoneStyle.Reef => new[] { "BoardCoral" },
            ZoneStyle.Context or ZoneStyle.Rift or ZoneStyle.WhiteHall or ZoneStyle.Ridge => new[] { "DataCrystals", "FallenRack" },
            ZoneStyle.Graveyard or ZoneStyle.RecycleBin => new[] { "FallenRack", "OldMonitor" },
            _ => new[] { "FallenRack", "CableDrum" },
        };

        // Corners take what stands up.
        static string[] CornerWrecks(ZoneStyle style) => style switch
        {
            ZoneStyle.Coral or ZoneStyle.Reef => new[] { "BoardCoral", "CableDrum" },
            ZoneStyle.Context or ZoneStyle.Rift or ZoneStyle.WhiteHall or ZoneStyle.Ridge or ZoneStyle.Plaza => new[] { "DataCrystals", "CableDrum" },
            ZoneStyle.Graveyard or ZoneStyle.RecycleBin => new[] { "OldMonitor", "CableDrum" },
            _ => new[] { "CableDrum", "OldMonitor", "DataCrystals" },
        };

        // Walls take what lies along them.
        static string[] WallWrecks(ZoneStyle style) => style switch
        {
            ZoneStyle.Coral or ZoneStyle.Reef => new[] { "BoardCoral", "CableHeap" },
            ZoneStyle.Context or ZoneStyle.Rift or ZoneStyle.WhiteHall => new[] { "DataCrystals", "FallenRack", "DrivePile" },
            ZoneStyle.Graveyard or ZoneStyle.RecycleBin => new[] { "DrivePile", "OldMonitor", "FallenRack" },
            _ => new[] { "FallenRack", "CableHeap", "DrivePile" },
        };

        // The wreckage of the sea's corridors (Tripo models, Dungeon/SeaModels.cs): something big filling the end
        // of a dead end, something standing in a corner, now and then something lying along a wall, each in a faint
        // pool of its zone's light. Only on the plain seabed clear of anything marked on the map (props, noise
        // bodies and their rounds, the puzzles), never across the way, and each stops the feet (DungeonMap.Wrecks).
        void BuildWreckage()
        {
            var root = Parts.Node("Wreckage", transform);
            var placed = new List<Vector2Int>();
            var pools = new Dictionary<ZoneStyle, Material>();
            var steps = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            for (var y = 1; y < Map.Height - 1; y++)
            for (var x = 1; x < Map.Width - 1; x++)
            {
                var c = new Vector2Int(x, y);
                if (Map[c] != '.' || Marked(c) || placed.Exists(p => (p - c).sqrMagnitude < 9)) continue;
                var walls = new List<Vector2Int>();
                foreach (var step in steps)
                    if (Map[c + step] == '#' || Map[c + step] == '=') walls.Add(step);
                var style = decor.StyleAt(c);
                var h = Hash(x * 7 + 3, y * 5 - 1);
                var centre = Map.World(c);
                Vector3 at, facing;
                string[] kinds;
                float chance, swing, widest; // widest: the most its footprint may stick out, so the way stays clear
                var alongWall = false;
                if (walls.Count == 3)
                {
                    // Pushed into the closed end, its front to the way out.
                    facing = DungeonMap.Direction(-(walls[0] + walls[1] + walls[2]));
                    at = centre - facing * 1.1f;
                    kinds = BigWrecks(style);
                    chance = 0.9f;
                    swing = 8f;
                    widest = 1f;
                }
                else if (walls.Count == 2 && walls[0] + walls[1] != Vector2Int.zero)
                {
                    var corner = DungeonMap.Direction(walls[0] + walls[1]);
                    at = centre + corner * 1.25f;
                    facing = -corner.normalized;
                    kinds = CornerWrecks(style);
                    chance = 0.85f;
                    swing = 40f;
                    widest = 1.1f;
                }
                else if (walls.Count is 1 or 2)
                {
                    var side = DungeonMap.Direction(walls[walls.Count == 2 && h >= 0.5f ? 1 : 0]);
                    at = centre + side * 1.25f; // then up to the wall, below
                    facing = -side;
                    alongWall = true;
                    kinds = WallWrecks(style);
                    chance = walls.Count == 2 ? 0.55f : 0.45f;
                    swing = 6f;
                    widest = 0.75f;
                }
                else continue;
                if (Hash(y * 3 + 1, x * 9 + 2) > chance) continue;
                placed.Add(c);
                var name = kinds[Mathf.Min(kinds.Length - 1, (int)(h * kinds.Length))];
                var turn = Hash(x + 11, y * 2);
                var wreck = SeaModels.Place(name, root, WreckSizes[name] * (0.9f + turn * 0.2f)).transform;
                wreck.localPosition = at;
                // Its footprint before it turns: a row of circles along its longer side.
                var bounds = wreck.GetComponentInChildren<Renderer>().bounds;
                var length = Mathf.Max(bounds.size.x, bounds.size.z);
                var radius = Mathf.Min(bounds.size.x, bounds.size.z) / 2f * 0.9f;
                var axis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                if (radius > widest)
                {
                    wreck.localScale = Vector3.one * (widest / radius);
                    length *= widest / radius;
                    radius = widest;
                }
                if (alongWall) at = wreck.localPosition = centre - facing * (Dungeons.Cell / 2f - radius - 0.05f);
                wreck.localRotation = Quaternion.LookRotation(facing) * Quaternion.Euler(0f, (turn - 0.5f) * 2f * swing, 0f);
                var circles = Mathf.Max(1, Mathf.RoundToInt(length / (radius * 2f)));
                for (var i = 0; i < circles; i++)
                {
                    var along = circles == 1 ? 0f : (i / (circles - 1f) - 0.5f) * (length - radius * 2f);
                    Map.Wrecks.Add((at + wreck.localRotation * axis * along, radius));
                }
                var unscaled = 1f / wreck.localScale.x; // the decals below are in its (scaled) space
                SeaArt.Shadow(wreck, length * 0.9f * unscaled);
                if (!pools.TryGetValue(style, out var pool))
                {
                    pool = pools[style] = SeaArt.Glow(SeaArt.Atlas, SeaDecor.For(style).Floor.WithAlpha(0.5f));
                    pool.mainTextureScale = new Vector2(0.25f, 1f);
                    pool.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
                }
                Parts.Shape("Pool", wreck, SeaArt.Decal(length * 1.8f * unscaled), pool, new Vector3(0f, 0.03f, 0f));
            }
        }

        // Something marked on the map within two cells (within one for a noise body's round), or a cache block.
        bool Marked(Vector2Int c)
        {
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var n = new Vector2Int(c.x + dx, c.y + dy);
                var mark = Map[n];
                if (".#=%".IndexOf(mark) >= 0 && !Map.BlockStarts.Contains(n)) continue;
                if (char.IsLower(mark) && Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) > 1) continue;
                return true;
            }
            return false;
        }

        void SpawnEnemy(char mark, Vector2Int cell)
        {
            var spawn = Array.Find(Def.Enemies, e => e.Mark == mark);
            if (spawn == null)
            {
                Debug.LogError($"迷宫 {Def.Id} 的噪声体 {mark} 没有定义");
                return;
            }
            Enemies.Add(new SeaNoise(spawn, Map, transform, cell));
        }

        // The sealed white door at the end of the deep layer: another AI's area lies behind it.
        void BuildDoor()
        {
            var cells = Map.Find('D');
            var a = Map.World(cells[0]);
            var b = Map.World(cells[cells.Count - 1]);
            var centre = (a + b) / 2f + new Vector3(0, 0, -Dungeons.Cell / 2f + 0.3f);
            var width = Vector3.Distance(a, b) + Dungeons.Cell;
            var root = door = Parts.Node("Sealed Door", transform, centre);
            var white = SeaArt.Toon(Palette.Hex("#F4F7FF"), Palette.Hex("#B8C6E6"), new Color(1f, 1f, 1f, 1f), Palette.Hex("#3A4660"), 0.03f);
            Parts.Shape("Slab", root, SeaArt.Box(new Vector3(width, 11f, 0.6f)), white, new Vector3(0, 5.5f, 0));
            var seam = SeaArt.Glow(SeaArt.White, new Color(0.85f, 0.92f, 1f, 0.9f));
            doorSeam = Parts.Shape("Seam", root, SeaArt.Box(new Vector3(0.12f, 10.4f, 0.7f)), seam, new Vector3(0, 5.5f, 0)).transform;
            var cloud = SeaArt.Glow(SeaArt.Atlas, new Color(0.8f, 0.88f, 1f, 0.5f));
            cloud.mainTextureScale = new Vector2(0.25f, 1f);
            cloud.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            doorMist = Parts.Node("Mist", root);
            for (var i = 0; i < 5; i++)
                Parts.Shape("Cloud", doorMist, SeaArt.Quad(7f, 4f), cloud, new Vector3((i - 2) * 3f, 0.8f + (i % 2) * 0.6f, -0.6f));
        }

        void BuildAmbience()
        {
            // Plankton-like motes around the player, and the sea's lost things drifting higher up.
            motes = Parts.Node("Motes", transform);
            SeaArt.Particles(motes, "Motes", SeaArt.AtlasDot, SeaArt.AtlasDot, 260, new Vector3(70f, 18f, 70f), 9f, 0.08f, 0.22f, new Color(0.5f, 0.85f, 1f, 0.9f), 0.25f);
            SeaArt.Particles(motes, "Debris", SeaArt.AtlasBubble, SeaArt.AtlasCard, 90, new Vector3(110f, 24f, 110f), 20f, 0.7f, 2.4f, new Color(0.45f, 0.75f, 1f, 0.55f), 0.12f, 14f);

            // Shafts of light from the surface.
            beams = Parts.Node("Shafts", transform, Centre);
            var shaft = SeaArt.Glow(SeaArt.Beam, new Color(0.35f, 0.65f, 0.9f, 0.07f));
            var tube = SeaArt.Tube(1.5f, 5f, 60f, 14);
            for (var i = 0; i < 9; i++)
            {
                var angle = i * 40f + Hash(i, 3) * 25f;
                var radius = 10f + Hash(3, i) * Mathf.Max(Map.Width, Map.Height) * Dungeons.Cell * 0.45f;
                var p = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad) * radius, 62f, Mathf.Sin(angle * Mathf.Deg2Rad) * radius);
                var t = Parts.Shape("Shaft", beams, tube, shaft, p).transform;
                t.localRotation = Quaternion.Euler(180f + Hash(i, 9) * 14f - 7f, 0, Hash(9, i) * 14f - 7f);
            }
        }

        // ------------------------------------------------------------------ playing

        public void Show(bool on)
        {
            shown = on;
            gameObject.SetActive(on);
        }

        public void Place(Vector2Int cell, float yaw)
        {
            PlayerPosition = Map.World(cell);
            PlayerYaw = yaw;
            CameraYaw = yaw;
            velocity = Vector3.zero;
            cameraDistance = CameraDistance;
            Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0, yaw, 0));
            Companion?.Place(PlayerPosition, yaw, Map);
            Reveal();
            UpdateCamera(0f, true);
        }

        // move: x right, y forward relative to the camera; look: degrees to turn the camera this frame.
        public void Step(float dt, Vector2 move, bool sprint, float turnKeys, Vector2 lookDegrees)
        {
            CameraYaw += turnKeys * KeyTurnSpeed * dt + lookDegrees.x;
            pitch = Mathf.Clamp(pitch - lookDegrees.y, 8f, 42f);

            var yaw = CameraYaw * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            var wish = right * move.x + forward * move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            var target = wish * (sprint ? SprintSpeed : RunSpeed);
            velocity = Vector3.MoveTowards(velocity, target, Acceleration * dt);
            var before = PlayerPosition;
            PlayerPosition = Map.Slide(PlayerPosition, velocity * dt, SeaAvatar.Radius);
            var moved = PlayerPosition - before;
            Travelled += moved.magnitude;
            if (dt > 0f) velocity = moved / dt; // walls take the speed away
            if (wish.sqrMagnitude > 0.01f)
                PlayerYaw = Mathf.MoveTowardsAngle(PlayerYaw, Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg, 760f * dt);
            Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0, PlayerYaw, 0));
            Avatar.Animate(dt, velocity.magnitude);
            Reveal();

            var safe = time < SafeUntil;
            foreach (var e in Enemies)
            {
                if (!e.Active) continue;
                if (e.Step(dt, PlayerPosition, safe)) Spotted?.Invoke(e);
            }
            if (!safe)
            {
                foreach (var e in Enemies)
                {
                    if (!e.Active || Vector3.Distance(e.Position, PlayerPosition) > SeaNoise.TouchDistance) continue;
                    Touched?.Invoke(e);
                    break;
                }
            }
            UpdateCamera(dt, false);
        }

        // Dialogue and the like: the avatar stands, nobody walks.
        // <<mirror on>> at the white door (story: Prologue_Deep_Exit): the player steps up to it, the camera
        // looks over their shoulder, and the door's face turns into a mirror with the player's battle-coat
        // double in it. <<mirror off>> settles the reflection back to the player as they are.
        public async Awaitable Mirror(bool on)
        {
            if (door == null)
            {
                Debug.LogError($"<<mirror>>：{Def.Id} 没有白门");
                return;
            }
            if (!on)
            {
                if (mirror != null) await mirror.Settle();
                return;
            }
            var glass = door.position + new Vector3(0f, 0f, -0.33f); // just off the slab's face, toward the maze
            var stand = glass + new Vector3(-0.45f, 0f, -1.5f);
            PlayerPosition = stand;
            PlayerYaw = 0f;
            velocity = Vector3.zero;
            Avatar.Root.SetPositionAndRotation(stand, Quaternion.identity);
            var beside = stand + new Vector3(-1.1f, 0f, -0.3f);
            Companion?.Stand(beside, glass - beside);
            // Over the right shoulder, far enough out that the reflection shows beside the player, not behind,
            // and near enough that it fills a good part of the frame.
            Camera.transform.position = stand + new Vector3(0.95f, 1.6f, -2.1f);
            Camera.transform.rotation = Quaternion.LookRotation(new Vector3(stand.x + 0.1f, 1.3f, glass.z) - Camera.transform.position);
            // The seam would split the glass, and the mist at the door's foot hangs in front of it.
            doorSeam.gameObject.SetActive(false);
            doorMist.gameObject.SetActive(false);
            if (mirror == null) mirror = SeaMirror.Create(transform, Camera, glass, Vector3.back, 3f, 4f, Avatar, gender);
            await mirror.Open();
        }

        public void Idle(float dt)
        {
            Avatar.Animate(dt, 0f);
            velocity = Vector3.zero;
        }

        public void OpenFirewall()
        {
            Map.FirewallOpen = true;
            foreach (var f in firewalls) f.Open(time);
        }

        public bool FirewallNear(float metres)
        {
            if (Map.FirewallOpen) return false;
            foreach (var x in Map.Find('X'))
                if (Vector3.Distance(Map.World(x), PlayerPosition) < metres) return true;
            return false;
        }

        public Vector2Int PlayerCell => Map.CellAt(PlayerPosition);

        // ------------------------------------------------------------------ puzzles

        // Where the light has to reach, the goal only shows once it has.
        public bool GoalOpen => !Puzzles.HasLight || Puzzles.Lit;

        void ShowGoal(bool on)
        {
            foreach (var p in Props)
                if (p.Kind == PropKind.Goal) p.Root.gameObject.SetActive(on);
            if (decor.Exit != null) decor.Exit.gameObject.SetActive(on);
        }

        public CacheBlock BlockInReach(out Vector2Int direction) => Puzzles.BlockInReach(PlayerPosition, out direction);

        // Props and noise bodies stand in the way of a block.
        public bool Push(CacheBlock block, Vector2Int direction) => Puzzles.Push(block, direction, c =>
            Props.Exists(p => p.Cell == c) || Enemies.Exists(e => e.Active && Map.CellAt(e.Position) == c), time);

        public void ResetBlocks() => Puzzles.ResetBlocks(time);

        public LightPlate PlateInReach() => Puzzles.PlateInReach(PlayerPosition);

        // True the first time the light reaches the goal, which then shows.
        public bool Turn(LightPlate plate)
        {
            var found = Puzzles.Turn(plate, time);
            if (found) ShowGoal(true);
            return found;
        }

        public ZoneDef Zone => Def.ZoneAt(PlayerCell);

        // The closest prop within its reach that hasn't been used up.
        public SeaProp PropInReach()
        {
            SeaProp best = null;
            var bestD = float.MaxValue;
            foreach (var p in Props)
            {
                if (p.Kind == PropKind.Goal || (p.Used && p.Kind != PropKind.Beacon)) continue;
                var d = Vector3.Distance(p.Position, PlayerPosition);
                if (d > p.Reach || d >= bestD) continue;
                best = p;
                bestD = d;
            }
            return best;
        }

        // A noise body close enough to jump on.
        public SeaNoise EnemyInReach(float metres)
        {
            SeaNoise best = null;
            var bestD = metres;
            foreach (var e in Enemies)
            {
                if (!e.Active) continue;
                var d = Vector3.Distance(e.Position, PlayerPosition);
                if (d > bestD || !Map.Clear(e.Position, PlayerPosition)) continue;
                best = e;
                bestD = d;
            }
            return best;
        }

        void Reveal()
        {
            var c = PlayerCell;
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var x = c.x + dx;
                var y = c.y + dy;
                if (Map.Inside(x, y)) Revealed[x, y] = true;
            }
        }

        // Behind the avatar, pulled in when a rack is in the way.
        void UpdateCamera(float dt, bool snap)
        {
            var yaw = CameraYaw * Mathf.Deg2Rad;
            var p = pitch * Mathf.Deg2Rad;
            var back = -new Vector3(Mathf.Sin(yaw) * Mathf.Cos(p), -Mathf.Sin(p), Mathf.Cos(yaw) * Mathf.Cos(p));
            var focus = PlayerPosition + Vector3.up * CameraHeight;
            var free = CameraDistance;
            for (var d = 0.6f; d <= CameraDistance; d += 0.2f)
            {
                var at = focus + back * d;
                if (!Map.Free(at, 0.35f, true) && at.y < 9f)
                {
                    free = Mathf.Max(0.8f, d - 0.3f);
                    break;
                }
            }
            cameraDistance = free < cameraDistance || snap ? free : Mathf.MoveTowards(cameraDistance, free, dt * 6f);
            Camera.transform.position = focus + back * cameraDistance;
            Camera.transform.rotation = Quaternion.LookRotation(focus - Camera.transform.position + Vector3.up * 0.4f);
        }

        // Screen point of a world point (y from the top), or false behind the camera.
        public bool ToScreen(Vector3 world, out Vector2 screen)
        {
            var s = Camera.WorldToScreenPoint(world);
            screen = new Vector2(s.x, Screen.height - s.y);
            return s.z > 0.2f;
        }

        void Update()
        {
            if (!shown) return;
            time += UnityEngine.Time.unscaledDeltaTime;
            ApplyGlobals();
            var t = time;
            sky.position = Camera.transform.position;
            motes.position = new Vector3(PlayerPosition.x, 0f, PlayerPosition.z);
            lampBeams.localRotation = Quaternion.Euler(0, t * 24f, 0);
            foreach (var p in Props) p.Animate(t, Camera);
            foreach (var e in Enemies) e.Animate(t, UnityEngine.Time.unscaledDeltaTime);
            Companion?.Follow(PlayerPosition, UnityEngine.Time.unscaledDeltaTime);
            decor.Animate(t);
            scene?.Animate(t);
            Puzzles.Animate(t);
            foreach (var f in firewalls) f.Animate(t);
        }

        void ApplyGlobals() => ApplySea(time, PlayerPosition, 6.5f);

        // The latent-space shaders' globals (Resources/Shaders/SeaCommon.cginc), here and on the battle field:
        // the seabed lights up within glowRadius of glowAt.
        public static void ApplySea(float time, Vector3 glowAt, float glowRadius)
        {
            Shader.SetGlobalColor("_SeaFogColor", FogColor);
            Shader.SetGlobalColor("_SeaSurfaceColor", SurfaceColor);
            Shader.SetGlobalVector("_SeaLightDir", new Vector4(0.35f, 1f, 0.25f, 0f));
            Shader.SetGlobalFloat("_SeaFogDensity", 0.016f);
            Shader.SetGlobalFloat("_SeaTime", time);
            Shader.SetGlobalVector("_SeaGlowPos", new Vector4(glowAt.x, glowAt.y, glowAt.z, glowRadius));
            Shader.SetGlobalColor("_SeaGlowColor", new Color(0.3f, 0.7f, 1f));
        }
    }

    // Turns to face the camera every frame (the lighthouse halo, the pop-up ads): the maze's, or the battle field's.
    public sealed class Billboard : MonoBehaviour
    {
        Camera view;

        void LateUpdate()
        {
            if (view == null)
            {
                var world = GetComponentInParent<DungeonWorld>();
                if (world != null) view = world.Camera;
                else
                {
                    var stage = GetComponentInParent<BattleStage>();
                    if (stage != null) view = stage.Camera;
                }
            }
            if (view == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - view.transform.position);
        }
    }
}
