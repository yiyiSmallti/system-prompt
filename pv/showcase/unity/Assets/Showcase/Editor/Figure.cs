using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Showcase
{
    // 母作的一个角色模型，照游戏里 Dungeon/SeaFigure.cs 的做法摆出来：按身高缩放、朝 +z，换成三渲二着色器，
    // 表情图集从正面投到脸上。动作按给定的时刻取样，不跟真实时间走，同一份脚本每次渲染出的帧都一样。
    // 批处理里不跑播放循环，SkinnedMeshRenderer 不会重新蒙皮，所以每帧把蒙皮结果烘成普通网格来画
    // （同衍生作 PV 的 Figure.cs）。
    sealed class Figure
    {
        static readonly Color Shade = new Color(0.78f, 0.74f, 0.8f);
        static readonly Color SkinShade = new Color(0.93f, 0.78f, 0.75f);
        const float RimPower = 8f;
        // 脸的取景框，按身高的比例，同 SeaFigure。
        const float FaceSpan = 0.15f, FaceRise = 0.02f, FaceFront = 0.12f, FaceDepth = 0.2f;
        const int EyesClosed = 0, EyesHappy = 1, EyesHalf = 2, EyesSmall = 3,
            MouthSmile = 4, MouthOpen = 5, MouthPout = 6, MouthO = 7, Blush = 8, None = -1;
        // 剧本的 #face: 标签和战斗里的表情，同 SeaFigure。
        public static readonly Dictionary<string, (int eyes, int mouth, int blush)> Faces = new Dictionary<string, (int, int, int)>
        {
            ["neutral"] = (None, None, None),
            ["smile"] = (EyesHappy, MouthSmile, Blush),
            ["pout"] = (EyesHalf, MouthPout, Blush),
            ["scared"] = (EyesSmall, MouthO, None),
            ["sleepy"] = (EyesHalf, None, None),
            ["concerned"] = (EyesHalf, MouthPout, None),
            ["hurt"] = (EyesClosed, MouthPout, None),
            ["down"] = (EyesClosed, None, None),
            ["shout"] = (None, MouthOpen, None),
        };
        // 投不投表情。表情图集（游戏的 Tools/face_expressions.py）里眼皮和嘴的底色比渲染出来的皮肤白，近景里成了
        // 贴在脸上的白斑，眨眼时眼皮也是白的，所以展示片先用模型自己画的脸；图集和皮肤对上色以后再打开。
        public static readonly bool Expressions = false;
        // 三渲二（母作的 Toon）还是写实光照（Showcase/Lit，主光带软阴影）。
        public static readonly bool Toon = false;
        // 模型自带的待机在 Pose 里的名字。
        public const string Own = "Own";

        public readonly string Model;
        public readonly Transform Root;
        public readonly float Height;
        // 补上的尾骨，只有 DeepSeek 战斗服有（Tail.cs），其余为 null。
        readonly Tail tail;
        // 手臂避开衣服（Arms.cs）。
        readonly Arms arms;
        readonly GameObject body;
        readonly Animator animator;
        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable mixer;
        readonly string path;
        readonly Dictionary<string, (AnimationClipPlayable clip, int input)> motions = new Dictionary<string, (AnimationClipPlayable, int)>();
        public readonly Material Look;
        readonly (SkinnedMeshRenderer skin, Mesh baked, MeshRenderer shown)[] bakes;
        readonly Transform head;
        readonly Matrix4x4 faceFromHead;
        readonly Vector3 faceForward;
        // 眨眼的时刻（秒），按模型名定下来，每次渲染都一样。
        readonly float[] blinks;

        // darkGreys：贴图里不是皮肤的中性灰压暗。主角男版战斗服后脑的黑发经 Tripo 去光照成了灰色，设定是黑发（#1F2026），
        // 衍生作 PV 的 Figure.cs 在载入时压回去，这里照做；外套本来就近黑，绿色滚边和皮肤不受影响。
        public Figure(string model, float height, Transform parent, bool tailBones = false, bool darkGreys = false)
        {
            Model = model;
            Height = height;
            Root = new GameObject(model).transform;
            Root.SetParent(parent, false);
            path = "Models/Characters/" + model;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"找不到角色模型 Resources/{path}");
            body = Object.Instantiate(prefab, Root, false);
            body.name = "Model";
            animator = body.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new InvalidOperationException($"角色模型 {model} 没有人形骨骼（看 SystemPrompt/Editor/ModelAssetImporter.cs 是否生效）");
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var texture = Resources.Load<Texture2D>(path + "_BaseColor");
            if (texture == null) throw new InvalidOperationException($"找不到角色贴图 Resources/{path}_BaseColor");
            Look = new Material(Stage.Shader(Toon ? "Toon" : "Lit"));
            Look.SetColor("_Color", Color.white);
            Look.SetFloat("_SkinMask", 1f);
            if (Toon)
            {
                Look.SetColor("_ShadeColor", Shade);
                Look.SetFloat("_OutlineWidth", 0f); // 生成模型的刘海和下巴外壳会变成压在脸上的粗黑线，同 SeaFigure
                Look.SetFloat("_RimPower", RimPower);
                Look.SetColor("_SkinShade", SkinShade);
                Look.SetFloat("_CameraLight", 1f);
            }
            Rim(new Color(0.5f, 0.92f, 1f, 0.35f));

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
                r.sharedMaterials = Enumerable.Repeat(Look, r.sharedMesh.subMeshCount).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.updateWhenOffscreen = true;
                r.forceRenderingOff = true;
            }
            if (renderers.Length == 0 || high <= low) throw new InvalidOperationException($"角色模型 {model} 没有蒙皮网格");
            foreach (var r in renderers) Uncross(r);
            bakes = new (SkinnedMeshRenderer, Mesh, MeshRenderer)[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                var baked = new Mesh { name = renderers[i].name + " Baked", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                baked.MarkDynamic();
                var shown = new GameObject("Baked").AddComponent<MeshRenderer>();
                shown.transform.SetParent(Root, false);
                shown.gameObject.AddComponent<MeshFilter>().sharedMesh = baked;
                shown.sharedMaterials = renderers[i].sharedMaterials;
                shown.shadowCastingMode = Toon ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                shown.receiveShadows = !Toon;
                bakes[i] = (renderers[i], baked, shown);
            }
            var scale = height / (high - low);
            body.transform.localScale *= scale;
            body.transform.localPosition = new Vector3(0f, -low * scale, 0f);

            graph = PlayableGraph.Create("Showcase " + model);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 0);
            AnimationPlayableOutput.Create(graph, model, animator).SetSourcePlayable(mixer);
            Pose((Own, 0f, 1f));

            // 脸的取景框：在自带待机的第一帧里量，之后钉在头骨上（同 SeaFigure）。
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            var at = Root.InverseTransformPoint(head.position);
            float span = FaceSpan * height, depth = FaceDepth * height, centre = at.y + FaceRise * height, front = at.z + FaceFront * height;
            var faceFromRoot = new Matrix4x4(
                new Vector4(-1f / span, 0f, 0f, 0f),
                new Vector4(0f, 1f / span, 0f, 0f),
                new Vector4(0f, 0f, -1f / depth, 0f),
                new Vector4(0.5f + at.x / span, 0.5f - centre / span, front / depth, 1f));
            faceFromHead = faceFromRoot * Root.worldToLocalMatrix * head.localToWorldMatrix;
            faceForward = head.InverseTransformDirection(Root.forward);

            var atlas = Resources.Load<Texture2D>(path + "_Faces");
            var faceDepth = Resources.Load<Texture2D>(path + "_FaceDepth");
            if (atlas == null || faceDepth == null) throw new InvalidOperationException($"找不到 {model} 的表情图集或正脸深度");

            // 贴图的修整要知道脸在贴图上是哪几块，所以放在量完脸之后。
            var eyes = Eyes(atlas);
            Look.mainTexture = Remap(texture, (px, w) =>
            {
                // 先补皮肤上的杂点，再压暗灰色：反过来的话，脸上的灰点不算皮肤，会被压成黑点。
                var face = FaceTexels(renderers, faceFromRoot * Root.worldToLocalMatrix, eyes, w, px.Length / w);
                var count = Despeckle(px, w, null) + Despeckle(px, w, null);
                for (var pass = 0; pass < 4; pass++) count += Despeckle(px, w, face);
                Debug.Log($"{model}：补了皮肤上 {count} 个浅色杂点（脸占贴图 {face.Count(x => x)} 个像素）");
                if (!darkGreys) return;
                for (var i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    Color.RGBToHSV(c, out var h, out var s, out var v);
                    var grey = (1f - Stage.Smooth(0.12f, 0.22f, s)) * (1f - Stage.Smooth(0.05f, 0.15f, c.a));
                    var dark = Color.HSVToRGB(h, s, v * Mathf.Lerp(1f, 0.36f, grey * Stage.Smooth(0.18f, 0.4f, v)));
                    dark.a = c.a;
                    px[i] = dark;
                }
            });

            Look.SetTexture("_Faces", atlas);
            Look.SetTexture("_FaceDepth", faceDepth);
            Look.SetFloat("_FaceOn", Expressions ? 1f : 0f);

            // 每 2 到 5.5 秒眨一次眼，偶尔连眨两下，同 SeaFigure。
            var random = new System.Random(model.Aggregate(17, (h, c) => h * 31 + c));
            var list = new List<float>();
            for (var t = 0.6f + (float)random.NextDouble() * 1.5f; t < 400f;)
            {
                list.Add(t);
                t += random.NextDouble() < 0.2 ? 0.25f : 2f + 3.5f * (float)random.NextDouble();
            }
            blinks = list.ToArray();

            if (tailBones)
            {
                if (renderers.Length != 1) throw new InvalidOperationException($"角色模型 {model} 有 {renderers.Length} 个蒙皮网格，补尾骨只认一个");
                tail = new Tail(this, renderers[0]);
            }
            arms = new Arms(this, renderers);
            Face("neutral", -1f);
            Commit();
        }

        public Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);

        static readonly HumanBodyBones[][] Legs =
        {
            new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes },
            new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes },
        };

        // Tripo 绑骨时两只脚靠得近的（DeepSeek 战斗服的绑定姿势里两只靴子只隔 9 厘米），一边靴子上的顶点分到了另一条腿的
        // 骨头，两腿一分开，靴底被另一只脚拽过去，拉成扁平的一片。这些顶点另一条腿上的权重换给这边对应的骨头。哪边：小腿
        // 以下（身高的两成）两只脚是分开的几块网格，整块按离哪只脚近算（靴子内侧离两只脚一样近，单看顶点分不出来；
        // 两边一样近的一块是垂在中间的尾巴，不动）；再往上看顶点自己，离这条腿不到离另一条的七成才算。
        void Uncross(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            var bindposes = mesh.bindposes;
            var chains = Legs.Select(chain => chain.Select(b => Array.IndexOf(bones, animator.GetBoneTransform(b))).ToArray()).ToArray();
            var segments = chains.Select(chain =>
            {
                var at = chain.Where(i => i >= 0).Select(i => bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero)).ToArray();
                return Enumerable.Range(0, at.Length - 1).Select(k => (at[k], at[k + 1])).ToArray();
            }).ToArray();
            if (segments.Any(s => s.Length == 0)) return;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            int Side(Vector3 p)
            {
                var d = segments.Select(s => s.Min(seg => Distance(p, seg.Item1, seg.Item2))).ToArray();
                return d[0] < 0.7f * d[1] ? 0 : d[1] < 0.7f * d[0] ? 1 : -1;
            }
            var up = (segments[0][0].Item1 - segments[0][segments[0].Length - 1].Item2).normalized;
            var heights = vertices.Select(p => Vector3.Dot(p, up)).ToArray();
            var lowest = heights.Min();
            var cut = lowest + 0.2f * (heights.Max() - lowest);
            var part = Tail.Parts(mesh, vertices, v => heights[v] < cut);
            var sides = Enumerable.Range(0, vertices.Length).Where(v => heights[v] < cut).GroupBy(v => part[v])
                .ToDictionary(g => g.Key, g => Side(g.Aggregate(Vector3.zero, (sum, v) => sum + vertices[v]) / g.Count()));
            var moved = 0;
            var amount = 0f;
            for (var v = 0; v < vertices.Length; v++)
            {
                var own = heights[v] < cut ? sides[part[v]] : Side(vertices[v]);
                if (own < 0) continue;
                var other = chains[1 - own];
                var total = new Dictionary<int, float>();
                var changed = 0f;
                void Add(int bone, float w)
                {
                    if (w <= 0f) return;
                    var k = Array.IndexOf(other, bone);
                    if (k >= 0 && chains[own][k] >= 0) { bone = chains[own][k]; changed += w; }
                    total[bone] = total.GetValueOrDefault(bone) + w;
                }
                var w0 = weights[v];
                Add(w0.boneIndex0, w0.weight0);
                Add(w0.boneIndex1, w0.weight1);
                Add(w0.boneIndex2, w0.weight2);
                Add(w0.boneIndex3, w0.weight3);
                if (changed <= 0f) continue;
                moved++;
                amount += changed;
                var top = total.OrderByDescending(x => x.Value).Take(4).ToArray();
                var sum = top.Sum(x => x.Value);
                var w = new BoneWeight();
                if (top.Length > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / sum; }
                if (top.Length > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / sum; }
                if (top.Length > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / sum; }
                if (top.Length > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / sum; }
                weights[v] = w;
            }
            Debug.Log($"{Model}：{moved} 个顶点挂着另一条腿的权重（合计 {amount:0.0}），换回这边");
            if (moved == 0) return;
            mesh = Object.Instantiate(mesh);
            mesh.name = skin.sharedMesh.name + " Uncrossed";
            mesh.boneWeights = weights;
            skin.sharedMesh = mesh;
        }

        static float Distance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).magnitude;
        }

        public void Show(bool on) => Root.gameObject.SetActive(on);

        // 边缘光的颜色（a 是强度），跟着场景的主题色换。
        public void Rim(Color rim) => Look.SetColor("_RimColor", rim);

        // 混合几段动作取样：每段播到第几秒、占多少比重，比重按总和归一；没列出的动作不参与。
        // 循环的动作按自己的长度绕回，只播一次的停在首尾。
        public void Pose(params (string motion, float time, float weight)[] layers)
        {
            var total = layers.Sum(l => Mathf.Max(0f, l.weight));
            if (total <= 0f) throw new ArgumentException("Pose 至少要有一段比重大于 0 的动作");
            foreach (var (_, input) in motions.Values) mixer.SetInputWeight(input, 0f);
            foreach (var (motion, time, weight) in layers)
            {
                if (weight <= 0f) continue;
                var (clip, input) = Motion(motion);
                var c = clip.GetAnimationClip();
                clip.SetTime(c.isLooping ? Mathf.Repeat(time, c.length) : Mathf.Clamp(time, 0f, c.length));
                mixer.SetInputWeight(input, mixer.GetInputWeight(input) + weight / total);
            }
            graph.Evaluate(0f);
            tail?.Reset();
            arms?.Clear();
        }

        public float Length(string motion) => Motion(motion).clip.GetAnimationClip().length;

        // 一段动作里上身平均朝哪边（度，0 是 Root 的 +z，往 +x 为正），由两肩算。格斗架势是侧身的，
        // 分屏时按它转回四分之三正面。只播一次的只看开头的架势。
        public float Turn(string motion)
        {
            var span = Loops(motion) ? Length(motion) : Mathf.Min(0.4f, Length(motion));
            var sum = Vector3.zero;
            for (var i = 0; i < 12; i++)
            {
                Pose((motion, span * i / 12f, 1f));
                var across = Bone(HumanBodyBones.RightUpperArm).position - Bone(HumanBodyBones.LeftUpperArm).position;
                sum += Root.InverseTransformDirection(Vector3.Cross(across, Vector3.up));
            }
            return Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
        }

        // 一段动作里腰平均偏离原点多少（Root 的本地 xz），展示时扣掉，人站回圆台上。
        readonly Dictionary<string, Vector3> drifts = new Dictionary<string, Vector3>();

        public Vector3 Drift(string motion)
        {
            if (drifts.TryGetValue(motion, out var d)) return d;
            var sum = Vector3.zero;
            const int n = 24;
            for (var i = 0; i < n; i++)
            {
                Pose((motion, Length(motion) * i / n, 1f));
                sum += Root.InverseTransformPoint(Hips);
            }
            sum /= n;
            return drifts[motion] = new Vector3(sum.x, 0f, sum.z);
        }

        public bool Loops(string motion) => Motion(motion).clip.GetAnimationClip().isLooping;

        (AnimationClipPlayable clip, int input) Motion(string motion)
        {
            if (motions.TryGetValue(motion, out var found)) return found;
            var clip = AnimationClipPlayable.Create(graph, Clip(motion == Own ? path : "Models/Motions/" + motion));
            clip.SetApplyFootIK(true);
            var input = mixer.GetInputCount();
            mixer.SetInputCount(input + 1);
            graph.Connect(clip, 0, mixer, input);
            return motions[motion] = (clip, input);
        }

        // 换表情；clock 是这个人物的时钟（秒），按它眨眼，小于 0 不眨。
        public void Face(string name, float clock)
        {
            if (!Faces.TryGetValue(name ?? "neutral", out var f)) throw new ArgumentException($"没有表情 {name}");
            if (!Expressions) return;
            var blinking = clock >= 0f && f.eyes != EyesHappy && f.eyes != EyesClosed && Blinking(clock);
            Look.SetVector("_FaceCells", new Vector4(blinking ? EyesClosed : f.eyes, f.mouth, f.blush, 0f));
        }

        bool Blinking(float clock)
        {
            var i = Array.BinarySearch(blinks, clock);
            if (i < 0) i = ~i - 1;
            return i >= 0 && clock - blinks[i] < 0.12f;
        }


        // 尾巴随时间轻轻摆（只有补了尾骨的有）。
        public void Sway(float clock, float amount = 1f)
        {
            tail?.Wave(t => (7f * amount * Mathf.Sin(t * 2.4f), 3f * amount * Mathf.Sin(t * 1.7f + 1f), 5f * amount * Mathf.Sin(t * 2.4f - 0.6f)), clock, 0.14f);
        }

        // 取样和改完骨骼之后调用：把此刻的姿势烘成网格，脸的投影跟着头骨走。
        public void Commit()
        {
            foreach (var (skin, baked, shown) in bakes)
            {
                skin.BakeMesh(baked, false); // false 才是世界尺寸：true 反而除掉了 Model 上的缩放
                baked.RecalculateBounds();
                shown.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            }
            Look.SetMatrix("_FaceFromWorld", faceFromHead * head.worldToLocalMatrix);
            Look.SetVector("_FaceForward", head.TransformDirection(faceForward).normalized);
            // 写实光照给脸用的球面法线（Lit.shader）：球心在取景框正面中心往后 1.2 个脸宽，脸上的法线大体朝前，只带一点弧度。
            var faceFront = (head.localToWorldMatrix * faceFromHead.inverse).MultiplyPoint(new Vector3(0.5f, 0.5f, 0f));
            Look.SetVector("_FaceCentre", faceFront - head.TransformDirection(faceForward).normalized * (1.2f * FaceSpan * Height));
        }

        // 此刻的身体：腰（地面上的投影给接地阴影）、头顶和脸的中心（世界坐标）。
        public Vector3 Hips => Bone(HumanBodyBones.Hips).position;
        public Vector3 HeadTop => head.position + Root.up * (Height * 0.09f);
        public Vector3 FaceCentre => (head.localToWorldMatrix * faceFromHead.inverse).MultiplyPoint(new Vector3(0.5f, 0.5f, 0.5f));

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        // 脸在贴图上占哪些像素：自带待机第一帧里，三个顶点都在脸的取景框里、朝前的三角形，按 uv 画进去，再往外扩两格。
        // 两只眼睛（eyes，取景框里的范围）不算：有的瞳孔是很淡的灰蓝，会被当成杂点抹掉。
        static bool[] FaceTexels(SkinnedMeshRenderer[] renderers, Matrix4x4 faceFromWorld, Rect[] eyes, int w, int h)
        {
            var mask = new bool[w * h];
            foreach (var r in renderers)
            {
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                r.BakeMesh(mesh, false);
                var toFace = faceFromWorld * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                var inside = new bool[vertices.Length];
                var spot = new Vector2[vertices.Length];
                for (var i = 0; i < vertices.Length; i++)
                {
                    var f = toFace.MultiplyPoint3x4(vertices[i]);
                    spot[i] = f;
                    // 取景框的 z 朝脸里（深度），朝前的面法线在框里是 -z。
                    inside[i] = f.x > 0f && f.x < 1f && f.y > 0f && f.y < 1f && f.z > 0f && f.z < 1f && toFace.MultiplyVector(normals[i]).normalized.z < -0.3f;
                }
                var triangles = mesh.triangles;
                for (var t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (!inside[a] || !inside[b] || !inside[c]) continue;
                    Vector2 pa = Vector2.Scale(uv[a], new Vector2(w, h)), pb = Vector2.Scale(uv[b], new Vector2(w, h)), pc = Vector2.Scale(uv[c], new Vector2(w, h));
                    var area = (pb.x - pa.x) * (pc.y - pa.y) - (pc.x - pa.x) * (pb.y - pa.y);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x, pc.x)) - 2), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x, pc.x)) + 2);
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y, pc.y)) - 2), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y, pc.y)) + 2);
                    for (var y = y0; y <= y1; y++)
                        for (var x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(x + 0.5f, y + 0.5f);
                            var u = ((pb.x - p.x) * (pc.y - p.y) - (pc.x - p.x) * (pb.y - p.y)) / area;
                            var v = ((pc.x - p.x) * (pa.y - p.y) - (pa.x - p.x) * (pc.y - p.y)) / area;
                            // 往外扩两格：边上的像素也算（按到三角形的大致距离）。
                            const float pad = 0.02f;
                            if (u < -pad || v < -pad || 1f - u - v < -pad) continue;
                            var at = u * spot[a] + v * spot[b] + (1f - u - v) * spot[c];
                            if (!eyes.Any(e => e.Contains(at))) mask[y * w + x] = true;
                        }
                }
                Object.DestroyImmediate(mesh);
            }
            return mask;
        }

        // 生成贴图的皮肤里夹着浅灰白的杂点（Tripo 去光照留下的，偏蓝，母作导入时灰色又被提亮一截，见
        // ModelAssetImporter），近景里脸上像起了一层白斑。发白、自己不算皮肤的像素换成四周 9×9 里皮肤的平均色，
        // 也算回皮肤。face 为空时全身都看，只补四周六成以上是皮肤的（成片的白，衬衫领子、袜子、眼白，和皮肤的
        // 交界是直的，四周的皮肤不到一半，不会被吃掉）；给了脸的像素就只看脸，脸上的杂点密，四周有一成半皮肤就补，
        // 但两格内有深色（睫毛、眉毛、嘴线、头发）或鲜艳的非皮肤色（瞳孔）的不动，护住眼白。
        // 返回补了几个像素；大一点的一团从边上往里补，多跑几遍。
        static int Despeckle(Color[] px, int w, bool[] face)
        {
            const int r = 4;
            var h = px.Length / w;
            var weighted = new Vector4[px.Length];
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                weighted[i] = new Vector4(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
            }
            // 先横着、再竖着加，得到每个像素四周 9×9 的和。
            var across = new Vector4[px.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var s = Vector4.zero;
                    for (var dx = Mathf.Max(0, x - r); dx <= Mathf.Min(w - 1, x + r); dx++) s += weighted[y * w + dx];
                    across[y * w + x] = s;
                }
            bool Feature(Color c)
            {
                Color.RGBToHSV(c, out _, out var sat, out var v);
                return v < 0.3f || sat > 0.35f && c.a < 0.2f;
            }
            var count = 0;
            var result = (Color[])px.Clone();
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (face != null && !face[y * w + x]) continue;
                    var c = px[y * w + x];
                    Color.RGBToHSV(c, out _, out var sat, out var v);
                    // 脸上一半算皮肤、却几乎没颜色的也补。
                    var pale = face != null ? c.a <= 0.5f && sat <= 0.25f || c.a <= 0.8f && sat <= 0.12f : c.a <= 0.5f && sat <= 0.2f;
                    if (!pale || v < 0.5f) continue;
                    var around = Vector4.zero;
                    int y0 = Mathf.Max(0, y - r), y1 = Mathf.Min(h - 1, y + r);
                    for (var dy = y0; dy <= y1; dy++) around += across[dy * w + x];
                    var n = (Mathf.Min(w - 1, x + r) - Mathf.Max(0, x - r) + 1) * (y1 - y0 + 1);
                    if (around.w < (face != null ? 0.15f : 0.6f) * n) continue;
                    if (face != null)
                    {
                        var near = false;
                        for (var dy = Mathf.Max(0, y - 2); dy <= Mathf.Min(h - 1, y + 2) && !near; dy++)
                            for (var dx = Mathf.Max(0, x - 2); dx <= Mathf.Min(w - 1, x + 2) && !near; dx++)
                                near = Feature(px[dy * w + dx]);
                        if (near) continue;
                    }
                    result[y * w + x] = new Color(around.x / around.w, around.y / around.w, around.z / around.w, face != null ? 1f : around.w / n);
                    count++;
                }
            Array.Copy(result, px, px.Length);
            return count;
        }

        // 两只眼睛在脸的取景框里的范围：表情图集里闭眼那一格（左上，眼皮正好盖住模型自己画的眼睛）按列分成左右两块，
        // 各往外放 0.03。眼角边上画出来的细条和眼皮隔着几列空白，隔得比放出去的边还近的并进同一只眼睛。
        static Rect[] Eyes(Texture2D atlas)
        {
            var copy = Readable(atlas, false);
            int cw = copy.width / 3, ch = copy.height / 3;
            var px = copy.GetPixels(0, 2 * ch, cw, ch); // 最上一行在像素里是最后几行
            Object.DestroyImmediate(copy);
            var eyes = new List<Rect>();
            int? start = null;
            float low = 1f, high = 0f;
            for (var x = 0; x <= cw; x++)
            {
                var any = false;
                for (var y = 0; x < cw && y < ch; y++)
                {
                    if (px[y * cw + x].a < 0.08f) continue;
                    any = true;
                    low = Mathf.Min(low, y / (float)ch);
                    high = Mathf.Max(high, y / (float)ch);
                }
                if (any && start == null) start = x;
                if (!any && start != null)
                {
                    var eye = Rect.MinMaxRect(start.Value / (float)cw - 0.03f, low - 0.03f, x / (float)cw + 0.03f, high + 0.03f);
                    var last = eyes.Count - 1;
                    if (last >= 0 && eyes[last].xMax >= eye.xMin)
                        eyes[last] = Rect.MinMaxRect(eyes[last].xMin, Mathf.Min(eyes[last].yMin, eye.yMin), eye.xMax, Mathf.Max(eyes[last].yMax, eye.yMax));
                    else eyes.Add(eye);
                    start = null;
                    low = 1f;
                    high = 0f;
                }
            }
            if (eyes.Count != 2) throw new InvalidOperationException($"表情图集 {atlas.name} 的闭眼格里分出 {eyes.Count} 块，不是两只眼睛");
            return eyes.ToArray();
        }

        // 贴图本身不可读（压缩、没开 Read/Write），先画到渲染贴图上再读回来。
        static Texture2D Readable(Texture2D source, bool mips)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mips) { anisoLevel = 16, filterMode = FilterMode.Trilinear };
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        // 把贴图读回内存改一遍（change 拿到全部像素和宽），重建 mipmap。
        static Texture2D Remap(Texture2D source, Action<Color[], int> change)
        {
            var copy = Readable(source, true);
            var px = copy.GetPixels();
            change(px, copy.width);
            copy.SetPixels(px);
            copy.Apply(true);
            return copy;
        }

        static AnimationClip Clip(string path)
        {
            foreach (var clip in Resources.LoadAll<AnimationClip>(path))
                if (!clip.name.StartsWith("__preview__")) return clip;
            throw new InvalidOperationException($"Resources/{path} 里没有动作");
        }
    }
}
