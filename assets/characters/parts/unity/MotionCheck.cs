using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using SystemPrompt.Dungeon;

// Imports the way the game's ModelAssetImporter does: a humanoid avatar from each model, clips in place,
// the battle motions cut to the part the game uses.
class MotionImport : AssetPostprocessor
{
    static readonly Dictionary<string, (float from, float to, bool loop)> Battle = new Dictionary<string, (float, float, bool)>
    {
        ["Slash"] = (0.9f, 3.6f, false), ["Cast"] = (0.8f, 4.4f, false), ["Hit"] = (0f, 0f, false),
        ["Cheer"] = (0f, 0f, true), ["Run"] = (0.02f, 0.625f, true),
    };

    // Bump when the import below changes, so cached imports are redone.
    public override uint GetVersion() => 3;

    void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        if (assetPath.StartsWith("Assets/Motions/")) importer.materialImportMode = ModelImporterMaterialImportMode.None;
    }

    void OnPreprocessAnimation()
    {
        var importer = (ModelImporter)assetImporter;
        var battle = Battle.TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out var cut) && assetPath.StartsWith("Assets/Motions/");
        var rate = importer.importedTakeInfos.Length > 0 ? importer.importedTakeInfos[0].sampleRate : 24f;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.loopTime = !battle || cut.loop;
            clip.loopPose = clip.loopTime;
            clip.lockRootRotation = clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = clip.keepOriginalPositionXZ = !battle;
            if (!battle) continue;
            clip.firstFrame = Mathf.Max(clip.firstFrame, cut.from * rate);
            if (cut.to > 0f) clip.lastFrame = Mathf.Min(clip.lastFrame, cut.to * rate);
        }
        // A humanoid clip keeps curves for bones outside the avatar (the tail) only where its mask lists them.
        // The paths are known from the first import on, so the model is imported twice.
        if (assetPath.StartsWith("Assets/DeepSeek/") && importer.transformPaths.Length > 0)
        {
            var mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
                if (part != AvatarMaskBodyPart.LastBodyPart) mask.SetHumanoidBodyPartActive(part, true);
            mask.transformCount = importer.transformPaths.Length;
            for (var i = 0; i < mask.transformCount; i++)
            {
                mask.SetTransformPath(i, importer.transformPaths[i]);
                mask.SetTransformActive(i, true);
            }
            foreach (var clip in clips)
            {
                clip.maskType = ClipAnimationMaskType.CreateFromThisModel;
                clip.ConfigureClipFromMask(mask);
            }
        }
        importer.clipAnimations = clips;
    }
}

