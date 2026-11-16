using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // A noise body walking the maze. It stands on its digit in the map and walks to each waypoint letter in
    // turn and back again (or round and round); a dozing one stays put, facing one way. Running into it, or
    // it into the player, starts its battle.
    public sealed class EnemySpawn
    {
        public char Mark;
        public string Kind;      // "clickbait" / "repost", as in the battles
        public string Route = ""; // waypoint letters
        public bool Loop;        // from the last waypoint straight on to its own cell, not back the same way
        public string Battle;    // Battles id
        public bool Dozing;
        public Vector2Int Facing = Vector2Int.down; // a dozing one's direction in map cells; rows grow southward, so down is north
    }

    public sealed class ChestDef
    {
        public string Item;     // GameData.Items id, or null for tokens
        public int Count;
        public int Tokens;
        public float Delay;     // 延时补丁: maintenance progress taken back
        public string Label;    // what the notice says was inside
    }

    // How a part of an area looks (Dungeon/SeaDecor.cs): the light on the seabed, what the walls and the
    // low walls are made of, the kelp, and what lies around.
    public enum ZoneStyle { Channel, Reef, Coral, Graveyard, Ridge, Canyon, Plaza, Rift, Servers, RecycleBin, Context, WhiteHall }

    // How the two leave an area at its goal (DungeonWorld.Depart): the lighthouse's light turned on them from the lamp
    // over them (the shallows), or a column of light coming up under their feet (the deep).
    public enum Departure { None, Lamp, Column }

    // A named part of an area. The first zone whose rectangle holds a cell owns it; entering one shows its
    // name, and the first time its story node (if any) runs.
    public sealed class ZoneDef
    {
        public string Name;     // 表情包珊瑚林
        public string Tag;      // EMOJI CORAL
        public RectInt Cells;   // map cells, x right and y down as in the map rows
        public ZoneStyle Style;
        public string Node;
        public SeaAir? Air;     // its water, where it differs from the area's (SeaAtmos)
    }

    // One latent-space area (design/systems.md 潜空间探索). The map is read top row first; north is up.
    //   #  rack wall          =  low wall (stops feet, not eyes or light)   %  kelp (just grows there)
    //   .  seabed             S  start                G  goal
    //   L  lighthouse         D  sealed door          B  beacon
    //   F  fragment (story)   C  chest                T  maintenance terminal
    //   X  firewall (all of them open when the terminal is used)
    //   K  cache block (pushed a cell at a time)      O  socket for a cache block
    //   Y  gate (opens once every socket holds a block)                  R  console putting the blocks back
    //   M  light plate (turns 45° at a time)          Q  where the lighthouse's light comes down
    //   1-9  noise body       a-z  patrol waypoint
    // Fragments, chests, plates and the like are matched to their list entries in reading order.
    public sealed class DungeonDef
    {
        public string Id;
        public string Area;       // 缓存海
        public string Layer;      // 浅层
        public string LayerTag;   // SHALLOWS
        public string Objective;
        public string ObjectiveAfterTerminal;
        public string[] Map;
        public ZoneDef[] Zones = new ZoneDef[0];
        public bool Meter;        // 维护进度 runs (100% ends the night)
        public string Music = "sea"; // Resources/Audio/BGM
        public float MeterStart;
        public float ChaseSpeed = 3.8f; // the noise bodies' speed after the player, metres a second (SeaNoise): outrun at a run (4.6)
        public Color Lighthouse;  // the lamp's colour: cold white while the shadow is there
        public Vector2 LighthouseCell; // may lie outside the map
        // Degrees the lighthouse's two beams point down from level: tilted, they come down to the seabed and sweep
        // over it and the racks (SeaCommon.cginc SeaBeam); 0, level over everything, lighting nothing.
        public float BeamTilt;
        public Departure Departure;
        // Near the surface (the shallows): the sea's lost things drift thick at the height of the racks' tops and
        // shafts of light come down round the lighthouse (DungeonWorld.BuildAmbience).
        public bool NearSurface;
        public Vector2[] Vents = new Vector2[0]; // cells (fractions toward a wall) a column of bubbles rises from
        public float Caustics = 1f; // how much of the surface's light net reaches the seabed here (SeaCommon.cginc SeaCaustic)
        public SeaAir Air = SeaAtmos.None; // its water: fog, mist, the lens's grade (SeaAtmos); a zone may have its own
        // The area's scenery modelled in Blender (assets/scenes/<id>/, Dungeon/SeaScene.cs): Resources path of
        // its FBX, with _outer and _drift beside it. Null: the racks and the zones' things are built in code.
        public string Scene;
        // Light plates in reading order, as they start: | / - \ (each Enter turns one a step on). With
        // plates the goal stays dark until the light from Q (going north) reaches it.
        public string Plates = "";

        public ZoneDef ZoneAt(Vector2Int cell)
        {
            foreach (var z in Zones)
                if (z.Cells.Contains(cell)) return z;
            return null;
        }

        // Yarn nodes run by the dungeon (story/00-prologue.yarn); any may be null. FirstBattleNode runs before
        // the area's first battle and FirstWinNode after it; GateNode when the cache blocks open their gate,
        // StuckNode the first time one is pushed into a corner it can't come out of; LightNode when the light
        // first reaches the goal.
        public string StartNode, SpottedNode, FirewallNode, TerminalNode, BeaconNode;
        public string FirstBattleNode, FirstWinNode, GateNode, StuckNode, LightNode;
        public string AmbushHintNode, WarningNode, GoalNode, TimeoutNode;
        public string SodaHintNode; // DeepSeek on the 冰镇汽水, the first time a patrolling noise body is near after the first battle
        public string[] FragmentNodes = new string[0];
        public string[] FragmentTitles = new string[0];
        public ChestDef[] Chests = new ChestDef[0];
        public EnemySpawn[] Enemies = new EnemySpawn[0];
        // 数据珍珠 (SeaPearl): floor cells off the main ways, kept apart from the map so the modelled scenes
        // built from it stay as they are; all of an area's together give PearlReward. PearlNode runs at the
        // first one taken, PearlsNode at the last.
        public Vector2Int[] Pearls = new Vector2Int[0];
        public ChestDef PearlReward;
        public string PearlNode, PearlsNode;
    }

    public static class Dungeons
    {
        public const float Cell = 4f;

        static readonly Dictionary<string, DungeonDef> All = new Dictionary<string, DungeonDef>
        {
            // 场景4：维护时刻。DeepSeek walks along but can't fight yet: the player fights alone with the
            // phone's screenshot, starting with the noise body dozing at the mouth of the reef. Around the
            // lighthouse the long way (west through the coral, north through the graveyard, behind the
            // lighthouse, down the canyon) to the maintenance room, where two cache blocks pushed into their
            // sockets open the gate to the terminal; the terminal opens both firewalls, and the east one lets
            // out into the main channel, the lighthouse straight ahead.
            ["cache_sea_shallows"] = new DungeonDef
            {
                Id = "cache_sea_shallows", Area = "缓存海", Layer = "浅层", LayerTag = "SHALLOWS",
                Objective = "和 DeepSeek 一起前往灯塔",
                ObjectiveAfterTerminal = "防火墙已解除，前往灯塔",
                Map = new[]
                {
                    "#############################",
                    "#.............B............##",
                    "#.........#=======####.###.##",
                    "#F=.=.=.=.#..LLL..####.###4##",
                    "#.........#..LLL..####.##%.##",
                    "#.=.=#=.=.#.......####.###.##",
                    "#3.....f..#...G...####...#.##",
                    "#.=#=.=.=.#.......####..F#.##",
                    "#.......F.#.......#######%.##",
                    "#.........#===X===########g##",
                    "####.######......%########.##",
                    "#2......e.#......%#.......B##",
                    "#..##%.#..#..===..X.K.==.K.##",
                    "#%.#.%.##.#..===..#........##",
                    "#%........#..===..#.=KOOO..##",
                    "#....B.....%......#R.......##",
                    "#.....##..#%..1...#####Y#####",
                    "#.##..#...#.......####.T.####",
                    "#..#%....#####.##############",
                    "#...%....####...#############",
                    "#....##.%.###.S.#############",
                    "#.......%C###################",
                    "#############################",
                },
                Zones = new[]
                {
                    new ZoneDef { Name = "气泡礁", Tag = "BUBBLE REEF", Cells = new RectInt(12, 18, 5, 5), Style = ZoneStyle.Reef,
                        Air = SeaAtmos.Shallows.With(fog: "#062026") },
                    new ZoneDef { Name = "主航道", Tag = "MAIN CHANNEL", Cells = new RectInt(10, 10, 8, 8), Style = ZoneStyle.Channel },
                    new ZoneDef { Name = "灯塔广场", Tag = "LIGHTHOUSE", Cells = new RectInt(10, 2, 9, 8), Style = ZoneStyle.Plaza,
                        Air = SeaAtmos.Shallows.With(fog: "#0A1830", density: 0.015f) }, // clear round the lighthouse
                    new ZoneDef { Name = "灯塔背面", Tag = "BEHIND THE LIGHT", Cells = new RectInt(10, 0, 9, 2), Style = ZoneStyle.Ridge, Node = "Prologue_Sea_Zone_Ridge",
                        Air = SeaAtmos.Shallows.With(fog: "#0A0E22") },
                    new ZoneDef { Name = "表情包珊瑚林", Tag = "EMOJI CORAL", Cells = new RectInt(0, 10, 10, 13), Style = ZoneStyle.Coral, Node = "Prologue_Sea_Zone_Coral",
                        Air = SeaAtmos.Shallows.With(fog: "#160A1E") },
                    new ZoneDef { Name = "删帖墓地", Tag = "DELETED POSTS", Cells = new RectInt(0, 0, 10, 10), Style = ZoneStyle.Graveyard, Node = "Prologue_Sea_Zone_Graveyard",
                        Air = SeaAtmos.Shallows.With(fog: "#120810", mist: 0.9f, saturation: 0.85f, shadow: "#2A0A10", shadowAmount: 0.07f) },
                    new ZoneDef { Name = "维护间", Tag = "MAINTENANCE", Cells = new RectInt(18, 11, 11, 7), Style = ZoneStyle.Canyon, Node = "Prologue_Sea_Zone_Maintenance",
                        // Its own room in the canyon: steel-grey still water, clear and without the blue cast, so the orange
                        // sockets and gate stay the warmest things in it and the beacon's blue and green still read; little of
                        // the racks' cyan pools on its floor.
                        Air = SeaAtmos.Shallows.With(fog: "#0A1418", surface: "#4A7E8E", density: 0.016f, mist: 0.2f, saturation: 1f, shadow: "#0E1A1C", shadowAmount: 0.06f,
                            spill: 0.25f) },
                    new ZoneDef { Name = "机柜峡谷", Tag = "RACK CANYON", Cells = new RectInt(19, 0, 10, 11), Style = ZoneStyle.Canyon, Node = "Prologue_Sea_Zone_Canyon",
                        Air = SeaAtmos.Shallows.With(fog: "#04121E", density: 0.021f) },
                },
                Lighthouse = new Color(0.86f, 0.92f, 1f),
                LighthouseCell = new Vector2(14f, 3.5f),
                BeamTilt = 32f,
                Departure = Departure.Lamp,
                NearSurface = true,
                Air = SeaAtmos.Shallows,
                // Against the reef's walls, either side of the sunken phone and of the way up to the lighthouse.
                Vents = new[]
                {
                    new Vector2(12.7f, 19.3f), new Vector2(15.3f, 19.6f), new Vector2(13.7f, 18.2f), new Vector2(15.3f, 20.4f),
                    new Vector2(12f, 13f), new Vector2(16f, 13f), new Vector2(12f, 6f), new Vector2(16f, 6f),
                },
                Scene = "Scenes/CacheSeaShallows/cache_sea_shallows",
                StartNode = "Prologue_Sea_Start",
                SpottedNode = "Prologue_Sea_Spotted",
                FirstBattleNode = "Prologue_Sea_FirstBattle",
                FirstWinNode = "Prologue_Sea_FirstWin",
                FirewallNode = "Prologue_Sea_Firewall",
                GateNode = "Prologue_Sea_Gate",
                StuckNode = "Prologue_Sea_Stuck",
                TerminalNode = "Prologue_Sea_Terminal",
                BeaconNode = "Prologue_Sea_Beacon",
                AmbushHintNode = "Prologue_Sea_Ambushed",
                SodaHintNode = "Prologue_Sea_Soda",
                FragmentNodes = new[] { "Prologue_Sea_Fragment_Post", "Prologue_Sea_Fragment_Rice", "Prologue_Sea_Fragment_Draft" },
                FragmentTitles = new[] { "已删除的帖子", "已撤回的消息", "未发送的草稿" },
                Chests = new[]
                {
                    new ChestDef { Item = "white_rice", Count = 1, Label = "白米饭 ×1" },
                },
                // Beside the start, the coral's far corner, the graveyard's, the east end of the ridge and the
                // canyon's side passage.
                Pearls = new[] { new Vector2Int(15, 20), new Vector2Int(1, 21), new Vector2Int(1, 1), new Vector2Int(18, 1), new Vector2Int(22, 4) },
                PearlReward = new ChestDef { Item = "ice_soda", Count = 2, Tokens = 200, Label = "冰镇汽水 ×2、200 token" },
                PearlNode = "Prologue_Sea_Pearl",
                PearlsNode = "Prologue_Sea_Pearls",
                Enemies = new[]
                {
                    // At the mouth of the reef, its back to the start: the first battle, and a free round.
                    new EnemySpawn { Mark = '1', Kind = "clickbait", Dozing = true, Facing = Vector2Int.down, Battle = "shallow_clickbait" },
                    // Along the top of the coral, across the way north.
                    new EnemySpawn { Mark = '2', Kind = "repost", Route = "e", Battle = "shallow_repost" },
                    // Along the lane between the graves, turning back two cells short of the east wall: going east it
                    // sees up the east side, going west it doesn't, so the player can slip past it up that side instead of
                    // fighting (design/prologue-playtest.md 改法 9).
                    new EnemySpawn { Mark = '3', Kind = "clickbait", Route = "f", Battle = "shallow_pair" },
                    // Up and down the canyon's long straight, in the way down to the maintenance room.
                    new EnemySpawn { Mark = '4', Kind = "repost", Route = "g", Battle = "shallow_mixed" },
                },
            },
            // After the awakening the lighthouse's light comes down the rift and runs north up the server
            // room's middle aisle, over the low wall into the white hall, and stops on the white door. The
            // way there goes round: west through the recycle bin (short, a beacon) or east through the
            // long-context corridor (long, but the delay patch is there). In the hall four light plates have
            // to be turned until the light reaches the exit beside the door; until then it stays dark. Two ways round
            // the hall do it (Plates). Among the
            // noise bodies here are 冷知识型, which shrug off ice; they run faster than the shallows', and the
            // meter running out throws the party back to the last beacon (UI/Dungeon/DungeonScreen.cs Bounce).
            ["cache_sea_deep"] = new DungeonDef
            {
                Id = "cache_sea_deep", Area = "缓存海", Layer = "深层", LayerTag = "DEEP",
                Music = "deep", // colder, with a clock ticking in it: the maintenance is running out
                Caustics = 0.45f,
                Air = SeaAtmos.Deep,
                Objective = "在维护结束前找到出口",
                Map = new[]
                {
                    "#############################",
                    "#############DDD#############",
                    "##......#M.G........#.......#",
                    "##.####.#..M.....M..#.#####.#",
                    "#............=.=......#####.#",
                    "#.==..=.#...........#.......#",
                    "#....j..#M....M..M..#######.#",
                    "#.F..=..#.6.......m.#######.#",
                    "#.......######=############.#",
                    "#%...=..#...........#.......#",
                    "#%...4..#.####.####.#.###.###",
                    "#.==....#.2.......h.#.###C###",
                    "#.....=.#.####.####.#.#######",
                    "#.....=...i.......3.#l.....5#",
                    "#.k.....#.####.####.#######.#",
                    "#..==...#.....1.....####.##.#",
                    "#.....%%#.####.####.####F##.#",
                    "#...B...#C..................#",
                    "#....=..#...........#########",
                    "#.......######.##############",
                    "#C......######Q.#############",
                    "#############..##############",
                    "#############..##############",
                    "##############S##############",
                    "#############################",
                },
                Zones = new[]
                {
                    new ZoneDef { Name = "裂缝", Tag = "THE RIFT", Cells = new RectInt(12, 19, 5, 6), Style = ZoneStyle.Rift,
                        Air = SeaAtmos.Deep.With(fog: "#181221", density: 0.019f, shadow: "#3A0A08", shadowAmount: 0.08f) },
                    new ZoneDef { Name = "沉没机房", Tag = "SUNKEN SERVERS", Cells = new RectInt(8, 9, 13, 10), Style = ZoneStyle.Servers,
                        Air = SeaAtmos.Deep.With(shadow: "#2A1A08", shadowAmount: 0.06f) },
                    new ZoneDef { Name = "长上下文回廊", Tag = "LONG CONTEXT", Cells = new RectInt(21, 2, 8, 21), Style = ZoneStyle.Context, Node = "Prologue_Deep_Zone_Context",
                        Air = SeaAtmos.Deep.With(fog: "#121436", surface: "#3A62B8") },
                    new ZoneDef { Name = "白门", Tag = "WHITE DOOR", Cells = new RectInt(8, 0, 13, 9), Style = ZoneStyle.WhiteHall, Node = "Prologue_Deep_Zone_WhiteDoor",
                        // 海水忽然变得很亮: the light from the door fills the water.
                        Air = SeaAtmos.Deep.With(fog: "#1B3A66", surface: "#8CC4FF", density: 0.012f, lift: 1f, mist: 0.3f, saturation: 1f,
                            shadow: "#1E3A60", shadowAmount: 0.1f, vignette: 0f, gain: Vector3.one) },
                    new ZoneDef { Name = "回收站", Tag = "RECYCLE BIN", Cells = new RectInt(0, 0, 8, 25), Style = ZoneStyle.RecycleBin, Node = "Prologue_Deep_Zone_Bin",
                        Air = SeaAtmos.Deep.With(fog: "#0E141C", surface: "#4A6A86", density: 0.02f, saturation: 0.75f) },
                },
                Meter = true,
                MeterStart = 15f,
                ChaseSpeed = 4.8f, // faster than the player runs: only a sprint gets away
                Lighthouse = new Color(0.45f, 0.72f, 1f),
                LighthouseCell = new Vector2(14f, 29f),
                Departure = Departure.Column,
                Scene = "Scenes/CacheSeaDeep/cache_sea_deep",
                // Two ways round: east (14,6) | → /, (17,6) - → /, (17,3) / → \, (11,3) | → \, five turns with one
                // back; or west (14,6) | → \, (9,6) / → \, (9,2) \ → /, five turns by the west wall's pearl.
                Plates = "\\|//|-",
                StartNode = "Prologue_Deep_Start",
                LightNode = "Prologue_Deep_Light",
                BeaconNode = "Prologue_Deep_Beacon",
                AmbushHintNode = "Prologue_Deep_Ambushed",
                WarningNode = "Prologue_Deep_Warning",
                GoalNode = "Prologue_Deep_Exit",
                TimeoutNode = "Prologue_Deep_Timeout",
                FragmentNodes = new[] { "Prologue_Deep_Fragment_Clickbait", "Prologue_Deep_Fragment_Help" },
                FragmentTitles = new[] { "没人点开的标题", "从上面漂下来的话" },
                Chests = new[]
                {
                    new ChestDef { Delay = 10f, Label = "延时补丁（已自动安装：维护进度 −10%）" },
                    new ChestDef { Item = "white_rice", Count = 2, Label = "白米饭 ×2" },
                    new ChestDef { Tokens = 500, Label = "500 token" },
                },
                // On the ways both routes share, so either can find them all if the meter allows: beside the start in
                // the rift, the server room's north-east and south-east corners, the white hall's north-east corner
                // and its west wall.
                Pearls = new[] { new Vector2Int(13, 22), new Vector2Int(19, 9), new Vector2Int(19, 18), new Vector2Int(19, 2), new Vector2Int(9, 5) },
                PearlReward = new ChestDef { Item = "spicy_strip", Count = 2, Tokens = 300, Label = "辣条 ×2、300 token" },
                PearlsNode = "Prologue_Deep_Pearls",
                Enemies = new[]
                {
                    // At the server room's door, its back to the rift: the first preemptive strike.
                    new EnemySpawn { Mark = '1', Kind = "clickbait", Dozing = true, Facing = Vector2Int.down, Battle = "sea_clickbait" },
                    new EnemySpawn { Mark = '2', Kind = "repost", Route = "h", Battle = "sea_mixed" },
                    new EnemySpawn { Mark = '3', Kind = "clickbait", Route = "i", Battle = "sea_clickbait" },
                    new EnemySpawn { Mark = '4', Kind = "repost", Route = "jk", Battle = "sea_repost" },
                    // In the long-context corridor: the first 冷知识型, beside a 标题党型 telling the same 「弱点：火」.
                    new EnemySpawn { Mark = '5', Kind = "coldfact", Route = "l", Battle = "sea_coldfact" },
                    // Across the white hall, in front of the exit.
                    new EnemySpawn { Mark = '6', Kind = "repost", Route = "m", Battle = "sea_repost" },
                },
            },
        };

        public static DungeonDef Get(string id) => id != null && All.TryGetValue(id, out var d) ? d : null;

        public static int PearlTotal
        {
            get
            {
                var n = 0;
                foreach (var d in All.Values) n += d.Pearls.Length;
                return n;
            }
        }
    }
}
