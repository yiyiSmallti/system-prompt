using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SystemPrompt.Dungeon;
using UnityEditor;
using UnityEngine;

namespace SystemPrompt.EditorTools
{
    // The battle's fighting stances (Resources/Models/Motions/Stance*.anim) and the poses struck from them in
    // place of a step for 防御, 道具 and 深度思考 (Guard*, Item*, Think*.anim, SeaFigure.Act). Each was drawn
    // first as a pose sheet (assets/characters/stances) and is posed here after it by hand, in the humanoid's
    // muscles, so one pose plays on every model of that kind, stood flat on the floor by Solve: a stance as
    // two keys a breath apart, looped; an action from the stance's first frame into its pose, held a breath and
    // back, once. The girl player's stance is the exception, a library motion fitted to her (MakePlayerFemale);
    // her actions are the boy's poses on her model. The clips carry no foot goals, so SeaFigure plays every
    // motion without foot IK. From the menu, or in batchmode with -executeMethod
    // SystemPrompt.EditorTools.StancePoser.Run; -stancePreview <dir> also draws each model in its stance and
    // holding each action's pose from the front, its right and behind, to set beside the sheets, and each
    // action from start to end in frames; -stanceProbe logs which way each muscle used here turns its bone.
    static class StancePoser
    {
        const string MotionRoot = "Assets/SystemPrompt/Resources/Models/Motions/";

        sealed class Stance
        {
            public string Name;
            public float Seconds;              // one breath (an action's hold)
            public float Rise, Return;         // an action's way into the pose from the stance and back, seconds
            public float Turn;                 // the body turned to its right, degrees
            public float Lean;                 // and tipped forward from the hips, degrees
            public string Back;                // the leg behind ("Left" or "Right"), its knee bent to reach the floor
            public Dictionary<string, float> Out; // the pose as drawn, breathing out
            public Dictionary<string, float> In;  // what breathing in changes
        }

        // Fingers curled round something held (Stretched below 0 curls them), or loose and a little open.
        static void Fingers(Dictionary<string, float> pose, string side, float curl, float spread)
        {
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Little" })
            {
                for (var joint = 1; joint <= 3; joint++) pose[$"{side} {finger} {joint} Stretched"] = curl;
                pose[$"{side} {finger} Spread"] = spread;
            }
            pose[$"{side} Thumb 1 Stretched"] = curl * 0.5f;
            pose[$"{side} Thumb 2 Stretched"] = curl * 0.5f;
            pose[$"{side} Thumb 3 Stretched"] = curl * 0.5f;
        }

        static Dictionary<string, float> With(Dictionary<string, float> pose, Action<Dictionary<string, float>> more)
        {
            more(pose);
            return pose;
        }