// Plays every humanoid clip on the DeepSeek avatar and renders frames. Each Tripo clip gets a second layer with
// DeepSeek's own walk or run masked to the tail, the way the game would keep the tail moving under any body clip.
// Clips are stepped at 60 Hz so TailDynamics runs as in play; every frame is shot before it (the tail as the clips
// leave it) and after, and report.txt has the tail's worst floor, leg and forward clearance over each clip.
public static class MotionCheck
{
    const string ModelPath = "Assets/DeepSeek/deepseek-battle.fbx";
    const int Cols = 6, W = 520, H = 640;
    // The run with the root moving the way DungeonWorld moves the player, instead of in place: straight, a quarter turn
    // to the left at the player's turn rate, straight on, then braking to a stop with the pose held where it stops, to
    // see the tail lag through the turn, swing past and settle.
    const float RunSpeed = 6.2f, TurnRate = 760f, Braking = 42f, BrakeAt = 0.6f, TurnLength = 1.4f;
    static readonly float[] TurnShots = { 0.05f, 0.2f, 0.35f, 0.6f, 0.75f, 1f };

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var outDir = args[Array.IndexOf(args, "-out") + 1];
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        // The FBX carries its textures inside; the materials only find them once they are files in the project.
        if (!Directory.Exists("Assets/DeepSeek/Textures"))
        {
            ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).ExtractTextures("Assets/DeepSeek/Textures");
            AssetDatabase.Refresh();
        }
        if (!AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Any(c => AnimationUtility.GetCurveBindings(c).Any(b => b.path.Contains("Tail"))))
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First();
        log.AppendLine($"avatar valid {avatar.isValid}, human {avatar.isHuman}");
        var human = avatar.humanDescription.human.ToDictionary(h => h.humanName, h => h.boneName);
        log.AppendLine($"mapped {human.Count} of {HumanTrait.BoneCount} human bones");
        log.AppendLine("unmapped: " + string.Join(", ", HumanTrait.BoneName.Where(b => !human.ContainsKey(b))));
        foreach (var kv in human.OrderBy(k => k.Key)) log.AppendLine($"  {kv.Key} -> {kv.Value}");

        var own = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        foreach (var c in own)
        {
            var tail = AnimationUtility.GetCurveBindings(c).Count(b => b.path.Contains("Tail"));
            log.AppendLine($"own clip {c.name}: {c.length:F2}s, humanMotion {c.humanMotion}, tail curves {tail}");
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);
        var light = new GameObject("key").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.9f;
        light.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
        var fill = new GameObject("fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.45f;
        fill.transform.rotation = Quaternion.Euler(20f, -60f, 0f);

        var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        // the skinned renderers are hidden behind baked copies below, and an imported Animator stops moving the bones
        // of a model it thinks is off screen
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // edit mode calls no Awake, Update or LateUpdate: the loop below calls Restore and Simulate itself
        var tailDynamics = go.AddComponent<TailDynamics>();
        tailDynamics.Init();
        var hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        var hipsRest = Quaternion.Inverse(go.transform.rotation) * hipsBone.rotation;
        var tailSkin = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.StartsWith("tail"));
        var tailTip = tailSkin.bones.First(b => b.name == "Tail_15");
        var tailMesh = new Mesh { indexFormat = tailSkin.sharedMesh.indexFormat };
        var hipsForward = Quaternion.Inverse(hipsBone.rotation) * go.transform.forward;
        // only the tail below the skirt counts for the legs: vertices led by Tail_4 and on, and the fins
        var outside = tailSkin.sharedMesh.boneWeights.Select(bw =>
        {
            var bone = tailSkin.bones[bw.boneIndex0].name;
            return bone.StartsWith("TailFin") || bone.StartsWith("Tail_") && int.Parse(bone.Substring(5)) >= 4;
        }).ToArray();
        var legs = new[]
        {
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.075f), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.06f),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.075f), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.06f),
        }.Select(l => (a: animator.GetBoneTransform(l.Item1), b: animator.GetBoneTransform(l.Item2), r: l.Item3)).ToArray();
        // The tail mesh as posed now: lowest point (below 0 is under the floor), deepest point inside a leg, and how
        // far ahead of the hips it reaches, the way they face (at rest it is all behind them); plus the hips' tilt.
        (float floor, float leg, float front, float tilt) Measure()
        {
            tailSkin.BakeMesh(tailMesh, true);
            var tr = tailSkin.transform;
            var fwd = hipsBone.rotation * hipsForward;
            fwd.y = 0f;
            fwd.Normalize();
            var hp = hipsBone.position;
            var seg = legs.Select(l => (a: l.a.position, b: l.b.position, l.r)).ToArray();
            float floor = float.MaxValue, leg = 0f, front = float.MinValue;
            var verts = tailMesh.vertices;
            for (var i = 0; i < verts.Length; i++)
            {
                var w = tr.position + tr.rotation * verts[i];
                floor = Mathf.Min(floor, w.y);
                front = Mathf.Max(front, Vector3.Dot(w - hp, fwd));
                if (!outside[i]) continue;
                foreach (var (a, b, r) in seg)
                {
                    var ab = b - a;
                    var c = a + ab * Mathf.Clamp01(Vector3.Dot(w - a, ab) / ab.sqrMagnitude);
                    leg = Mathf.Max(leg, r - (w - c).magnitude);
                }
            }
            var rel = Quaternion.Inverse(go.transform.rotation) * hipsBone.rotation * Quaternion.Inverse(hipsRest);
            var tilt = Vector3.Angle(rel * Vector3.up, Vector3.up);
            return (floor, leg, front, tilt);
        }
        // the model comes in posed at the first frame of its first take, tail included
        tailDynamics.Restore();
        var restMeasure = Measure();
        log.AppendLine($"tail at rest: lowest {restMeasure.floor:F3} m, in legs {restMeasure.leg:F3} m, ahead of hips {restMeasure.front:F3} m");
        // The FBX materials come in as Standard: emission colours arrive without their maps (the tail glows white)
        // and the face decals lose their alpha blend. Fixed here for the check only; the game uses its own shader.
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials.Select(m => new Material(m)).ToArray();
            foreach (var m in mats)
            {
                if (m.GetTexture("_EmissionMap") == null) { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
                if (!new[] { "eye", "brow", "blush", "mouth" }.Any(r.name.StartsWith)) continue;
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.renderQueue = 3000;
            }
            r.sharedMaterials = mats;
        }
        // cam.Render() from an editor script does not run the player loop's skinning step, so a SkinnedMeshRenderer
        // shows a stale pose, not the one just evaluated. Each part is drawn from a mesh baked from the current bones.
        var proxies = new List<(SkinnedMeshRenderer skin, Transform t, Mesh mesh)>();
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var p = new GameObject(s.name + "_baked");
            var mesh = new Mesh { indexFormat = s.sharedMesh.indexFormat };
            p.AddComponent<MeshFilter>().sharedMesh = mesh;
            p.AddComponent<MeshRenderer>().sharedMaterials = s.sharedMaterials;
            s.forceRenderingOff = true;
            proxies.Add((s, p.transform, mesh));
        }
        void Bake()
        {
            foreach (var (skin, t, mesh) in proxies)
            {
                skin.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                t.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            }
        }
        var bounds = go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        log.AppendLine($"rest bounds size {bounds.size}, renderers {go.GetComponentsInChildren<Renderer>().Length}");

        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.42f, 0.44f, 0.48f);
        cam.fieldOfView = 32f;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt;
        var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.6f, 0.62f, 0.66f) };

        var tailMask = new AvatarMask();
        foreach (AvatarMaskBodyPart p in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            if (p != AvatarMaskBodyPart.LastBodyPart) tailMask.SetHumanoidBodyPartActive(p, false);
        tailMask.AddTransformPath(go.transform, true);
        for (var i = 0; i < tailMask.transformCount; i++)
            tailMask.SetTransformActive(i, tailMask.GetTransformPath(i).Contains("Tail"));

        AnimationClip OwnClip(string name) => own.FirstOrDefault(c => c.name.EndsWith(name));
        var jobs = new List<(string label, AnimationClip body, AnimationClip tail, bool turn)>();
        foreach (var c in own) jobs.Add(("DeepSeek " + c.name.Split('|').Last(), c, null, false));
        jobs.Add(("DeepSeek Run turn", OwnClip("Run"), null, true));
        foreach (var m in new[] { "Walk", "Run", "Cheer", "Cast", "Slash", "Hit" })
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"Assets/Motions/{m}.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            jobs.Add(("Tripo " + m, clip, OwnClip(m == "Run" ? "Run" : "Walk"), false));
        }

        void Snap(string file)
        {
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, file), shot.EncodeToPNG());
        }

        foreach (var (label, body, tail, turn) in jobs)
        {
            var graph = PlayableGraph.Create(label);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var bodyPlay = AnimationClipPlayable.Create(graph, body);
            bodyPlay.SetApplyFootIK(true);
            AnimationClipPlayable tailPlay = default;
            if (tail != null)
            {
                var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
                tailPlay = AnimationClipPlayable.Create(graph, tail);
                graph.Connect(bodyPlay, 0, mixer, 0);
                graph.Connect(tailPlay, 0, mixer, 1);
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 1f);
                mixer.SetLayerMaskFromAvatarMask(1, tailMask);
                output.SetSourcePlayable(mixer);
            }
            else output.SetSourcePlayable(bodyPlay);

            const float Dt = 1f / 60f;
            var name = label.Replace(' ', '_');
            // a loop plays once before the frames are taken, and a one-shot holds its first pose for half a second, so the
            // tail has settled into it
            var start = body.isLooping ? -body.length : -0.5f;
            var length = turn ? TurnLength : body.length;
            var steps = Mathf.RoundToInt((length - start) / Dt);
            var shotAt = turn ? TurnShots : Enumerable.Range(0, Cols).Select(c => body.length * c / Cols).ToArray();
            var root = turn ? new Vector3(2.5f, 0f, -6f) : Vector3.zero;
            var speed = RunSpeed;
            var held = 0f;
            // the turn: how far the tip, seen from above, swings off where the clip alone puts it; + trails the turn
            var swing = new List<(float time, float angle)>();
            Vector3 TipFromHips() => Vector3.ProjectOnPlane(tailTip.position - hipsBone.position, Vector3.up);
            var worst = new (float floor, float leg, float front, float tilt)[2];
            for (var w = 0; w < 2; w++) worst[w] = (float.MaxValue, 0f, float.MinValue, 0f);
            void Keep(int w, (float floor, float leg, float front, float tilt) m) => worst[w] = (Mathf.Min(worst[w].floor, m.floor),
                Mathf.Max(worst[w].leg, m.leg), Mathf.Max(worst[w].front, m.front), Mathf.Max(worst[w].tilt, m.tilt));
            // how far the tip swings about the hips, sideways and up and down in the character's frame: whether the
            // dynamics keep the clip's wave or smooth it away
            var sway = new Bounds?[2];
            void Sway(int w)
            {
                var tip = go.transform.InverseTransformVector(tailTip.position - hipsBone.position);
                if (sway[w] is Bounds b) { b.Encapsulate(tip); sway[w] = b; }
                else sway[w] = new Bounds(tip, Vector3.zero);
            }
            void Shots(string suffix)
            {
                Bake();
                var hips = hipsBone.position;
                var aim = new Vector3(hips.x, 0.78f, hips.z);
                // three-quarter view from the front left, and her left side, following the hips
                cam.transform.position = aim + new Vector3(1.55f, 0.25f, 2.75f);
                cam.transform.LookAt(aim);
                Snap($"{name}_{suffix}.png");
                cam.transform.position = aim + new Vector3(-3.1f, 0.2f, 0f);
                cam.transform.LookAt(aim);
                Snap($"{name}_{suffix}_side.png");
                if (!turn) return;
                cam.transform.SetPositionAndRotation(new Vector3(hips.x, 5.5f, hips.z), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                Snap($"{name}_{suffix}_top.png");
            }
            tailDynamics.Snap();
            for (int k = 0, col = 0; k <= steps && col < Cols; k++)
            {
                var time = start + k * Dt;
                var clipTime = time;
                var take = time >= shotAt[col] - 1e-4f;
                if (turn)
                {
                    var heading = Quaternion.Euler(0f, -Mathf.Clamp(time * TurnRate, 0f, 90f), 0f);
                    if (time > BrakeAt) speed = Mathf.Max(0f, speed - Braking * Dt);
                    if (k > 0) root += heading * Vector3.forward * (speed * Dt);
                    go.transform.SetPositionAndRotation(root, heading);
                    if (speed > 0f) held = time;
                    clipTime = held;
                }
                bodyPlay.SetTime(body.isLooping ? Mathf.Repeat(clipTime, body.length) : Mathf.Clamp(clipTime, 0f, body.length));
                if (tail != null) tailPlay.SetTime(Mathf.Repeat(clipTime, tail.length));
                tailDynamics.Restore();
                graph.Evaluate(0f);
                var measure = time >= 0f && (take || k % 4 == 0);
                if (measure) { Keep(0, Measure()); Sway(0); }
                if (take) Shots($"{col}_before");
                var clipTip = TipFromHips();
                tailDynamics.Simulate(Dt);
                if (measure) { Keep(1, Measure()); Sway(1); }
                if (turn && time >= 0f) swing.Add((time, Vector3.SignedAngle(clipTip, TipFromHips(), Vector3.up)));
                if (!take) continue;
                Shots($"{col}");
                // close-up of the hand nearer the camera, to see the fingers the clip drives
                cam.transform.position = hipsBone.position + new Vector3(1.55f, 0.25f, 2.75f);
                var from = cam.transform.position;
                var hand = new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal }
                    .Select(b => animator.GetBoneTransform(b).position).OrderBy(p => (p - from).sqrMagnitude).First();
                cam.transform.position = hand + (from - hand).normalized * 0.4f;
                cam.transform.LookAt(hand);
                Snap($"{name}_{col}_hand.png");
                col++;
            }
            for (var w = 0; w < 2; w++)
                log.AppendLine($"  {label} tail {(w == 0 ? "from the clips" : "with TailDynamics")}: lowest {worst[w].floor:F3} m, " +
                               $"in legs {worst[w].leg:F3} m, ahead of hips {worst[w].front:F3} m, hips tilt up to {worst[w].tilt:F0} deg, " +
                               $"tip swings {sway[w].Value.size.x * 100:F0} cm sideways and {sway[w].Value.size.y * 100:F0} cm up and down");
            if (turn)
            {
                var running = swing.Where(s => s.time <= BrakeAt).ToList();
                var peak = running.OrderByDescending(s => s.angle).First();
                var past = running.Where(s => s.time > peak.time).Min(s => s.angle);
                var settled = running.LastOrDefault(s => Mathf.Abs(s.angle) > 5f).time;
                var braking = swing.Where(s => s.time > BrakeAt).Max(s => Mathf.Abs(s.angle));
                log.AppendLine($"  {label} tail tip against the clip's: up to {peak.angle:F0} deg behind at {peak.time:F2}s, " +
                               $"{-past:F0} deg past, within 5 deg after {settled:F2}s; braking moves it up to {braking:F0} deg");
            }
            log.AppendLine($"rendered {label}: {body.length:F2}s");
            graph.Destroy();
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        File.WriteAllText(Path.Combine(outDir, "report.txt"), log.ToString());
        Debug.Log("MotionCheck done\n" + log);
    }
}
