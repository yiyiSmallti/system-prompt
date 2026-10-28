using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SystemPrompt.Dungeon
{
    // An area's scenery modelled in Blender (assets/scenes/<id>/, DungeonDef.Scene, imported by
    // Editor/ModelAssetImporter.cs): the maze's racks and the ring of taller stacks round it, the lighthouse,
    // what stands in each zone instead of racks (the reef's bubbles, the coral, the graves, the sunken phone,
    // kelp and cables, litter at the walls' foot) and the things drifting high over it all. It stands in for
    // DungeonWorld's rack mesh and SeaDecor's cells. The seabed (its own shader, lit by the zones and the
    // avatar) and everything the player uses (props, firewalls, puzzles) are still built in code, so the
    // model's pieces of them are left out, all but the firewalls' and the gate's frames.
    // The model comes in at its place in the world: Blender (x, y, z) is Unity (x, z, y), as DungeonMap.World
    // has it. Its flat materials, known by name, are swapped for the sea's cel shading; the fans on the racks
    // turn, the kelp sways, the drifting things bob, and the rest is batched.
    // The ring of rack stacks round the battle field (BattleStage, assets/scenes/battle_ring/) comes in the
    // same way, one file of racks in the field's own space.
    public sealed class SeaScene
    {
        struct Mover
        {
            public Transform T;
            public Vector3 Rest;
            public Quaternion Turn;
            public float Phase, Speed;
        }

        // The model's pieces built in code instead, by the start of their names, or the end.
        static readonly string[] CodeBuilt = { "Seabed_", "Beacon_", "Fragment_", "DataChest_", "Terminal_", "ResetConsole_", "CacheBlock_", "Socket_" };
        static readonly string[] CodeBuiltParts = { "_Barrier", "_Door" };

        static readonly Color Rim = new Color(0.55f, 0.9f, 1f, 1f); // as SeaDecor's things

        // Each of the model's materials (assets/scenes/<id>/README.md 材质表) in the cel shading: lit, shade and
        // emission, the outline's width, how strong the rim is and how much the avatar's lamp lights it. The
        // reef, coral, graves and phone keep SeaDecor's colours. The rim is for round things: on the long flat
        // faces of racks, slabs and the phone's screen, seen edge on, it washes them out.
        static readonly Dictionary<string, (string lit, string shade, string emission, float outline, float rim, float lamp)> Looks =
            new Dictionary<string, (string, string, string, float, float, float)>
        {
            ["M_Rack_Body"] = ("#1A2944", "#0C1528", "#000000", 0.02f, 0.2f, 1f),
            ["M_Rack_Trim"] = ("#0A1222", "#050A14", "#000000", 0.02f, 0.2f, 1f),
            ["M_Rack_Shadow"] = ("#050A14", "#03060C", "#000000", 0.02f, 0f, 1f),
            ["M_Rock"] = ("#172942", "#0A1424", "#000000", 0.04f, 0.2f, 1f),
            ["M_Bubble_White"] = ("#EEF3FF", "#97A8C8", "#1A2238", 0.03f, 1f, 0f),
            ["M_Bubble_Green"] = ("#6EE08F", "#2F8F55", "#0E2A1A", 0.03f, 1f, 0f),
            ["M_Bubble_Blue"] = ("#4A8BFF", "#2448A8", "#0C1838", 0.03f, 1f, 0f),
            ["M_Coral_Gold"] = ("#FFD23F", "#C98A12", "#3A1A00", 0.04f, 1f, 0f),
            ["M_Coral_Pink"] = ("#FF6FA8", "#B3326A", "#3A1A00", 0.04f, 1f, 0f),
            ["M_Coral_Orange"] = ("#FF8A3D", "#B84A12", "#3A1A00", 0.04f, 1f, 0f),
            ["M_Coral_Cyan"] = ("#5CE1FF", "#1E7FA8", "#0A3A50", 0.04f, 1f, 0f),
            ["M_Coral_Stem"] = ("#7A3A62", "#3A1430", "#000000", 0.03f, 1f, 0f),
            ["M_Coral_Mound"] = ("#5A2A4A", "#2A1028", "#12060E", 0.04f, 0.5f, 0.6f),
            ["M_Grave"] = ("#4A5268", "#22283A", "#08090E", 0.04f, 0.2f, 0.35f),
            ["M_Phone_Body"] = ("#1B2436", "#0B101C", "#050810", 0.06f, 0.2f, 1f),
            ["M_Phone_Screen"] = ("#163C70", "#163C70", "#0E2448", 0f, 0f, 0f),
            // The deep layer: the white hall's bleached racks, a step under the white door so it stays the
            // brightest thing there, and the recycle bin's thrown-away files (SeaDecor.Files' colours).
            ["M_White_Body"] = ("#B4C0D8", "#7C8AAA", "#1A2030", 0.02f, 0.2f, 0.2f),
            ["M_White_Trim"] = ("#94A2C0", "#5E6C8C", "#10141E", 0.02f, 0.2f, 0.2f),
            ["M_White_Shadow"] = ("#6C7A9A", "#454F6A", "#000000", 0.02f, 0f, 0.2f),
            ["M_File_Sheet"] = ("#E8EEF8", "#8A98B2", "#10141C", 0.03f, 0.3f, 0.3f),
            ["M_File_Folder"] = ("#F2C14E", "#A87A1A", "#2A1A00", 0.03f, 0.3f, 0.3f),
            ["M_Bin"] = ("#5A6680", "#2A3044", "#0A0C12", 0.03f, 0.3f, 0.6f),
        };

        // Lights: the racks' LEDs in each zone's light (SeaDecor.Look.Rack) and what glows on the props.
        static readonly Dictionary<string, string> Lights = new Dictionary<string, string>
        {
            ["M_Rack_LED_Channel"] = "#40F2D9", ["M_Rack_LED_Reef"] = "#7BFFD2", ["M_Rack_LED_Coral"] = "#FF8AB4",
            ["M_Rack_LED_Graveyard"] = "#FF5A64", ["M_Rack_LED_Ridge"] = "#BFD8FF", ["M_Rack_LED_Canyon"] = "#5CE1FF",
            ["M_Rack_LED_Plaza"] = "#E8F0FF", ["M_Emit_ColdWhite"] = "#DCEBFF", ["M_Emit_Green"] = "#4CFF99",
            ["M_Emit_Firewall"] = "#FF455C", ["M_Emit_Amber"] = "#FFB347", ["M_Emit_ChannelGrid"] = "#3380FF",
            ["M_Rack_LED_Rift"] = "#FF6A4A", ["M_Rack_LED_Servers"] = "#FFB347", ["M_Rack_LED_RecycleBin"] = "#C2CCE0",
            ["M_Rack_LED_Context"] = "#B596FF", ["M_Rack_LED_WhiteHall"] = "#FFFFFF", ["M_Emit_Crack"] = "#FF5A3C",
        };

        // See-through: the lighthouse's lamp room, the drifting chat bubbles and cards.
        static readonly Dictionary<string, Color> Glass = new Dictionary<string, Color>
        {
            ["M_Glass"] = Palette.Hex("#A3DFFF").WithAlpha(0.22f),
            ["M_Drift_Glass"] = Palette.Hex("#7BCFFF").WithAlpha(0.3f),
        };

        MeshRenderer lamp; // the lighthouse's

        // The centre of the lighthouse's lamp, where DungeonWorld hangs its halo and beams.
        public Vector3 LampAt => lamp.bounds.center;

        readonly Transform root;
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly List<Mover> fans = new List<Mover>(), kelp = new List<Mover>(), drift = new List<Mover>();

        // An area's scenery, with its outer ring, the things drifting over it and the lighthouse beside the file
        // at def.Scene. Each file is copied in over as many frames as that takes, then sorted, then batched, a step
        // a frame, so that the dive playing meanwhile never stops.
        public static async Awaitable<SeaScene> Load(DungeonDef def, Transform parent)
        {
            if (def.Scene == null) return null;
            var scene = new SeaScene(parent);
            foreach (var part in new[] { "", "_outer", "_drift" })
            {
                var prefab = Prefab(def.Scene + part);
                var made = Object.InstantiateAsync(prefab, scene.root);
                await Awaitable.FromAsyncOperation(made);
                var model = made.Result[0];
                var still = new List<GameObject>();
                scene.Sort(model, prefab.name, still);
                if (part == "_drift")
                    foreach (Transform t in model.transform)
                    {
                        scene.drift.Add(new Mover { T = t, Rest = t.localPosition, Turn = t.localRotation, Phase = Hash(scene.drift.Count, 5) * 9f, Speed = 0.6f + Hash(5, scene.drift.Count) * 0.6f });
                        still.Remove(t.gameObject);
                    }
                await Awaitable.NextFrameAsync();
                StaticBatchingUtility.Combine(still.ToArray(), scene.root.gameObject);
                await Awaitable.NextFrameAsync();
            }
            if (scene.lamp == null) throw new System.InvalidOperationException($"场景模型 {def.Scene} 里没有 Lighthouse_Lamp");
            return scene;
        }

        // The ring of rack stacks round the battle field.
        public static SeaScene Ring(Transform parent)
        {
            var scene = new SeaScene(parent);
            var prefab = Prefab("Scenes/BattleRing/battle_ring");
            var still = new List<GameObject>();
            scene.Sort(Object.Instantiate(prefab, scene.root, false), prefab.name, still);
            StaticBatchingUtility.Combine(still.ToArray(), scene.root.gameObject);
            return scene;
        }

        SeaScene(Transform parent)
        {
            root = Parts.Node("Scene", parent);
        }

        // The lighthouse's lamp in the light's colour (DungeonWorld), its frame as it is.
        public void Light(Material light)
        {
            var looks = lamp.sharedMaterials;
            for (var i = 0; i < looks.Length; i++)
                if (Lights.ContainsKey(looks[i].name)) looks[i] = light;
            lamp.sharedMaterials = looks;
        }

        static GameObject Prefab(string path)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) throw new System.InvalidOperationException($"找不到场景模型 Resources/{path}");
            return prefab;
        }

        // A copy of one of the model's files under root, named as the file, its pieces sorted: left out, moving, or
        // still (into still).
        void Sort(GameObject model, string file, List<GameObject> still)
        {
            model.name = file;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                var go = r.gameObject;
                var name = go.name;
                if (System.Array.Exists(CodeBuilt, name.StartsWith) || System.Array.Exists(CodeBuiltParts, name.EndsWith))
                {
                    go.SetActive(false);
                    continue;
                }
                // The phone's screen and the message on it lie flat, seen edge on from across the channel.
                var flat = name.StartsWith("SunkenPhone_");
                var looks = r.sharedMaterials;
                for (var i = 0; i < looks.Length; i++) looks[i] = Look(looks[i].name, flat);
                r.sharedMaterials = looks;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                var t = go.transform;
                if (name == "Lighthouse_Lamp")
                {
                    lamp = r; // DungeonWorld lights it in the lighthouse's colour
                    continue;
                }
                if (name.EndsWith("_Fan"))
                {
                    // Turning on their axle, the model's up, each at its own speed.
                    fans.Add(new Mover { T = t, Turn = t.localRotation, Speed = 120f + Hash(fans.Count, 1) * 160f });
                    continue;
                }
                if (name.StartsWith("KelpEdge_"))
                {
                    // Swaying from where the strands come out of the seabed.
                    var foot = r.bounds;
                    var pivot = Parts.Node(name, t.parent);
                    pivot.position = new Vector3(foot.center.x, foot.min.y, foot.center.z);
                    t.SetParent(pivot, true);
                    kelp.Add(new Mover { T = pivot, Turn = pivot.localRotation, Phase = Hash(kelp.Count, 9) * 9f });
                    continue;
                }
                still.Add(go);
            }
        }

        // flat: without the rim, whatever the material.
        Material Look(string name, bool flat)
        {
            name = name.Replace(" (Instance)", "");
            var key = flat ? name + " (flat)" : name;
            if (materials.TryGetValue(key, out var m)) return m;
            if (Looks.TryGetValue(name, out var look))
            {
                var rim = Rim.WithAlpha(flat ? 0f : look.rim);
                m = SeaArt.Toon(Palette.Hex(look.lit), Palette.Hex(look.shade), rim, Palette.Hex(look.emission), look.outline);
                m.SetFloat("_Lamp", look.lamp);
            }
            else if (Lights.TryGetValue(name, out var hex))
            {
                var c = Palette.Hex(hex);
                m = SeaArt.Toon(c * 0.45f, c * 0.45f, Color.clear, c * 0.7f, 0f);
            }
            else if (Glass.TryGetValue(name, out var glass)) m = SeaArt.Glow(SeaArt.White, glass);
            else
            {
                Debug.LogError($"场景模型的材质 {name} 没有对应的样子");
                m = SeaArt.Toon(Color.magenta, Color.magenta, Color.clear);
            }
            m.name = name;
            return materials[key] = m;
        }

        static float Hash(int x, int y) => DungeonWorld.Hash(x, y);

        public void Animate(float time)
        {
            foreach (var f in fans) f.T.localRotation = f.Turn * Quaternion.Euler(0f, time * f.Speed, 0f);
            foreach (var k in kelp)
                k.T.localRotation = k.Turn * Quaternion.Euler(Mathf.Sin(time * 0.9f + k.Phase) * 3f, 0f, Mathf.Cos(time * 0.7f + k.Phase * 1.3f) * 3f);
            foreach (var d in drift)
            {
                d.T.localPosition = d.Rest + Vector3.up * Mathf.Sin(time * 0.35f * d.Speed + d.Phase) * 0.6f;
                d.T.localRotation = Quaternion.Euler(0f, time * 6f * d.Speed, 0f) * d.Turn;
            }
        }
    }
}