        // The player in the lounge clothes, the phone their weapon: an ordinary student squaring up, feet wide
        // and knees soft, the body turned so the left shoulder leads and the head back round to the noise
        // bodies, the left hand up by the chin, the phone low in the right hand. Muscle values from the probe:
        // a thigh hangs straight at Front-Back 0.56 and goes forward 90° a unit below 0, back 50° a unit
        // above; a knee or elbow bends 80° for each unit its Stretch is under 1; a foot pitches 50° a unit
        // (flat on the floor near -0.2 under a straight shin); an arm comes forward as its Front-Back goes
        // below 0 and up as its Down-Up goes above.
        static readonly Stance Player = new Stance
        {
            Name = "StancePlayer",
            Seconds = 2.6f,
            Turn = 15f,
            Back = "Right",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.6f,
                ["Chest Front-Back"] = -0.3f,
                ["Chest Twist Left-Right"] = -0.5f,
                ["Neck Nod Down-Up"] = -0.15f,
                ["Head Nod Down-Up"] = -0.1f,
                ["Neck Turn Left-Right"] = -0.2f,
                ["Head Turn Left-Right"] = -0.2f,
                // Feet wide, the left forward with the knee bent, the right back, straighter and turned out
                // (the feet are set flat and the back knee straightened by Solve).
                ["Left Upper Leg Front-Back"] = 0.1f,
                ["Left Upper Leg In-Out"] = 0.45f,
                ["Left Upper Leg Twist In-Out"] = 0.15f,
                ["Left Lower Leg Stretch"] = 0.42f,
                ["Left Foot Twist In-Out"] = 0.3f,
                ["Right Upper Leg Front-Back"] = 1f,
                ["Right Upper Leg In-Out"] = 0.7f,
                ["Right Upper Leg Twist In-Out"] = 0.3f,
                ["Right Lower Leg Stretch"] = 0.62f,
                ["Right Foot Twist In-Out"] = 0.5f,
                // The guard: the elbow down by the ribs, the forearm up in front, the hand at the chin.
                ["Left Arm Down-Up"] = -0.9f,
                ["Left Arm Front-Back"] = -0.3f,
                ["Left Forearm Stretch"] = -0.3f,
                ["Left Hand Down-Up"] = 0.2f,
                // The phone hand down by the hip, the elbow a little bent.
                ["Right Arm Down-Up"] = -0.5f,
                ["Right Arm Front-Back"] = 0.05f,
                ["Right Forearm Stretch"] = 0.7f,
                ["Right Hand Down-Up"] = -0.2f,
            }, p =>
            {
                Fingers(p, "Right", -0.6f, 0f);
                Fingers(p, "Left", 0.2f, 0.3f);
            }),
            In = new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.5f,
                ["Chest Front-Back"] = -0.15f,
                ["Left Shoulder Down-Up"] = 0.08f,
                ["Right Shoulder Down-Up"] = 0.08f,
                ["Left Forearm Stretch"] = -0.26f,
                ["Head Nod Down-Up"] = -0.05f,
            },
        };

        // DeepSeek in her battle dress, the ice caster: the body turned so the right shoulder leads, the right
        // hand held out open at the noise bodies at chest height, the left loose in front of her chest, feet
        // apart with the right one forward, a little crouched, the head tipped.
        static readonly Stance DeepSeek = new Stance
        {
            Name = "StanceDeepSeek",
            Seconds = 3.2f,
            Turn = -15f,
            Back = "Left",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.4f,
                ["Chest Front-Back"] = -0.2f,
                ["Chest Twist Left-Right"] = 0.3f,
                ["Neck Nod Down-Up"] = -0.05f,
                ["Neck Turn Left-Right"] = 0.2f,
                ["Head Turn Left-Right"] = 0.2f,
                ["Head Tilt Left-Right"] = 0.15f,
                ["Right Upper Leg Front-Back"] = 0.16f,
                ["Right Upper Leg In-Out"] = 0.45f,
                ["Right Lower Leg Stretch"] = 0.5f,
                ["Left Upper Leg Front-Back"] = 0.66f,
                ["Left Upper Leg In-Out"] = 0.45f,
                ["Left Upper Leg Twist In-Out"] = 0.4f,
                ["Left Lower Leg Stretch"] = 0.7f,
                ["Left Foot Twist In-Out"] = 0.2f,
                // The casting hand out level at the noise bodies, the palm to them.
                ["Right Arm Down-Up"] = 0.4f,
                ["Right Arm Front-Back"] = -0.25f,
                ["Right Forearm Stretch"] = 0.85f,
                ["Right Hand Down-Up"] = 0.6f,
                ["Left Arm Down-Up"] = -0.6f,
                ["Left Arm Front-Back"] = -0.45f,
                ["Left Forearm Stretch"] = -0.2f,
            }, p =>
            {
                Fingers(p, "Right", 0.3f, 0.6f);
                Fingers(p, "Left", -0.5f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Chest Front-Back"] = -0.05f,
                ["Left Shoulder Down-Up"] = 0.08f,
                ["Right Shoulder Down-Up"] = 0.08f,
                ["Right Arm Down-Up"] = 0.43f,
                ["Head Tilt Left-Right"] = 0.2f,
            },
        };

        // The player when she is the girl, not posed but taken from Quaternius' Universal Animation Library
        // (CC0, the Standard pack): Spell_Simple_Idle_Loop, the left hand up and open at the noise bodies, the
        // right low by the hip, breathing and shifting her weight, which reads less stiff than two posed keys.
        // The library's stances are a man's: on her the feet stood twice a shoulder apart, so both thighs close
        // in by LegsIn (a muscle's In-Out goes out above 0). The right hand hangs open in the clip, so its
        // fingers are curled round the phone as in the posed stance.
        const string PlayerFemaleSource = "Assets/SystemPrompt/Editor/Motions/Spell_Simple_Idle_Loop.anim";
        const string PlayerFemaleName = "StancePlayerFemale";
        const float LegsIn = -0.12f;

        // ------------------------------------------------------------------ the actions (Name + the stance's kind)

        // 防御, the player (guard-player.png): dropped low with the feet wide, the left shoulder leading and the
        // left forearm up level in front of the face, the head ducked behind it, the phone gripped to the chest.
        static readonly Stance GuardPlayer = new Stance
        {
            Name = "Guard",
            Rise = 0.22f,
            Seconds = 1.2f,
            Return = 0.4f,
            Turn = 25f,
            Lean = 12f,
            Back = "Right",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.8f,
                ["Chest Front-Back"] = -0.7f,
                ["UpperChest Front-Back"] = -0.5f,
                ["Chest Twist Left-Right"] = -0.2f,
                ["Neck Nod Down-Up"] = -0.35f,
                ["Head Nod Down-Up"] = -0.3f,
                ["Neck Turn Left-Right"] = -0.25f,
                ["Head Turn Left-Right"] = -0.25f,
                ["Left Upper Leg Front-Back"] = 0.05f,
                ["Left Upper Leg In-Out"] = 0.5f,
                ["Left Upper Leg Twist In-Out"] = 0.3f,
                ["Left Lower Leg Stretch"] = 0.38f,
                ["Left Foot Twist In-Out"] = 0.3f,
                ["Right Upper Leg Front-Back"] = 1f,
                ["Right Upper Leg In-Out"] = 0.65f,
                ["Right Upper Leg Twist In-Out"] = 0.4f,
                ["Right Lower Leg Stretch"] = 0.55f,
                ["Right Foot Twist In-Out"] = 0.5f,
                // The guarding arm: the elbow up and out, the forearm across in front of the face.
                ["Left Shoulder Down-Up"] = 0.3f,
                ["Left Arm Down-Up"] = 0.3f,
                ["Left Arm Front-Back"] = -0.55f,
                ["Left Forearm Stretch"] = -0.4f,
                // The phone fist in against the chest.
                ["Right Arm Down-Up"] = -0.9f,
                ["Right Arm Front-Back"] = -0.3f,
                ["Right Forearm Stretch"] = -0.25f,
                ["Right Hand Down-Up"] = -0.2f,
            }, p =>
            {
                Fingers(p, "Left", -0.7f, 0f);
                Fingers(p, "Right", -0.6f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.75f,
                ["Neck Nod Down-Up"] = -0.3f,
                ["Left Shoulder Down-Up"] = 0.34f,
                ["Right Shoulder Down-Up"] = 0.06f,
            },
        };

        // 道具, the player (item-player.png): upright, the feet apart and the chest out, the 白米饭 held up
        // overhead in the left hand as a rice ball (SeaFigure.HoldRiceBall), looking up at it, the phone low
        // in the right.
        static readonly Stance ItemPlayer = new Stance
        {
            Name = "Item",
            Rise = 0.35f,
            Seconds = 1.0f,
            Return = 0.4f,
            Turn = 5f,
            Back = "Right",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.2f,
                ["Chest Front-Back"] = 0.2f,
                ["Neck Nod Down-Up"] = 0.35f,
                ["Head Nod Down-Up"] = 0.45f,
                ["Neck Turn Left-Right"] = -0.1f,
                ["Left Upper Leg Front-Back"] = 0.42f,
                ["Left Upper Leg In-Out"] = 0.22f,
                ["Left Lower Leg Stretch"] = 0.9f,
                ["Left Foot Twist In-Out"] = 0.2f,
                ["Right Upper Leg Front-Back"] = 0.48f,
                ["Right Upper Leg In-Out"] = 0.22f,
                ["Right Lower Leg Stretch"] = 0.95f,
                ["Right Foot Twist In-Out"] = 0.2f,
                ["Left Shoulder Down-Up"] = 0.8f,
                ["Left Arm Down-Up"] = 1f,
                ["Left Arm Front-Back"] = -0.15f,
                ["Left Forearm Stretch"] = 0.95f,
                ["Left Hand Down-Up"] = 0.2f,
                ["Right Arm Down-Up"] = -0.5f,
                ["Right Arm Front-Back"] = 0.05f,
                ["Right Forearm Stretch"] = 0.7f,
                ["Right Hand Down-Up"] = -0.2f,
            }, p =>
            {
                Fingers(p, "Left", -0.35f, 0.1f);
                Fingers(p, "Right", -0.6f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.3f,
                ["Neck Nod Down-Up"] = 0.4f,
                ["Left Shoulder Down-Up"] = 0.9f,
            },
        };

        // 防御, DeepSeek (guard-deepseek.png): the right foot forward and the knees bent, leaning back into it,
        // both arms pushed out at chest height, the palms open at the noise bodies and the fingers spread, as if
        // holding up a sheet of ice; the head down a little.
        static readonly Stance GuardDeepSeek = new Stance
        {
            Name = "Guard",
            Rise = 0.22f,
            Seconds = 1.2f,
            Return = 0.4f,
            Turn = -10f,
            Back = "Left",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.3f,
                ["Chest Front-Back"] = 0.1f,
                ["Neck Nod Down-Up"] = -0.2f,
                ["Head Nod Down-Up"] = -0.1f,
                ["Right Upper Leg Front-Back"] = 0.12f,
                ["Right Upper Leg In-Out"] = 0.45f,
                ["Right Upper Leg Twist In-Out"] = 0.1f,
                ["Right Lower Leg Stretch"] = 0.42f,
                ["Left Upper Leg Front-Back"] = 0.7f,
                ["Left Upper Leg In-Out"] = 0.45f,
                ["Left Upper Leg Twist In-Out"] = 0.4f,
                ["Left Lower Leg Stretch"] = 0.65f,
                ["Left Foot Twist In-Out"] = 0.2f,
                ["Left Shoulder Down-Up"] = 0.1f,
                ["Right Shoulder Down-Up"] = 0.1f,
                ["Left Arm Down-Up"] = -0.15f,
                ["Left Arm Front-Back"] = -0.5f,
                ["Left Forearm Stretch"] = 0.95f,
                ["Left Hand Down-Up"] = 0.8f,
                ["Right Arm Down-Up"] = -0.15f,
                ["Right Arm Front-Back"] = -0.5f,
                ["Right Forearm Stretch"] = 0.95f,
                ["Right Hand Down-Up"] = 0.8f,
            }, p =>
            {
                Fingers(p, "Left", 0.6f, 1f);
                Fingers(p, "Right", 0.6f, 1f);
            }),
            In = new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.35f,
                ["Left Forearm Stretch"] = 1f,
                ["Right Forearm Stretch"] = 1f,
            },
        };

        // 道具, DeepSeek (item-deepseek.png), who eats it: the feet together and the knees in, the shoulders up
        // with delight, the rice ball in both hands at her chin, the elbows tucked in.
        static readonly Stance ItemDeepSeek = new Stance
        {
            Name = "Item",
            Rise = 0.35f,
            Seconds = 1.0f,
            Return = 0.4f,
            Turn = -5f,
            Back = "Left",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.3f,
                ["Chest Front-Back"] = -0.2f,
                ["Neck Nod Down-Up"] = -0.2f,
                ["Head Nod Down-Up"] = -0.1f,
                ["Head Tilt Left-Right"] = 0.15f,
                ["Right Upper Leg Front-Back"] = 0.45f,
                ["Right Upper Leg In-Out"] = 0f,
                ["Right Upper Leg Twist In-Out"] = -0.4f,
                ["Right Lower Leg Stretch"] = 0.8f,
                ["Left Upper Leg Front-Back"] = 0.5f,
                ["Left Upper Leg In-Out"] = 0f,
                ["Left Upper Leg Twist In-Out"] = -0.4f,
                ["Left Lower Leg Stretch"] = 0.8f,
                ["Left Shoulder Down-Up"] = 0.45f,
                ["Right Shoulder Down-Up"] = 0.45f,
                ["Left Arm Down-Up"] = -0.6f,
                ["Left Arm Front-Back"] = -0.35f,
                ["Left Forearm Stretch"] = -0.65f,
                ["Left Hand Down-Up"] = 0.1f,
                ["Right Arm Down-Up"] = -0.6f,
                ["Right Arm Front-Back"] = -0.35f,
                ["Right Forearm Stretch"] = -0.65f,
                ["Right Hand Down-Up"] = 0.1f,
            }, p =>
            {
                Fingers(p, "Left", -0.25f, 0f);
                Fingers(p, "Right", -0.25f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Neck Nod Down-Up"] = -0.28f,
                ["Left Shoulder Down-Up"] = 0.5f,
                ["Right Shoulder Down-Up"] = 0.5f,
            },
        };

        // 深度思考 (think-deepseek.png): the feet apart and planted, the eyes shut and the head bowed and tipped
        // to her right, two fingers of the right hand to her temple, the left arm across her middle holding the
        // right elbow.
        static readonly Stance ThinkDeepSeek = new Stance
        {
            Name = "Think",
            Rise = 0.4f,
            Seconds = 1.6f,
            Return = 0.45f,
            Turn = -10f,
            Back = "Left",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = -0.3f,
                ["Chest Front-Back"] = -0.2f,
                ["Neck Nod Down-Up"] = -0.45f,
                ["Head Nod Down-Up"] = -0.2f,
                ["Neck Tilt Left-Right"] = 0.25f,
                ["Head Tilt Left-Right"] = 0.25f,
                ["Right Upper Leg Front-Back"] = 0.45f,
                ["Right Upper Leg In-Out"] = 0.35f,
                ["Right Lower Leg Stretch"] = 0.85f,
                ["Left Upper Leg Front-Back"] = 0.6f,
                ["Left Upper Leg In-Out"] = 0.35f,
                ["Left Upper Leg Twist In-Out"] = 0.2f,
                ["Left Lower Leg Stretch"] = 0.85f,
                ["Right Arm Down-Up"] = 0.25f,
                ["Right Arm Front-Back"] = -0.5f,
                ["Right Arm Twist In-Out"] = 0.4f,
                ["Right Forearm Stretch"] = -0.85f,
                ["Right Hand Down-Up"] = 0.2f,
                ["Left Arm Down-Up"] = -0.55f,
                ["Left Arm Front-Back"] = -0.15f,
                ["Left Arm Twist In-Out"] = -0.7f,
                ["Left Forearm Stretch"] = -0.05f,
            }, p =>
            {
                // The index and middle fingers straight to the temple, the others and the thumb folded.
                Fingers(p, "Right", -0.8f, 0f);
                foreach (var finger in new[] { "Index", "Middle" })
                    for (var joint = 1; joint <= 3; joint++) p[$"Right {finger} {joint} Stretched"] = 0.6f;
                Fingers(p, "Left", -0.3f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Neck Nod Down-Up"] = -0.5f,
                ["Spine Front-Back"] = -0.35f,
            },
        };

        [MenuItem("System Prompt/生成战斗待机动作")]
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            if (args.Contains("-stanceProbe")) Probe();
            Make(Player, "PlayerMale");
            Make(DeepSeek, "DeepSeek");
            MakePlayerFemale();
            // After the stances, which they start and end on.
            MakeAction(GuardPlayer, "PlayerMale", "Player");
            MakeAction(GuardPlayer, "PlayerFemale", "PlayerFemale");
            MakeAction(GuardDeepSeek, "DeepSeek", "DeepSeek");
            MakeAction(ItemPlayer, "PlayerMale", "Player");
            MakeAction(ItemPlayer, "PlayerFemale", "PlayerFemale");
            MakeAction(ItemDeepSeek, "DeepSeek", "DeepSeek");
            MakeAction(ThinkDeepSeek, "DeepSeek", "DeepSeek");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var preview = Arg("-stancePreview");
            if (preview == null) return;
            Directory.CreateDirectory(preview);
            var holder = new GameObject("Stance Preview");
            try
            {
                SeaFigure Male() => SeaFigure.Player("male", false, holder.transform);
                SeaFigure Female() => SeaFigure.Player("female", false, holder.transform);
                SeaFigure Her() => SeaFigure.DeepSeek(true, holder.transform);
                Preview(Male(), Path.Combine(preview, "male.png"));
                Preview(Female(), Path.Combine(preview, "female.png"));
                Preview(Her(), Path.Combine(preview, "deepseek.png"));
                // The strips from ahead and out to the side the action is done on: the player's left, DeepSeek's right.
                foreach (var (file, figure, action, side) in new (string, Func<SeaFigure>, Stance, float)[]
                {
                    ("guard-male", Male, GuardPlayer, -1f), ("guard-female", Female, GuardPlayer, -1f), ("guard-deepseek", Her, GuardDeepSeek, 1f),
                    ("item-male", Male, ItemPlayer, -1f), ("item-female", Female, ItemPlayer, -1f), ("item-deepseek", Her, ItemDeepSeek, 1f),
                    ("think-deepseek", Her, ThinkDeepSeek, 1f),
                })
                {
                    Preview(figure(), Path.Combine(preview, file + ".png"), action);
                    Strip(figure, Path.Combine(preview, file + "-strip.png"), action, side);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        // ------------------------------------------------------------------ the clips

        static void Make(Stance stance, string model)
        {
            var (outPose, inPose, ground) = Poses(stance, model);
            var t = stance.Seconds;
            var clip = Keyed(stance.Name, (0f, outPose), (t / 2f, inPose), (t, outPose));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            Save(clip);
            Debug.Log($"{stance.Name}：{stance.Seconds} 秒一呼吸，身体高 {outPose.bodyPosition.y:F3}（站直 {ground.Body:F3}）");
        }

        // An action on one kind of figure (<Name><kind>.anim): from the first frame of the kind's stance into
        // the pose, held a breath (breathing in halfway), and back to that frame, for SeaFigure.Act to play once
        // over the stance.
        static void MakeAction(Stance action, string model, string kind)
        {
            var name = action.Name + kind;
            var stancePath = MotionRoot + "Stance" + kind + ".anim";
            var stance = AssetDatabase.LoadAssetAtPath<AnimationClip>(stancePath)
                         ?? throw new InvalidOperationException($"{name}：找不到它开始和结束的战斗待机 {stancePath}");
            var from = PoseAt(stance, 0f);
            var (outPose, inPose, _) = Poses(action, model);
            Debug.Log($"{name}：待机身体朝向 {from.bodyRotation.eulerAngles}，姿势 {outPose.bodyRotation.eulerAngles}");
            // The body's turn the short way round from the stance's.
            if (Quaternion.Dot(from.bodyRotation, outPose.bodyRotation) < 0f) from.bodyRotation = new Quaternion(-from.bodyRotation.x, -from.bodyRotation.y, -from.bodyRotation.z, -from.bodyRotation.w);
            float up = action.Rise, down = up + action.Seconds;
            var clip = Keyed(name, (0f, from), (up, outPose), ((up + down) / 2f, inPose), (down, outPose), (down + action.Return, from));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            // The body's turn, height and place baked into the pose as in the stances, not taken out as root
            // motion (which SeaFigure throws away, leaving the body where the stance under it has it).
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            Save(clip);
            Debug.Log($"{name}：起 {action.Rise} 秒，保持 {action.Seconds} 秒，收 {action.Return} 秒（{model}，身体高 {outPose.bodyPosition.y:F3}）");
        }

        // The pose breathing out and in on the model, stood on the floor, and how its feet stand in its idle.
        static (HumanPose outPose, HumanPose inPose, Ground ground) Poses(Stance stance, string model)
        {
            var muscles = HumanTrait.MuscleName;
            foreach (var name in stance.Out.Keys.Concat(stance.In.Keys))
                if (Array.IndexOf(muscles, name) < 0) throw new InvalidOperationException($"{stance.Name}：没有这块肌肉「{name}」");

            var body = Instantiate(model, out var animator);
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                var ground = StandingFeet(model, animator, handler);
                var outPose = Solve(stance, stance.Out, animator, handler, ground);
                var inValues = new Dictionary<string, float>(stance.Out);
                foreach (var kv in stance.In) inValues[kv.Key] = kv.Value;
                var inPose = Solve(stance, inValues, animator, handler, ground);
                handler.SetHumanPose(ref outPose);
                {
                    Vector3 P(HumanBodyBones bone) => animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                    float M(string m) => outPose.muscles[Array.IndexOf(muscles, m)];
                    Debug.Log($"解算 {stance.Name} {model}：左踝 {P(HumanBodyBones.LeftFoot)} 右踝 {P(HumanBodyBones.RightFoot)}，站地踝高 {ground.Ankles:F3}；膝 左 {M("Left Lower Leg Stretch"):F2} 右 {M("Right Lower Leg Stretch"):F2}，脚 左 {M("Left Foot Up-Down"):F2} 右 {M("Right Foot Up-Down"):F2}");
                }
                // Breathing in lifts the body a little with the chest.
                inPose.bodyPosition += Vector3.up * 0.006f;
                return (outPose, inPose, ground);
            }
            finally
            {
                handler.Dispose();
                UnityEngine.Object.DestroyImmediate(body);
            }
        }

        // A humanoid clip through these poses at these times, eased in and out of each.
        static AnimationClip Keyed(string name, params (float time, HumanPose pose)[] keys)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            void Curve(string property, Func<HumanPose, float> value)
            {
                var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.time, value(k.pose))).ToArray());
                for (var i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                }
                clip.SetCurve("", typeof(Animator), property, curve);
            }
            var muscles = HumanTrait.MuscleName;
            for (var i = 0; i < muscles.Length; i++)
            {
                var m = i;
                Curve(CurveName(muscles[i]), p => p.muscles[m]);
            }
            Curve("RootT.x", p => p.bodyPosition.x);
            Curve("RootT.y", p => p.bodyPosition.y);
            Curve("RootT.z", p => p.bodyPosition.z);
            Curve("RootQ.x", p => p.bodyRotation.x);
            Curve("RootQ.y", p => p.bodyRotation.y);
            Curve("RootQ.z", p => p.bodyRotation.z);
            Curve("RootQ.w", p => p.bodyRotation.w);
            return clip;
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

        static void MakePlayerFemale()
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerFemaleSource);
            if (source == null) throw new InvalidOperationException($"找不到女主角战斗待机的来源动作 {PlayerFemaleSource}");
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = PlayerFemaleName;
            AnimationCurve Get(string property) =>
                AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property))
                ?? throw new InvalidOperationException($"{PlayerFemaleSource} 里没有曲线 {property}");
            void Set(string property, AnimationCurve curve) =>
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);

            foreach (var side in new[] { "Left", "Right" })
            {
                var property = $"{side} Upper Leg In-Out";
                var curve = Get(property);
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++) keys[i].value += LegsIn;
                curve.keys = keys;
                Set(property, curve);
            }
            var fist = new Dictionary<string, float>();
            Fingers(fist, "Right", -0.6f, 0f);
            foreach (var kv in fist) Set(CurveName(kv.Key), AnimationCurve.Constant(0f, clip.length, kv.Value));
            Save(clip);
            Debug.Log($"{PlayerFemaleName}：来自 {source.name}，{clip.length:F2} 秒一循环，双腿内收 {LegsIn}，右手握拳");
        }

        // The muscle's name as an animation curve calls it: the fingers' go under their hand.
        static string CurveName(string muscle)
        {
            foreach (var side in new[] { "Left", "Right" })
                foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    var prefix = $"{side} {finger} ";
                    if (muscle.StartsWith(prefix)) return $"{side}Hand.{finger}.{muscle.Substring(prefix.Length)}";
                }
            return muscle;
        }

        // Where the feet stand in the model's own idle (its first frame, flat on the floor): how high the
        // ankles are, how far each ankle is above the ball of its foot, and the body's height; the idle's
        // curves read straight into a pose. The idle is the one SeaFigure plays: Motions/Idle<model> for a
        // model rigged without one (DeepSeek's battle dress), else the take in its file.
        struct Ground
        {
            public float Ankles, Lift, Body;
        }

        static Ground StandingFeet(string model, Animator animator, HumanPoseHandler handler)
        {
            var idle = Resources.Load<AnimationClip>("Models/Motions/Idle" + model)
                ?? Resources.LoadAll<AnimationClip>("Models/Characters/" + model).First(c => !c.name.StartsWith("__preview__"));
            var pose = PoseAt(idle, 0f);
            Debug.Log($"{model} 的待机 {idle.name}：身体朝向 {pose.bodyRotation.eulerAngles}");
            handler.SetHumanPose(ref pose);
            float Y(HumanBodyBones b) => Height(animator, b);
            return new Ground
            {
                Ankles = Ankles(animator),
                Lift = (Y(HumanBodyBones.LeftFoot) - Y(HumanBodyBones.LeftToes) + Y(HumanBodyBones.RightFoot) - Y(HumanBodyBones.RightToes)) / 2f,
                Body = pose.bodyPosition.y,
            };
        }

        // A humanoid clip's pose at a time, read straight from its curves.
        static HumanPose PoseAt(AnimationClip clip, float time)
        {
            float At(string property)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property));
                if (curve == null) throw new InvalidOperationException($"{clip.name} 里没有曲线 {property}");
                return curve.Evaluate(time);
            }
            return new HumanPose
            {
                muscles = HumanTrait.MuscleName.Select(m => At(CurveName(m))).ToArray(),
                bodyPosition = new Vector3(At("RootT.x"), At("RootT.y"), At("RootT.z")),
                bodyRotation = new Quaternion(At("RootQ.x"), At("RootQ.y"), At("RootQ.z"), At("RootQ.w")),
            };
        }

        static float Height(Animator animator, HumanBodyBones bone) => animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position).y;

        static float Ankles(Animator animator) => Mathf.Min(Height(animator, HumanBodyBones.LeftFoot), Height(animator, HumanBodyBones.RightFoot));

        // The pose with these muscles, turned, and stood on the floor: each foot pitched flat, the body
        // lowered until the front ankle is as high as in the idle, and the back knee bent or straightened
        // until that foot is down too, a few times over since each of these moves the others.
        static HumanPose Solve(Stance stance, Dictionary<string, float> values, Animator animator, HumanPoseHandler handler, Ground ground)
        {
            var muscles = HumanTrait.MuscleName;
            var pose = new HumanPose { muscles = new float[muscles.Length], bodyRotation = Quaternion.Euler(stance.Lean, stance.Turn, 0f), bodyPosition = new Vector3(0f, ground.Body, 0f) };
            foreach (var kv in values) pose.muscles[Array.IndexOf(muscles, kv.Key)] = kv.Value;
            float With(Action change, Func<float> measure)
            {
                change();
                handler.SetHumanPose(ref pose);
                return measure();
            }
            HumanBodyBones Foot(string side) => side == "Left" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            HumanBodyBones Toes(string side) => side == "Left" ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            for (var round = 0; round < 5; round++)
            {
                foreach (var side in new[] { "Left", "Right" })
                {
                    var i = Array.IndexOf(muscles, side + " Foot Up-Down");
                    pose.muscles[i] = Search(v => With(() => pose.muscles[i] = v, () => Height(animator, Foot(side)) - Height(animator, Toes(side))), -1f, 1f, ground.Lift);
                }
                var front = stance.Back == "Left" ? "Right" : "Left";
                pose.bodyPosition.y = Search(y => With(() => pose.bodyPosition.y = y, () => Height(animator, Foot(front))), ground.Body - 0.6f, ground.Body + 0.2f, ground.Ankles);
                var k = Array.IndexOf(muscles, stance.Back + " Lower Leg Stretch");
                pose.muscles[k] = Search(v => With(() => pose.muscles[k] = v, () => Height(animator, Foot(stance.Back))), -0.3f, 1f, ground.Ankles);
            }
            handler.SetHumanPose(ref pose);
            var miss = Height(animator, Foot(stance.Back)) - ground.Ankles;
            if (Mathf.Abs(miss) > 0.005f) Debug.LogWarning($"{stance.Name}：后脚够不到地面，差 {miss:F3}");
            return pose;
        }

        // Where between lo and hi f comes to target (f going one way between them), or the end nearer it.
        static float Search(Func<float, float> f, float lo, float hi, float target)
        {
            float a = f(lo) - target, b = f(hi) - target;
            if (a * b > 0f) return Mathf.Abs(a) < Mathf.Abs(b) ? lo : hi;
            for (var i = 0; i < 30; i++)
            {
                var mid = (lo + hi) / 2f;
                var m = f(mid) - target;
                if (m * a > 0f)
                {
                    lo = mid;
                    a = m;
                }
                else hi = mid;
            }
            return (lo + hi) / 2f;
        }

        static GameObject Instantiate(string model, out Animator animator)
        {
            var prefab = Resources.Load<GameObject>("Models/Characters/" + model);
            var body = UnityEngine.Object.Instantiate(prefab);
            animator = body.GetComponent<Animator>();
            return body;
        }

        // ------------------------------------------------------------------ checking

        // Each muscle used in the stances at -1 and at 1, everything else at 0: where its bone then points, in
        // the body's own space (x its right, y up, z ahead), and for a stretch how far the joint is bent.
        static void Probe()
        {
            var body = Instantiate("PlayerMale", out var animator);
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var muscles = HumanTrait.MuscleName;
            var log = new StringBuilder("肌肉探测（PlayerMale，身体坐标 x 右 y 上 z 前）\n");
            (HumanBodyBones from, HumanBodyBones to) BoneOf(string m)
            {
                var left = m.StartsWith("Left");
                HumanBodyBones L(HumanBodyBones l, HumanBodyBones r) => left ? l : r;
                if (m.Contains("Upper Leg")) return (L(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg), L(HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg));
                if (m.Contains("Lower Leg")) return (L(HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg), L(HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot));
                if (m.Contains("Foot")) return (L(HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot), L(HumanBodyBones.LeftToes, HumanBodyBones.RightToes));
                if (m.Contains("Shoulder")) return (L(HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder), L(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm));
                if (m.Contains(" Arm ")) return (L(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm), L(HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm));
                if (m.Contains("Forearm")) return (L(HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm), L(HumanBodyBones.LeftHand, HumanBodyBones.RightHand));
                if (m.Contains("Hand")) return (L(HumanBodyBones.LeftHand, HumanBodyBones.RightHand), L(HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal));
                if (m.StartsWith("Spine")) return (HumanBodyBones.Spine, HumanBodyBones.Neck);
                if (m.StartsWith("Chest") || m.StartsWith("UpperChest")) return (HumanBodyBones.Chest, HumanBodyBones.Neck);
                if (m.StartsWith("Neck") || m.StartsWith("Head")) return (HumanBodyBones.Neck, HumanBodyBones.Head);
                return (HumanBodyBones.Hips, HumanBodyBones.Head);
            }
            string Dir(Vector3 v) => $"({v.x:+0.00;-0.00},{v.y:+0.00;-0.00},{v.z:+0.00;-0.00})";
            var probe = new[]
            {
                "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right", "Chest Front-Back", "Chest Twist Left-Right",
                "Neck Nod Down-Up", "Neck Turn Left-Right", "Head Nod Down-Up", "Head Turn Left-Right", "Head Tilt Left-Right",
                "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out", "Left Lower Leg Stretch", "Left Foot Up-Down",
                "Right Upper Leg Front-Back", "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out", "Right Lower Leg Stretch",
                "Left Shoulder Down-Up", "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out", "Left Forearm Stretch", "Left Forearm Twist In-Out", "Left Hand Down-Up", "Left Hand In-Out",
                "Right Arm Down-Up", "Right Arm Front-Back", "Right Forearm Stretch",
                "Left Index 1 Stretched", "Left Index Spread", "Left Foot Twist In-Out", "Right Foot Up-Down", "Right Foot Twist In-Out",
                "Right Shoulder Down-Up", "Right Arm Twist In-Out", "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out",
                "Chest Left-Right", "Neck Tilt Left-Right", "UpperChest Front-Back", "UpperChest Twist Left-Right",
            };
            var pose = new HumanPose { muscles = new float[muscles.Length], bodyRotation = Quaternion.identity, bodyPosition = Vector3.up };
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Quaternion turned = Quaternion.identity;
            Vector3 Point(HumanBodyBones a, HumanBodyBones b)
            {
                handler.SetHumanPose(ref pose);
                turned = Quaternion.Inverse(animator.transform.rotation) * animator.GetBoneTransform(a).rotation;
                var d = animator.GetBoneTransform(b).position - animator.GetBoneTransform(a).position;
                return animator.transform.InverseTransformDirection(d).normalized;
            }
            string Spin(Quaternion from, Quaternion to)
            {
                (to * Quaternion.Inverse(from)).ToAngleAxis(out var angle, out var axis);
                if (angle > 180f) angle -= 360f;
                return $"{angle:F0}° 绕 {Dir(axis)}";
            }
            foreach (var m in probe)
            {
                var i = Array.IndexOf(muscles, m);
                if (i < 0)
                {
                    log.AppendLine($"  ?? {m} 不存在");
                    continue;
                }
                var (a, b) = BoneOf(m);
                Array.Clear(pose.muscles, 0, pose.muscles.Length);
                var zero = Point(a, b);
                var zeroTurn = turned;
                pose.muscles[i] = 1f;
                var plus = Point(a, b);
                log.AppendLine($"  {m}: 0 {Dir(zero)} → +1 {Dir(plus)}，+1 转 {Spin(zeroTurn, turned)}");
            }
            Array.Clear(pose.muscles, 0, pose.muscles.Length);
            Debug.Log(log.ToString());
            handler.Dispose();
            UnityEngine.Object.DestroyImmediate(body);
        }

        // The figure in its stance, or holding an action's pose a moment after it is reached, from the front, its
        // right and behind, side by side on grey, 512 × 768 each.
        static void Preview(SeaFigure figure, string file, Stance action = null)
        {
            figure.Stance();
            if (action != null)
            {
                Act(figure, action);
                figure.Animate(action.Rise + 0.15f, 0f);
            }
            else figure.Animate(0f, 0f);
            DungeonWorld.ApplySea(0f, Vector3.zero, 9f);
            using (var sheet = new Sheet(512, 768, 3))
            {
                foreach (var view in new[] { Vector3.forward, Vector3.right, Vector3.back }) sheet.Shoot(view);
                sheet.Save(file);
            }
            figure.Dispose();
            UnityEngine.Object.DestroyImmediate(figure.Root.gameObject);
            Debug.Log($"战斗姿势预览：{file}");
        }

        // The action from start to end in 9 frames, seen from ahead and out to the given side (1 its right, -1
        // its left), 256 × 384 each. Each frame is a new figure taken there in one step: rendered again in the
        // same call, a figure keeps the skinning of its first shot while its bones (and the phone) move on.
        static void Strip(Func<SeaFigure> make, string file, Stance action, float side)
        {
            const int Frames = 9;
            var length = action.Rise + action.Seconds + action.Return;
            DungeonWorld.ApplySea(0f, Vector3.zero, 9f);
            using (var sheet = new Sheet(256, 384, Frames))
            {
                for (var i = 0; i < Frames; i++)
                {
                    var figure = make();
                    figure.Stance();
                    Act(figure, action);
                    figure.Animate(length * i / (Frames - 1), 0f);
                    sheet.Shoot(Vector3.forward + Vector3.right * side);
                    figure.Dispose();
                    UnityEngine.Object.DestroyImmediate(figure.Root.gameObject);
                }
                sheet.Save(file);
            }
            Debug.Log($"战斗动作胶片：{file}");
        }

        // As BattleStage does it: the 道具 action holds the rice ball until the pose is let go.
        static float Act(SeaFigure figure, Stance action)
        {
            var length = figure.Act(action.Name);
            if (action.Name == "Item") figure.HoldRiceBall(action.Rise + action.Seconds);
            return length;
        }

        // Shots of whatever is posed at the origin, left to right on grey, each from a way round it, orthographic
        // so they set beside the drawn sheets.
        sealed class Sheet : IDisposable
        {
            readonly Camera cam;
            readonly RenderTexture rt;
            readonly Texture2D sheet;
            readonly int w, h;
            int shots;

            public Sheet(int w, int h, int count)
            {
                this.w = w;
                this.h = h;
                cam = new GameObject("Preview Camera").AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 1.15f; // room for an arm held up overhead
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.78f, 0.79f, 0.8f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 20f;
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                sheet = new Texture2D(w * count, h, TextureFormat.RGB24, false);
            }

            public void Shoot(Vector3 from)
            {
                var way = from.normalized;
                var centre = new Vector3(0f, 1f, 0f);
                cam.transform.SetPositionAndRotation(centre + way * 6f, Quaternion.LookRotation(-way));
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(w, h, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = null;
                sheet.SetPixels(shots++ * w, 0, w, h, shot.GetPixels());
                UnityEngine.Object.DestroyImmediate(shot);
            }

            public void Save(string file) => File.WriteAllBytes(file, sheet.EncodeToPNG());

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(sheet);
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
