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

        // Destroying a GameObject leaves the meshes and materials made for it behind; this takes those under
        // root with it. Only the ones made at run time go (a negative instance id: models and materials read
        // from files have a positive one), and not what SeaArt keeps for everyone, marked DontUnloadUnusedAsset.
        public static void Free(GameObject root)
        {
            var made = new HashSet<Object>();
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) made.Add(f.sharedMesh);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer skin) made.Add(skin.sharedMesh);
                foreach (var m in r.sharedMaterials) made.Add(m);
            }
            foreach (var o in made)
                if (o != null && o.GetInstanceID() < 0 && (o.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0) Object.Destroy(o);
        }
    }

    // The player in the latent space, in the turnarounds' lounge clothes (4-lounge) of the chosen gender.
    public sealed class SeaAvatar
    {
        public const float Radius = 0.45f;

        public readonly Transform Root;
        readonly SeaFigure figure;

        public SeaAvatar(Transform parent, string gender)
        {
            Root = Parts.Node("Avatar", parent);
            figure = SeaFigure.Player(gender, false, Root);
            SeaArt.Shadow(Root, 1f);
        }

        // speed in metres per second.
        public void Animate(float dt, float speed) => figure.Animate(dt, speed);

        public void Dispose() => figure.Dispose();
    }

    // DeepSeek walking a little behind the player: in her weekend hoodie dress (3-casual) before she
    // awakens at the lighthouse, in her latent-space battle dress (4-battle) after. She follows the trail
    // the player walked, so she goes round the racks instead of through them.
    public sealed class SeaCompanion
    {
        const float Gap = 1.8f;        // metres of trail between the player and her
        const float Aside = 1.1f;      // and this far to the player's left, out of the camera's line
        const float Crumb = 0.3f;      // trail points are dropped this far apart
        const int MaxCrumbs = 48;

        public readonly Transform Root;
        readonly SeaFigure figure;
        readonly List<Vector3> trail = new List<Vector3>();
        DungeonMap map;
        Vector3 position;
        float speed;
        bool standing; // put somewhere by the scene, so not following

        public SeaCompanion(Transform parent, bool awakened)
        {
            Root = Parts.Node("DeepSeek", parent);
            figure = SeaFigure.DeepSeek(awakened, Root);
            SeaArt.Shadow(Root, 0.95f);
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
            Root.position = position;
        }

        // Stands her at a spot of the scene's choosing (the white door's mirror), facing `look`; she stays there.
        public void Stand(Vector3 at, Vector3 look)
        {
            position = at;
            standing = true;
            speed = 0f;
            trail.Clear();
            trail.Add(at);
            Root.position = at;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) Root.rotation = Quaternion.LookRotation(look);
        }

        public void Follow(Vector3 player, float dt)
        {
            if (standing)
            {
                figure.Animate(dt, 0f);
                return;
            }
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
            // Smoothed, so the easing in and out of her steps doesn't flick between idle and walk.
            var moved = dt > 0f ? (position - before).magnitude / dt : 0f;
            speed = Mathf.Lerp(speed, moved, 1f - Mathf.Exp(-10f * dt));

            var look = (speed > 0.3f ? position - before : player - position);
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                Root.rotation = Quaternion.Slerp(Root.rotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-8f * dt));
            Root.position = position;
            figure.Animate(dt, speed);
        }

        // Her face for one of the story's #face: tags (null: as modelled), and her mouth moving while she talks.
        public void SetFace(string face) => figure.SetFace(face);
        public bool Talking { set => figure.Talking = value; }
        public Vector4 FaceCells => figure.FaceCells;

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
        readonly NoiseLook look;
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
            look = new NoiseLook(spawn.Kind, Root);
            SeaArt.Shadow(Root, NoiseLook.ShadowSize);

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

        // Its look (bobbing, eyes, alert colours) and the view fan.
        public void Animate(float time, float dt)
        {
            if (State == NoiseState.Gone) return;
            var alert = State == NoiseState.Chase ? 1f : State == NoiseState.Look ? 0.4f : 0f;
            look.Animate(time, seed, alert, Spawn.Dozing && State == NoiseState.Doze ? 12f : 0f);
            var c = State == NoiseState.Chase ? new Color(1f, 0.2f, 0.22f, 0.28f)
                : State == NoiseState.Look ? new Color(1f, 0.55f, 0.15f, 0.24f)
                : new Color(1f, 0.82f, 0.3f, State == NoiseState.Doze ? 0.1f : 0.16f);
            fanMaterial.SetColor("_Color", c);
            var r = SightRange;
            fan.localScale = new Vector3(r, 1f, r);
        }
    }

    // A noise body's shape (design/systems.md 噪声体图鉴), in the maze and on the battle field. 标题党型: a
    // lumpy block of glitching red-and-yellow voxels, one now and then jumping out of it, pixels dripping off
    // underneath and pop-up ads (「震惊！」「点击领取」) springing open around it. 转发型: a sphere bristling with
    // forked arrows, copies of itself drifting off it and fading. It floats a little over the seabed, with two
    // flat eyes on the front (+z) that show what it is up to: shut while dozing, wide while it looks, slanted
    // and red in a chase.
    public sealed class NoiseLook
    {
        public const float Height = 1.25f; // of the body's centre
        public const float ShadowSize = 1.8f; // across, on the seabed under it
        const float Voxel = 0.3f;
        const float AdSeconds = 2.8f, GhostSeconds = 1.9f;
        static readonly string[] Ads = { "震惊！", "点击领取", "99%", "恭喜中奖", "限时免费" };
        // Where the 转发型's arrows point, none in front of its eyes.
        static readonly Vector3[] ArrowDirs =
        {
            new Vector3(0f, 1f, 0.15f), new Vector3(0.9f, 0.35f, 0.1f), new Vector3(-0.9f, 0.35f, 0.1f),
            new Vector3(0.6f, -0.6f, 0.45f), new Vector3(-0.6f, -0.6f, 0.45f), new Vector3(0.45f, 0.35f, -0.85f),
            new Vector3(-0.45f, 0.35f, -0.85f), new Vector3(0f, -0.5f, -0.85f),
        };

        public readonly Transform Body;
        readonly string kind;
        readonly Material skin;

        // Its loud colour (red for 标题党型, sea blue for 转发型), for what flies off it when it is hit.
        public Color Tint => skin.GetColor("_ColorA");
        readonly Material eyes;
        readonly Transform eyeL, eyeR;
        readonly Transform[] arrows = new Transform[0], ghosts = new Transform[0], ads = new Transform[0], drips = new Transform[0];
        readonly Quaternion[] arrowRest = new Quaternion[0];
        readonly Material[] ghostLooks = new Material[0];
        readonly TextMesh[] adWords = new TextMesh[0];
        readonly int[] adCycle = new int[0];
        readonly TextMesh snore;

        public NoiseLook(string kind, Transform parent)
        {
            this.kind = kind;
            Body = Parts.Node("Body", parent, new Vector3(0, Height, 0));
            eyes = SeaArt.Toon(Color.white, Color.white, Color.white, new Color(0.8f, 0.8f, 0.8f), 0.01f);
            Vector3 eyeAt;
            Vector3 eyeSize;
            if (kind == "repost")
            {
                skin = SeaArt.Glitch(Palette.Sea, Palette.Cold, 0.16f);
                var core = SeaArt.Sphere(0.55f);
                var fork = ForkedArrow(0.42f);
                Parts.Shape("Core", Body, core, skin);
                arrows = new Transform[ArrowDirs.Length];
                arrowRest = new Quaternion[arrows.Length];
                var ghostParts = new List<(Mesh, Matrix4x4)> { (core, Matrix4x4.identity) };
                for (var i = 0; i < arrows.Length; i++)
                {
                    arrowRest[i] = Quaternion.FromToRotation(Vector3.up, ArrowDirs[i].normalized) * Quaternion.Euler(0f, i * 47f, 0f);
                    arrows[i] = Parts.Node("Arrow", Body);
                    arrows[i].localRotation = arrowRest[i];
                    Parts.Shape("Fork", arrows[i], fork, skin);
                    ghostParts.Add((fork, Matrix4x4.Rotate(arrowRest[i])));
                }
                // The copies it keeps making of itself, drifting off its sides and fading.
                var ghost = SeaArt.Combine("Repost Ghost", ghostParts);
                ghosts = new Transform[3];
                ghostLooks = new Material[ghosts.Length];
                for (var i = 0; i < ghosts.Length; i++)
                {
                    ghostLooks[i] = SeaArt.Glow(SeaArt.White, Palette.Cold);
                    ghosts[i] = Parts.Shape("Copy", parent, ghost, ghostLooks[i]).transform;
                }
                eyeAt = new Vector3(0.17f, 0.12f, 0.53f);
                eyeSize = new Vector3(0.16f, 0.07f, 0.04f);
            }
            else
            {
                skin = SeaArt.Glitch(Palette.Red, Palette.Hex("#FFD23F"), 0.2f);
                skin.SetFloat("_Voxels", 1f);
                // The front row with the eyes stays put.
                Parts.Shape("Block", Body, SeaArt.Voxels(BlockCells(), Voxel, 0.012f, c => c.z == 1 && c.y == 0), skin);
                drips = new Transform[4];
                var pixel = SeaArt.Box(Vector3.one * 0.11f);
                for (var i = 0; i < drips.Length; i++) drips[i] = Parts.Shape("Drip", Body, pixel, skin).transform;
                ads = new Transform[3];
                adWords = new TextMesh[ads.Length];
                adCycle = new int[ads.Length];
                var chrome = SeaArt.Toon(Palette.Hex("#F2F4FA"), Palette.Hex("#B8BFD0"), new Color(1f, 1f, 1f, 0.3f), Palette.Hex("#40444F"), 0.008f);
                var page = SeaArt.Toon(Palette.Hex("#1B1230"), Palette.Hex("#120A22"), default, Palette.Hex("#0E0820"), 0f);
                var bar = SeaArt.Toon(Palette.Red, Palette.Red, default, Palette.Red * 0.6f, 0f);
                for (var i = 0; i < ads.Length; i++)
                {
                    // A pop-up window, turned to the camera, a little askew.
                    ads[i] = Parts.Node("Ad", Body);
                    ads[i].gameObject.AddComponent<Billboard>();
                    var window = Parts.Node("Window", ads[i]);
                    window.localRotation = Quaternion.Euler(0f, 0f, (i - 1) * 9f);
                    Parts.Shape("Chrome", window, SeaArt.Box(new Vector3(0.7f, 0.48f, 0.02f)), chrome);
                    Parts.Shape("Page", window, SeaArt.Box(new Vector3(0.62f, 0.3f, 0.02f)), page, new Vector3(0f, -0.05f, -0.012f));
                    Parts.Shape("Bar", window, SeaArt.Box(new Vector3(0.7f, 0.09f, 0.03f)), bar, new Vector3(0f, 0.195f, -0.006f));
                    Parts.Shape("Close", window, SeaArt.Box(new Vector3(0.06f, 0.06f, 0.02f)), chrome, new Vector3(0.29f, 0.195f, -0.025f));
                    adWords[i] = SeaArt.Text("Words", window, Ads[i], 0.15f, Palette.Hex("#FFE14A"), new Vector3(0f, -0.05f, -0.03f));
                    adCycle[i] = int.MinValue;
                    ads[i].localScale = Vector3.zero;
                }
                eyeAt = new Vector3(0.22f, 0.16f, 0.62f);
                eyeSize = new Vector3(0.2f, 0.08f, 0.04f);
            }
            eyeL = Parts.Node("EyeL", Body, new Vector3(-eyeAt.x, eyeAt.y, eyeAt.z));
            eyeR = Parts.Node("EyeR", Body, eyeAt);
            var eye = SeaArt.Box(eyeSize, eyeSize.y * 0.45f); // rounded off: a slit of light, not a block
            Parts.Shape("Eye", eyeL, eye, eyes);
            Parts.Shape("Eye", eyeR, eye, eyes);
            snore = SeaArt.Text("Snore", Body, "Z", 0.3f, new Color(0.7f, 0.9f, 1f, 0f));
            snore.gameObject.AddComponent<Billboard>();
        }

        // The 标题党型's voxels, in cells of Voxel round the centre: a 4 × 4 × 4 block with most corners and
        // some edges knocked off, and a few lumps standing out of it (none on the front, where the eyes are).
        static List<Vector3Int> BlockCells()
        {
            var cells = new List<Vector3Int>();
            for (var x = -2; x < 2; x++)
            for (var y = -2; y < 2; y++)
            for (var z = -2; z < 2; z++)
            {
                var outer = (x == -2 || x == 1 ? 1 : 0) + (y == -2 || y == 1 ? 1 : 0) + (z == -2 || z == 1 ? 1 : 0);
                var h = DungeonWorld.Hash(x * 3 + z + 20, y * 5 + z * 7 + 20);
                if ((outer == 3 && h < 0.7f) || (outer == 2 && h < 0.22f)) continue;
                cells.Add(new Vector3Int(x, y, z));
            }
            var inside = new HashSet<Vector3Int>(cells);
            for (var x = -3; x < 3; x++)
            for (var y = -2; y < 3; y++)
            for (var z = -3; z < 2; z++)
            {
                var c = new Vector3Int(x, y, z);
                if (inside.Contains(c)) continue;
                var touching = inside.Contains(c + Vector3Int.right) || inside.Contains(c + Vector3Int.left) || inside.Contains(c + Vector3Int.down)
                    || inside.Contains(c + Vector3Int.forward) || inside.Contains(c + Vector3Int.back);
                if (touching && DungeonWorld.Hash(x * 11 + 40, y * 13 + z * 17 + 40) < 0.1f) cells.Add(c);
            }
            return cells;
        }

        // The 转发型's arrow, along +y from `from` inside the core: a stem forking into two arrowheads. Its
        // 转发保平安 shoots them (BattleEffects.Forward).
        internal static Mesh ForkedArrow(float from)
        {
            // Round rods, standing on their foot (SeaArt.Cylinder runs from 0 up).
            var stem = SeaArt.Cylinder(0.045f, 0.04f, 0.3f, 8);
            var twig = SeaArt.Cylinder(0.034f, 0.03f, 0.26f, 8);
            var head = SeaArt.Cone(0.11f, 0.2f, 8);
            var fork = new Vector3(0f, from + 0.28f, 0f);
            var parts = new List<(Mesh, Matrix4x4)> { (stem, Matrix4x4.Translate(new Vector3(0f, from, 0f))) };
            foreach (var lean in new[] { -30f, 30f })
            {
                var turn = Quaternion.Euler(0f, 0f, lean);
                var dir = turn * Vector3.up;
                parts.Add((twig, Matrix4x4.TRS(fork - dir * 0.01f, turn, Vector3.one)));
                parts.Add((head, Matrix4x4.TRS(fork + dir * 0.24f, turn, Vector3.one)));
            }
            var arrow = SeaArt.Combine("Forked Arrow", parts);
            Object.Destroy(stem);
            Object.Destroy(twig);
            Object.Destroy(head);
            return arrow;
        }

        // Bobbing and the rest of its life. alert 0..1 heats the glitch (its eyes wide open while it looks)
        // and, at 1, shakes it with red, slanted eyes; tilt leans it over, dozing in the maze with its eyes shut.
        public void Animate(float time, float seed, float alert, float tilt = 0f)
        {
            var shake = alert >= 1f ? Mathf.Sin(time * 40f) * 0.05f : 0f;
            Body.localPosition = new Vector3(shake, Height + Mathf.Sin(time * 2.1f + seed) * 0.12f, 0);
            Body.localRotation = Quaternion.Euler(0, Mathf.Sin(time * 0.9f + seed) * 8f, tilt);
            skin.SetFloat("_Alert", alert);
            eyes.SetColor("_Color", alert >= 1f ? Palette.Red : Color.white);
            eyes.SetColor("_ShadeColor", alert >= 1f ? Palette.Red : Color.white);
            var dozing = tilt != 0f;
            Eyes(time, seed, alert, dozing);
            var z = Mathf.Repeat(time * 0.55f + seed, 1f);
            snore.transform.localPosition = new Vector3(0.45f + z * 0.25f, 0.7f + z * 0.6f, 0f);
            snore.color = new Color(0.7f, 0.9f, 1f, dozing ? Mathf.Sin(z * Mathf.PI) * 0.9f : 0f);

            if (kind == "repost") AnimateArrows(time, seed, alert);
            else AnimateBlock(time, seed, alert);
        }

        void Eyes(float time, float seed, float alert, bool dozing)
        {
            float wide = 1f, open = 1f, slant = 0f;
            if (dozing) open = 0.18f;
            else if (alert >= 1f)
            {
                open = 0.7f;
                slant = 24f;
            }
            else if (alert > 0f)
            {
                open = 1.6f;
                wide = 0.85f;
            }
            if (!dozing && Mathf.Repeat(time * 0.31f + seed * 0.37f, 1f) < 0.035f) open = 0.12f; // a blink
            eyeL.localScale = eyeR.localScale = new Vector3(wide, open, 1f);
            eyeL.localRotation = Quaternion.Euler(0f, 0f, -slant);
            eyeR.localRotation = Quaternion.Euler(0f, 0f, slant);
        }

        // The arrows turning and, one after another, shooting out a little as they pass something on; the
        // copies drifting off to the sides and back, growing and fading.
        void AnimateArrows(float time, float seed, float alert)
        {
            var pace = 1f + alert;
            for (var i = 0; i < arrows.Length; i++)
            {
                var beat = Mathf.Repeat(time * 0.7f * pace + i * 0.37f + seed, 1f);
                var push = beat < 0.2f ? Mathf.Sin(beat / 0.2f * Mathf.PI) : 0f;
                arrows[i].localRotation = arrowRest[i] * Quaternion.Euler(0f, time * 40f * pace, 0f);
                arrows[i].localScale = new Vector3(1f, 1f + push * 0.3f, 1f);
            }
            for (var i = 0; i < ghosts.Length; i++)
            {
                var t = Mathf.Repeat(time / GhostSeconds * pace + (float)i / ghosts.Length + seed * 0.1f, 1f);
                var round = Mathf.Floor(time / GhostSeconds * pace + (float)i / ghosts.Length + seed * 0.1f);
                var a = (DungeonWorld.Hash((int)round, i + 7) - 0.5f) * 2.4f + (i - 1) * 1.9f;
                var drift = new Vector3(Mathf.Sin(a), 0.35f * Mathf.Cos(a * 1.7f), -0.3f) * (t * 1.9f);
                ghosts[i].localPosition = Body.localPosition + Body.localRotation * drift;
                ghosts[i].localRotation = Body.localRotation;
                ghosts[i].localScale = Vector3.one * (1f + t * 0.25f);
                var fade = Mathf.SmoothStep(0f, 1f, t / 0.1f) * Mathf.Pow(1f - t, 1.5f);
                var tint = Color.Lerp(Palette.Cold, Palette.Red, alert >= 1f ? 0.6f : 0f);
                tint.a = 0.35f * fade;
                ghostLooks[i].SetColor("_Color", tint);
            }
        }

        // Pop-ups springing open round the block one after another (faster when alerted), each somewhere new
        // with a new come-on, and pixels dripping off underneath.
        void AnimateBlock(float time, float seed, float alert)
        {
            var pace = 1f + alert * 0.8f;
            for (var i = 0; i < ads.Length; i++)
            {
                var clock = time / AdSeconds * pace + (float)i / ads.Length + seed * 0.13f;
                var t = Mathf.Repeat(clock, 1f);
                var round = Mathf.FloorToInt(clock);
                if (round != adCycle[i])
                {
                    adCycle[i] = round;
                    // Round the sides and the back, clear of the face.
                    var a = (40f + 280f * Mathf.Repeat(DungeonWorld.Hash(round, i * 5 + 3) * 0.6f + (float)i / ads.Length, 1f)) * Mathf.Deg2Rad;
                    ads[i].localPosition = new Vector3(Mathf.Sin(a) * 1.1f, -0.15f + DungeonWorld.Hash(i * 5 + 3, round) * 0.7f, Mathf.Cos(a) * 0.75f);
                    adWords[i].text = Ads[(int)(DungeonWorld.Hash(round, i + 11) * Ads.Length) % Ads.Length];
                }
                float size;
                if (t < 0.08f) size = OutBack(t / 0.08f);
                else if (t < 0.7f) size = 1f;
                else if (t < 0.76f) size = 1f - (t - 0.7f) / 0.06f;
                else size = 0f;
                ads[i].localScale = Vector3.one * size;
            }
            for (var i = 0; i < drips.Length; i++)
            {
                var clock = time * 0.5f + (float)i / drips.Length + seed * 0.21f;
                var t = Mathf.Repeat(clock, 1f);
                var round = Mathf.Floor(clock);
                var x = (DungeonWorld.Hash((int)round, i + 30) - 0.5f) * 0.8f;
                var z = (DungeonWorld.Hash(i + 30, (int)round) - 0.5f) * 0.8f;
                drips[i].localPosition = new Vector3(x, -0.55f - t * t * 0.6f, z);
                drips[i].localScale = Vector3.one * (1f - t);
            }
        }

        static float OutBack(float t)
        {
            const float c = 1.7f;
            t -= 1f;
            return 1f + t * t * ((c + 1f) * t + c);
        }
    }
}
