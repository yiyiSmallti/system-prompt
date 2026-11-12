using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The spring bones of a VRoid model (hair, skirt, hood strings, tail, fins) as its VRM had them, read from
    // Resources/Models/Characters/<model>_Springs.json (written with the model by the VrmCheck project's GameBake).
    // The VRM 1.0 spring bone: each joint's tip carries on with what it moved last frame, less its drag, is pulled
    // toward where the pose above it puts it and by gravity, stays a bone's length from the joint and is pushed out
    // of the body's spheres. The motions move the humanoid bones only, so each joint is turned afresh every frame
    // from its rest. A spring with a centre (VRoid's own: the model's Root) moves with the figure, so running and
    // turning don't drag its hair behind; one with none (the tail, the hip fins) trails in the world.
    // Neither is how something moves through water. Carried with the figure, long hair hung straight down at a
    // full run, a wall in front of her arm; trailing in the world with a drag near a half each frame, a chain was
    // pulled out level behind her as a rod. So each link is pushed back by a share of its own joint's way through
    // the water this frame (Air): hair streams and lifts with the run and settles as she stops, cloth a little,
    // and what trails in the world trails by that share instead of its whole drag, sinking as it does (Droop), so
    // it runs out behind her in an arc that hangs.
    // The uniform's tail is not left to any of this: it is held (Hold), posed link by link in the figure's space
    // and kept near that, and rolled so its fluke lies across her (turning a joint the shortest way to its tip
    // leaves its roll to chance: the fin stood up on edge as the tail swung round). See there.
    public sealed class SeaSprings
    {
        [Serializable]
        public sealed class Joint
        {
            public string bone;
            public float stiffness, gravity, drag, radius;
            public Vector3 gravityDir;
        }

        [Serializable]
        public sealed class Spring
        {
            public string center; // a bone of the model, or empty for the world
            public Joint[] joints;
            public int[] colliders;
        }

        [Serializable]
        public sealed class Collider
        {
            public string bone;
            public Vector3 offset, tail; // tail: the capsule's other end
            public float radius;
            public bool capsule;
        }

        [Serializable]
        public sealed class Data
        {
            public Spring[] springs;
            public Collider[] colliders;
        }

        // One joint and the next one as its tip.
        sealed class Link
        {
            public Transform bone, center;
            public Joint joint;
            public Quaternion rest;  // the joint's own turn at rest
            public Vector3 restTip;  // the tip at rest, in the joint's space
            public Vector3 axis;     // toward it
            public float length;
            public Transform anchor; // what the spring hangs from
            public bool held;        // of a chain that is held (Hold)
            public Vector3 side;     // held: what lies across the figure at rest, across the bone, in the bone's space
            public float bend;       // held: degrees it may turn from where it is posed to point
            public float steady;     // held: the share of the anchor's turning from rest that its pose does not take
            public Vector3 pose;     // held: where it is posed to point, in the figure's space (Aim)
            public Quaternion anchored; // held: the anchor's turn at rest, in the figure's space
            public Vector3 tip, was; // where the tip is and was a frame ago, in the centre's space (Into)
            public (Transform bone, Collider shape)[] colliders;
            public float air;        // the share of its joint's way through the water that pushes it back
            public int chain;        // which spring it is a link of
            public Vector3 headWas;  // where its joint was a frame ago, in the world
        }

        const float LongestStep = 0.05f; // a hitch is taken as this long, so the hair doesn't fly off
        const float Jump = 1f;           // the figure moved further than this in a frame (into a battle): start again
        // The share by what the link is: hair most; skirts, hems and strings a little (a hem has nothing to keep it
        // out of the body); a chest none; what trails in the world (tail, fins) AirLoose, sinking Droop for each
        // metre gone.
        const float AirHair = 0.16f, AirCloth = 0.04f, AirLoose = 0.2f, Droop = 0.14f;
        const float Fastest = 9f;        // m/s of a joint's way that still counts (a lunge, a throw)

        readonly List<Link> links = new List<Link>();
        readonly Transform root;
        Vector3 rootWas;

        // body: the model as instantiated; root: what carries it about (SeaFigure.Root).
        public SeaSprings(Transform body, Transform root, string json)
        {
            this.root = root;
            var data = JsonUtility.FromJson<Data>(json);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            Transform Find(string name) => bones.TryGetValue(name, out var t) ? t
                : throw new InvalidOperationException($"{body.name} 的弹簧骨骼找不到 {name}");
            foreach (var spring in data.springs)
            {
                var center = string.IsNullOrEmpty(spring.center) ? null : Find(spring.center);
                var colliders = Array.ConvertAll(spring.colliders, i => (Find(data.colliders[i].bone), data.colliders[i]));
                var anchor = Find(spring.joints[0].bone).parent;
                for (var i = 0; i + 1 < spring.joints.Length; i++)
                {
                    Transform bone = Find(spring.joints[i].bone), next = Find(spring.joints[i + 1].bone);
                    links.Add(new Link
                    {
                        bone = bone,
                        center = center,
                        joint = spring.joints[i],
                        rest = bone.localRotation,
                        restTip = next.localPosition,
                        axis = next.localPosition.normalized,
                        length = Vector3.Distance(bone.position, next.position),
                        anchor = anchor,
                        colliders = colliders,
                        air = center == null ? AirLoose : bone.name.Contains("Hair") ? AirHair : bone.name.Contains("Bust") ? 0f : AirCloth,
                        chain = Array.IndexOf(data.springs, spring),
                    });
                }
            }
            Reset();
        }

        // The link at a bone (the first joint of a spring), or -1.
        public int Find(string bone) => links.FindIndex(l => l.bone.name == bone);

        // The chain a link starts held to a pose: a tail hooked up beside a leg. As a spring hung from the joint above,
        // each link aimed by the one before, it could not keep a shape. Its links are 5 cm long and the hips they hang
        // from rise and fall 14 cm at every running step, some 2 cm a frame: kept where they were in the figure's
        // space, each link was left a good part of a turn behind, the next as much again, and by its end the tail
        // had folded on itself and thrashed across both legs.
        // Held, every link is posed on its own, to a way in the figure's space (as at rest, until Aim gives another),
        // turned by 1 - `steady` of whatever the bone the chain hangs from has turned from its rest; it may be
        // `bend` degrees off that and no more, the last `flat` links (a fluke) `flatBend`, so what one link does is
        // not handed down to the next. Its tips are kept by where that bone is, so its rise and fall carries the
        // chain whole, and `air` of each joint's way through the water pushes it back: it lags and streams by that
        // much. Stiffness times `stiffer`, no gravity, drag `drag`. And each link is rolled about itself so that
        // what lies across the figure at rest, across the link, still does whichever way the link points: a fluke
        // stays face on from behind. (Left to the shortest turn its roll is chance; carried down the chain from
        // link to link, a roll built up along it as the hook's plane swung.)
        public void Hold(int first, float stiffer, float drag, float air, float bend, float steady, int flat, float flatBend)
        {
            var end = first;
            while (end < links.Count && links[end].chain == links[first].chain) end++;
            var anchored = Quaternion.Inverse(root.rotation) * links[first].anchor.rotation;
            for (var i = first; i < end; i++)
            {
                var link = links[i];
                var reach = link.bone.TransformPoint(link.restTip) - link.bone.position;
                link.joint.stiffness *= stiffer;
                link.joint.gravity = 0f;
                link.joint.drag = drag;
                link.center = root;
                link.air = air;
                link.held = true;
                link.bend = i >= end - flat ? flatBend : bend;
                link.steady = steady;
                link.anchored = anchored;
                link.pose = Quaternion.Inverse(root.rotation) * reach.normalized;
                link.side = link.bone.InverseTransformDirection(Vector3.ProjectOnPlane(-root.right, reach).normalized);
            }
            Reset();
        }

        // Where a held link is posed to point from now on, in the figure's space.
        public void Aim(int link, Vector3 way) => links[link].pose = way.normalized;

        // Every joint back at rest and still, as posed now.
        public void Reset()
        {
            foreach (var link in links) link.bone.localRotation = link.rest;
            foreach (var link in links)
            {
                link.tip = link.was = Into(link, link.bone.TransformPoint(link.restTip));
                link.headWas = link.bone.position;
            }
            rootWas = root.position;
        }

        // After the pose for this frame is set.
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            if ((root.position - rootWas).sqrMagnitude > Jump * Jump) Reset();
            rootWas = root.position;
            // A hitch counts as LongestStep for the forces, so of the way a joint went in it only that share pushes.
            var counted = Mathf.Min(dt, LongestStep) / dt;
            dt = Mathf.Min(dt, LongestStep);
            foreach (var link in links)
            {
                var joint = link.joint;
                var head = link.bone.position;
                var scale = Scale(link.bone); // the forces are for a model at its own size
                var turn = link.bone.parent.rotation * link.rest;
                if (link.held)
                {
                    var swung = Quaternion.Slerp(Quaternion.Inverse(root.rotation) * link.anchor.rotation * Quaternion.Inverse(link.anchored), Quaternion.identity, link.steady);
                    turn = Quaternion.FromToRotation(turn * link.axis, root.rotation * (swung * link.pose)) * turn;
                }
                var aim = turn * link.axis;
                Vector3 tip = Out(link, link.tip), was = Out(link, link.was);
                var next = tip + (tip - was) * (1f - joint.drag) + (aim * joint.stiffness + joint.gravityDir * joint.gravity) * (dt * scale);
                var gone = Vector3.ClampMagnitude((head - link.headWas) * counted, Fastest * dt);
                link.headWas = head;
                // Carried with the figure, the water pushes it back; trailing in the world, its drag already holds it
                // back by the whole of it, so it is carried on by all but the share.
                if (link.center != null) next -= gone * link.air;
                else next += gone * Mathf.Max(joint.drag - link.air, 0f) + Vector3.down * (gone.magnitude * Droop);
                next = head + (next - head).normalized * link.length;
                foreach (var (bone, shape) in link.colliders)
                {
                    var at = bone.TransformPoint(shape.offset);
                    if (shape.capsule)
                    {
                        var end = bone.TransformPoint(shape.tail);
                        var along = end - at;
                        at += along * Mathf.Clamp01(Vector3.Dot(next - at, along) / Mathf.Max(along.sqrMagnitude, 1e-8f));
                    }
                    var r = shape.radius * Scale(bone) + joint.radius * scale;
                    if ((next - at).sqrMagnitude >= r * r) continue;
                    next = at + (next - at).normalized * r;
                    next = head + (next - head).normalized * link.length;
                }
                if (link.bend > 0f)
                {
                    var off = Vector3.Angle(aim, next - head);
                    if (off > link.bend) next = head + Vector3.Slerp(aim.normalized, (next - head).normalized, link.bend / off) * link.length;
                }
                link.was = link.tip;
                link.tip = Into(link, next);
                var reach = next - head;
                link.bone.rotation = Quaternion.FromToRotation(aim, reach) * turn;
                if (!link.held) continue;
                var want = Vector3.ProjectOnPlane(-root.right, reach);
                if (want.sqrMagnitude < 1e-6f) continue;
                var side = link.bone.TransformDirection(link.side);
                link.bone.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(side, want, reach), reach) * link.bone.rotation;
            }
        }

        // A tip in what carries it: a held link's by where its anchor is, turned as the figure is; else in its centre.
        Vector3 Into(Link link, Vector3 world) => link.held ? Quaternion.Inverse(root.rotation) * (world - link.anchor.position)
            : link.center != null ? link.center.InverseTransformPoint(world) : world;
        Vector3 Out(Link link, Vector3 local) => link.held ? link.anchor.position + root.rotation * local
            : link.center != null ? link.center.TransformPoint(local) : local;

        static float Scale(Transform t)
        {
            var s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
    }
}
