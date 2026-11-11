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
    // turn, the drifting things bob, and the rest is batched, the coral, kelp and cables swaying in the shader.
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
        // emission, the outline's width, how strong the rim is, how much the avatar's lamp lights it, how dark the
        // foot of it stands (Toon.shader _FootDark; the racks' as the racks built in code had it) and how much the
        // lamp glints on it (_Gloss: the racks' metal and the phone's glass most, the white hall none, so the white
        // door stays the brightest thing there). The reef, coral, graves and phone keep SeaDecor's colours. The rim
        // is for round things: on the long flat faces of racks, slabs and the phone's screen, seen edge on, it
        // washes them out; on the bubbles it is half (the white one's a fifth: its top edge, seen from the camera's
        // height, went white), or a wall of them beside the lens turns white.
        static readonly Dictionary<string, (string lit, string shade, string emission, float outline, float rim, float lamp, float foot, float gloss)> Looks =
            new Dictionary<string, (string, string, string, float, float, float, float, float)>
        {
            ["M_Rack_Body"] = ("#1A2944", "#0C1528", "#000000", 0.02f, 0.2f, 1f, 0.6f, 1f),
            ["M_Rack_Trim"] = ("#0A1222", "#050A14", "#000000", 0.02f, 0.2f, 1f, 0.6f, 1.4f),
            ["M_Rack_Shadow"] = ("#050A14", "#03060C", "#000000", 0.02f, 0f, 1f, 0.6f, 0f),
            ["M_Rock"] = ("#172942", "#0A1424", "#000000", 0.04f, 0.2f, 1f, 0.65f, 0.3f),
            ["M_Bubble_White"] = ("#B8C6E0", "#7486AE", "#141C30", 0.012f, 0.2f, 0f, 0.8f, 0.3f),
            ["M_Bubble_Green"] = ("#5FD394", "#24805A", "#0B2418", 0.012f, 0.4f, 0f, 0.8f, 0.6f),
            ["M_Bubble_Blue"] = ("#4F86F0", "#2343A0", "#0B1736", 0.012f, 0.4f, 0f, 0.8f, 0.6f),
            ["M_Coral_Gold"] = ("#FFD23F", "#C98A12", "#3A1A00", 0.04f, 1f, 0f, 0.8f, 0.3f),
            ["M_Coral_Pink"] = ("#FF6FA8", "#B3326A", "#3A1A00", 0.04f, 1f, 0f, 0.8f, 0.3f),
            ["M_Coral_Orange"] = ("#FF8A3D", "#B84A12", "#3A1A00", 0.04f, 1f, 0f, 0.8f, 0.3f),
            ["M_Coral_Cyan"] = ("#5CE1FF", "#1E7FA8", "#0A3A50", 0.04f, 1f, 0f, 0.8f, 0.3f),
            ["M_Coral_Stem"] = ("#7A3A62", "#3A1430", "#000000", 0.03f, 1f, 0f, 0.8f, 0.3f),
            ["M_Coral_Mound"] = ("#5A2A4A", "#2A1028", "#12060E", 0.04f, 0.5f, 0.6f, 0.8f, 0.3f),
            ["M_Grave"] = ("#4A5268", "#22283A", "#08090E", 0.04f, 0.2f, 0.35f, 0.7f, 0.3f),
            ["M_Phone_Body"] = ("#1B2436", "#0B101C", "#050810", 0.06f, 0.2f, 1f, 0.7f, 1.2f),
            ["M_Phone_Screen"] = ("#163C70", "#163C70", "#0E2448", 0f, 0f, 0f, 0.7f, 1.2f),
            // The deep layer: the white hall's bleached racks, a step under the white door so it stays the
            // brightest thing there, and the recycle bin's thrown-away files (SeaDecor.Files' colours).
            ["M_White_Body"] = ("#B4C0D8", "#7C8AAA", "#1A2030", 0.02f, 0.2f, 0.2f, 0.8f, 0f),
            ["M_White_Trim"] = ("#94A2C0", "#5E6C8C", "#10141E", 0.02f, 0.2f, 0.2f, 0.8f, 0f),
            ["M_White_Shadow"] = ("#6C7A9A", "#454F6A", "#000000", 0.02f, 0f, 0.2f, 0.8f, 0f),
            ["M_File_Sheet"] = ("#E8EEF8", "#8A98B2", "#10141C", 0.03f, 0.3f, 0.3f, 0.8f, 0.3f),
            ["M_File_Folder"] = ("#F2C14E", "#A87A1A", "#2A1A00", 0.03f, 0.3f, 0.3f, 0.8f, 0.3f),
            ["M_Bin"] = ("#5A6680", "#2A3044", "#0A0C12", 0.03f, 0.3f, 0.6f, 0.8f, 0.3f),
        };

        // Lights: the racks' LEDs in each zone's light (SeaDecor.Look.Rack) and what glows on the props. Their emission
        // goes past white, so they glow through the lens (SeaLens) as the Blender scenes' emission strengths have it;
        // the cracks of the rift a little more.
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

        static readonly HashSet<string> Brighter = new HashSet<string> { "M_Emit_Crack", "M_Rack_LED_Rift" };

        // The reef's chat pillows are soft (Toon.shader _Wrap, _Inner, _Sheen): the light's edge spreads wide over
        // their curve, they are lighter toward the middle where they face the eye, the sea's light sits on them as a
        // soft highlight, and their thin outline is a dark tone of their own colour, not ink.
        static readonly Dictionary<string, string> Soft = new Dictionary<string, string>
        {
            ["M_Bubble_White"] = "#2A3656", ["M_Bubble_Green"] = "#0F3A2A", ["M_Bubble_Blue"] = "#10204A",
        };

        // What sways in the water, by the start of its name, swayed in the vertex shader (SeaCommon.cginc SeaSway),
        // so the coral and kelp batch with the rest: the coral's heads on their stalks, the kelp's blades from the
        // seabed (drawn from both sides, without an outline), the canyon's cables from the rack tops they hang from.
        static readonly (string prefix, string variant, float amp, float anchor, float length)[] Swaying =
        {
            ("EmojiCoral_", "sway", 0.12f, 0.3f, 1.2f),
            ("KelpEdge_", "kelp", 0.2f, 0f, 2.4f),
            ("CableHanging_", "cable", 0.25f, 11.5f, -6f),
        };

        MeshRenderer lamp; // the lighthouse's

        // The centre of the lighthouse's lamp, where DungeonWorld hangs its halo and beams.
        public Vector3 LampAt => lamp.bounds.center;

        readonly Transform root;
        readonly float glow; // how far the lights go past white: the maze's fully, the battle ring's a little
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly List<Mover> fans = new List<Mover>(), drift = new List<Mover>();

        // Where the sunken phone's screen is, its middle and facing (y out of the glass), for what SeaDecor lays over it;
        // null without one.
        public Pose? Screen { get; private set; }

        // An area's scenery, with its outer ring, the things drifting over it and the lighthouse beside the file
        // at def.Scene. Each file is copied in over as many frames as that takes, then sorted, then batched, a step
        // a frame, so that the dive playing meanwhile never stops. Near the surface the drifting things come down
        // over the walkways (OverWalkway), bigger: the see-through and shining ones (drawn added on, so where two
        // overlap they stack toward white, and their low-poly facets show) two to three times, the solid ones three
        // to four.
        public static async Awaitable<SeaScene> Load(DungeonDef def, Transform parent, DungeonMap map, System.Func<Vector2Int, float> wallTop)
        {
            if (def.Scene == null) return null;
            var scene = new SeaScene(parent, 1f);
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
                        var i = scene.drift.Count;
                        if (def.NearSurface)
                        {
                            t.localScale *= (Shines(t) ? 2f : 3f) + Hash(i, 23);
                            t.localPosition = OverWalkway(t.localPosition, i, def, map, wallTop);
                        }
                        scene.drift.Add(new Mover { T = t, Rest = t.localPosition, Turn = t.localRotation, Phase = Hash(i, 5) * 9f, Speed = 0.6f + Hash(5, i) * 0.6f });
                        still.Remove(t.gameObject);
                    }
                await Awaitable.NextFrameAsync();
                StaticBatchingUtility.Combine(still.ToArray(), scene.root.gameObject);
                await Awaitable.NextFrameAsync();
            }
            if (scene.lamp == null) throw new System.InvalidOperationException($"场景模型 {def.Scene} 里没有 Lighthouse_Lamp");
            return scene;
        }

        // The ring of rack stacks round the battle field: its lights glow less than the maze's, so in a battle the
        // effects are what shine.
        public static SeaScene Ring(Transform parent)
        {
            var scene = new SeaScene(parent, 0.42f);
            var prefab = Prefab("Scenes/BattleRing/battle_ring");
            var still = new List<GameObject>();
            scene.Sort(Object.Instantiate(prefab, scene.root, false), prefab.name, still);
            StaticBatchingUtility.Combine(still.ToArray(), scene.root.gameObject);
            return scene;
        }

        // A drifting thing modelled at `at` (the model lies at its place in the world), moved over the walkway (any
        // cell but a wall or the lighthouse) nearest it, up to 1.2 m off its middle; never over the main channel (the
        // sunken phone and its message), the plaza or behind the light, which lie in the line from the channel and the
        // plaza up to the lamp. Two metres over the walls round it but at least 10 m up, 8 m over the eye, so a camera
        // looking up from a walkway has the light net and the lamp clear over it; and up to 3 m higher still.
        static Vector3 OverWalkway(Vector3 at, int i, DungeonDef def, DungeonMap map, System.Func<Vector2Int, float> wallTop)
        {
            var from = new Vector2Int(Mathf.FloorToInt(at.x / Dungeons.Cell), map.Height - 1 - Mathf.FloorToInt(at.z / Dungeons.Cell));
            var cell = from;
            var nearest = int.MaxValue;
            for (var dy = -8; dy <= 8; dy++)
            for (var dx = -8; dx <= 8; dx++)
            {
                var c = from + new Vector2Int(dx, dy);
                if (!map.Inside(c.x, c.y) || map[c] == '#' || map[c] == 'L' || dx * dx + dy * dy >= nearest) continue;
                if (def.ZoneAt(c)?.Style is ZoneStyle.Channel or ZoneStyle.Plaza or ZoneStyle.Ridge) continue;
                nearest = dx * dx + dy * dy;
                cell = c;
            }
            var top = 0f;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                top = Mathf.Max(top, wallTop(cell + new Vector2Int(dx, dy)));
            var p = map.World(cell) + new Vector3((Hash(i, 29) - 0.5f) * 2.4f, 0f, (Hash(29, i) - 0.5f) * 2.4f);
            p.y = Mathf.Clamp(top + 2f, 10f, 14f) + Hash(i, 31) * 3f;
            return p;
        }

        // Wears glass or a light: drawn added on, or glowing past the lens's threshold.
        static bool Shines(Transform t)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            foreach (var m in r.sharedMaterials)
                if (Glass.ContainsKey(m.name) || Lights.ContainsKey(m.name)) return true;
            return false;
        }

        SeaScene(Transform parent, float glow)
        {
            root = Parts.Node("Scene", parent);
            this.glow = glow;
        }

        // The lighthouse's lamp in the light's colour (DungeonWorld), its frame as it is.
        public void Light(Material light)
        {
            var looks = lamp.sharedMaterials;
            for (var i = 0; i < looks.Length; i++)
                if (Lights.ContainsKey(looks[i].name)) looks[i] = light;
            lamp.sharedMaterials = looks;
        }

        // Its looks, with the area or the battle field taken down: Parts.Free only finds those a piece still
        // wears, and Light takes the lamp's off it.
        public void Dispose()
        {
            foreach (var m in materials.Values) Object.Destroy(m);
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
                var variant = name.StartsWith("SunkenPhone_") ? "flat" : null;
                var sway = System.Array.FindIndex(Swaying, s => name.StartsWith(s.prefix));
                if (sway >= 0) variant = Swaying[sway].variant;
                var looks = r.sharedMaterials;
                if (variant == "flat") FindScreen(r, looks);
                for (var i = 0; i < looks.Length; i++) looks[i] = Look(looks[i].name, variant, sway);
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
                still.Add(go);
            }
        }

        // The phone's screen (Screen) from the top of the part in M_Phone_Screen, as the model has no uv to lay a
        // picture on: of its faces lying flat (the glass and its underside), those highest along their mean normal.
        void FindScreen(MeshRenderer r, Material[] looks)
        {
            var sub = System.Array.FindIndex(looks, m => m.name.Replace(" (Instance)", "") == "M_Phone_Screen");
            if (sub < 0) return;
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices;
            var t = mesh.GetTriangles(sub);
            var world = r.transform.localToWorldMatrix;
            var corners = new Vector3[t.Length];
            for (var i = 0; i < t.Length; i++) corners[i] = world.MultiplyPoint3x4(v[t[i]]);
            Vector3 Normal(int i)
            {
                var n = Vector3.Cross(corners[i + 1] - corners[i], corners[i + 2] - corners[i]);
                return n.y < 0f ? -n : n;
            }
            var up = Vector3.zero;
            for (var i = 0; i < t.Length; i += 3)
                if (Normal(i).normalized.y > 0.95f) up += Normal(i);
            up.Normalize();
            var high = float.MinValue;
            for (var i = 0; i < t.Length; i += 3)
                if (Normal(i).normalized.y > 0.95f) high = Mathf.Max(high, Vector3.Dot(corners[i], up));
            var sum = Vector3.zero;
            var area = 0f;
            for (var i = 0; i < t.Length; i += 3)
            {
                var n = Normal(i);
                if (n.normalized.y <= 0.95f || Vector3.Dot(corners[i], up) < high - 0.01f) continue;
                sum += (corners[i] + corners[i + 1] + corners[i + 2]) / 3f * n.magnitude;
                area += n.magnitude;
            }
            Screen = new Pose(sum / area, Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up), up));
        }

        // variant: "flat" without the rim, whatever the material; or swaying as Swaying[sway] has it.
        Material Look(string name, string variant, int sway)
        {
            name = name.Replace(" (Instance)", "");
            var key = variant == null ? name : name + " (" + variant + ")";
            if (materials.TryGetValue(key, out var m)) return m;
            var flat = variant == "flat";
            if (Looks.TryGetValue(name, out var look))
            {
                var rim = Rim.WithAlpha(flat ? 0f : look.rim);
                m = SeaArt.Toon(Palette.Hex(look.lit), Palette.Hex(look.shade), rim, Palette.Hex(look.emission), look.outline);
                m.SetFloat("_Lamp", look.lamp);
                m.SetFloat("_FootDark", look.foot);
                m.SetFloat("_Gloss", look.gloss);
                m.SetFloat("_Caustic", 1f); // the surface's light net plays over it
                m.SetFloat("_Fade", 1f); // makes way for the lens and the avatar in the maze (Toon.shader)
                if (Soft.TryGetValue(name, out var ink))
                {
                    m.SetFloat("_Wrap", 0.45f);
                    m.SetFloat("_Inner", 0.35f);
                    // A little lighter than lit, but no channel past 0.9, so the white one's face (and the blue one's
                    // blue) stays off white at 1-3 m, where the caustic and the sheen still add to it.
                    var inner = Palette.Hex(look.lit) * 1.1f;
                    m.SetColor("_InnerColor", inner * Mathf.Min(1f, 0.9f / inner.maxColorComponent));
                    // The sea's light sits on them as glossy as the lamp's: close up the whole front of a pillow can
                    // turn into that highlight, so the white one's is the least.
                    m.SetFloat("_Sheen", look.gloss);
                    m.SetColor("_OutlineColor", Palette.Hex(ink));
                }
            }
            else if (Lights.TryGetValue(name, out var hex))
            {
                var c = Palette.Hex(hex);
                m = SeaArt.Toon(c * 0.35f, c * 0.35f, Color.clear, c * (1.9f * glow * (Brighter.Contains(name) ? 1.2f : 1f)), 0f);
                m.SetFloat("_Fade", 1f);
                // The racks' LEDs blink (the battle ring's less, as they glow less), the rift's cracks breathe.
                if (name.StartsWith("M_Rack_LED_")) m.SetFloat("_Blink", glow);
                else if (name == "M_Emit_Crack") m.SetFloat("_Breathe", 1f);
            }
            else if (Glass.TryGetValue(name, out var glass)) m = SeaArt.Glow(SeaArt.White, glass);
            else
            {
                Debug.LogError($"场景模型的材质 {name} 没有对应的样子");
                m = SeaArt.Toon(Color.magenta, Color.magenta, Color.clear);
            }
            if (sway >= 0)
            {
                var (_, kind, amp, anchor, length) = Swaying[sway];
                m.SetFloat("_SwayAmp", amp);
                m.SetFloat("_SwayAnchor", anchor);
                m.SetFloat("_SwayLength", length);
                if (kind == "kelp")
                {
                    m.SetFloat("_Cull", 0f);
                    m.SetFloat("_OutlineWidth", 0f);
                }
            }
            m.name = name;
            return materials[key] = m;
        }

        static float Hash(int x, int y) => DungeonWorld.Hash(x, y);

        public void Animate(float time)
        {
            foreach (var f in fans) f.T.localRotation = f.Turn * Quaternion.Euler(0f, time * f.Speed, 0f);
            foreach (var d in drift)
            {
                d.T.localPosition = d.Rest + Vector3.up * Mathf.Sin(time * 0.35f * d.Speed + d.Phase) * 0.6f;
                d.T.localRotation = Quaternion.Euler(0f, time * 6f * d.Speed, 0f) * d.Turn;
            }
        }
    }
}
