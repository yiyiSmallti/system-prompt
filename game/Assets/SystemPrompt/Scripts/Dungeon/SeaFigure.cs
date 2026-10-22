using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace SystemPrompt.Dungeon
{
    // A character model from Resources/Models/Characters (made in Tripo from the turnarounds in T-pose,
    // rigged to Mixamo bones, imported as humanoid by Editor/ModelAssetImporter.cs), standing on Root at
    // the given height and facing +z. A small playable graph blends the model's own idle with the shared
    // walk and run from Resources/Models/Motions by speed; the imported look is swapped for the sea's toon
    // shader on the model's base colour texture.
    public sealed class SeaFigure
    {
        const float WalkSpeed = 1.6f;   // m/s where the walk plays at its own pace
        const float RunFrom = 3.2f;     // the run has fully taken over by here
        const float RunSpeed = 6.2f;    // and plays at its own pace here

        static readonly Dictionary<string, AnimationClip> motions = new Dictionary<string, AnimationClip>();

        public readonly Transform Root;
        public readonly Animator Animator;
        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable walk, run;

        public SeaFigure(string model, Transform parent, float height, Color shade, Color rim, Color emission, float outline)
        {
            Root = Parts.Node(model, parent);
            var path = "Models/Characters/" + model;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"找不到角色模型 Resources/{path}");
            var body = Object.Instantiate(prefab, Root, false);
            body.name = "Model";
            Animator = body.GetComponent<Animator>();
            if (Animator == null || Animator.avatar == null || !Animator.avatar.isHuman)
                throw new InvalidOperationException($"角色模型 {model} 没有人形骨骼（看 Editor/ModelAssetImporter.cs 是否生效）");
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var texture = Resources.Load<Texture2D>(path + "_BaseColor");
            if (texture == null) throw new InvalidOperationException($"找不到角色贴图 Resources/{path}_BaseColor");
            var look = SeaArt.Toon(Color.white, shade, rim, emission, outline);
            look.mainTexture = texture;

            // The bind pose's extent, from the meshes' own bounds, gives the scale to the wanted height.
            var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>();
            float low = float.MaxValue, high = float.MinValue;
            foreach (var r in renderers)
            {
                var b = r.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var y = Root.InverseTransformPoint(r.transform.TransformPoint(corner)).y;
                    low = Mathf.Min(low, y);
                    high = Mathf.Max(high, y);
                }
                var looks = new Material[r.sharedMesh.subMeshCount];
                for (var i = 0; i < looks.Length; i++) looks[i] = look;
                r.sharedMaterials = looks;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.updateWhenOffscreen = true; // the bounds follow the pose, not the T of the bind
            }
            if (renderers.Length == 0 || high <= low) throw new InvalidOperationException($"角色模型 {model} 没有蒙皮网格");
            var scale = height / (high - low);
            body.transform.localScale *= scale;
            body.transform.localPosition = new Vector3(0f, -low * scale, 0f);

            graph = PlayableGraph.Create("Figure " + model);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            AnimationPlayableOutput.Create(graph, model, Animator).SetSourcePlayable(mixer);
            Connect(Clip(path), 0);
            walk = Connect(Motion("Walk"), 1);
            run = Connect(Motion("Run"), 2);
            mixer.SetInputWeight(0, 1f);
            graph.Evaluate(0f);
        }

        // speed in metres per second: idle when still, the walk while picking up speed, then the run.
        public void Animate(float dt, float speed)
        {
            var moving = Mathf.Clamp01(speed / WalkSpeed);
            var running = Mathf.Clamp01((speed - WalkSpeed) / (RunFrom - WalkSpeed));
            mixer.SetInputWeight(0, 1f - moving);
            mixer.SetInputWeight(1, moving * (1f - running));
            mixer.SetInputWeight(2, moving * running);
            walk.SetSpeed(Mathf.Clamp(speed / WalkSpeed, 0.7f, 1.5f));
            run.SetSpeed(Mathf.Clamp(speed / RunSpeed, 0.7f, 1.45f));
            graph.Evaluate(dt);
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        AnimationClipPlayable Connect(AnimationClip clip, int input)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            graph.Connect(playable, 0, mixer, input);
            return playable;
        }

        static AnimationClip Motion(string name)
        {
            if (!motions.TryGetValue(name, out var clip) || clip == null)
                motions[name] = clip = Clip("Models/Motions/" + name);
            return clip;
        }

        // The take inside a model file (Tripo exports one per file).
        static AnimationClip Clip(string path)
        {
            foreach (var clip in Resources.LoadAll<AnimationClip>(path))
                if (!clip.name.StartsWith("__preview__")) return clip;
            throw new InvalidOperationException($"Resources/{path} 里没有动作");
        }
    }
}
