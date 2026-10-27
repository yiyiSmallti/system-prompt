using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Showcase
{
    // 一套主题色：天幕从地平线的亮色过渡到顶上的深色，地面的网格、圆台的光圈和人物的边缘光用同一个色相。
    readonly struct Palette
    {
        public readonly Color Top, Horizon, Glow, Floor, Line, Ring, Rim;

        public Palette(string top, string horizon, string glow, float glowStrength, string floor, string line, string ring, Color rim)
        {
            Top = Stage.Hex(top);
            Horizon = Stage.Hex(horizon);
            Glow = Stage.Hex(glow);
            Glow.a = glowStrength;
            Floor = Stage.Hex(floor);
            Line = Stage.Hex(line);
            Ring = Stage.Hex(ring);
            Rim = rim;
        }

        // DeepSeek 战斗服：深海蓝。
        public static readonly Palette Sea = new Palette("#061230", "#2F6BFF", "#8CC4FF", 0.55f, "#050E26", "#3D7BFF", "#8FD0FF", new Color(0.55f, 0.85f, 1f, 0.45f));
        // DeepSeek 便服：周末的晴空青。
        public static readonly Palette Sky = new Palette("#0B2546", "#36BCE6", "#BDF3FF", 0.5f, "#081A33", "#5FD4F0", "#C8F7FF", new Color(0.7f, 0.95f, 1f, 0.4f));
        // 主角战斗服：终端绿。
        public static readonly Palette Terminal = new Palette("#031D17", "#1E9E68", "#8CF0BE", 0.5f, "#03170F", "#3FBF7F", "#86FFC6", new Color(0.6f, 1f, 0.8f, 0.4f));
        // 主角家居服：凌晨三点房间里的紫和玫瑰色。
        public static readonly Palette Night = new Palette("#150B33", "#D9669A", "#FFB3B0", 0.5f, "#100826", "#F08AB8", "#FFC6DA", new Color(1f, 0.78f, 0.88f, 0.38f));
        // 几个人一起出场：靛紫。
        public static readonly Palette Studio = new Palette("#0B0D2C", "#5548FF", "#AC9FFF", 0.5f, "#090A22", "#7F8CFF", "#BAC4FF", new Color(0.62f, 0.8f, 1f, 0.4f));
        // 衍生作的格斗动作：街机的橙红。
        public static readonly Palette Arena = new Palette("#1D0713", "#FF5E3A", "#FFC38F", 0.55f, "#15050E", "#FF7A50", "#FFD6A8", new Color(1f, 0.82f, 0.62f, 0.38f));
    }

    // 场景：天幕（天空盒）、地面，以及道具模型的摆放。场景里的东西全用代码做，着色器在 Resources/Shaders。
    sealed class Stage
    {
        public readonly Transform Root;
        readonly Material sky, floor;
        // 写实光照的主光（Figure.Toon 关掉时才有用）。
        readonly Light key;
        readonly Vector4[] rings = new Vector4[8], shadows = new Vector4[8];

        public Stage()
        {
            Root = new GameObject("Stage").transform;
            sky = new Material(Shader("Backdrop"));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.fog = false;

            floor = new Material(Shader("Floor"));
            var plane = new GameObject("Floor");
            plane.transform.SetParent(Root, false);
            const float half = 160f;
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half), new Vector3(half, 0f, half), new Vector3(half, 0f, -half) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
            };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 0.1f, half * 2f));
            plane.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = plane.AddComponent<MeshRenderer>();
            r.sharedMaterial = floor;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(Root, false);
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.intensity = 0.68f;
            key.shadows = Figure.Toon ? LightShadows.None : LightShadows.Soft;
            key.shadowStrength = 0.75f;
            key.shadowResolution = UnityEngine.Rendering.LightShadowResolution.High; // 再细边缘就太硬
            key.shadowBias = 0.03f;
            key.shadowNormalBias = 0.3f;
        }

        // 主光跟着这一格的镜头摆：机位右上方偏前，脸总在亮面里，影子落在人物身后。
        public void Light(Camera camera)
        {
            var c = camera.transform;
            key.transform.rotation = Quaternion.LookRotation(-(c.right * 0.45f + Vector3.up * 0.65f - c.forward).normalized);
        }

        public void Use(Palette p, Vector3 centre, float fade = 22f, float stripePhase = 0f)
        {
            sky.SetColor("_Top", p.Top);
            sky.SetColor("_Horizon", p.Horizon);
            sky.SetColor("_Glow", p.Glow);
            sky.SetFloat("_Phase", stripePhase);
            floor.SetColor("_Base", p.Floor);
            floor.SetColor("_Line", p.Line);
            floor.SetColor("_Edge", p.Horizon);
            floor.SetColor("_Glow", p.Ring);
            floor.SetFloat("_Fade", fade);
            floor.SetVector("_Center", new Vector4(centre.x, centre.z, 0f, 0f));
            Scroll(Vector2.zero);
            Clear();
        }

        // 远处融进地平线的中心，分屏时每格换成那一格的人物。
        public void Centre(Vector3 at) => floor.SetVector("_Center", new Vector4(at.x, at.z, 0f, 0f));

        // 网格往后挪（米，xz），人物原地走跑时看起来在前进。
        public void Scroll(Vector2 by) => floor.SetVector("_Scroll", new Vector4(by.x, by.y, 0f, 0f));

        public void Clear()
        {
            Array.Clear(rings, 0, rings.Length);
            Array.Clear(shadows, 0, shadows.Length);
            Apply();
        }

        // 第 slot 个圆台（xz 圆心，半径米），半径 0 不画。
        public void Ring(int slot, Vector3 at, float radius)
        {
            rings[slot] = new Vector4(at.x, at.z, radius, 0f);
            Apply();
        }

        // 第 slot 团接地阴影，跟着人物的腰；离地越高越淡。
        public void Shadow(int slot, Figure f, float strength = 0.6f)
        {
            if (f == null) { shadows[slot] = Vector4.zero; Apply(); return; }
            var hips = f.Hips;
            var lift = Mathf.Clamp01((hips.y - f.Root.position.y) / (f.Height * 0.53f));
            shadows[slot] = new Vector4(hips.x, hips.z, 0.36f * f.Height / 1.6f, strength * Mathf.Lerp(0.55f, 1f, lift));
            Apply();
        }

        public void Shadow(int slot, Vector3 at, float radius, float strength)
        {
            shadows[slot] = new Vector4(at.x, at.z, radius, strength);
            Apply();
        }

        void Apply()
        {
            floor.SetVectorArray("_Rings", rings);
            floor.SetVectorArray("_Shadows", shadows);
        }

        public static Shader Shader(string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + name);
            if (s == null) throw new InvalidOperationException($"找不到着色器 Resources/Shaders/{name}");
            return s;
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : throw new ArgumentException(hex);

        public static float Smooth(float from, float to, float x)
        {
            var t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // 缓入缓出（0–1）。
        public static float Ease(float x)
        {
            x = Mathf.Clamp01(x);
            return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
        }

        // 道具模型（Resources/Models/Props），同游戏的 Dungeon/SeaModels.cs：放在 parent 的原点、底面贴地、最长边 size 米，
        // 和人物用同一种着色：三渲二加描边（光从世界里的方向打来，_SeaLightDir，转台转的时候明暗跟着变），或写实光照。
        static readonly Dictionary<string, Quaternion> Turns = new Dictionary<string, Quaternion>
        {
            ["Whale"] = Quaternion.Euler(0f, 180f, 0f),
            ["OldMonitor"] = Quaternion.Euler(0f, 90f, 0f),
        };

        readonly List<GameObject> props = new List<GameObject>();

        // 每镜开始前把道具都收起来，道具那几镜再拿出自己的。
        public void HideProps()
        {
            foreach (var p in props) p.SetActive(false);
        }

        public GameObject Prop(string name, float size)
        {
            const string root = "Models/Props/";
            var prefab = Resources.Load<GameObject>(root + name);
            if (prefab == null) throw new InvalidOperationException($"找不到道具模型 Resources/{root}{name}");
            var texture = Resources.Load<Texture2D>(root + name + "_BaseColor");
            if (texture == null) throw new InvalidOperationException($"找不到道具贴图 Resources/{root}{name}_BaseColor");
            var look = new Material(Shader(Figure.Toon ? "Toon" : "Lit"));
            look.SetColor("_Color", new Color(1.12f, 1.12f, 1.15f, 1f));
            look.SetColor("_ShadeColor", new Color(0.55f, 0.6f, 0.8f, 1f));
            look.SetColor("_RimColor", new Color(0.6f, 0.85f, 1f, 0.6f));
            look.SetFloat("_RimPower", 3f);
            look.SetFloat("_OutlineWidth", 0.012f);
            look.mainTexture = texture;

            var holder = new GameObject(name).transform;
            holder.SetParent(Root, false);
            props.Add(holder.gameObject);
            var model = Object.Instantiate(prefab, holder, false);
            model.name = "Model";
            if (Turns.TryGetValue(name, out var turn)) model.transform.localRotation = turn * model.transform.localRotation;
            var renderers = model.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) throw new InvalidOperationException($"道具模型 {name} 没有网格");
            var bounds = new Bounds();
            var first = true;
            foreach (var r in renderers)
            {
                var looks = new Material[r.sharedMaterials.Length];
                for (var i = 0; i < looks.Length; i++) looks[i] = look;
                r.sharedMaterials = looks;
                r.shadowCastingMode = Figure.Toon ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = !Figure.Toon;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var p = holder.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (first) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    first = false;
                }
            }
            var scale = size / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            model.transform.localScale *= scale;
            model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            return holder.gameObject;
        }
    }
}
