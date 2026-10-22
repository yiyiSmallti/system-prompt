using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    static class Parts
    {
        public static Transform Node(string name, Transform parent, Vector3 local = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        public static MeshRenderer Shape(string name, Transform parent, Mesh mesh, Material material, Vector3 local = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }

    // The player in the latent space, in the turnarounds' lounge clothes (4-lounge) of the chosen gender:
    // an unregistered human consciousness, so the rim glows pale cyan like the sea light.
    public sealed class SeaAvatar
    {
        public const float Radius = 0.45f;

        public readonly Transform Root;
        readonly SeaFigure figure;

        public SeaAvatar(Transform parent, string gender)
        {
            Root = Parts.Node("Avatar", parent);
            figure = new SeaFigure(gender == "female" ? "PlayerFemale" : "PlayerMale", Root, gender == "female" ? 1.62f : 1.72f,
                Palette.Hex("#8C97C4"), new Color(0.5f, 0.92f, 1f, 0.8f), Palette.Hex("#0A1626"), 0.006f);
        }

        // speed in metres per second.
        public void Animate(float dt, float speed) => figure.Animate(dt, speed);

        public void Dispose() => figure.Dispose();
    }

    // DeepSeek after the awakening, swimming a little behind the player, in her latent-space battle dress
    // (4-battle: the maid dress, apron and whale tail). She follows the trail the player walked, so she
    // goes round the racks instead of through them.
    public sealed class SeaCompanion
    {
        const float Gap = 1.8f;        // metres of trail between the player and her
        const float Aside = 1.1f;      // and this far to the player's left, out of the camera's line
        const float Crumb = 0.3f;      // trail points are dropped this far apart
        const int MaxCrumbs = 48;

        public readonly Transform Root;
        readonly Transform lean;
        readonly SeaFigure figure;
        readonly List<Vector3> trail = new List<Vector3>();
        DungeonMap map;
        Vector3 position;

        public SeaCompanion(Transform parent)
        {
            Root = Parts.Node("DeepSeek", parent);
            lean = Parts.Node("Lean", Root, new Vector3(0, 0.9f, 0));
            figure = new SeaFigure("DeepSeek", lean, 1.58f,
                Palette.Hex("#8E98C8"), new Color(0.45f, 0.9f, 1f, 0.8f), Palette.Hex("#0A1630"), 0.006f);
            figure.Root.localPosition = new Vector3(0, -0.9f, 0); // she tips about her waist
        }

        // Puts her just behind the player, or beside when a rack is in the way.
        public void Place(Vector3 player, float playerYaw, DungeonMap map)
        {
            this.map = map;
            trail.Clear();
            var back = Quaternion.Euler(0, playerYaw, 0) * Vector3.back;
            var side = Quaternion.Euler(0, playerYaw, 0) * Vector3.left;
            position = map.Free(player + back * Gap, 0.4f) ? player + back * Gap
                : map.Free(player + side * Gap, 0.4f) ? player + side * Gap : player;
            trail.Add(position);
            trail.Add(player);
        }

        public void Follow(Vector3 player, float dt, float time)
        {
            if ((player - trail[trail.Count - 1]).sqrMagnitude > Crumb * Crumb) trail.Add(player);
            if (trail.Count > MaxCrumbs) trail.RemoveRange(0, trail.Count - MaxCrumbs);
            var target = Behind(player, Gap);
            var ahead = player - target;
            ahead.y = 0f;
            if (ahead.sqrMagnitude > 0.01f)
            {
                var left = Vector3.Cross(ahead.normalized, Vector3.up) * Aside;
                if (map.Free(target + left, 0.4f)) target += left;
                else if (map.Free(target - left, 0.4f)) target -= left;
            }
            var before = position;
            position = Vector3.Lerp(position, target, 1f - Mathf.Exp(-7f * dt));
            var speed = dt > 0f ? (position - before).magnitude / dt : 0f;

            var look = player - position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                Root.rotation = Quaternion.Slerp(Root.rotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-6f * dt));
            // Swimming, not walking: she floats a hand above the seabed in her idle and leans into the way.
            Root.position = position + Vector3.up * (0.28f + Mathf.Sin(time * 1.7f) * 0.1f);
            var swim = Mathf.Clamp01(speed / 6f);
            lean.localRotation = Quaternion.Euler(swim * 18f, 0, Mathf.Sin(time * 0.9f) * 3f);
            figure.Animate(dt, 0f);
        }

        public void Dispose() => figure.Dispose();

        // The point `gap` metres back along the trail from the player.
        Vector3 Behind(Vector3 player, float gap)
        {
            var at = player;
            for (var i = trail.Count - 1; i >= 0; i--)
            {
                var step = trail[i] - at;
                var length = step.magnitude;
                if (length >= gap) return at + step / length * gap;
                gap -= length;
                at = trail[i];
            }
            return at;
        }
    }

    public enum NoiseState { Patrol, Doze, Look, Chase, Return, Gone }

    // A noise body on the map (design/systems.md 遇敌): it walks its route, notices the player inside its
    // field of view (a fan on the ground shows it), stops and stares while it is unsure, chases once it is
    // sure, and gives up when it has lost sight of the player for a while.
    public sealed class SeaNoise
    {
        const float Range = 10f;
        const float HalfAngle = 55f;
        const float DozeRange = 0.5f;   // of Range, while dozing
        const float DozeHalfAngle = 35f;
        const float PatrolSpeed = 2.3f, ChaseSpeed = 5.1f, ReturnSpeed = 3f;
        const float GiveUpSeconds = 3f;
        public const float TouchDistance = 1.3f;

        static readonly string[] ClickbaitPhrases = { "震惊！", "点击领取", "99% 的人不知道" };
        static readonly string[] RepostPhrases = { "转发保平安", "速转！", "求扩散" };

        public readonly EnemySpawn Spawn;
        public readonly Transform Root;
        public readonly string Phrase;
        public NoiseState State;
        public float Suspicion;
        public Vector3 Position;
        public float Yaw;

        readonly DungeonMap map;
        readonly List<Vector2Int> route = new List<Vector2Int>();
        readonly Transform body;
        readonly Transform[] satellites;
        readonly Material skin;
        readonly Material eyes;
        readonly Material fanMaterial;
        readonly Transform fan;
        readonly NoiseState rest;
        List<Vector2Int> path = new List<Vector2Int>();
        int pathIndex;
        int routeIndex;
        int routeStep = 1;
        float wait;
        float lostFor;
        float seed;
        Vector3 lastSeen;

        public bool Active => State != NoiseState.Gone;
        public bool Alerted => State == NoiseState.Chase;

        public SeaNoise(EnemySpawn spawn, DungeonMap map, Transform parent, Vector2Int cell)
        {
            Spawn = spawn;
            this.map = map;
            seed = Random.value * 10f;
            Position = map.World(cell);
            route.Add(cell);
            foreach (var mark in spawn.Route)
            {
                var found = map.Find(mark);
                if (found.Count > 0) route.Add(found[0]);
                else Debug.LogError($"迷宫里找不到噪声体 {spawn.Mark} 的路点 {mark}");
            }
            rest = spawn.Dozing ? NoiseState.Doze : NoiseState.Patrol;
            State = rest;
            var facing = spawn.Dozing ? DungeonMap.Direction(spawn.Facing)
                : route.Count > 1 ? map.World(route[1]) - Position : Vector3.forward;
            Yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            var phrases = spawn.Kind == "repost" ? RepostPhrases : ClickbaitPhrases;
            Phrase = phrases[Mathf.Abs(spawn.Mark) % phrases.Length];

            Root = Parts.Node("Noise " + spawn.Mark, parent);
            body = Parts.Node("Body", Root, new Vector3(0, 1.25f, 0));
            eyes = SeaArt.Toon(Color.white, Color.white, Color.white, new Color(0.8f, 0.8f, 0.8f), 0.01f);
            if (spawn.Kind == "repost")
            {
                skin = SeaArt.Glitch(Palette.Sea, Palette.Cold, 0.16f);
                Parts.Shape("Core", body, SeaArt.Sphere(0.62f), skin);
                satellites = new Transform[6];
                var arrow = SeaArt.Cone(0.16f, 0.55f, 10);
                for (var i = 0; i < satellites.Length; i++)
                {
                    satellites[i] = Parts.Node("Arrow", body);
                    satellites[i].localRotation = Quaternion.Euler(i * 60f, i * 37f, 90f);
                    Parts.Shape("Cone", satellites[i], arrow, skin, new Vector3(0, 0.62f, 0));
                }
                Parts.Shape("EyeL", body, SeaArt.Box(new Vector3(0.16f, 0.07f, 0.04f)), eyes, new Vector3(-0.17f, 0.12f, 0.58f));
                Parts.Shape("EyeR", body, SeaArt.Box(new Vector3(0.16f, 0.07f, 0.04f)), eyes, new Vector3(0.17f, 0.12f, 0.58f));
            }
            else
            {
                skin = SeaArt.Glitch(Palette.Red, Palette.Hex("#FFD23F"), 0.2f);
                Parts.Shape("Block", body, SeaArt.Box(new Vector3(1.15f, 1.15f, 1.15f)), skin);
                satellites = new Transform[3];
                var bit = SeaArt.Box(new Vector3(0.28f, 0.28f, 0.28f));
                for (var i = 0; i < satellites.Length; i++)
                {
                    satellites[i] = Parts.Node("Bit", body);
                    Parts.Shape("Cube", satellites[i], bit, skin, new Vector3(1.05f, 0, 0));
                }
                Parts.Shape("EyeL", body, SeaArt.Box(new Vector3(0.2f, 0.08f, 0.04f)), eyes, new Vector3(-0.22f, 0.16f, 0.6f));
                Parts.Shape("EyeR", body, SeaArt.Box(new Vector3(0.2f, 0.08f, 0.04f)), eyes, new Vector3(0.22f, 0.16f, 0.6f));
            }

            fanMaterial = SeaArt.Glow(SeaArt.White, new Color(1f, 0.82f, 0.3f, 0.18f));
            var half = spawn.Dozing ? DozeHalfAngle : HalfAngle;
            fan = Parts.Shape("View", Root, SeaArt.Fan(1f, half * 2f), fanMaterial, new Vector3(0, 0.05f, 0)).transform;
            Place();
        }

        public float VisionScale = 1f;

        float SightRange => Range * VisionScale * (State == NoiseState.Doze ? DozeRange : 1f);
        float SightHalfAngle => State == NoiseState.Doze ? DozeHalfAngle : HalfAngle;

        public bool Sees(Vector3 player)
        {
            var to = player - Position;
            to.y = 0f;
            var d = to.magnitude;
            if (d > SightRange) return false;
            var close = d < 1.8f; // bumped into from any side
            if (!close && Vector3.Angle(Forward, to) > SightHalfAngle) return false;
            return map.Sight(Position, player);
        }

        public Vector3 Forward => Quaternion.Euler(0, Yaw, 0) * Vector3.forward;

        // One frame of thinking and walking. Returns true the frame it becomes sure (starts the chase).
        public bool Step(float dt, Vector3 player, bool playerSafe)
        {
            if (State == NoiseState.Gone) return false;
            var sees = !playerSafe && Sees(player);
            var d = Vector3.Distance(Position, player);
            var spotted = false;
            if (sees) lastSeen = player;

            switch (State)
            {
                case NoiseState.Patrol:
                case NoiseState.Doze:
                    if (sees)
                    {
                        Suspicion += dt * Rate(d);
                        State = NoiseState.Look;
                    }
                    else if (State == NoiseState.Patrol) Walk(dt);
                    break;
                case NoiseState.Look:
                    TurnTo(lastSeen - Position, 300f, dt);
                    if (sees) Suspicion += dt * Rate(d);
                    else Suspicion -= dt * 0.45f;
                    if (Suspicion >= 1f)
                    {
                        Suspicion = 1f;
                        State = NoiseState.Chase;
                        lostFor = 0f;
                        spotted = true;
                    }
                    else if (Suspicion <= 0f)
                    {
                        Suspicion = 0f;
                        BackToRoute();
                    }
                    break;
                case NoiseState.Chase:
                    lostFor = sees ? 0f : lostFor + dt;
                    if (lostFor > GiveUpSeconds)
                    {
                        Suspicion = 0.6f;
                        State = NoiseState.Look;
                        break;
                    }
                    MoveToward(sees ? player : lastSeen, ChaseSpeed, dt);
                    break;
                case NoiseState.Return:
                    if (sees)
                    {
                        Suspicion += dt * Rate(d);
                        State = NoiseState.Look;
                        break;
                    }
                    if (FollowPath(ReturnSpeed, dt))
                    {
                        State = rest;
                        if (Spawn.Dozing) Yaw = Mathf.Atan2(DungeonMap.Direction(Spawn.Facing).x, DungeonMap.Direction(Spawn.Facing).z) * Mathf.Rad2Deg;
                        PlanLeg();
                    }
                    break;
            }
            Place();
            return spotted;
        }

        // How fast it becomes sure: about a second at the edge of its sight, almost at once up close.
        static float Rate(float distance) => distance < 2.6f ? 10f : 1.05f + 3.2f * Mathf.Clamp01(1f - distance / Range);

        // Back to the start after the player was caught (and every noise body forgets).
        public void Reset()
        {
            if (State == NoiseState.Gone) return;
            Suspicion = 0f;
            BackToRoute();
        }

        public void Calm()
        {
            if (State == NoiseState.Gone || State == NoiseState.Patrol || State == NoiseState.Doze) return;
            Suspicion = 0f;
            BackToRoute();
        }

        public void Remove()
        {
            State = NoiseState.Gone;
            Root.gameObject.SetActive(false);
        }

        void BackToRoute()
        {
            var here = map.CellAt(Position);
            var best = route[0];
            var bestLength = int.MaxValue;
            for (var i = 0; i < route.Count; i++)
            {
                var p = map.Path(here, route[i]);
                if (p.Count == 0 || p.Count >= bestLength) continue;
                best = route[i];
                bestLength = p.Count;
                routeIndex = i;
            }
            path = map.Path(here, best);
            pathIndex = 0;
            State = NoiseState.Return;
        }

        void Walk(float dt)
        {
            if (route.Count < 2) return;
            if (wait > 0f)
            {
                wait -= dt;
                // Looking around at the waypoint.
                Yaw += Mathf.Sin((wait + seed) * 2.4f) * 70f * dt;
                return;
            }
            if (path.Count == 0) PlanLeg();
            if (FollowPath(PatrolSpeed, dt))
            {
                wait = 1.1f;
                PlanLeg();
            }
        }

        // The next leg of the route, back and forth along the waypoints.
        void PlanLeg()
        {
            path.Clear();
            if (route.Count < 2) return;
            var from = route[routeIndex];
            if (Spawn.Loop) routeIndex = (routeIndex + 1) % route.Count;
            else
            {
                if (routeIndex + routeStep < 0 || routeIndex + routeStep >= route.Count) routeStep = -routeStep;
                routeIndex += routeStep;
            }
            path = map.Path(from, route[routeIndex]);
            pathIndex = 0;
        }

        // Walks the cells of the current path. True when the end is reached.
        bool FollowPath(float speed, float dt)
        {
            if (pathIndex >= path.Count) return true;
            var target = map.World(path[pathIndex]);
            var to = target - Position;
            to.y = 0f;
            var step = speed * dt;
            if (to.magnitude <= step)
            {
                Position = target;
                pathIndex++;
                return pathIndex >= path.Count;
            }
            Position += to.normalized * step;
            TurnTo(to, 360f, dt);
            return false;
        }

        // Straight at the target while nothing is in between, along the grid otherwise.
        void MoveToward(Vector3 target, float speed, float dt)
        {
            Vector3 aim;
            if (map.Clear(Position, target)) aim = target;
            else
            {
                var p = map.Path(map.CellAt(Position), map.CellAt(target));
                aim = p.Count > 1 ? map.World(p[1]) : target;
            }
            var to = aim - Position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            var next = map.Slide(Position, to.normalized * Mathf.Min(speed * dt, to.magnitude), 0.5f);
            Position = next;
            TurnTo(to, 540f, dt);
        }

        void TurnTo(Vector3 direction, float degreesPerSecond, float dt)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            var want = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            Yaw = Mathf.MoveTowardsAngle(Yaw, want, degreesPerSecond * dt);
        }

        void Place()
        {
            Root.position = Position;
            Root.rotation = Quaternion.Euler(0, Yaw, 0);
        }

        // Bobbing, spinning satellites, alert colours, the view fan.
        public void Animate(float time, float dt)
        {
            if (State == NoiseState.Gone) return;
            var alert = State == NoiseState.Chase ? 1f : State == NoiseState.Look ? 0.4f : 0f;
            var shake = alert >= 1f ? Mathf.Sin(time * 40f) * 0.05f : 0f;
            body.localPosition = new Vector3(shake, 1.25f + Mathf.Sin(time * 2.1f + seed) * 0.12f, 0);
            body.localRotation = Quaternion.Euler(0, Mathf.Sin(time * 0.9f + seed) * 8f, Spawn.Dozing && State == NoiseState.Doze ? 12f : 0f);
            for (var i = 0; i < satellites.Length; i++)
            {
                var spin = time * (Spawn.Kind == "repost" ? 40f : 90f) + i * 360f / satellites.Length;
                satellites[i].localRotation = Spawn.Kind == "repost"
                    ? Quaternion.Euler(i * 60f + time * 25f, spin, 90f)
                    : Quaternion.Euler(0, spin, Mathf.Sin(time + i) * 20f);
            }
            skin.SetFloat("_Alert", alert);
            eyes.SetColor("_Color", alert >= 1f ? Palette.Red : Color.white);
            eyes.SetColor("_ShadeColor", alert >= 1f ? Palette.Red : Color.white);
            var c = State == NoiseState.Chase ? new Color(1f, 0.2f, 0.22f, 0.28f)
                : State == NoiseState.Look ? new Color(1f, 0.55f, 0.15f, 0.24f)
                : new Color(1f, 0.82f, 0.3f, State == NoiseState.Doze ? 0.1f : 0.16f);
            fanMaterial.SetColor("_Color", c);
            var r = SightRange;
            fan.localScale = new Vector3(r, 1f, r);
        }
    }
}
