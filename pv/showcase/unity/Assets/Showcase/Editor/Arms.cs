using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Showcase
{
    // 衣服挡住手臂。Tripo 的衣服比身体宽得多（卫衣、外套、蓬裙），动作里手臂却按瘦的身体贴着垂下，整条陷进衣服里，
    // 侧面看手臂不见了。载入时按绑定姿势（T 字）量出躯干连衣服的外形：躯干、锁骨和大腿骨骼主导的顶点（头、脖子、
    // 手臂和它们下面的不算，头发、披肩跟着这些走），按离躯干竖轴的高度和方位分格，每格取外沿；袖子和手也量出粗细。
    // 贴着躯干那一侧的袖子本来就和衣服互相穿插（腋下、上臂内侧），从外面看不见，不管；难看的是整条手臂被吞掉。
    // 所以每帧取样完动作，看肘、前臂和手的中心线：离外形不到六成粗细的往外推，先转上臂把肘带出来，再转前臂把手
    // 带出来，来回几遍。
    // 外形跟着躯干的骨头走（按高度在相邻两节之间插值），腰以下的裙摆和外套下摆跟着 Hips。
    sealed class Arms
    {
        const int Sectors = 36;
        const float Showing = 0.6f; // 中心线至少露在衣服外面几成粗细
        const float MaxTurn = 30f; // 一节一帧最多转多少度，量错了也不会把手臂甩飞

        readonly Vector3 origin, up, right, forward;
        readonly float bottom, step;
        readonly float[,] outline; // [高度格, 方位格]：外沿离竖轴多远
        readonly (Transform bone, Matrix4x4 toB, float at)[] torso; // 绑定姿势下从骨头本地到 Hips 本地（B）
        readonly Side[] sides;

        sealed class Side
        {
            public Transform Upper, Lower, Hand;
            public Vector3 TipLocal; // 指尖附近，手骨本地
            public float UpperRadius, LowerRadius, HandRadius; // 袖子和手的粗细（绑定姿势的尺寸）
        }

        public Arms(Figure figure, SkinnedMeshRenderer[] skins)
        {
            var skin = skins.OrderByDescending(s => s.sharedMesh.vertexCount).First();
            var hips = figure.Bone(HumanBodyBones.Hips);
            Matrix4x4 BindFrom(Transform bone) => skin.sharedMesh.bindposes[Array.IndexOf(skin.bones, hips)] * skin.sharedMesh.bindposes[Array.IndexOf(skin.bones, bone)].inverse;
            Vector3 At(HumanBodyBones b)
            {
                var t = figure.Bone(b);
                if (t == null || Array.IndexOf(skin.bones, t) < 0) throw new InvalidOperationException($"手臂避让：{figure.Model} 的蒙皮里没有 {b}");
                return BindFrom(t).MultiplyPoint3x4(Vector3.zero);
            }

            // 躯干的竖轴、左右和前后（B 空间：Hips 绑定时的本地坐标）。
            origin = At(HumanBodyBones.Hips);
            up = (At(HumanBodyBones.Neck) - origin).normalized;
            right = Vector3.ProjectOnPlane(At(HumanBodyBones.RightUpperArm) - At(HumanBodyBones.LeftUpperArm), up).normalized;
            forward = Vector3.Cross(right, up);
            if (Vector3.Dot(At(HumanBodyBones.LeftToes) - At(HumanBodyBones.LeftFoot), forward) < 0f) forward = -forward;

            torso = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                .Select(figure.Bone).Where(t => t != null && Array.IndexOf(skin.bones, t) >= 0)
                .Select(t => (t, BindFrom(t), Vector3.Dot(BindFrom(t).MultiplyPoint3x4(Vector3.zero) - origin, up))).ToArray();

            // 所有顶点：B 空间的位置、主导骨骼、在主导骨骼本地的位置。
            var points = new List<(Vector3 p, Transform bone, Vector3 local)>();
            foreach (var s in skins)
            {
                var hipsIndex = Array.IndexOf(s.bones, hips);
                if (hipsIndex < 0) continue;
                var bindposes = s.sharedMesh.bindposes;
                var vertices = s.sharedMesh.vertices;
                var weights = s.sharedMesh.boneWeights;
                for (var v = 0; v < vertices.Length; v++)
                {
                    var w = weights[v];
                    var top = w.boneIndex0;
                    var most = w.weight0;
                    if (w.weight1 > most) { top = w.boneIndex1; most = w.weight1; }
                    if (w.weight2 > most) { top = w.boneIndex2; most = w.weight2; }
                    if (w.weight3 > most) { top = w.boneIndex3; most = w.weight3; }
                    points.Add((bindposes[hipsIndex].MultiplyPoint3x4(vertices[v]), s.bones[top], bindposes[top].MultiplyPoint3x4(vertices[v])));
                }
            }

            var shoulder = At(HumanBodyBones.RightUpperArm) - origin;
            var shoulderHeight = Vector3.Dot(shoulder, up);
            var shoulderHalf = Mathf.Abs(Vector3.Dot(shoulder, right));

            // 袖子和手的粗细：主导骨骼是这一节、T 字里在肩关节外侧的顶点离这一节骨头多远，取七成五处的。DeepSeek 战斗服
            // 的披肩和背后的头发也挂在上臂上，离骨头远，所以上臂只量离骨头不到前臂粗细两倍半的那一圈。
            Side Measure(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, float sign)
            {
                Transform u = figure.Bone(upper), l = figure.Bone(lower), h = figure.Bone(hand);
                Vector3 pu = At(upper), pl = At(lower), ph = At(hand);
                var fingers = new HashSet<Transform>(h.GetComponentsInChildren<Transform>());
                var outside = points.Where(x => sign * Vector3.Dot(x.p - origin, right) > shoulderHalf).ToArray();
                float Thickness(IEnumerable<float> distances)
                {
                    var d = distances.OrderBy(x => x).ToArray();
                    return d.Length == 0 ? 0f : d[(int)(0.75f * (d.Length - 1))];
                }
                // 手往前臂的方向伸多长：手和手指的顶点沿这个方向九成处。
                var along = (ph - pl).normalized;
                var reach = outside.Where(x => fingers.Contains(x.bone)).Select(x => Vector3.Dot(x.p - ph, along)).OrderBy(x => x).ToArray();
                var tip = ph + along * (reach.Length == 0 ? 0.08f : reach[(int)(0.9f * (reach.Length - 1))]);
                var lowerRadius = Thickness(outside.Where(x => x.bone == l).Select(x => Distance(x.p, pl, ph)));
                return new Side
                {
                    Upper = u, Lower = l, Hand = h,
                    TipLocal = BindFrom(h).inverse.MultiplyPoint3x4(tip),
                    UpperRadius = Thickness(outside.Where(x => x.bone == u).Select(x => Distance(x.p, pu, pl)).Where(d => d < 2.5f * lowerRadius)),
                    LowerRadius = lowerRadius,
                    HandRadius = Thickness(outside.Where(x => fingers.Contains(x.bone)).Select(x => Distance(x.p, ph, tip))),
                };
            }
            sides = new[]
            {
                Measure(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -1f),
                Measure(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1f),
            };

            // 躯干连衣服的外形。腋下往上只要两肩之间的（肩上的锁骨顶点有一截是袖子）。
            var bodyBones = new HashSet<Transform>(new[]
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            }.Select(figure.Bone).Where(t => t != null));
            var low = points.Min(x => Vector3.Dot(x.p - origin, up));
            var armpit = shoulderHeight - 0.09f * (shoulderHeight - low);
            var body = points.Where(x => bodyBones.Contains(x.bone)).Select(x => x.p - origin)
                .Where(p => Vector3.Dot(p, up) < shoulderHeight && (Vector3.Dot(p, up) < armpit || Mathf.Abs(Vector3.Dot(p, right)) < 0.95f * shoulderHalf))
                .ToArray();
            bottom = body.Min(p => Vector3.Dot(p, up));
            step = (shoulderHeight - bottom) / 64f;
            var bins = Mathf.CeilToInt((shoulderHeight - bottom) / step) + 1;
            var cells = new List<float>[bins, Sectors];
            foreach (var p in body)
            {
                var (h, sector, rho) = Polar(p);
                var k = Mathf.Clamp(Mathf.FloorToInt((h - bottom) / step), 0, bins - 1);
                (cells[k, Mathf.FloorToInt(sector) % Sectors] ??= new List<float>()).Add(rho);
            }
            // 每格取九成五处的外沿（去掉几个扣子、带子扎出来的点），上下三格取最大，没点的格子用左右两格的。
            var raw = new float[bins, Sectors];
            for (var k = 0; k < bins; k++)
            for (var j = 0; j < Sectors; j++)
            {
                var c = cells[k, j];
                if (c == null) continue;
                c.Sort();
                raw[k, j] = c[(int)(0.95f * (c.Count - 1))];
            }
            outline = new float[bins, Sectors];
            for (var k = 0; k < bins; k++)
            for (var j = 0; j < Sectors; j++)
                outline[k, j] = Mathf.Max(raw[k, j], Mathf.Max(k > 0 ? raw[k - 1, j] : 0f, k + 1 < bins ? raw[k + 1, j] : 0f));
            for (var k = 0; k < bins; k++)
            for (var j = 0; j < Sectors; j++)
                if (outline[k, j] <= 0f) outline[k, j] = Mathf.Min(outline[k, (j + 1) % Sectors], outline[k, (j + Sectors - 1) % Sectors]);
            Debug.Log($"手臂避让：{figure.Model} 上臂粗 {sides[0].UpperRadius:0.000}/{sides[1].UpperRadius:0.000}，前臂 {sides[0].LowerRadius:0.000}/{sides[1].LowerRadius:0.000}，" +
                      $"手 {sides[0].HandRadius:0.000}/{sides[1].HandRadius:0.000}（模型约 1 个单位高）");
        }

        // 每帧取样之后调用：手臂陷进衣服的推出来。
        public void Clear()
        {
            foreach (var s in sides)
            for (var pass = 0; pass < 3; pass++)
            {
                // 上臂：上臂下段和肘，按离肩的远近换算成肘该挪多远。
                Vector3 shoulder = s.Upper.position, elbow = s.Lower.position;
                Turn(s.Upper, shoulder, elbow, Need(shoulder, elbow, (Vector3.Lerp(shoulder, elbow, 0.75f), s.UpperRadius), (elbow, s.UpperRadius)));
                // 前臂和手：按离肘的远近换算成指尖该挪多远。
                elbow = s.Lower.position;
                var wrist = s.Hand.position;
                var tip = s.Hand.TransformPoint(s.TipLocal);
                Turn(s.Lower, elbow, tip, Need(elbow, tip,
                    (Vector3.Lerp(elbow, wrist, 0.33f), s.LowerRadius), (Vector3.Lerp(elbow, wrist, 0.66f), s.LowerRadius),
                    (wrist, s.LowerRadius), (Vector3.Lerp(wrist, tip, 0.5f), s.HandRadius), (tip, s.HandRadius)));
            }
        }

        // 绕 pivot 转的这一节，末端 end 要挪多远才能把这几点都带出来：每点往外推的量按离 pivot 的远近放大到末端，取最大的。
        Vector3 Need(Vector3 pivot, Vector3 end, params (Vector3 at, float radius)[] samples)
        {
            var length = (end - pivot).magnitude;
            var most = Vector3.zero;
            foreach (var (at, radius) in samples)
            {
                var lever = (at - pivot).magnitude;
                if (lever < 1e-4f) continue;
                var push = Push(at, Showing * radius) * (length / lever);
                if (push.sqrMagnitude > most.sqrMagnitude) most = push;
            }
            return most;
        }

        // 转 bone，让 from→to 这一节的末端挪 move（只取垂直于这一节的部分）。
        static void Turn(Transform bone, Vector3 from, Vector3 to, Vector3 move)
        {
            if (move.sqrMagnitude < 1e-12f) return;
            var turn = Quaternion.FromToRotation(to - from, to + move - from);
            turn.ToAngleAxis(out var angle, out var axis);
            if (angle > MaxTurn) turn = Quaternion.AngleAxis(MaxTurn, axis);
            bone.rotation = turn * bone.rotation;
        }

        // 世界坐标的一点离外形不到 clearance（绑定姿势的尺寸）的，往外推的世界向量。
        Vector3 Push(Vector3 world, float clearance)
        {
            // 放回绑定姿势里比：按高度在相邻两节躯干骨头之间插值，每节在它到下一节的中点上占满。
            Vector3 Back(int i) => torso[i].toB.MultiplyPoint3x4(torso[i].bone.InverseTransformPoint(world));
            var height = Vector3.Dot(Back(0) - origin, up);
            var last = torso.Length - 1;
            float Mid(int i) => i < last ? 0.5f * (torso[i].at + torso[i + 1].at) : torso[i].at + 0.05f;
            var main = 0;
            while (main < last && height > Mid(main + 1)) main++;
            var blend = main < last ? Mathf.InverseLerp(Mid(main), Mid(main + 1), height) : 0f;
            var p = blend > 0f ? Vector3.Lerp(Back(main), Back(main + 1), blend) : Back(main);
            if (blend > 0.5f) main++;

            var (h, sector, rho) = Polar(p - origin);
            var reach = Outline(h, sector);
            var depth = reach + clearance - rho;
            if (reach <= 0f || depth <= 0f) return Vector3.zero;
            var outward = rho > 1e-4f ? Vector3.ProjectOnPlane(p - origin, up) / rho : right;
            return torso[main].bone.TransformVector(torso[main].toB.inverse.MultiplyVector(outward * depth));
        }

        // 外形在这个高度和方位上离竖轴多远：高度和方位上都按相邻格插值，超出量过的高度为 0。
        float Outline(float h, float sector)
        {
            var bins = outline.GetLength(0);
            var y = (h - bottom) / step - 0.5f;
            if (y < -0.5f || y > bins - 0.5f) return 0f;
            int k0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, bins - 1), k1 = Mathf.Min(k0 + 1, bins - 1);
            var x = sector - 0.5f;
            var j0 = (Mathf.FloorToInt(x) + Sectors) % Sectors;
            var j1 = (j0 + 1) % Sectors;
            float ty = Mathf.Clamp01(y - k0), tx = x - Mathf.Floor(x);
            return Mathf.Lerp(Mathf.Lerp(outline[k0, j0], outline[k0, j1], tx), Mathf.Lerp(outline[k1, j0], outline[k1, j1], tx), ty);
        }

        // 相对 Hips 原点的一点：高度、方位（格，0 是正前方，往右增加）、离竖轴多远。
        (float h, float sector, float rho) Polar(Vector3 rel)
        {
            var h = Vector3.Dot(rel, up);
            float x = Vector3.Dot(rel, right), z = Vector3.Dot(rel, forward);
            return (h, Mathf.Repeat(Mathf.Atan2(x, z) / (2f * Mathf.PI), 1f) * Sectors, Mathf.Sqrt(x * x + z * z));
        }

        static float Distance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }
    }
}
