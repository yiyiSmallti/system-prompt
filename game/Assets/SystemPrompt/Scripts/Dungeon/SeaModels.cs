using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SystemPrompt.Dungeon
{
    // The still things that are models (Resources/Models/Props, Tripo models that Editor/ModelAssetImporter.cs
    // brings in as plain meshes, each with its <name>_BaseColor): the wreckage lying about the maze
    // (DungeonWorld.BuildWreckage) and the whale of light in the 集成 (BattleEffects.Whale).
    public static class SeaModels
    {
        const string Root = "Models/Props/";

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Material> looks = new Dictionary<string, Material>();

        // Turns that put a model's front toward +z: the old monitor's screen.
        static readonly Dictionary<string, Quaternion> Turns = new Dictionary<string, Quaternion>
        {
            ["OldMonitor"] = Quaternion.Euler(0f, 90f, 0f),
        };

        public static Texture2D Texture(string name)
        {
            if (textures.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(Root + name + "_BaseColor");
            if (t == null) throw new InvalidOperationException($"找不到道具贴图 Resources/{Root}{name}_BaseColor");
            return textures[name] = t;
        }

        // The model's own colours in the sea's cel shading, one material per model.
        public static Material Look(string name)
        {
            if (looks.TryGetValue(name, out var m) && m != null) return m;
            // Lifted a little out of the dark of the sea, with a strong rim so they read against the racks.
            m = SeaArt.Toon(new Color(1.15f, 1.15f, 1.2f, 1f), new Color(0.5f, 0.58f, 0.8f, 1f), new Color(0.5f, 0.9f, 1f, 1f), new Color(0.04f, 0.07f, 0.1f, 0f), 0.02f);
            m.SetFloat("_RimPower", 2.5f);
            m.SetFloat("_Fade", 1f); // a wreck makes way for the lens and the avatar in the maze (Toon.shader)
            m.SetFloat("_Caustic", 1f); // and takes the surface's light net
            m.mainTexture = Texture(name);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset; // shared by every copy of the model: Parts.Free leaves it
            return looks[name] = m;
        }

        // The model under a holder of its name at parent's origin, standing on it (the bottom of its bounds at
        // y 0, centred over it) and scaled so its longest side is size metres; look replaces its own colours.
        public static GameObject Place(string name, Transform parent, float size, Material look = null)
        {
            var prefab = Resources.Load<GameObject>(Root + name);
            if (prefab == null) throw new InvalidOperationException($"找不到道具模型 Resources/{Root}{name}");
            var holder = Parts.Node(name, parent).gameObject;
            var model = Object.Instantiate(prefab, holder.transform, false);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            if (Turns.TryGetValue(name, out var turn)) model.transform.localRotation = turn * model.transform.localRotation;

            look ??= Look(name);
            var renderers = model.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) throw new InvalidOperationException($"道具模型 {name} 没有网格");
            var bounds = new Bounds();
            var first = true;
            foreach (var r in renderers)
            {
                var looksOf = new Material[r.sharedMaterials.Length];
                for (var i = 0; i < looksOf.Length; i++) looksOf[i] = look;
                r.sharedMaterials = looksOf;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var p = holder.transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (first) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    first = false;
                }
            }
            var scale = size / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            model.transform.localScale *= scale;
            model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            return holder;
        }
    }
}
