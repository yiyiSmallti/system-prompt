using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // One latent-space area in 3D (P5-style third person): the rack maze on the seabed and what each zone
    // has instead (SeaDecor.cs), the lighthouse, the props, puzzles (SeaPuzzles.cs) and noise bodies, the
    // player's avatar, DeepSeek beside it and the camera that follows. The UI on top (UI/Dungeon/DungeonScreen.cs) drives it a step
    // at a time and runs the story around it.
    public sealed class DungeonWorld : MonoBehaviour
    {
        const float RunSpeed = 6.2f, SprintSpeed = 9f, Acceleration = 42f;
        const float CameraDistance = 7f, CameraHeight = 1.55f, KeyTurnSpeed = 110f;
        static readonly Color FogColor = Palette.Hex("#061428");
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
        readonly List<Firewall> firewalls = new List<Firewall>();
        Transform sky, motes, beams;
        Material lamp, beamA, beamB;
        Transform lampBeams;
        bool shown = true;

        public float Time => time;

        // gender: the player's (GameState.playerGender), which picks the avatar's model.
        public static DungeonWorld Create(DungeonDef def, string gender)
        {
            var go = new GameObject("Latent Space · " + def.Id);
            var world = go.AddComponent<DungeonWorld>();
            world.Build(def, gender);
            return world;
        }

        void OnDestroy()
        {
            Avatar?.Dispose();
            Companion?.Dispose();
        }

        void Build(DungeonDef def, string gender)
        {
            Def = def;
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
            BuildSea();
            BuildWalls();
            decor.Build();
            BuildLighthouse();
            BuildProps();
            Puzzles = new SeaPuzzles(def, Map, transform);
            Puzzles.Build();
            ShowGoal(GoalOpen);
            BuildAmbience();

            Avatar = new SeaAvatar(transform, gender);
            Companion = new SeaCompanion(transform);
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
            sky = Parts.Shape("Sky", transform, SeaArt.Sphere(1f, 0f, 32, 20), SeaArt.Plain("Sky")).transform;
            sky.localScale = Vector3.one * 420f;
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
            var walls = SeaArt.Walls(blocks, c => Map.World(c), Dungeons.Cell, c => SeaDecor.For(decor.StyleAt(c)).Rack);
            Parts.Shape("Racks", transform, walls, SeaArt.Plain("Rack"));
        }

        public static float Hash(int x, int y)
        {
            var n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        // The lighthouse of stacked server cabinets, its lamp and two beams sweeping the sea.
        void BuildLighthouse()
        {
            var root = Parts.Node("Lighthouse", transform, Map.World(Def.LighthouseCell.x, Def.LighthouseCell.y));
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
            lamp = SeaArt.Toon(Def.Lighthouse, Def.Lighthouse, Color.white, Def.Lighthouse * 0.8f, 0.02f);
            Parts.Shape("Lamp", root, SeaArt.Sphere(1.6f), lamp, new Vector3(0, y + 1.6f, 0));
            var halo = SeaArt.Glow(SeaArt.Atlas, Def.Lighthouse * 0.5f);
            halo.mainTextureScale = new Vector2(0.25f, 1f);
            halo.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            var haloQuad = Parts.Shape("Halo", root, SeaArt.Quad(18f, 18f), halo, new Vector3(0, y + 1.6f, 0)).transform;
            haloQuad.gameObject.AddComponent<Billboard>();

            lampBeams = Parts.Node("Beams", root, new Vector3(0, y + 1.6f, 0));
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
            var root = Parts.Node("Sealed Door", transform, centre);
            var white = SeaArt.Toon(Palette.Hex("#F4F7FF"), Palette.Hex("#B8C6E6"), new Color(1f, 1f, 1f, 1f), Palette.Hex("#3A4660"), 0.03f);
            Parts.Shape("Slab", root, SeaArt.Box(new Vector3(width, 11f, 0.6f)), white, new Vector3(0, 5.5f, 0));
            var seam = SeaArt.Glow(SeaArt.White, new Color(0.85f, 0.92f, 1f, 0.9f));
            Parts.Shape("Seam", root, SeaArt.Box(new Vector3(0.12f, 10.4f, 0.7f)), seam, new Vector3(0, 5.5f, 0));
            var cloud = SeaArt.Glow(SeaArt.Atlas, new Color(0.8f, 0.88f, 1f, 0.5f));
            cloud.mainTextureScale = new Vector2(0.25f, 1f);
            cloud.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            for (var i = 0; i < 5; i++)
                Parts.Shape("Cloud", root, SeaArt.Quad(7f, 4f), cloud, new Vector3((i - 2) * 3f, 0.8f + (i % 2) * 0.6f, -0.6f));
        }

        void BuildAmbience()
        {
            // Plankton-like motes around the player, and the sea's lost things drifting higher up.
            motes = Parts.Node("Motes", transform);
            Particles(motes, "Motes", SeaArt.AtlasDot, SeaArt.AtlasDot, 260, new Vector3(70f, 18f, 70f), 9f, 0.08f, 0.22f, new Color(0.5f, 0.85f, 1f, 0.9f), 0.25f);
            Particles(motes, "Debris", SeaArt.AtlasBubble, SeaArt.AtlasCard, 90, new Vector3(110f, 24f, 110f), 20f, 0.7f, 2.4f, new Color(0.45f, 0.75f, 1f, 0.55f), 0.12f, 14f);

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

        void Particles(Transform parent, string name, int firstTile, int lastTile, int count, Vector3 box, float lifetime,
            float minSize, float maxSize, Color color, float rise, float height = 4f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, height, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = lifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = count / lifetime;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            vel.y = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(firstTile / 4f, (lastTile + 0.99f) / 4f);
            sheet.cycleCount = 1;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SeaArt.Glow(SeaArt.Atlas, Color.white);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
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
            Companion?.Follow(PlayerPosition, UnityEngine.Time.unscaledDeltaTime, t);
            decor.Animate(t);
            Puzzles.Animate(t);
            foreach (var f in firewalls) f.Animate(t);
        }

        void ApplyGlobals()
        {
            Shader.SetGlobalColor("_SeaFogColor", FogColor);
            Shader.SetGlobalColor("_SeaSurfaceColor", SurfaceColor);
            Shader.SetGlobalVector("_SeaLightDir", new Vector4(0.35f, 1f, 0.25f, 0f));
            Shader.SetGlobalFloat("_SeaFogDensity", 0.016f);
            Shader.SetGlobalFloat("_SeaTime", time);
            Shader.SetGlobalVector("_SeaGlowPos", new Vector4(PlayerPosition.x, PlayerPosition.y, PlayerPosition.z, 6.5f));
            Shader.SetGlobalColor("_SeaGlowColor", new Color(0.3f, 0.7f, 1f));
        }
    }

    // Turns to face the area's camera every frame (the lighthouse halo).
    public sealed class Billboard : MonoBehaviour
    {
        DungeonWorld world;

        void LateUpdate()
        {
            if (world == null) world = GetComponentInParent<DungeonWorld>();
            if (world == null || world.Camera == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - world.Camera.transform.position);
        }
    }
}
