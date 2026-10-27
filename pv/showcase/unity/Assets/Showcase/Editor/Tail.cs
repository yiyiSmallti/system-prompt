using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Showcase
{
    // DeepSeek 战斗服鲸尾的骨骼，从衍生作 PV 的 Tail.cs 搬来。Tripo 的骨架没有尾巴：尾巴从裙下贴着小腿后面垂到地上，末端是左右展开的
    // 尾鳍，导入时离腿远的部分交给了 Hips（ModelAssetImporter.Unhook），离腿近的上半段还分着腿的权重，
    // 所以尾巴只能跟着腰整体转，迈腿时还会被腿带着走。这里在载入时补一条四节的尾骨，从裙下到尾鳍根，
    // 挂在 Hips 下；尾巴上的顶点按高度分给相邻两节，尾鳍整个给最后一节。
    // 哪些顶点算尾巴：离两条腿的骨骼都超过 7–9.5 厘米、在裙摆以下（0.56–0.64 米）、在腰的中心往后。
    // 尺寸都按 1.58 米高的 DeepSeek 战斗服量的，换别的模型要重新量。
    sealed class Tail
    {
        // 各节的高度（米），从裙下到尾鳍根。
        static readonly float[] Heights = { 0.58f, 0.42f, 0.27f, 0.15f };

        // 平时尾巴不垂到地上，往后翘：各节往后弯多少度，越往尾鳍弯得越多，尾鳍翘到身后上方。
        static readonly float[] Carriage = { 45f, 35f, 30f, 25f };

        public readonly Transform[] Bones;
        readonly Quaternion firstRest;
        readonly Transform hips, head;
        float carry = 1f;

        public Tail(Figure figure, SkinnedMeshRenderer skin)
        {
            var mesh = Object.Instantiate(skin.sharedMesh);
            mesh.name = skin.sharedMesh.name + " Tail";
            var bones = skin.bones;
            var bindposes = mesh.bindposes;
            hips = figure.Bone(HumanBodyBones.Hips);
            head = figure.Bone(HumanBodyBones.Head);
            var hipsIndex = Array.IndexOf(bones, hips);
            if (hipsIndex < 0) throw new InvalidOperationException("尾巴：蒙皮里没有 Hips");
            // 网格空间（绑定姿势）到世界：整个网格跟着 Hips 走时的位置。腿的位置也按绑定姿势算，同一个参照里比。
            var toWorld = hips.localToWorldMatrix * bindposes[hipsIndex];
            var toRoot = figure.Root.worldToLocalMatrix * toWorld;
            Vector3 BoneAt(HumanBodyBones b)
            {
                var i = Array.IndexOf(bones, figure.Bone(b));
                if (i < 0) throw new InvalidOperationException($"尾巴：蒙皮里没有 {b}");
                return toRoot.MultiplyPoint3x4(bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero));
            }
            var legs = new List<(Vector3, Vector3)>();
            foreach (var chain in new[]
            {
                new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes },
                new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes },
            })
                for (var k = 0; k + 1 < chain.Length; k++) legs.Add((BoneAt(chain[k]), BoneAt(chain[k + 1])));

            var vertices = mesh.vertices;
            var local = vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
            var tailness = local.Select(p =>
                Stage.Smooth(0.07f, 0.095f, legs.Min(s => Distance(p, s.Item1, s.Item2)))
                * (1f - Stage.Smooth(0.56f, 0.64f, p.y)) * (1f - Stage.Smooth(0.04f, 0.09f, p.z))).ToArray();
            // 裙下腿和尾巴是分开的几块（按位置合并顶点后连通）。靴跟底、靴口背面离腿骨七八厘米以上，按距离会分到尾骨的
            // 权重，两腿一分开，被尾巴拽向中间，拉成扁平的几片；有顶点离腿骨不到 5 厘米的那几块是腿和靴子，一点不给尾巴。
            // 0.3 米以下其余的是尾鳍，整个给尾巴。
            const float under = 0.56f, low = 0.3f;
            var part = Parts(mesh, vertices, v => local[v].y < under);
            var below = Enumerable.Range(0, local.Length).Where(v => local[v].y < under).ToArray();
            var boots = new HashSet<int>(below.Where(v => legs.Min(sg => Distance(local[v], sg.Item1, sg.Item2)) < 0.05f).Select(v => part[v]));
            var freed = 0;
            foreach (var v in below)
            {
                if (boots.Contains(part[v])) { if (tailness[v] > 0f) freed++; tailness[v] = 0f; }
                else if (local[v].y < low) tailness[v] = 1f;
            }
            Debug.Log($"尾巴：{under} 米以下腿和靴子 {below.Count(v => boots.Contains(part[v]))} 个顶点不给尾巴（其中 {freed} 个原先按距离分到了），尾鳍 {below.Count(v => !boots.Contains(part[v]) && local[v].y < low)} 个整个给尾巴");

            // 各节放在那个高度上尾巴截面的中心，朝向和 Root 一致（本地 x 往右、y 往上、z 往前）。
            Bones = new Transform[Heights.Length];
            var parent = hips;
            for (var k = 0; k < Heights.Length; k++)
            {
                var slice = Enumerable.Range(0, local.Length).Where(v => tailness[v] > 0.5f && Mathf.Abs(local[v].y - Heights[k]) < 0.03f).ToArray();
                if (slice.Length < 8) throw new InvalidOperationException($"尾巴：{Heights[k]} 米高处找不到尾巴（{slice.Length} 个顶点）");
                var centre = slice.Aggregate(Vector3.zero, (sum, v) => sum + local[v]) / slice.Length;
                var bone = new GameObject("Tail" + k).transform;
                bone.SetPositionAndRotation(figure.Root.TransformPoint(centre), figure.Root.rotation);
                bone.SetParent(parent, true);
                Bones[k] = bone;
                parent = bone;
            }
            firstRest = Bones[0].localRotation;

            // 权重：尾巴上的顶点按高度分给相邻两节，原来的权重按尾巴的程度让出来，留最重的四个。
            var weights = mesh.boneWeights;
            var first = bones.Length;
            for (var v = 0; v < weights.Length; v++)
            {
                var t = tailness[v];
                if (t <= 0f) continue;
                var total = new Dictionary<int, float>();
                void Add(int bone, float w) { if (w > 0f) total[bone] = total.GetValueOrDefault(bone) + w; }
                var w0 = weights[v];
                Add(w0.boneIndex0, w0.weight0 * (1f - t));
                Add(w0.boneIndex1, w0.weight1 * (1f - t));
                Add(w0.boneIndex2, w0.weight2 * (1f - t));
                Add(w0.boneIndex3, w0.weight3 * (1f - t));
                var y = local[v].y;
                var k = 0;
                while (k + 1 < Heights.Length && y < Heights[k + 1]) k++;
                if (k + 1 < Heights.Length && y < Heights[k])
                {
                    var u = Mathf.InverseLerp(Heights[k], Heights[k + 1], y);
                    Add(first + k, t * (1f - u));
                    Add(first + k + 1, t * u);
                }
                else Add(first + k, t);
                weights[v] = Pack(total);
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bindposes.Concat(Bones.Select(b => b.worldToLocalMatrix * toWorld)).ToArray();
            skin.bones = bones.Concat(Bones).ToArray();
            skin.sharedMesh = mesh;
        }

        // 每帧取样动作之后回到平时往后翘的样子，各镜头再按需要弯。躺倒时（腰到头的方向离竖直过了五六十度）往后翘
        // 就扎进地里，翘的角度跟着放平，尾巴顺着腿摆。
        public void Reset()
        {
            carry = Stage.Smooth(0.2f, 0.6f, Vector3.Dot((head.position - hips.position).normalized, Vector3.up));
            for (var k = 0; k < Bones.Length; k++) Bend(k, 0f, 0f);
        }

        // 弯一节（度，绕这一节自己的轴，静止时就是身体的轴），在平时往后翘的基础上：side 往右摆，lift 往后翘，
        // twist 绕竖轴拧。
        public void Bend(int bone, float side, float lift, float twist = 0f)
        {
            var rest = bone == 0 ? firstRest : Quaternion.identity;
            Bones[bone].localRotation = rest * Quaternion.AngleAxis(side, Vector3.forward)
                * Quaternion.AngleAxis(Carriage[bone] * carry + lift, Vector3.right) * Quaternion.AngleAxis(twist, Vector3.up);
        }

        // 整条尾巴一起弯：总角度按 shares 分给各节，越往尾鳍分得越多；lag 是每往后一节晚多少秒，
        // angles 按时间给出 (side, lift, twist)，后面的节取更早的值，摆起来像波一样传下去。
        static readonly float[] Shares = { 0.12f, 0.24f, 0.32f, 0.32f };

        public void Wave(Func<float, (float side, float lift, float twist)> angles, float time, float lag)
        {
            for (var k = 0; k < Bones.Length; k++)
            {
                var (side, lift, twist) = angles(time - lag * k);
                Bend(k, side * Shares[k], lift * Shares[k], k == Bones.Length - 1 ? twist : 0f);
            }
        }

        // 每个顶点属于哪一块：位置相同的顶点（uv 和法线接缝处拆开的）先合并，再按三角形连起来。
        public static int[] Parts(Mesh mesh, Vector3[] vertices, Func<int, bool> keep)
        {
            var part = Enumerable.Range(0, vertices.Length).ToArray();
            int Find(int i) { while (part[i] != i) i = part[i] = part[part[i]]; return i; }
            void Join(int a, int b) => part[Find(a)] = Find(b);
            var cell = mesh.bounds.size.magnitude * 1e-5f;
            var seen = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] / cell);
                if (seen.TryGetValue(key, out var other)) Join(i, other); else seen[key] = i;
            }
            var triangles = mesh.triangles;
            for (var t = 0; t < triangles.Length; t += 3)
            {
                if (!keep(triangles[t]) || !keep(triangles[t + 1]) || !keep(triangles[t + 2])) continue;
                Join(triangles[t], triangles[t + 1]);
                Join(triangles[t], triangles[t + 2]);
            }
            return Enumerable.Range(0, vertices.Length).Select(Find).ToArray();
        }

        static float Distance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        static BoneWeight Pack(Dictionary<int, float> total)
        {
            var top = total.Where(x => x.Value > 0f).OrderByDescending(x => x.Value).Take(4).ToArray();
            var sum = top.Sum(x => x.Value);
            var w = new BoneWeight();
            if (top.Length > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / sum; }
            if (top.Length > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / sum; }
            if (top.Length > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / sum; }
            if (top.Length > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / sum; }
            return w;
        }
    }
}
