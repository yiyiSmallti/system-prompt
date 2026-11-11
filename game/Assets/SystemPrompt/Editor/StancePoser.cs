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
    // place of a step for 防御, 道具 and 深度思考 (Guard*, Item*, Think*.anim, SeaFigure.Act). The stances are
    // library idles fitted to each model (Library, MakeStance); the actions were drawn first as pose sheets
    // (assets/characters/stances) and are posed here after them by hand, in the humanoid's muscles, so one
    // pose plays on every model of that kind, stood flat on the floor by Solve: from the stance's first frame
    // into the pose, held a breath and back, once. The girl player's actions are the boy's poses on her model.
    // The clips carry no foot goals, so SeaFigure plays every motion without foot IK. From the menu, or in
    // batchmode with -executeMethod
    // SystemPrompt.EditorTools.StancePoser.Run; -stancePreview <dir> also draws each model in its stance and
    // holding each action's pose from the front, its right and behind, to set beside the sheets, and each
    // action from start to end in frames; -stanceProbe logs which way each muscle used here turns its bone.
    static class StancePoser
    {
        const string MotionRoot = "Assets/SystemPrompt/Resources/Models/Motions/";

        sealed class Stance
        {
            public string Name;
            // An action's way into the pose from the stance, one breath held there, and back, seconds: SeaFigure's,
            // which holds the pose by them (Act).
            public float Rise => SeaFigure.PoseTimes[Name].Rise;
            public float Seconds => SeaFigure.PoseTimes[Name].Seconds;
            public float Return => SeaFigure.PoseTimes[Name].Return;
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

        // Another figure's take on an action: its pose as drawn with some muscles changed, breathing the same.
        static Stance Varied(Stance from, Action<Dictionary<string, float>> more) => new Stance
        {
            Name = from.Name,
            Turn = from.Turn,
            Lean = from.Lean,
            Back = from.Back,
            Out = With(new Dictionary<string, float>(from.Out), more),
            In = from.In,
        };

        // ------------------------------------------------------------------ the stances

        // The stances are not posed but taken from Quaternius' Universal Animation Library (CC0, the Standard
        // pack), which breathes and shifts its weight where two posed keys stood stiff (Editor/Motions holds the
        // clips as imported). The player, the phone their weapon, takes Pistol_Idle_Loop: the phone held out in
        // both hands at the noise bodies like a camera about to shoot, the right hand gripping it (its fingers
        // curled round the phone, the clip's hanging open), the left under it; boy and girl alike. DeepSeek, the
        // ice caster, takes Spell_Simple_Idle_Loop mirrored, so the hand held out is
        // her right, the one her spells leave and the rice paddle appears in (BattleStage.Hand, SeaFigure.Wield),
        // and her right foot leads as in her posed actions. The library's stances are a man's: on the VRoid
        // models the feet stand too far apart, so both thighs close in by each model's legsIn (a muscle's In-Out
        // goes out above 0; the girl's 0.12 stood twice a shoulder apart, the boy's wider hips need half). The hands
        // are brought in by adding to their muscles over the whole loop: DeepSeek's open right hand came up past her
        // shoulder and read from behind as a wave, so it is lowered to her chest, the elbow bent and the wrist back,
        // the palm at the foe; the players' hands are put on the phone at run time (SeaFigure.Ready).
        const string MotionSources = "Assets/SystemPrompt/Editor/Motions/";
        static readonly (string name, string source, float legsIn, bool phoneFist, bool mirror, float yaw, (string muscle, float add)[] offsets)[] Library =
        {
            // Both players hold the phone before the chest (SeaFigure.Ready puts the hands on it): the girl stood in
            // the casting idle with both arms straight out ahead, a sleepwalker seen from the side. The idle leans back
            // from the hips; the spine and chest bring them a little forward, onto the front foot.
            ("StancePlayer", "Pistol_Idle_Loop", -0.06f, true, false, 0f, new[]
            {
                ("Spine Front-Back", -0.1f), ("Chest Front-Back", -0.1f),
            }),
            ("StancePlayerFemale", "Pistol_Idle_Loop", -0.12f, true, false, 0f, new[]
            {
                ("Spine Front-Back", -0.1f), ("Chest Front-Back", -0.1f),
            }),
            // Her casting arm is not held out straight like a signpost: the elbow bends and drops, the palm stays up
            // at the foe.
            ("StanceDeepSeek", "Spell_Simple_Idle_Loop", -0.12f, false, true, 28f, new[]
            {
                ("Right Arm Down-Up", -0.3f), ("Right Arm Front-Back", -0.34f),
                ("Right Forearm Stretch", -0.5f), ("Right Hand Down-Up", 0.3f),
                ("Chest Twist Left-Right", 0.18f), ("UpperChest Twist Left-Right", 0.18f),
            }),
        };

        // ------------------------------------------------------------------ the actions (Name + the stance's kind)

        // 防御, the player (guard-player.png): dropped low with the feet wide, the left shoulder leading and the
        // left forearm up level in front of the face, the head ducked behind it, the phone gripped to the chest.
        static readonly Stance GuardPlayer = new Stance
        {
            Name = "Guard",
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

        // The win, held for as long as the results stay up (SeaFigure.Act with hold). The player: the right arm up
        // high with the phone in its fist, taking the cleared field, the left fist on the hip, leaning back a little
        // onto the back foot, looking up at the phone's screen.
        static readonly Stance VictoryPlayer = new Stance
        {
            Name = "Victory",
            Turn = 8f,
            Lean = -4f,
            Back = "Right",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.25f,
                ["Chest Front-Back"] = 0.2f,
                ["Neck Nod Down-Up"] = 0.25f,
                ["Head Nod Down-Up"] = 0.3f,
                ["Neck Turn Left-Right"] = 0.1f,
                ["Left Upper Leg Front-Back"] = 0.45f,
                ["Left Upper Leg In-Out"] = 0.12f,
                ["Left Lower Leg Stretch"] = 0.95f,
                ["Left Foot Twist In-Out"] = 0.2f,
                ["Right Upper Leg Front-Back"] = 0.5f,
                ["Right Upper Leg In-Out"] = 0.12f,
                ["Right Lower Leg Stretch"] = 0.85f,
                ["Right Foot Twist In-Out"] = 0.25f,
                // The phone arm straight up, the palm turned back to him: the phone lies across the fist
                // (SeaFigure.InHand), so it is held landscape as for the 截图, its screen to him and its lens to the
                // field. From behind, the lit screen over the head tells it from the rice ball held up in 道具.
                ["Right Shoulder Down-Up"] = 0.7f,
                ["Right Arm Down-Up"] = 1f,
                ["Right Arm Front-Back"] = 0.1f,
                ["Right Forearm Stretch"] = 0.9f,
                ["Right Hand Down-Up"] = 0.4f,
                ["Right Forearm Twist In-Out"] = -0.6f,
                // The other fist on the hip, the elbow out to the side and a little back (the arm turned in all the
                // way, so the forearm comes down to the hip rather than across the front; further back, the fist
                // went behind the hip).
                ["Left Arm Down-Up"] = -0.3f,
                ["Left Arm Front-Back"] = 0.25f,
                ["Left Arm Twist In-Out"] = -1f,
                ["Left Forearm Stretch"] = 0.25f,
            }, p =>
            {
                Fingers(p, "Right", -0.6f, 0f);
                Fingers(p, "Left", -0.8f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.3f,
                ["Right Shoulder Down-Up"] = 0.75f,
            },
        };

        // The girl's win: the same, the head cocked over to her left, away from the phone arm, so from behind the two
        // part. (Not swaying the spine after the phone: the pose's body turn is fixed, so bending the spine aside tips
        // the hips the other way and lifts a foot off the floor. Nor the phone held out in front: turned to her
        // there, it lies flat or edge on to the camera behind her.)
        static readonly Stance VictoryPlayerFemale = Varied(VictoryPlayer, p =>
        {
            p["Neck Tilt Left-Right"] = -0.15f;
            p["Head Tilt Left-Right"] = -0.25f;
        });

        // DeepSeek's win: the 饭勺 held up at the sky in her right hand (SeaFigure.Wield), the left fist in at her
        // side, the feet apart, the chin up.
        static readonly Stance VictoryDeepSeek = new Stance
        {
            Name = "Victory",
            Turn = -8f,
            Back = "Left",
            Out = With(new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.15f,
                ["Chest Front-Back"] = 0.15f,
                ["Neck Nod Down-Up"] = 0.2f,
                ["Head Nod Down-Up"] = 0.2f,
                ["Head Tilt Left-Right"] = -0.1f,
                ["Right Upper Leg Front-Back"] = 0.45f,
                ["Right Upper Leg In-Out"] = 0.3f,
                ["Right Lower Leg Stretch"] = 0.9f,
                ["Left Upper Leg Front-Back"] = 0.55f,
                ["Left Upper Leg In-Out"] = 0.3f,
                ["Left Upper Leg Twist In-Out"] = 0.2f,
                ["Left Lower Leg Stretch"] = 0.85f,
                ["Right Shoulder Down-Up"] = 0.6f,
                ["Right Arm Down-Up"] = 0.95f,
                ["Right Arm Front-Back"] = 0.15f,
                ["Right Forearm Stretch"] = 0.85f,
                ["Right Hand Down-Up"] = 0.1f,
                ["Left Shoulder Down-Up"] = -0.5f,
                ["Left Arm Down-Up"] = -0.35f,
                ["Left Arm Front-Back"] = 0f,
                ["Left Forearm Stretch"] = 0.2f,
                ["Left Hand Down-Up"] = 0.1f,
            }, p =>
            {
                Fingers(p, "Right", -0.7f, 0f);
                Fingers(p, "Left", -0.8f, 0f);
            }),
            In = new Dictionary<string, float>
            {
                ["Spine Front-Back"] = 0.2f,
                ["Neck Nod Down-Up"] = 0.25f,
                ["Right Shoulder Down-Up"] = 0.66f,
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
            foreach (var (name, source, legsIn, phoneFist, mirror, yaw, offsets) in Library) MakeStance(name, source, legsIn, phoneFist, mirror, yaw, offsets);
            // After the stances, which they start and end on.
            MakeAction(GuardPlayer, "PlayerMale", "Player");
            MakeAction(GuardPlayer, "PlayerFemale", "PlayerFemale");
            MakeAction(GuardDeepSeek, "DeepSeek", "DeepSeek");
            MakeAction(ItemPlayer, "PlayerMale", "Player");
            MakeAction(ItemPlayer, "PlayerFemale", "PlayerFemale");
            MakeAction(ItemDeepSeek, "DeepSeek", "DeepSeek");
            MakeAction(ThinkDeepSeek, "DeepSeek", "DeepSeek");
            MakeAction(VictoryPlayer, "PlayerMale", "Player");
            MakeAction(VictoryPlayerFemale, "PlayerFemale", "PlayerFemale");
            MakeAction(VictoryDeepSeek, "DeepSeek", "DeepSeek");
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
                    ("victory-male", Male, VictoryPlayer, 1f), ("victory-female", Female, VictoryPlayerFemale, 1f), ("victory-deepseek", Her, VictoryDeepSeek, 1f),
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

        static void MakeStance(string name, string sourceName, float legsIn, bool phoneFist, bool mirror, float yaw, (string muscle, float add)[] offsets)
        {
            var path = MotionSources + sourceName + ".anim";
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (source == null) throw new InvalidOperationException($"找不到 {name} 的来源动作 {path}");
            Save(Fitted(source, name, legsIn, phoneFist, mirror, offsets, yaw));
            Debug.Log($"{name}：来自 {source.name}{(mirror ? "（镜像）" : "")}，{source.length:F2} 秒一循环，双腿内收 {-legsIn}{(phoneFist ? "，右手握拳" : "")}");
        }

        // A library loop as a stance: mirrored if asked (each side's curves swapped, the body's left-right
        // muscles, its x and its yaw and roll negated), both thighs closed in by legsIn, looping whatever its
        // own flag says (Sword_Idle is an idle without _Loop in its name), with the phone hand's fingers curled
        // round it, and each of offsets added to its muscle (named as after the mirror) over the whole loop.
        internal static AnimationClip Fitted(AnimationClip source, string name, float legsIn, bool phoneFist, bool mirror = false, (string muscle, float add)[] offsets = null,
            float yaw = 0f)
        {
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = name;
            AnimationCurve Get(string property) =>
                AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property))
                ?? throw new InvalidOperationException($"{source.name} 里没有曲线 {property}");
            void Set(string property, AnimationCurve curve) =>
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);

            if (mirror)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var curves = bindings.Select(b => (b, AnimationUtility.GetEditorCurve(clip, b))).ToArray();
                foreach (var b in bindings) AnimationUtility.SetEditorCurve(clip, b, null);
                foreach (var (b, curve) in curves)
                {
                    var property = b.propertyName;
                    var mirrored = curve;
                    if (property.StartsWith("Left")) property = "Right" + property.Substring(4);
                    else if (property.StartsWith("Right")) property = "Left" + property.Substring(5);
                    else if (property.EndsWith("Left-Right") || property == "RootT.x" || property == "RootQ.y" || property == "RootQ.z")
                    {
                        var keys = curve.keys;
                        for (var i = 0; i < keys.Length; i++)
                        {
                            keys[i].value = -keys[i].value;
                            keys[i].inTangent = -keys[i].inTangent;
                            keys[i].outTangent = -keys[i].outTangent;
                        }
                        mirrored = new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(b.path, b.type, property), mirrored);
                }
            }
            void Add(string property, float add)
            {
                var curve = Get(property);
                var keys = curve.keys;
                for (var i = 0; i < keys.Length; i++) keys[i].value += add;
                curve.keys = keys;
                Set(property, curve);
            }
            Add("Left Upper Leg In-Out", legsIn);
            Add("Right Upper Leg In-Out", legsIn);
            foreach (var (muscle, add) in offsets ?? Array.Empty<(string, float)>()) Add(CurveName(muscle), add);
            if (yaw != 0f)
            {
                // The whole body turned about the upright, feet and all (the caster's idle stands side on to what it
                // faces, hips 42 degrees off and chest 61: on the battle field that reads as facing away from the noise
                // bodies), its place over the root turned with it.
                var turn = Quaternion.AngleAxis(yaw, Vector3.up);
                AnimationCurve qx = Get("RootQ.x"), qy = Get("RootQ.y"), qz = Get("RootQ.z"), qw = Get("RootQ.w"), tx = Get("RootT.x"), tz = Get("RootT.z");
                var times = qx.keys.Select(k => k.time).Concat(tx.keys.Select(k => k.time)).Distinct().OrderBy(t => t).ToArray();
                var turned = new Keyframe[6][];
                for (var c = 0; c < 6; c++) turned[c] = new Keyframe[times.Length];
                for (var i = 0; i < times.Length; i++)
                {
                    var t = times[i];
                    var q = turn * new Quaternion(qx.Evaluate(t), qy.Evaluate(t), qz.Evaluate(t), qw.Evaluate(t)).normalized;
                    var at = turn * new Vector3(tx.Evaluate(t), 0f, tz.Evaluate(t));
                    var values = new[] { q.x, q.y, q.z, q.w, at.x, at.z };
                    for (var c = 0; c < 6; c++) turned[c][i] = new Keyframe(t, values[c]);
                }
                var names = new[] { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w", "RootT.x", "RootT.z" };
                for (var c = 0; c < 6; c++)
                {
                    var curve = new AnimationCurve(turned[c]) { preWrapMode = qx.preWrapMode, postWrapMode = qx.postWrapMode };
                    for (var i = 0; i < times.Length; i++) curve.SmoothTangents(i, 0f);
                    Set(names[c], curve);
                }
            }
            if (phoneFist)
            {
                var fist = new Dictionary<string, float>();
                Fingers(fist, "Right", -0.6f, 0f);
                foreach (var kv in fist) Set(CurveName(kv.Key), AnimationCurve.Constant(0f, clip.length, kv.Value));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        // Not part of Run: -executeMethod SystemPrompt.EditorTools.StancePoser.Survey -stanceSurvey <dir> fits every
        // library loop in Editor/Motions to the boy player (lounge clothes) and DeepSeek (battle dress) as a stance
        // and draws each: from the front, its right and behind at the start of the loop, then four frames over the
        // loop from behind its right shoulder, about where the battle camera sits while a command is chosen. The
        // fitted clips are written under Motions as Survey* while the figures need them and deleted after.
        public static void Survey()
        {
            var args = Environment.GetCommandLineArgs();
            var dir = args[Array.IndexOf(args, "-stanceSurvey") + 1];
            Directory.CreateDirectory(dir);
            var holder = new GameObject("Stance Survey");
            var made = new List<string>();
            try
            {
                foreach (var file in Directory.GetFiles(MotionSources, "*.anim").OrderBy(f => f))
                {
                    var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(file);
                    var tag = Path.GetFileNameWithoutExtension(file).Replace("_", "");
                    foreach (var (model, who, legsIn, fist) in new[] { ("PlayerMale", "male", Library[0].legsIn, true), ("DeepSeek", "deepseek", Library[2].legsIn, false) })
                    {
                        var name = "Survey" + tag + model;
                        Save(Fitted(source, name, legsIn, fist));
                        made.Add(MotionRoot + name + ".anim");
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                        Func<SeaFigure> make = model == "PlayerMale"
                            ? () => new SeaFigure("PlayerMaleLounge", holder.transform, 0f, name, true, 0.05f)
                            : () => new SeaFigure("DeepSeek", holder.transform, 1.58f, name, false, 0.075f);
                        Loop(make, Path.Combine(dir, $"{tag}-{who}.png"), source.length);
                    }
                }
            }
            finally
            {
                foreach (var path in made) AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        // A stance over its loop: three views at its start, then four frames of it from behind the right
        // shoulder; a new figure for each shot, as Strip does.
        static void Loop(Func<SeaFigure> make, string file, float length)
        {
            const int Frames = 4;
            DungeonWorld.ApplySea(0f, Vector3.zero, 9f);
            var shoulder = new Vector3(0.8f, 0.45f, -1f);
            using (var sheet = new Sheet(512, 768, 3 + Frames))
            {
                for (var i = 0; i < 3 + Frames; i++)
                {
                    var figure = make();
                    figure.Stance();
                    figure.Animate(i < 3 ? 0f : length * (i - 3) / Frames, 0f);
                    sheet.Shoot(i < 3 ? new[] { Vector3.forward, Vector3.right, Vector3.back }[i] : shoulder);
                    figure.Dispose();
                    UnityEngine.Object.DestroyImmediate(figure.Root.gameObject);
                }
                sheet.Save(file);
            }
            Debug.Log($"待机调查：{file}");
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
        internal static HumanPose PoseAt(AnimationClip clip, float time)
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

        internal static GameObject Instantiate(string model, out Animator animator)
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

        // As BattleStage does it: the 道具 action holds the rice ball until the pose is let go, and DeepSeek wins with
        // the 饭勺 in her hand.
        static float Act(SeaFigure figure, Stance action)
        {
            var length = figure.Act(action.Name);
            if (action.Name == "Item") figure.HoldRiceBall(action.Rise + action.Seconds);
            if (action.Name == "Victory" && !figure.CarriesPhone) figure.Wield(true);
            return length;
        }

        // Shots of whatever is posed at the origin, left to right on grey, each from a way round it, orthographic
        // so they set beside the drawn sheets.
        internal sealed class Sheet : IDisposable
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
