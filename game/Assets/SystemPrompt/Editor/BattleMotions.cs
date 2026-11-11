using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // The party's battle motions cut from Quaternius' Universal Animation Library (CC0, Standard: UAL1 and UAL2),
    // whose clips Editor/Motions/Actions holds as taken out of the library's FBX (humanoid, axes baked, so the body
    // already faces ahead and needs no turn round, unlike the nine taken straight from UAL1_Standard.fbx): each the
    // stretch of its clip used, mirrored where it is thrown with the other hand, into Resources/Models/Motions for
    // SeaFigure.Play. Also sets the hit reactions' additive reference to their last frame and reimports the cast's
    // FBX after its cut changes (ModelAssetImporter.Battle). From the menu, or in batchmode with -executeMethod
    // SystemPrompt.EditorTools.BattleMotions.Run.
    static class BattleMotions
    {
        const string Sources = "Assets/SystemPrompt/Editor/Motions/Actions/";
        const string MotionRoot = "Assets/SystemPrompt/Resources/Models/Motions/";

        // name, source, the stretch used in seconds (to 0: up to the end), mirrored, whether the body's travel
        // over the floor stays in the pose (a throw's step, a fall backward) or is dropped so it plays where the
        // figure stands, as the shot's Kneel does, which KneelUp takes over from, and whether it ends (1) or starts
        // (-1) lying sprawled (Sprawl).
        static readonly (string name, string source, float from, float to, bool mirror, bool travel, int sprawl)[] Cuts =
        {
            ("Throw", "OverhandThrow", 0f, 0f, false, true, 0),       // DeepSeek, right-handed
            ("ThrowLeft", "OverhandThrow", 0f, 0f, true, true, 0),    // the player, the phone in the right
            ("KneelUp", "Fixing_Kneeling", 4.2f, 5.2f, false, false, 0), // up off one knee after the low shot
            ("SlashCombo", "Sword_Regular_Combo", 0f, 2f, false, true, 0), // 横斩: three cuts, stopped before the bow
            ("Faint", "Death01", 0f, 0f, false, true, 1),
            ("Knockback", "Hit_Knockback", 0f, 0f, false, true, 1),
            ("GetUp", "LayToIdle", 0f, 0f, false, true, -1),
        };

        // The library lays a fallen figure out straight: legs together, arms at the sides, face to the sky, a plank
        // on the seabed. The falls come to rest sprawled instead, and the get-up starts from it: the left knee drawn
        // up, the legs apart, the left arm thrown out with the elbow bent, the right up by the head, the head rolled
        // to the side. Muscle values the pose is brought to (not added), so both falls and the get-up meet in it.
        static readonly (string muscle, float value)[] Sprawl =
        {
            ("Left Upper Leg Front-Back", 0.05f), ("Left Lower Leg Stretch", 0.2f), ("Left Upper Leg In-Out", 0.12f),
            ("Right Upper Leg In-Out", 0.2f), ("Right Lower Leg Stretch", 0.8f),
            ("Left Arm Down-Up", -0.3f), ("Left Forearm Stretch", 0.3f), ("Left Arm Twist In-Out", -0.6f),
            ("Right Arm Down-Up", 0.7f), ("Right Forearm Stretch", 0.05f), ("Right Arm Twist In-Out", 0.8f),
            ("Head Turn Left-Right", 0.45f), ("Neck Turn Left-Right", 0.25f),
            ("Spine Twist Left-Right", 0.1f),
        };
        const float SprawlRamp = 0.4f; // seconds the pose takes to come in at the end of a fall, or go at the start of the get-up

        // HitChest and HitHead start on the hardest of the blow and end back upright: added over the pose (SeaFigure.
        // Flinch), they take the difference from that last frame, or the jolt would come out backwards.
        static readonly string[] Flinches = { "HitChest", "HitHead" };

        [MenuItem("System Prompt/生成战斗动作片段")]
        public static void Run()
        {
            foreach (var (name, source, from, to, mirror, travel, sprawl) in Cuts)
            {
                var path = Sources + source + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                           ?? throw new System.InvalidOperationException($"找不到 {name} 的来源动作 {path}");
                var length = (to > 0f ? to : clip.length) - from;
                var cut = Cut(clip, name, from, to > 0f ? to : clip.length, mirror, travel);
                if (sprawl != 0) Sprawled(cut, length, sprawl > 0);
                Save(cut);
                Debug.Log($"{name}：来自 {source} {from:F2}–{(to > 0f ? to : clip.length):F2} 秒{(mirror ? "，镜像" : "")}{(travel ? "，保留位移" : "")}"
                          + (sprawl > 0 ? "，结尾瘫倒" : sprawl < 0 ? "，从瘫倒起身" : ""));
            }
            foreach (var name in Flinches)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MotionRoot + name + ".anim");
                AnimationUtility.SetAdditiveReferencePose(clip, clip, clip.length);
                EditorUtility.SetDirty(clip);
                Debug.Log($"{name}：叠加参考取末帧 {clip.length:F2} 秒");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MotionRoot + "Cast.fbx", ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        static AnimationClip Cut(AnimationClip source, string name, float from, float to, bool mirror, bool travel)
        {
            var clip = new AnimationClip { name = name, frameRate = source.frameRate };
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
                AnimationUtility.SetEditorCurve(clip, binding, Span(AnimationUtility.GetEditorCurve(source, binding), from, to));
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = 0f;
            settings.stopTime = to - from;
            settings.loopTime = false;
            settings.loopBlend = false;
            settings.mirror = mirror;
            settings.orientationOffsetY = 0f;
            settings.loopBlendOrientation = true;
            settings.keepOriginalOrientation = true;
            settings.loopBlendPositionY = true;
            settings.keepOriginalPositionY = true;
            settings.loopBlendPositionXZ = travel;
            settings.keepOriginalPositionXZ = travel;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        // The clip's Sprawl muscles brought to the sprawled pose over the last SprawlRamp seconds before its end
        // (`into`: a fall, done a tenth of a second before the frame it is then held on), or let go of it over the
        // first SprawlRamp seconds (the get-up). The curves are sampled a frame at a time.
        static void Sprawled(AnimationClip clip, float length, bool into)
        {
            foreach (var (muscle, value) in Sprawl)
            {
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), muscle);
                var curve = AnimationUtility.GetEditorCurve(clip, binding)
                            ?? throw new System.InvalidOperationException($"{clip.name} 里没有曲线 {muscle}");
                var frames = Mathf.CeilToInt(length * clip.frameRate);
                var keys = new Keyframe[frames + 1];
                for (var i = 0; i <= frames; i++)
                {
                    var t = Mathf.Min(i / clip.frameRate, length);
                    var w = into ? Mathf.SmoothStep(0f, 1f, (t - (length - SprawlRamp - 0.1f)) / SprawlRamp) : 1f - Mathf.SmoothStep(0f, 1f, t / SprawlRamp);
                    keys[i] = new Keyframe(t, Mathf.Lerp(curve.Evaluate(t), value, w));
                }
                Debug.Log($"{clip.name}：{muscle} {curve.Evaluate(into ? length : 0f):F2} → {value:F2}");
                var made = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
                for (var i = 0; i < made.length; i++) made.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, binding, made);
            }
        }

        // The curve between from and to, moved to start at 0, with a key at each end where it has none.
        static AnimationCurve Span(AnimationCurve curve, float from, float to)
        {
            const float Near = 1e-4f;
            Keyframe At(float t)
            {
                const float e = 1e-3f;
                var slope = (curve.Evaluate(t + e) - curve.Evaluate(t - e)) / (2f * e);
                return new Keyframe(t - from, curve.Evaluate(t), slope, slope);
            }
            var keys = new List<Keyframe>();
            foreach (var key in curve.keys)
            {
                if (key.time < from - Near || key.time > to + Near) continue;
                var moved = key;
                moved.time = Mathf.Clamp(key.time - from, 0f, to - from);
                keys.Add(moved);
            }
            if (keys.Count == 0 || keys[0].time > Near) keys.Insert(0, At(from));
            if (keys[keys.Count - 1].time < to - from - Near) keys.Add(At(to));
            return new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        static void Save(AnimationClip clip)
        {
            var path = MotionRoot + clip.name + ".anim";
            var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (old != null)
            {
                EditorUtility.CopySerialized(clip, old);
                EditorUtility.SetDirty(old);
            }
            else AssetDatabase.CreateAsset(clip, path);
        }
    }
}
