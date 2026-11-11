using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace SystemPrompt.Dungeon
{
    // A character model from Resources/Models/Characters (made in Tripo from the turnarounds in T-pose,
    // rigged to Mixamo bones, DeepSeek's battle dress in Blender with bones down its tail too, imported as
    // humanoid by Editor/ModelAssetImporter.cs), standing on Root at
    // the given height and facing +z. A small playable graph blends the model's own idle (in battle the
    // fighting stance) with the shared walk and run from Resources/Models/Motions by speed, and fades a
    // one-off motion (a slash, a hit) in over that and out again; the imported look is swapped for the sea's
    // toon shader on the model's base colour texture, with a neutral shade so the texture keeps its colours.
    // A model made in VRoid (a prefab baked from its VRM, Characters/<model>/) brings a toon material per part
    // instead, changes expression with its face's blend shapes rather than drawn ones, and swings its hair,
    // skirt and tail on spring bones (SeaSprings).
    public sealed class SeaFigure
    {
        const float WalkSpeed = 1.6f;   // m/s by which the walk has taken over from the idle
        const float RunFrom = 3.2f;     // the run has fully taken over by here
        // How fast the planted foot of the walk and of the run goes back at their own pace, per metre of the avatar's
        // human scale (about its hips' height): Quaternius' Walk_Loop and Jog_Fwd_Loop, measured over each footfall
        // on the VRoid models.
        const float WalkPace = 0.87f, RunPace = 5.7f;
        const float FadeIn = 0.12f, FadeOut = 0.22f; // a one-off motion over the idle, in seconds, unless told otherwise
        const float FlinchOut = 0.2f; // a hit's jolt easing off at the end, seconds
        const float PhoneUp = 0.18f, PhoneDown = 0.25f; // bringing the 截图's phone up and down, in seconds
        const float RiceGrow = 0.15f; // the 道具's rice ball coming into the hand and going, in seconds
        static readonly Color Shade = new Color(0.78f, 0.74f, 0.8f);
        static readonly Color SkinShade = new Color(0.93f, 0.78f, 0.75f); // peach in shadow, not mauve
        static readonly Color Rim = new Color(0.5f, 0.92f, 1f, 0.35f);
        // A dense mesh turns grazing faces to the camera all over its folds and hair; a high power keeps
        // the rim to the silhouette.
        const float RimPower = 8f;
        // No ink outline: on a generated mesh the inverted hull of the fringe and the jaw showed through as
        // thick black strokes across the eyes and cheeks.
        const float Outline = 0f;

        static readonly Dictionary<string, AnimationClip> motions = new Dictionary<string, AnimationClip>();

        // The face box, as fractions of the figure's height: as wide and tall as FaceSpan, centred FaceRise
        // above the head bone, its front plane FaceFront ahead of it (clear of the nose), FaceDepth deep.
        const float FaceSpan = 0.15f, FaceRise = 0.02f, FaceFront = 0.12f, FaceDepth = 0.2f;
        // Cells of the expression atlas (Tools/face_expressions.py draws them, 3 × 3, row by row from the top).
        public const int EyesClosed = 0, EyesHappy = 1, EyesHalf = 2, EyesSmall = 3,
            MouthSmile = 4, MouthOpen = 5, MouthPout = 6, MouthO = 7, Blush = 8, None = -1;
        // The story's #face: tags (Art/Portraits) as eyes, mouth and blush, and the battle's own (Dungeon/BattleStage.cs).
        static readonly Dictionary<string, (int eyes, int mouth, int blush)> faces = new Dictionary<string, (int, int, int)>
        {
            ["neutral"] = (None, None, None),
            ["smile"] = (EyesHappy, MouthSmile, Blush),
            ["pout"] = (EyesHalf, MouthPout, Blush),
            ["scared"] = (EyesSmall, MouthO, None),
            ["sleepy"] = (EyesHalf, None, None),
            ["concerned"] = (EyesHalf, MouthPout, None),
            ["hurt"] = (EyesClosed, MouthPout, None),
            ["down"] = (EyesClosed, None, None),
            ["think"] = (EyesClosed, None, None),
        };
        // The same cells on a VRoid face, as its blend shapes (0–100) with the brows that go with the eyes; its
        // blush is painted on, so that cell has none.
        static readonly Dictionary<int, (string shape, float weight)[]> faceShapes = new Dictionary<int, (string, float)[]>
        {
            [EyesClosed] = new[] { ("Fcl_EYE_Close", 100f), ("Fcl_BRW_Sorrow", 40f) },
            [EyesHappy] = new[] { ("Fcl_EYE_Joy", 100f), ("Fcl_BRW_Fun", 100f) },
            [EyesHalf] = new[] { ("Fcl_EYE_Close", 40f), ("Fcl_BRW_Sorrow", 60f) },
            [EyesSmall] = new[] { ("Fcl_EYE_Surprised", 100f), ("Fcl_BRW_Surprised", 100f) },
            [MouthSmile] = new[] { ("Fcl_MTH_Fun", 100f) },
            [MouthOpen] = new[] { ("Fcl_MTH_A", 70f) },
            [MouthPout] = new[] { ("Fcl_MTH_Sorrow", 70f), ("Fcl_MTH_U", 30f) },
            [MouthO] = new[] { ("Fcl_MTH_O", 100f) },
        };

        public readonly Transform Root;
        public readonly Animator Animator;
        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable mixer;
        readonly AnimationLayerMixerPlayable layers; // the idle, walk and run under the motion playing over them, a flinch added over both
        readonly AnimationMixerPlayable actions; // the motion playing, and the one it is taking over from
        readonly AnimationClipPlayable walk, run;
        readonly float stride; // the avatar's human scale in metres as the model is sized, which the paces scale by
        AnimationClipPlayable idle, action, leaving;
        int actionInput, leavingInput; // their inputs on actions
        string actionName;
        float actionTime, actionLength, actionWeight; // length infinite for a loop or a hold, which plays until Stop
        // The motion playing: its own fades in and out, the most it shows over the idle, its speed; the one leaving's most.
        float actionFadeIn, actionFadeOut, actionMax, actionSpeed, leavingMax;
        float actionShare; // how much of the pose the motions have this frame, eased
        float holdFrom, holdTo = -1f; // held (Hold): its clip's time going round between these until Release or Stop
        float handover; // 0 to 1, from the motion leaving to the one playing
        AnimationClipPlayable flinch; // a hit's jolt, added over the chest and head only
        float flinchTime, flinchLength, flinchWeight;
        static AvatarMask torso;

        // The poses struck in place (Act, posed by Editor/StancePoser.cs): seconds from the stance into the pose, held
        // a breath, and back to the stance. A held one (Act's hold) goes round its breath until Release.
        public static readonly Dictionary<string, (float Rise, float Seconds, float Return)> PoseTimes = new Dictionary<string, (float, float, float)>
        {
            ["Guard"] = (0.22f, 1.2f, 0.4f),
            ["Item"] = (0.35f, 1.0f, 0.4f),
            ["Think"] = (0.4f, 1.6f, 0.45f),
            ["Victory"] = (0.3f, 1.6f, 0.4f),
        };

        // In battle the stance settles over the motion (Settle): the body lowered by Crouch (metres) into the knees,
        // breathing, its weight drifting from foot to foot, nudged by a blow or a start (Nudge), and the feet put back
        // where the motion has them, so only the knees take it. The breath and the drift start at the stance's phase.
        public float Crouch;
        // In the stance the phone held ready before the chest in both hands (HoldReady), rather than out at arm's
        // length as the library's stance has it.
        public bool Ready;
        float readyShare;      // how much of it shows: none while a motion plays or the figure walks
        Vector3 readyAt;       // where it puts the phone, for the 截图 to bring it up from
        Quaternion readyTurn;
        float settleTime, settlePhase;
        Vector3 nudge;
        float nudgeTime, nudgeRise = 1f, nudgeFall = 1f;
        readonly Material look; // null on a VRoid model, whose parts each bring their own
        // The expressions are drawn on a picture of the face taken straight from the front (Editor/FaceCapture.cs)
        // and laid back over it from the front by the toon shader, in a box that moves with the head bone.
        readonly Transform head;
        readonly Matrix4x4 faceFromHead;
        readonly Vector3 faceForward; // in the head bone's space
        // On a VRoid face: the mesh, and per cell its blend shapes by index; every index any cell uses.
        readonly SkinnedMeshRenderer faceMesh;
        readonly Dictionary<int, (int index, float weight)[]> faceShapeIndex = new Dictionary<int, (int, float)[]>();
        readonly List<int> faceShapesUsed = new List<int>();
        readonly SeaSprings springs;
        (int eyes, int mouth, int blush) face = (None, None, None);
        Vector4 faceShown = new Vector4(None, None, None, 0f);
        float blinkIn = 2.5f, blinkLeft, talkTime;
        readonly float height;
        readonly string stance;
        readonly bool carriesPhone; // the player: in the stance the phone is in their right hand
        bool inStance;
        Transform phone;
        float phoneTime, phoneLength, phoneWeight;
        Transform riceBall;
        float riceTime, riceLength;
        PhoneHold hold;
        Transform spoon;
        float spoonShown;
        bool wielding;

        // Exploring the maze (SeaAvatar, SeaCompanion): the head turns to what it is told to look at (LookAt). The
        // battle's figures leave it off and keep their motions as made, but are Attentive: the head alone turns to
        // what BattleStage points it at, less far, and not while a motion or the phone has the head.
        public bool Explorer, Attentive;
        Vector3? lookPoint;
        bool lookQuick, lookIdle;
        float lookYaw, lookPitch, lookWeight; // the head's wanted way in Root's space (degrees aside, up), and how much
        float eyeYaw, eyePitch;               // a VRoid face's eyes, ahead of the head
        readonly Transform neck;
        readonly Transform[] eyes;            // a VRoid face's eye bones, or none (a drawn face)
        readonly Quaternion[] eyeRest;
        // Exploring, DeepSeek's tail sways of itself, slowly and less as she runs, and wags for a moment (Wag): the
        // battle dress's as it is laid (LayTail), the uniform's spring by turning its first links' pull (SeaSprings.Swing).
        const float SwayHz = 0.35f, WagHz = 1.8f;
        const int SwayLinks = 4;
        readonly int tailSpring = -1;         // the uniform tail's first link in the springs, or -1
        float swayTime, swayShare, wagTime, wagLength, wagShare; // the wag's length 0 with none
        // Exploring, each foot set down at a walk or a run: where on the seabed, and how fast the figure is going. The
        // clips carry no events, so it is found as the low point of the foot's height over Root, near its height at rest.
        public event Action<Vector3, float> Footfall;
        readonly Transform[] feet;
        readonly float footRest;
        readonly float[] footWas = new float[2], footSince = new float[2];
        readonly bool[] footFalling = new bool[2];
        // Exploring, the body leans with how Root speeds up, slows and turns: forward pushing off, back pulling up, into
        // a turn at a run; on a damped spring, so a stop rocks back past upright and settles. On the spine and chest
        // alone: turning the hips would swing the legs (there is no foot IK) and the battle dress's tail with them.
        const float LeanSpring = 14f, LeanDamping = 0.45f; // radians a second, and the damping ratio
        readonly Transform spine, chest;
        Vector3 leanWas, leanVelocity, leanAccel, leanAccelChange;
        float leanPitch, leanPitchSpeed, leanRoll, leanRollSpeed;

        // A tail that curves down from the hips and lies along the floor (DeepSeek's battle dress, bones Tail1–Tail6).
        // The motions are made for figures without one and carry it stiffly on the hips, so every lean and bob of
        // the hips swung its far end up off the floor. After each pose it is laid back joint by joint from the
        // hips: the first joints go where the hips carry them, and each one after keeps the height it has at rest,
        // a bone's length from the joint above, in the direction it trailed in last frame, easing back to the way
        // it runs at rest turned as far as the hips have turned (not as they lean: the run leans so far forward
        // that the tail would be laid under the feet); so it drags behind as the figure runs and turns. Those
        // bones are rolled level as at rest, so the fin lies flat. The last point is past the end of Tail6, a
        // bone's length on, for the fin's own bone.
        const int TailCarried = 2;      // the joints the hips carry: Tail1's and Tail2's
        const float TailFollow = 8f;    // how fast a trailing joint eases back to the pose, per second
        readonly Transform[] tail;      // Tail1 down to the last; point i is joint i, placed by turning bone i - 1
        readonly Vector3 tailEnd;       // the last point, the fin's end, in the last bone's space
        readonly float[] tailRest;      // each point's height at rest, in Root's space
        readonly Vector3[] tailHeading; // the level way to each point from the one above at rest, in Root's space
        readonly Vector3[] tailSide;    // each bone's level axis across the tail at rest, in its own space
        readonly Vector3 hipsAhead;     // Root's forward at rest, in the hips' space
        Vector3[] tailWas;              // where each point was put last frame, in the world
        // In battle the joints the hips carry sway from side to side about the hips' forward axis too, and are flung
        // (Flick) on a damped spring (about 1.2 Hz, gone in half a second), at most 25 degrees either way. No motion
        // writes the tail's bones, so the turn is put on Tail1's pose at rest each frame, never on last frame's. The
        // sway dies away while the figure lies on the floor (Lying), where it would sweep the tail over it.
        const float CarriedSwayHz = 0.4f, FlickStiffness = 60f, FlickDamping = 8f, FlickMost = 25f;
        float carriedTime, flick, flickSpeed; // degrees toward the figure's right, and its speed
        float carriedSway = 1f;               // how much of the sway shows: none while lying
        readonly Quaternion carriedRest;      // Tail1's local rotation at rest
        public bool Lying;

        // The player of the chosen gender: in the lounge clothes they were pulled in with (4-lounge), or in the
        // latent-space battle coat (5-battle) that the white door's mirror shows. Each has a fighting stance of
        // their own, both with the phone in the right hand. The lounge models are made in VRoid and keep their own
        // size (162 and 175 cm, a few more to the tip of the ahoge); having no idle of their own, they take that of
        // the Tripo models they replaced, the boy's with the legs opened so his wide trousers don't meet
        // (Motions/IdlePlayerMaleLounge, Editor/StancePoser.cs), and their face box sits as DeepSeek's VRoid one does.
        public static SeaFigure Player(string gender, bool battle, Transform parent) =>
            gender == "female"
                ? battle ? new SeaFigure("PlayerFemaleBattle", parent, 1.62f, "StancePlayerFemale", true)
                    : new SeaFigure("PlayerFemaleLounge", parent, 0f, "StancePlayerFemale", true, 0.05f, "PlayerFemale")
                : battle ? new SeaFigure("PlayerMaleBattle", parent, 1.72f, "StancePlayer", true)
                    : new SeaFigure("PlayerMaleLounge", parent, 0f, "StancePlayer", true, 0.05f);

        // DeepSeek in her school uniform under the whale hoodie (1-uniform, made in VRoid) before the lighthouse,
        // her battle dress (4-battle) after; the Tripo weekend hoodie dress (3-casual, DeepSeekCasual) is no longer
        // used. The battle dress is rigged in Blender, whose head bone sits at the top of the neck, lower under
        // the face than Tripo's: its face box rises further above the bone. The VRoid model keeps its own size
        // (151 cm, 1.64 m to the tip of the ahoge) and takes the battle dress's idle, having none of its own.
        public static SeaFigure DeepSeek(bool awakened, Transform parent) =>
            awakened ? new SeaFigure("DeepSeek", parent, 1.58f, "StanceDeepSeek", false, 0.075f)
                : new SeaFigure("DeepSeekUniform", parent, 0f, "StanceDeepSeek", false, 0.05f, "DeepSeek");

        // What Player(gender, false) and DeepSeek(awakened) read from Resources, the same models as above, so a
        // maze can read it in the background before building them.
        public static IEnumerable<(string path, Type type)> Files(string gender, bool deepSeekAwakened)
        {
            var player = gender == "female" ? ("PlayerFemaleLounge", "PlayerFemale") : ("PlayerMaleLounge", (string)null);
            var deepSeek = deepSeekAwakened ? ("DeepSeek", (string)null) : ("DeepSeekUniform", "DeepSeek");
            foreach (var (model, idleOf) in new[] { player, deepSeek })
            {
                var path = "Models/Characters/" + model;
                yield return (path, typeof(GameObject));
                yield return (path + "_BaseColor", typeof(Texture2D));
                yield return (path + "_Faces", typeof(Texture2D));
                yield return (path + "_FaceDepth", typeof(Texture2D));
                yield return (path + "_Springs", typeof(TextAsset));
                yield return ("Models/Motions/Idle" + (idleOf ?? model), typeof(AnimationClip));
                yield return ("Models/Characters/" + (idleOf ?? model), typeof(AnimationClip));
            }
            yield return ("Models/Motions/Walk", typeof(AnimationClip));
            yield return ("Models/Motions/Run", typeof(AnimationClip));
        }

        // height: from the soles to the top, 0 for the model's own; stance: the fighting stance it takes in battle
        // (Resources/Models/Motions, Editor/StancePoser.cs), and whether it holds the 截图's phone there; faceRise:
        // the face box's centre above the head bone, of the height; idleOf: the model whose idle it takes.
        public SeaFigure(string model, Transform parent, float height, string stance, bool carriesPhone, float faceRise = FaceRise, string idleOf = null)
        {
            this.stance = stance;
            this.carriesPhone = carriesPhone;
            Root = Parts.Node(model, parent);
            SeaShade.Add(Root, true); // its shadow on the seabed and its rim (SeaShade.cs)
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

            // A Tripo model's one base colour texture, or a VRoid model's toon materials of its own.
            var texture = Resources.Load<Texture2D>(path + "_BaseColor");
            var renderers = body.GetComponentsInChildren<Renderer>();
            var own = texture == null && renderers.Length > 0 && Array.TrueForAll(renderers,
                r => Array.TrueForAll(r.sharedMaterials, m => m != null && m.shader.name == "SystemPrompt/Toon"));
            if (texture == null && !own) throw new InvalidOperationException($"找不到角色贴图 Resources/{path}_BaseColor");
            if (!own)
            {
                look = SeaArt.Toon(Color.white, Shade, Rim, default, Outline);
                look.SetFloat("_RimPower", RimPower);
                look.SetColor("_SkinShade", SkinShade);
                look.SetFloat("_SkinMask", 1f);
                look.SetFloat("_CameraLight", 1f); // faces stay lit whichever way the figure turns
                look.mainTexture = texture;
            }

            // The bind pose's extent, from the meshes' own bounds, gives the scale to the wanted height.
            float low = float.MaxValue, high = float.MinValue;
            foreach (var r in renderers)
            {
                var skin = r as SkinnedMeshRenderer;
                var mesh = skin != null ? skin.sharedMesh : r.GetComponent<MeshFilter>().sharedMesh;
                var b = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var y = Root.InverseTransformPoint(r.transform.TransformPoint(corner)).y;
                    low = Mathf.Min(low, y);
                    high = Mathf.Max(high, y);
                }
                if (own) r.sharedMaterials = Array.ConvertAll(r.sharedMaterials, Dress);
                else
                {
                    var looks = new Material[mesh.subMeshCount];
                    for (var i = 0; i < looks.Length; i++) looks[i] = look;
                    r.sharedMaterials = looks;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (skin == null) continue;
                skin.updateWhenOffscreen = true; // the bounds follow the pose, not the T of the bind
                if (skin.sharedMesh.GetBlendShapeIndex("Fcl_EYE_Close") >= 0) faceMesh = skin;
            }
            if (renderers.Length == 0 || high <= low) throw new InvalidOperationException($"角色模型 {model} 没有蒙皮网格");
            var scale = height > 0f ? height / (high - low) : 1f;
            this.height = (high - low) * scale;
            body.transform.localScale *= scale;
            body.transform.localPosition = new Vector3(0f, -low * scale, 0f);
            stride = Animator.humanScale * body.transform.lossyScale.y;

            // The tail at rest, as the file has it: down from the hips and along the floor behind the figure.
            var tailBones = new List<Transform>();
            foreach (var t in body.GetComponentsInChildren<Transform>())
                if (t.name.EndsWith("Tail1")) { tailBones.Add(t); break; }
            while (tailBones.Count > 0 && tailBones.Count < 16)
            {
                Transform next = null;
                foreach (Transform child in tailBones[tailBones.Count - 1])
                    if (child.name.Contains("Tail")) next = child;
                if (next == null) break;
                tailBones.Add(next);
            }
            if (tailBones.Count >= 2)
            {
                tail = tailBones.ToArray();
                var end = tail[tail.Length - 1];
                tailEnd = end.InverseTransformPoint(end.position + (end.position - tail[tail.Length - 2].position));
                tailRest = new float[tail.Length + 1];
                tailHeading = new Vector3[tail.Length + 1];
                for (var i = 0; i < tailRest.Length; i++)
                {
                    var point = Root.InverseTransformPoint(TailPoint(i));
                    tailRest[i] = point.y;
                    if (i == 0) continue;
                    var way = point - Root.InverseTransformPoint(TailPoint(i - 1));
                    way.y = 0f;
                    tailHeading[i] = way.sqrMagnitude > 1e-8f ? way.normalized : Vector3.back;
                }
                hipsAhead = Animator.GetBoneTransform(HumanBodyBones.Hips).InverseTransformDirection(Root.forward);
                carriedRest = tail[0].localRotation;
                tailSide = new Vector3[tail.Length];
                for (var i = 0; i < tail.Length; i++)
                {
                    var side = Vector3.Cross(Root.up, TailPoint(i + 1) - TailPoint(i));
                    tailSide[i] = tail[i].InverseTransformDirection(side.sqrMagnitude > 1e-8f ? side.normalized : Root.right);
                }
            }

            graph = PlayableGraph.Create("Figure " + model);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            layers = AnimationLayerMixerPlayable.Create(graph, 3);
            graph.Connect(mixer, 0, layers, 0);
            layers.SetInputWeight(0, 1f);
            actions = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(actions, 0, layers, 1);
            // Added over the pose, not laid over it: the arms (in muscle space hung off the chest) go with the chest and
            // keep what they hold, the phone too.
            layers.SetLayerMaskFromAvatarMask(2, Torso);
            layers.SetLayerAdditive(2, true);
            AnimationPlayableOutput.Create(graph, model, Animator).SetSourcePlayable(layers);
            idle = Connect(Idle(idleOf ?? model), 0);
            walk = Connect(Motion("Walk"), 1);
            run = Connect(Motion("Run"), 2);
            mixer.SetInputWeight(0, 1f);
            graph.Evaluate(0f);
            var springData = Resources.Load<TextAsset>(path + "_Springs");
            if (springData != null) springs = new SeaSprings(body.transform, Root, springData.text);
            if (springs != null) tailSpring = springs.Find("Tail_0");

            // The face box in the idle's first frame, in Root's space (x right, y up, z ahead), then fixed to the head.
            head = Animator.GetBoneTransform(HumanBodyBones.Head);
            neck = Animator.GetBoneTransform(HumanBodyBones.Neck);
            Transform leftEye = Animator.GetBoneTransform(HumanBodyBones.LeftEye), rightEye = Animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye != null && rightEye != null)
            {
                eyes = new[] { leftEye, rightEye };
                eyeRest = new[] { leftEye.localRotation, rightEye.localRotation };
            }
            feet = new[] { Animator.GetBoneTransform(HumanBodyBones.LeftFoot), Animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            footRest = Mathf.Min(Root.InverseTransformPoint(feet[0].position).y, Root.InverseTransformPoint(feet[1].position).y);
            spine = Animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            leanWas = Root.position;
            var at = Root.InverseTransformPoint(head.position);
            float span = FaceSpan * this.height, depth = FaceDepth * this.height, centre = at.y + faceRise * this.height, front = at.z + FaceFront * this.height;
            // Seen from the front: u runs from the figure's right to its left, v up, w from the front plane in.
            var faceFromRoot = new Matrix4x4(
                new Vector4(-1f / span, 0f, 0f, 0f),
                new Vector4(0f, 1f / span, 0f, 0f),
                new Vector4(0f, 0f, -1f / depth, 0f),
                new Vector4(0.5f + at.x / span, 0.5f - centre / span, front / depth, 1f));
            faceFromHead = faceFromRoot * Root.worldToLocalMatrix * head.localToWorldMatrix;
            faceForward = head.InverseTransformDirection(Root.forward);
            var atlas = Resources.Load<Texture2D>(path + "_Faces");
            var faceDepth = Resources.Load<Texture2D>(path + "_FaceDepth");
            if (look != null && atlas != null && faceDepth != null)
            {
                look.SetTexture("_Faces", atlas);
                look.SetTexture("_FaceDepth", faceDepth);
                look.SetFloat("_FaceOn", 1f);
            }
            if (faceMesh != null)
                foreach (var cell in faceShapes)
                {
                    var shapes = Array.ConvertAll(cell.Value, s => (index: faceMesh.sharedMesh.GetBlendShapeIndex(s.shape), s.weight));
                    faceShapeIndex[cell.Key] = Array.FindAll(shapes, s => s.index >= 0);
                    foreach (var s in faceShapeIndex[cell.Key])
                        if (!faceShapesUsed.Contains(s.index)) faceShapesUsed.Add(s.index);
                }
            ShowFace();
        }

        // A VRoid part's own toon material, shaded as the other figures are, its colour's tint in the shade too.
        static Material Dress(Material part)
        {
            var m = new Material(part);
            m.SetColor("_ShadeColor", Shade * part.GetColor("_Color"));
            m.SetColor("_SkinShade", SkinShade);
            m.SetColor("_RimColor", Rim);
            m.SetFloat("_RimPower", RimPower);
            m.SetFloat("_CameraLight", 1f);
            m.SetFloat("_OutlineWidth", Outline);
            // Hair and cloth cut out at _Cutoff leave MSAA their edge as coverage, or their tips stay stair-stepped.
            if (part.GetFloat("_Cutoff") > 0f && part.GetFloat("_ZWrite") > 0.5f) m.SetFloat("_AlphaToMask", 1f);
            return m;
        }

        // A VRoid face, which changes by its own blend shapes and has no picture to draw on (Editor/FaceCapture.cs).
        public bool ShapedFace => faceMesh != null;

        // The eyes, mouth and blush cells shown now, blinks and the talking mouth included.
        public Vector4 FaceCells => faceShown;

        // From the face box to the world, for Editor/FaceCapture.cs: (u, v, w) in the box's unit cube.
        public Matrix4x4 FaceToWorld => head.localToWorldMatrix * faceFromHead.inverse;

        // The middle of the face box and the way the face looks, as posed now (the 截图's phone, BattleStage's shot of it).
        public Vector3 FaceCentre => FaceToWorld.MultiplyPoint(new Vector3(0.5f, 0.5f, 0.5f));
        public Vector3 FaceForward => head.TransformDirection(faceForward).normalized;

        // One of the story's #face: tags; any other (or none) is the face as modelled.
        public void SetFace(string name) => face = name != null && faces.TryGetValue(name, out var f) ? f : (None, None, None);

        // While talking the mouth opens and closes.
        public bool Talking { get; set; }

        // What the head turns to while exploring (Explorer) or in battle (Attentive): a point, or null to look where the
        // motion looks; quick for a threat, which it snaps round to, idle for something only kept an eye on.
        public void LookAt(Vector3? point, bool quick = false, bool idle = false)
        {
            lookPoint = point;
            lookQuick = quick;
            lookIdle = idle;
        }

        void ShowFace()
        {
            var blinking = blinkLeft > 0f && face.eyes != EyesHappy && face.eyes != EyesClosed;
            var eyes = blinking ? EyesClosed : face.eyes;
            var mouth = Talking && Mathf.Repeat(talkTime, 0.24f) < 0.13f ? (face.mouth == MouthO ? MouthO : MouthOpen) : face.mouth;
            faceShown = new Vector4(eyes, mouth, face.blush, 0f);
            if (faceMesh != null)
            {
                foreach (var i in faceShapesUsed) faceMesh.SetBlendShapeWeight(i, 0f);
                foreach (var cell in new[] { eyes, mouth })
                    if (faceShapeIndex.TryGetValue(cell, out var shapes))
                        foreach (var s in shapes) faceMesh.SetBlendShapeWeight(s.index, s.weight);
                return;
            }
            if (look == null) return;
            look.SetVector("_FaceCells", faceShown);
            look.SetMatrix("_FaceFromWorld", faceFromHead * head.worldToLocalMatrix);
            look.SetVector("_FaceForward", head.TransformDirection(faceForward).normalized);
        }

        // In battle the fighting stance stands in for the model's own idle, `phase` (0 to 1) of the way round its loop
        // and at `pace` times its speed, so two figures on the same loop don't move as one. Called once the face box
        // is set, which is taken from the model's idle.
        public void Stance(float phase = 0f, float pace = 1f)
        {
            graph.Disconnect(mixer, 0);
            idle.Destroy();
            var clip = Motion(stance);
            idle = Connect(clip, 0);
            idle.SetTime(phase * clip.length);
            idle.SetSpeed(pace);
            settlePhase = phase;
            settleTime = phase * 7.3f;
            inStance = true;
            if (carriesPhone && phone == null) phone = BuildPhone();
        }

        // Plays one of the motions once over whatever the figure is doing (a loop until Stop), speed times its
        // own pace, from `from` seconds into it, fading in and out over those seconds, at most `weight` of the pose;
        // returns how long it plays, in seconds. One played while another is still on takes over from it in its
        // fade in, so a run of them (a roll into a jab into a cross) flows on without jumping.
        public float Play(string motion, float speed = 1f, float from = 0f, float fadeIn = FadeIn, float fadeOut = FadeOut, float weight = 1f)
        {
            var clip = Motion(motion);
            if (leaving.IsValid()) Release(ref leaving, leavingInput);
            var input = 0;
            if (action.IsValid())
            {
                if (actionWeight > 0f)
                {
                    leaving = action;
                    leavingInput = actionInput;
                    leavingMax = actionMax;
                    input = 1 - actionInput;
                    handover = 0f;
                }
                else Release(ref action, actionInput);
            }
            action = AnimationClipPlayable.Create(graph, clip);
            action.SetApplyFootIK(false);
            action.SetSpeed(speed);
            if (from > 0f) action.SetTime(from);
            graph.Connect(action, 0, actions, input);
            actionInput = input;
            actionName = motion;
            actionTime = 0f;
            actionFadeIn = Mathf.Max(fadeIn, 0.01f);
            actionFadeOut = Mathf.Max(fadeOut, 0.01f);
            actionMax = weight;
            actionSpeed = speed;
            holdTo = -1f;
            actionLength = clip.isLooping ? float.PositiveInfinity : Mathf.Max(0f, clip.length - from) / speed;
            // Straight from one motion into the next, without dipping back into the idle between them.
            if (actionWeight > 0f) actionWeight = Mathf.Max(actionWeight, 0.5f);
            return actionLength;
        }

        // The motion playing kept on between `clipFrom` and `clipTo` (seconds of its clip) until Release or Stop: it
        // goes round that stretch, so a pose holds and breathes (an action's two ends there are the same pose), or a
        // last frame stays put.
        public void Hold(float clipFrom, float clipTo)
        {
            if (!action.IsValid()) return;
            holdFrom = clipFrom;
            holdTo = Mathf.Max(clipTo, clipFrom + 0.001f);
            actionLength = float.PositiveInfinity;
        }

        // The motion playing held on its last `seconds` (a pose it ends in: down on one knee, lying on the seabed).
        public void HoldEnd(float seconds = 0.05f)
        {
            if (!action.IsValid()) return;
            var end = action.GetAnimationClip().length;
            Hold(end - seconds, end);
        }

        // A held motion let go: on from the end of what it held to its own end, taken over from where the hold had got
        // to in a moment, so it doesn't jump. Returns how long it has left, in seconds.
        public float Release()
        {
            if (!action.IsValid()) return 0f;
            if (holdTo < 0f) return Mathf.Max(0f, actionLength - actionTime);
            var length = Play(actionName, actionSpeed, holdTo, 0.12f, actionFadeOut, actionMax);
            return length;
        }

        // One of the battle's actions struck in place (Guard, Item, Think) as posed for this figure's stance
        // (Editor/StancePoser.cs makes GuardPlayer, GuardPlayerFemale, GuardDeepSeek and so on), played once over
        // it, or held in the pose (PoseTimes) until Release; returns how long it plays, in seconds.
        public float Act(string action, bool hold = false)
        {
            var length = Play(action + stance.Substring("Stance".Length));
            if (!hold || !PoseTimes.TryGetValue(action, out var times)) return length;
            Hold(times.Rise, times.Rise + times.Seconds);
            return float.PositiveInfinity;
        }

        // The 白米饭 as a rice ball, held for seconds over whatever the figure is doing (the 道具 action): in the
        // player's left hand, the phone being in their right, or in both of DeepSeek's.
        public void HoldRiceBall(float seconds)
        {
            if (riceBall == null) riceBall = BuildRiceBall();
            riceTime = 0f;
            riceLength = seconds;
        }

        // Where the rice ball is held now.
        public Vector3 RiceBallAt => riceBall.position;

        // How the 截图's phone is held up (RaisePhone): in both hands in front of the face; out at arm's length
        // in the right hand, turned over a little; in both hands low before the chest, tipped back to look up,
        // for a shot from down on one knee (the Kneel motion under it).
        public enum PhoneHold { Eye, Reach, Low }

        // The 截图's phone, brought up, held, and lowered again, seconds in all, over whatever the figure is doing
        // (in battle the fighting stance): the chest turns square to ahead, the arms bend to the phone and the
        // head looks out over it.
        public void RaisePhone(float seconds, PhoneHold how = PhoneHold.Eye)
        {
            if (phone == null) phone = BuildPhone();
            phoneTime = 0f;
            phoneLength = seconds;
            hold = how;
        }

        // Which way is up on the phone's screen, for the picture it takes (BattleStage.Photo).
        public Vector3 ScreenUp => phone != null && phone.gameObject.activeSelf ? phone.up : Root.up;

        // The phone's lens, where its flash goes off (SeaAvatar's map strike).
        public Vector3 PhoneLens => phone != null ? phone.position + phone.forward * (0.01f * height / 1.7f) : Root.position;

        // The player: the phone stays in their right hand in the stance, so they throw with the left.
        public bool CarriesPhone => carriesPhone;

        // The 饭勺 in DeepSeek's right hand (her normal attack), coming into it while wield, going when not.
        public void Wield(bool wield)
        {
            if (spoon == null) spoon = BuildSpoon();
            wielding = wield;
        }

        // The end of the 饭勺's bowl, for the trail its swing leaves.
        public Transform SpoonTip => (spoon != null ? spoon : spoon = BuildSpoon()).Find("Tip");

        // The 饭勺's bowl, where the blade of water its swing leaves starts (BattleEffects.Swipe).
        public Transform SpoonBowl => (spoon != null ? spoon : spoon = BuildSpoon()).Find("Bowl");

        void Release(ref AnimationClipPlayable playable, int input)
        {
            graph.Disconnect(actions, input);
            playable.Destroy();
        }

        // One of the hit reactions (HitChest, HitHead) jolting the chest and head, speed times its own pace, weight
        // of it, added over whatever the figure is doing: the legs keep their stance, the arms what they hold. The
        // clips' reference is their last frame (Editor/BattleMotions.cs), the pose they come back to.
        public void Flinch(string motion, float speed = 1f, float weight = 1f)
        {
            if (flinch.IsValid())
            {
                graph.Disconnect(layers, 2);
                flinch.Destroy();
            }
            var clip = Motion(motion);
            flinch = AnimationClipPlayable.Create(graph, clip);
            flinch.SetApplyFootIK(false);
            flinch.SetSpeed(speed);
            graph.Connect(flinch, 0, layers, 2);
            flinchTime = 0f;
            flinchLength = clip.length / speed;
            flinchWeight = weight;
        }

        // The spine, chest and head, not the arms, the legs nor where the body stands.
        static AvatarMask Torso
        {
            get
            {
                if (torso != null) return torso;
                torso = new AvatarMask { name = "Torso", hideFlags = HideFlags.DontUnloadUnusedAsset };
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                    torso.SetHumanoidBodyPartActive(part, part is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head);
                return torso;
            }
        }

        // Fades the motion playing back into the idle over fadeOut, held or not.
        public void Stop(float fadeOut = FadeOut)
        {
            if (!action.IsValid() || actionLength <= actionTime) return;
            actionFadeOut = Mathf.Max(fadeOut, 0.01f);
            actionLength = Mathf.Min(actionLength, actionTime + actionFadeOut);
        }

        // The pelvis pushed by metres (a blow, a start) and back, over rise then fall seconds; the feet stay put.
        public void Nudge(Vector3 metres, float rise, float fall)
        {
            nudge = metres;
            nudgeTime = 0f;
            nudgeRise = Mathf.Max(rise, 0.01f);
            nudgeFall = Mathf.Max(fall, 0.01f);
        }

        // Back to the plain idle at once, out of whatever it was in the middle of and with nothing in its hands: a map
        // strike cut off out of sight under the battle's wipe.
        public void Rest()
        {
            if (leaving.IsValid()) Release(ref leaving, leavingInput);
            if (action.IsValid()) Release(ref action, actionInput);
            actionWeight = actionShare = 0f;
            holdTo = -1f;
            nudge = Vector3.zero;
            if (flinch.IsValid())
            {
                graph.Disconnect(layers, 2);
                flinch.Destroy();
            }
            phoneLength = phoneWeight = 0f;
            riceLength = 0f;
            if (riceBall != null) riceBall.gameObject.SetActive(false);
            wielding = false;
            spoonShown = 0f;
        }

        // speed in metres per second: idle when still, the walk while picking up speed, then the run.
        public void Animate(float dt, float speed)
        {
            var moving = Mathf.Clamp01(speed / WalkSpeed);
            var running = Mathf.Clamp01((speed - WalkSpeed) / (RunFrom - WalkSpeed));
            mixer.SetInputWeight(0, 1f - moving);
            mixer.SetInputWeight(1, moving * (1f - running));
            mixer.SetInputWeight(2, moving * running);
            // Each at the rate whose planted foot keeps up with the ground going by, so the feet don't slide: the run
            // 1.1 to 1.25 times its own pace at 6.2 m/s, 1.6 to 1.8 at the 9 m/s sprint.
            walk.SetSpeed(Mathf.Clamp(speed / (WalkPace * stride), 0.6f, 2.2f));
            run.SetSpeed(Mathf.Clamp(speed / (RunPace * stride), 0.6f, 1.9f));
            if (action.IsValid())
            {
                actionTime += dt;
                var target = actionTime < actionLength - actionFadeOut ? 1f : 0f;
                actionWeight = Mathf.MoveTowards(actionWeight, target, dt / (target > 0f ? actionFadeIn : actionFadeOut));
                handover = Mathf.MoveTowards(handover, 1f, dt / actionFadeIn);
                if (leaving.IsValid() && handover >= 1f) Release(ref leaving, leavingInput);
                if (actionTime >= actionLength && actionWeight <= 0f)
                {
                    Release(ref action, actionInput);
                    if (leaving.IsValid()) Release(ref leaving, leavingInput);
                    holdTo = -1f;
                }
                else if (holdTo >= 0f)
                {
                    // Held: where this frame takes the clip kept within the stretch held, going round it.
                    var next = (float)action.GetTime() + dt * actionSpeed;
                    if (next >= holdTo) action.SetTime(holdFrom + Mathf.Repeat(next - holdFrom, holdTo - holdFrom) - dt * actionSpeed);
                }
            }
            else actionWeight = 0f;
            // Eased in and out rather than straight lines, so a motion doesn't start or land with a jerk.
            var over = SmoothStep(handover);
            for (var i = 0; i < 2; i++)
                actions.SetInputWeight(i, !leaving.IsValid() ? (i == actionInput ? 1f : 0f) : i == actionInput ? over : 1f - over);
            actionShare = !action.IsValid() ? 0f : SmoothStep(actionWeight) * (leaving.IsValid() ? Mathf.Lerp(leavingMax, actionMax, over) : actionMax);
            layers.SetInputWeight(0, 1f);
            layers.SetInputWeight(1, actionShare);
            if (flinch.IsValid())
            {
                flinchTime += dt;
                // In at once, out over the last of it.
                layers.SetInputWeight(2, flinchWeight * Mathf.Clamp01(Mathf.Min(flinchTime / 0.04f, (flinchLength - flinchTime) / FlinchOut)));
                if (flinchTime >= flinchLength)
                {
                    graph.Disconnect(layers, 2);
                    flinch.Destroy();
                }
            }
            graph.Evaluate(dt);
            if (Explorer) Lean(dt, speed);
            else if (inStance) Settle(dt, speed);

            if (phoneLength > 0f)
            {
                phoneTime += dt;
                var target = phoneTime < phoneLength - PhoneDown ? 1f : 0f;
                phoneWeight = Mathf.MoveTowards(phoneWeight, target, dt / (target > 0f ? PhoneUp : PhoneDown));
                if (phoneTime >= phoneLength && phoneWeight <= 0f) phoneLength = 0f;
            }
            else phoneWeight = 0f;
            var inHand = inStance && carriesPhone;
            if (phone != null) phone.gameObject.SetActive(phoneWeight > 0f || inHand);
            // Ready before the chest while only the stance plays; a motion's own arms, a walk or the 截图 take it over.
            var ready = Ready && inHand ? (1f - actionShare) * (1f - Mathf.Clamp01(speed / WalkSpeed)) : 0f;
            readyShare = Mathf.MoveTowards(readyShare, ready, dt / 0.25f);
            if (readyShare > 0f) HoldReady(Ease.InOutCubic(readyShare) * (1f - Ease.InOutCubic(phoneWeight)));
            if (phoneWeight > 0f) HoldPhone(Ease.InOutCubic(phoneWeight));
            else if (inHand)
            {
                InHand(out var at, out var turn);
                var w = Ease.InOutCubic(readyShare);
                phone.SetPositionAndRotation(Vector3.Lerp(at, readyAt, w), Quaternion.Slerp(turn, readyTurn, w));
            }
            if (Explorer || Attentive) Look(dt);
            if (spoon != null)
            {
                spoonShown = Mathf.MoveTowards(spoonShown, wielding ? 1f : 0f, dt / RiceGrow);
                spoon.gameObject.SetActive(spoonShown > 0f);
                if (spoonShown > 0f)
                {
                    SpoonInHand(out var at, out var turn);
                    spoon.SetPositionAndRotation(at, turn);
                    spoon.localScale = Vector3.one * Ease.OutBack(spoonShown);
                }
            }
            if (riceLength > 0f)
            {
                riceTime += dt;
                var shown = Mathf.Clamp01(Mathf.Min(riceTime, riceLength - riceTime) / RiceGrow);
                riceBall.gameObject.SetActive(shown > 0f);
                if (shown > 0f)
                {
                    RiceInHands(out var at, out var turn);
                    riceBall.SetPositionAndRotation(at, turn);
                    riceBall.localScale = Vector3.one * Ease.OutBack(shown);
                }
                if (riceTime >= riceLength) riceLength = 0f;
            }
            if (Explorer) Sway(dt, speed);
            LayTail(dt);
            springs?.Step(dt);
            if (Explorer) Steps(dt, speed);

            // A blink every few seconds, now and then a double one.
            blinkLeft -= dt;
            blinkIn -= dt;
            if (blinkIn <= 0f)
            {
                blinkLeft = 0.12f;
                blinkIn = UnityEngine.Random.value < 0.2f ? 0.25f : UnityEngine.Random.Range(2f, 5.5f);
            }
            talkTime = Talking ? talkTime + dt : 0f;
            ShowFace();
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        // A phone held landscape, its lens toward +z and its lit screen toward the one holding it.
        Transform BuildPhone()
        {
            var s = height / 1.7f;
            var body = Parts.Shape("Phone", Root, SeaArt.Box(new Vector3(0.15f, 0.072f, 0.011f) * s),
                SeaArt.Toon(new Color(0.16f, 0.17f, 0.21f), new Color(0.09f, 0.1f, 0.13f), Rim, default, 0f));
            Parts.Shape("Screen", body.transform, SeaArt.Quad(0.138f * s, 0.062f * s),
                SeaArt.Toon(new Color(0.55f, 0.85f, 0.95f), new Color(0.55f, 0.85f, 0.95f), Rim, new Color(0.12f, 0.3f, 0.38f), 0f), new Vector3(0f, 0f, -0.0062f * s));
            body.gameObject.SetActive(false);
            return body.transform;
        }

        // A wooden rice paddle (the 木饭勺) along +z from the grip: a round handle and a flat oval bowl, Tip at its end.
        Transform BuildSpoon()
        {
            var s = height / 1.7f;
            var wood = SeaArt.Toon(new Color(0.93f, 0.8f, 0.6f), new Color(0.7f, 0.52f, 0.4f), Rim, default, 0f);
            var paddle = Parts.Node("Spoon", Root);
            var handle = Parts.Shape("Handle", paddle, SeaArt.Cylinder(0.013f * s, 0.016f * s, 0.17f * s, 10), wood, new Vector3(0f, 0f, -0.03f * s));
            handle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var bowl = Parts.Shape("Bowl", paddle, SeaArt.Sphere(0.055f * s, 0f, 16, 10), wood, new Vector3(0f, 0f, 0.19f * s));
            bowl.transform.localScale = new Vector3(1f, 0.22f, 1.4f);
            Parts.Node("Tip", paddle, new Vector3(0f, 0f, 0.27f * s));
            paddle.gameObject.SetActive(false);
            return paddle;
        }

        // A rice ball: a white triangle a hand across, edge on to its front and back, a band of nori round its foot.
        Transform BuildRiceBall()
        {
            var s = height / 1.7f;
            float r = 0.065f * s, depth = 0.045f * s;
            var ball = Parts.Node("Rice Ball", Root);
            // The prism SeaArt.Cylinder makes along y with a corner on +x, laid along z with that corner up.
            var rice = Parts.Shape("Rice", ball, SeaArt.Cylinder(r, r, depth, 3),
                SeaArt.Toon(Color.white, new Color(0.8f, 0.83f, 0.9f), Rim, default, 0f), new Vector3(0f, -r / 4f, -depth / 2f));
            rice.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
            Parts.Shape("Nori", ball, SeaArt.Box(new Vector3(0.06f * s, 0.04f * s, depth * 1.08f)),
                SeaArt.Toon(new Color(0.12f, 0.16f, 0.14f), new Color(0.06f, 0.08f, 0.08f), Rim, default, 0f), new Vector3(0f, -0.75f * r + 0.02f * s, 0f));
            ball.gameObject.SetActive(false);
            return ball;
        }

        // Upright in the fingers of the player's left hand, or between DeepSeek's two at her chin (her battle dress
        // has no finger bones: each palm is taken a little on from the wrist along the forearm).
        void RiceInHands(out Vector3 at, out Quaternion turn)
        {
            var s = height / 1.7f;
            turn = Quaternion.LookRotation(Root.forward, Root.up);
            if (!carriesPhone)
            {
                Vector3 Palm(HumanBodyBones handBone, HumanBodyBones forearm)
                {
                    var wrist = Bone(handBone).position;
                    return wrist + (wrist - Bone(forearm).position).normalized * (0.05f * s);
                }
                // Out in front of the gloves, which would hide it held between them.
                at = (Palm(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm) + Palm(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm)) / 2f
                     + (Root.forward * 0.06f + Root.up * 0.01f) * s;
                return;
            }
            Transform hand = Bone(HumanBodyBones.LeftHand), middle = Bone(HumanBodyBones.LeftMiddleProximal),
                index = Bone(HumanBodyBones.LeftIndexProximal), little = Bone(HumanBodyBones.LeftLittleProximal);
            var fingers = (middle.position - hand.position).normalized;
            var palm = Vector3.Cross((index.position - little.position).normalized, fingers).normalized; // out of the palm, for a left hand
            at = middle.position + (fingers * 0.02f + palm * 0.05f) * s;
        }

        // The pose as evaluated, bent to hold the phone up as `hold` says, w of the way.
        void HoldPhone(float w)
        {
            Vector3 up = Root.up, ahead = Root.forward, right = Root.right;
            var s = height / 1.7f;
            // The chest turned square to ahead (the fighting stance stands side on), from the spine up.
            var spine = Bone(HumanBodyBones.Spine);
            var chest = Vector3.ProjectOnPlane(Vector3.Cross(Bone(HumanBodyBones.RightUpperArm).position - Bone(HumanBodyBones.LeftUpperArm).position, up), up);
            if (spine != null && chest.sqrMagnitude > 1e-6f)
                spine.rotation = Quaternion.AngleAxis(Mathf.Clamp(Vector3.SignedAngle(chest, ahead, up), -60f, 60f) * w, up) * spine.rotation;
            // Down on one knee the motion bows over the seabed: the back straightened up again, a little forward,
            // so the phone is held up before the chest rather than the head bent back over a bowed body.
            var neck = Bone(HumanBodyBones.Neck);
            if (hold == PhoneHold.Low && spine != null && neck != null)
                spine.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(neck.position - spine.position, up + 0.25f * ahead), w * 0.85f) * spine.rotation;

            Transform leftHand = Bone(HumanBodyBones.LeftHand), rightHand = Bone(HumanBodyBones.RightHand);
            Vector3 at, look;
            Quaternion turn;
            if (hold == PhoneHold.Reach)
            {
                // At the end of the right arm held out straight ahead, a little over the shoulder, turned over 18
                // degrees: the hand holds it by its right end, the phone out to the left of it.
                Transform upper = Bone(HumanBodyBones.RightUpperArm), lower = Bone(HumanBodyBones.RightLowerArm);
                var arm = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, rightHand.position);
                var wrist = upper.position + (ahead + 0.12f * up - 0.1f * right).normalized * (arm * 0.97f);
                turn = Quaternion.LookRotation(ahead, Quaternion.AngleAxis(-18f, ahead) * up);
                at = wrist + turn * new Vector3(-0.07f, 0.03f, 0.03f) * s;
                look = (at - FaceCentre).normalized;
            }
            else
            {
                // A forearm's length out in front of the face and a little below it, clear of the face's own camera;
                // held low, before the chest and tipped back 25 degrees.
                var low = hold == PhoneHold.Low;
                at = FaceCentre + (ahead * 0.33f - up * (low ? 0.24f : 0.11f)) * s;
                var facing = low ? Quaternion.AngleAxis(-25f, right) * ahead : ahead;
                turn = Quaternion.LookRotation(facing, Vector3.Cross(facing, right));
                look = low ? (ahead + 0.2f * up).normalized : (ahead - 0.1f * up).normalized;
            }
            // Up out of the right hand in the stance (from before the chest, held ready there), or out of nowhere
            // between the hands.
            var from = (leftHand.position + rightHand.position) / 2f;
            var fromTurn = Quaternion.LookRotation(ahead, up);
            if (inStance && carriesPhone)
            {
                InHand(out from, out fromTurn);
                var ready = Ease.InOutCubic(readyShare);
                from = Vector3.Lerp(from, readyAt, ready);
                fromTurn = Quaternion.Slerp(fromTurn, readyTurn, ready);
            }
            phone.SetPositionAndRotation(Vector3.Lerp(from, at, w), Quaternion.Slerp(fromTurn, turn, w));
            // The wrists just past the phone's two ends, a little below and behind its middle; held out, the right
            // one alone, the left staying in the stance.
            Vector3 End(float side) => at + turn * new Vector3(side * 0.095f, -0.04f, -0.015f) * s;
            Vector3 across = turn * Vector3.right;
            Reach(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, rightHand, End(1f), right, w);
            Grip(HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightMiddleProximal, across, turn, w);
            if (hold != PhoneHold.Reach)
            {
                Reach(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, leftHand, End(-1f), -right, w);
                Grip(HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftMiddleProximal, -across, turn, w);
            }

            // The head up and out over the phone, or along the arm to it.
            head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(FaceForward, look), w * 0.8f) * head.rotation;
        }

        // The stance's phone held ready (Ready), w of the way: landscape before the chest a forearm out and a hand
        // under the shoulders, tipped back a little so its lens looks at the noise bodies, a hand on each end with the
        // elbows down. Set off the shoulders, so it rides the stance's breathing and turns with the chest.
        void HoldReady(float w)
        {
            var up = Root.up;
            var s = height / 1.7f;
            Transform left = Bone(HumanBodyBones.LeftUpperArm), right = Bone(HumanBodyBones.RightUpperArm);
            var chest = Vector3.ProjectOnPlane(Vector3.Cross(right.position - left.position, up), up);
            if (chest.sqrMagnitude < 1e-6f) chest = Root.forward;
            chest.Normalize();
            var side = Vector3.Cross(up, chest);
            var facing = Quaternion.AngleAxis(-14f, side) * chest;
            readyAt = (left.position + right.position) * 0.5f + (chest * 0.3f - up * 0.2f) * s;
            readyTurn = Quaternion.LookRotation(facing, Vector3.Cross(facing, side));
            if (w <= 0f) return;
            Vector3 End(float end) => readyAt + readyTurn * new Vector3(end * 0.095f, -0.04f, -0.015f) * s;
            Reach(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, Bone(HumanBodyBones.RightHand), End(1f), side, w);
            Grip(HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightMiddleProximal, side, readyTurn, w);
            Reach(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, Bone(HumanBodyBones.LeftHand), End(-1f), -side, w);
            Grip(HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftMiddleProximal, -side, readyTurn, w);
        }

        // The head turned over the pose onto LookAt's point, the neck taking 0.4 of the turn: up to 70 degrees aside, 20 up
        // and 30 down, a point further round than 110 let go (in battle 40 aside, 15 down, so the shots framed on the
        // figures stay as they are). A VRoid face's eyes get there first and make up what the head can't, 10 degrees
        // aside and 6 up or down. Easing in and out over 0.3 s, and out while the phone is up, which has the head
        // (HoldPhone), or in battle while a motion plays. Before the springs, so the hair goes with it, and the drawn
        // face's box.
        void Look(float dt)
        {
            float aside = lookYaw, rise = lookPitch, want = 0f;
            float most = Explorer ? 70f : 40f, down = Explorer ? 30f : 15f;
            if (lookPoint.HasValue)
            {
                var to = Root.InverseTransformDirection(lookPoint.Value - FaceCentre);
                var level = new Vector2(to.x, to.z).magnitude;
                var round = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                if (level > 0.05f && Mathf.Abs(round) < 110f)
                {
                    aside = round;
                    rise = Mathf.Atan2(to.y, level) * Mathf.Rad2Deg;
                    want = Explorer ? 1f - phoneWeight : phoneWeight > 0f || actionShare > 0.2f ? 0f : 1f;
                }
            }
            var k = 1f - Mathf.Pow(0.5f, dt / (lookQuick ? 0.08f : lookIdle ? 0.4f : 0.25f));
            lookYaw = Mathf.Lerp(lookYaw, Mathf.Clamp(aside, -most, most), k);
            lookPitch = Mathf.Lerp(lookPitch, Mathf.Clamp(rise, -down, 20f), k);
            var quick = 1f - Mathf.Pow(0.5f, dt / 0.04f);
            eyeYaw = Mathf.Lerp(eyeYaw, Mathf.Clamp(aside - lookYaw, -10f, 10f), quick);
            eyePitch = Mathf.Lerp(eyePitch, Mathf.Clamp(rise - lookPitch, -6f, 6f), quick);
            lookWeight = Mathf.MoveTowards(lookWeight, want, dt / 0.3f);
            if (eyes != null)
                for (var i = 0; i < eyes.Length; i++) eyes[i].localRotation = eyeRest[i];
            if (lookWeight <= 0f) return;

            // The way the face looks now, and the turn still to make onto the wanted way.
            var w = Ease.InOutCubic(lookWeight);
            var face = Root.InverseTransformDirection(FaceForward);
            var faceLevel = new Vector2(face.x, face.z).magnitude;
            var turnAside = Mathf.DeltaAngle(Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg, lookYaw) * w;
            var turnUp = (lookPitch - Mathf.Atan2(face.y, faceLevel) * Mathf.Rad2Deg) * w;
            var up = Root.up;
            var across = Vector3.Cross(up, Root.TransformDirection(new Vector3(face.x, 0f, face.z)));
            if (across.sqrMagnitude < 1e-6f) return;
            across.Normalize();
            var neckTurn = Quaternion.AngleAxis(0.4f * turnAside, up) * Quaternion.AngleAxis(-0.4f * turnUp, across);
            if (neck != null) neck.rotation = neckTurn * neck.rotation;
            else head.rotation = neckTurn * head.rotation;
            across = neckTurn * across;
            head.rotation = Quaternion.AngleAxis(0.6f * turnAside, up) * Quaternion.AngleAxis(-0.6f * turnUp, across) * head.rotation;
            if (eyes == null) return;
            var eyeTurn = Quaternion.AngleAxis(eyeYaw * w, up) * Quaternion.AngleAxis(-eyePitch * w, Quaternion.AngleAxis(0.6f * turnAside, up) * across);
            foreach (var eye in eyes) eye.rotation = eyeTurn * eye.rotation;
        }

        // The 饭勺 out of DeepSeek's right palm along the forearm, its bowl's face turned up off the arm. Her battle
        // dress has no finger bones, so the palm is taken a little on from the wrist along the forearm.
        void SpoonInHand(out Vector3 at, out Quaternion turn)
        {
            var wrist = Bone(HumanBodyBones.RightHand).position;
            var along = (wrist - Bone(HumanBodyBones.RightLowerArm).position).normalized;
            at = wrist + along * (0.06f * height / 1.7f);
            var across = Vector3.Cross(along, Root.up);
            if (across.sqrMagnitude < 0.01f) across = Root.right;
            turn = Quaternion.LookRotation(along, Vector3.Cross(across.normalized, along));
        }

        // Gripped in the right fist of the stance (StancePoser curls those fingers): along the knuckles, its
        // middle in the curl of the fingers, the screen in to the palm.
        void InHand(out Vector3 at, out Quaternion turn)
        {
            Transform hand = Bone(HumanBodyBones.RightHand), index = Bone(HumanBodyBones.RightIndexProximal),
                little = Bone(HumanBodyBones.RightLittleProximal), middle = Bone(HumanBodyBones.RightMiddleProximal);
            var fingers = (middle.position - hand.position).normalized;
            var across = (index.position - little.position).normalized;
            var palm = Vector3.Cross(fingers, across).normalized; // out of the palm, for a right hand
            at = middle.position + palm * (0.02f * height / 1.7f);
            // Its length (x) across the fist, the lens (z) away from the palm.
            turn = Quaternion.LookRotation(-palm, Vector3.Cross(-palm, across));
        }

        // Bends the arm from upper so its hand comes to target, the elbow down and out on side; w of the way.
        void Reach(HumanBodyBones upperBone, HumanBodyBones lowerBone, Transform hand, Vector3 target, Vector3 side, float w)
        {
            Transform upper = Bone(upperBone), lower = Bone(lowerBone);
            var shoulder = upper.position;
            float a = Vector3.Distance(shoulder, lower.position), b = Vector3.Distance(lower.position, hand.position);
            var to = target - shoulder;
            var d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 0.001f, (a + b) * 0.999f);
            var dir = to.normalized;
            var bend = Vector3.ProjectOnPlane(0.6f * side - Root.up, dir).normalized;
            var along = (a * a + d * d - b * b) / (2f * d);
            var elbow = shoulder + dir * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            upper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(lower.position - shoulder, elbow - shoulder), w) * upper.rotation;
            lower.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(hand.position - lower.position, shoulder + dir * d - lower.position), w) * lower.rotation;
        }

        // Turns the hand onto the phone's end on side: the fingers up and over its top, the palm in to it, the
        // index finger's edge back toward the face. phone: the phone's turn, whose up and ahead these are.
        void Grip(HumanBodyBones handBone, HumanBodyBones indexBone, HumanBodyBones littleBone, HumanBodyBones middleBone, Vector3 side, Quaternion phone, float w)
        {
            Transform hand = Bone(handBone), index = Bone(indexBone), little = Bone(littleBone), middle = Bone(middleBone);
            if (index == null || little == null || middle == null) return;
            Vector3 up = phone * Vector3.up, ahead = phone * Vector3.forward;
            var now = Quaternion.LookRotation(middle.position - hand.position, index.position - little.position);
            var want = Quaternion.LookRotation(up + ahead - 0.4f * side, -ahead);
            hand.rotation = Quaternion.Slerp(Quaternion.identity, want * Quaternion.Inverse(now), w) * hand.rotation;
        }

        // The stance's settle (Crouch): the pelvis lowered, breathing (±1 cm at 0.27 Hz), its weight drifting from foot
        // to foot (±1.4 cm at 0.09 Hz, out of step with the 2 s loop) and nudged (Nudge), the feet then put back where
        // the motion has them so only the knees take it. Less as a motion takes over or the figure walks off.
        void Settle(float dt, float speed)
        {
            settleTime += dt;
            nudgeTime += dt;
            var w = (1f - actionShare) * (1f - Mathf.Clamp01(speed / WalkSpeed));
            if (w <= 0f) return;
            Transform hips = Bone(HumanBodyBones.Hips), left = feet[0], right = feet[1];
            Vector3 leftAt = left.position, rightAt = right.position;
            Quaternion leftTurn = left.rotation, rightTurn = right.rotation;
            var bob = -Crouch + 0.01f * Mathf.Sin(2f * Mathf.PI * 0.27f * settleTime);
            var shift = 0.014f * Mathf.Sin(2f * Mathf.PI * (0.09f * settleTime + settlePhase));
            var push = nudgeTime < nudgeRise ? Ease.OutCubic(nudgeTime / nudgeRise)
                : 1f - Mathf.SmoothStep(0f, 1f, (nudgeTime - nudgeRise) / nudgeFall);
            hips.position += (Root.up * bob + Root.right * shift + nudge * Mathf.Clamp01(push)) * w;
            Plant(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, left, leftAt, leftTurn);
            Plant(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, right, rightAt, rightTurn);
        }

        // Bends the leg from upper so its foot comes back to target and turn, the knee kept the way the pose has it
        // (only how far it bends changes).
        void Plant(HumanBodyBones upperBone, HumanBodyBones lowerBone, Transform foot, Vector3 target, Quaternion turn)
        {
            Transform upper = Bone(upperBone), lower = Bone(lowerBone);
            var hip = upper.position;
            float a = Vector3.Distance(hip, lower.position), b = Vector3.Distance(lower.position, foot.position);
            var to = target - hip;
            var d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 0.001f, (a + b) * 0.999f);
            var dir = to.normalized;
            var bend = Vector3.ProjectOnPlane(lower.position - (hip + foot.position) * 0.5f, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Root.forward, dir);
            var along = (a * a + d * d - b * b) / (2f * d);
            var knee = hip + dir * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - hip, knee - hip) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, hip + dir * d - lower.position) * lower.rotation;
            foot.rotation = turn;
        }

        static float SmoothStep(float x) => x * x * (3f - 2f * x);

        Transform Bone(HumanBodyBones bone) => Animator.GetBoneTransform(bone);

        public Transform Hips => Bone(HumanBodyBones.Hips);

        Vector3 TailPoint(int i) => i < tail.Length ? tail[i].position : tail[tail.Length - 1].TransformPoint(tailEnd);

        // A flick of the tail (in battle): thrown about `degrees` toward the figure's right (its left below 0) and
        // springing back. A tail flicks by the speed it is given, about 14 times the degrees it then swings out.
        public void Flick(float degrees) => flickSpeed += 14.4f * degrees;

        // A wag of the tail (exploring), in and out over its seconds.
        public void Wag(float seconds = 1.6f)
        {
            wagTime = 0f;
            wagLength = seconds;
        }

        void Sway(float dt, float speed)
        {
            swayTime += dt;
            swayShare = 1f - 0.7f * Mathf.Clamp01(speed / 6f);
            wagShare = 0f;
            if (wagLength > 0f)
            {
                wagTime += dt;
                wagShare = Mathf.Sin(Mathf.PI * Mathf.Clamp01(wagTime / wagLength));
                if (wagTime >= wagLength) wagLength = 0f;
            }
            if (tailSpring < 0) return;
            for (var i = 0; i < SwayLinks; i++) springs.Swing(tailSpring + i, Swayed(i, 4f, 12f));
        }

        // Degrees aside for the tail's joint i this frame, sway at most `sway` and the wag `wag`, each a little later
        // down the tail.
        float Swayed(int i, float sway, float wag) =>
            sway * swayShare * Mathf.Sin(2f * Mathf.PI * SwayHz * swayTime - 0.6f * i)
            + wag * wagShare * Mathf.Sin(2f * Mathf.PI * WagHz * wagTime - 0.6f * i);

        // Degrees forward from Root's flat acceleration (eased over 0.08 s), clamped to 9 forward and 7 back; aside into it,
        // up to 10 and only at a run; none while a motion or the phone has the body. A jump in Root's place (put
        // somewhere new) is not a speed.
        void Lean(float dt, float speed)
        {
            if (dt <= 0f) return;
            var moved = Root.position - leanWas;
            moved.y = 0f;
            leanWas = Root.position;
            var velocity = moved.sqrMagnitude < 1f ? moved / dt : Vector3.zero;
            var accel = moved.sqrMagnitude < 1f ? (velocity - leanVelocity) / dt : Vector3.zero;
            leanVelocity = velocity;
            leanAccel = Vector3.SmoothDamp(leanAccel, accel, ref leanAccelChange, 0.08f, Mathf.Infinity, dt);
            var free = 1f - Mathf.Max(phoneWeight, actionWeight);
            var pitch = free * Mathf.Clamp(Vector3.Dot(leanAccel, Root.forward) * 0.22f, -7f, 9f);
            var roll = free * Mathf.Clamp(-Vector3.Dot(leanAccel, Root.right) * 0.6f, -10f, 10f) * Mathf.Clamp01(speed / 6.2f);
            // The springs step at most a 30th of a second: past ~0.09 s this stiff and this little damped they blow up,
            // and a hitch (a talk or a menu opening) can hand a follower a frame several times that long.
            var step = Mathf.Min(dt, 1f / 30f);
            Spring(ref leanPitch, ref leanPitchSpeed, pitch, step);
            Spring(ref leanRoll, ref leanRollSpeed, roll, step);
            if (spine == null) return;
            var turn = Quaternion.AngleAxis(leanPitch * (chest != null ? 0.5f : 1f), Root.right)
                * Quaternion.AngleAxis(leanRoll * (chest != null ? 0.5f : 1f), Root.forward);
            spine.rotation = turn * spine.rotation;
            if (chest != null) chest.rotation = turn * chest.rotation;
            // The head kept half as level, so the lean reads in the body.
            head.rotation = Quaternion.AngleAxis(-0.5f * leanRoll, Root.forward) * head.rotation;
        }

        // Put down somewhere new (a teleport, beside the player after a strike, a scene's spot): the lean takes Root's
        // new place as standing still there, instead of reading the move as a burst of speed.
        public void Placed()
        {
            leanWas = Root.position;
            leanVelocity = leanAccel = leanAccelChange = Vector3.zero;
            leanPitch = leanPitchSpeed = leanRoll = leanRollSpeed = 0f;
        }

        static void Spring(ref float x, ref float v, float target, float dt)
        {
            v += (LeanSpring * LeanSpring * (target - x) - 2f * LeanDamping * LeanSpring * v) * dt;
            x += v * dt;
        }

        // A foot down where it stops sinking within a few centimetres of its height at rest, going above a walk; each
        // foot at most once a stride, so a heel's and then a toe's low point is one step.
        void Steps(float dt, float speed)
        {
            var low = footRest + 0.06f * height / 1.7f;
            for (var i = 0; i < 2; i++)
            {
                var h = Root.InverseTransformPoint(feet[i].position).y;
                footSince[i] += dt;
                if (footFalling[i] && h >= footWas[i] && footWas[i] < low && speed > 1.5f && footSince[i] > 0.18f)
                {
                    footSince[i] = 0f;
                    var at = feet[i].position;
                    Footfall?.Invoke(new Vector3(at.x, Root.position.y, at.z), speed);
                }
                footFalling[i] = h < footWas[i];
                footWas[i] = h;
            }
        }

        // The tail bent back down onto the floor over the pose, in Root's space (y up from the floor).
        void LayTail(float dt)
        {
            if (tail == null) return;
            if (tailWas == null) tailWas = new Vector3[tailRest.Length];
            var ease = 1f - Mathf.Exp(-TailFollow * dt);
            // How far the hips have turned from Root's forward, about the up axis alone.
            var ahead = Root.InverseTransformDirection(Bone(HumanBodyBones.Hips).TransformDirection(hipsAhead));
            ahead.y = 0f;
            var turn = ahead.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.forward, ahead.normalized) : Quaternion.identity;
            if (!Explorer) SwayCarried(dt);
            for (var i = 1; i < tailRest.Length; i++)
            {
                var from = Root.InverseTransformPoint(TailPoint(i - 1));
                var posed = Root.InverseTransformPoint(TailPoint(i)) - from;
                var length = posed.magnitude;
                if (i < TailCarried || length < 1e-5f)
                {
                    tailWas[i] = TailPoint(i);
                    continue;
                }
                // Toward where it was last frame, unless it was never placed or the figure jumped (into a battle); the
                // way it eases back to swung aside by the sway, more toward the end (along the floor, so it stays on it).
                var across = turn * tailHeading[i];
                // (The wag well over what should show: the ease below takes about 40% off at its pace.)
                if (Explorer) across = Quaternion.AngleAxis((0.4f + 0.6f * i / tailRest.Length) * Swayed(i, 10f, 32f), Vector3.up) * across;
                var trail = Root.InverseTransformPoint(tailWas[i]) - from;
                trail.y = 0f;
                if (trail.sqrMagnitude > 1e-8f && trail.sqrMagnitude < 9f * length * length)
                    across = Vector3.Slerp(trail.normalized, across, ease).normalized;
                var drop = from.y - tailRest[i];
                var to = Mathf.Abs(drop) >= length
                    ? from + Vector3.down * (Mathf.Sign(drop) * length)
                    : from + across * Mathf.Sqrt(length * length - drop * drop) + Vector3.down * drop;
                var bone = tail[i - 1];
                var along = Root.TransformVector(to - from);
                bone.rotation = Quaternion.FromToRotation(Root.TransformVector(posed), along) * bone.rotation;
                // Rolled about itself so its crosswise axis is level again, as at rest (not where it hangs down).
                var level = Vector3.Cross(Root.up, along);
                if (level.sqrMagnitude > 0.1f * along.sqrMagnitude)
                {
                    var side = bone.TransformDirection(tailSide[i - 1]);
                    bone.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(side, level, along), along) * bone.rotation;
                }
                tailWas[i] = Root.TransformPoint(to);
            }
        }

        // The carried joints turned about the hips' forward axis: Tail1 from its pose at rest, which swings Tail2 out
        // with it. Tail2's own bone is aimed at the third point along the floor right after (LayTail), so turning it
        // here would come to nothing; the joints laid on the floor follow where Tail2 is put, trailing.
        void SwayCarried(float dt)
        {
            var step = Mathf.Min(dt, 1f / 30f);
            carriedTime += dt;
            carriedSway = Mathf.MoveTowards(carriedSway, Lying ? 0f : 1f, 2f * dt);
            flickSpeed += (-FlickStiffness * flick - FlickDamping * flickSpeed) * step;
            flick += flickSpeed * step;
            var wave = 5f * carriedSway * Mathf.Sin(2f * Mathf.PI * CarriedSwayHz * carriedTime);
            var axis = Bone(HumanBodyBones.Hips).TransformDirection(hipsAhead);
            tail[0].localRotation = carriedRest;
            tail[0].rotation = Quaternion.AngleAxis(Mathf.Clamp(wave + flick, -FlickMost, FlickMost), axis) * tail[0].rotation;
        }

        // No foot IK on any motion: the stances are posed in the muscles alone and have no foot goals, so a
        // motion fading in over one would pull both feet up to the body while its goals blended in from none.
        AnimationClipPlayable Connect(AnimationClip clip, int input)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            graph.Connect(playable, 0, mixer, input);
            return playable;
        }

        static AnimationClip Motion(string name)
        {
            if (!motions.TryGetValue(name, out var clip) || clip == null)
                motions[name] = clip = Clip("Models/Motions/" + name);
            return clip;
        }

        // The model's idle: Motions/Idle<model> where there is one (DeepSeek's battle dress, rigged in Blender, keeps
        // the idle of the Tripo model it replaced), else the take in its file (Tripo exports one per model). The VRoid
        // models have neither and ask for another model's (idleOf).
        static AnimationClip Idle(string model)
        {
            var own = Resources.Load<AnimationClip>("Models/Motions/Idle" + model);
            return own != null ? own : Clip("Models/Characters/" + model);
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
