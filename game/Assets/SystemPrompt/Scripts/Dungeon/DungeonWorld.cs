using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // One latent-space area in 3D (P5-style third person): the rack maze on the seabed and what each zone
    // has instead (SeaDecor.cs; an area modelled in Blender has SeaScene.cs for both), the lighthouse, the
    // props, the wreckage (SeaModels.cs), puzzles (SeaPuzzles.cs) and noise bodies, the
    // player's avatar, DeepSeek beside it and the camera that follows. The UI on top (UI/Dungeon/DungeonScreen.cs) drives it a step
    // at a time and runs the story around it. Its Update comes after GameRoot's, which steps the player, so the figures
    // and the camera move on from where the player is this frame.
    [DefaultExecutionOrder(50)]
    public sealed class DungeonWorld : MonoBehaviour
    {
        const float RunSpeed = 6.2f, SprintSpeed = 9f, Acceleration = 42f;
        const float CameraDistance = 7f, CameraHeight = 1.55f, KeyTurnSpeed = 110f, CameraFov = 52f;
        public static readonly Color FogColor = Palette.Hex("#061428");
        // How much of the surface's light net reaches the seabed: the last area's (DungeonDef.Caustics), which a battle
        // fought there keeps.
        static float caustics = 1f;
        // The light the seabed throws up into the figures' shade (Toon.shader _SeaGroundColor): that of the zone the
        // player is in, eased across as they walk into the next and kept through the corridors between; a battle
        // keeps the zone it was fought in.
        static Color ground = Ground(null);
        ZoneDef groundZone;
        Color groundTo;
        bool groundSnap;
        // The water's air (SeaAtmos.Current, which the battle keeps): the zone's, eased across as the player walks into
        // the next (about 2 s, never a switch at the border), straight there when put down somewhere; the first walk
        // into the white door's hall fills the water with light for a moment (Surge).
        ZoneDef airZone;
        SeaAir airTo;
        bool airSnap;
        float surge = -1f;
        bool surged;
        // The maze's lights reaching the racks and the figures round them (SeaLights), built with the props.
        SeaLights lights;
        SeaFloaters.ShaftPool shaftPool; // the deep's shafts of light (none near the surface, which has its own)

        public DungeonDef Def { get; private set; }
        public DungeonMap Map { get; private set; }
        public Camera Camera { get; private set; }
        public SeaAvatar Avatar { get; private set; }
        public SeaCompanion Companion { get; private set; } // DeepSeek
        public SeaPuzzles Puzzles { get; private set; }
        public readonly List<SeaNoise> Enemies = new List<SeaNoise>();
        public readonly List<SeaProp> Props = new List<SeaProp>();
        public readonly List<SeaPearl> Pearls = new List<SeaPearl>();
        public bool[,] Revealed { get; private set; }

        public Vector3 PlayerPosition { get; private set; }
        public float PlayerYaw { get; private set; }
        public float CameraYaw { get; private set; }
        public float Travelled { get; private set; } // metres walked, for the maintenance progress
        public float SafeUntil;                    // noise bodies ignore the player until then
        // Set by DungeonScreen for where the two look (Looks): what the Enter prompt is over, and who has the line in a talk.
        public Vector3? Interest;
        public bool PlayerSpeaks, DeepSeekSpeaks;

        // Raised during Step.
        public event Action<SeaNoise> Spotted;
        public event Action<SeaNoise> Touched;

        Vector3 velocity;
        bool driven; // Step or Idle ran this frame
        float pitch = 20f;
        float cameraDistance = CameraDistance;
        // The follow camera's easing (UpdateCamera): its focus and how fast that moves, the lead it takes ahead of a
        // run, how far it has drawn back for a sprint and risen for a rack behind it (0 to 1 each).
        Vector3 focus, focusVelocity, lead, leadVelocity;
        float sprinting, sprintingVelocity, cramped, crampedVelocity;
        bool held, arriving; // the mirror's shot or the dive's fall has the camera
        // A map strike (Strike) under way: on what, with the noise bodies joining it; seconds into it; the player's
        // turn to it (from, to, over how long, after how long); the camera's punch (how far in, how much narrower,
        // tipped and cut round by how many degrees, shaken, onto what) and where it settles; the phone's shutter to come.
        SeaNoise struck;
        readonly List<SeaNoise> struckToo = new List<SeaNoise>();
        float strikeTime, turnFrom, turnTo, turnSeconds, turnAfter;
        float punchIn, punchFov, punchRoll, punchSwing, punchedFov;
        Vector3 punchOn, punchedAt;
        Quaternion punchedTurn;
        bool punchShake, shutter;
        // Looks: seconds to each one's next glance at the other (below 0 while glancing); what the player's body turns to
        // once stood still a while (a noise body after them, the Enter prompt's object), and for how long they have.
        float playerGlance, herGlance;
        Vector3? faceTo;
        float stillFor;
        bool facing;
        // What the two leave on the seabed, and each one's seconds since last going at a run (Pulled).
        SeaWake wake;
        float playerRan = 9f, herRan = 9f;
        // The light the set pieces throw round them and the seabed's rings (SeaGlims); how far the seabed's glow round
        // the player has turned to a chase's red (0 to 1).
        public readonly SeaGlims Glims = new SeaGlims();
        float chaseTint;
        // A cutaway (DungeonScreen.Cutaway) has the camera: the follow camera keeps track but places nothing, and the
        // maze's clock runs at `pace` (3 when the player hurries it on), so what the shot shows keeps up.
        float pace = 1f;
        // The departure (Depart): the lighthouse's beams held turned on the two and dipped further toward them, the
        // column under their feet; the timeout's dark (Blackout): the fog thick and black, ended by Restore.
        Quaternion? lampAim;
        float lampDip;
        Material beamLook;
        SeaColumn departure;
        float fogDensity = FogDensity;
        Color fogColor = FogColor;
        int fogSerial;
        const float FogDensity = 0.016f, DepartFov = 68f;
        static readonly Color Dark = Palette.Hex("#010308");
        // The follow camera turned most of the way toward something for a moment (Glance), from where it stands.
        Vector3 glanceAt;
        bool glancing;
        float glance;
        float time;
        SeaDecor decor;
        SeaScene scene; // the area's scenery modelled in Blender, if it has one
        readonly List<Firewall> firewalls = new List<Firewall>();
        Transform sky, motes, beams;
        Material[] shafts; // the shafts of light near the surface, each breathing on its own
        Material lamp;
        Transform lampBeams, beamA, beamB;
        bool shown = true;
        string gender;
        Transform door; // the white door at the deep exit, if the area has one
        readonly List<Transform> underGlass = new List<Transform>(); // its mist in front of where the mirror comes (Mirror)
        Material mist; // the mist's, faded out as the two come up to the door (MirrorWalk)
        SeaMirror mirror;
        bool walking; // the two going up to the white door (MirrorWalk): no braking
        bool leading; // MirrorWalk walks DeepSeek there (SeaCompanion.WalkTo), so she doesn't follow meanwhile
        Texture2D zones, walls; // the seabed's, made for this area

        public float Time => time;
        public SeaWake Wake => wake;
        public bool Cutting { get; private set; }
        public IReadOnlyList<Firewall> Firewalls => firewalls;

        // gender: the player's (GameState.playerGender), which picks the avatar's model; deepSeekAwakened
        // picks DeepSeek's (her hoodie dress before the lighthouse, her battle dress after).
        // It is built out of sight a piece a frame, its models read in the background first, so that the dive
        // playing meanwhile never stops; Show puts it on screen.
        public static async Awaitable<DungeonWorld> Create(DungeonDef def, string gender, bool deepSeekAwakened)
        {
            var files = await Preload(def, gender, deepSeekAwakened);
            var go = new GameObject("Latent Space · " + def.Id);
            var world = go.AddComponent<DungeonWorld>();
            world.shown = false;
            try
            {
                await world.Build(def, gender, deepSeekAwakened);
            }
            catch
            {
                world.Drop();
                throw;
            }
            GC.KeepAlive(files);
            return world;
        }

        // The area's scenery and the two figures, read in the background; the build's own Resources.Load calls
        // then find them in memory instead of reading them on the spot.
        static async Awaitable<List<ResourceRequest>> Preload(DungeonDef def, string gender, bool deepSeekAwakened)
        {
            var files = new List<(string path, Type type)>(SeaFigure.Files(gender, deepSeekAwakened));
            if (def.Scene != null)
                foreach (var part in new[] { "", "_outer", "_drift" }) files.Add((def.Scene + part, typeof(GameObject)));
            var requests = files.ConvertAll(f => Resources.LoadAsync(f.path, f.type));
            foreach (var r in requests) await Awaitable.FromAsyncOperation(r);
            return requests;
        }

        // The area taken down with what was made for it, as BattleStage.Drop: destroying its GameObject alone would
        // leave every dive's meshes, materials (the scenery's batched mesh and looks, the racks, the noise bodies,
        // the figures) and the seabed's textures behind.
        public void Drop()
        {
            Glims.Clear();
            ResetFog();
            Glance(null);
            glance = 0f;
            SeaAtmos.Current = SeaAtmos.None; // the story's battles after the dive are fought in the plain sea
            lights = null;
            shaftPool = null;
            scene?.Dispose();
            Parts.Free(gameObject);
            if (zones != null) Destroy(zones);
            if (walls != null) Destroy(walls);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Avatar?.Dispose();
            Companion?.Dispose();
        }

        async Awaitable Build(DungeonDef def, string gender, bool deepSeekAwakened)
        {
            Def = def;
            caustics = def.Caustics;
            groundZone = null;
            groundTo = Ground(null);
            groundSnap = true;
            airZone = null;
            airTo = SeaAtmos.For(def, null);
            airSnap = true;
            this.gender = gender;
            Map = new DungeonMap(def.Map);
            Revealed = new bool[Map.Width, Map.Height];

            var camGo = new GameObject("Sea Camera");
            camGo.transform.SetParent(transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = FogColor;
            Camera.fieldOfView = CameraFov;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 600f;
            Camera.depth = 10f;
            Camera.enabled = false;
            SeaLens.Fit(Camera, SeaLens.Explore);

            decor = new SeaDecor(def, Map, transform);
            scene = await SeaScene.Load(def, transform, Map, decor.WallTop);
            BuildSea();
            if (scene == null) BuildWalls();
            else Litter();
            await Awaitable.NextFrameAsync();
            decor.Build(scene != null, scene?.Screen);
            await Awaitable.NextFrameAsync();
            BuildLighthouse();
            await Awaitable.NextFrameAsync();
            BuildProps();
            for (var i = 0; i < def.Pearls.Length; i++) Pearls.Add(new SeaPearl(def.Pearls[i], i, false, Map, transform));
            await Awaitable.NextFrameAsync();
            BuildWreckage();
            await Awaitable.NextFrameAsync();
            Puzzles = new SeaPuzzles(def, Map, transform, Glims);
            Puzzles.Build();
            ShowGoal(GoalOpen);
            BuildAmbience();
            lights = SeaLights.Of(this);
            await Awaitable.NextFrameAsync();

            Avatar = new SeaAvatar(transform, gender);
            await Awaitable.NextFrameAsync();
            Companion = new SeaCompanion(transform, deepSeekAwakened);
            wake = new SeaWake(transform);
            Avatar.Footfall += wake.Footfall;
            Companion.Footfall += wake.Footfall;
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
            zones = decor.FloorLight(out var rect);
            walls = WallMask();
            floor.sharedMaterial.SetTexture("_ZoneTex", zones);
            floor.sharedMaterial.SetVector("_ZoneRect", rect);
            floor.sharedMaterial.SetTexture("_WallTex", walls);
            sky = Parts.Shape("Sky", transform, SeaArt.Sphere(1f, 0f, 32, 20), SeaArt.Plain("Sky")).transform;
            sky.localScale = Vector3.one * 420f;
        }

        // The maze's cells in SeaDecor.FloorLight's layout, for the seabed: red where a wall, low wall or the lighthouse
        // stands (it darkens at their foot), blue on the rift's open floor (the glowing cracks run through it).
        Texture2D WallMask()
        {
            var t = new Texture2D(Map.Width + 2, Map.Height + 2, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "Walls",
            };
            var px = new Color[t.width * t.height];
            for (var y = 0; y < Map.Height; y++)
            for (var x = 0; x < Map.Width; x++)
            {
                var c = new Color("#=L".IndexOf(Map[x, y]) >= 0 ? 1f : 0f, 0f, 0f, 0f);
                if (c.r == 0f && Def.ZoneAt(new Vector2Int(x, y))?.Style == ZoneStyle.Rift) c.b = 1f;
                px[(Map.Height - y) * t.width + x + 1] = c;
            }
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
            Parts.Shape("Racks", transform, SeaArt.Walls(blocks, c => Map.World(c), Dungeons.Cell, Tint), SeaArt.Plain("Rack"));
            Litter(blocks);
        }

        Color Tint(Vector2Int c) => SeaDecor.For(decor.StyleAt(c)).Rack;

        // Litter at the racks' foot (`blocks`), and crystals near the surface, only on the plain seabed of the maze's
        // corridors: at the racks built here, or at the modelled ones (SeaScene), whose faces stand where these would.
        void Litter(Dictionary<Vector2Int, float> blocks = null)
        {
            if (blocks == null)
            {
                blocks = new Dictionary<Vector2Int, float>();
                for (var y = 0; y < Map.Height; y++)
                for (var x = 0; x < Map.Width; x++)
                    if (Map[x, y] == '#' && !decor.Draws(new Vector2Int(x, y))) blocks[new Vector2Int(x, y)] = 0f;
            }
            bool Plain(Vector2Int c) => Map.Inside(c.x, c.y) && (Map[c] == '.' || char.IsLower(Map[c])) && !decor.Draws(c) && !Map.BlockStarts.Contains(c);
            Parts.Shape("Rack Litter", transform, SeaArt.Scatter(blocks, c => Map.World(c), Dungeons.Cell, Tint, Plain, Def.NearSurface), SeaArt.Plain("Scatter"));
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
            lamp = SeaArt.Toon(Def.Lighthouse * 0.6f, Def.Lighthouse * 0.6f, Color.clear, Def.Lighthouse * 2.4f, 0.02f);
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
            // Tilted down (DungeonDef.BeamTilt) the beams reach the seabed, and the halo is wide and faint, as the
            // lamp is seen from far off across the sea.
            var tilted = Def.BeamTilt > 0f;
            var halo = SeaArt.Glow(SeaArt.Atlas, Def.Lighthouse * (tilted ? 0.3f : 0.5f));
            halo.mainTextureScale = new Vector2(0.25f, 1f);
            halo.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            var haloSize = tilted ? 40f : 18f;
            var haloQuad = Parts.Shape("Halo", root, SeaArt.Quad(haloSize, haloSize), halo, light).transform;
            halo.SetFloat("_FogReach", LampReach);
            haloQuad.gameObject.AddComponent<Billboard>();

            lampBeams = Parts.Node("Beams", root, light);
            var beam = beamLook = SeaArt.Glow(SeaArt.Beam, Def.Lighthouse * 0.22f);
            beam.SetFloat("_FogReach", LampReach);
            var tube = SeaArt.Tube(0.8f, 9f, 90f, 24);
            beamA = Parts.Shape("BeamA", lampBeams, tube, beam).transform;
            beamA.localRotation = Quaternion.Euler(90f + Def.BeamTilt, 0, 0);
            beamB = Parts.Shape("BeamB", lampBeams, tube, beam).transform;
            beamB.localRotation = Quaternion.Euler(-90f - Def.BeamTilt, 0, 0);
            if (!tilted) return;
            // Spread up and down as the light they throw is (LightBeams), so the shaft meets the seabed where it lights it.
            var fan = new Vector3(1f, 1f, Mathf.Tan(BeamSpread * Mathf.Deg2Rad) / (8.2f / 90f));
            beamA.localScale = fan;
            beamB.localScale = fan;
            // A brighter core down the middle of each.
            var core = SeaArt.Glow(SeaArt.Beam, Def.Lighthouse * 0.4f);
            core.SetFloat("_FogReach", LampReach);
            var thin = SeaArt.Tube(0.3f, 1.2f, 90f, 12);
            Parts.Shape("CoreA", beamA, thin, core);
            Parts.Shape("CoreB", beamB, thin, core);
        }

        const float BeamSpread = 12f; // degrees a tilted beam spreads above and below its axis
        // The lamp, its halo and beams carry through the zone's fog much further than the racks (Glow.shader _FogReach), so
        // the lighthouse is seen from the start across the shallows however thick the water there.
        const float LampReach = 0.35f;
        const float BeamWidth = 7f;   // and to either side

        // The lamp breathes once every 4 s (barely, under 减弱闪光) and its beams, tilted down, light where they fall
        // (SeaCommon.cginc SeaBeam; ApplySea has turned that off for whoever else draws the sea, a battle).
        void LightBeams(float t)
        {
            if (Def.BeamTilt <= 0f) return;
            var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
            lamp.SetColor("_Emission", Def.Lighthouse * (2.4f * (1f + (calm ? 0.05f : 0.2f) * Mathf.Sin(t * Mathf.PI * 0.5f))));
            var apex = lampBeams.position;
            Shader.SetGlobalVector("_SeaBeamApex", new Vector4(apex.x, apex.y, apex.z, Mathf.Cos(BeamWidth * Mathf.Deg2Rad)));
            // Flat and of unit length: SeaBeam measures across against the cosine of BeamWidth, and a tilted beam's own
            // axis is only cos(tilt) long on the seabed, which no direction would ever reach.
            Shader.SetGlobalVector("_SeaBeamDirA", Flat(beamA.up));
            Shader.SetGlobalVector("_SeaBeamDirB", Flat(beamB.up));
            float Tan(float deg) => Mathf.Tan(deg * Mathf.Deg2Rad);
            float low = Def.BeamTilt + lampDip - BeamSpread, high = Def.BeamTilt + lampDip + BeamSpread;
            Shader.SetGlobalVector("_SeaBeamDip", new Vector4(Tan(low - 3f), Tan(low + 3f), Tan(high - 3f), Tan(high + 3f)));
            Shader.SetGlobalColor("_SeaBeamColor", (Def.Lighthouse * (calm ? 0.6f : 1.2f)).WithAlpha(1f));
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
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

        // Something marked on the map within two cells (within one for a noise body's round), a cache block, or a
        // 数据珍珠 within one.
        bool Marked(Vector2Int c)
        {
            foreach (var p in Def.Pearls)
                if (Mathf.Abs(p.x - c.x) <= 1 && Mathf.Abs(p.y - c.y) <= 1) return true;
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
            // White by its albedo (0.9) with a cool blue shade, not white to the limit: in the white hall's bright water
            // a face lit and shaded at white clipped to one flat board and lost its panels. A little blue emission keeps
            // it the coolest, brightest face in the hall, short of the lens's threshold (SeaLens): so big a face glowing
            // would flood the picture with its halo. What glows is its outline, the seam between the leaves and a strip
            // down each pilaster.
            var white = SeaArt.Toon(Palette.Hex("#E6ECF8"), Palette.Hex("#A4B0CC"), new Color(1f, 1f, 1f, 0.25f), Palette.Hex("#141A28"), 0.03f);
            var trim = SeaArt.Toon(Palette.Hex("#DCE3F4"), Palette.Hex("#96A4C4"), new Color(1f, 1f, 1f, 0.25f), Palette.Hex("#101626"), 0.03f);
            Parts.Shape("Slab", root, SeaArt.Box(new Vector3(width, 11f, 0.6f)), white, new Vector3(0, 5.5f, 0));
            // Two leaves of raised panels on the face toward the maze, a lintel over them and a pilaster at each
            // edge. The mirror comes on just in front of the panels (Mirror), over them.
            var leaf = width / 2f;
            var panelW = (leaf - 1.5f) / 2f;
            const float panelH = 2.75f;
            var panels = Parts.Node("Panels", root, new Vector3(0, 0, -0.33f));
            var panel = SeaArt.Box(new Vector3(panelW, panelH, 0.07f), 0.03f);
            for (var side = -1; side <= 1; side += 2)
            for (var col = 0; col < 2; col++)
            for (var row = 0; row < 3; row++)
            {
                var x = side * (0.5f + panelW / 2f + col * (panelW + 0.5f));
                Parts.Shape("Panel", panels, panel, trim, new Vector3(x, 0.9f + panelH / 2f + row * (panelH + 0.45f), 0f));
            }
            Parts.Shape("Lintel", root, SeaArt.Box(new Vector3(width + 0.5f, 0.6f, 0.9f)), trim, new Vector3(0, 10.9f, 0));
            var strip = SeaArt.Glow(SeaArt.White, new Color(0.8f, 0.9f, 1f, 0.6f));
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * (width / 2f - 0.25f);
                Parts.Shape("Pilaster", root, SeaArt.Box(new Vector3(0.5f, 10.6f, 0.8f)), trim, new Vector3(x, 5.3f, 0));
                Parts.Shape("Strip", root, SeaArt.Quad(0.12f, 10.4f), strip, new Vector3(x, 5.3f, -0.42f));
            }
            var seam = SeaArt.Glow(SeaArt.White, new Color(0.85f, 0.92f, 1f, 0.9f));
            Parts.Shape("Seam", root, SeaArt.Box(new Vector3(0.12f, 10.4f, 0.7f)), seam, new Vector3(0, 5.5f, 0));
            var cloud = mist = SeaArt.Glow(SeaArt.Atlas, MistColor);
            cloud.mainTextureScale = new Vector2(0.25f, 1f);
            cloud.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
            var clouds = Parts.Node("Mist", root);
            for (var i = 0; i < 5; i++)
            {
                var shape = Parts.Shape("Cloud", clouds, SeaArt.Quad(7f, 4f), cloud, new Vector3((i - 2) * 3f, 0.8f + (i % 2) * 0.6f, -0.6f)).transform;
                if (Mathf.Abs((i - 2) * 3f) - 3.5f < MirrorWidth / 2f) underGlass.Add(shape);
            }
        }

        void BuildAmbience()
        {
            // Plankton-like motes around the player, and the sea's lost things drifting higher up: near the surface
            // thick and big from just over the low racks to a little over the tall ones, 4 to 12 m, where the camera
            // takes them in over the walls; none of them over 7 % of the view, so one drifting by the lens as it looks
            // up at the lighthouse stays a speck and does not cover the lamp.
            motes = Parts.Node("Motes", transform);
            SeaArt.Particles(motes, "Motes", SeaArt.AtlasDot, SeaArt.AtlasDot, 260, new Vector3(70f, 18f, 70f), 9f, 0.08f, 0.22f, new Color(0.5f, 0.85f, 1f, 0.9f), 0.25f);
            SeaFloaters.Explore(motes, Camera, !Def.NearSurface); // on a current, near the lens, rising in the deep
            if (Def.NearSurface)
                SeaArt.Particles(motes, "Debris", SeaArt.AtlasBubble, SeaArt.AtlasCard, 160, new Vector3(90f, 8f, 90f), 20f, 1.2f, 3.5f, new Color(0.45f, 0.75f, 1f, 0.6f), 0.12f, 8f,
                    maxScreen: 0.07f);
            else
                SeaArt.Particles(motes, "Debris", SeaArt.AtlasBubble, SeaArt.AtlasCard, 90, new Vector3(110f, 24f, 110f), 20f, 0.7f, 2.4f, new Color(0.45f, 0.75f, 1f, 0.55f), 0.12f, 14f);

            // Columns of bubbles from vents in the seabed, a faint glow round each vent. Their look is made only where
            // there are vents: Parts.Free frees what a piece wears, so one made for none would outlive the maze.
            Material vent = null;
            Mesh spot = null;
            foreach (var v in Def.Vents)
            {
                if (vent == null)
                {
                    vent = SeaArt.Glow(SeaArt.Atlas, new Color(0.55f, 0.85f, 1f, 0.45f));
                    vent.mainTextureScale = new Vector2(0.25f, 1f);
                    vent.mainTextureOffset = new Vector2(0.25f * SeaArt.AtlasDot, 0f);
                    spot = SeaArt.Decal(1.6f);
                }
                var at = Map.World(v.x, v.y);
                Parts.Shape("Vent", transform, spot, vent, at + Vector3.up * 0.03f);
                var column = Parts.Node("Bubbles", transform, at);
                SeaArt.Particles(column, "Bubbles", 0, 0, 40, new Vector3(0.6f, 0.2f, 0.6f), 8f, 0.06f, 0.22f, new Color(0.7f, 0.9f, 1f, 0.8f), 1.2f, 0.1f, SeaArt.Bubble);
            }

            if (Def.NearSurface)
            {
                SurfaceShafts();
                return;
            }
            // Shafts of light from the surface, anchored to the world near the camera (SeaFloaters.ShaftPool).
            beams = Parts.Node("Shafts", transform);
            shaftPool = new SeaFloaters.ShaftPool(beams);
        }

        // Near the surface, six shafts of light where the camera sees them: four round the lighthouse, 18 to 30 m off it
        // (halfway down) on the side the sea lies, and one either side of the way from the start up to it, 24 m off it so
        // a camera at eye height on the way never stands in one and gets the top of its picture washed. They lean in
        // toward its top, so they fan out from the light above it, and each breathes slowly at its own pace (Update).
        void SurfaceShafts()
        {
            var foot = Map.World(Def.LighthouseCell.x, Def.LighthouseCell.y);
            beams = Parts.Node("Shafts", transform, foot);
            var tube = SeaArt.Tube(2.4f, 8f, 42f, 14);
            var toSea = Centre - foot;
            toSea.y = 0f;
            var seaYaw = Mathf.Atan2(toSea.x, toSea.z) * Mathf.Rad2Deg;
            var start = Map.World(Map.Find('S')[0]);
            var way = start - foot;
            var across = Vector3.Cross(Vector3.up, way.normalized);
            var at = new Vector3[6];
            float[] angles = { -100f, -35f, 35f, 100f };
            for (var i = 0; i < 4; i++)
            {
                var yaw = (seaYaw + angles[i]) * Mathf.Deg2Rad;
                at[i] = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (18f + Hash(i, 13) * 12f);
            }
            at[4] = way * 0.6f + across * 24f;
            at[5] = way * 0.6f - across * 24f;
            shafts = new Material[at.Length];
            for (var i = 0; i < at.Length; i++)
            {
                // Each its own material, for its own breath; the light's streaks turn slowly round it.
                var look = shafts[i] = SeaArt.Glow(SeaArt.Beam, ShaftColor);
                look.mainTextureScale = new Vector2(1f, 0.6f); // kept bright down to the racks, where it is seen
                look.SetVector("_Scroll", new Vector4((Hash(i, 17) - 0.5f) * 0.02f, 0f, 0f, 0f));
                var flat = at[i].normalized;
                var lean = (12f + Hash(17, i) * 6f) * Mathf.Deg2Rad;
                var down = -Vector3.up * Mathf.Cos(lean) + flat * Mathf.Sin(lean);
                var t = Parts.Shape("Shaft", beams, tube, look, at[i] - flat * (21f * Mathf.Sin(lean)) + Vector3.up * 40f).transform;
                t.localRotation = Quaternion.FromToRotation(Vector3.up, down);
            }
        }

        static readonly Color ShaftColor = new Color(0.35f, 0.65f, 0.9f, 0.13f);

        // ±15% over 8 to 12 s each; ±5% under 减弱闪光.
        void BreatheShafts(float t)
        {
            if (shafts == null) return;
            var amount = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash ? 0.05f : 0.15f;
            for (var i = 0; i < shafts.Length; i++)
            {
                var period = 8f + Hash(i, 19) * 4f;
                var c = ShaftColor;
                c.a *= 1f + amount * Mathf.Sin(t * 2f * Mathf.PI / period + Hash(19, i) * 6.283f);
                shafts[i].SetColor("_Color", c);
            }
        }

        // ------------------------------------------------------------------ playing

        public void Show(bool on)
        {
            // Taken off the screen under the battle's wipe: a strike's bodies go with it (DungeonScreen.Engage removes
            // them once the battle is over), so the maze comes back without them.
            if (!on && struck != null) EndStrike(true);
            if (!on)
            {
                // The battle's own light starts dark, in the sea's own fog.
                Glims.Clear();
                ResetFog();
                Glance(null);
                glance = 0f;
            }
            shown = on;
            Camera.enabled = on;
            gameObject.SetActive(on);
        }

        // The 0.75 s under a strike's stamp (DungeonScreen.Engage), before the battle's wipe takes the picture. first:
        // the player struck first (Enter: 先制攻击, or 迎击 on one already chasing), taking its picture with the phone
        // and DeepSeek leaping in; ambushed: it jumped them from behind; neither: it ran into them, and they brace.
        // The noise bodies joining the battle lunge at the player. The camera punches in on the two, an ambush from
        // the camera's side cutting it round to the far side and shaking it.
        public void Strike(SeaNoise enemy, SystemPrompt.Battle.BattleOpening opening, bool byEnter, List<SeaNoise> joining)
        {
            var ambushed = opening == SystemPrompt.Battle.BattleOpening.Ambush;
            var first = byEnter && !ambushed;
            struck = enemy;
            struckToo.Clear();
            struckToo.AddRange(joining);
            strikeTime = 0f;
            shutter = first;
            var to = enemy.Position - PlayerPosition;
            to.y = 0f;
            turnFrom = PlayerYaw;
            turnTo = to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : PlayerYaw;
            turnSeconds = ambushed ? 0.2f : 0.1f;
            turnAfter = ambushed ? 0.08f : 0f; // knocked forward first
            Avatar.Strike(first, ambushed);
            Companion?.Strike(enemy.Position, first, ambushed);
            enemy.Struck(first, opening == SystemPrompt.Battle.BattleOpening.Preemptive, PlayerPosition);
            foreach (var o in joining) o.Struck(false, false, PlayerPosition);

            var settings = GameRoot.Instance != null ? GameRoot.Instance.Settings : null;
            var shaking = settings != null && settings.screenShake;
            var bearing = Mathf.DeltaAngle(CameraYaw, turnTo); // where it is from the camera's way, right positive
            // 画面震动 off: the kick in at 0.4, as the battle's punch.
            punchIn = (first ? 0.6f : 0.3f) * (shaking ? 1f : 0.4f);
            punchFov = (first ? -6f : -3f) * (shaking ? 1f : 0.4f);
            punchRoll = shaking ? -3f * Mathf.Sign(bearing) : 0f;
            punchShake = ambushed && shaking && !settings.reduceFlash;
            // In on the two of them, and on DeepSeek too when she is by them, so the push doesn't crowd her out.
            var her = Companion != null && (Companion.Root.position - PlayerPosition).sqrMagnitude < 9f;
            punchOn = (PlayerPosition + enemy.Position + (her ? Companion.Root.position : Vector3.zero)) / (her ? 3f : 2f) + Vector3.up * 1.2f;
            // Coming from the camera's side of the player it would fill the lens: the camera cuts round to the far side
            // and takes it over their shoulder, a little off the line between them so the player doesn't hide it, on
            // whichever side of the line keeps DeepSeek in the picture and off the party's cards.
            punchSwing = 0f;
            if (ambushed && Mathf.Abs(bearing) > 100f)
            {
                var side = Mathf.Sign(bearing);
                punchSwing = Mathf.DeltaAngle(CameraYaw, turnTo + 22f * side);
                if (Companion != null)
                {
                    var off = Crowded(punchSwing);
                    var other = Mathf.DeltaAngle(CameraYaw, turnTo - 22f * side);
                    if (off > 0f && Crowded(other) < off) punchSwing = other;
                }
            }
            UpdateCamera(0f, false); // there as the stamp lands
            // Where the punch settles, which DungeonScreen places the stamp from (ToScreen).
            punchedAt = Camera.transform.position;
            punchedTurn = Camera.transform.rotation;
            punchedFov = Camera.fieldOfView;
            Punch(1f, ref punchedAt, ref punchedTurn, ref punchedFov);
        }

        // How far DeepSeek's middle is from the part of the strike's first picture clear of the party's cards and the
        // screen's edges (0: inside), with the camera cut round by `swing`.
        float Crowded(float swing)
        {
            var keep = cameraDistance;
            punchSwing = swing;
            UpdateCamera(0f, false);
            cameraDistance = keep;
            var v = Camera.WorldToViewportPoint(Companion.Root.position + Vector3.up * 0.9f);
            if (v.z <= 0f) return 9f;
            return Mathf.Max(0f, 0.33f - v.x) + Mathf.Max(0f, v.x - 0.97f) + Mathf.Max(0f, 0.12f - v.y) + Mathf.Max(0f, v.y - 1f);
        }

        // Everyone out of the strike, the camera back behind the player. fought: the battle took over, and the bodies
        // are gone from sight; otherwise (the battle never came) they are back as they were.
        public void EndStrike(bool fought)
        {
            if (struck == null) return;
            foreach (var e in struckToo) Unstrike(e, fought);
            Unstrike(struck, fought);
            struck = null;
            struckToo.Clear();
            Avatar.Rest();
            Companion?.Rest(PlayerPosition, CameraYaw);
            playerRan = herRan = 9f; // her pace held through the strike isn't a stop
            UpdateCamera(0f, true);

            static void Unstrike(SeaNoise e, bool fought)
            {
                if (fought) e.Root.gameObject.SetActive(false);
                else e.Unstrike();
            }
        }

        void StepStrike(float dt)
        {
            var was = strikeTime;
            strikeTime += dt;
            PlayerYaw = Mathf.LerpAngle(turnFrom, turnTo, Ease.OutCubic(Mathf.Clamp01((strikeTime - turnAfter) / turnSeconds)));
            Avatar.Root.rotation = Quaternion.Euler(0f, PlayerYaw, 0f);
            if (shutter && was < SeaAvatar.ShutterAt && strikeTime >= SeaAvatar.ShutterAt && GameRoot.Instance != null)
                GameRoot.Instance.Audio.Sfx("shutter", 0.8f);
        }

        // A strike's punch over the follow pose, k of the way in: toward the middle of the two of them (punchOn) unless
        // the camera is already close, narrower, turned a little their way and tipped toward the noise body's side; an
        // ambush shakes it.
        void Punch(float k, ref Vector3 at, ref Quaternion turn, ref float fov)
        {
            var toward = punchOn - at;
            var nearer = at + toward.normalized * (punchIn * k * Mathf.Clamp01((cameraDistance - 2f) / 3f));
            if (Map.Free(nearer, 0.35f, true)) at = nearer;
            turn = Quaternion.Slerp(turn, Quaternion.LookRotation(punchOn - at), 0.3f * k) * Quaternion.Euler(0f, 0f, punchRoll * k);
            fov += punchFov * k;
            if (!punchShake) return;
            var s = 0.06f * Mathf.Exp(-12f * strikeTime) * Mathf.Sin(70f * strikeTime);
            at += turn * new Vector3(s, 0.7f * s, 0f);
        }

        public void Place(Vector2Int cell, float yaw)
        {
            PlayerPosition = Map.World(cell);
            PlayerYaw = yaw;
            CameraYaw = yaw;
            velocity = Vector3.zero;
            cameraDistance = CameraDistance;
            held = arriving = false;
            herGlance = 2.5f; // put down somewhere new, she looks round at the player first
            playerGlance = 5f;
            playerRan = herRan = 9f; // and not pulled up there from a run elsewhere
            Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0, yaw, 0));
            Avatar.Placed();
            Companion?.Place(PlayerPosition, yaw, Map);
            Reveal();
            UpdateCamera(0f, true);
            airSnap = true;
        }

        // move: x right, y forward relative to the camera; look: degrees to turn the camera this frame.
        public void Step(float dt, Vector2 move, bool sprint, float turnKeys, Vector2 lookDegrees)
        {
            CameraYaw += turnKeys * KeyTurnSpeed * dt + lookDegrees.x;
            pitch = Mathf.Clamp(pitch - lookDegrees.y, -12f, 42f);

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
            {
                PlayerYaw = Mathf.MoveTowardsAngle(PlayerYaw, Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg, 760f * dt);
                stillFor = 0f;
                facing = false;
            }
            else if ((stillFor += dt) > 1.5f && faceTo is Vector3 at)
            {
                // Stood still a while with it more than 70 degrees round from where they face: they turn to it.
                var to = at - PlayerPosition;
                var aim = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                var off = Mathf.Abs(Mathf.DeltaAngle(PlayerYaw, aim));
                facing = facing ? off > 8f : off > 70f;
                if (facing) PlayerYaw = Mathf.LerpAngle(PlayerYaw, aim, 1f - Mathf.Exp(-6f * dt));
            }
            Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0, PlayerYaw, 0));
            driven = true;
            held = false;
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
        }

        // <<mirror on>> at the white door (story: Prologue_Deep_Exit): the player steps up to it, the camera
        // looks over their shoulder, and the door's face turns into a mirror with the player's battle-coat
        // double in it. <<mirror off>> settles the reflection back to the player as they are.
        // Where the glass comes on the door's face (just off the slab, toward the maze) and where the player stands to it.
        // The glass stands just in front of the door's panels (and their outlines), so it covers them where it shows
        // and the door shows round it; the player stands left of its middle and DeepSeek right, a step from it: the
        // side she keeps to following them in (SeaCompanion's Beside, the camera's right), so she is not put across
        // in front of them when it comes on.
        const float MirrorWidth = 5.7f, MirrorHeight = 7.2f, MirrorFov = 46f, DoorWalkSpeed = 4.5f;
        static readonly Color MistColor = new Color(0.8f, 0.88f, 1f, 0.5f);
        static readonly Vector3 HerFacing = new Vector3(-0.25f, 0f, 1f); // a little in toward the player, as if to look at them in it

        void MirrorMarks(out Vector3 glass, out Vector3 stand, out Vector3 beside)
        {
            glass = door.position + new Vector3(0f, 0f, -0.42f);
            // Far enough out that the player, near and broad from behind, doesn't cover the double (at 0.75 m it hid half).
            stand = glass + new Vector3(-1f, 0f, -1.1f);
            beside = glass + new Vector3(0.9f, 0f, -1.2f);
        }

        // From behind the two, between them, at eye height: they stand at the sides of the picture, cut at the shins,
        // and their reflections, twice as far off, stand whole between them, over a third of the picture's height (a
        // camera over one shoulder put the double behind the player's head, and at a fifth of the height). Far enough
        // back that the glass's sides and the door round them are in the picture, so it reads as a mirror in the door,
        // not a window.
        void MirrorShot(Vector3 glass, out Vector3 at, out Quaternion look)
        {
            at = glass + new Vector3(0f, 1.65f, -4.1f);
            look = Quaternion.LookRotation(glass + new Vector3(0f, 1.3f, 0f) - at);
        }

        // <<mirror walk>> before the white door's lines (story: Prologue_Deep_Exit): the player goes up to the door at
        // a trot and DeepSeek to her place beside it, the two getting there together, the camera following and then
        // easing round behind them for the last few metres as the mist before the door thins away; light comes up on the
        // floor between them as they get there. Not a step of the maze (the meter stays where it is).
        // hurry: three times as fast from then on; cancel: the run is over.
        public async Awaitable MirrorWalk(Func<bool> hurry, Func<bool> cancel)
        {
            if (door == null)
            {
                Debug.LogError($"<<mirror walk>>：{Def.Id} 没有白门");
                return;
            }
            MirrorMarks(out var glass, out var stand, out var beside);
            var path = Route(PlayerPosition, stand);
            var her = Companion;
            var herPath = her != null ? Route(her.Root.position, beside) : null;
            var herLeg = 0;
            // Her pace has her get there when he does, within what still looks like the same trot.
            var herSpeed = her == null ? 0f : DoorWalkSpeed * Mathf.Clamp(Remaining(her.Root.position, herPath, 0) / Mathf.Max(Remaining(PlayerPosition, path, 0), 0.5f), 0.6f, 1.5f);
            MirrorShot(glass, out var shotAt, out var shotLook);
            const float blend = 0.8f * DoorWalkSpeed; // the camera's last 0.8 s, in metres
            Vector3 fromAt = default;
            Quaternion fromLook = default;
            var fromFov = CameraFov;
            // The walk's length at its pace and a little over: held short of a mark (Map.Slide) they are put at it
            // rather than the controls staying gone.
            var limit = Remaining(PlayerPosition, path, 0) / DoorWalkSpeed + 1.5f;
            var spent = 0f;
            walking = true;
            leading = her != null;
            mist.color = MistColor; // whole till the camera comes round, even on a walk taken again
            try
            {
                var leg = 0;
                while ((leg < path.Count || her != null && herLeg < herPath.Count) && !cancel())
                {
                    var fast = hurry() ? 3f : 1f;
                    pace = fast;
                    var dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.05f);
                    spent += dt * fast;
                    if (spent > limit) break;
                    var turnTo = 0f; // the door, once there
                    if (leg < path.Count)
                    {
                        var step = DoorWalkSpeed * fast * dt;
                        var to = path[leg] - PlayerPosition;
                        to.y = 0f;
                        var d = to.magnitude;
                        if (d <= step) leg++;
                        if (d > 1e-3f)
                        {
                            PlayerPosition = Map.Slide(PlayerPosition, d <= step ? to : to / d * step, SeaAvatar.Radius);
                            velocity = to / d * DoorWalkSpeed * fast;
                            // Along the way, and round to the door over the last 0.8 m rather than as they stop.
                            if (Remaining(PlayerPosition, path, leg) > 0.8f) turnTo = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        }
                    }
                    else velocity = Vector3.zero;
                    PlayerYaw = Mathf.MoveTowardsAngle(PlayerYaw, turnTo, 600f * fast * dt);
                    Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0, PlayerYaw, 0));
                    if (her != null && herLeg < herPath.Count) herLeg = Lead(her, herPath, herLeg, herSpeed * fast, dt);
                    Reveal();
                    var left = Remaining(PlayerPosition, path, leg);
                    if (left < blend)
                    {
                        if (!held)
                        {
                            held = true;
                            fromAt = Camera.transform.position;
                            fromLook = Camera.transform.rotation;
                            fromFov = Camera.fieldOfView;
                        }
                        var k = Ease.InOutCubic(1f - left / blend);
                        Camera.fieldOfView = Mathf.Lerp(fromFov, MirrorFov, k);
                        Camera.transform.SetPositionAndRotation(Vector3.Lerp(fromAt, shotAt, k), Quaternion.Slerp(fromLook, shotLook, k));
                        // The mist before the door goes with the camera coming round: lit by the door it whitened the
                        // lower half of the picture, and went in one frame when the mirror came on.
                        mist.color = MistColor.WithAlpha(MistColor.a * (1f - k));
                    }
                    await Awaitable.NextFrameAsync();
                }
            }
            finally
            {
                walking = false;
                leading = false;
                pace = 1f;
                velocity = Vector3.zero;
            }
            if (cancel()) return;
            if (new Vector2(PlayerPosition.x - stand.x, PlayerPosition.z - stand.z).magnitude > 0.05f)
            {
                PlayerPosition = stand;
                Avatar.Placed();
            }
            if (her != null && herLeg < herPath.Count) her.Stand(beside, HerFacing);
            PlayerYaw = 0f;
            Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.identity);
            mist.color = MistColor.WithAlpha(0f);
            held = true;
            Camera.fieldOfView = MirrorFov;
            Camera.transform.SetPositionAndRotation(shotAt, shotLook);
            // On the floor between the two, not the door's face: a door near white takes no more light without
            // burning out.
            Glims.Light(glass + new Vector3(0f, 0.2f, -1.2f), Def.Lighthouse * 0.25f, 5f, 0.7f, time);
        }

        // From `from` to `to`: straight if clear, else round by the middle of its own cell and of the one at `to`.
        List<Vector3> Route(Vector3 from, Vector3 to)
        {
            var route = new List<Vector3>();
            if (!Map.Clear(from, to))
            {
                route.Add(Map.World(Map.CellAt(from)));
                route.Add(Map.World(Map.CellAt(to)));
            }
            route.Add(to);
            return route;
        }

        // A frame of DeepSeek's walk along `route` (MirrorWalk): on past each corner a step short of it, so only the
        // last mark stands her there (SeaCompanion.WalkTo). The leg she is on after it.
        static int Lead(SeaCompanion her, List<Vector3> route, int leg, float speed, float dt)
        {
            if (leg < route.Count - 1)
            {
                var off = route[leg] - her.Root.position;
                off.y = 0f;
                if (off.magnitude <= Mathf.Max(0.35f, speed * dt + 0.06f)) leg++;
            }
            var last = leg == route.Count - 1;
            return her.WalkTo(route[leg], last ? HerFacing : Vector3.zero, dt, speed) ? leg + 1 : leg;
        }

        static float Remaining(Vector3 from, List<Vector3> path, int leg)
        {
            if (leg >= path.Count) return 0f;
            var left = Vector3.Distance(new Vector3(from.x, 0f, from.z), path[leg]);
            for (var i = leg + 1; i < path.Count; i++) left += Vector3.Distance(path[i - 1], path[i]);
            return left;
        }

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
            MirrorMarks(out var glass, out var stand, out var beside);
            // Already there after MirrorWalk; put there otherwise (a node changed, or reached some other way).
            if (new Vector2(PlayerPosition.x - stand.x, PlayerPosition.z - stand.z).magnitude > 0.3f)
            {
                PlayerPosition = stand;
                PlayerYaw = 0f;
                velocity = Vector3.zero;
                Avatar.Root.SetPositionAndRotation(stand, Quaternion.identity);
                Avatar.Placed();
            }
            // Walked there with the player (MirrorWalk); put there otherwise.
            if (Companion != null && new Vector2(Companion.Root.position.x - beside.x, Companion.Root.position.z - beside.z).magnitude > 0.3f)
                Companion.Stand(beside, HerFacing);
            held = true;
            Camera.fieldOfView = MirrorFov;
            MirrorShot(glass, out var at, out var look);
            Camera.transform.SetPositionAndRotation(at, look);
            if (mirror == null) mirror = SeaMirror.Create(transform, Camera, glass, Vector3.back, MirrorWidth, MirrorHeight, Avatar, gender);
            // The mist in front of the glass goes as its ripple starts (it would haze the reflection over).
            foreach (var t in underGlass) t.gameObject.SetActive(false);
            await mirror.Open();
        }

        // The controls taken away (a talk, a node firing mid-run): the avatar pulls up as it would with the stick let go,
        // within half a metre, instead of standing still in one frame.
        public void Idle(float dt)
        {
            if (!walking) Brake(dt);
            driven = true;
        }

        void Brake(float dt)
        {
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, Acceleration * dt);
            if (velocity.sqrMagnitude < 1e-6f)
            {
                velocity = Vector3.zero; // stood still, where it was put (Slide would nudge it off a wall it touches)
                return;
            }
            var before = PlayerPosition;
            PlayerPosition = Map.Slide(PlayerPosition, velocity * dt, SeaAvatar.Radius);
            if (dt > 0f) velocity = (PlayerPosition - before) / dt;
            Avatar.Root.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0, PlayerYaw, 0));
        }

        // The terminal used: the way is open at once. reveal: the walls are left standing for the story to show them
        // going (DungeonScreen.Reveal, then SettleFirewalls for any it didn't).
        public void OpenFirewall(bool reveal = false)
        {
            Map.FirewallOpen = true;
            if (!reveal) SettleFirewalls();
        }

        public void DissolveFirewall(Firewall wall) => wall.Open(time, Glims, wake, true);

        public void SettleFirewalls()
        {
            if (!Map.FirewallOpen) return;
            foreach (var f in firewalls) f.Open(time, Glims, wake);
        }

        // The gate's sockets all full (Puzzles.Solved, at once): the gate goes when the player is shown it.
        public void OpenGateVisual() => Puzzles.OpenGate(time, Glims, wake);

        // ------------------------------------------------------------------ leaving

        // At the goal, before the way out's cover (DungeonScreen.Goal), as the area's DungeonDef.Departure has it: the
        // lighthouse's light turned on the two, or a column of light up under their feet. hurry: three times as fast from
        // then on; cancel: the run is over.
        public async Awaitable Depart(Func<bool> hurry, Func<bool> cancel)
        {
            try
            {
                switch (Def.Departure)
                {
                    case Departure.Lamp: await DepartLamp(hurry, cancel); break;
                    case Departure.Column: await DepartColumn(hurry, cancel); break;
                }
            }
            finally
            {
                pace = 1f;
            }
        }

        // The camera drops low behind the two and opens out to look up past them at the lamp, and the lamp turns its
        // beams down onto them (the story's next scene: 灯塔的光忽然照向我们), lighting them up. The lamp is some 30 m
        // up, so the lens goes wide and as far back as there is room: the two from the hips up at the bottom of the
        // picture, the lamp at its top. Short of room, only the lamp turns.
        async Awaitable DepartLamp(Func<bool> hurry, Func<bool> cancel)
        {
            var lampAt = lampBeams.position;
            var player = PlayerPosition;
            var away = player - lampAt;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.back;
            Vector3? eye = null;
            foreach (var d in new[] { 10f, 7f, 5f })
            {
                var at = player + away * d + Vector3.up * 0.5f;
                if (!Map.Free(at, 0.5f, true) || !Map.Sight(at, player)) continue;
                eye = at;
                break;
            }
            var fromAt = Camera.transform.position;
            var fromLook = Camera.transform.rotation;
            var fromFov = Camera.fieldOfView;
            var toLook = fromLook;
            if (eye.HasValue)
            {
                // Square on to the lamp, raised until it sits 4 degrees under the top of the picture.
                var flat = lampAt - eye.Value;
                flat.y = 0f;
                var rise = Mathf.Atan2(lampAt.y - eye.Value.y, flat.magnitude) * Mathf.Rad2Deg;
                toLook = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(-(rise - (DepartFov / 2f - 4f)), 0f, 0f);
                held = true;
            }
            var from = lampBeams.localRotation;
            var onThem = (player + Vector3.up - lampAt).normalized;
            // The beams point BeamTilt down from the lamp's forward: forward raised by as much puts them on the two.
            var aim = Quaternion.LookRotation(onThem) * Quaternion.Euler(-Def.BeamTilt, 0f, 0f);
            var dip = Mathf.Asin(-onThem.y) * Mathf.Rad2Deg - Def.BeamTilt;
            var lit = false;
            for (var t = 0f; t < 2f && !cancel();)
            {
                pace = hurry() ? 3f : 1f;
                if (eye.HasValue)
                {
                    var k = Ease.InOutCubic(Mathf.Clamp01(t / 1.4f));
                    Camera.fieldOfView = Mathf.Lerp(fromFov, DepartFov, k);
                    Camera.transform.SetPositionAndRotation(Vector3.Lerp(fromAt, eye.Value, k), Quaternion.Slerp(fromLook, toLook, k));
                }
                var u = Ease.InOutCubic(Mathf.Clamp01((t - 0.4f) / 1f));
                lampAim = Quaternion.Slerp(from, aim, u);
                lampDip = dip * u;
                beamLook.SetColor("_Color", Def.Lighthouse * Mathf.Lerp(0.22f, 0.6f, u));
                if (!lit && t >= 1.4f)
                {
                    lit = true;
                    Glims.Light(player + Vector3.up, Def.Lighthouse * 0.6f, 14f, 0.9f, time);
                    Glims.Pulse(player, Def.Lighthouse * 0.6f, time);
                }
                await Awaitable.NextFrameAsync();
                t += UnityEngine.Time.unscaledDeltaTime * pace;
            }
        }

        // A column of light comes up between the two (the story: 脚下的光柱亮了起来), sparks going up it, the camera
        // lifting its eyes after it.
        async Awaitable DepartColumn(Func<bool> hurry, Func<bool> cancel)
        {
            var at = Companion != null ? (PlayerPosition + Companion.Root.position) / 2f : PlayerPosition;
            at.y = 0f;
            departure ??= new SeaColumn(transform, at, Def.Lighthouse, 2.8f);
            departure.Root.position = at;
            departure.Rise(time, 0.7f);
            Glims.Pulse(at, Def.Lighthouse * 0.7f, time);
            Glims.Light(at + Vector3.up, Def.Lighthouse * 0.6f, 10f, 1f, time);
            for (var i = 0; i < 30; i++)
            {
                var r = UnityEngine.Random.insideUnitCircle * 1.2f;
                wake.Spark(at + new Vector3(r.x, UnityEngine.Random.Range(0.05f, 0.6f), r.y), Vector3.up * UnityEngine.Random.Range(3f, 7f) + new Vector3(r.x, 0f, r.y) * 0.3f,
                    UnityEngine.Random.Range(0.08f, 0.18f), UnityEngine.Random.Range(0.8f, 1.4f), Color.Lerp(Color.white, Def.Lighthouse, UnityEngine.Random.value));
            }
            held = true;
            var look = Camera.transform.rotation;
            for (var t = 0f; t < 1.1f && !cancel();)
            {
                pace = hurry() ? 3f : 1f;
                var u = Ease.InOutCubic(Mathf.Clamp01((t - 0.2f) / 0.8f));
                Camera.transform.rotation = look * Quaternion.Euler(-10f * u, 0f, 0f);
                await Awaitable.NextFrameAsync();
                t += UnityEngine.Time.unscaledDeltaTime * pace;
            }
        }

        // The meter run out (story: Prologue_Deep_Timeout, 四周的机柜一座接一座地熄灭，海水开始往上倒流): over `seconds`
        // the fog thickens to black, the far racks going first, and bubbles stream up round the two. Restore ends it.
        public async Awaitable Blackout(float seconds, Func<bool> cancel)
        {
            var serial = ++fogSerial;
            var bubbles = 0f;
            for (var t = 0f; t < seconds && serial == fogSerial && !cancel(); t += UnityEngine.Time.unscaledDeltaTime)
            {
                var k = Ease.InCubic(t / seconds);
                fogDensity = Mathf.Lerp(FogDensity, 0.15f, k);
                fogColor = Color.Lerp(FogColor, Dark, k);
                for (bubbles += UnityEngine.Time.unscaledDeltaTime * 40f; bubbles >= 1f; bubbles--)
                {
                    var r = UnityEngine.Random.insideUnitCircle * 8f;
                    wake.Bubble(PlayerPosition + new Vector3(r.x, UnityEngine.Random.Range(0.1f, 1.5f), r.y), Vector3.up * UnityEngine.Random.Range(3f, 5f), UnityEngine.Random.Range(0.06f, 0.14f), UnityEngine.Random.Range(1.2f, 2f));
                }
                await Awaitable.NextFrameAsync();
            }
        }

        // Back in the light (Bounce: 再睁开眼，我们站在来时那片光里).
        public async Awaitable Restore(float seconds)
        {
            var serial = ++fogSerial;
            var fromDensity = fogDensity;
            var from = fogColor;
            await Anim.Tween(seconds, k =>
            {
                if (serial != fogSerial) return;
                fogDensity = Mathf.Lerp(fromDensity, FogDensity, k);
                fogColor = Color.Lerp(from, FogColor, k);
            }, Ease.OutCubic);
        }

        void ResetFog()
        {
            fogSerial++;
            fogDensity = FogDensity;
            fogColor = FogColor;
        }

        // ------------------------------------------------------------------ cutaways

        static readonly float[] ShotTurns = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        static readonly float[] ShotDistances = { 8f, 7f, 9f };

        // Somewhere to look at `focus` from for a cutaway: `height` up, round from the camera's heading (or from
        // `prefer`, the way the lens should face) as little as will do, with nothing tall in the way, not within
        // 2.5 m of either of the two nor looking them in the face (the models don't take a close-up), and the
        // player not standing in front of what it shows. False when there is nowhere.
        public bool FindShot(Vector3 focus, out Vector3 eye, out Quaternion look, Vector3? prefer = null, float height = 3.5f, float lookUp = 2f)
        {
            var from = prefer is Vector3 p && new Vector2(p.x, p.z).sqrMagnitude > 1e-4f ? Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg : CameraYaw;
            var tanV = Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad);
            var tanH = tanV * Camera.aspect;
            foreach (var turn in ShotTurns)
            foreach (var distance in ShotDistances)
            {
                var yaw = (from + turn) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var e = focus - dir * distance + Vector3.up * height;
                var cell = Map.CellAt(e);
                if (!Map.Inside(cell.x, cell.y) || !Map.Free(e, 0.5f, true) || !Map.Sight(e, focus)) continue;
                var l = Quaternion.LookRotation(focus + Vector3.up * lookUp - e);
                if (Faces(e, l, PlayerPosition, Avatar.Root.forward, tanH, tanV)) continue;
                if (Companion != null && Faces(e, l, Companion.Root.position, Companion.Root.forward, tanH, tanV)) continue;
                if (Across(PlayerPosition + Vector3.up, e, focus) < 1f) continue;
                eye = e;
                look = l;
                return true;
            }
            eye = default;
            look = default;
            return false;
        }

        // Too near someone, or near enough with them on the picture and the lens in front of their face.
        static bool Faces(Vector3 eye, Quaternion look, Vector3 at, Vector3 facing, float tanH, float tanV)
        {
            var flat = new Vector3(eye.x - at.x, 0f, eye.z - at.z);
            if (flat.magnitude < 2.5f) return true;
            if (flat.magnitude >= 6f) return false;
            var local = Quaternion.Inverse(look) * (at + Vector3.up * 1.4f - eye);
            if (local.z <= 0f || Mathf.Abs(local.x / local.z) > tanH * 0.8f || Mathf.Abs(local.y / local.z) > tanV * 0.8f) return false;
            facing.y = 0f;
            return Vector3.Dot(flat.normalized, facing.normalized) > 0.5f;
        }

        // How far `p` is from the segment a–b.
        static float Across(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector3.Distance(p, a + ab * t);
        }

        public void BeginCut()
        {
            Cutting = true;
            Camera.fieldOfView = CameraFov;
        }

        // A frame of a cutaway's shot, u of the way through it: the camera eases in 0.6 m, so the picture isn't dead.
        public void HoldShot(Vector3 eye, Quaternion look, Vector3 focus, float u)
        {
            var push = 1f - Mathf.Pow(1f - Mathf.Clamp01(u), 3f);
            Camera.transform.SetPositionAndRotation(eye + (focus - eye).normalized * 0.6f * push, look);
        }

        public void Hurry(bool on) => pace = on ? 3f : 1f;

        public void EndCut()
        {
            Cutting = false;
            pace = 1f;
            UpdateCamera(0f, true);
        }

        // Plainly on the player's screen now: within `metres`, nothing tall between, and clear of the picture's edges and
        // of the HUD's corners (the call-out and the map top right, the party bottom left). A gate at the top of the
        // picture past the racks counts as unseen: its opening would go by as a sliver.
        public bool InView(Vector3 at, float metres = 18f)
        {
            var eye = Camera.transform.position;
            if (Vector3.Distance(eye, at) > metres || !Map.Sight(eye, at)) return false;
            var v = Camera.WorldToViewportPoint(at);
            return v.z > 0f && v.x > 0.15f && v.x < 0.75f && v.y > 0.15f && v.y < 0.7f;
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

        public Vector3 ExitPosition => Map.World(Map.Find('G')[0]);

        // The light has run all the way to the goal: the way out comes up out of the seabed (DungeonScreen.LightFound,
        // in its cutaway).
        public void RiseExit()
        {
            if (decor.Exit != null && decor.Exit.gameObject.activeSelf) return;
            ShowGoal(true);
            decor.ExitColumn?.Rise(time);
            var at = ExitPosition;
            Glims.Pulse(at, Def.Lighthouse * 0.7f, time);
            Glims.Light(at + Vector3.up, Def.Lighthouse * 0.6f, 10f, 1.2f, time);
        }

        // A beacon rested at (DungeonScreen.RestAt): a ring out over the seabed from it, its light on what stands round
        // it, sparks going up from the lamp.
        public void LightBeacon(SeaProp beacon)
        {
            var lamp = beacon.Position + Vector3.up * SeaProp.LampHeight;
            Glims.Pulse(beacon.Position, SeaProp.Rested * 0.7f, time);
            Glims.Light(lamp, SeaProp.Rested * 0.8f, 6f, 0.9f, time);
            for (var i = 0; i < 20; i++)
            {
                var r = UnityEngine.Random.insideUnitCircle * 0.25f;
                wake.Spark(lamp + new Vector3(r.x, 0f, r.y), Vector3.up * UnityEngine.Random.Range(2f, 4f) + new Vector3(r.x, 0f, r.y) * 3f,
                    UnityEngine.Random.Range(0.08f, 0.14f), UnityEngine.Random.Range(0.8f, 1.3f), Color.Lerp(Color.white, SeaProp.Rested, UnityEngine.Random.value));
            }
        }

        // A chest opened (DungeonScreen.Use): gold sparks thrown up out of it, its light on what stands round it, and
        // the camera turned toward it until DungeonScreen lets go (Glance).
        public void OpenChest(SeaProp chest)
        {
            var mouth = chest.Position + Vector3.up * 0.7f;
            Glims.Light(mouth + Vector3.up * 0.4f, SeaProp.Gold * 0.6f, 4f, 0.8f, time);
            for (var i = 0; i < 18; i++)
            {
                var r = UnityEngine.Random.insideUnitCircle * 0.4f;
                wake.Spark(mouth + new Vector3(r.x, 0f, r.y), Vector3.up * UnityEngine.Random.Range(2.5f, 4.5f) + new Vector3(r.x, 0f, r.y) * 2f,
                    UnityEngine.Random.Range(0.07f, 0.13f), UnityEngine.Random.Range(0.6f, 1.1f), Color.Lerp(Color.white, SeaProp.Gold, 0.4f + 0.6f * UnityEngine.Random.value));
            }
            Glance(chest.Position + Vector3.up);
        }

        // The follow camera turns 60 % of the way to look at `at` over 0.35 s, staying where it is (in a dead end there is
        // no room to move it), and back over 0.25 s once given null.
        public void Glance(Vector3? at)
        {
            glancing = at.HasValue;
            if (at is Vector3 point) glanceAt = point;
        }

        public CacheBlock BlockInReach(out Vector2Int direction) => Puzzles.BlockInReach(PlayerPosition, out direction);

        // Props and noise bodies stand in the way of a block.
        public bool Push(CacheBlock block, Vector2Int direction) => Puzzles.Push(block, direction, c =>
            Props.Exists(p => p.Cell == c) || Enemies.Exists(e => e.Active && Map.CellAt(e.Position) == c), time);

        // Not back onto the player either.
        public bool Undo() => Puzzles.Undo(c =>
            Props.Exists(p => p.Cell == c) || Enemies.Exists(e => e.Active && Map.CellAt(e.Position) == c)
            || Vector3.Distance(Map.World(c), PlayerPosition) < CacheBlock.Size * 0.5f + 0.6f, time);

        public void ResetBlocks() => Puzzles.ResetBlocks(time);

        public LightPlate PlateInReach() => Puzzles.PlateInReach(PlayerPosition);

        // True the first time the light reaches the goal (the exit comes up once the light has run there, RiseExit).
        public bool Turn(LightPlate plate, int step = 1) => Puzzles.Turn(plate, time, step);

        public ZoneDef Zone => Def.ZoneAt(PlayerCell);

        // A 数据珍珠 the avatar has walked into.
        public SeaPearl PearlInReach() => Pearls.Find(p => !p.Taken && Vector3.Distance(p.Position, PlayerPosition) < SeaPearl.Reach);

        public void Take(SeaPearl pearl)
        {
            pearl.Take(time, Avatar.Root);
            Companion?.Wag();
        }

        // A taken pearl gone into the player: its light on them a moment, a ring out from their feet, sparks off them.
        void Absorb()
        {
            var chest = PlayerPosition + Vector3.up * 1.1f;
            Glims.Light(PlayerPosition + Vector3.up, SeaPearl.Nacre * 0.7f, 4f, 0.6f, time);
            Glims.Pulse(PlayerPosition, SeaPearl.Nacre * 0.6f, time);
            for (var i = 0; i < 14; i++)
                wake.Spark(chest + UnityEngine.Random.insideUnitSphere * 0.15f, UnityEngine.Random.insideUnitSphere * 2.5f + Vector3.up,
                    UnityEngine.Random.Range(0.06f, 0.12f), UnityEngine.Random.Range(0.5f, 0.9f), Color.Lerp(Color.white, SeaPearl.Nacre, UnityEngine.Random.value));
        }

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

        // The dive's fall carried on into the maze: the camera drops from high over its place behind the avatar,
        // turning a quarter of the way round it as it comes, and is there at t = 1.
        public void Arrive(float t)
        {
            arriving = t < 1f;
            UpdateCamera(0f, true);
            var focus = PlayerPosition + Vector3.up * CameraHeight;
            var k = 1f - t;
            k *= k * k;
            var at = focus + Quaternion.Euler(0f, 90f * k, 0f) * (Camera.transform.position - focus) + Vector3.up * 16f * k;
            Camera.transform.SetPositionAndRotation(at, Quaternion.LookRotation(focus - at + Vector3.up * 0.4f));
        }

        // Behind the avatar. Its focus eases after the avatar, a little ahead of where it runs, so starts, stops and
        // turns carry the picture rather than jerk it; a sprint draws it back and widens it. A rack behind pulls it in at
        // once (and lets it out at 6 m/s); pulled in, it rises over the right shoulder, levels its boom out and widens, so
        // the back of the head never fills the lens, and keeps the way ahead in the picture before the player's legs
        // (Frame). Below 6° of pitch it stays put and only looks up, so the player can raise their eyes to the
        // lighthouse's lamp without the camera dropping to the seabed. A strike punches in over all of it (Punch).
        // snap: there at once, nothing eased (a teleport, the dive's fall).
        void UpdateCamera(float dt, bool snap)
        {
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            var speed = flat.magnitude;
            var target = PlayerPosition + Vector3.up * CameraHeight;
            if (snap)
            {
                lead = leadVelocity = focusVelocity = Vector3.zero;
                sprinting = sprintingVelocity = crampedVelocity = 0f;
                focus = target;
            }
            else
            {
                var wantLead = speed > 0.1f ? flat * (0.45f * Mathf.Clamp01(speed / RunSpeed) / speed) : Vector3.zero;
                lead = Vector3.SmoothDamp(lead, wantLead, ref leadVelocity, 0.3f, Mathf.Infinity, dt);
                target += lead;
                focus.x = Mathf.SmoothDamp(focus.x, target.x, ref focusVelocity.x, 0.1f, Mathf.Infinity, dt);
                focus.z = Mathf.SmoothDamp(focus.z, target.z, ref focusVelocity.z, 0.1f, Mathf.Infinity, dt);
                focus.y = target.y; // the seabed is flat
                var behind = new Vector3(focus.x - target.x, 0f, focus.z - target.z);
                if (behind.sqrMagnitude > 0.49f) focus = target + behind.normalized * 0.7f;
                var fast = speed > 7.2f ? 1f : 0f;
                sprinting = Mathf.SmoothDamp(sprinting, fast, ref sprintingVelocity, fast > sprinting ? 0.35f : 0.5f, Mathf.Infinity, dt);
            }
            var yaw = (CameraYaw + (struck != null ? punchSwing : 0f)) * Mathf.Deg2Rad;
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            var longest = CameraDistance + 0.5f * sprinting;
            // How cramped it is: from what the rack behind would leave at the player's own pitch.
            var free = Boom(focus, Back(yaw, Mathf.Max(pitch, 6f)), longest);
            var want = Mathf.Clamp01((4f - free) / 3.2f);
            cramped = snap ? want : Mathf.SmoothDamp(cramped, want, ref crampedVelocity, want > cramped ? 0.08f : 0.5f, Mathf.Infinity, dt);
            var from = focus + (Vector3.up * 0.35f + right * 0.4f) * cramped;
            // Its boom levelled out the more it's pulled in: with the top edge held up (Frame), the lower the lens the more
            // of the legs it can show, and the rise alone keeps it over the shoulder.
            var back = Back(yaw, Mathf.Max(Mathf.Max(pitch, 6f) - 20f * cramped, 0f));
            free = Boom(from, back, longest);
            cameraDistance = free < cameraDistance || snap ? free : Mathf.MoveTowards(cameraDistance, free, dt * 6f);
            glance = snap ? (glancing ? 1f : 0f) : Mathf.MoveTowards(glance, glancing ? 1f : 0f, dt / (glancing ? 0.35f : 0.25f));
            if (held || Cutting) return;
            var at = from + back * cameraDistance;
            // Wider pulled in: with the top edge held up (Frame), what it has left for the legs is the field of view.
            var fov = CameraFov + 4f * sprinting + 12f * cramped;
            var half = fov * 0.5f;
            var aim = Frame(Quaternion.LookRotation(from - at + Vector3.up * 0.4f), at, half, sprinting);
            // Looking up stops where the avatar's head would leave the bottom of the picture, so the player never
            // steers someone out of sight.
            var head = Quaternion.Inverse(aim) * (PlayerPosition + Vector3.up * 1.45f - at);
            var below = Mathf.Atan2(-head.y, head.z) * Mathf.Rad2Deg;
            var tilt = Mathf.Min(6f - pitch, half - 2f - below);
            var turn = aim * Quaternion.Euler(-Mathf.Max(0f, tilt), 0f, 0f);
            if (struck != null) Punch(Ease.OutCubic(Mathf.Clamp01(strikeTime / 0.25f)), ref at, ref turn, ref fov);
            if (glance > 0f) turn = Quaternion.Slerp(turn, Quaternion.LookRotation(glanceAt - at), 0.6f * Ease.InOutCubic(glance));
            Camera.fieldOfView = fov;
            Camera.transform.SetPositionAndRotation(at, turn);
        }

        // Tipped from `aim` for what it has to show, in order. The way ahead first: the top edge no lower over the horizon
        // than the open camera has it at this pitch (CameraDistance back), less 4° (about 5° at the default 20°), so what
        // stands ahead at eye level, a gate, a noise body, the lighthouse, stays in the picture. Then the player: the face
        // 2° and the crown 3° inside the edges, and tipped down as far as that leaves until the feet are 3° inside the
        // bottom; where it can't, the legs are cut, at the shins or knees, at the hips only with the boom at its shortest.
        // In the open nothing binds. Turned about its own x only, so each of these is an exact angle in the picture.
        Quaternion Frame(Quaternion aim, Vector3 at, float half, float sprint)
        {
            var p = Mathf.Max(pitch, 6f) * Mathf.Deg2Rad;
            var top = (CameraFov + 4f * sprint) * 0.5f - Mathf.Atan2(CameraDistance * Mathf.Sin(p) - 0.4f, CameraDistance * Mathf.Cos(p)) * Mathf.Rad2Deg - 4f;
            var dip = Mathf.Asin(-(aim * Vector3.forward).y) * Mathf.Rad2Deg;
            var feet = Below(PlayerPosition) - (half - 3f);                  // further down to have them in
            var face = Below(PlayerPosition + Vector3.up * 1.45f) - (half - 2f);
            var crown = Below(PlayerPosition + Vector3.up * 1.8f) + half - 3f; // further down before it leaves the top
            var down = Mathf.Min(Mathf.Max(0f, feet), crown, half - top - dip);
            return aim * Quaternion.Euler(Mathf.Max(down, face), 0f, 0f);

            // Degrees under the middle of the picture.
            float Below(Vector3 point)
            {
                var v = Quaternion.Inverse(aim) * (point - at);
                return Mathf.Atan2(-v.y, v.z) * Mathf.Rad2Deg;
            }
        }

        // The way from the focus back to the camera, at yaw (radians) and pitch (degrees).
        static Vector3 Back(float yaw, float pitch)
        {
            var p = pitch * Mathf.Deg2Rad;
            return -new Vector3(Mathf.Sin(yaw) * Mathf.Cos(p), -Mathf.Sin(p), Mathf.Cos(yaw) * Mathf.Cos(p));
        }

        // How far back from `from` the camera can go before a rack's in the way (not over one: they stop by 9 m). The
        // edge is narrowed to about a centimetre between the 0.2 m steps: pulled in close, a whole step would jump the
        // picture by a fifth as the player backs up to a rack or turns beside one.
        float Boom(Vector3 from, Vector3 back, float longest)
        {
            for (var d = 0.6f; d <= longest; d += 0.2f)
            {
                if (Clear(d)) continue;
                var lo = d - 0.2f;
                var hi = d;
                for (var i = 0; i < 4; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (Clear(mid)) lo = mid;
                    else hi = mid;
                }
                return Mathf.Max(0.8f, hi - 0.3f);
            }
            return longest;

            bool Clear(float along)
            {
                var at = from + back * along;
                return Map.Free(at, 0.35f, true) || at.y >= 9f;
            }
        }

        // Screen point of a world point (y from the top), or false behind the camera. punched: as the camera will see it
        // once a strike's punch has come in (Strike), for what stays on screen through it.
        public bool ToScreen(Vector3 world, out Vector2 screen, bool punched = false)
        {
            Vector3 s;
            if (punched && struck != null)
            {
                var t = Camera.transform;
                var at = t.position;
                var turn = t.rotation;
                var fov = Camera.fieldOfView;
                t.SetPositionAndRotation(punchedAt, punchedTurn);
                Camera.fieldOfView = punchedFov;
                s = Camera.WorldToScreenPoint(world);
                t.SetPositionAndRotation(at, turn);
                Camera.fieldOfView = fov;
            }
            else s = Camera.WorldToScreenPoint(world);
            screen = new Vector2(s.x, Screen.height - s.y);
            return s.z > 0.2f;
        }

        void Update()
        {
            if (!shown) return;
            time += UnityEngine.Time.unscaledDeltaTime * pace;
            if (Zone is ZoneDef zone && zone != groundZone)
            {
                groundZone = zone;
                groundTo = Ground(zone);
            }
            ground = groundSnap ? groundTo : Color.Lerp(ground, groundTo, 1f - Mathf.Exp(-1.5f * UnityEngine.Time.unscaledDeltaTime));
            groundSnap = false;
            UpdateAir();
            ApplyGlobals();
            lights?.Show(PlayerPosition);
            // The avatar every frame, whoever has the controls: with none this frame (a wipe or a menu over the maze) it
            // pulls up as in a talk, rather than freezing mid-stride.
            var dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.05f);
            if (!driven) Brake(dt);
            driven = false;
            if (struck != null) StepStrike(dt);
            Looks(dt);
            var speed = velocity.magnitude;
            Avatar.Animate(dt, speed);
            wake.Sprint(PlayerPosition + Vector3.up, speed > 0.01f ? -velocity / speed : Vector3.zero, speed, dt);
            Pulled(ref playerRan, speed, PlayerPosition, dt);
            if (!arriving) UpdateCamera(dt, false);
            var t = time;
            motes.position = new Vector3(PlayerPosition.x, 0f, PlayerPosition.z);
            // Tilted down to the seabed they turn at half the speed, so a place they pass over lights up for over a
            // second every 15 s: the lamp going on and off, seen from anywhere in the shallows.
            lampBeams.localRotation = lampAim ?? Quaternion.Euler(0, t * (Def.BeamTilt > 0f ? 12f : 24f), 0);
            LightBeams(t);
            BreatheShafts(t);
            shaftPool?.Update(Camera, t, SeaAtmos.Current.Surface);
            foreach (var p in Props) p.Animate(t, Camera);
            foreach (var p in Pearls)
                if (p.Animate(t, Camera)) Absorb();
            foreach (var e in Enemies) e.Animate(t, UnityEngine.Time.unscaledDeltaTime);
            if (!leading) Companion?.Follow(PlayerPosition, velocity, Camera, UnityEngine.Time.unscaledDeltaTime);
            if (Companion != null) Pulled(ref herRan, Companion.Speed, Companion.Root.position, dt);
            if (decor.Message is Vector3 message) decor.Veil(InFrontOf(message), dt);
            decor.Animate(t);
            decor.ExitColumn?.Animate(t);
            departure?.Animate(t);
            scene?.Animate(t);
            Puzzles.Animate(t);
            foreach (var f in firewalls) f.Animate(t);
            Glims.Shine(t);
        }

        // Someone stands in front of what floats at `at` (the sunken phone's message, SeaDecor.Veil) from the camera: the
        // two's heads, or a noise body with what shows over it, nearer the lens and within 3.5 m of it on the picture.
        bool InFrontOf(Vector3 at)
        {
            var eye = Camera.transform.position;
            var to = at - eye;
            var reach = Mathf.Atan2(3.5f, to.magnitude) * Mathf.Rad2Deg;
            if (Before(eye, to, reach, PlayerPosition + Vector3.up * 1.5f)) return true;
            if (Companion != null && Before(eye, to, reach, Companion.Root.position + Vector3.up * 1.5f)) return true;
            foreach (var e in Enemies)
                if (e.Active && e.Root.gameObject.activeSelf && Before(eye, to, reach, e.Position + Vector3.up * 2f)) return true;
            return false;
        }

        static bool Before(Vector3 eye, Vector3 to, float reach, Vector3 p)
        {
            var d = p - eye;
            return d.sqrMagnitude < to.sqrMagnitude && Vector3.Angle(d, to) < reach;
        }

        // Pulled up out of a run (over 5 m/s) to near standing within half a second: silt kicked out round the feet.
        void Pulled(ref float sinceRun, float speed, Vector3 at, float dt)
        {
            if (speed > 5f) sinceRun = 0f;
            else if (speed < 1f && sinceRun < 0.5f)
            {
                sinceRun = 9f;
                wake.Stop(at);
            }
            else sinceRun += dt;
        }

        // Where the two look this frame (SeaFigure.LookAt), the first that holds: a noise body after them in sight within
        // 14 m, or the one struck, snapped round to; whoever has the line in a talk (she looks at the player either way);
        // what the Enter prompt is over; a 数据珍珠 ahead within 7 m (9 m for her, a beat early, as a hint); now and
        // then, both standing, each other for 1.2 s.
        void Looks(float dt)
        {
            var threat = struck;
            var nearest = 14f;
            if (threat == null)
                foreach (var e in Enemies)
                {
                    if (!e.Active || e.Engaged || !e.Root.gameObject.activeSelf || e.State != NoiseState.Look && e.State != NoiseState.Chase) continue;
                    var d = Vector3.Distance(e.Position, PlayerPosition);
                    if (d >= nearest || !Map.Sight(e.Position, PlayerPosition)) continue;
                    threat = e;
                    nearest = d;
                }
            Vector3? threatAt = threat != null ? threat.Position + Vector3.up * 1.2f : null;
            faceTo = threatAt ?? Interest;

            var idle = velocity.sqrMagnitude < 0.09f && (Companion == null || Companion.Speed < 0.3f);
            var hisGlance = Glance(ref playerGlance, idle, dt);
            var herGlanceNow = Glance(ref herGlance, idle, dt);
            Vector3? his = threatAt ?? (DeepSeekSpeaks && Companion != null ? Companion.Face : Interest ?? Pearl(PlayerPosition, Avatar.Root.forward, 7f));
            if (his == null && hisGlance && Companion != null) his = Companion.Face;
            Avatar.LookAt(his, threatAt.HasValue);
            if (Companion == null) return;
            Vector3? hers = threatAt ?? (PlayerSpeaks || DeepSeekSpeaks ? Avatar.Face : Interest ?? Pearl(Companion.Root.position, Companion.Root.forward, 9f));
            if (hers == null && herGlanceNow) hers = Avatar.Face;
            Companion.LookAt(hers, threatAt.HasValue);
        }

        // Counts down to a glance while both stand; true for its 1.2 s, then 6 to 9 s to the next.
        static bool Glance(ref float left, bool idle, float dt)
        {
            if (!idle) left = Mathf.Max(left, 1.5f);
            else left -= dt;
            if (left < -1.2f) left = UnityEngine.Random.Range(6f, 9f);
            return idle && left < 0f;
        }

        // The nearest untaken 数据珍珠 within metres of `from` and 60 degrees of `ahead`, where it floats.
        Vector3? Pearl(Vector3 from, Vector3 ahead, float metres)
        {
            Vector3? best = null;
            var bestD = metres;
            foreach (var p in Pearls)
            {
                if (p.Taken) continue;
                var to = p.Position - from;
                to.y = 0f;
                var d = to.magnitude;
                if (d >= bestD || Vector3.Angle(ahead, to) > 60f) continue;
                best = p.Position + Vector3.up * 0.95f;
                bestD = d;
            }
            return best;
        }

        // The sky dome round the camera wherever it ended up this frame (the mirror's shot included).
        void LateUpdate()
        {
            if (shown) sky.position = Camera.transform.position;
        }

        // The seabed's glow round the player breathes a little (not with 减少背景动态), opens out at a sprint and turns a
        // steady shade warmer while anything is after them.
        void ApplyGlobals()
        {
            var settings = GameRoot.Instance != null ? GameRoot.Instance.Settings : null;
            var breathe = settings == null || settings.backgroundMotion ? 0.3f * Mathf.Sin(time * 1.3f) : 0f;
            var sprint = Mathf.InverseLerp(RunSpeed, SprintSpeed, velocity.magnitude);
            chaseTint = Mathf.MoveTowards(chaseTint, Chased() ? 1f : 0f, UnityEngine.Time.unscaledDeltaTime / 0.4f);
            ApplySea(time, PlayerPosition, 6.5f + breathe + sprint, true, Color.Lerp(GlowColor, ChaseColor, 0.35f * chaseTint), fogDensity, fogColor);
            Camera.backgroundColor = fogColor;
        }

        static readonly Color GlowColor = new Color(0.3f, 0.7f, 1f), ChaseColor = new Color(0.95f, 0.38f, 0.32f);

        bool Chased()
        {
            foreach (var e in Enemies)
                if (e.Active && e.State == NoiseState.Chase) return true;
            return false;
        }

        static Color Ground(ZoneDef zone) => SeaDecor.For(zone?.Style ?? ZoneStyle.Channel).Floor * 0.35f;

        // ------------------------------------------------------------------ the water's air (SeaAtmos)

        SeaAir air; // eased toward the zone's, before the hall's surge
        static readonly Color SurgeFog = Palette.Hex("#3A6FB0"), SurgeSurface = Palette.Hex("#B8DCFF");

        // The zone's air (SeaAtmos.Current, which ApplySea and the lenses take and a battle keeps), eased across from the
        // last, and the white door's hall surging bright the first time the player walks in (海水忽然变得很亮): up in
        // 0.4 s and back down to the hall's own over 2.5 s; with 减弱闪光 half as far and over 4 s.
        void UpdateAir()
        {
            var dt = UnityEngine.Time.unscaledDeltaTime;
            if (Zone is ZoneDef zone && zone != airZone)
            {
                if (zone.Style == ZoneStyle.WhiteHall && !surged)
                {
                    surged = true;
                    if (!airSnap) surge = 0f; // walked in, not put down there
                }
                airZone = zone;
            }
            airTo = SeaAtmos.For(Def, airZone);
            air = airSnap ? airTo : SeaAir.Lerp(air, airTo, 1f - Mathf.Exp(-1.6f * dt));
            airSnap = false;
            var shown = air;
            if (surge >= 0f)
            {
                var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
                var length = calm ? 4f : 2.5f;
                surge += dt;
                var k = Mathf.SmoothStep(0f, 1f, surge / 0.4f) * (1f - Mathf.SmoothStep(0f, 1f, (surge - 0.4f) / (length - 0.4f))) * (calm ? 0.5f : 1f);
                shown.Fog = Color.Lerp(shown.Fog, SurgeFog, k);
                shown.Surface = Color.Lerp(shown.Surface, SurgeSurface, k);
                if (surge >= length) surge = -1f;
            }
            SeaAtmos.Current = shown;
        }

        // How far an event's fog (Blackout, Restore) has gone from the plain sea's toward black, 0 to 1.
        static float Darkness(Color fog)
        {
            var way = (Vector4)(Dark - FogColor);
            return Mathf.Clamp01(Vector4.Dot((Vector4)(fog - FogColor), way) / Mathf.Max(way.sqrMagnitude, 1e-6f));
        }

        // The latent-space shaders' globals (Resources/Shaders/SeaCommon.cginc), here and on the battle field:
        // the seabed lights up within glowRadius of glowAt. In the maze (fade) the scenery thins out near the lens and
        // between it and glowAt's chest; never in a battle, whose effects come right up to the lens.
        public static void ApplySea(float time, Vector3 glowAt, float glowRadius, bool fade = false, Color? glow = null,
            float fogDensity = FogDensity, Color? fog = null)
        {
            Shader.SetGlobalVector("_SeaFade", fade ? new Vector4(1f, 1f, 0.9f, 2.8f) : Vector4.zero);
            var chest = glowAt + Vector3.up * 1.2f;
            Shader.SetGlobalVector("_SeaFocus", new Vector4(chest.x, chest.y, chest.z, 1.2f));
            // The zone's air (SeaAtmos.Current: the maze's, or the zone a battle was met in), under an event's black fog
            // (Blackout passes its density and colour, on their way from the plain sea's toward black): as far toward
            // black as that has gone, and as much thicker.
            var air = SeaAtmos.Current;
            Shader.SetGlobalColor("_SeaFogColor", Color.Lerp(air.Fog, Dark, fog.HasValue ? Darkness(fog.Value) : 0f));
            Shader.SetGlobalColor("_SeaSurfaceColor", air.Surface);
            Shader.SetGlobalFloat("_SeaFogDensity", air.Density + (fogDensity - FogDensity));
            Shader.SetGlobalVector("_SeaAir", new Vector4(air.Lift, air.Mist, air.Carry, 0f));
            Shader.SetGlobalVector("_SeaAbsorb", air.Absorb);
            // Its seabed (Seabed.shader): the maze's, or the battle ring's under the same water.
            Shader.SetGlobalVector("_SeaFloor", fade ? air.Floor : SeaAtmos.RingFloor(air));
            var wet = fade ? air.Wet : SeaAtmos.RingWet(air);
            // The rift's cracks breathe half as much under 减弱闪光.
            var still = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
            Shader.SetGlobalVector("_SeaFloorSpec", new Vector4(wet.x, wet.y, wet.z, wet.w * (still ? 0.5f : 1f)));
            SeaLights.Clear(); // none but what the maze shows after this (SeaLights.Show)
            Shader.SetGlobalColor("_SeaGroundColor", ground);
            Shader.SetGlobalVector("_SeaLightDir", new Vector4(0.35f, 1f, 0.25f, 0f));
            Shader.SetGlobalFloat("_SeaTime", time);
            Shader.SetGlobalFloat("_SeaCausticStrength", caustics);
            // 减弱闪光: the racks' LEDs blink slower and dip less, and their packets are dimmer.
            var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;
            Shader.SetGlobalVector("_SeaBlink", calm ? new Vector4(0.5f, 0.6f, 0.6f, 0f) : new Vector4(1f, 0.25f, 1.2f, 0f));
            Shader.SetGlobalVector("_SeaGlowPos", new Vector4(glowAt.x, glowAt.y, glowAt.z, glowRadius));
            Shader.SetGlobalColor("_SeaGlowColor", glow ?? GlowColor);
            // The maze's ring over the seabed (SeaGlims.Pulse) is out unless it is running this frame.
            Shader.SetGlobalVector("_SeaPulse", SeaGlims.Off);
            Shader.SetGlobalColor("_SeaPulseColor", Color.clear);
            Shader.SetGlobalColor("_SeaBeamColor", Color.clear); // the lighthouse's beams light nothing unless the maze turns them on (LightBeams)
            Shader.SetGlobalFloat("_SeaInk", 0f); // colour, unless a battle's 集成 inks the frame after this (BattleStage.Update)
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
