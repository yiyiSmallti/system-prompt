using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SystemPrompt.Dungeon;
using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // The walk, run and sprint every figure moves with and the stand of each that explores (SeaFigure), made from
    // Quaternius' Universal Animation Library loops (CC0, Standard; Editor/Motions/Gaits holds them as taken out of
    // the library's FBX). The library's are a tall man's. Its walk is a stroll of half-metre steps that had to play
    // at twice its pace. Its jog and its sprint both cover 2.5 m a step with the foot on the ground for a third of
    // it: played so the feet don't slip, a girl of 1.55 m went by like a triple jumper, and played faster the
    // planted foot skated backward.
    // Each gait has its stride resized (the thighs' swing scaled about its middle, the knees' fold about the
    // straight leg; the shoulders' and hips' swing, the arms' and the lean can be scaled too), and the body is then
    // stood back on the floor: on each model, every frame in which the library's loop has a foot down has the lower
    // foot put exactly on the floor again (ankle or toes, whichever touches), and the frames in the air between keep
    // their arc over that, scaled by Bob. The run and the sprint are then retimed (Stance): the part of each step
    // in which the foot is pressed to the ground is played slower and the flight between faster, so the foot is
    // down for about half a step, as it is at the speeds the party moves at, and goes back under the body as fast
    // as the ground does at a turnover of three steps a second and more. What comes out is logged with its pace:
    // how fast the pressed foot goes back per metre of the avatar's human scale, the speed SeaFigure plays it at
    // its own rate for.
    // From the menu, or in batchmode with -executeMethod SystemPrompt.EditorTools.GaitMotions.Run; with
    // -gaitPreview <folder> (GaitMotions.Preview) nothing is saved: every stand is drawn from three sides and every
    // gait over its loop from the side and from behind.
    static class GaitMotions
    {
        const string Sources = "Assets/SystemPrompt/Editor/Motions/Gaits/";
        const string MotionRoot = "Assets/SystemPrompt/Resources/Models/Motions/";
        // The models the feet are put on the floor for; what the loop needs is averaged over them. The first is the
        // one a changed lean and the retiming are worked out on.
        static readonly string[] Models = { "PlayerFemaleLounge", "PlayerMaleLounge", "DeepSeekUniform", "DeepSeek" };
        const float Down = 0.025f; // metres off the floor within which the library's loop has a foot planted
        const float Scale = 0.95f; // the figures' human scale as sized, about: what the logged speeds are worked out with

        sealed class Gait
        {
            public string Name, Source;
            public float Speed;       // metres a second the party goes at it (DungeonWorld), for the logged numbers
            public float Swing = 1f;  // the thighs' swing, about its middle
            public float Fold = 1f;   // the knees' fold, about the straight leg
            public float Bob = 1f;    // the body's rise in the air between steps (on the floor the legs decide its height)
            public float Turn = 1f;   // the hips' and the shoulders' swing from side to side
            public float Arms = 1f;   // the arms' swing (the shoulders' with it, up to a quarter more)
            public float Elbows;      // added to the forearms' Stretch: above 0 straightens the arms
            public float ArmsIn;      // added to the upper arms' Down-Up: below 0 brings the elbows in to the body's sides
            public float ReachFront, ReachBack; // degrees the thigh swings ahead of and behind straight down at most (eased in from 0.6 of it); 0: as the loop has it
            public float StanceKnees = 1f;      // how much of the knee's bend is kept while its foot is on the floor: below 1 the body rides higher over it
            public float Track;       // added to the thighs' In-Out: above 0 sets the feet down wider apart
            public float Straightest = 2f; // the most a knee's Stretch comes to (1 is a straight leg), eased in over the last 0.12 of it: below 1 the leg never locks
            public float Knees;       // added to the knees' Stretch: below 0 keeps them a little bent where the loop straightens them
            public float Hands = float.NaN; // every finger's Stretched held at this (the loops run with fists, -0.9); NaN: as the loop has it
            public float Lean = 1f;   // the body's lean forward; the thighs are turned back to where they were
            public float Upright;     // added to the chest's Front-Back muscles: above 0 straightens the back
            public float Nod;         // added to the head's Nod Down-Up and half of it to the neck's: below 0 lowers the chin
            public float Pitch = 1f;  // how much of the chest's, neck's and head's swing forward and back within a step is kept
            public float Stance;      // the part of a step the foot is pressed to the ground for, retimed to; 0 as the loop has it
        }

        static readonly Gait[] Gaits =
        {
            // A brisker walk: longer steps, so 1.6 m/s is 2.6 steps a second and not 3 (longer still and the hips
            // start to bounce); the library's walker looks at the floor with rounded shoulders, so the back is
            // straightened and the chin lifted. Its arms hardly swing, bent with the fists at the hips: they swing
            // 1.7 times as far and hang straighter.
            new Gait { Name = "Walk", Source = "Walk_Loop", Speed = 1.6f, Swing = 1.15f, Arms = 1.7f, Elbows = 0.2f, Upright = 0.1f, Nod = 0.2f },
            // The run is the library's sprint taken down to a run. Its plunge forward (25 degrees, for a dash) is stood
            // up to a jog's and the back straightened; the chin, which the sprinter keeps on the track a few metres
            // ahead, is lifted. The sprinter throws its thighs 75 degrees ahead and 37 behind and sits deep into every
            // step: at a run that was a leap out of a squat, so the swing is eased off past 42 and 30 degrees and the
            // knee on the floor keeps two thirds of its bend, while the body's rise in the air is kept whole and a
            // little over (cut down, she rode highest over the planted leg and lowest in the air, feet skimming the
            // floor: a glide, the wrong way up for a run); and it lands on a leg swung out straight, so the knee is
            // never let quite straighten (Straightest: about 22 degrees of bend left). Its elbows stand out from the
            // body with the fists up at the chest: they are brought in and folded a little more (opened further, the
            // forearm reached out ahead at each swing as if rowing), and the hands hang half open. The foot is down for
            // half of each step. The sprinter's chest and head also pitch forward and back with every step, the head
            // some 17 degrees and the chest as much again: stood upright at a run that is a nod into the collar at
            // each footfall, so under half of that swing is kept (Pitch). The sprinter also swings its hips 40 degrees round
            // the upright and rolls them 21 degrees from side to side every step, the upper chest twisting hard back
            // against them: at a run, seen from behind, that is a waddle, the waist wringing at each footfall. Under half of
            // it is kept (Turn), about what a runner's pelvis does.
            new Gait { Name = "Run", Source = "Sprint_Loop", Speed = 4.6f, Bob = 1.1f, Lean = 0.5f, Turn = 0.45f, Upright = 0.16f, Nod = 0.12f, Pitch = 0.45f, Stance = 0.55f,
                ReachFront = 42f, ReachBack = 30f, StanceKnees = 0.65f, Straightest = 0.72f, Track = 0.03f, Arms = 1.1f, ArmsIn = -0.2f, Elbows = -0.12f, Hands = -0.45f },
            // The sprint: the same loop leaning nearly as the library has it and reaching further, the body rising as
            // it does, the hips swinging a little further than at the run. One loop in two carriages, so the two blend in
            // step (SeaFigure keeps them at one time).
            new Gait { Name = "Sprint", Source = "Sprint_Loop", Speed = 6.6f, Lean = 0.85f, Turn = 0.6f, Upright = 0.08f, Nod = 0.1f, Stance = 0.5f,
                ReachFront = 60f, ReachBack = 34f, StanceKnees = 0.8f, Track = 0.03f, Arms = 1.2f, ArmsIn = -0.1f, Hands = -0.6f },
        };

        // The standing idle of each figure that explores (Motions/Idle<model>, which SeaFigure takes before the take
        // in the model's own file): the library's Idle_Loop, a relaxed stand with one foot ahead and the arms hanging
        // a little bent, where the generated models' own idle stood to attention with the feet together. Per model:
        // how far both thighs close in (the library's stance is a man's, too wide for the girls) and how far the
        // chest is straightened out of the library's slouch. The loop stands 17 degrees side on with the head turned
        // back; it is turned to face ahead, the head with it (the head is SeaFigure's to turn anyway). Its hands are
        // fists: they are opened to hang loose. As the library has it the stand is half a step: one foot well ahead
        // and turned out, the other on its toes behind, the elbows bent and the wrists turned in, which on these
        // figures read as stopped mid-stride with hooked hands. So the feet are brought nearly level (Stagger of the
        // stride kept), the knees and the elbows straightened, the wrists let hang, and both feet laid flat on one
        // floor again (Idle works that out on each model).
        sealed class Standing
        {
            public string Model;
            public float LegsIn;  // added to both thighs' In-Out: below 0 closes the stance
            public float Upright; // added to the chest's Front-Back, 1.6 times it to the upper chest's
            public float ArmsOut; // added to both arms' Down-Up: below 0 lets them hang nearer the body's side (the library holds them well off it)
        }

        static readonly Standing[] Idles =
        {
            new Standing { Model = "PlayerFemaleLounge", LegsIn = -0.16f, Upright = 0.12f, ArmsOut = -0.08f },
            new Standing { Model = "PlayerMaleLounge", LegsIn = -0.12f, Upright = 0.1f, ArmsOut = -0.12f },
            new Standing { Model = "DeepSeekUniform", LegsIn = -0.16f, Upright = 0.12f, ArmsOut = -0.08f },
            new Standing { Model = "DeepSeek", LegsIn = -0.16f, Upright = 0.12f }, // her battle dress, following through the deep; its skirt is wide
        };
        const float Stagger = 0.3f;      // of the library's one foot ahead of the other
        const float Knees = 0.1f;        // added to both knees' Stretch
        const float RightToeIn = -0.25f; // added to the right thigh's twist: its foot pointed 40 degrees out

        [MenuItem("System Prompt/生成行走跑步动作")]
        public static void Run()
        {
            foreach (var gait in Gaits) Save(Build(gait));
            foreach (var standing in Idles) Save(Idle(standing));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void Save(AnimationClip clip)
        {
            var saved = MotionRoot + clip.name + ".anim";
            var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(saved);
            if (old != null)
            {
                EditorUtility.CopySerialized(clip, old);
                EditorUtility.SetDirty(old);
            }
            else AssetDatabase.CreateAsset(clip, saved);
        }

        // Gaits being tried against the ones above: drawn by Preview beside them, never saved. Empty when nothing is.
        static readonly Gait[] Trials = { };

        // Every stand from three sides, and every gait (and trial) over its loop on the girl and the boy, twelve frames
        // from the side, from behind and from ahead. With -gaitOnly <prefix>, only the gaits whose name starts with it,
        // and no stands.
        public static void Preview()
        {
            var args = Environment.GetCommandLineArgs();
            var dir = args[Array.IndexOf(args, "-gaitPreview") + 1];
            Directory.CreateDirectory(dir);
            const int Shots = 12;
            DungeonWorld.ApplySea(0f, Vector3.zero, 9f);
            var only = Array.IndexOf(args, "-gaitOnly") >= 0 ? args[Array.IndexOf(args, "-gaitOnly") + 1] : null;
            if (only == null) PreviewIdles(dir);
            foreach (var gait in Gaits.Concat(Trials))
            {
                if (only != null && !gait.Name.StartsWith(only)) continue;
                var clip = Build(gait);
                // A new model for every shot: drawn again in the same call, one keeps the skinning of its first shot.
                foreach (var model in new[] { "PlayerFemaleLounge", "PlayerMaleLounge" })
                    foreach (var (view, from) in new[] { ("side", Vector3.right), ("back", Vector3.back), ("front", Vector3.forward) })
                        using (var sheet = new StancePoser.Sheet(256, 384, Shots))
                        {
                            for (var i = 0; i < Shots; i++)
                            {
                                var body = StancePoser.Instantiate(model, out var animator);
                                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                                var pose = StancePoser.PoseAt(clip, clip.length * i / Shots);
                                handler.SetHumanPose(ref pose);
                                sheet.Shoot(from);
                                UnityEngine.Object.DestroyImmediate(body);
                            }
                            sheet.Save(Path.Combine(dir, $"{gait.Name}-{model}-{view}.png"));
                        }
            }
        }

        // Each idle from ahead, the side and behind at the loop's start, and from ahead a third and two thirds of the
        // way round.
        static void PreviewIdles(string dir)
        {
            foreach (var standing in Idles)
                using (var sheet = new StancePoser.Sheet(384, 576, 5))
                {
                    var model = standing.Model;
                    var clip = Idle(standing);
                    foreach (var (from, at) in new[] { (Vector3.forward, 0f), (Vector3.right, 0f), (Vector3.back, 0f), (Vector3.forward, 1f / 3f), (Vector3.forward, 2f / 3f) })
                    {
                        var body = StancePoser.Instantiate(model, out var animator);
                        var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                        var pose = StancePoser.PoseAt(clip, clip.length * at);
                        handler.SetHumanPose(ref pose);
                        sheet.Shoot(from);
                        UnityEngine.Object.DestroyImmediate(body);
                    }
                    sheet.Save(Path.Combine(dir, $"Idle-{model}.png"));
                }
        }

        static AnimationClip Idle(Standing standing)
        {
            var model = standing.Model;
            var library = AssetDatabase.LoadAssetAtPath<AnimationClip>(Sources + "Idle_Loop.anim")
                          ?? throw new InvalidOperationException($"找不到待机的来源动作 {Sources}Idle_Loop.anim");
            const int Samples = 10;
            float Mean(string property)
            {
                var curve = AnimationUtility.GetEditorCurve(library, EditorCurveBinding.FloatCurve("", typeof(Animator), property))
                            ?? throw new InvalidOperationException($"Idle_Loop 里没有曲线 {property}");
                return Enumerable.Range(0, Samples).Average(i => curve.Evaluate(library.length * i / Samples));
            }
            // The foot ahead and the foot behind brought toward each other: each thigh moved toward the middle of the two.
            var apart = (Mean("Right Upper Leg Front-Back") - Mean("Left Upper Leg Front-Back")) * 0.5f * (1f - Stagger);
            var clip = StancePoser.Fitted(library, "Idle" + model, standing.LegsIn, false, false, new[]
            {
                ("Chest Front-Back", standing.Upright), ("UpperChest Front-Back", standing.Upright * 1.6f), ("Neck Nod Down-Up", -standing.Upright),
                ("Head Turn Left-Right", 0.28f),
                ("Left Upper Leg Front-Back", apart), ("Right Upper Leg Front-Back", -apart),
                ("Left Lower Leg Stretch", Knees), ("Right Lower Leg Stretch", Knees),
                ("Right Upper Leg Twist In-Out", RightToeIn),
                ("Left Arm Down-Up", standing.ArmsOut), ("Right Arm Down-Up", standing.ArmsOut),
                ("Left Forearm Stretch", 0.1f), ("Right Forearm Stretch", 0.22f),
                ("Left Hand In-Out", 0.1f), ("Right Hand In-Out", 0.1f),
            }, -17.25f);
            void Hold(string property, float value) =>
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), AnimationCurve.Constant(0f, library.length, value));
            void Add(string property, float add)
            {
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++) keys[i].value += add;
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            // The fingers hang open and a little apart, each curled more than the last from the index to the little one.
            foreach (var hand in new[] { "LeftHand", "RightHand" })
            {
                foreach (var (finger, open, spread) in new[] { ("Index", 0.4f, 0.1f), ("Middle", 0.28f, 0f), ("Ring", 0.16f, 0.1f), ("Little", 0.05f, 0.2f) })
                {
                    foreach (var joint in new[] { "1", "2", "3" }) Hold($"{hand}.{finger}.{joint} Stretched", open);
                    Hold($"{hand}.{finger}.Spread", spread);
                }
                Hold($"{hand}.Thumb.1 Stretched", 0.3f);
                Hold($"{hand}.Thumb.2 Stretched", 0.4f);
                Hold($"{hand}.Thumb.3 Stretched", 0.4f);
            }
            using (var stand = new Stand(model))
            {
                var sides = new[] { "Left", "Right" };
                (float off, float tilt) Foot(int foot)
                {
                    float off = 0f, tilt = 0f;
                    for (var i = 0; i < Samples; i++)
                    {
                        var (o, t) = stand.Foot(clip, library.length * i / Samples, foot);
                        off += o / Samples;
                        tilt += t / Samples;
                    }
                    return (off, tilt);
                }
                // What `measure` reads brought to nothing by adding to a muscle: its slope from a trial step, a few times over.
                void Solve(Func<float> measure, string muscle, float step)
                {
                    for (var round = 0; round < 4; round++)
                    {
                        var has = measure();
                        if (Mathf.Abs(has) < 0.001f) return;
                        Add(muscle, step);
                        var slope = (measure() - has) / step;
                        Add(muscle, -step);
                        if (Mathf.Abs(slope) < 1e-3f) return;
                        Add(muscle, Mathf.Clamp(-has / slope, -0.4f, 0.4f));
                    }
                }
                // With the thighs moved the feet no longer lie flat, nor at one height. Each foot is laid flat by its
                // ankle; then the leg that reaches lower bends its knee until both are on one floor; then flat again.
                void Flat()
                {
                    for (var f = 0; f < 2; f++)
                    {
                        var foot = f;
                        Solve(() => Foot(foot).tilt, $"{sides[f]} Foot Up-Down", 0.1f);
                    }
                }
                Flat();
                var lower = Foot(0).off < Foot(1).off ? 0 : 1;
                Solve(() => Foot(1 - lower).off - Foot(lower).off, $"{sides[lower]} Lower Leg Stretch", -0.05f);
                Flat();
                // Stood on the floor: the lower foot's sole where it is in the model's file, on average over the loop.
                var off = 0f;
                for (var i = 0; i < Samples; i++) off += stand.Lower(clip, library.length * i / Samples).off / Samples;
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++) keys[i].value -= off / stand.Scale;
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                var (leftOff, leftTilt) = Foot(0);
                var (rightOff, rightTilt) = Foot(1);
                Debug.Log($"Idle{model}：来自 Idle_Loop，一圈 {library.length:F2} 秒，双腿内收 {-standing.LegsIn}，挺胸 {standing.Upright}，脚离地 {off * 100f:F1} cm 已校正；"
                          + $"校正后左脚离地 {leftOff * 100f:F1} cm、脚尖翘 {leftTilt * 100f:F1} cm，右脚离地 {rightOff * 100f:F1} cm、脚尖翘 {rightTilt * 100f:F1} cm");
            }
            return clip;
        }

        // A model stood up to be posed and measured.
        sealed class Stand : IDisposable
        {
            readonly GameObject body;
            readonly Animator animator;
            readonly HumanPoseHandler handler;
            readonly Transform[] feet, toes;
            readonly float[] ankle, ball; // as the model stands in its file: the feet flat on the floor
            public float Scale => animator.humanScale;

            public Stand(string model)
            {
                body = StancePoser.Instantiate(model, out animator);
                handler = new HumanPoseHandler(animator.avatar, animator.transform);
                feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
                toes = new[] { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
                ankle = feet.Select(f => In(f).y).ToArray();
                ball = toes.Select(t => t != null ? In(t).y : 0f).ToArray();
            }

            Vector3 In(Transform t) => animator.transform.InverseTransformPoint(t.position);

            // How far the lower foot is off the floor (below 0: through it), which foot it is, and where its ankle is.
            public (float off, int foot, Vector3 at) Lower(AnimationClip clip, float time)
            {
                var pose = StancePoser.PoseAt(clip, time);
                handler.SetHumanPose(ref pose);
                var best = (off: float.MaxValue, foot: 0, at: Vector3.zero);
                for (var f = 0; f < 2; f++)
                {
                    var off = In(feet[f]).y - ankle[f];
                    if (toes[f] != null) off = Mathf.Min(off, In(toes[f]).y - ball[f]);
                    if (off < best.off) best = (off, f, In(feet[f]));
                }
                return best;
            }

            // How far a foot goes back under the body between two times of a loop (for Slip): its ankle while that is
            // the part on the floor at the first, the ball of the foot once the heel has lifted.
            public float Back(AnimationClip clip, float from, float to, int foot) => Back(StancePoser.PoseAt(clip, from), StancePoser.PoseAt(clip, to), foot);

            public float Back(HumanPose from, HumanPose to, int foot)
            {
                handler.SetHumanPose(ref from);
                var onBall = toes[foot] != null && In(toes[foot]).y - ball[foot] < In(feet[foot]).y - ankle[foot];
                var point = onBall ? toes[foot] : feet[foot];
                var was = In(point).z;
                handler.SetHumanPose(ref to);
                return was - In(point).z;
            }

            // One foot in a pose: how far its ankle is over where it stands in the model's file, and how far its toes
            // are up from lying flat with it (below 0: dug in).
            public (float off, float tilt) Foot(AnimationClip clip, float time, int foot)
            {
                var pose = StancePoser.PoseAt(clip, time);
                handler.SetHumanPose(ref pose);
                var at = In(feet[foot]).y;
                return (at - ankle[foot], toes[foot] != null ? In(toes[foot]).y - at - (ball[foot] - ankle[foot]) : 0f);
            }

            public float Hips(AnimationClip clip, float time)
            {
                var pose = StancePoser.PoseAt(clip, time);
                handler.SetHumanPose(ref pose);
                return In(animator.GetBoneTransform(HumanBodyBones.Hips)).y;
            }

            // Degrees the thigh points ahead of straight down, seen from the side, in a pose.
            public float Thigh(HumanPose pose, bool left)
            {
                handler.SetHumanPose(ref pose);
                var thigh = In(animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg))
                            - In(animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg));
                return Mathf.Atan2(thigh.z, -thigh.y) * Mathf.Rad2Deg;
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(body);
        }

        // The samples of a loop in which one foot stays planted (within `floor` of the floor) and goes back under the
        // body at least three tenths as fast as its fastest: pressed to the ground, not hanging ahead about to land. With them which foot is the lower in each
        // sample, and how fast the pressed foot goes back, per metre of the model's human scale.
        static (bool[] pressed, int[] foot, float pace) Pressed(Stand stand, AnimationClip clip, int samples, float floor)
        {
            var length = clip.length;
            var lower = Enumerable.Range(0, samples).Select(i => stand.Lower(clip, length * i / samples)).ToArray();
            var step = new float[samples];
            var fastest = 0f;
            for (var i = 0; i < samples; i++)
            {
                var next = (i + 1) % samples;
                if (lower[i].off >= floor || lower[next].off >= floor || lower[i].foot != lower[next].foot) continue;
                step[i] = lower[i].at.z - lower[next].at.z;
                fastest = Mathf.Max(fastest, step[i]);
            }
            var pressed = step.Select(s => fastest > 0f && s >= fastest * 0.3f).ToArray();
            var count = pressed.Count(p => p);
            var pace = count > 0 ? step.Where((s, i) => pressed[i]).Sum() / (count * length / samples) / stand.Scale : 0f;
            return (pressed, lower.Select(l => l.foot).ToArray(), pace);
        }

        static AnimationClip Build(Gait gait)
        {
            var path = Sources + gait.Source + ".anim";
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                         ?? throw new InvalidOperationException($"找不到 {gait.Name} 的来源动作 {path}");
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = gait.Name;
            var length = source.length;
            var frames = Mathf.RoundToInt(length * source.frameRate);
            float At(int i) => length * i / frames;

            AnimationCurve Get(string property) =>
                AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property))
                ?? throw new InvalidOperationException($"{source.name} 里没有曲线 {property}");
            // A key a frame, the loop's last equal to its first.
            void Set(string property, Func<int, float> value)
            {
                var keys = new Keyframe[frames + 1];
                for (var i = 0; i <= frames; i++) keys[i] = new Keyframe(At(i), value(i % frames));
                var curve = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
                for (var i = 0; i <= frames; i++) curve.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);
            }
            float[] Sampled(string property)
            {
                var curve = Get(property);
                return Enumerable.Range(0, frames).Select(i => curve.Evaluate(At(i))).ToArray();
            }
            // The curve scaled about `about` of its own values (its mean, or its highest), then moved.
            void Scale(string property, float by, Func<float[], float> about, float add = 0f)
            {
                if (by == 1f && add == 0f) return;
                var v = Sampled(property);
                var middle = about(v);
                Set(property, i => middle + (v[i] - middle) * by + add);
            }
            float Mean(float[] v) => v.Average();
            float Top(float[] v) => v.Max();

            // The body's turn: its lean (x) scaled, its swing about the up axis (y) and its roll (z) scaled with the
            // shoulders', w whatever keeps it a rotation. Leaning less swings the legs forward with the body, so each
            // thigh is turned back until it points where the library had it (worked out on the first model).
            if (gait.Lean != 1f || gait.Turn != 1f)
            {
                float[] x = Sampled("RootQ.x"), y = Sampled("RootQ.y"), z = Sampled("RootQ.z"), w = Sampled("RootQ.w");
                var turned = new Quaternion[frames];
                for (var i = 0; i < frames; i++)
                {
                    var q = new Vector3(x[i] * gait.Lean, y[i] * gait.Turn, z[i] * gait.Turn);
                    turned[i] = new Quaternion(q.x, q.y, q.z, Mathf.Sign(w[i]) * Mathf.Sqrt(Mathf.Max(0f, 1f - q.sqrMagnitude)));
                }
                Set("RootQ.x", i => turned[i].x);
                Set("RootQ.y", i => turned[i].y);
                Set("RootQ.z", i => turned[i].z);
                Set("RootQ.w", i => turned[i].w);
            }
            // Each thigh turned until it points where the library had it (worked out on the first model): leaning less
            // swings the legs forward with the body. The library's sprinter throws its legs 66 degrees ahead and 46
            // behind, a split in the air that at a run reads as a leap; past ReachFront and ReachBack the swing is
            // eased off, the part of it on the floor (the middle of the swing) left as it is.
            if (gait.Lean != 1f || gait.ReachFront > 0f || gait.ReachBack > 0f)
                using (var stand = new Stand(Models[0]))
                {
                    var muscles = HumanTrait.MuscleName;
                    foreach (var left in new[] { true, false })
                    {
                        var name = $"{(left ? "Left" : "Right")} Upper Leg Front-Back";
                        var index = Array.IndexOf(muscles, name);
                        var value = Sampled(name);
                        float ahead = float.MinValue, behind = float.MaxValue;
                        for (var i = 0; i < frames; i++)
                        {
                            var want = stand.Thigh(StancePoser.PoseAt(source, At(i)), left);
                            ahead = Mathf.Max(ahead, want);
                            behind = Mathf.Min(behind, want);
                            want = want >= 0f ? Within(want, gait.ReachFront) : -Within(-want, gait.ReachBack);
                            var pose = StancePoser.PoseAt(clip, At(i));
                            for (var pass = 0; pass < 4; pass++)
                            {
                                pose.muscles[index] = value[i];
                                var has = stand.Thigh(pose, left);
                                pose.muscles[index] = value[i] + 0.05f;
                                var slope = (stand.Thigh(pose, left) - has) / 0.05f;
                                if (Mathf.Abs(slope) < 1f) break;
                                value[i] += Mathf.Clamp((want - has) / slope, -0.5f, 0.5f);
                            }
                        }
                        Set(name, i => value[i]);
                        if (left) Debug.Log($"{gait.Name}：库里的大腿最多前摆 {ahead:F0}°、后摆 {-behind:F0}°，限制到 {Within(ahead, gait.ReachFront):F0}° 和 {Within(-behind, gait.ReachBack):F0}°");
                    }
                }
            foreach (var side in new[] { "Left", "Right" })
            {
                Scale($"{side} Upper Leg Front-Back", gait.Swing, Mean);
                Scale($"{side} Lower Leg Stretch", gait.Fold, Top, gait.Knees);
                Scale($"{side} Arm Down-Up", 1f, Mean, gait.ArmsIn);
                Scale($"{side} Upper Leg In-Out", 1f, Mean, gait.Track);
                Scale($"{side} Arm Front-Back", gait.Arms, Mean);
                Scale($"{side} Shoulder Front-Back", Mathf.Min(gait.Arms, 1.25f), Mean);
                Scale($"{side} Forearm Stretch", 1f, Mean, gait.Elbows);
            }
            foreach (var part in new[] { "Spine", "Chest", "UpperChest" })
            {
                Scale($"{part} Twist Left-Right", gait.Turn, Mean);
                if (part != "Spine") Scale($"{part} Front-Back", gait.Pitch, Mean, gait.Upright);
            }
            if (!float.IsNaN(gait.Hands))
                foreach (var hand in new[] { "LeftHand", "RightHand" })
                    foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                        foreach (var joint in new[] { "1", "2", "3" })
                            Set($"{hand}.{finger}.{joint} Stretched", i => finger == "Thumb" ? Mathf.Max(gait.Hands, -0.2f) : gait.Hands);
            Scale("Head Nod Down-Up", gait.Pitch, Mean, gait.Nod);
            Scale("Neck Nod Down-Up", gait.Pitch, Mean, gait.Nod * 0.5f);
            // While a foot is on the floor (as the library has it, on the first model) its knee is straightened by a share
            // of its bend, eased in and out over two frames: the sprinter sits deep into each step, and at a run that is
            // a squat. The body's height follows below, where the feet are put back on the floor.
            if (gait.StanceKnees < 1f)
                using (var stand = new Stand(Models[0]))
                {
                    var low = Enumerable.Range(0, frames).Select(i => stand.Lower(source, At(i))).ToArray();
                    for (var f = 0; f < 2; f++)
                    {
                        var name = $"{(f == 0 ? "Left" : "Right")} Lower Leg Stretch";
                        var bend = Sampled(name);
                        var on = low.Select(l => l.off < Down && l.foot == f ? 1f : 0f).ToArray();
                        float On(int i) => on[(i % frames + frames) % frames];
                        var down = Enumerable.Range(0, frames).Select(i => (On(i - 2) + 2f * On(i - 1) + 3f * On(i) + 2f * On(i + 1) + On(i + 2)) / 9f).ToArray();
                        Set(name, i => bend[i] + (1f - bend[i]) * (1f - gait.StanceKnees) * down[i]);
                    }
                }
            // With the legs reshaped, a foot on the floor no longer goes back under the body evenly (it slid 15 cm and more
            // within a step): over the stretch it is pressed (as the library has it, the stretch Retimed then draws out
            // evenly), each frame's thigh is turned until
            // the foot has gone back as far as in every other, the whole way as it was; what that turned the last by
            // fades over the two frames after, as the leg swings free.
            // The loop swings its leg out straight before it lands, and the knee then gives all at once: a stamp. Kept a
            // little bent all the way round, it comes down already giving.
            if (gait.Straightest < 1.2f)
                foreach (var side in new[] { "Left", "Right" })
                {
                    var stretch = Sampled($"{side} Lower Leg Stretch");
                    var from = gait.Straightest - 0.12f;
                    Set($"{side} Lower Leg Stretch", i => stretch[i] <= from ? stretch[i] : from + 0.12f * (float)Math.Tanh((stretch[i] - from) / 0.12f));
                }
            var stretches = gait.Stance > 0f ? Stretches(source, frames) : null;
            if (stretches != null)
                using (var stand = new Stand(Models[0]))
                {
                    var muscles = HumanTrait.MuscleName;
                    for (var f = 0; f < 2; f++)
                    {
                        var (first, last) = stretches[f];
                        var name = $"{(f == 0 ? "Left" : "Right")} Upper Leg Front-Back";
                        var index = Array.IndexOf(muscles, name);
                        var value = Sampled(name);
                        var was = (float[])value.Clone();
                        HumanPose Posed(int frame)
                        {
                            var pose = StancePoser.PoseAt(clip, At(frame % frames));
                            pose.muscles[index] = value[frame % frames];
                            return pose;
                        }
                        var whole = 0f;
                        for (var j = first; j <= last; j++) whole += stand.Back(Posed(j), Posed(j + 1), f);
                        var each = whole / (last + 1 - first);
                        for (var j = first; j <= last; j++)
                        {
                            var to = (j + 1) % frames;
                            for (var pass = 0; pass < 5; pass++)
                            {
                                var has = stand.Back(Posed(j), Posed(j + 1), f);
                                if (Mathf.Abs(has - each) < 0.0005f) break;
                                value[to] += 0.03f;
                                var slope = (stand.Back(Posed(j), Posed(j + 1), f) - has) / 0.03f;
                                value[to] -= 0.03f;
                                if (Mathf.Abs(slope) < 1e-3f) break;
                                value[to] += Mathf.Clamp((each - has) / slope, -0.3f, 0.3f);
                            }
                        }
                        var turned = value[(last + 1) % frames] - was[(last + 1) % frames];
                        value[(last + 2) % frames] += turned * 2f / 3f;
                        value[(last + 3) % frames] += turned / 3f;
                        Set(name, i => value[i]);
                    }
                }
            var height = Sampled("RootT.y");

            // Onto the floor: what each frame's body height has to change by, averaged over the models.
            var change = new float[frames];
            var report = new List<string>();
            foreach (var model in Models)
                using (var stand = new Stand(model))
                {
                    var was = Enumerable.Range(0, frames).Select(i => stand.Lower(source, At(i))).ToArray();
                    var now = Enumerable.Range(0, frames).Select(i => stand.Lower(clip, At(i))).ToArray();
                    var planted = was.Select(c => c.off < Down).ToArray();
                    if (!planted.Any(p => p)) throw new InvalidOperationException($"{gait.Name}：{model} 上来源动作没有一帧脚着地");
                    var need = new float[frames];
                    for (var i = 0; i < frames; i++)
                    {
                        if (planted[i])
                        {
                            need[i] = -now[i].off / stand.Scale;
                            continue;
                        }
                        // In the air: between what the planted frames either side need, the library's rise over them
                        // scaled, and never through the floor.
                        int before = i, after = i;
                        while (!planted[(before % frames + frames) % frames]) before--;
                        while (!planted[after % frames]) after++;
                        int b0 = (before % frames + frames) % frames, a0 = after % frames;
                        var t = (i - before) / (float)(after - before);
                        var ends = Mathf.Lerp(-now[b0].off / stand.Scale, -now[a0].off / stand.Scale, t);
                        var arc = height[i] - Mathf.Lerp(height[b0], height[a0], t); // the library's rise over the straight line between its landings
                        need[i] = Mathf.Max(ends + arc * (gait.Bob - 1f), -now[i].off / stand.Scale);
                    }
                    for (var i = 0; i < frames; i++) change[i] += need[i] / Models.Length;
                    report.Add($"{model} 原动作离地 {was.Where((c, i) => planted[i]).Min(c => c.off) * 100f:F1}–{was.Where((c, i) => planted[i]).Max(c => c.off) * 100f:F1} cm、"
                               + $"改后校正前 {now.Where((c, i) => planted[i]).Min(c => c.off) * 100f:F1}–{now.Where((c, i) => planted[i]).Max(c => c.off) * 100f:F1} cm"
                               + $"（着地 {planted.Count(p => p)}/{frames} 帧）");
                }
            Set("RootT.y", i => height[i] + change[i]);
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));

            if (gait.Stance > 0f) clip = Retimed(clip, gait.Stance, frames, stretches);

            // What it comes to, measured on the loop as made.
            const int Samples = 80;
            float pace = 0f, share = 0f, rise, lift = 0f;
            var slips = new List<string>();
            foreach (var model in Models)
                using (var stand = new Stand(model))
                {
                    var (pressed, foot, own) = Pressed(stand, clip, Samples, Down + 0.015f);
                    pace += own / Models.Length;
                    slips.Add($"{model} {Slip(stand, clip, Samples, pressed, foot) * 100f:F1} cm");
                    share += pressed.Count(p => p) / (float)Samples / Models.Length; // each step is half the loop, and one foot's
                }
            using (var stand = new Stand(Models[0]))
            {
                var hips = Enumerable.Range(0, Samples).Select(i => stand.Hips(clip, length * i / Samples)).ToArray();
                rise = hips.Max() - hips.Min();
                // A run rides lowest over the planted leg and highest in the air: the other way up it glides.
                var (down, _, _) = Pressed(stand, clip, Samples, Down + 0.015f);
                if (down.Any(d => d) && down.Any(d => !d))
                    lift = hips.Where((h, i) => !down[i]).Average() - hips.Where((h, i) => down[i]).Average();
            }
            var rate = gait.Speed / (pace * GaitMotions.Scale);
            var steps = 2f * rate / length;
            Debug.Log($"{gait.Name}：来自 {gait.Source}，一圈 {length:F3} 秒；步伐 {pace:F2}/米（SeaFigure 的 {gait.Name}Pace），脚踩实占每步的 {share:P0}，髋起伏 {rise * 100f:F1} cm（腾空时比踩地时高 {lift * 100f:F1} cm）；"
                      + $"{gait.Speed:0.0} m/s 时 {rate:F2} 倍速，每秒 {steps:F1} 步，每步 {gait.Speed / steps:F2} 米；按这个步伐走，踩实的脚在地上最多滑：{string.Join("、", slips)}\n  "
                      + string.Join("\n  ", report));
            return clip;
        }

        // The loop with each foot pressed to the ground for `stance` of a step: the stretch in which it is pressed
        // (worked out on the first model) played slower to fill it, the flight between played faster, both about the
        // middle of the stretch, so the loop keeps its length and its two steps stay half a loop apart. Every curve
        // is read where the retiming puts it, twice as many keys as the loop had frames (the flight goes by faster).
        // How far a pressed foot slides over the ground within one step, the figure going at the loop's own pace: the
        // widest its place on the ground strays from where it was set down (by the part of the foot that is down,
        // Stand.Back), in metres at the model's size.
        static float Slip(Stand stand, AnimationClip clip, int samples, bool[] pressed, int[] foot)
        {
            var length = clip.length;
            var count = pressed.Count(p => p);
            if (count == 0) return 0f;
            var back = new float[samples];
            for (var i = 0; i < samples; i++)
                if (pressed[i]) back[i] = stand.Back(clip, length * i / samples, length * ((i + 1) % samples) / samples, foot[i]);
            var mean = back.Sum() / count;
            var worst = 0f;
            for (var start = 0; start < samples; start++)
            {
                if (!pressed[start] || pressed[(start + samples - 1) % samples]) continue;
                float at = 0f, low = 0f, high = 0f;
                for (var i = start; i < start + samples && pressed[i % samples]; i++)
                {
                    at += back[i % samples] - mean;
                    low = Mathf.Min(low, at);
                    high = Mathf.Max(high, at);
                }
                worst = Mathf.Max(worst, high - low);
            }
            return worst;
        }

        // Degrees held within `most`: as they are up to 0.6 of it, then easing toward it. 0: no limit.
        static float Within(float degrees, float most) =>
            most <= 0f || degrees <= 0.6f * most ? degrees : most * (0.6f + 0.4f * (float)Math.Tanh((degrees - 0.6f * most) / (0.4f * most)));

        // The stretch of a loop each foot (left, right) is pressed to the ground for, on the first model: from frame
        // `first` to the frame after `last` (which may run past the loop's end).
        static (int first, int last)[] Stretches(AnimationClip clip, int frames)
        {
            var found = new (int, int)[2];
            using (var stand = new Stand(Models[0]))
            {
                var (pressed, foot, _) = Pressed(stand, clip, frames, Down + 0.015f);
                for (var f = 0; f < 2; f++)
                {
                    var on = Enumerable.Range(0, frames).Select(i => pressed[i] && foot[i] == f).ToArray();
                    if (!on.Any(o => o) || on.All(o => o)) throw new InvalidOperationException($"{clip.name}：找不到{(f == 0 ? "左" : "右")}脚踩实的一段");
                    var first = 0;
                    while (!(on[first % frames] && !on[(first + frames - 1) % frames])) first++;
                    var last = first;
                    while (on[(last + 1) % frames]) last++;
                    found[f] = (first, last);
                }
            }
            return found;
        }

        static AnimationClip Retimed(AnimationClip clip, float stance, int frames, (int first, int last)[] stretches)
        {
            var length = clip.length;
            var points = new List<(float to, float from)>(); // a time in the retimed loop and the time in the loop it shows, both in loops
            for (var f = 0; f < 2; f++)
            {
                var (first, last) = stretches[f];
                float middle = (first + last + 1) * 0.5f / frames, has = (last + 1 - first) / (float)frames, wants = stance * 0.5f;
                points.Add((middle - wants * 0.5f, middle - has * 0.5f));
                points.Add((middle + wants * 0.5f, middle + has * 0.5f));
                Debug.Log($"{clip.name}：{(f == 0 ? "左" : "右")}脚踩实 {first}–{last + 1} 帧（共 {frames} 帧），占每步的 {has * 2f:P0} → {stance:P0}");
            }
            points.Sort((a, b) => a.to.CompareTo(b.to));
            points.Add((points[0].to + 1f, points[0].from + 1f));
            float Straight(float u)
            {
                var turns = Mathf.Floor(u - points[0].to);
                u -= turns;
                for (var i = 0; i + 1 < points.Count; i++)
                    if (u <= points[i + 1].to)
                        return turns + Mathf.Lerp(points[i].from, points[i + 1].from, Mathf.InverseLerp(points[i].to, points[i + 1].to, u));
                return turns + points[points.Count - 1].from;
            }
            // Eased over a fiftieth of the loop either side, so the legs don't change speed in a frame.
            float Where(float u)
            {
                var sum = 0f;
                for (var k = -2; k <= 2; k++) sum += Straight(u + k * 0.01f) - k * 0.01f;
                return sum / 5f;
            }

            var made = new AnimationClip { name = clip.name, frameRate = clip.frameRate * 2f };
            var count = frames * 2;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = new Keyframe[count + 1];
                for (var i = 0; i <= count; i++) keys[i] = new Keyframe(length * i / count, curve.Evaluate(Mathf.Repeat(Where((i % count) / (float)count), 1f) * length));
                var retimed = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
                for (var i = 0; i <= count; i++) retimed.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(made, binding, retimed);
            }
            AnimationUtility.SetAnimationClipSettings(made, AnimationUtility.GetAnimationClipSettings(clip));
            return made;
        }
    }
}
