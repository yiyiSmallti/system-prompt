using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // DeepSeek's tail, finished after the Animator has posed her. Only her own Walk and Run carry tail curves; any
    // other humanoid clip plays with those masked to the tail, or with none, so the tail keeps its walking arc on the
    // Hips. Where a clip tips the pelvis back or drops it into a wide stance (Cast, Hit), that arc hangs straight
    // down under the floor, or forward between the legs. So:
    // - the arc does not follow the hips tipping back by more than a few degrees;
    // - the pose the clip gives is laid back where it is steeper than the rest arc allows or points ahead of the
    //   hips, then folded at the floor: from where it would go under, the tail runs along the floor, turned backward
    //   the deeper it would have gone;
    // - each joint is a particle pulled toward that pose, with inertia, so the tail lags through a turn, swings past
    //   and settles, and is kept above the floor (the character's root height) and out of the legs, the fluke by
    //   its full width;
    // - near the floor the fluke is rolled level, so its wide lobes lie on the floor instead of cutting into it, and a
    //   fin whose tip would go under is tipped up to the floor.
    // The tail and fin bones are put back to rest before the Animator runs, so a clip that leaves them alone starts
    // from the rest arc, not from this component's last frame.
    [DisallowMultipleComponent]
    public sealed class TailDynamics : MonoBehaviour
    {
        const float Step = 1f / 60f;       // the pull and damping below are per step of this length
        const int MaxSteps = 4;            // a long frame drops the rest rather than stalling
        const float RootPull = 0.35f;      // per step, the share of the way to the clip's pose, at the root...
        const float TipPull = 0.07f;       // ...and at the tip, which lags the most
        const float Damping = 0.15f;       // share of the velocity lost per step
        const float SnapDistance = 1f;     // the root moving this far in a frame is a teleport, not motion
        const int FirstLegJoint = 3;       // the joints before are under the skirt, between the hips
        const float FinGap = 0.01f;
        const float SlopeSlack = 20f;      // degrees a bone may hang steeper than at rest
        const float TiltSlack = 5f;        // degrees the hips may tip back before the tail stops following...
        const float TiltUndo = 40f;        // ...and the most it is turned back up; lying down, the floor fold lays it out
        // Sizes in lengths of one tail bone (a sixteenth of the tail), measured on the Blender rig, so they follow
        // the tail when it is rigged at another size.
        const float Radius = 0.75f;        // tail thickness off the bones
        const float FlukeSpan = 1.75f;     // half the fluke's width, for keeping it out of the legs
        const float FinLength = 5f;        // TailFin_L/R; the FBX has no end bones to measure them by
        const float LevelHeight = 4f;      // the fluke is rolled fully level on the floor, not at all this high

        static readonly (HumanBodyBones from, HumanBodyBones to, float radius)[] Legs =
        {
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.075f),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.06f),
            (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 0.065f),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.075f),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.06f),
            (HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 0.065f),
        };

        Animator animator;
        Transform hips;
        Transform[] bones;                 // Tail_0 to Tail_15
        Transform[] fins;                  // the fluke's side bones, off the chain
        Quaternion[] restLocal;            // bones, then fins
        Vector3 hipsForward;               // the way the hips face, in their own space
        Vector3 tipLocal;                  // the end of the last bone, in its own space
        int fluke;                         // the first bone of the flat fluke at the end
        Vector3[] lobeLocal;               // the fluke's left-right span, in each fluke bone's space
        Vector3[] finTipLocal;
        float[] maxDrop;                   // per bone, the steepest it may point below level, in radians
        float radius, flukeSpan, levelHeight;   // the sizes above in metres
        (Transform from, Transform to, float radius)[] legs;
        Vector3[] pos, prev, target;       // joints 0 to N, N being the tip; joint 0 stays on the hips
        float lastStep = Step;
        bool settled;

        void Awake() => Init();

        // The rest pose comes from the skins' bind poses, not from the bones as they stand: Unity poses a freshly
        // imported model at the first frame of its first clip (DeepSeek|Run), not at rest.
        public void Init()
        {
            if (bones != null) return;
            animator = GetComponentInChildren<Animator>();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var chain = new List<Transform>();
            for (var t = Find(hips, "Tail_0"); t != null; t = Find(t, "Tail_" + chain.Count, false))
                chain.Add(t);
            if (chain.Count < 2)
            {
                Debug.LogError($"{name}: no Tail_0, Tail_1... under the hips", this);
                enabled = false;
                return;
            }
            bones = chain.ToArray();
            fins = bones.SelectMany(b => b.Cast<Transform>()).Where(c => !chain.Contains(c)).ToArray();
            var n = bones.Length;

            var bind = new Dictionary<Transform, Matrix4x4>();
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                for (var k = 0; k < skin.bones.Length; k++)
                    if (skin.bones[k] != null && !bind.ContainsKey(skin.bones[k]))
                        bind[skin.bones[k]] = skin.transform.localToWorldMatrix * skin.sharedMesh.bindposes[k].inverse;
            Matrix4x4 Rest(Transform t) => bind.TryGetValue(t, out var m) ? m : t.localToWorldMatrix;

            restLocal = bones.Concat(fins).Select(b => (Rest(b.parent).inverse * Rest(b)).rotation).ToArray();
            var at = bones.Select(b => Rest(b).GetPosition()).ToList();
            at.Add(2f * at[n - 1] - at[n - 2]);
            hipsForward = Quaternion.Inverse(Rest(hips).rotation) * transform.forward;
            maxDrop = Enumerable.Range(0, n).Select(i => Mathf.Min(90f, SlopeSlack +
                Mathf.Asin(Mathf.Clamp(-(at[i + 1] - at[i]).normalized.y, -1f, 1f)) * Mathf.Rad2Deg) * Mathf.Deg2Rad).ToArray();
            tipLocal = Rest(bones[n - 1]).inverse.MultiplyPoint3x4(at[n]);
            var unit = (at[1] - at[0]).magnitude;
            radius = Radius * unit;
            flukeSpan = FlukeSpan * unit;
            levelHeight = LevelHeight * unit;
            // the fluke lies flat at rest: the run of level bones at the end of the chain
            for (fluke = n; fluke > 1; fluke--)
                if (Mathf.Abs((at[fluke] - at[fluke - 1]).normalized.y) > 0.1f) break;
            lobeLocal = new Vector3[n];
            for (var i = fluke; i < n; i++) lobeLocal[i] = Quaternion.Inverse(Rest(bones[i]).rotation) * transform.right;
            // a fin points along the same local axis as the chain's bones (the exporter's primary bone axis)
            var axis = Quaternion.Inverse(Rest(bones[0]).rotation) * (at[1] - at[0]).normalized;
            finTipLocal = fins.Select(f => Rest(f).inverse.MultiplyPoint3x4(Rest(f).GetPosition() + Rest(f).rotation * axis * (FinLength * unit))).ToArray();

            legs = Legs.Select(l => (animator.GetBoneTransform(l.from), animator.GetBoneTransform(l.to), l.radius))
                .Where(l => l.Item1 != null && l.Item2 != null).ToArray();
            pos = new Vector3[n + 1];
            prev = new Vector3[n + 1];
            target = new Vector3[n + 1];
        }

        static Transform Find(Transform parent, string name, bool deep = true)
        {
            foreach (Transform c in parent)
            {
                if (c.name == name) return c;
                var d = deep ? Find(c, name) : null;
                if (d != null) return d;
            }
            return null;
        }

        // Restore runs just before the Animator: in Update, or in FixedUpdate when it animates with physics.
        void Update()
        {
            if (animator.updateMode != AnimatorUpdateMode.Fixed) Restore();
        }

        void FixedUpdate()
        {
            if (animator.updateMode == AnimatorUpdateMode.Fixed) Restore();
        }

        void LateUpdate() => Simulate(animator.updateMode == AnimatorUpdateMode.UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        public void Restore()
        {
            if (bones == null) return;
            for (var i = 0; i < bones.Length; i++) bones[i].localRotation = restLocal[i];
            for (var f = 0; f < fins.Length; f++) fins[f].localRotation = restLocal[bones.Length + f];
        }

        // Next frame takes the clip's pose as it is, as after a cut or a teleport.
        public void Snap() => settled = false;

        // After the Animator: fold the clip's pose at the floor, move the particles, and turn the bones to them.
        public void Simulate(float dt)
        {
            if (bones == null) return;
            var n = bones.Length;
            for (var i = 0; i < n; i++) target[i] = bones[i].position;
            target[n] = bones[n - 1].TransformPoint(tipLocal);
            Fold();
            if (!settled || (target[0] - pos[0]).sqrMagnitude > SnapDistance * SnapDistance)
            {
                target.CopyTo(pos, 0);
                target.CopyTo(prev, 0);
                lastStep = Step;
                settled = true;
            }
            else if (dt > 0f)
            {
                var steps = Mathf.Clamp(Mathf.CeilToInt(dt / Step - 0.01f), 1, MaxSteps);
                var h = Mathf.Min(dt / steps, Step);
                for (var s = 0; s < steps; s++) Integrate(h);
            }
            Apply();
        }

        // Rebuilds the target chain joint by joint from the clip's bone directions. A bone loses any heading ahead of
        // the hips and is raised to its steepest allowed slope, turning backward the more it hung past it; a bone
        // that would end below the floor keeps its heading over the ground but drops only to the floor, and the
        // deeper it would have gone, the more it turns to trail behind the hips.
        void Fold()
        {
            var floor = transform.position.y + radius;
            var forward = hips.rotation * hipsForward;
            var facing = new Vector3(forward.x, 0f, forward.z);
            var back = facing.sqrMagnitude > 1e-4f ? -facing.normalized : -transform.forward; // hips facing up: lying down
            // hips tipped back swing the whole arc down and under her: past TiltSlack, it is turned back up about its
            // root, keeping its shape
            var tilt = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (tilt > TiltSlack)
            {
                var undo = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(forward, -back), Mathf.Min(tilt - TiltSlack, TiltUndo) / tilt);
                for (var i = 1; i < target.Length; i++) target[i] = target[0] + undo * (target[i] - target[0]);
            }
            var last = target[0];
            for (var i = 1; i < target.Length; i++)
            {
                var d = target[i] - target[i - 1];
                var length = d.magnitude;
                var level = new Vector3(d.x, 0f, d.z);
                var ahead = Vector3.Dot(level, back);
                var down = Mathf.Atan2(-d.y, level.magnitude);
                if (ahead < 0f || down > maxDrop[i - 1])
                {
                    level -= back * Mathf.Min(ahead, 0f);
                    level += back * (length * Mathf.Clamp01((down - maxDrop[i - 1]) / (Mathf.PI / 2f - maxDrop[i - 1] + 1e-4f)));
                    level = level.sqrMagnitude > 1e-8f ? level.normalized : back;
                    down = Mathf.Min(down, maxDrop[i - 1]);
                    d = (level * Mathf.Cos(down) + Vector3.down * Mathf.Sin(down)) * length;
                }
                var p = last + d;
                if (p.y < floor)
                {
                    var flat = new Vector3(d.x, 0f, d.z);
                    var heading = flat + back * (length * Mathf.Clamp01((floor - p.y) / length));
                    heading = heading.sqrMagnitude > 1e-8f ? heading.normalized : back;
                    var drop = Mathf.Clamp(floor - last.y, -length, 0f);
                    p = last + heading * Mathf.Sqrt(length * length - drop * drop) + Vector3.up * drop;
                }
                target[i - 1] = last;
                last = p;
            }
            target[target.Length - 1] = last;
        }

        // One step of h seconds; the per-step pull and damping are rescaled from Step, the carried velocity from the
        // step before.
        void Integrate(float h)
        {
            var n = bones.Length;
            var r = h / Step;
            var keep = Mathf.Pow(1f - Damping, r) * h / lastStep;
            lastStep = h;
            pos[0] = prev[0] = target[0];
            var floor = transform.position.y + radius;
            for (var i = 1; i <= n; i++)
            {
                var v = (pos[i] - prev[i]) * keep;
                prev[i] = pos[i];
                var p = pos[i] + v;
                var pull = 1f - Mathf.Pow(1f - Mathf.Lerp(RootPull, TipPull, (i - 1f) / (n - 1f)), r);
                p += (target[i] - p) * pull;
                // keep the bone's length from the joint before, then push out of the floor and the legs
                var length = (target[i] - target[i - 1]).magnitude;
                p = pos[i - 1] + (p - pos[i - 1]).normalized * length;
                if (p.y < floor) p.y = floor;
                if (i >= FirstLegJoint)
                    foreach (var (from, to, thick) in legs) p = PushOut(p, from.position, to.position, thick + (i > fluke ? flukeSpan : radius));
                pos[i] = p;
            }
        }

        static Vector3 PushOut(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            var ab = b - a;
            var c = a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            var d = p - c;
            var m = d.magnitude;
            return m >= r || m < 1e-6f ? p : c + d * (r / m);
        }

        // Turns each bone, root first, so that its child lands on the next particle; then levels the fluke and lifts
        // the fins off the floor.
        void Apply()
        {
            var n = bones.Length;
            var ground = transform.position.y;
            for (var i = 0; i < n; i++)
            {
                var bone = bones[i];
                var child = i + 1 < n ? bones[i + 1].position : bone.TransformPoint(tipLocal);
                bone.rotation = Quaternion.FromToRotation(child - bone.position, pos[i + 1] - bone.position) * bone.rotation;
                if (i < fluke) continue;
                // roll about the bone so the lobes span level, by how close it is to the floor
                var near = 1f - Mathf.Clamp01((bone.position.y - ground - radius) / levelHeight);
                var along = (pos[i + 1] - bone.position).normalized;
                var level = Vector3.Cross(Vector3.up, along);
                if (near <= 0f || level.sqrMagnitude < 0.01f) continue;
                var lobe = Vector3.ProjectOnPlane(bone.rotation * lobeLocal[i], along);
                level = level.normalized * Mathf.Sign(Vector3.Dot(level, lobe));
                bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(lobe, level), near) * bone.rotation;
            }
            for (var f = 0; f < fins.Length; f++)
            {
                var root = fins[f].position;
                var tip = fins[f].TransformPoint(finTipLocal[f]);
                if (tip.y >= ground + FinGap) continue;
                var flat = new Vector3(tip.x - root.x, 0f, tip.z - root.z);
                if (flat.sqrMagnitude < 1e-8f) continue;
                var length = (tip - root).magnitude;
                var drop = Mathf.Clamp(ground + FinGap - root.y, -length, 0f);
                var lifted = flat.normalized * Mathf.Sqrt(length * length - drop * drop) + Vector3.up * drop;
                fins[f].rotation = Quaternion.FromToRotation(tip - root, lifted) * fins[f].rotation;
            }
        }
    }
}
