using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The battle's 3D field (design/systems.md 战斗) behind the 2D battle UI: a round of cache-sea floor ringed
    // by sunken racks, the party on the near side with their backs to the camera, the noise bodies across from
    // them. The camera works after Persona 5's (see the camera section): it opens on the noise bodies from down at
    // their feet, stands low behind whoever is choosing, closes in behind them to pick a target, goes down to
    // the floor for a strike, gets behind the member a noise body comes at, circles the noise bodies when all are
    // down and rises behind the party when they win. The party stand in a fighting
    // stance and act with the battle's motions (Resources/Models/Motions), the fighting lit by BattleEffects.
    // BattleScreen pins the name plates, damage numbers and 2D effects to the screen points this gives for
    // each member and noise body.
    public sealed class BattleStage : MonoBehaviour
    {
        static readonly Vector3 Centre = new Vector3(0f, 0f, 3.4f); // between the lines; the camera swings round it
        // The motions' pace and the moment in each (seconds at that pace) that lands: the slash's blade passing
        // in front of her, the cast's arm thrown out.
        const float SlashPace = 1.5f, SlashLands = 0.85f;
        const float CastPace = 1.6f, CastLands = 1.3f;
        const float ShootSeconds = 1.1f; // the phone up, the shutter and down again
        // The poses struck in place (SeaFigure.Act): how long after the start each has been reached and seen,
        // when the command goes on, the 道具's rice ball used up at that moment; the way back follows by itself.
        const float GuardSeen = 0.75f, ItemUsed = 0.75f, ThinkSeen = 0.6f, FaceRound = 60f;
        static readonly Color Sea = new Color(0.4f, 0.85f, 1f), Frozen = new Color(0.55f, 0.9f, 1f);
        static readonly Color Lens = new Color(0.55f, 1f, 0.6f), Harm = new Color(1f, 0.3f, 0.35f), Heal = new Color(0.6f, 1f, 0.55f);

        public Camera Camera { get; private set; }
        readonly List<Member> party = new List<Member>();
        readonly List<Foe> foes = new List<Foe>();
        Shot from, to;
        System.Func<float, float> shotEase;
        float shotTime, shotLength, spun;
        Vector3 standing, look;  // where the shot puts the camera, before its drift and shake
        float roll, fov, drift;
        float shake, shakeLeft, punch, punchLeft, punchLength;
        float time;
        BattleEffects effects;

        // partyIds in BattleScreen's order (GameState.PlayerId first); foeKinds the noise bodies' kinds.
        public static BattleStage Create(string playerGender, IList<string> partyIds, IList<string> foeKinds)
        {
            var go = new GameObject("Battle Field");
            var stage = go.AddComponent<BattleStage>();
            stage.Build(playerGender, partyIds, foeKinds);
            return stage;
        }

        void OnDestroy()
        {
            foreach (var m in party) m.Figure.Dispose();
        }

        void Build(string playerGender, IList<string> partyIds, IList<string> foeKinds)
        {
            var camGo = new GameObject("Battle Camera");
            camGo.transform.SetParent(transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = DungeonWorld.FogColor;
            Camera.fieldOfView = 46f;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 600f;
            Camera.depth = 10f;
            effects = new BattleEffects(transform, Camera);

            Parts.Shape("Seabed", transform, SeaArt.Plane(220f), SeaArt.Plain("Seabed"), Centre);
            var sky = Parts.Shape("Sky", transform, SeaArt.Sphere(1f, 0f, 32, 20), SeaArt.Plain("Sky"), Centre).transform;
            sky.localScale = Vector3.one * 420f;
            BuildRacks();
            var motes = Parts.Node("Motes", transform, Centre);
            SeaArt.Particles(motes, "Motes", SeaArt.AtlasDot, SeaArt.AtlasDot, 160, new Vector3(36f, 14f, 36f), 9f, 0.08f, 0.22f, new Color(0.5f, 0.85f, 1f, 0.9f), 0.25f);
            SeaArt.Particles(motes, "Debris", SeaArt.AtlasBubble, SeaArt.AtlasCard, 50, new Vector3(70f, 20f, 70f), 20f, 0.7f, 2.4f, new Color(0.45f, 0.75f, 1f, 0.55f), 0.12f, 12f);

            // The party in a line across, the player on the left; the noise bodies facing them, the middle one
            // further back so all three show between the party's shoulders.
            for (var i = 0; i < partyIds.Count; i++)
            {
                var home = new Vector3((i - (partyIds.Count - 1) / 2f) * 2f - 0.3f, 0f, i * -0.2f);
                var root = Parts.Node(partyIds[i], transform, home);
                var figure = partyIds[i] == "DeepSeek" ? SeaFigure.DeepSeek(true, root) : SeaFigure.Player(playerGender, false, root);
                figure.Stance();
                party.Add(new Member { Root = root, Figure = figure, Home = home, At = home, Shadow = SeaArt.Shadow(transform, 1f).transform });
            }
            for (var i = 0; i < foeKinds.Count; i++)
            {
                var n = foeKinds.Count;
                var x = n == 1 ? 0f : n == 2 ? (i * 2 - 1) * 1.7f : (i - 1) * 2.8f;
                var home = new Vector3(x, 0f, n == 3 && i == 1 ? 7.6f : 6.4f);
                var root = Parts.Node("Noise " + i, transform, home);
                root.rotation = Quaternion.Euler(0f, 180f, 0f);
                foes.Add(new Foe { Root = root, Look = new NoiseLook(foeKinds[i], root), Home = home, At = home, Seed = i * 2.3f,
                    Shadow = SeaArt.Shadow(transform, NoiseLook.ShadowSize).transform });
            }

            Cut(Reveal(0f));
        }

        // A ring of rack stacks beyond the field, lower in front of the camera's usual side, lost in the fog.
        void BuildRacks()
        {
            const float cell = 4f;
            var blocks = new Dictionary<Vector2Int, float>();
            for (var y = -9; y <= 9; y++)
            for (var x = -9; x <= 9; x++)
            {
                var d = new Vector2(x, y).magnitude;
                var h = DungeonWorld.Hash(x + 40, y + 40);
                if (d < 4.2f || d > 9.5f || h < 0.3f + (d - 4.2f) * 0.05f) continue;
                blocks[new Vector2Int(x, y)] = 4f + DungeonWorld.Hash(y + 40, x + 40) * (5f + (d - 4.2f) * 2.2f);
            }
            var walls = SeaArt.Walls(blocks, c => Centre + new Vector3(c.x * cell, 0f, c.y * cell), cell, c => new Color(0.25f, 0.95f, 0.85f, 0.6f));
            Parts.Shape("Racks", transform, walls, SeaArt.Plain("Rack"));
        }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            time += dt;
            DungeonWorld.ApplySea(time, Vector3.zero, 9f);
            foreach (var m in party)
            {
                var moved = dt > 0f ? Vector3.Distance(m.Root.localPosition, m.At) / dt : 0f;
                m.Speed = Mathf.Lerp(m.Speed, moved, 1f - Mathf.Exp(-14f * dt));
                m.Root.localPosition = m.At;
                m.Root.localRotation = Quaternion.Euler(m.Fall * -78f, m.Yaw, 0f);
                m.Wince -= dt;
                m.Figure.SetFace(m.Fall > 0.5f ? "down" : m.Wince > 0f ? "hurt" : m.Mood);
                m.Figure.Animate(dt, m.Fall > 0f ? 0f : m.Speed);
                m.Shadow.localPosition = new Vector3(m.At.x, 0.02f, m.At.z);
            }
            foreach (var f in foes)
            {
                if (f.Gone) continue;
                f.Heat = Mathf.MoveTowards(f.Heat, 0f, dt * 2.5f);
                f.Look.Animate(time, f.Seed, Mathf.Max(f.Heat, f.Down * 0.4f));
                // Knocked down: over onto its side, sunk into the seabed a little.
                f.Root.localPosition = f.At + new Vector3(0f, -0.55f * f.Down, 0f);
                f.Root.localRotation = Quaternion.Euler(0f, 180f, f.Down * 70f);
                f.Shadow.localPosition = new Vector3(f.At.x, 0.02f, f.At.z);
                f.Shadow.localScale = new Vector3(1f, 0f, 1f) * (NoiseLook.ShadowSize * f.Root.localScale.x) + Vector3.up;
            }
            UpdateCamera(dt);
            // Held by hand: never quite still, more so as it shakes; a punch narrows the view at once and lets it
            // back out.
            var sway = new Vector3(Mathf.Sin(time * 0.71f) + 0.5f * Mathf.Sin(time * 1.37f), 0.6f * Mathf.Sin(time * 0.93f + 1f), 0.5f * Mathf.Sin(time * 0.57f + 2f)) * drift;
            var aim = new Vector3(Mathf.Sin(time * 0.61f + 3f), 0.5f * Mathf.Sin(time * 0.83f), 0f) * drift * 0.5f;
            shakeLeft -= dt;
            punchLeft -= dt;
            var jolt = shakeLeft > 0f ? Random.insideUnitSphere * shake * Mathf.Min(1f, shakeLeft * 4f) : Vector3.zero;
            var kick = punchLeft > 0f ? punch * Mathf.Pow(punchLeft / punchLength, 2f) : 0f;
            Camera.transform.position = standing + sway + jolt;
            Camera.transform.rotation = Quaternion.LookRotation(look + aim - standing) * Quaternion.Euler(0f, 0f, roll);
            Camera.fieldOfView = fov - kick;
        }

        // ------------------------------------------------------------------ camera

        // After Persona 5's battles: low, so the noise bodies loom over the party; tipped off the level (Roll)
        // when something happens, a long lens to pick one thing out and a wide one down on the floor for a
        // strike (Fov); quick to get where it is going and slow to settle; held by hand once there (Drift), and
        // turning slowly round what it looks at while there is time to look (Spin, degrees a second).

        // The noise bodies from down at their feet, tipped, the camera pushing in (push 0 to 1) as the battle opens.
        Shot Reveal(float push) => new Shot(FoeCentre + new Vector3(1.7f - push * 0.5f, -0.8f, -3.4f + push * 0.9f), FoeCentre + Vector3.up * 0.3f, 11f, 40f);

        // Behind and right of the party, the whole enemy line in view.
        public Shot Overview => new Shot(new Vector3(2.7f, 2f, -4.9f), new Vector3(-0.2f, 1.1f, 5.4f), -3f, 46f, 0.05f);

        // Low behind the member choosing, three-quarters from their right: the member on the left third, full
        // length, the noise bodies over the rest, drawn up by a longish lens. With a noise body to aim at: behind
        // the member's right shoulder on the line to it, the member's back on the left and the noise body picked
        // out by a long lens. No shot here comes nearer a member than a couple of metres, nor round to their
        // faces: the generated models do not stand a close look, and the cards and cut-ins show the faces in 2D.
        public Shot Behind(int member, int foe = -1)
        {
            var m = party[member].Home;
            if (foe < 0) return new Shot(m + new Vector3(1.5f, 1.1f, -3f), FoeCentre + Vector3.down * 0.15f, -3f, 40f, 0.05f);
            var f = foes[foe].Home;
            var ahead = f - m;
            ahead.y = 0f;
            ahead.Normalize();
            var right = new Vector3(ahead.z, 0f, -ahead.x);
            return new Shot(m - ahead * 2.4f + right * 0.85f + Vector3.up * 1.25f, f + Vector3.up * NoiseLook.Height, 4f, 38f, 0.025f);
        }

        // Low, a third of the way over from the party, at a noise body: out to its side, so it crosses the view
        // when it goes for them, or for the one in the middle straight in front, so it comes at the camera.
        public Shot Watch(int foe)
        {
            var f = foes[foe].Home;
            var side = Mathf.Abs(f.x) < 0.5f ? 0f : Mathf.Sign(f.x);
            return new Shot(new Vector3(f.x * 0.35f + 2.6f * side, 0.7f, f.z * (side == 0f ? 0.5f : 0.4f)), f + Vector3.up * (NoiseLook.Height - 0.1f),
                side == 0f ? -6f : -6f * side, 44f, 0.03f);
        }

        // Low behind the member a noise body has picked, the noise body over them: it comes at them and the camera.
        public Shot Threat(int foe, int member)
        {
            var m = party[member].Home;
            var f = foes[foe].Home + Vector3.up * NoiseLook.Height;
            var side = f.x >= m.x ? 1f : -1f;
            return new Shot(m + new Vector3(0.9f * side, 1.2f, -2.6f), f + Vector3.up * 0.25f, -7f * side, 44f, 0.02f);
        }

        // Down on the floor beside the run at a noise body, tipped, a wide lens: the member comes in from the side
        // and strikes across the view.
        Shot Charge(int member, int foe)
        {
            Vector3 m = party[member].Home, f = foes[foe].Home;
            var run = f - m;
            run.y = 0f;
            run.Normalize();
            var right = new Vector3(run.z, 0f, -run.x);
            return new Shot(Vector3.Lerp(m, f, 0.55f) + right * 2.6f - run * 0.4f + Vector3.up * 0.35f, Vector3.Lerp(m, f, 0.8f) + Vector3.up * 0.95f, 12f, 56f);
        }

        // Behind the member and out to their right, a little over their eyes: their back and the phone coming
        // up past their shoulder on the left, the noise body it is held up at beyond (SeaFigure.RaisePhone's pose).
        Shot OverPhone(int member, int foe)
        {
            var m = party[member].Home;
            var f = foes[foe].Home + Vector3.up * NoiseLook.Height;
            var ahead = f - m;
            ahead.y = 0f;
            ahead.Normalize();
            var right = new Vector3(ahead.z, 0f, -ahead.x);
            var eye = party[member].Figure.FaceCentre.y;
            var phone = m + ahead * 0.46f + Vector3.up * (eye - 0.11f);
            return new Shot(m + right * 1.05f - ahead * 2.3f + Vector3.up * (eye + 0.25f), Vector3.Lerp(phone, f, 0.55f), -5f, 40f, 0.015f);
        }

        // Beside the member on its outer side, low and a little behind, looking past it at the noise bodies:
        // what it casts flies across the view instead of from behind its back.
        public Shot Beside(int member)
        {
            var m = party[member].Home;
            var side = m.x >= 0f ? 1f : -1f;
            return new Shot(m + new Vector3(2.8f * side, 0.8f, -2f), Vector3.Lerp(m + Vector3.up * 1.3f, FoeCentre, 0.6f), -9f * side, 52f, 0.02f);
        }

        // Out on the member's outer side and behind, a little over their head, three and a half metres off: a pose
        // struck where they stand (防御, 道具, 深度思考) seen full length from over their shoulder, the rest of
        // the party off to the side of the frame. Round degrees from straight behind: further round for a hand
        // at the face (the rice ball, the fingers at the temple), which DeepSeek's hair hides from 45; much past
        // 60 the one beside them comes in behind them, and it never gets round to the face.
        public Shot Aside(int member, float round = 45f)
        {
            var m = party[member].Home;
            var side = m.x >= 0f ? 1f : -1f;
            var at = Quaternion.Euler(0f, -round * side, 0f) * Vector3.back * 3.4f + Vector3.up * 1.4f;
            return new Shot(m + at, m + new Vector3(0f, 0.95f, 0.5f), -5f * side, 42f, 0.02f);
        }

        // Low and to one side of the noise bodies, all down, turning slowly round them: the hold-up before the 集成.
        public Shot HoldUp => new Shot(FoeCentre + new Vector3(5f, -0.5f, -4.6f), FoeCentre + Vector3.down * 0.45f, 5f, 44f, 0.02f, -7f);

        // Up behind the party, looking out over the field they have cleared: their backs and raised arms on the
        // right, under the results, coming slowly round (slow enough that a long look at the results still
        // leaves the camera behind them).
        public Shot Victory => new Shot(new Vector3(-1.6f, 2.5f, -5.6f), new Vector3(-2.4f, 1.1f, 2.5f), -4f, 42f, 0.03f, 1.5f);

        Vector3 FoeCentre
        {
            get
            {
                var sum = Vector3.zero;
                var n = 0;
                foreach (var f in foes)
                {
                    if (f.Gone) continue;
                    sum += f.Home;
                    n++;
                }
                return (n > 0 ? sum / n : new Vector3(0f, 0f, 6.8f)) + Vector3.up * NoiseLook.Height;
            }
        }

        // The noise bodies close from down at their feet, the camera pushing in, then round behind the party.
        public void Opening()
        {
            Run().Forget();

            async Awaitable Run()
            {
                Move(Reveal(1f), 0.75f, Ease.Linear);
                await Anim.Wait(0.75f);
                if (this != null) Move(Overview, 0.65f);
            }
        }

        // Everyone still standing smiles and cheers at the win.
        public void Cheer()
        {
            foreach (var m in party)
            {
                m.Mood = "smile";
                if (m.Fall < 0.5f) m.Figure.Play("Cheer");
            }
        }

        public void Shake(float metres, float seconds)
        {
            shake = metres;
            shakeLeft = seconds;
        }

        // The view narrowed by degrees at once, opening back out over seconds: the jolt of a blow landing.
        public void Punch(float degrees, float seconds)
        {
            punch = degrees;
            punchLeft = punchLength = seconds;
        }

        public void Cut(Shot shot) => Move(shot, 0f);

        // Swings round the field's centre to the shot, rather than cutting straight across it; fast away and slow
        // to arrive unless told otherwise.
        public void Move(Shot shot, float seconds, System.Func<float, float> ease = null)
        {
            from = new Shot(standing, look, roll, fov, drift);
            to = shot;
            shotEase = ease ?? Ease.OutCubic;
            shotTime = 0f;
            shotLength = seconds;
            spun = 0f;
            if (seconds <= 0f) UpdateCamera(0f);
        }

        void UpdateCamera(float dt)
        {
            shotTime += dt;
            var t = shotLength > 0f ? shotEase(Mathf.Clamp01(shotTime / shotLength)) : 1f;
            Vector3 a = from.Position - Centre, b = to.Position - Centre;
            var angle = Mathf.LerpAngle(Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg, Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
            var radius = Mathf.Lerp(new Vector2(a.x, a.z).magnitude, new Vector2(b.x, b.z).magnitude, t);
            var position = Centre + new Vector3(Mathf.Sin(angle) * radius, Mathf.Lerp(a.y, b.y, t), Mathf.Cos(angle) * radius);
            look = Vector3.Lerp(from.Look, to.Look, t);
            // There: turning round what it looks at.
            if (shotTime >= shotLength && to.Spin != 0f)
            {
                spun += to.Spin * dt;
                position = look + Quaternion.Euler(0f, spun, 0f) * (position - look);
            }
            standing = position;
            roll = Mathf.Lerp(from.Roll, to.Roll, t);
            fov = Mathf.Lerp(from.Fov, to.Fov, t);
            drift = Mathf.Lerp(from.Drift, to.Drift, t);
        }

        // ------------------------------------------------------------------ anchors for the 2D layer

        // Screen point (pixels, y from the top) of a world point, or false behind the camera.
        public bool ToScreen(Vector3 world, out Vector2 screen)
        {
            var s = Camera.WorldToScreenPoint(world);
            screen = new Vector2(s.x, Screen.height - s.y);
            return s.z > 0.2f;
        }

        public Vector3 FoePoint(int foe) => foes[foe].Root.position + Vector3.up * NoiseLook.Height;

        public Vector3 MemberPoint(int member) => party[member].Root.position + Vector3.up * 1.15f;

        Vector3 Hand(int member) => party[member].Figure.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;

        // ------------------------------------------------------------------ party

        // Runs at the noise body, stops short of it and slashes; done as the blade lands (Slash lights it), the
        // run back follows once the slash is through.
        public async Awaitable Dash(int member, int foe)
        {
            var m = party[member];
            var target = foes[foe].Home;
            var to = target - m.Home;
            to.y = 0f;
            var stop = m.Home + to.normalized * Mathf.Max(0f, to.magnitude - 1.6f);
            var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            m.Yaw = yaw;
            Move(Charge(member, foe), 0.25f);
            await Anim.Tween(0.3f, t => m.At = Vector3.Lerp(m.Home, stop, t), Ease.InOutCubic);
            var length = m.Figure.Play("Slash", SlashPace);
            await Anim.Wait(SlashLands);
            Back().Forget();

            async Awaitable Back()
            {
                await Anim.Wait(length - SlashLands - 0.35f);
                m.Figure.Stop();
                Anim.Tween(0.4f, t => m.At = Vector3.Lerp(stop, m.Home, t), Ease.InOutCubic).Forget();
                Anim.Tween(0.4f, t => m.Yaw = Mathf.LerpAngle(yaw, 0f, t)).Forget();
            }
        }

        // The blade's light across the noise body: two crescents crossing, sparks and a ring.
        public void Slash(int foe)
        {
            var at = FoePoint(foe) + Vector3.down * 0.15f;
            effects.Slash(at, Sea, 1.25f, -32f);
            effects.Slash(at, Color.white, 1.0f, -20f, 0.26f);
            effects.Sparks(at, Sea, 28, 8f);
            effects.Flash(at, Sea, 2.2f);
            effects.Ring(at, (Camera.transform.position - at).normalized, Sea, 1.3f, 0.35f);
            Shake(0.06f, 0.2f);
        }

        // A step out in front of the line and back, with a cast.
        public async Awaitable Step(int member)
        {
            var m = party[member];
            var out_ = m.Home + new Vector3(0f, 0f, 0.7f);
            await Anim.Tween(0.25f, t => m.At = Vector3.Lerp(m.Home, out_, t), Ease.InOutCubic);
            Anim.Tween(0.45f, t => m.At = Vector3.Lerp(out_, m.Home, t), Ease.InOutCubic).Forget();
        }

        // The player's 截图: turns to the noise body where they stand and brings the phone up in both hands.
        public void Shoot(int member, int foe)
        {
            var m = party[member];
            var to = foes[foe].Home - m.Home;
            var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Anim.Tween(0.2f, t => m.Yaw = Mathf.LerpAngle(0f, yaw, t)).Forget();
            Move(OverPhone(member, foe), 0.3f);
            m.Figure.RaisePhone(ShootSeconds);
            Turn().Forget();

            async Awaitable Turn()
            {
                await Anim.Wait(ShootSeconds);
                await Anim.Tween(0.3f, t => m.Yaw = Mathf.LerpAngle(yaw, 0f, t));
            }
        }

        // The shutter: a flash at the phone, and the noise body lit white, shedding pixels of its colours.
        public void Capture(int member, int foe)
        {
            var at = FoePoint(foe);
            effects.Flash(Hand(member), Color.white, 0.9f, 0.2f);
            effects.Flash(at, Color.white, 3.2f, 0.3f);
            effects.Pixels(at, foes[foe].Look.Tint, Lens, 40, 5f);
            effects.Ring(at, (Camera.transform.position - at).normalized, Lens, 1.6f, 0.4f);
            Shake(0.05f, 0.15f);
        }

        // Steps out and casts; done as the arm is thrown out at the noise bodies.
        public async Awaitable Cast(int member)
        {
            var m = party[member];
            Move(Beside(member), 0.4f);
            m.Figure.Play("Cast", CastPace);
            Step(member).Forget();
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, 1.4f, 0.8f);
            await Anim.Wait(CastLands);
        }

        // 防御: braced where they stand, a ring of light held up between them and the noise bodies.
        public async Awaitable Guard(int member)
        {
            var m = party[member];
            Move(Aside(member), 0.35f);
            m.Figure.Act("Guard");
            await Anim.Wait(0.2f);
            var at = MemberPoint(member) + Vector3.forward * 0.6f;
            effects.Ring(at, Vector3.forward, Sea, 0.8f, 0.5f);
            effects.Flash(at, Sea, 1.2f, 0.25f);
            await Anim.Wait(GuardSeen - 0.2f);
        }

        // 道具: the 白米饭 held up as a rice ball (eaten, by DeepSeek), gone in a flash as the one it is for is lit
        // green; done then.
        public async Awaitable UseItem(int member, int target)
        {
            var m = party[member];
            Move(Aside(member, FaceRound), 0.35f);
            Mood(m, "smile", m.Figure.Act("Item")).Forget();
            m.Figure.HoldRiceBall(ItemUsed + 0.1f);
            await Anim.Wait(ItemUsed);
            effects.Flash(m.Figure.RiceBallAt, Color.white, 0.6f, 0.25f);
            effects.Ring(party[target].Root.position + Vector3.up * 0.05f, Vector3.up, Heal, 1.1f, 0.6f);
            effects.Sparks(MemberPoint(target), Heal, 18, 2.5f);
        }

        // 深度思考: DeepSeek stops to think, eyes shut, rings of cold light spreading at her feet.
        public async Awaitable Think(int member)
        {
            var m = party[member];
            Move(Aside(member, FaceRound), 0.4f);
            Mood(m, "think", m.Figure.Act("Think")).Forget();
            await Anim.Wait(0.35f);
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, 1.2f, 0.7f);
            await Anim.Wait(ThinkSeen - 0.35f);
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, 0.9f, 0.6f);
        }

        // The face for as long as an action plays, then as modelled.
        static async Awaitable Mood(Member m, string face, float seconds)
        {
            m.Mood = face;
            await Anim.Wait(seconds);
            if (m.Mood == face) m.Mood = null;
        }

        // A volley of ice lances from the caster's hand at each noise body; done when they land, frost bursting
        // out of the seabed round each.
        public async Awaitable Ice(int member, IList<int> targets)
        {
            var from = Hand(member);
            effects.Flash(from, Frozen, 1.2f, 0.25f);
            var flights = new List<Awaitable>();
            foreach (var foe in targets)
                for (var i = 0; i < 6; i++)
                    flights.Add(effects.Lance(from, FoePoint(foe) + Random.insideUnitSphere * 0.35f, Frozen, i * 0.045f));
            foreach (var flight in flights) await flight;
            foreach (var foe in targets)
            {
                effects.Frost(FoePoint(foe), Frozen, 0.75f).Forget();
                effects.Sparks(FoePoint(foe), Frozen, 20, 6f);
            }
            Shake(0.08f, 0.25f);
        }

        // 集成: the party dash across in front of the noise bodies one after another, trailing light and cutting
        // each as they pass (`pass` called as each dash sets off), then DeepSeek's whale of light swims up out of
        // the seabed behind them and over the field, `strike` called as it passes over them. Ends with everyone
        // back home and the camera on the overview.
        public async Awaitable AllOut(IList<int> members, System.Action pass, System.Action strike)
        {
            var line = float.MaxValue;
            foreach (var f in foes) if (!f.Gone) line = Mathf.Min(line, f.Home.z);
            line = (line == float.MaxValue ? 6.4f : line) - 1.1f;
            // Under the 2D layer's flash they are all off the field, so nobody stands between the camera and
            // the dashes; whoever is down stays where they lie, under the bottom of the view.
            foreach (var i in members) party[i].At = party[i].Root.localPosition = new Vector3(-12f, 0f, line);
            Move(new Shot(new Vector3(0.6f, 1.5f, -2.2f), new Vector3(0f, 1.1f, line + 1.2f)), 0.35f);
            await Anim.Wait(0.35f);
            for (var k = 0; k < 3 && members.Count > 0; k++)
            {
                var m = party[members[k % members.Count]];
                var dir = k % 2 == 0 ? 1f : -1f;
                Vector3 from = new Vector3(-9f * dir, 0f, line + k * 0.25f), to = new Vector3(9f * dir, 0f, line + k * 0.25f);
                m.At = m.Root.localPosition = from;
                m.Yaw = 90f * dir;
                var trail = effects.Streak(m.Root, k % 2 == 0 ? Sea : Lens, 1.1f, 0.3f);
                m.Figure.Play("Slash", 2.6f);
                pass?.Invoke();
                var cut = new HashSet<int>();
                await Anim.Tween(0.42f, t =>
                {
                    m.At = Vector3.Lerp(from, to, t);
                    for (var i = 0; i < foes.Count; i++)
                    {
                        if (foes[i].Gone || cut.Contains(i) || (m.At.x - foes[i].Home.x) * dir < 0f) continue;
                        cut.Add(i);
                        var at = FoePoint(i);
                        effects.Slash(at, k % 2 == 0 ? Sea : Lens, 1.4f, Random.Range(-60f, 60f) + (dir > 0 ? 0f : 180f), 0.3f);
                        effects.Sparks(at, Color.white, 16, 9f);
                        foes[i].Heat = 1f;
                    }
                }, Ease.Linear);
                if (trail != null) Destroy(trail.gameObject, 0.35f);
                m.Figure.Stop();
            }

            // A flash over the whole view while they are back in their places, and the whale.
            effects.Flash(Camera.transform.position + Camera.transform.forward * 1.2f, Color.white, 6f, 0.4f);
            foreach (var m in party)
            {
                m.At = m.Root.localPosition = m.Home;
                m.Yaw = 0f;
            }
            Move(new Shot(new Vector3(0f, 2.4f, -4.2f), new Vector3(0f, 2.8f, line + 2f)), 0.6f);
            await effects.Whale(new Vector3(-16f, -5f, line + 12f), new Vector3(0f, 8f, line + 1.5f), new Vector3(16f, -5f, line - 4f),
                8f, 1.6f, Sea, () =>
                {
                    var centre = Vector3.zero;
                    var n = 0;
                    for (var i = 0; i < foes.Count; i++)
                    {
                        if (foes[i].Gone) continue;
                        effects.Pillar(foes[i].Home, Sea, 0.9f, 9f, 1f);
                        effects.Sparks(FoePoint(i), Sea, 30, 10f, 0.12f);
                        effects.Flash(FoePoint(i), Color.white, 3f, 0.35f);
                        centre += foes[i].Home;
                        n++;
                    }
                    if (n > 0) effects.Ring(centre / n + Vector3.up * 0.05f, Vector3.up, Sea, 9f, 0.7f);
                    Shake(0.15f, 0.5f);
                    strike?.Invoke();
                });
            await Anim.Wait(0.3f);
            Move(Overview, 0.6f);
        }

        // Knocked back a little by a hit, wincing, sparks off where it struck.
        public void Recoil(int member)
        {
            var m = party[member];
            m.Wince = 0.7f;
            Punch(3f, 0.3f);
            if (m.Fall <= 0f) m.Figure.Play("Hit", 1.1f);
            effects.Sparks(MemberPoint(member), Harm, 18, 5f);
            effects.Flash(MemberPoint(member), Harm, 1.2f, 0.2f);
            Anim.Tween(0.35f, t =>
            {
                var k = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.3f);
                m.At = m.Home + new Vector3(Random.Range(-0.04f, 0.04f), 0f, -0.35f * k);
            }, Ease.Linear).Forget();
        }

        // Down at 0 HP, up again when healed.
        public void Faint(int member, bool down)
        {
            var m = party[member];
            var from = m.Fall;
            if (down) m.Figure.Stop();
            Anim.Tween(0.35f, t => m.Fall = Mathf.Lerp(from, down ? 1f : 0f, t)).Forget();
        }

        // ------------------------------------------------------------------ noise bodies

        public void Hit(int foe)
        {
            var f = foes[foe];
            f.Heat = 1f;
            Punch(3.5f, 0.3f);
            Anim.Tween(0.3f, t =>
            {
                var k = Mathf.Sin(t * Mathf.PI) * (1f - t);
                f.At = f.Home + new Vector3(Random.Range(-0.08f, 0.08f), 0f, 0.5f * k);
            }, Ease.Linear).Forget();
        }

        public void Down(int foe, bool down)
        {
            var f = foes[foe];
            var from = f.Down;
            Anim.Tween(0.3f, t => f.Down = Mathf.Lerp(from, down ? 1f : 0f, t), Ease.OutBack).Forget();
        }

        // Shrinks away with a shudder and is gone.
        public async Awaitable Vanish(int foe)
        {
            var f = foes[foe];
            f.Heat = 1f;
            await Anim.Tween(0.4f, t =>
            {
                if (f.Root != null) f.Root.localScale = Vector3.one * (1f - t) * (1f + Mathf.Sin(t * 30f) * 0.15f * (1f - t));
            }, Ease.InCubic);
            if (f.Root == null) return; // the field went while it shrank
            f.Gone = true;
            f.Root.gameObject.SetActive(false);
            f.Shadow.gameObject.SetActive(false);
        }

        // Comes at the party (or the member it picked) and goes back.
        public async Awaitable Lunge(int foe, int member = -1)
        {
            var f = foes[foe];
            f.Heat = 1f;
            var target = member >= 0 ? party[member].Home : Vector3.zero;
            var to = target - f.Home;
            to.y = 0f;
            var reach = f.Home + to.normalized * Mathf.Max(0f, to.magnitude - 1.6f);
            await Anim.Tween(0.22f, t => f.At = Vector3.Lerp(f.Home, reach, t), Ease.InCubic);
            Anim.Tween(0.35f, t => f.At = Vector3.Lerp(reach, f.Home, t), Ease.OutCubic).Forget();
        }

        public readonly struct Shot
        {
            public readonly Vector3 Position, Look;
            public readonly float Roll, Fov, Drift, Spin; // degrees, degrees, metres, degrees a second

            public Shot(Vector3 position, Vector3 look, float roll = 0f, float fov = 46f, float drift = 0f, float spin = 0f)
            {
                Position = position;
                Look = look;
                Roll = roll;
                Fov = fov;
                Drift = drift;
                Spin = spin;
            }
        }

        sealed class Member
        {
            public Transform Root;
            public SeaFigure Figure;
            public Vector3 Home, At;
            public float Yaw, Speed, Fall;
            public float Wince;  // seconds left of the face pulled at a hit
            public string Mood;  // the face otherwise (SeaFigure's names), null as modelled
            public Transform Shadow; // on the seabed, apart so it stays flat when they fall
        }

        sealed class Foe
        {
            public Transform Root;
            public NoiseLook Look;
            public Vector3 Home, At;
            public float Seed, Heat, Down;
            public bool Gone;
            public Transform Shadow;
        }
    }
}
