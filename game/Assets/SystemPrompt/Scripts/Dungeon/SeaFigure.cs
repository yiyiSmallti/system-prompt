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
        const float WalkSpeed = 1.6f;   // m/s where the walk plays at its own pace
        const float RunFrom = 3.2f;     // the run has fully taken over by here
        const float RunSpeed = 6.2f;    // and plays at its own pace here
        const float FadeIn = 0.12f, FadeOut = 0.22f; // a one-off motion over the idle, in seconds
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
        readonly AnimationLayerMixerPlayable layers; // the idle, walk and run under the motion playing over them
        readonly AnimationClipPlayable walk, run;
        AnimationClipPlayable idle, action;
        float actionTime, actionLength, actionWeight; // length infinite for a loop, which plays until Stop
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

        // The player of the chosen gender: in the lounge clothes they were pulled in with (4-lounge), or in the
        // latent-space battle coat (5-battle) that the white door's mirror shows. Each has a fighting stance of
        // their own, both with the phone in the right hand.
        public static SeaFigure Player(string gender, bool battle, Transform parent) =>
            gender == "female"
                ? new SeaFigure("PlayerFemale" + (battle ? "Battle" : ""), parent, 1.62f, "StancePlayerFemale", true)
                : new SeaFigure("PlayerMale" + (battle ? "Battle" : ""), parent, 1.72f, "StancePlayer", true);

        // DeepSeek in her school uniform under the whale hoodie (1-uniform, made in VRoid) before the lighthouse,
        // her battle dress (4-battle) after; the Tripo weekend hoodie dress (3-casual, DeepSeekCasual) is no longer
        // used. The battle dress is rigged in Blender, whose head bone sits at the top of the neck, lower under
        // the face than Tripo's: its face box rises further above the bone. The VRoid model keeps its own size
        // (151 cm, 1.64 m to the tip of the ahoge) and takes the battle dress's idle, having none of its own.
        public static SeaFigure DeepSeek(bool awakened, Transform parent) =>
            awakened ? new SeaFigure("DeepSeek", parent, 1.58f, "StanceDeepSeek", false, 0.075f)
                : new SeaFigure("DeepSeekUniform", parent, 0f, "StanceDeepSeek", false, 0.05f, "DeepSeek");

        // height: from the soles to the top, 0 for the model's own; stance: the fighting stance it takes in battle
        // (Resources/Models/Motions, Editor/StancePoser.cs), and whether it holds the 截图's phone there; faceRise:
        // the face box's centre above the head bone, of the height; idleOf: the model whose idle it takes.
        public SeaFigure(string model, Transform parent, float height, string stance, bool carriesPhone, float faceRise = FaceRise, string idleOf = null)
        {
            this.stance = stance;
            this.carriesPhone = carriesPhone;
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
            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(mixer, 0, layers, 0);
            layers.SetInputWeight(0, 1f);
            AnimationPlayableOutput.Create(graph, model, Animator).SetSourcePlayable(layers);
            idle = Connect(Idle(idleOf ?? model, path), 0);
            walk = Connect(Motion("Walk"), 1);
            run = Connect(Motion("Run"), 2);
            mixer.SetInputWeight(0, 1f);
            graph.Evaluate(0f);
            var springData = Resources.Load<TextAsset>(path + "_Springs");
            if (springData != null) springs = new SeaSprings(body.transform, Root, springData.text);

            // The face box in the idle's first frame, in Root's space (x right, y up, z ahead), then fixed to the head.
            head = Animator.GetBoneTransform(HumanBodyBones.Head);
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

        // In battle the fighting stance stands in for the model's own idle. Called once the face box is set,
        // which is taken from the model's idle.
        public void Stance()
        {
            graph.Disconnect(mixer, 0);
            idle.Destroy();
            idle = Connect(Motion(stance), 0);
            inStance = true;
            if (carriesPhone && phone == null) phone = BuildPhone();
        }

        // Plays one of the motions once over whatever the figure is doing (a loop until Stop), speed times its
        // own pace; returns how long it plays, in seconds.
        public float Play(string motion, float speed = 1f)
        {
            var clip = Motion(motion);
            if (action.IsValid())
            {
                graph.Disconnect(layers, 1);
                action.Destroy();
            }
            action = AnimationClipPlayable.Create(graph, clip);
            action.SetApplyFootIK(false);
            action.SetSpeed(speed);
            graph.Connect(action, 0, layers, 1);
            actionTime = 0f;
            actionLength = clip.isLooping ? float.PositiveInfinity : clip.length / speed;
            // Straight from one motion into the next, without dipping back into the idle between them.
            if (actionWeight > 0f) actionWeight = Mathf.Max(actionWeight, 0.5f);
            return clip.isLooping ? float.PositiveInfinity : actionLength;
        }

        // One of the battle's actions struck in place (Guard, Item, Think) as posed for this figure's stance
        // (Editor/StancePoser.cs makes GuardPlayer, GuardPlayerFemale, GuardDeepSeek and so on), played once over
        // it; returns how long it plays, in seconds.
        public float Act(string action) => Play(action + stance.Substring("Stance".Length));

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

        // The 截图's phone, brought up in both hands in front of the face, held, and lowered again, seconds in
        // all, over whatever the figure is doing (in battle the fighting stance): the chest turns square to
        // ahead, the arms bend to the phone's two ends and the head looks out over it.
        public void RaisePhone(float seconds)
        {
            if (phone == null) phone = BuildPhone();
            phoneTime = 0f;
            phoneLength = seconds;
        }

        // Fades the motion playing back into the idle.
        public void Stop()
        {
            if (action.IsValid()) actionLength = Mathf.Min(actionLength, actionTime + FadeOut);
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
            if (action.IsValid())
            {
                actionTime += dt;
                var target = actionTime < actionLength - FadeOut ? 1f : 0f;
                actionWeight = Mathf.MoveTowards(actionWeight, target, dt / (target > 0f ? FadeIn : FadeOut));
                if (actionTime >= actionLength && actionWeight <= 0f)
                {
                    graph.Disconnect(layers, 1);
                    action.Destroy();
                }
            }
            else actionWeight = 0f;
            layers.SetInputWeight(0, 1f);
            layers.SetInputWeight(1, actionWeight);
            graph.Evaluate(dt);

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
            if (phoneWeight > 0f) HoldPhone(Ease.InOutCubic(phoneWeight));
            else if (inHand)
            {
                InHand(out var at, out var turn);
                phone.SetPositionAndRotation(at, turn);
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
            LayTail(dt);
            springs?.Step(dt);

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

        // The pose as evaluated, bent to hold the phone up, w of the way.
        void HoldPhone(float w)
        {
            Vector3 up = Root.up, ahead = Root.forward, right = Root.right;
            var s = height / 1.7f;
            // The chest turned square to ahead (the fighting stance stands side on), from the spine up.
            var spine = Bone(HumanBodyBones.Spine);
            var chest = Vector3.ProjectOnPlane(Vector3.Cross(Bone(HumanBodyBones.RightUpperArm).position - Bone(HumanBodyBones.LeftUpperArm).position, up), up);
            if (spine != null && chest.sqrMagnitude > 1e-6f)
                spine.rotation = Quaternion.AngleAxis(Mathf.Clamp(Vector3.SignedAngle(chest, ahead, up), -60f, 60f) * w, up) * spine.rotation;

            // A forearm's length out in front of the face and a little below it, clear of the face's own camera.
            var at = FaceCentre + (ahead * 0.33f - up * 0.11f) * s;
            Transform leftHand = Bone(HumanBodyBones.LeftHand), rightHand = Bone(HumanBodyBones.RightHand);
            // Up out of the right hand in the stance, or out of nowhere between the hands.
            var from = (leftHand.position + rightHand.position) / 2f;
            var fromTurn = Quaternion.LookRotation(ahead, up);
            if (inStance && carriesPhone) InHand(out from, out fromTurn);
            phone.SetPositionAndRotation(Vector3.Lerp(from, at, w), Quaternion.Slerp(fromTurn, Quaternion.LookRotation(ahead, up), w));
            // The wrists just past the phone's two ends, a little below and behind its middle.
            Vector3 End(float side) => at + (side * 0.095f * right - 0.04f * up - 0.015f * ahead) * s;
            Reach(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, rightHand, End(1f), right, w);
            Reach(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, leftHand, End(-1f), -right, w);
            Grip(HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightMiddleProximal, right, w);
            Grip(HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftMiddleProximal, -right, w);

            // The head up and out over the phone.
            head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(FaceForward, (ahead - 0.1f * up).normalized), w * 0.8f) * head.rotation;
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
        // index finger's edge back toward the face.
        void Grip(HumanBodyBones handBone, HumanBodyBones indexBone, HumanBodyBones littleBone, HumanBodyBones middleBone, Vector3 side, float w)
        {
            Transform hand = Bone(handBone), index = Bone(indexBone), little = Bone(littleBone), middle = Bone(middleBone);
            if (index == null || little == null || middle == null) return;
            Vector3 up = Root.up, ahead = Root.forward;
            var now = Quaternion.LookRotation(middle.position - hand.position, index.position - little.position);
            var want = Quaternion.LookRotation(up + ahead - 0.4f * side, -ahead);
            hand.rotation = Quaternion.Slerp(Quaternion.identity, want * Quaternion.Inverse(now), w) * hand.rotation;
        }

        Transform Bone(HumanBodyBones bone) => Animator.GetBoneTransform(bone);

        Vector3 TailPoint(int i) => i < tail.Length ? tail[i].position : tail[tail.Length - 1].TransformPoint(tailEnd);

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
                // Toward where it was last frame, unless it was never placed or the figure jumped (into a battle).
                var across = turn * tailHeading[i];
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

        // The model's own idle: the take in its file (Tripo exports one per model), or for a model rigged elsewhere
        // without one, Motions/Idle<model> (DeepSeek's battle dress keeps the idle of the Tripo model it replaced).
        static AnimationClip Idle(string model, string path)
        {
            var own = Resources.Load<AnimationClip>("Models/Motions/Idle" + model);
            return own != null ? own : Clip(path);
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
