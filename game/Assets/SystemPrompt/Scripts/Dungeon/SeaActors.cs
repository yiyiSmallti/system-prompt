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
        public const float ShutterAt = 0.28f; // seconds into a strike its phone's shutter goes
        static readonly Color FlareColor = new Color(1.6f, 1.95f, 2.1f); // cyan-white, past the lens's bloom threshold

        public readonly Transform Root;
        readonly SeaFigure figure;
        readonly Transform flare;
        readonly Material flareLook;
        float shownSpeed, shownAcceleration; // the speed its gait shows, eased so scraping along a rack doesn't flicker it
        float striking = -1f; // seconds into a strike (Strike), -1 with none
        bool shooting;

        public SeaAvatar(Transform parent, string gender)
        {
            Root = Parts.Node("Avatar", parent);
            figure = SeaFigure.Player(gender, false, Root);
            figure.Explorer = true;
            SeaArt.Shadow(Root, 1f);
            // The phone's flash, made with the avatar so a strike never stops to make it.
            flareLook = SeaArt.Glow(SeaArt.Flare, FlareColor);
            flare = Parts.Shape("Flash", Root, SeaArt.Quad(0.5f, 0.5f), flareLook).transform;
            flare.gameObject.AddComponent<Billboard>();
            flare.gameObject.SetActive(false);
        }

        // speed in metres per second.
        public void Animate(float dt, float speed)
        {
            shownSpeed = Mathf.SmoothDamp(shownSpeed, speed, ref shownAcceleration, 0.08f, Mathf.Infinity, dt);
            figure.Animate(dt, shownSpeed);
            if (striking < 0f) return;
            striking += dt;
            // The flash over the lens as the shutter goes, swelling and gone in a seventh of a second: held out in front
            // of them it is behind their back from the camera, so it is drawn big enough to burst out round them.
            var u = (striking - ShutterAt) / 0.14f;
            var on = shooting && u >= 0f && u < 1f;
            if (flare.gameObject.activeSelf != on) flare.gameObject.SetActive(on);
            if (!on) return;
            var calm = GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash ? 0.35f : 1f;
            flare.position = figure.PhoneLens;
            flare.localScale = Vector3.one * Mathf.Lerp(2.4f, 4.4f, u);
            flareLook.SetColor("_Color", new Color(FlareColor.r, FlareColor.g, FlareColor.b, (1f - u) * calm));
        }

        // A map strike (DungeonWorld.Strike), the 截图's phone being their weapon: striking first, out at arm's length
        // to take the noise body's picture, its flash going off at ShutterAt; run into by one, held up before the face,
        // on guard; jumped from behind, knocked forward from the waist up. Held until Rest.
        public void Strike(bool first, bool ambushed)
        {
            striking = 0f;
            shooting = first && !ambushed;
            if (ambushed) figure.Flinch("HitChest", 1.2f);
            else figure.RaisePhone(5f, first ? SeaFigure.PhoneHold.Reach : SeaFigure.PhoneHold.Eye);
        }

        public void Rest()
        {
            striking = -1f;
            flare.gameObject.SetActive(false);
            figure.Rest();
        }

        // Root put down somewhere new (SeaFigure.Placed).
        public void Placed() => figure.Placed();

        // The middle of the face, for DeepSeek to look at; what the player looks at (SeaFigure.LookAt); their steps.
        public Vector3 Face => figure.FaceCentre;
        public void LookAt(Vector3? point, bool quick) => figure.LookAt(point, quick);
        public event System.Action<Vector3, float> Footfall { add => figure.Footfall += value; remove => figure.Footfall -= value; }

        public void Dispose() => figure.Dispose();
    }

    // DeepSeek walking with the player: in her school uniform under the whale hoodie before she awakens at the
    // lighthouse, in her latent-space battle dress (4-battle) after. She keeps beside them on the camera's side, in the
    // picture and out of its way (Pick), slid along the racks; where there is no room beside them she follows the trail
    // the player walked, so she goes round the racks instead of through them.
    public sealed class SeaCompanion
    {
        const float Gap = 1.8f;        // metres of trail between the player and her
        const float Crumb = 0.3f;      // trail points are dropped this far apart
        const int MaxCrumbs = 48;
        const float Repick = 0.25f, Switch = 0.6f; // seconds between picks of where to keep to, and a better one must last
        const float Apart = 1.1f;      // never nearer the player than this
        // A strike's 跃劈, as in battle (BattleStage): off the ground this long in, over this long, this high at the top;
        // the landing played from this far through, and she comes down this far short of the noise body.
        const float LeapPace = 1.6f, LeapOff = 0.1f, LeapSeconds = 0.4f, LeapHeight = 1f, LandFrom = 0.8f, LandPace = 1.5f, LeapShort = 1.2f;

        public readonly Transform Root;
        readonly SeaFigure figure;
        readonly Transform shadow;
        readonly bool awakened;
        readonly List<Vector3> trail = new List<Vector3>();
        DungeonMap map;
        Vector3 position;
        float speed;
        bool standing; // put somewhere by the scene, so not following
        float striking = -1f; // seconds into a strike (Strike), -1 with none
        bool leaping, landing;
        Vector3 leapTo;
        int slot;             // the place she keeps to (Pick), on this side of the player (camera-right +1)
        float side = 1f;
        float ahead = StillAhead, aheadTo = StillAhead; // metres Beside is ahead of the player along the camera (Spot), eased to what Pick wants
        float pickIn, beaten; // seconds to the next pick; how long a better place has been better
        Vector3 via, stay;    // crossing over: the way round, from the player; standing by: where
        bool routing;

        public SeaCompanion(Transform parent, bool awakened)
        {
            this.awakened = awakened;
            Root = Parts.Node("DeepSeek", parent);
            figure = SeaFigure.DeepSeek(awakened, Root);
            figure.Explorer = true;
            shadow = SeaArt.Shadow(Root, 0.95f).transform;
        }

        // Puts her beside the player on the right of the camera looking along yaw (Beside), else just behind, else on
        // their left.
        public void Place(Vector3 player, float yaw, DungeonMap map)
        {
            this.map = map;
            trail.Clear();
            var turn = Quaternion.Euler(0, yaw, 0);
            var beside = Spot(Beside, 1f, player, turn * Vector3.forward, turn * Vector3.right, StillAhead);
            var back = player + turn * Vector3.back * Gap;
            var left = player + turn * Vector3.left * Gap;
            var free = map.Free(beside, 0.4f) && map.Clear(player, beside);
            position = free ? beside : map.Free(back, 0.4f) ? back : map.Free(left, 0.4f) ? left : player;
            trail.Add(position);
            trail.Add(player);
            Root.position = position;
            speed = 0f;
            slot = free ? Beside : Trail;
            side = 1f;
            ahead = aheadTo = StillAhead;
            pickIn = beaten = 0f;
            routing = false;
            figure.Placed();
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
            figure.Placed();
        }

        // Walks her to a spot of the scene's choosing (the white door's mirror, DungeonWorld.MirrorWalk) at `speed`
        // metres a second, facing the way she goes, slowing and turning to `look` over the last 0.3 m; within 0.05 m she
        // stands there as Stand does. True once she has: she arrives with the player instead of being put there.
        public bool WalkTo(Vector3 at, Vector3 look, float dt, float speed)
        {
            var to = at - position;
            to.y = 0f;
            var left = to.magnitude;
            if (left <= 0.05f)
            {
                Stand(at, look);
                return true;
            }
            standing = false;
            var before = position;
            var pace = left < 0.3f ? Mathf.Max(1f, speed * left / 0.3f) : speed;
            position = map.Slide(position, to / left * Mathf.Min(left, pace * dt), 0.35f);
            // Smoothed as in Follow, so the gait eases between walk and run instead of flicking.
            var moved = dt > 0f ? (position - before).magnitude / dt : 0f;
            this.speed = Mathf.Lerp(this.speed, moved, 1f - Mathf.Exp(-10f * dt));
            look.y = 0f;
            var face = left < 0.3f && look.sqrMagnitude > 0.01f ? Vector3.Slerp(to / left, look.normalized, 1f - left / 0.3f) : to;
            Root.rotation = Quaternion.Slerp(Root.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-12f * dt));
            Root.position = position;
            figure.Animate(dt, this.speed);
            return false;
        }

        // A map strike (DungeonWorld.Strike). Awakened, she leaps in with her 饭勺 when the player strikes first (the
        // 跃劈 she fights with), stands ready with it when a noise body runs into them, and is knocked about by one
        // from behind. Before, she can't fight (「我只会躲」): she ducks, scared. Held until Rest.
        public void Strike(Vector3 enemy, bool first, bool ambushed)
        {
            striking = 0f;
            leaping = landing = false;
            if (!awakened)
            {
                figure.SetFace("scared");
                if (ambushed) figure.Flinch("HitHead", 1.2f);
                figure.Play("Kneel", 2.4f);
                return;
            }
            if (ambushed)
            {
                figure.Flinch("HitHead", 1.2f);
                return;
            }
            figure.Wield(true);
            if (!first)
            {
                figure.Play("StanceDeepSeek");
                return;
            }
            var way = enemy - position;
            way.y = 0f;
            var length = way.magnitude;
            if (length < 0.01f) return;
            way /= length;
            Root.rotation = Quaternion.LookRotation(way);
            figure.Play("Leap", LeapPace);
            // Down on the seabed short of it, or as near as the racks let her on the way from where she is.
            leapTo = position;
            for (var back = LeapShort; back < length; back += 0.3f)
            {
                var spot = enemy - way * back;
                if (!map.Free(spot, 0.4f) || !map.Clear(position, spot)) continue;
                leapTo = spot;
                break;
            }
            leaping = true;
        }

        // Back beside the player as she follows, out of her strike: under the battle's wipe.
        public void Rest(Vector3 player, float cameraYaw)
        {
            striking = -1f;
            shadow.localPosition = new Vector3(0f, 0.02f, 0f);
            figure.Rest();
            figure.SetFace(null);
            Place(player, cameraYaw, map);
        }

        // A frame of keeping up with the player (playerVelocity in metres a second), as the camera shows them.
        public void Follow(Vector3 player, Vector3 playerVelocity, Camera camera, float dt)
        {
            if (standing)
            {
                figure.Animate(dt, 0f);
                return;
            }
            if (striking >= 0f)
            {
                striking += dt;
                var at = position;
                var lift = 0f;
                if (leaping)
                {
                    var t = Mathf.Clamp01((striking - LeapOff) / LeapSeconds);
                    at = Vector3.Lerp(position, leapTo, Ease.InOutCubic(t));
                    lift = LeapHeight * 4f * t * (1f - t);
                    // The landing's own drop starts a little before the feet are down.
                    if (t >= LandFrom && !landing)
                    {
                        landing = true;
                        figure.Play("Land", LandPace);
                    }
                }
                Root.position = at + Vector3.up * lift;
                shadow.localPosition = new Vector3(0f, 0.02f - lift, 0f); // left on the seabed under her
                figure.Animate(dt, 0f);
                return;
            }
            if ((player - trail[trail.Count - 1]).sqrMagnitude > Crumb * Crumb) trail.Add(player);
            if (trail.Count > MaxCrumbs) trail.RemoveRange(0, trail.Count - MaxCrumbs);
            var camF = camera.transform.forward;
            camF.y = 0f;
            camF = camF.sqrMagnitude > 1e-4f ? camF.normalized : Vector3.forward;
            var camR = Vector3.Cross(Vector3.up, camF);
            var still = playerVelocity.sqrMagnitude < 0.09f;
            pickIn -= dt;
            if (pickIn <= 0f)
            {
                pickIn = Repick;
                Pick(player, playerVelocity, still, camera, camF, camR);
            }
            ahead = Mathf.Lerp(ahead, aheadTo, 1f - Mathf.Exp(-3f * dt));
            var target = routing ? player + via : Spot(slot, side, player, camF, camR, ahead);
            if (routing && (position - target).sqrMagnitude < 0.16f) routing = false;
            if (!map.Clear(position, target)) target = Behind(player, Gap);
            var before = position;
            var rate = (target - position).sqrMagnitude > 36f ? 12f : 7f; // a long way behind: catching up
            position = map.Slide(position, Vector3.Lerp(position, target, 1f - Mathf.Exp(-rate * dt)) - position, 0.35f);
            // Never into the player.
            var off = position - player;
            off.y = 0f;
            if (off.sqrMagnitude < Apart * Apart && off.sqrMagnitude > 1e-6f)
                position = map.Slide(position, off.normalized * (Apart - off.magnitude), 0.35f);
            // Smoothed, so the easing in and out of her steps doesn't flick between idle and walk.
            var moved = dt > 0f ? (position - before).magnitude / dt : 0f;
            speed = Mathf.Lerp(speed, moved, 1f - Mathf.Exp(-10f * dt));

            // Walking, the way she goes; standing by, halfway between the player and the way the camera looks: looking
            // where they look, her cheek to the camera, not her back.
            var toPlayer = player - position;
            toPlayer.y = 0f;
            // (The way she goes is this frame's step made a direction: a step is a few centimetres long, and measured
            // as it stands it fell under the "no way to look" test below at anything short of a sprint, so she walked
            // sideways with her face to the player.)
            var step = position - before;
            step.y = 0f;
            var look = speed > 0.3f && step.sqrMagnitude > 1e-10f ? step.normalized : toPlayer.normalized + camF;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) look = toPlayer;
            if (look.sqrMagnitude > 0.0001f)
                Root.rotation = Quaternion.Slerp(Root.rotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-8f * dt));
            Root.position = position;
            figure.Animate(dt, speed);
        }

        // Where she keeps to: beside the player on the camera's side of them, a little back at a run and level with them
        // standing, further ahead along the camera when it has come in close over a rack and frames the player's feet
        // at its bottom edge, where anything level with them is cut (Beside); further back (Back), on either side; the
        // trail the player walked (Trail); or, the player standing, where she stands (Stay). The camera's right first:
        // the bottom left of the screen holds the party's cards, the bottom right only the keys.
        const int Trail = 0, Beside = 1, Back = 2, Stay = 3;
        const float RunAhead = -0.6f, StillAhead = 0.3f;

        Vector3 Spot(int kind, float s, Vector3 player, Vector3 camF, Vector3 camR, float reach) => kind switch
        {
            Beside => player + camR * (1.45f * s) + camF * reach,
            Back => player + camR * (1.2f * s) - camF * 1.3f,
            Stay => stay,
            _ => Trailing(player, camR),
        };

        // Back along the trail, a little to the camera's right of it where there is room, so the player doesn't hide her
        // running at the camera.
        Vector3 Trailing(Vector3 player, Vector3 camR)
        {
            var at = Behind(player, Gap);
            return map.Free(at + camR * 0.7f, 0.4f) ? at + camR * 0.7f : at;
        }

        // Picked again every Repick: each place on the seabed and in the player's sight is scored by how it shows in
        // the picture; another takes over from the one kept to when it has been clearly better for Switch, at once
        // when that one is no longer possible. Crossing to the player's other side goes round by their front, or
        // behind them at a run.
        void Pick(Vector3 player, Vector3 playerVelocity, bool still, Camera camera, Vector3 camF, Vector3 camR)
        {
            var chest = camera.WorldToViewportPoint(player + Vector3.up * 1.1f);
            var towardLens = Vector3.Dot(playerVelocity, camF) < -1.5f; // running at the camera: she can only trail
            // How far ahead Beside goes: as far as it takes her feet to stand clear of the bottom edge and the keys.
            aheadTo = still ? StillAhead : RunAhead;
            for (var i = 0; i < 4; i++)
            {
                var feet = camera.WorldToViewportPoint(Spot(Beside, side, player, camF, camR, aheadTo) + Vector3.up * 0.1f);
                if (feet.z > 0f && feet.y >= 0.12f) break;
                aheadTo += 0.35f;
            }
            int bestKind = -1;
            float bestSide = side, bestScore = float.MinValue, keptScore = float.MinValue;
            for (var kind = Trail; kind <= Stay; kind++)
            {
                if (kind == Stay && (!still || slot != Stay && speed > 0.3f)) continue;
                if (towardLens && kind != Trail) continue;
                for (var s = -1f; s <= 1f; s += 2f)
                {
                    if ((kind == Trail || kind == Stay) && s < 0f) continue;
                    var at = kind == Stay && slot != Stay ? position : Spot(kind, s, player, camF, camR, aheadTo);
                    var kept = kind == slot && (s == side || kind == Trail || kind == Stay);
                    var far = (at - player).sqrMagnitude;
                    if (!map.Free(at, 0.4f) || !map.Clear(player, at) || kind == Stay && (far > 3.2f * 3.2f || far < Apart * Apart)) continue;
                    // Her middle, feet and head as the camera sees them.
                    var v = camera.WorldToViewportPoint(at + Vector3.up * 0.9f);
                    var feet = camera.WorldToViewportPoint(at + Vector3.up * 0.1f);
                    var head = camera.WorldToViewportPoint(at + Vector3.up * 1.5f);
                    var score = -0.15f * Vector3.Distance(position, at);
                    if (Mathf.Abs(v.x - chest.x) < 0.07f && v.z < chest.z) score -= 3f; // in front of the player
                    if (v.x < 0.33f && v.y < 0.2f) score -= 2f;                         // under the party's cards
                    if (feet.x > 0.62f && feet.y < 0.1f) score -= 2f;                   // on the keys' strip
                    if (head.x < 0.03f || head.x > 0.97f || head.y > 0.98f || feet.y < 0.02f || v.z < 0.5f) score -= 2.5f; // out of the picture
                    if (v.x > chest.x) score += 0.5f;
                    if (kind == Trail) score -= 0.5f; // where there is no room beside them
                    if (kept) score += 0.5f;
                    if (kind == Stay) score += 0.5f; // standing about, she doesn't walk round the player for nothing
                    if (kept) keptScore = score;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestKind = kind;
                    bestSide = s;
                }
            }
            if (bestKind < 0 || bestKind == slot && (bestSide == side || slot == Trail || slot == Stay))
            {
                beaten = 0f;
                return;
            }
            if (keptScore > float.MinValue && bestScore < keptScore + 0.5f)
            {
                beaten = 0f;
                return;
            }
            beaten += Repick;
            if (keptScore > float.MinValue && beaten < Switch) return;
            beaten = 0f;
            // To the other side of the player: round by their front, or behind them on the run.
            var crossing = (slot == Beside || slot == Back) && (bestKind == Beside || bestKind == Back) && bestSide != side;
            via = playerVelocity.sqrMagnitude > 4f ? -camF * 1.6f : camF * 1.4f;
            routing = crossing && map.Free(player + via, 0.4f) && map.Clear(player, player + via);
            if (bestKind == Stay) stay = position;
            slot = bestKind;
            side = bestSide;
        }

        // Her face for one of the story's #face: tags (null: as modelled), and her mouth moving while she talks; a smile
        // wags her tail.
        public void SetFace(string face)
        {
            figure.SetFace(face);
            if (face == "smile") figure.Wag();
        }

        // A wag of her tail: a 数据珍珠 taken, the gate opened, the light found.
        public void Wag() => figure.Wag();
        public bool Talking { set => figure.Talking = value; }
        public Vector4 FaceCells => figure.FaceCells;

        // The middle of her face, for the player to look at; what she looks at (SeaFigure.LookAt); how fast she goes;
        // her steps.
        public Vector3 Face => figure.FaceCentre;
        public void LookAt(Vector3? point, bool quick) => figure.LookAt(point, quick);
        public float Speed => speed;
        public event System.Action<Vector3, float> Footfall { add => figure.Footfall += value; remove => figure.Footfall -= value; }

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
        const float PatrolSpeed = 1.7f, ReturnSpeed = 2.2f;
        public float ChaseSpeed = 3.8f; // metres a second after the player; the deep layer's run faster (DungeonDef.ChaseSpeed)
        const float GiveUpSeconds = 3f;
        public const float TouchDistance = 1.3f;
        const float Lift = 0.55f; // metres a strike's flash lifts one caught unawares

        static readonly string[] ClickbaitPhrases = { "震惊！", "点击领取", "99% 的人不知道" };
        static readonly string[] RepostPhrases = { "转发保平安", "速转！", "求扩散" };
        static readonly string[] ColdFactPhrases = { "每天一个冷知识", "你知道吗", "据统计" };

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
        float struck = -1f;  // seconds into a strike on it (Struck), -1 with none
        bool shot, caught;   // the player's phone struck first; caught unawares at it
        Vector3 toPlayer;    // level, from it at the strike
        float lunge;         // metres it lunges at the player, short of going into them

        public bool Active => State != NoiseState.Gone;
        public bool Alerted => State == NoiseState.Chase;
        public bool Engaged => struck >= 0f;

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
            var phrases = spawn.Kind == "repost" ? RepostPhrases : spawn.Kind == "coldfact" ? ColdFactPhrases : ClickbaitPhrases;
            Phrase = phrases[Mathf.Abs(spawn.Mark) % phrases.Length];

            Root = Parts.Node("Noise " + spawn.Mark, parent);
            look = new NoiseLook(spawn.Kind, Root, seed);
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

        // Called by a neighbour that has seen the player: straight into the chase, towards where they are.
        public void Alert(Vector3 player)
        {
            if (State == NoiseState.Gone || State == NoiseState.Chase) return;
            lastSeen = player;
            Suspicion = 1f;
            lostFor = 0f;
            State = NoiseState.Chase;
        }

        // Back on its route from the start, beaten or not: the maintenance meter ran out and the cache gave it back.
        public void Revive()
        {
            Unstrike();
            if (State != NoiseState.Gone)
            {
                Reset();
                return;
            }
            Root.gameObject.SetActive(true);
            Position = map.World(route[0]);
            routeIndex = 0;
            routeStep = 1;
            path.Clear();
            Suspicion = 0f;
            State = rest;
            var facing = Spawn.Dozing ? DungeonMap.Direction(Spawn.Facing) : route.Count > 1 ? map.World(route[1]) - Position : Vector3.forward;
            Yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            Place();
        }

        public void Remove()
        {
            State = NoiseState.Gone;
            Root.gameObject.SetActive(false);
        }

        // A map strike (DungeonWorld.Strike) from the stamp to the battle's wipe, what it is doing left as it was. The
        // player's phone striking first (shot): caught unawares, it starts up off the seabed, the flash at the shutter
        // knocks it back with its cubes shaken loose, and it is held there in the picture, the 标题党型 coming apart
        // as the wipe takes it; seen coming, it is knocked back. Running into the player, or jumping them, it lunges.
        public void Struck(bool shot, bool unaware, Vector3 player)
        {
            struck = 0f;
            this.shot = shot;
            caught = shot && unaware;
            toPlayer = player - Position;
            toPlayer.y = 0f;
            // Up to its front (drawn out by the hold below) against the player's side: one that ran into them is
            // already that close and hardly moves, one joining from further off comes the whole way.
            lunge = Mathf.Clamp(toPlayer.magnitude - (NoiseLook.Radius * 1.3f + 0.3f), 0f, 0.6f);
            toPlayer = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Forward;
            if (!shot) look.Rig.Hold(0f, 10f, 0.3f); // tipped and drawn out toward the player
        }

        // Where the top of its body will be as a strike holds it (lifted when caught unawares), for the stamp over it.
        public Vector3 StruckTop => Position + Vector3.up * (NoiseLook.Height + NoiseLook.Top + (caught ? Lift : 0f));

        void StrikeFrame(float dt)
        {
            var was = struck;
            struck += dt;
            if (!shot)
            {
                TurnTo(toPlayer, 900f, dt);
                Place();
                look.Shift = Root.InverseTransformDirection(toPlayer) * (lunge * Ease.OutCubic(Mathf.Clamp01(struck / 0.18f)));
                return;
            }
            if (caught) look.Shift = Vector3.up * (Lift * Ease.OutCubic(Mathf.Clamp01(struck / 0.3f)));
            if (was < SeaAvatar.ShutterAt && struck >= SeaAvatar.ShutterAt)
            {
                look.SetMood(NoiseMood.Hurt, 2f);
                look.Flash(0.6f);
                look.Loosen(1f, 0.3f);
                look.Rig.Kick(Root.InverseTransformDirection(-toPlayer), 0.3f, 22f, 0.25f);
            }
            if (!caught) return;
            if (was < 0.36f && struck >= 0.36f) look.Rig.Freeze = 2f; // held as the picture took it
            if (struck < 0.55f) return;
            if (look.Kind != "repost" && look.Kind != "coldfact") look.Burst = 0.4f * Mathf.Clamp01((struck - 0.55f) / 0.2f);
            else if (was < 0.55f) look.Tear(0.5f, 0.4f);
        }

        // As it was before the strike: a battle that never came, or back on its route.
        public void Unstrike()
        {
            if (struck < 0f) return;
            struck = -1f;
            look.Shift = Vector3.zero;
            look.Rig.Freeze = 0f;
            look.Rig.Hold(0f);
            look.Burst = 0f;
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

        // Its look (bobbing, eyes for what it is doing, alert colours) and the view fan.
        public void Animate(float time, float dt)
        {
            if (State == NoiseState.Gone) return;
            if (struck >= 0f) StrikeFrame(dt);
            look.BaseMood = struck >= 0f ? (caught ? NoiseMood.Wary : NoiseMood.Angry) : State switch
            {
                NoiseState.Doze => NoiseMood.Asleep,
                NoiseState.Look => NoiseMood.Wary,
                NoiseState.Chase => NoiseMood.Angry,
                _ => NoiseMood.Calm,
            };
            look.Animate(dt, struck >= 0f || State == NoiseState.Chase ? 1f : State == NoiseState.Look ? 0.4f : 0f);
            var c = State == NoiseState.Chase ? new Color(1f, 0.2f, 0.22f, 0.28f)
                : State == NoiseState.Look ? new Color(1f, 0.55f, 0.15f, 0.24f)
                : new Color(1f, 0.82f, 0.3f, State == NoiseState.Doze ? 0.1f : 0.16f);
            fanMaterial.SetColor("_Color", c);
            var r = SightRange;
            fan.localScale = new Vector3(r, 1f, r);
        }
    }

    // What a noise body's eyes (NoiseLook) show it is up to, and with them how it carries itself. Something that
    // has just happened to it (SetMood: hurt by a blow, angry while it attacks or gets up) goes over what its
    // owner says it is doing (BaseMood: dozing, looking, chasing on the map; picked out, knocked down or cowering
    // before a 集成 in a battle); dying goes over everything.
    public enum NoiseMood { Calm, Wary, Angry, Hurt, Dizzy, Asleep, Dying, Cower }

    // Springs that throw a noise body's look about when something happens to it and bring it back: the whole of
    // it moved and tipped over (on its Body), its shape squashed flat or stretched long along its front (Form),
    // and what sticks out of it knocked in by a blow (Fold). Each wobbles back to where it is held (Hold) twice,
    // in about a third of a second. A kick with a freeze snaps it straight to its furthest and holds it there that
    // long, the hit-stop of a blow landing on it alone; without one it is sent on its way, furthest after ~55 ms.
    public sealed class NoiseRig
    {
        const float W = 24f, Z = 0.32f;
        const float PeakPerVelocity = 0.0274f; // the furthest a kick of 1 a second gets, at W and Z
        const float Substep = 1f / 120f;       // ω·dt under 0.2: the springs stay stable however the frames come

        struct Spring
        {
            public float X, V, Rest;

            public void Step(float dt)
            {
                V += (-2f * Z * W * V - W * W * (X - Rest)) * dt;
                X += V * dt;
            }

            public void Kick(float by, bool snap)
            {
                if (snap) X += by;
                else V += by / PeakPerVelocity;
            }
        }

        Spring x, y, z, pitch, roll, yaw, squash, stretch, fold;
        float held;           // the squash it is held at, its breath on top
        public float Freeze;  // seconds it stays as it is
        public float Tremble; // metres its owner has it shaking
        public float Breath;  // the squash of its breathing (NoiseLook)

        public Vector3 Offset => new Vector3(x.X, y.X, z.X);
        public Quaternion Tilt => Quaternion.Euler(pitch.X, yaw.X, roll.X);
        public float Fold => fold.X;

        // Squashed flat and wide (drawn up tall below 0), stretched long along its front.
        public Vector3 FormScale
        {
            get
            {
                float s = Mathf.Clamp(squash.X, -0.6f, 0.6f), l = Mathf.Clamp(stretch.X, -0.6f, 0.8f);
                return new Vector3(1f + 0.5f * s - 0.5f * l, 1f - s - 0.5f * l, 1f + 0.5f * s + l);
            }
        }

        // Thrown `metres` along `dir` (a unit direction in the noise body's own space, its Root's), its top
        // tipped `tiltDeg` the same way, turned by yawDeg, squashed and stretched by those amounts.
        public void Kick(Vector3 dir, float metres, float tiltDeg, float squashBy, float freeze = 0f, float yawDeg = 0f, float stretchBy = 0f)
        {
            var snap = freeze > 0f;
            x.Kick(dir.x * metres, snap);
            y.Kick(dir.y * metres, snap);
            z.Kick(dir.z * metres, snap);
            pitch.Kick(tiltDeg * dir.z, snap); // a positive pitch tips the top forward (+z)
            roll.Kick(-tiltDeg * dir.x, snap); // a positive roll tips it toward -x
            yaw.Kick(yawDeg, snap);
            squash.Kick(squashBy, snap);
            stretch.Kick(stretchBy, snap);
            Freeze = Mathf.Max(Freeze, freeze);
        }

        // What sticks out of it knocked in all the way, to spring back out past where it was.
        public void Jolt() => fold.X = 1f;

        // Where it comes to rest: squashed (drawn up tall below 0), tipped forward by `pitch` degrees, stretched.
        public void Hold(float squash, float pitch = 0f, float stretch = 0f)
        {
            held = squash;
            this.pitch.Rest = pitch;
            this.stretch.Rest = stretch;
        }

        public void Step(float dt)
        {
            if (Freeze > 0f)
            {
                Freeze -= dt;
                return;
            }
            squash.Rest = held + Breath;
            dt = Mathf.Min(dt, 0.1f);
            if (dt <= 0f) return;
            var n = Mathf.CeilToInt(dt / Substep);
            var h = dt / n;
            for (var i = 0; i < n; i++)
            {
                x.Step(h);
                y.Step(h);
                z.Step(h);
                pitch.Step(h);
                roll.Step(h);
                yaw.Step(h);
                squash.Step(h);
                stretch.Step(h);
                fold.Step(h);
            }
        }
    }

    // A noise body's shape (design/systems.md 噪声体图鉴), in the maze and on the battle field. 标题党型: a
    // lumpy block of glitching red-and-yellow voxels, one now and then jumping out of it, pixels dripping off
    // underneath and pop-up ads (「震惊！」「点击领取」) springing open around it. 转发型: a sphere bristling with
    // forked arrows, copies of itself drifting off it and fading. 冷知识型: a stack of pale blue index cards
    // that keeps shuffling itself, made-up figures (「99.7%」「据统计」) floating up off it. It floats a little
    // over the seabed, breathing, with two flat eyes on the front (+z) lit by its mood (NoiseMood): shut while
    // dozing, wide and amber while it looks or is picked out, slanted and red, shaking, in a chase or an attack,
    // squeezed shut by a blow, rolling round under a spinner of stars when knocked down. Each runs on its own
    // clock and seed, which also give it its own lumps, size and turn, so no two of a kind look or flicker
    // alike. A blow throws it about on its rig (NoiseRig), flashes it and knocks its cubes loose; knocked down
    // it lies dim and slow; dying, it bursts (Burst, Debris). On the battle field, filmed close, it has a face
    // to read (Face): eyes half as big again in a dark socket, shaped by lids or crossed out by 「> <」, and the
    // 标题党型 a mouth that shouts.
    public sealed class NoiseLook
    {
        public const float Height = 1.25f; // of the body's centre
        public const float Radius = 0.6f;  // how far its body reaches out round the centre (a 转发型's arrows aside)
        public const float Top = 1f;       // how high over the centre its highest part reaches (the 转发型's top arrow)
        public const float ShadowSize = 1.8f; // across, on the seabed under it
        const float Voxel = 0.3f;
        const float Socket = 0.01f; // the dark outline round its eyes, widened when it is angry
        const float AdSeconds = 2.8f, GhostSeconds = 1.9f, FactSeconds = 2.4f;
        static readonly string[] Ads = { "震惊！", "点击领取", "99%", "恭喜中奖", "限时免费" };
        static readonly string[] Facts = { "99.7%", "据统计", "你知道吗", "每天一个", "第 3 条", "其实" };
        // Where the 转发型's arrows point, none in front of its eyes.
        static readonly Vector3[] ArrowDirs =
        {
            new Vector3(0f, 1f, 0.15f), new Vector3(0.9f, 0.35f, 0.1f), new Vector3(-0.9f, 0.35f, 0.1f),
            new Vector3(0.6f, -0.6f, 0.45f), new Vector3(-0.6f, -0.6f, 0.45f), new Vector3(0.45f, 0.35f, -0.85f),
            new Vector3(-0.45f, 0.35f, -0.85f), new Vector3(0f, -0.5f, -0.85f),
        };
        static readonly Color Unlit = new Color(0.05f, 0.05f, 0.05f);
        static readonly Color Blank = new Color(1.6f, 1.6f, 1.6f); // a blow's flash, just over the glow's threshold
        static readonly int ClockId = Shader.PropertyToID("_Clock"), AlertId = Shader.PropertyToID("_Alert"), HitId = Shader.PropertyToID("_Hit"),
            TintId = Shader.PropertyToID("_Tint"), LooseId = Shader.PropertyToID("_Loose"), JitterId = Shader.PropertyToID("_Jitter"),
            DimId = Shader.PropertyToID("_Dim"), CalmId = Shader.PropertyToID("_Calm"), BurstId = Shader.PropertyToID("_Burst"),
            BurstDirId = Shader.PropertyToID("_BurstDir"), EmissionId = Shader.PropertyToID("_Emission"), RimId = Shader.PropertyToID("_OutlineWidth"),
            FrostId = Shader.PropertyToID("_Frost");

        public readonly Transform Body;  // bob, sway, the rig's throw and tilt; the ads, drips, snore and figures go with it
        public readonly Transform Form;  // its shape and eyes, squashed and stretched by the rig
        public readonly NoiseRig Rig = new NoiseRig();
        public readonly string Kind;
        public float Pace = 1f;          // how fast its clock runs (0 holds it: frozen in ice)
        public Vector3 Shift;            // its owner's move of it on its Body, in the parent's space (a map strike's lunge or start)
        public float Clock { get; private set; }
        public NoiseMood BaseMood;       // what it is doing, set by its owner every frame
        public NoiseMood Mood { get; private set; }

        // Its loud colours (red and yellow for 标题党型, sea blue for 转发型, pale blue for 冷知识型), for what
        // flies off it when it is hit.
        public Color Tint => skin.GetColor("_ColorA");
        public Color TintB => skin.GetColor("_ColorB");
        // Where the stars of a knocked-down one go round: clear over it, lying on its side half sunk in the seabed.
        public Vector3 Crown => Body.position + Vector3.up * 0.95f;

        readonly float seed;
        readonly Material skin, eyes;
        readonly Transform eyeL, eyeR, block, core;
        readonly Transform[] arrows = new Transform[0], ghosts = new Transform[0], ads = new Transform[0], windows = new Transform[0], drips = new Transform[0];
        readonly Quaternion[] arrowRest = new Quaternion[0];
        readonly Material[] ghostLooks = new Material[0];
        readonly TextMesh[] adWords = new TextMesh[0];
        readonly int[] adCycle = new int[0];
        readonly Transform[] cards = new Transform[0];
        readonly TextMesh[] facts = new TextMesh[0];
        readonly int[] factCycle = new int[0];
        readonly TextMesh snore;
        readonly Vector3 restScale;
        readonly float jitter; // the skin's own, what a tear goes back to
        Transform spinner;
        NoiseMood timed;
        float timedLeft, dying = -1f, scattered = -1f; // seconds into its mood, dying, its parts flown off (-1 not)
        float flash, tear, tearLength = 1f, tearLeft, loose, looseLength = 1f, looseLeft, tingeLength = 1f, tingeLeft;
        Color flashColor = Blank, tinge;
        float open = 1f, wide = 1f, slant, socket = Socket, lean, burst;
        float adTime, arrowTime, arrowTurn, cardTime, spinTurn, flip = 1f;
        int flips;
        // Its attack coming (Windup): whether it is wound up, how far into the pose (0 to 1, eased), seconds since it
        // began, which way the one it goes for is (in its parent's space, flat), its figures all asking; the parts
        // it has thrown (Throw), seconds since each went (-1: there), back ThrownFor later.
        bool winding, asking;
        float wound, windTime;
        Vector3 windToward = Vector3.forward;
        readonly float[] adGone = new float[0], arrowGone = new float[0], cardGone = new float[0];
        readonly Vector3[] adHome = new Vector3[0];
        readonly float faceZ;
        const float ThrownFor = 0.9f, Regrown = 0.2f;
        // Coming together on the battle field (Unform, Assemble): how far, 0 nothing yet to 1 whole, and the seconds
        // left before it starts (-1: not asked yet).
        float formed = 1f, formWait = -1f;
        const float FormSeconds = 0.45f;
        // Cowering in the hold-up (BaseMood Cower): how far into it (0 to 1, eased over 0.2 s) and a clock of its own
        // for the begging, in real time (lying down, its own runs slow); one beg every BegSeconds.
        float cower, begTime;
        const float BegSeconds = 1.1f;
        static readonly string[] Begs = { "求放过", "别删我", "我错了", "下次一定改" };
        static readonly string[] Confessions = { "其实…", "我编的", "求别辟谣" };
        // The battle face (null on the map, which keeps its plain slits): per eye (left, right) the slit, the lids
        // over it (each from its edge to the socket's, so never past it) and the two bars of its 「> <」 or X; half
        // an eye's height, where the socket ends over and under its middle, where the lids' edges are and the slits'
        // tilt; the 标题党型's mouth, how open it is (eased from mouthFrom to mouthTo), and its shout.
        readonly MeshRenderer[] slits;
        readonly Transform[] lidsUp, lidsDown, marksA, marksB;
        readonly Transform mouth;
        readonly TextMesh shout;
        readonly float eyeHalf, socketEdge, markLength;
        float lidUp, lidDown, slitTurn, mouthFrom, mouthTo, mouthTime = 1f, mouthOpen;
        static readonly Color SocketDark = new Color(0.04f, 0.04f, 0.07f), ShoutColor = new Color(1f, 0.882f, 0.29f); // #FFE14A, the ads' words
        const float MarkTilt = 28f;

        // `battle`: on the battle field, with its face (Face); the map's bodies are seen from further off.
        public NoiseLook(string kind, Transform parent, float seed, bool battle = false)
        {
            Kind = kind;
            this.seed = seed;
            Clock = seed * 37.1f;
            Body = Parts.Node("Body", parent, new Vector3(0, Height, 0));
            SeaShade.Add(Body, false); // its shadow on the seabed (SeaShade.cs)
            Form = Parts.Node("Form", Body);
            // A size and a turn of its own.
            var mark = Mathf.FloorToInt(seed * 113f);
            restScale = Vector3.one * (0.94f + 0.12f * DungeonWorld.Hash(mark, 5));
            Form.localScale = restScale;
            Form.localRotation = Quaternion.Euler(0f, (DungeonWorld.Hash(mark, 9) - 0.5f) * 12f, 0f);
            // Lit by its mood alone (Eyes): dark where its emission isn't.
            eyes = SeaArt.Toon(Unlit, Unlit, new Color(1f, 1f, 1f, 0f), Color.white, Socket);
            Vector3 eyeAt;
            Vector3 eyeSize;
            if (kind == "repost")
            {
                skin = SeaArt.Glitch(Palette.Sea, Palette.Cold, 0.16f);
                var ball = SeaArt.Sphere(0.55f);
                var fork = ForkedArrow(0.42f);
                core = Parts.Shape("Core", Form, ball, skin).transform;
                arrows = new Transform[ArrowDirs.Length];
                arrowRest = new Quaternion[arrows.Length];
                arrowGone = Gone(arrows.Length);
                var ghostParts = new List<(Mesh, Matrix4x4)> { (ball, Matrix4x4.identity) };
                for (var i = 0; i < arrows.Length; i++)
                {
                    arrowRest[i] = Quaternion.FromToRotation(Vector3.up, ArrowDirs[i].normalized) * Quaternion.Euler(0f, i * 47f, 0f);
                    arrows[i] = Parts.Node("Arrow", Form);
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
            else if (kind == "coldfact")
            {
                skin = SeaArt.Glitch(Palette.Hex("#8FC8FF"), Palette.Cold, 0.12f);
                // A stack of index cards fanned a little, the front one (+z) carrying the eyes.
                var card = SeaArt.Box(new Vector3(1.2f, 0.8f, 0.08f), 0.03f);
                cards = new Transform[4];
                cardGone = Gone(cards.Length);
                for (var i = 0; i < cards.Length; i++)
                {
                    cards[i] = Parts.Node("Card", Form, new Vector3(0f, 0f, -0.12f * i));
                    Parts.Shape("Face", cards[i], card, skin);
                }
                // The figures it keeps quoting, rising off the stack and fading.
                facts = new TextMesh[3];
                factCycle = new int[facts.Length];
                for (var i = 0; i < facts.Length; i++)
                {
                    facts[i] = SeaArt.Text("Fact", Body, Facts[i], 0.16f, new Color(0.75f, 0.91f, 1f, 0f));
                    facts[i].gameObject.AddComponent<Billboard>();
                    factCycle[i] = int.MinValue;
                }
                eyeAt = new Vector3(0.24f, 0.12f, 0.07f);
                eyeSize = new Vector3(0.2f, 0.08f, 0.04f);
            }
            else
            {
                skin = SeaArt.Glitch(Palette.Red, Palette.Hex("#FFD23F"), 0.2f);
                skin.SetFloat("_Voxels", 1f);
                // Lumps of its own; the front row with the eyes stays put, and in a battle the two cells under them
                // that carry its mouth. Its cubes fly metres off when it bursts, which the mesh's bounds take in, or
                // it is culled while they are still in view.
                var cells = SeaArt.Voxels(BlockCells(Mathf.FloorToInt(seed * 7f) % 5), Voxel, 0.012f,
                    c => c.z == 1 && (c.y == 0 || (battle && c.y == -1 && (c.x == -1 || c.x == 0))));
                cells.bounds = new Bounds(cells.bounds.center, cells.bounds.size + Vector3.one * 7f);
                block = Parts.Shape("Block", Form, cells, skin).transform;
                drips = new Transform[4];
                var pixel = SeaArt.Box(Vector3.one * 0.11f);
                for (var i = 0; i < drips.Length; i++) drips[i] = Parts.Shape("Drip", Body, pixel, skin).transform;
                ads = new Transform[3];
                windows = new Transform[ads.Length];
                adWords = new TextMesh[ads.Length];
                adCycle = new int[ads.Length];
                adHome = new Vector3[ads.Length];
                adGone = Gone(ads.Length);
                var chrome = SeaArt.Toon(Palette.Hex("#F2F4FA"), Palette.Hex("#B8BFD0"), new Color(1f, 1f, 1f, 0.3f), Palette.Hex("#40444F"), 0.008f);
                var page = SeaArt.Toon(Palette.Hex("#1B1230"), Palette.Hex("#120A22"), default, Palette.Hex("#0E0820"), 0f);
                var bar = SeaArt.Toon(Palette.Red, Palette.Red, default, Palette.Red * 0.6f, 0f);
                for (var i = 0; i < ads.Length; i++)
                {
                    // A pop-up window, turned to the camera, a little askew.
                    ads[i] = Parts.Node("Ad", Body);
                    ads[i].gameObject.AddComponent<Billboard>();
                    var window = windows[i] = Parts.Node("Window", ads[i]);
                    window.localRotation = Quaternion.Euler(0f, 0f, (i - 1) * 9f);
                    Parts.Shape("Chrome", window, SeaArt.Box(new Vector3(0.7f, 0.48f, 0.02f)), chrome);
                    Parts.Shape("Page", window, SeaArt.Box(new Vector3(0.62f, 0.3f, 0.02f)), page, new Vector3(0f, -0.05f, -0.012f));
                    Parts.Shape("Bar", window, SeaArt.Box(new Vector3(0.7f, 0.09f, 0.03f)), bar, new Vector3(0f, 0.195f, -0.006f));
                    Parts.Shape("Close", window, SeaArt.Box(new Vector3(0.06f, 0.06f, 0.02f)), chrome, new Vector3(0.29f, 0.195f, -0.025f));
                    adWords[i] = SeaArt.Text("Words", window, Ads[i], 0.15f, Palette.Hex("#FFE14A"), new Vector3(0f, -0.05f, -0.03f));
                    adCycle[i] = int.MinValue;
                    ads[i].localScale = Vector3.zero;
                }
                if (battle)
                {
                    // Its mouth under the eyes, on the cells that keep still: a slot dark as its pop-ups' pages
                    // under their red bar, its 「震惊！」 in it, shut (flat) till it shouts (Face).
                    mouth = Parts.Node("Mouth", Form, new Vector3(0f, -0.15f, 0.61f));
                    Parts.Shape("Slot", mouth, SeaArt.Box(new Vector3(0.36f, 0.16f, 0.02f), 0.02f), page);
                    Parts.Shape("Lip", mouth, SeaArt.Box(new Vector3(0.38f, 0.04f, 0.025f), 0.01f), bar, new Vector3(0f, 0.08f, 0.002f));
                    shout = SeaArt.Text("Shout", mouth, "震惊！", 0.07f, ShoutColor, new Vector3(0f, -0.01f, 0.015f));
                    shout.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // read from in front of the face
                    mouth.localScale = new Vector3(1f, 0f, 1f);
                }
                eyeAt = new Vector3(0.22f, 0.16f, 0.62f);
                eyeSize = new Vector3(0.2f, 0.08f, 0.04f);
            }
            faceZ = eyeAt.z;
            if (battle) eyeSize = new Vector3(eyeSize.x * 1.5f, eyeSize.y * 1.5f, eyeSize.z);
            eyeL = Parts.Node("EyeL", Form, new Vector3(-eyeAt.x, eyeAt.y, eyeAt.z));
            eyeR = Parts.Node("EyeR", Form, eyeAt);
            var eye = SeaArt.Box(eyeSize, eyeSize.y * 0.45f); // rounded off: a slit of light, not a block
            var slitL = Parts.Shape("Eye", eyeL, eye, eyes);
            var slitR = Parts.Shape("Eye", eyeR, eye, eyes);
            if (battle)
            {
                // Its face (Face): a dark socket behind both eyes; over each slit two lids of the socket's dark,
                // standing on the edge that closes over it (a box a metre tall, scaled to reach the socket's edge),
                // and two thin bars of the eyes' light for 「> <」 and X.
                var dark = SeaArt.Toon(SocketDark, SocketDark, default, default, 0f);
                Parts.Shape("Socket", Form, SeaArt.Box(new Vector3(2f * eyeAt.x + 0.42f, eyeSize.y + 0.14f, 0.02f), 0.02f), dark,
                    new Vector3(0f, eyeAt.y, eyeAt.z - 0.01f));
                eyeHalf = eyeSize.y / 2f;
                socketEdge = eyeHalf + 0.065f;
                lidUp = 0.5f * eyeHalf;
                lidDown = -lidUp;
                var lid = SeaArt.Box(new Vector3(eyeSize.x * 1.1f, 1f, 0.012f), 0.003f);
                markLength = eyeSize.x * 0.55f;
                var stroke = SeaArt.Box(new Vector3(markLength, 0.03f, 0.02f), 0.008f);
                slits = new[] { slitL, slitR };
                lidsUp = new Transform[2];
                lidsDown = new Transform[2];
                marksA = new Transform[2];
                marksB = new Transform[2];
                var front = eyeSize.z / 2f + 0.006f;
                for (var i = 0; i < 2; i++)
                {
                    var at = i == 0 ? eyeL : eyeR;
                    lidsUp[i] = Parts.Node("LidUp", at, new Vector3(0f, lidUp, front));
                    Parts.Shape("Lid", lidsUp[i], lid, dark, new Vector3(0f, 0.5f, 0f));
                    lidsDown[i] = Parts.Node("LidDown", at, new Vector3(0f, lidDown, front));
                    Parts.Shape("Lid", lidsDown[i], lid, dark, new Vector3(0f, -0.5f, 0f));
                    marksA[i] = Parts.Shape("Mark", at, stroke, eyes).transform;
                    marksB[i] = Parts.Shape("Mark", at, stroke, eyes).transform;
                    marksA[i].localScale = marksB[i].localScale = Vector3.zero;
                }
            }
            snore = SeaArt.Text("Snore", Body, "Z", 0.3f, new Color(0.7f, 0.9f, 1f, 0f));
            snore.gameObject.AddComponent<Billboard>();
            jitter = skin.GetFloat(JitterId);
            skin.SetFloat("_Seed", seed * 3.7f);
            skin.SetFloat(ClockId, Clock); // its own tick from the first frame drawn
        }

        // The 标题党型's voxels, in cells of Voxel round the centre: a 4 × 4 × 4 block with most corners and
        // some edges knocked off, and a few lumps standing out of it (none on the front, where the eyes are),
        // which ones by `variant` (0 to 4); the row with the eyes is always whole.
        static List<Vector3Int> BlockCells(int variant)
        {
            var cells = new List<Vector3Int>();
            for (var x = -2; x < 2; x++)
            for (var y = -2; y < 2; y++)
            for (var z = -2; z < 2; z++)
            {
                var outer = (x == -2 || x == 1 ? 1 : 0) + (y == -2 || y == 1 ? 1 : 0) + (z == -2 || z == 1 ? 1 : 0);
                var h = DungeonWorld.Hash(x * 3 + z + 20 + variant * 31, y * 5 + z * 7 + 20);
                var face = z == 1 && y == 0;
                if (!face && ((outer == 3 && h < 0.7f) || (outer == 2 && h < 0.22f))) continue;
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
                if (touching && DungeonWorld.Hash(x * 11 + 40 + variant * 31, y * 13 + z * 17 + 40) < 0.1f) cells.Add(c);
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

        // ------------------------------------------------------------------ what happens to it

        // A mood for `seconds` over what it is doing; hurt, it flinches (what sticks out of it knocked in, the top
        // card flipped over). Dying is for good.
        public void SetMood(NoiseMood mood, float seconds)
        {
            if (dying >= 0f) return;
            if (mood == NoiseMood.Dying)
            {
                dying = 0f;
                return;
            }
            timed = mood;
            timedLeft = seconds;
            if (mood != NoiseMood.Hurt) return;
            Rig.Jolt();
            flip = 0f;
            flips++;
        }

        // Lit up all over by a blow (`amount` 0 to 1, white or the colour given), fading in an eighth of a second.
        public void Flash(float amount) => Flash(amount, Blank);

        public void Flash(float amount, Color color)
        {
            flash = Mathf.Max(flash, amount);
            flashColor = color;
        }

        // Its rows torn sideways by up to `jitter` metres, settling over `seconds`.
        public void Tear(float jitter, float seconds)
        {
            tear = jitter;
            tearLength = tearLeft = seconds;
        }

        // A third of its cubes knocked out of it at once (`amount` 0 to 1), going back over `seconds`.
        public void Loosen(float amount, float seconds)
        {
            loose = amount;
            looseLength = looseLeft = seconds;
        }

        // How far it is frozen in ice, 0 to 1 (Glitch.shader _Frost): the alert gone out of its skin, greyed toward
        // the ice's blue, its vertices still. On its own skin, which nothing else draws with; 0 lets it go.
        public float Frost
        {
            set => skin.SetFloat(FrostId, value);
        }

        // Its coloured cells taken into what struck it (ice, fire), fading over `seconds`.
        public void Tinge(Color color, float seconds)
        {
            tinge = color;
            tingeLength = tingeLeft = seconds;
        }

        // 0 whole, 1 burst: the 标题党型's cubes flung apart, tumbling and shrinking, onto the seabed; the
        // 转发型's core torn and gone; the eyes out from the first.
        public float Burst
        {
            set
            {
                burst = value;
                if (block != null) skin.SetFloat(BurstId, value);
            }
        }

        // The way its cubes fly when it bursts, after the blow that ended it (a direction in the world).
        public void BurstFrom(Vector3 direction, float strength)
        {
            if (block == null) return;
            var d = block.InverseTransformDirection(direction);
            skin.SetVector(BurstDirId, new Vector4(d.x, d.y, d.z, strength));
        }

        // Its loose parts flung off it as it bursts, out from it and along `direction` (in the world), put under
        // `keep` so they fly on once it is gone, and hidden when they land; never destroyed: the meshes they show
        // were made for this look, and Parts.Free finds them only under a renderer. The 标题党型's open pop-ups
        // tumble away and its drips fall; the 转发型's arrows shoot off the way they point; the 冷知识型's cards
        // scatter like paper, fluttering down.
        public void Debris(Transform keep, Vector3 direction)
        {
            if (scattered >= 0f) return;
            scattered = 0f;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized * 1.5f : Vector3.zero;
            for (var i = 0; i < ads.Length; i++)
            {
                if (ads[i].localScale.x < 0.1f) continue;
                var v = (ads[i].position - Body.position).normalized * 3f + Vector3.up * 2f + direction;
                var axis = Random.onUnitSphere;
                var window = windows[i];
                var start = window.localRotation;
                Fly(ads[i], keep, v, 6f, 0f, 0.6f, 0f, (s, t) => window.localRotation = Quaternion.AngleAxis(400f * s, axis) * start).Forget();
            }
            foreach (var drip in drips) Fly(drip, keep, Vector3.zero, 6f, 0f, 0.4f, 0.3f, null).Forget();
            if (core != null) Tear(0.5f, 0.45f);
            for (var i = 0; i < arrows.Length; i++)
            {
                var arrow = arrows[i];
                var v = Body.rotation * ArrowDirs[i].normalized * Random.Range(4f, 6f) + direction;
                var axis = Random.onUnitSphere;
                var start = arrow.rotation;
                Fly(arrow, keep, v, 6f, 0f, 0.6f, 0f, (s, t) => arrow.rotation = Quaternion.AngleAxis(720f * s, axis) * start).Forget();
            }
            for (var i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                var out_ = card.position - Body.position;
                out_.y = 0f;
                if (out_.sqrMagnitude < 0.0001f)
                {
                    var r = Random.insideUnitCircle;
                    out_ = new Vector3(r.x, 0f, r.y);
                }
                out_ = out_.normalized * Random.Range(2f, 3f);
                var v = new Vector3(out_.x + Random.Range(-1f, 1f), 1.2f, out_.z + Random.Range(-1f, 1f)) + direction;
                var spin = Random.Range(-360f, 360f);
                var start = card.rotation;
                Fly(card, keep, v, 4f, 2f, 0.9f, 0.6f, (s, t) =>
                    card.rotation = Quaternion.AngleAxis(spin * s, Vector3.up) * start * Quaternion.Euler(Mathf.Sin(s * 38f) * 25f, 0f, 0f)).Forget();
            }
        }

        // One part flying from where it is at `velocity`, falling at `gravity`, slowed by `drag` (per second), for
        // `seconds`, shrinking away from `shrink` of the way through, turned by `turn` (seconds and 0 to 1 in);
        // kept off the seabed, then hidden.
        static async Awaitable Fly(Transform part, Transform keep, Vector3 velocity, float gravity, float drag, float seconds, float shrink,
            System.Action<float, float> turn)
        {
            part.SetParent(keep, true);
            var from = part.position;
            var size = part.localScale;
            await Anim.Tween(seconds, t =>
            {
                if (part == null) return;
                var s = t * seconds;
                Vector3 at;
                if (drag > 0f)
                {
                    var k = (1f - Mathf.Exp(-drag * s)) / drag;
                    at = from + velocity * k + Vector3.down * (gravity / drag * (s - k));
                }
                else at = from + velocity * s + Vector3.down * (0.5f * gravity * s * s);
                at.y = Mathf.Max(at.y, 0.06f);
                part.position = at;
                turn?.Invoke(s, t);
                part.localScale = size * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shrink, 1f, t)));
            }, Ease.Linear, () => part == null);
            if (part != null) part.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ its attack (battle)

        // A part it throws (Throw): at `Local` on `Anchor` (its Body, which it rides along with as it leaves), turned
        // as `Rotation`, drawn at `Scale` (what BattleEffects draws its own window, card or arrow at to match it),
        // with the words a pop-up had on it.
        public readonly struct Thrown
        {
            public readonly Transform Anchor;
            public readonly Vector3 Local;
            public readonly Quaternion Rotation;
            public readonly float Scale;
            public readonly string Words;

            public Thrown(Transform anchor, Vector3 world, Quaternion rotation, float scale, string words = null)
            {
                Anchor = anchor;
                Local = anchor.InverseTransformPoint(world);
                Rotation = rotation;
                Scale = scale;
                Words = words;
            }

            public Vector3 Position => Anchor != null ? Anchor.TransformPoint(Local) : Vector3.zero;
        }

        // Its attack coming (BattleStage.Brace): what it throws drawn out ready, toward `toward` (a direction in the
        // world): the 标题党型's pop-ups all springing open at once, every one crying 「震惊！」, leaning that way,
        // their round held; the 转发型's arrows drawn out long and spinning fast, its copies coming off twice as fast;
        // the 冷知识型's cards fanned like a hand, the front one tipped at them, its figures all 「你知道吗？」 and three
        // times as fast. Unwind lets it go back to what it was doing.
        public void Windup(Vector3 toward)
        {
            winding = true;
            windTime = 0f;
            var local = Body.parent.InverseTransformDirection(toward);
            local.y = 0f;
            windToward = local.sqrMagnitude > 0.0001f ? local.normalized : Vector3.forward;
            foreach (var words in adWords) words.text = "震惊！";
        }

        public void Unwind() => winding = false;

        // Part `i` of its attack leaving it, from where it is now: gone from it (the 标题党型's last, out of its mouth,
        // takes nothing with it), growing back a little under a second later.
        public Thrown Throw(int i)
        {
            if (block != null)
            {
                if (i >= ads.Length) return new Thrown(Body, Form.TransformPoint(new Vector3(0f, -0.15f, faceZ)), Quaternion.LookRotation(-Form.forward, Form.up), 0.4f, "震惊！");
                adGone[i] = 0f;
                adCycle[i] = int.MinValue; // somewhere new when it comes back
                return new Thrown(Body, windows[i].position, windows[i].rotation, ads[i].lossyScale.x, adWords[i].text);
            }
            if (core != null)
            {
                var arrow = arrows[i % 3];
                arrowGone[i % 3] = 0f;
                return new Thrown(Body, arrow.TransformPoint(new Vector3(0f, 0.95f, 0f)), arrow.rotation, arrow.lossyScale.x);
            }
            var card = cards[1 + i % (cards.Length - 1)];
            cardGone[1 + i % (cards.Length - 1)] = 0f;
            // Its face (+z) where the effect's card has its words (-z); a 0.66 m card drawn as big as its 1.2 m one.
            return new Thrown(Body, card.position, card.rotation * Quaternion.Euler(0f, 180f, 0f), card.lossyScale.x * 1.2f / 0.66f);
        }

        // On the front of its face, small: where an attack that throws nothing of its own leaves it.
        public Thrown Face() => new Thrown(Body, Form.TransformPoint(new Vector3(0f, -0.1f, faceZ)), Quaternion.LookRotation(-Form.forward, Form.up), 0.4f);

        // What it has thrown grown back at once (knocked down, deleted): it would lie or burst without it.
        public void Restore()
        {
            foreach (var gone in new[] { adGone, arrowGone, cardGone })
                for (var i = 0; i < gone.Length; i++)
                    if (gone[i] >= 0f) gone[i] = Mathf.Max(gone[i], ThrownFor);
        }

        // ------------------------------------------------------------------ coming together (battle)

        // Nothing of it there yet: on the battle field from the frame it is made till Assemble brings it in.
        public void Unform()
        {
            formed = 0f;
            formWait = -1f;
            if (block != null) skin.SetFloat(BurstId, 1f);
        }

        // Coming together out of the pixels after `delay` seconds of its clock, in FormSeconds: the 标题党型's cubes
        // rising off the seabed and flying in (its burst run backwards), the 转发型's core swelling up and its arrows
        // shooting in from round it, the 冷知识型's cards dealt in from either side, the one with the eyes last. Its
        // eyes, pop-ups, copies and figures are there once it is whole.
        public void Assemble(float delay) => formWait = Mathf.Max(0f, delay);

        public bool Whole => formed >= 1f;
        public float Formed => formed;

        static float[] Gone(int count)
        {
            var gone = new float[count];
            for (var i = 0; i < count; i++) gone[i] = -1f;
            return gone;
        }

        // How much of a thrown part shows: none till it grows back, springing out to whole.
        static float Shown(float gone) => gone < 0f ? 1f : gone < ThrownFor ? 0f : OutBack(Mathf.Clamp01((gone - ThrownFor) / Regrown));

        static void Regrow(float[] gone, float dt)
        {
            for (var i = 0; i < gone.Length; i++)
                if (gone[i] >= 0f && (gone[i] += dt) >= ThrownFor + Regrown) gone[i] = -1f;
        }

        // ------------------------------------------------------------------ its life

        // A frame of it, `dt` seconds on (0 holds it, frozen in ice): bobbing, swaying and breathing, the rig, the
        // eyes for its mood, what its kind does. heat 0 to 1 reddens its glitch (alerted, struck, attacking);
        // down 0 to 1 is how far it lies knocked down.
        public void Animate(float dt, float heat, float down = 0f)
        {
            // A hit-stop holds its own clock; lying down it runs slow, the signal weak.
            var held = Rig.Freeze > 0f;
            Rig.Breath = dying >= 0f ? 0f : 0.03f * Mathf.Sin(Clock * 6.9f);
            Rig.Step(dt);
            var step = held ? 0f : dt * Pace * Mathf.Lerp(1f, 0.35f, down);
            Clock += step;
            timedLeft -= dt;
            if (dying >= 0f) dying += dt;
            if (scattered >= 0f) scattered += dt;
            wound = Mathf.Lerp(wound, winding ? 1f : 0f, 1f - Mathf.Exp(-16f * dt));
            windTime += dt;
            Regrow(adGone, dt);
            Regrow(arrowGone, dt);
            Regrow(cardGone, dt);
            if (formed < 1f)
            {
                if (formWait >= 0f && (formWait -= dt) < 0f)
                {
                    formed = Mathf.Min(1f, formed - formWait / FormSeconds);
                    formWait = 0f;
                }
                if (block != null) skin.SetFloat(BurstId, 1f - Ease.OutCubic(formed));
            }
            Mood = dying >= 0f ? NoiseMood.Dying : timedLeft > 0f ? timed : BaseMood;
            var cowering = BaseMood == NoiseMood.Cower && dying < 0f;
            if (cowering && cower <= 0f) begTime = 0f; // each hold-up begins at the first beg
            cower = Mathf.MoveTowards(cower, cowering ? 1f : 0f, dt / 0.2f);
            if (cower > 0f && !held) begTime += dt * Pace;
            var time = Clock;
            var still = dying >= 0f ? 0f : 1f - down; // its bob and sway, gone while it lies or goes
            var tremble = Mathf.Max(Rig.Tremble, Mood == NoiseMood.Angry ? 0.03f : 0f);
            lean = Mathf.MoveTowards(lean, Mood == NoiseMood.Asleep ? 12f : 0f, dt * 30f);
            // Cowering, it shivers where it lies, quicker than an angry body's shake (about 14 a second).
            var shiver = 0.015f * cower * Mathf.Sin(begTime * 90f);
            Body.localPosition = new Vector3(tremble * Mathf.Sin(time * 70f) + shiver, Height + Mathf.Sin(time * 2.1f + seed) * 0.12f * still, 0f) + Rig.Offset + Shift;
            Body.localRotation = Rig.Tilt * Quaternion.Euler(0f, Mathf.Sin(time * 0.9f + seed) * 8f * still, lean);
            Form.localScale = Vector3.Scale(restScale, Rig.FormScale);

            flash = Mathf.MoveTowards(flash, 0f, dt / 0.12f);
            tearLeft = Mathf.Max(0f, tearLeft - dt);
            looseLeft = Mathf.Max(0f, looseLeft - dt);
            tingeLeft = Mathf.Max(0f, tingeLeft - dt);
            skin.SetFloat(ClockId, time);
            skin.SetFloat(AlertId, heat);
            skin.SetFloat(DimId, Mathf.Lerp(1f, 0.7f, down));
            skin.SetFloat(CalmId, GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash ? 1f : 0f);
            skin.SetFloat(JitterId, jitter + tear * tearLeft / tearLength);
            skin.SetFloat(LooseId, loose * looseLeft / looseLength);
            skin.SetColor(HitId, new Color(flashColor.r, flashColor.g, flashColor.b, flash));
            // Held through the flash, then given back cell by cell (Glitch.shader), never a muddy mix of the two.
            skin.SetColor(TintId, new Color(tinge.r, tinge.g, tinge.b, Mathf.Clamp01(tingeLeft / tingeLength / 0.55f)));

            Eyes(dt, time);
            Stars(dt, Mood == NoiseMood.Dizzy && burst <= 0f);
            var z = Mathf.Repeat(time * 0.55f + seed, 1f);
            snore.transform.localPosition = new Vector3(0.45f + z * 0.25f, 0.7f + z * 0.6f, 0f);
            snore.color = new Color(0.7f, 0.9f, 1f, Mood == NoiseMood.Asleep ? Mathf.Sin(z * Mathf.PI) * 0.9f : 0f);

            if (core != null) AnimateArrows(step, heat, down);
            else if (cards.Length > 0) AnimateCards(dt, step, heat, down);
            else AnimateBlock(step, heat, down);
        }

        // The eyes for its mood, lit by it (in a chase or an attack hot enough to glow red in the lens's bloom),
        // blinking now and then while it is at ease.
        void Eyes(float dt, float time)
        {
            var glow = Color.white;
            float wantOpen = 1f, wantWide = 1f, wantSlant = 0f, wantSocket = Socket, roll = 0f;
            var blinks = false;
            switch (Mood)
            {
                case NoiseMood.Wary:
                    glow = new Color(1.15f, 0.95f, 0.35f);
                    wantOpen = 1.6f;
                    wantWide = 0.85f;
                    blinks = true;
                    break;
                case NoiseMood.Angry:
                    // White-hot in a dark socket, red only in the glow round it: its alerted skin is red as well.
                    glow = new Color(2.2f, 0.85f, 0.7f);
                    wantOpen = 0.7f;
                    wantSlant = 24f;
                    wantSocket = 0.035f;
                    break;
                case NoiseMood.Hurt:
                case NoiseMood.Cower:
                    glow = new Color(1.2f, 1.2f, 1.2f);
                    wantOpen = 0.25f;
                    wantWide = 1.2f;
                    wantSlant = -15f;
                    break;
                case NoiseMood.Dizzy:
                    wantOpen = 0.35f;
                    roll = spinTurn * 1.1f;
                    break;
                case NoiseMood.Asleep:
                    glow = new Color(0.5f, 0.55f, 0.65f);
                    wantOpen = 0.18f;
                    break;
                case NoiseMood.Dying:
                    // A last flicker, then out.
                    glow = dying < 0.2f && Mathf.Repeat(dying * 12f, 1f) < 0.5f ? Color.white : Color.black;
                    break;
                default:
                    blinks = true;
                    break;
            }
            var k = 1f - Mathf.Exp(-30f * dt);
            open = Mathf.Lerp(open, wantOpen, k);
            wide = Mathf.Lerp(wide, wantWide, k);
            slant = Mathf.Lerp(slant, wantSlant, k);
            socket = Mathf.Lerp(socket, wantSocket, k);
            var blink = blinks && Mathf.Repeat(time * 0.31f + seed * 0.37f, 1f) < 0.035f;
            if (slits != null) Face(dt, k, blink, roll);
            else
            {
                var shown = blink ? 0.12f : open;
                if (burst > 0f) shown = 0f;
                eyeL.localScale = eyeR.localScale = new Vector3(wide, shown, 1f);
                eyeL.localRotation = Quaternion.Euler(0f, 0f, roll - slant);
                eyeR.localRotation = Quaternion.Euler(0f, 0f, roll + slant);
            }
            eyes.SetColor(EmissionId, glow);
            eyes.SetFloat(RimId, socket);
        }

        // The battle face for its mood. The lids shape the slits: a slit at ease, round when wary or picked out,
        // shut in a blink, a thin line asleep; angry, the slit tips its inner end down and the lower lid cuts it
        // flat, a wedge sloping to the middle. A blow or cowering crosses them out with 「> <」, the points toward
        // the middle; knocked down they are a spinning X. The 标题党型's mouth springs open to shout while it is
        // angry, gasps at a blow, gapes as it goes.
        void Face(float dt, float k, bool blink, float roll)
        {
            float up = 0.5f, down = -0.5f, turn = 0f, slit = 1f, mouthWant = 0f; // lid edges in half an eye's height
            var marks = 0; // 1 「> <」, 2 X
            switch (Mood)
            {
                case NoiseMood.Wary:
                    up = 1.05f;
                    down = -1.05f;
                    break;
                case NoiseMood.Angry:
                    up = 2.5f;
                    down = -0.35f;
                    turn = 20f;
                    mouthWant = 1f;
                    break;
                case NoiseMood.Hurt:
                case NoiseMood.Cower:
                    up = 1.05f;
                    down = -1.05f;
                    slit = 0f;
                    marks = 1;
                    mouthWant = Mood == NoiseMood.Hurt ? 0.6f : 0.35f;
                    break;
                case NoiseMood.Dizzy:
                    up = 1.05f;
                    down = -1.05f;
                    slit = 0f;
                    marks = 2;
                    break;
                case NoiseMood.Asleep:
                    up = 1.05f;
                    down = -1.05f;
                    slit = 0.2f;
                    break;
                case NoiseMood.Dying:
                    mouthWant = 1f;
                    break;
            }
            if (blink) up = down = 0f;
            lidUp = Mathf.Lerp(lidUp, Mathf.Min(up * eyeHalf, socketEdge), k);
            lidDown = Mathf.Lerp(lidDown, Mathf.Max(down * eyeHalf, -socketEdge), k);
            slitTurn = Mathf.Lerp(slitTurn, turn, k);
            var gone = burst > 0f || formed < 1f;
            eyeL.localScale = eyeR.localScale = gone ? Vector3.zero : Vector3.one;
            eyeL.localRotation = eyeR.localRotation = Quaternion.Euler(0f, 0f, roll);
            var rise = markLength * Mathf.Sin(MarkTilt * Mathf.Deg2Rad) / 2f;
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1f : 1f; // the left eye is on −x; the middle of the face is the other way
                var shape = slits[i].transform;
                slits[i].enabled = slit > 0f; // flattened it would still show its outline
                shape.localScale = new Vector3(wide, slit, 1f);
                shape.localRotation = Quaternion.Euler(0f, 0f, side * slitTurn); // its inner end down
                // Each lid from its edge to the socket's.
                var at = lidsUp[i].localPosition;
                lidsUp[i].localPosition = new Vector3(at.x, lidUp, at.z);
                lidsUp[i].localScale = new Vector3(1f, Mathf.Max(0.001f, socketEdge - lidUp), 1f);
                lidsDown[i].localPosition = new Vector3(at.x, lidDown, at.z);
                lidsDown[i].localScale = new Vector3(1f, Mathf.Max(0.001f, socketEdge + lidDown), 1f);
                var size = marks == 0 ? Vector3.zero : Vector3.one * (marks == 2 ? 1.3f : 1f);
                marksA[i].localScale = marksB[i].localScale = size;
                if (marks == 0) continue;
                // 「>」 meets at its point on the inner side, centred on the eye; X crosses on the centre.
                var tilt = marks == 1 ? MarkTilt : 45f;
                var lift = marks == 1 ? rise : 0f;
                marksA[i].localPosition = new Vector3(0f, lift, 0f);
                marksA[i].localRotation = Quaternion.Euler(0f, 0f, side * tilt);
                marksB[i].localPosition = new Vector3(0f, -lift, 0f);
                marksB[i].localRotation = Quaternion.Euler(0f, 0f, -side * tilt);
            }
            if (mouth == null) return;
            if (mouthWant != mouthTo)
            {
                mouthFrom = mouthOpen;
                mouthTo = mouthWant;
                mouthTime = 0f;
            }
            mouthTime += dt;
            mouthOpen = Mathf.LerpUnclamped(mouthFrom, mouthTo, OutBack(Mathf.Clamp01(mouthTime / 0.12f)));
            mouth.localScale = new Vector3(1f, gone ? 0f : Mathf.Max(0f, mouthOpen), 1f);
            // Its shout only while the mouth is wide enough to read it.
            var alpha = Mathf.Clamp01((mouthOpen - 0.7f) / 0.3f);
            if (shout.color.a != alpha) shout.color = new Color(ShoutColor.r, ShoutColor.g, ShoutColor.b, alpha);
        }

        // Knocked down: a buffering spinner going round over it, the lead cube biggest; however it lies, tipped a
        // little toward the way it faces (the party, and the camera with them) so it reads as a ring from low down.
        void Stars(float dt, bool on)
        {
            if (!on)
            {
                if (spinner != null && spinner.gameObject.activeSelf) spinner.gameObject.SetActive(false);
                return;
            }
            if (spinner == null)
            {
                spinner = Parts.Node("Stars", Body);
                var cube = SeaArt.Box(Vector3.one * 0.1f);
                for (var k = 0; k < 8; k++)
                {
                    var a = -k * 45f * Mathf.Deg2Rad;
                    var star = Parts.Shape("Star", spinner, cube, eyes, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.36f).transform;
                    star.localScale = Vector3.one * (1f - 0.7f * k / 7f);
                }
            }
            if (!spinner.gameObject.activeSelf) spinner.gameObject.SetActive(true);
            spinTurn += dt * 330f;
            var facing = Body.parent.forward;
            facing.y = 0f;
            var tip = Quaternion.FromToRotation(Vector3.up, Vector3.up + facing.normalized * 0.7f);
            spinner.SetPositionAndRotation(Crown, tip * Quaternion.Euler(0f, spinTurn, 0f));
        }

        // The cards swaying in the stack, one now and then dealt out to the side and slid back, and the figures
        // floating up off the stack one after another (faster when alerted), each a new one. A blow fans the
        // stack and flips the front card over the top; knocked down, the cards spill out of the stack and the
        // figures stop.
        void AnimateCards(float dt, float step, float heat, float down)
        {
            var pace = (1f + heat) * (1f + 2f * wound);
            cardTime += step * pace * (1f - down);
            flip = Mathf.Min(1f, flip + dt / 0.2f);
            var time = Clock;
            if (scattered < 0f)
                for (var i = 0; i < cards.Length; i++)
                {
                    var beat = Mathf.Repeat(cardTime * 0.45f + i * 0.27f + seed * 0.1f, 1f);
                    var deal = beat < 0.22f ? Mathf.Sin(beat / 0.22f * Mathf.PI) : 0f;
                    var side = i % 2 == 0 ? 1f : -1f;
                    var at = new Vector3(deal * 0.7f * side + Mathf.Sin(time * 1.3f + i) * 0.03f, (i - 1.5f) * 0.04f + deal * 0.18f, -0.12f * i);
                    var turn = Quaternion.Euler(0f, deal * -25f * side,
                        Mathf.Sin(time * 0.8f + i * 1.7f + seed) * (4f + heat * 8f) + deal * 12f * side + (i - 1.5f) * 8f * Rig.Fold);
                    if (i == 0)
                    {
                        // Lifted clear of the stack and over, top first away from the blow.
                        var u = Ease.InOutCubic(flip);
                        at.y += Mathf.Sin(u * Mathf.PI) * 0.5f;
                        turn *= Quaternion.Euler(-180f * (flips - 1 + u), 0f, 0f);
                    }
                    var fan = i - 1.5f;
                    // Wound up: fanned out like a hand, the front one tipped forward at whoever it goes for.
                    at = Vector3.Lerp(at, new Vector3(0.07f * fan, 0.05f - 0.02f * Mathf.Abs(fan), -0.12f * i), wound);
                    turn = Quaternion.Slerp(turn, Quaternion.Euler(i == 0 ? 20f : 0f, 0f, fan * 14f), wound);
                    // Lying, the stack spills sideways along its top, which then runs along the seabed.
                    // Coming together: dealt in from its side, the back one first and the one with the eyes last.
                    var dealt = 1f;
                    if (formed < 1f)
                    {
                        dealt = Mathf.Clamp01((formed * FormSeconds - 0.06f * (cards.Length - 1 - i)) / 0.25f);
                        var slide = 1f - Ease.OutCubic(dealt);
                        at += new Vector3(side * 1.4f * slide, 0.25f * slide, 0f);
                        turn *= Quaternion.Euler(0f, 0f, side * 80f * slide);
                    }
                    cards[i].localPosition = Vector3.Lerp(at, new Vector3(0.05f * fan, 0.2f * fan, -0.12f * i), down);
                    cards[i].localRotation = Quaternion.Slerp(turn, Quaternion.Euler(0f, 0f, fan * 14f), down);
                    cards[i].localScale = Vector3.one * (dealt > 0f ? Shown(cardGone[i]) : 0f);
                }
            // Wound up, every figure it quotes is the question; back to its figures once it has let go.
            if (asking != (wound > 0.5f))
            {
                asking = !asking;
                for (var i = 0; i < factCycle.Length; i++) factCycle[i] = int.MinValue;
            }
            for (var i = 0; i < facts.Length; i++)
            {
                var clock = cardTime / FactSeconds + (float)i / facts.Length + seed * 0.13f;
                var t = Mathf.Repeat(clock, 1f);
                var round = Mathf.FloorToInt(clock);
                if (round != factCycle[i])
                {
                    factCycle[i] = round;
                    facts[i].text = asking ? "你知道吗？" : Facts[(int)(DungeonWorld.Hash(round, i + 17) * Facts.Length) % Facts.Length];
                }
                // Up one side or the other, clear of the face.
                var a = (50f + 260f * DungeonWorld.Hash(round, i * 7 + 5)) * Mathf.Deg2Rad;
                facts[i].transform.localPosition = new Vector3(Mathf.Sin(a) * 0.75f, -0.1f + t * 1.1f, Mathf.Cos(a) * 0.45f);
                var c = facts[i].color;
                c.a = formed < 1f ? 0f : Mathf.Sin(t * Mathf.PI) * 0.95f * (1f - down) * (1f - Mathf.Clamp01(burst * 4f));
                facts[i].color = c;
            }
        }

        // The arrows turning and, one after another, shooting out a little as they pass something on; the
        // copies drifting off to the sides and back, growing and fading. A blow knocks the arrows in; knocked
        // down they droop and stop; as it bursts the copies flare and fade and the core tears away.
        void AnimateArrows(float step, float heat, float down)
        {
            // Wound up: its copies coming off twice as fast, the arrows spinning three times as fast, drawn out long.
            var pace = 1f + heat;
            arrowTime += step * pace * (1f + wound);
            arrowTurn += step * pace * 40f * (1f - down) * (1f + 2f * wound);
            if (scattered < 0f)
                for (var i = 0; i < arrows.Length; i++)
                {
                    var beat = Mathf.Repeat(arrowTime * 0.7f + i * 0.37f + seed, 1f);
                    var push = beat < 0.2f ? Mathf.Sin(beat / 0.2f * Mathf.PI) : 0f;
                    // Coming together, shooting in from a metre and a half out.
                    var shown = formed > 0f ? Shown(arrowGone[i]) : 0f;
                    arrows[i].localPosition = arrowRest[i] * Vector3.up * (1.5f * (1f - Ease.OutCubic(formed)));
                    arrows[i].localRotation = arrowRest[i] * Quaternion.Euler(0f, arrowTurn, 0f);
                    arrows[i].localScale = new Vector3(shown, shown * Mathf.Lerp(1f + push * 0.3f, 0.7f, down) * (1f - 0.45f * Rig.Fold) * (1f + 0.45f * wound), shown);
                }
            if (formed < 1f) core.localScale = Vector3.one * OutBack(Mathf.Clamp01(formed / 0.66f)); // swelling up, 0.3 s
            if (burst > 0f)
            {
                // Swells a little, then goes.
                const float c = 1.70158f;
                var u = Mathf.Clamp01(burst * 1.25f);
                core.localScale = Vector3.one * Mathf.Max(0f, 1f - u * u * ((c + 1f) * u - c));
            }
            for (var i = 0; i < ghosts.Length; i++)
            {
                var tint = Color.Lerp(Palette.Cold, Palette.Red, Mood == NoiseMood.Angry ? 0.6f : 0f);
                if (scattered >= 0f)
                {
                    var u = Mathf.Clamp01(scattered / 0.3f);
                    ghosts[i].localScale = Vector3.one * (1.1f + 0.8f * u);
                    tint.a = scattered < 0.3f ? Mathf.Lerp(0.35f, 0.6f, u) : 0.6f * Mathf.Clamp01(1f - (scattered - 0.3f) / 0.25f);
                    ghostLooks[i].SetColor("_Color", tint);
                    continue;
                }
                var clock = arrowTime / GhostSeconds + (float)i / ghosts.Length + seed * 0.1f;
                var t = Mathf.Repeat(clock, 1f);
                var a = (DungeonWorld.Hash(Mathf.FloorToInt(clock), i + 7) - 0.5f) * 2.4f + (i - 1) * 1.9f;
                var drift = new Vector3(Mathf.Sin(a), 0.35f * Mathf.Cos(a * 1.7f), -0.3f) * (t * 1.9f);
                ghosts[i].localPosition = Body.localPosition + Body.localRotation * drift;
                ghosts[i].localRotation = Body.localRotation;
                ghosts[i].localScale = Vector3.one * (1f + t * 0.25f);
                tint.a = formed < 1f ? 0f : 0.35f * Mathf.SmoothStep(0f, 1f, t / 0.1f) * Mathf.Pow(1f - t, 1.5f) * (1f - down);
                ghostLooks[i].SetColor("_Color", tint);
            }
        }

        // Pop-ups springing open round the block one after another (faster when alerted), each somewhere new
        // with a new come-on, and pixels dripping off underneath. Knocked down, the pop-ups shrink and droop where
        // they are.
        void AnimateBlock(float step, float heat, float down)
        {
            if (scattered >= 0f) return;
            // Wound up, the round holds and every pop-up springs open at once, one a moment after another, leaning
            // toward whoever it goes for.
            adTime += step * (1f + heat * 0.8f) * (1f - down) * (1f - wound);
            for (var i = 0; i < ads.Length; i++)
            {
                var clock = adTime / AdSeconds + (float)i / ads.Length + seed * 0.13f;
                var t = Mathf.Repeat(clock, 1f);
                var round = Mathf.FloorToInt(clock);
                if (round != adCycle[i])
                {
                    adCycle[i] = round;
                    // Round the sides and the back, clear of the face.
                    var a = (40f + 280f * Mathf.Repeat(DungeonWorld.Hash(round, i * 5 + 3) * 0.6f + (float)i / ads.Length, 1f)) * Mathf.Deg2Rad;
                    adHome[i] = new Vector3(Mathf.Sin(a) * 1.1f, -0.15f + DungeonWorld.Hash(i * 5 + 3, round) * 0.7f, Mathf.Cos(a) * 0.75f);
                    adWords[i].text = Ads[(int)(DungeonWorld.Hash(round, i + 11) * Ads.Length) % Ads.Length];
                }
                float size;
                if (t < 0.08f) size = OutBack(t / 0.08f);
                else if (t < 0.7f) size = 1f;
                else if (t < 0.76f) size = 1f - (t - 0.7f) / 0.06f;
                else size = 0f;
                size = Mathf.Lerp(size, winding ? OutBack(Mathf.Clamp01((windTime - 0.04f * i) / 0.1f)) : 1f, wound);
                ads[i].localPosition = adHome[i] + windToward * (0.15f * wound);
                ads[i].localScale = formed < 1f ? Vector3.zero : Vector3.one * (Mathf.Lerp(size, 0.45f, down) * Shown(adGone[i]));
                windows[i].localRotation = Quaternion.Euler(0f, 0f, (i - 1) * 9f + 25f * down);
            }
            for (var i = 0; i < drips.Length; i++)
            {
                var clock = Clock * 0.5f + (float)i / drips.Length + seed * 0.21f;
                var t = Mathf.Repeat(clock, 1f);
                var round = Mathf.Floor(clock);
                var x = (DungeonWorld.Hash((int)round, i + 30) - 0.5f) * 0.8f;
                var z = (DungeonWorld.Hash(i + 30, (int)round) - 0.5f) * 0.8f;
                drips[i].localPosition = new Vector3(x, -0.55f - t * t * 0.6f, z);
                drips[i].localScale = Vector3.one * (formed < 1f ? 0f : 1f - t);
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
