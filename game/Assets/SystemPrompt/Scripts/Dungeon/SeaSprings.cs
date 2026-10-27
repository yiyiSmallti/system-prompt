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
    // turning don't drag its hair behind; the tail and the hip fins have none and trail in the world.
    // Turning a joint the shortest way to its tip leaves its roll to chance, and as the tail swung round behind
    // the figure the fin stood up on edge, its white belly to the camera; a spring marked level (the tail) has each
    // joint rolled about itself after, so its crosswise axis lies level as at rest, as SeaFigure does to the Tripo tail.
    // Hanging straight down a joint has no level, so there it lies across her back as at rest, by the bone the spring
    // hangs from (blended in from 30 degrees off plumb): left to the shortest turn, the fin twisted round as the tail
    // swung down under her when she stopped, and each joint keeping its own last roll wound the tail into a corkscrew.
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
            public bool level;
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
            public Vector3 side;     // level and across the bone at rest, in its own space (zero: not kept level)
            public Transform anchor; // what the spring hangs from
            public Vector3 hang;     // that axis at rest, in the anchor's space
            public Vector3 tip, was; // where the tip is and was a frame ago, in the centre's space
            public (Transform bone, Collider shape)[] colliders;
        }

        const float LongestStep = 0.05f; // a hitch is taken as this long, so the hair doesn't fly off
        const float Jump = 1f;           // the figure moved further than this in a frame (into a battle): start again

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
                    var across = Vector3.Cross(Vector3.up, next.position - bone.position);
                    var level = spring.level && across.sqrMagnitude > 1e-8f;
                    links.Add(new Link
                    {
                        bone = bone,
                        center = center,
                        joint = spring.joints[i],
                        rest = bone.localRotation,
                        restTip = next.localPosition,
                        axis = next.localPosition.normalized,
                        length = Vector3.Distance(bone.position, next.position),
                        side = level ? bone.InverseTransformDirection(across.normalized) : Vector3.zero,
                        anchor = anchor,
                        hang = level ? anchor.InverseTransformDirection(across.normalized) : Vector3.zero,
                        colliders = colliders,
                    });
                }
            }
            Reset();
        }

        // Every joint back at rest and still, as posed now.
        public void Reset()
        {
            foreach (var link in links) link.bone.localRotation = link.rest;
            foreach (var link in links)
            {
                link.tip = link.was = Into(link, link.bone.TransformPoint(link.restTip));
            }
            rootWas = root.position;
        }

        // After the pose for this frame is set.
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            if ((root.position - rootWas).sqrMagnitude > Jump * Jump) Reset();
            rootWas = root.position;
            dt = Mathf.Min(dt, LongestStep);
            foreach (var link in links)
            {
                var joint = link.joint;
                var head = link.bone.position;
                var scale = Scale(link.bone); // the forces are for a model at its own size
                var turn = link.bone.parent.rotation * link.rest;
                var aim = turn * link.axis;
                Vector3 tip = Out(link, link.tip), was = Out(link, link.was);
                var next = tip + (tip - was) * (1f - joint.drag) + (aim * joint.stiffness + joint.gravityDir * joint.gravity) * (dt * scale);
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
                link.was = link.tip;
                link.tip = Into(link, next);
                var reach = next - head;
                link.bone.rotation = Quaternion.FromToRotation(aim, reach) * turn;
                if (link.side == Vector3.zero) continue;
                var level = Vector3.Cross(Vector3.up, reach);
                var across = Vector3.ProjectOnPlane(link.anchor.TransformDirection(link.hang), reach);
                var flat = Mathf.Clamp01(level.magnitude / (0.5f * reach.magnitude));
                var want = level.normalized * flat + across.normalized * (1f - flat);
                if (want.sqrMagnitude < 1e-6f) continue;
                var side = link.bone.TransformDirection(link.side);
                link.bone.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(side, want, reach), reach) * link.bone.rotation;
            }
        }

        static Vector3 Into(Link link, Vector3 world) => link.center != null ? link.center.InverseTransformPoint(world) : world;
        static Vector3 Out(Link link, Vector3 local) => link.center != null ? link.center.TransformPoint(local) : local;

        static float Scale(Transform t)
        {
            var s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
    }
}
