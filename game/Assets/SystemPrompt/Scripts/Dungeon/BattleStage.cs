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
        // in front of her, the sweep's arm coming round across her front, the jab's and the cross's fist out,
        // the leap's feet leaving the seabed and the landing's crouch, the cast's arm thrown out.
        const float SlashPace = 1.5f, SlashLands = 0.85f;
        const float SweepPace = 1.35f, SweepLands = 0.48f;
        const float RollPace = 1.7f, RollSeconds = 0.5f; // the dive and the roll over, across the floor
        const float JabPace = 1.6f, JabLands = 0.17f, CrossPace = 1.5f, CrossLands = 0.22f;
        const float LeapPace = 1.6f, LeapOff = 0.1f, LeapSeconds = 0.5f, LeapHeight = 1.5f, LandPace = 1.5f, LandLands = 0.04f;
        const float CastPace = 1.6f, CastLands = 1.3f;
        const float ShootSeconds = 1.1f; // the phone up, the shutter and down again
        const float KneelPace = 2.1f; // down on one knee for the low shot, a little ahead of the phone coming up
        const float FlinchPace = 0.8f; // a hit's jolt, a little slower than the library's
        // The poses struck in place (SeaFigure.Act): how long after the start each has been reached and seen,
        // when the command goes on, the 道具's rice ball used up at that moment; the way back follows by itself.
        const float GuardSeen = 0.75f, ItemUsed = 0.75f, ThinkSeen = 0.6f, FaceRound = 60f;
        static readonly Color Sea = new Color(0.4f, 0.85f, 1f), Frozen = new Color(0.55f, 0.9f, 1f), Flame = new Color(1f, 0.5f, 0.18f);
        static readonly Color Lens = new Color(0.55f, 1f, 0.6f), Harm = new Color(1f, 0.3f, 0.35f), Heal = new Color(0.6f, 1f, 0.55f);
        const float FrameAspect = GameRoot.Width / GameRoot.Height; // the UI's frame, which the shots are framed in

        public Camera Camera { get; private set; }
        readonly List<Member> party = new List<Member>();
        readonly List<Foe> foes = new List<Foe>();
        Shot from, to;
        System.Func<float, float> shotEase;
        bool shotStraight;
        float shotTime, shotLength, spun;
        Vector3 standing, look;  // where the shot puts the camera, before its drift and shake
        float roll, fov, drift;
        float shake, shakeLeft, punch, punchLeft, punchLength;
        float time;
        BattleEffects effects;
        SeaScene ring;
        // DeepSeek's 饭勺 and the player's 截图 each go through their ways in turn, picking up in the next battle
        // where the last left off, so no two in a row look alike.
        enum Strike { Slash, Sweep, Rush, Leap }
        static int strikes, shots;
        Strike strike; // the 饭勺 under way, whose blow Slash lights
        public SeaFigure.PhoneHold Held { get; private set; } // how the last 截图 was taken

        // partyIds in BattleScreen's order (GameState.PlayerId first); foeKinds the noise bodies' kinds.
        public static BattleStage Create(string playerGender, IList<string> partyIds, IList<string> foeKinds)
        {
            var go = new GameObject("Battle Field");
            var stage = go.AddComponent<BattleStage>();
            stage.Build(playerGender, partyIds, foeKinds);
            return stage;
        }

        // The field taken down with what was made for it: destroying its GameObject alone would leave its meshes
        // and materials behind, a lot of them every battle (the seabed, the racks' batched mesh, the figures'
        // and noise bodies' looks) and more with every hit.
        public void Drop()
        {
            effects.Dispose();
            ring.Dispose();
            Parts.Free(gameObject);
            Destroy(gameObject);
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
            camGo.AddComponent<BattleGlow>();
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
                // Four or more (noise bodies that joined up in the maze) stand closer, the inner ones further back.
                var x = n == 1 ? 0f : n == 2 ? (i * 2 - 1) * 1.7f : n == 3 ? (i - 1) * 2.8f : (i - (n - 1) / 2f) * 2.3f;
                var back = n == 3 && i == 1 || n >= 4 && i > 0 && i < n - 1;
                var home = new Vector3(x, 0f, back ? 7.6f : 6.4f);
                var root = Parts.Node("Noise " + i, transform, home);
                root.rotation = Quaternion.Euler(0f, 180f, 0f);
                foes.Add(new Foe { Root = root, Look = new NoiseLook(foeKinds[i], root), Home = home, At = home, Seed = i * 2.3f,
                    Shadow = SeaArt.Shadow(transform, NoiseLook.ShadowSize).transform });
            }

            Cut(Reveal(0f));
        }

        // A ring of rack stacks beyond the field, lower in front of the camera's usual side, lost in the fog:
        // modelled in Blender (assets/scenes/battle_ring/, SeaScene.Ring), its fans turning.
        void BuildRacks() => ring = SeaScene.Ring(transform);

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            time += dt;
            DungeonWorld.ApplySea(time, Vector3.zero, 9f);
            effects.Shine();
            ring.Animate(time);
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
                // Frozen solid: still, as it was when the ice closed on it.
                if (!f.Frozen) f.Look.Animate(time, f.Seed, Mathf.Max(f.Heat, f.Down * 0.4f));
                // Knocked down: over onto its side, sunk into the seabed a little. Carried round in a whirlpool:
                // lifted off the seabed, turning.
                f.Root.localPosition = f.At + new Vector3(0f, f.Lift - 0.55f * f.Down, 0f);
                f.Root.localRotation = Quaternion.Euler(0f, 180f + f.Spin, f.Down * 70f);
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
            Camera.fieldOfView = Opened(fov - kick);
        }

        // ------------------------------------------------------------------ camera

        // After Persona 5's battles: low, so the noise bodies loom over the party; tipped off the level (Roll)
        // when something happens, a long lens to pick one thing out and a wide one down on the floor for a
        // strike (Fov); quick to get where it is going and slow to settle; held by hand once there (Drift), and
        // turning slowly round what it looks at while there is time to look (Spin, degrees a second).

        // The noise bodies from down at their feet, tipped, the camera pushing in (push 0 to 1) as the battle opens.
        Shot Reveal(float push) => new Shot(FoeCentre + new Vector3(1.7f - push * 0.5f, -0.8f, -3.4f + push * 0.9f), FoeCentre + Vector3.up * 0.3f, 11f, 40f);

        // Behind and right of the party, the whole enemy line in view, the party standing up off the cards.
        public Shot Overview
        {
            get
            {
                var eye = new Vector3(2.7f, 2f, -4.9f);
                return new Shot(eye, Aim(eye, PartyCentre + Vector3.up * 0.9f, 0.4f, 0.44f, 46f, -3f), -3f, 46f, 0.05f);
            }
        }

        // The overview's, lower and wider, the party down near the cards: room over the field for what comes down
        // on them from high above it (转发保平安's arrows).
        Shot Skyward
        {
            get
            {
                var eye = new Vector3(2.7f, 1.6f, -5.2f);
                return new Shot(eye, Aim(eye, PartyCentre + Vector3.up * 0.9f, 0.4f, 0.34f, 56f, -3f), -3f, 56f, 0.05f);
            }
        }

        // Low behind the member choosing, over their right shoulder: the member just right of the command menu,
        // all but their feet, the noise bodies over the rest of the view, drawn up by a longish lens. With a noise
        // body to aim at: in closer on the line to it, the member's back on the left and the noise body picked
        // out by a long lens. No shot here comes nearer a member than a couple of metres, nor round to their
        // faces: the generated models do not stand a close look, and the cards and cut-ins show the faces in 2D.
        public Shot Behind(int member, int foe = -1)
        {
            var m = party[member].Home;
            if (foe < 0)
            {
                var eye = Over(m, FoeCentre, 2.6f, 1.2f, 0.38f, 0.65f, 40f);
                return new Shot(eye, Aim(eye, m + Vector3.up * 1.1f, 0.38f, 0.5f, 40f, -3f), -3f, 40f, 0.05f);
            }
            var f = foes[foe].Home + Vector3.up * NoiseLook.Height;
            var near = Over(m, f, 2.5f, 1.25f, 0.27f, 0.62f, 38f);
            return new Shot(near, Aim(near, f, 0.62f, 0.56f, 38f, 4f), 4f, 38f, 0.025f);
        }

        // Low, a third of the way over from the party, at a noise body: out to its side, so it crosses the view
        // when it goes for them, or for the one in the middle straight in front, so it comes at the camera, five
        // metres off so it does not fill the view. A little over the middle, clear of the cards.
        public Shot Watch(int foe)
        {
            var f = foes[foe].Home;
            var side = Mathf.Abs(f.x) < 0.5f ? 0f : Mathf.Sign(f.x);
            var eye = new Vector3(f.x * 0.35f + 2.6f * side, 0.7f, f.z * (side == 0f ? 0.3f : 0.4f));
            var roll = side == 0f ? -6f : -6f * side;
            return new Shot(eye, Aim(eye, f + Vector3.up * (NoiseLook.Height - 0.1f), 0.5f, 0.56f, 44f, roll), roll, 44f, 0.03f);
        }

        // Low behind the member a noise body has picked, the noise body over them: it comes at them and the camera.
        // Over the shoulder on the outside of the line, where nobody stands between: the right one on the left
        // of the view, clear of the cards; the left one on the right, stood up over them. Far enough back that
        // the hit throwing them back does not fill the view with them.
        public Shot Threat(int foe, int member)
        {
            var m = party[member].Home;
            var f = foes[foe].Home + Vector3.up * NoiseLook.Height;
            var left = party.Count > 1 && m.x < PartyCentre.x;
            var eye = Over(m, f, 3.2f, left ? 1.3f : 1.05f, left ? 0.68f : 0.3f, left ? 0.4f : 0.6f, 44f);
            return new Shot(eye, Aim(eye, m + Vector3.up * 1.05f, left ? 0.68f : 0.3f, left ? 0.6f : 0.42f, 44f, -6f), -6f, 44f, 0.02f);
        }

        // Down on the floor beside the run at a noise body, tipped, a wide lens: the member comes in from the side
        // (side 1 her right, -1 her left) and strikes across the view, the blow over the cards. At a noise body on
        // the end of the line, out on the outside of it whichever side was asked for: on the inside the next one
        // stands right by the camera. At the one in the middle, with one either side, back down the run off the
        // nearer and the blow off the middle toward it, which turns the view away from it.
        Shot Charge(int member, int foe, float side = 1f)
        {
            Vector3 m = party[member].Home, f = foes[foe].Home;
            var run = f - m;
            run.y = 0f;
            run.Normalize();
            if (Mathf.Abs(f.x) > 0.5f) side = Mathf.Sign(f.x * run.z);
            var right = new Vector3(run.z, 0f, -run.x) * side;
            var eye = Vector3.Lerp(m, f, 0.45f) + right * 2.9f - run * 0.4f + Vector3.up * 0.35f;
            for (var k = 0; k < 8 && Crowded(eye, foe, 4.4f); k++) eye -= run * 0.3f;
            return new Shot(eye, Aim(eye, Vector3.Lerp(m, f, 0.8f) + Vector3.up * 0.95f, 0.5f + 0.08f * side, 0.52f, 54f, 12f * side), 12f * side, 54f);
        }

        // Another noise body than `foe` stands within `metres` of `eye` across the seabed.
        bool Crowded(Vector3 eye, int foe, float metres)
        {
            for (var i = 0; i < foes.Count; i++)
            {
                if (i == foe || foes[i].Gone) continue;
                var apart = foes[i].Home - eye;
                apart.y = 0f;
                if (apart.magnitude < metres) return true;
            }
            return false;
        }

        // Behind her right shoulder and above it, a longish lens down the run, where she comes up out of the
        // dive (`at`): moved there over the dive, straight down the run, it comes along behind her as she goes
        // across the floor, and she jabs into the noise body in the middle of the frame. Her right, not her
        // left, where the player stands too near the camera.
        Shot Chase(Vector3 at, int foe)
        {
            var f = foes[foe].Home;
            var run = f - at;
            run.y = 0f;
            run.Normalize();
            var right = new Vector3(run.z, 0f, -run.x);
            var eye = at - run * 3.5f + right * 1.2f + Vector3.up * 1.6f;
            return new Shot(eye, Aim(eye, at + Vector3.up * 0.6f, 0.36f, 0.45f, 44f, 6f), 6f, 44f, 0.02f);
        }

        // Out to her right, down on the seabed and looking up, a wide lens: the top of her jump high in the frame,
        // the noise body she comes down on under it; landed, the camera has come in on the blow.
        Shot Under(int member, int foe, bool landed)
        {
            Vector3 m = party[member].Home, f = foes[foe].Home;
            var run = f - m;
            run.y = 0f;
            run.Normalize();
            var right = new Vector3(run.z, 0f, -run.x);
            if (landed) return new Shot(Vector3.Lerp(m, f, 0.7f) + right * 2.4f + Vector3.up * 0.25f, f - run * 0.6f + Vector3.up * 0.7f, 14f, 54f);
            var eye = Vector3.Lerp(m, f, 0.4f) + right * 3.6f - run * 0.8f + Vector3.up * 1.5f;
            var top = Vector3.Lerp(m, f, 0.43f) + Vector3.up * (LeapHeight + 0.9f);
            return new Shot(eye, Aim(eye, top, 0.38f, 0.7f, 56f, 9f), 9f, 56f);
        }

        // Behind the member and out to their right, a little over their eyes: their back and the phone coming
        // up past their shoulder on the left, the noise body it is held up at beyond (SeaFigure.RaisePhone's pose).
        // Held out at arm's length: higher and further back, the arm reaching out of the frame's middle at the
        // noise body. Held low from down on one knee: down by the seabed, looking up past them.
        Shot OverPhone(int member, int foe, SeaFigure.PhoneHold hold)
        {
            var m = party[member].Home;
            var f = foes[foe].Home + Vector3.up * NoiseLook.Height;
            var ahead = f - m;
            ahead.y = 0f;
            ahead.Normalize();
            var right = new Vector3(ahead.z, 0f, -ahead.x);
            var eye = party[member].Figure.FaceCentre.y;
            var phone = m + ahead * 0.46f + Vector3.up * (eye - 0.11f);
            return hold switch
            {
                SeaFigure.PhoneHold.Reach => new Shot(m + right * 0.8f - ahead * 2.6f + Vector3.up * (eye + 0.55f), Vector3.Lerp(phone + ahead * 0.3f, f, 0.5f), 7f, 38f, 0.015f),
                SeaFigure.PhoneHold.Low => new Shot(m + right * 1.1f - ahead * 2.1f + Vector3.up * 0.4f, Vector3.Lerp(m + Vector3.up * 0.9f, f, 0.6f), -10f, 46f, 0.015f),
                _ => new Shot(m + right * 1.05f - ahead * 2.3f + Vector3.up * (eye + 0.25f), Vector3.Lerp(phone, f, 0.55f), -5f, 40f, 0.015f),
            };
        }

        // Beside the member on its outer side, low and a little behind, looking past it at the noise bodies:
        // what it casts flies across the view instead of from behind its back.
        public Shot Beside(int member)
        {
            var m = party[member].Home;
            var side = m.x >= 0f ? 1f : -1f;
            // The member to the side the camera is on, the noise bodies across from them, all up off the cards.
            var eye = m + new Vector3(2.8f * side, 0.8f, -2f);
            return new Shot(eye, Aim(eye, m + Vector3.up * 0.9f, side > 0f ? 0.3f : 0.7f, 0.57f, 52f, -9f * side), -9f * side, 52f, 0.02f);
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
            var eye = m + Quaternion.Euler(0f, -round * side, 0f) * Vector3.back * 3.4f + Vector3.up * 1.4f;
            // Off the middle toward the field, standing up off the cards.
            return new Shot(eye, Aim(eye, m + Vector3.up * 0.95f, side > 0f ? 0.6f : 0.4f, 0.57f, 42f, -5f * side), -5f * side, 42f, 0.02f);
        }

        // Low and off to the left of the noise bodies, all down, turning slowly round them toward the front of the
        // line (round to its end instead, the nearest would come in between): the hold-up before the 集成. They
        // lie across the frame under the prompt that asks for it (BattleScreen), the nearest one down on the
        // left where there are no cards.
        public Shot HoldUp
        {
            get
            {
                var at = FoeCentre + Vector3.down * 0.8f; // lying on the seabed
                var eye = FoeCentre + new Vector3(-4f, -0.55f, -3.6f);
                return new Shot(eye, Aim(eye, at, 0.45f, 0.36f, 44f, -5f), -5f, 44f, 0.02f, -4f, at);
            }
        }

        // Up behind the party and out to their left, looking out over the field they have cleared: their backs and
        // raised arms right of the middle, over the cards, coming slowly round behind them (slow enough that a long
        // look at the results, which cover the field, still leaves the camera behind them).
        public Shot Victory
        {
            get
            {
                var at = PartyCentre + Vector3.up * 1f;
                var eye = PartyCentre + new Vector3(-3.4f, 2.3f, -4f);
                return new Shot(eye, Aim(eye, at, 0.66f, 0.53f, 40f, -4f), -4f, 40f, 0.03f, -1.5f, at);
            }
        }

        // Framing round the battle UI (1920 by 1080): the command menu down the left (to 0.29 of the width) and
        // the party's cards along the bottom (the lowest fifth, from the right edge to 0.42 with two of them).
        // A member the shot is about stands clear of both: right of the menu, above the cards. Points and lenses
        // are the UI's frame's, which GameRoot letterboxes in the middle of the window; the 3D fills the window
        // round it, so a window of another shape shows more of the field round the same picture.

        // The lens, degrees high, that has the frame see `degrees` high: the same in a window as wide as the frame
        // or wider (the window shows more to the sides); opened up in a narrower one, which has the frame across
        // its whole width with room above and below.
        float Opened(float degrees) => Camera.aspect >= FrameAspect ? degrees
            : 2f * Mathf.Atan(Mathf.Tan(degrees * 0.5f * Mathf.Deg2Rad) * FrameAspect / Camera.aspect) * Mathf.Rad2Deg;

        // Where to look from `eye` to have `subject` at the frame's point (x, y), 0 to 1 from the left and from
        // the bottom, through a lens fov degrees high, with the view tipped roll degrees (Shot.Roll) as it will be.
        Vector3 Aim(Vector3 eye, Vector3 subject, float x, float y, float fov, float roll)
        {
            var tanY = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var tanX = tanY * FrameAspect;
            // Where it has to be before the tip turns the picture round its middle.
            var tip = roll * Mathf.Deg2Rad;
            float u = (x - 0.5f) * 2f * tanX, v = (y - 0.5f) * 2f * tanY;
            x = 0.5f + (u * Mathf.Cos(tip) - v * Mathf.Sin(tip)) / (2f * tanX);
            y = 0.5f + (u * Mathf.Sin(tip) + v * Mathf.Cos(tip)) / (2f * tanY);
            var far = Vector3.Distance(eye, subject);
            var look = subject;
            for (var i = 0; i < 5; i++)
            {
                var turn = Quaternion.LookRotation(look - eye);
                var seen = Quaternion.Inverse(turn) * (subject - eye);
                var across = Mathf.Atan(seen.x / seen.z) - Mathf.Atan((x - 0.5f) * 2f * tanX);
                var up = Mathf.Atan(seen.y / seen.z) - Mathf.Atan((y - 0.5f) * 2f * tanY);
                look = eye + turn * Quaternion.Euler(-up * Mathf.Rad2Deg, across * Mathf.Rad2Deg, 0f) * Vector3.forward * far;
            }
            return look;
        }

        // Behind `member` and round off the line on from them to `at`, `back` metres from them and `up` over the
        // seabed: as far round as sets the two apart across the view, the member at frame x `mx` and `at` at `ax`
        // (0 the left edge, 1 the right), through a lens fov degrees high. Round to their right with them on the
        // left of the view, to their left with them on the right.
        Vector3 Over(Vector3 member, Vector3 at, float back, float up, float mx, float ax, float fov)
        {
            var ahead = at - member;
            ahead.y = 0f;
            var far = ahead.magnitude;
            ahead /= far;
            var right = new Vector3(ahead.z, 0f, -ahead.x);
            var half = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * FrameAspect;
            var apart = Mathf.Atan((ax - 0.5f) * 2f * half) - Mathf.Atan((mx - 0.5f) * 2f * half);
            var round = apart + Mathf.Asin(Mathf.Clamp(back * Mathf.Sin(apart) / far, -1f, 1f));
            var eye = member + (right * Mathf.Sin(round) - ahead * Mathf.Cos(round)) * back;
            eye.y = up;
            return eye;
        }

        Vector3 PartyCentre
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var m in party) sum += m.Home;
                return sum / party.Count;
            }
        }

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

        // Not with 画面震动 turned off in the settings, like the 2D effects' Fx.Shake.
        public void Shake(float metres, float seconds)
        {
            if (!GameRoot.Instance.Settings.screenShake) return;
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

        // Swings round the field's centre to the shot, rather than cutting straight across it (unless told to go
        // straight, along with someone); fast away and slow to arrive unless told otherwise.
        public void Move(Shot shot, float seconds, System.Func<float, float> ease = null, bool straight = false)
        {
            from = new Shot(standing, look, roll, fov, drift);
            to = shot;
            shotEase = ease ?? Ease.OutCubic;
            shotStraight = straight;
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
            var position = shotStraight ? Vector3.Lerp(from.Position, to.Position, t)
                : Centre + new Vector3(Mathf.Sin(angle) * radius, Mathf.Lerp(a.y, b.y, t), Mathf.Cos(angle) * radius);
            look = Vector3.Lerp(from.Look, to.Look, t);
            // There: turning round what it is about, the view turning with it so that stays where it was framed.
            if (shotTime >= shotLength && to.Spin != 0f)
            {
                spun += to.Spin * dt;
                var turn = Quaternion.Euler(0f, spun, 0f);
                position = to.Pivot + turn * (position - to.Pivot);
                look = to.Pivot + turn * (look - to.Pivot);
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

        // DeepSeek's 饭勺 (design/skills.md 战斗特效), the next of its four ways in: runs at the noise body and
        // slashes across it; runs in from her other side and sweeps low round on one foot; dives across the floor,
        // rolls up and jabs left, right; jumps and comes down on it. The spoon comes into her hand as she goes and
        // its bowl trails light through the swing. Done as the blow lands (Slash lights it), the way back follows
        // once the motion is through.
        public async Awaitable Dash(int member, int foe)
        {
            var m = party[member];
            strike = (Strike)(strikes++ % 4);
            var target = foes[foe].Home;
            var to = target - m.Home;
            to.y = 0f;
            var stop = m.Home + to.normalized * Mathf.Max(0f, to.magnitude - (strike == Strike.Rush ? 1.3f : strike == Strike.Leap ? 1.1f : 1.6f));
            var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            m.Yaw = yaw;
            m.Figure.Wield(true);
            var trail = effects.Streak(m.Figure.SpoonTip, Sea, 0.16f, 0.2f, "Spoon " + strike);
            if (trail != null) trail.transform.localPosition = Vector3.zero;
            float length, lands;
            switch (strike)
            {
                case Strike.Sweep:
                    Cut(Charge(member, foe, -1f));
                    await RunIn(m, stop);
                    length = m.Figure.Play("Sweep", SweepPace);
                    await Anim.Wait(lands = SweepLands);
                    break;
                case Strike.Rush:
                {
                    Move(Chase(stop, foe), RollSeconds + 0.1f, Ease.OutCubic, true);
                    m.Figure.Play("Roll", RollPace);
                    var bubbles = effects.Streak(m.Root, Sea, 0.5f, 0.25f, "Wake");
                    if (bubbles != null) bubbles.transform.localPosition = Vector3.up * 0.4f;
                    await Anim.Tween(RollSeconds, t => m.At = Vector3.Lerp(m.Home, stop, t), Ease.OutCubic);
                    if (bubbles != null) Destroy(bubbles.gameObject, 0.25f);
                    m.Figure.Play("Jab", JabPace);
                    await Anim.Wait(JabLands);
                    Poke(foe);
                    Move(Charge(member, foe), 0.35f);
                    length = m.Figure.Play("Cross", CrossPace);
                    await Anim.Wait(lands = CrossLands);
                    break;
                }
                case Strike.Leap:
                {
                    Move(Under(member, foe, false), 0.25f);
                    m.Figure.Play("Leap", LeapPace);
                    await Anim.Wait(LeapOff);
                    var landing = false;
                    length = 0f;
                    await Anim.Tween(LeapSeconds, t =>
                    {
                        m.At = Vector3.Lerp(m.Home, stop, Ease.InOutCubic(t)) + Vector3.up * (LeapHeight * 4f * t * (1f - t));
                        // The landing's own drop starts a little before the feet are down.
                        if (t < 0.8f || landing) return;
                        landing = true;
                        length = m.Figure.Play("Land", LandPace);
                        Move(Under(member, foe, true), 0.2f);
                    });
                    m.At = stop;
                    await Anim.Wait(lands = LandLands);
                    break;
                }
                default:
                    Cut(Charge(member, foe));
                    await RunIn(m, stop);
                    length = m.Figure.Play("Slash", SlashPace);
                    await Anim.Wait(lands = SlashLands);
                    break;
            }
            Back().Forget();

            async Awaitable Back()
            {
                await Anim.Wait(Mathf.Max(0.15f, length - lands - 0.35f));
                m.Figure.Stop();
                m.Figure.Wield(false);
                if (trail != null) Destroy(trail.gameObject);
                Anim.Tween(0.4f, t => m.At = Vector3.Lerp(stop, m.Home, t), Ease.InOutCubic).Forget();
                Anim.Tween(0.4f, t => m.Yaw = Mathf.LerpAngle(yaw, 0f, t)).Forget();
            }
        }

        // The run in up to `stop`, a wake of water behind.
        async Awaitable RunIn(Member m, Vector3 stop)
        {
            var wake = effects.Streak(m.Root, Sea, 0.7f, 0.22f, "Wake");
            await Anim.Tween(0.3f, t => m.At = Vector3.Lerp(m.Home, stop, t), Ease.InOutCubic);
            if (wake != null) Destroy(wake.gameObject, 0.25f);
        }

        // The jab before the cross: a small burst of water off the noise body and a jolt.
        void Poke(int foe)
        {
            var at = FoePoint(foe) + Vector3.down * 0.1f;
            effects.Flash(at, Sea, 1.3f, 0.18f);
            effects.Sparks(at, Sea, 12, 6f);
            effects.Ring(at, (Camera.transform.position - at).normalized, Color.white, 0.7f, 0.25f);
            Shake(0.03f, 0.1f);
        }

        // The 饭勺's blow landing on the noise body, after the way it came in. The slash: three crescents of
        // seawater across it, the deep blue one back the other way. The sweep: two crescents low round its
        // foot and one back over the top, a ring out across the seabed. The cross: two short cuts crossing.
        // The leap: crescents straight down, a ring and a spout of water out of the seabed.
        // Each with water thrown off, sparks and a ring.
        public void Slash(int foe)
        {
            var at = FoePoint(foe) + Vector3.down * 0.15f;
            var feet = new Vector3(at.x, 0.05f, at.z);
            var facing = (Camera.transform.position - at).normalized;
            switch (strike)
            {
                case Strike.Sweep:
                    var low = at + Vector3.down * 0.45f;
                    effects.Slash(low, Sea, 1.7f, 176f);
                    effects.Slash(low, Color.white, 1.4f, 182f, 0.26f);
                    effects.Slash(at, new Color(0.15f, 0.45f, 1f), 1.6f, 10f, 0.4f);
                    effects.Ring(feet, Vector3.up, Sea, 2.6f, 0.5f);
                    break;
                case Strike.Rush:
                    effects.Slash(at, Color.white, 0.95f, 68f, 0.24f);
                    effects.Slash(at, Sea, 1.15f, -64f, 0.3f);
                    effects.Ring(at, facing, Color.white, 1.0f, 0.3f);
                    break;
                case Strike.Leap:
                    effects.Slash(at, Color.white, 1.3f, 96f, 0.26f);
                    effects.Slash(at, Sea, 1.7f, 84f);
                    effects.Ring(feet, Vector3.up, Sea, 3.2f, 0.55f);
                    effects.Ring(feet, Vector3.up, Color.white, 2f, 0.35f);
                    effects.Pillar(feet, Sea, 0.7f, 2.6f, 0.5f);
                    break;
                default:
                    effects.Slash(at, Sea, 1.25f, -32f);
                    effects.Slash(at, Color.white, 1.0f, -20f, 0.26f);
                    effects.Slash(at, new Color(0.15f, 0.45f, 1f), 1.5f, 150f, 0.4f);
                    break;
            }
            effects.Splash(at, Sea);
            effects.Sparks(at, Sea, 28, 8f);
            effects.Flash(at, Sea, 2.2f);
            effects.Ring(at, facing, Sea, 1.3f, 0.35f);
            if (strike == Strike.Leap) Shake(0.1f, 0.3f);
            else Shake(0.06f, 0.2f);
        }

        // A step out in front of the line and back, with a cast.
        public async Awaitable Step(int member)
        {
            var m = party[member];
            var out_ = m.Home + new Vector3(0f, 0f, 0.7f);
            await Anim.Tween(0.25f, t => m.At = Vector3.Lerp(m.Home, out_, t), Ease.InOutCubic);
            Anim.Tween(0.45f, t => m.At = Vector3.Lerp(out_, m.Home, t), Ease.InOutCubic).Forget();
        }

        // The player's 截图: turns to the noise body where they stand and brings the phone up, the next of three
        // ways in turn: in both hands at the eyes, out at arm's length in one, or low from down on one knee.
        public void Shoot(int member, int foe)
        {
            var m = party[member];
            var hold = Held = (SeaFigure.PhoneHold)(shots++ % 3);
            var to = foes[foe].Home - m.Home;
            var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Anim.Tween(0.2f, t => m.Yaw = Mathf.LerpAngle(0f, yaw, t)).Forget();
            Move(OverPhone(member, foe, hold), 0.3f);
            if (hold == SeaFigure.PhoneHold.Low) m.Figure.Play("Kneel", KneelPace);
            m.Figure.RaisePhone(ShootSeconds, hold);
            Turn().Forget();

            async Awaitable Turn()
            {
                await Anim.Wait(ShootSeconds);
                if (hold == SeaFigure.PhoneHold.Low) m.Figure.Stop();
                await Anim.Tween(0.3f, t => m.Yaw = Mathf.LerpAngle(yaw, 0f, t));
            }
        }

        // The shutter: a flash at the phone, and a frame of light snapping shut on the noise body, lit white, and
        // flying back into the phone with the pixels it took off it.
        public void Capture(int member, int foe)
        {
            var at = FoePoint(foe);
            var phone = Hand(member);
            effects.Flash(phone, Color.white, 0.9f, 0.2f);
            effects.Flash(at, Color.white, 3.2f, 0.3f);
            effects.Snap(at, phone, Lens, foes[foe].Look.Tint, Lens);
            effects.Ring(at, (Camera.transform.position - at).normalized, Lens, 1.6f, 0.4f);
            Shake(0.05f, 0.15f);
        }

        // What the phone saw at the shutter, `width` by `height`: the noise body from the member's hand, close,
        // for the thumbnail of the shot (BattleScreen.ScreenshotFx). The caller releases it.
        public RenderTexture Photo(int member, int foe, int width, int height)
        {
            var photo = new RenderTexture(width, height, 24) { name = "Screenshot" };
            var target = FoePoint(foe);
            var eye = Camera.transform;
            Vector3 position = eye.position;
            Quaternion rotation = eye.rotation;
            var lens = Camera.fieldOfView;
            eye.position = target + (Hand(member) - target).normalized * 3f;
            // Turned over, or looking up, as the phone was held.
            eye.rotation = Quaternion.LookRotation(target - eye.position, party[member].Figure.ScreenUp);
            Camera.fieldOfView = 34f;
            Camera.aspect = (float)width / height;
            Camera.targetTexture = photo;
            Camera.Render();
            Camera.targetTexture = null;
            Camera.ResetAspect();
            Camera.fieldOfView = lens;
            eye.SetPositionAndRotation(position, rotation);
            return photo;
        }

        // Out on the member's outer side and behind as she casts at the noise body, then running alongside her
        // cast up the field to it: 寒流 (pv/README.md S15, 镜头快速横移跟随).
        Shot Alongside(int member, Vector3 target, bool there)
        {
            var m = party[member].Home;
            var run = target - m;
            run.y = 0f;
            run.Normalize();
            var side = new Vector3(run.z, 0f, -run.x) * (m.x >= 0f ? 1f : -1f);
            var roll = m.x >= 0f ? -8f : 8f;
            return there
                ? new Shot(target - run * 2.2f + side * 3.4f + Vector3.down * 0.2f, target, roll * 0.75f, 46f)
                : new Shot(m + side * 3.4f - run * 1.3f + Vector3.up * 1.1f, Vector3.Lerp(m + Vector3.up * 1.2f, target, 0.35f), roll, 50f, 0.02f);
        }

        // Steps out and casts, a ring of cold at her feet, wider charged; done as the arm is thrown out.
        async Awaitable Cast(int member, bool charged)
        {
            var m = party[member];
            m.Figure.Play("Cast", CastPace);
            Step(member).Forget();
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, charged ? 2.1f : 1.4f, 0.8f);
            if (charged) effects.Pillar(m.Home, Frozen, 0.9f, 3.5f, 0.7f);
            await Anim.Wait(CastLands);
        }

        // 寒流 (S15): her palm thrown out at the noise body and a current of cold carrying crystals of ice from
        // it, the camera running along beside it; the noise body frozen solid where it stands (`frozen` called
        // then), and a moment later the ice bursting. Done as it bursts, for the hit.
        public async Awaitable Current(int member, int foe, bool charged, System.Action frozen)
        {
            var target = FoePoint(foe);
            Move(Alongside(member, target, false), 0.35f);
            await Cast(member, charged);
            if (this == null) return;
            const float Flight = 0.42f;
            var hand = Hand(member);
            effects.Flash(hand, Frozen, charged ? 1.8f : 1.3f, 0.25f);
            Move(Alongside(member, target, true), Flight + 0.15f, Ease.InOutCubic);
            await effects.Stream(hand, target, Frozen, charged ? 0.6f : 0.38f, Flight);
            if (this == null) return;
            var f = foes[foe];
            var shell = effects.Freeze(target, Frozen, charged ? 1.25f : 1.05f);
            f.Frozen = true;
            frozen?.Invoke();
            Shake(0.08f, 0.2f);
            Punch(4f, 0.3f);
            await Anim.Wait(0.38f);
            if (this == null) return;
            Break(f, shell);
        }

        // 深海潜流 (S16): a whirlpool of cold light opening under the noise bodies, the camera down in it so it
        // fills the view, carrying them round and up in it as the camera pulls fast back to take it all in; all frozen together as it bursts (`frozen`
        // called then), the ice on each bursting one after another and the noise bodies back where they stood.
        // Done as the last bursts, for the hits.
        public async Awaitable Maelstrom(int member, IList<int> targets, bool charged, System.Action frozen)
        {
            Move(Beside(member), 0.4f);
            await Cast(member, charged);
            if (this == null) return;
            effects.Flash(Hand(member), Frozen, 1.6f, 0.3f);
            var centre = FoeCentre;
            centre.y = 0f;
            Cut(new Shot(centre + new Vector3(-1.8f, 1.1f, -4.6f), centre + Vector3.up * 1.4f, -10f, 66f));
            var pulled = false;
            var carried = new List<Foe>();
            foreach (var i in targets) if (!foes[i].Gone) carried.Add(foes[i]);
            await effects.Maelstrom(centre, 4.2f * (charged ? 1.15f : 1f), Frozen, 1.25f, t =>
            {
                if (!pulled && t > 0.16f)
                {
                    pulled = true;
                    Move(new Shot(centre + new Vector3(-3.2f, 4.4f, -10f), centre + Vector3.up * 1.2f, -6f, 56f, 0.03f), 1f);
                }
                // Round the way the whirlpool turns (clockwise from above), drawn in toward its middle.
                var k = Ease.InOutCubic(t);
                foreach (var f in carried)
                {
                    var off = f.Home - centre;
                    off.y = 0f;
                    f.At = centre + Quaternion.Euler(0f, 50f * k, 0f) * off * (1f - 0.15f * k);
                    f.Spin = 900f * k * k;
                    f.Lift = 1.3f * Mathf.Sin(Mathf.Min(1f, t * 1.3f) * Mathf.PI * 0.5f);
                    f.Heat = 1f;
                }
            });
            if (this == null) return;
            var shells = new List<Transform>();
            foreach (var f in carried)
            {
                shells.Add(effects.Freeze(f.Root.position + Vector3.up * NoiseLook.Height, Frozen, charged ? 1.2f : 1f));
                f.Frozen = true;
            }
            frozen?.Invoke();
            Shake(0.12f, 0.3f);
            Punch(5f, 0.35f);
            await Anim.Wait(0.3f);
            for (var i = 0; i < carried.Count; i++)
            {
                if (this == null) return;
                Break(carried[i], shells[i]);
                Settle(carried[i]).Forget();
                await Anim.Wait(0.1f);
            }
        }

        // The ice round a frozen noise body bursting and the noise body free again, for the hit.
        void Break(Foe f, Transform shell)
        {
            effects.Shatter(shell, Frozen);
            f.Frozen = false;
            f.Heat = 1f;
            Shake(0.1f, 0.25f);
            Punch(5f, 0.3f);
        }

        // A noise body the whirlpool carried off back where it stood, set down and turned to face the party.
        static async Awaitable Settle(Foe f)
        {
            var at = f.At;
            var spin = f.Spin;
            var lift = f.Lift;
            var facing = Mathf.Round(spin / 360f) * 360f;
            await Anim.Tween(0.35f, t =>
            {
                f.At = Vector3.Lerp(at, f.Home, t);
                f.Spin = Mathf.Lerp(spin, facing, t);
                f.Lift = Mathf.Lerp(lift, 0f, t);
            });
            f.Spin = 0f;
        }

        // A 冰镇汽水 (or a 辣条, alight) thrown overarm at the noise body: it arcs over, turning, and bursts on it in
        // foam and frost (or a ball of flame). Done as it bursts, for the hit.
        public async Awaitable Toss(int member, int foe, bool fire)
        {
            var m = party[member];
            var color = fire ? Flame : Frozen;
            Move(Beside(member), 0.4f);
            m.Figure.Play("Cast", CastPace);
            Step(member).Forget();
            await Anim.Wait(CastLands);
            if (this == null) return;
            var target = FoePoint(foe);
            await effects.Toss(fire, Hand(member), target, color, 0.5f);
            if (this == null) return;
            if (fire) effects.Blaze(target, color);
            else effects.Fizz(target, color);
            foes[foe].Heat = 1f;
            Shake(0.08f, 0.25f);
            Punch(3f, 0.25f);
        }

        // 防御: braced where they stand, a shield of cells of light raised between them and the noise bodies and
        // held there until their next turn (Unguard), flaring where a blow lands on it.
        public async Awaitable Guard(int member)
        {
            var m = party[member];
            Move(Aside(member), 0.35f);
            m.Figure.Act("Guard");
            await Anim.Wait(0.2f);
            if (this == null) return;
            var at = MemberPoint(member) + Vector3.forward * 0.75f;
            m.Shield?.Release();
            m.Shield = effects.Barrier(at, Vector3.forward, Sea);
            effects.Flash(at, Sea, 1.2f, 0.25f);
            await Anim.Wait(GuardSeen - 0.2f);
        }

        public void Unguard(int member)
        {
            var m = party[member];
            m.Shield?.Release();
            m.Shield = null;
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
            if (this == null) return;
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, 1.2f, 0.7f);
            effects.Thoughts(MemberPoint(member) + Vector3.up * 0.75f, Frozen, 1.4f);
            await Anim.Wait(ThinkSeen - 0.35f);
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, 0.9f, 0.6f);
        }

        // 深度思考's charge held under her feet (held) until her next skill spends it.
        public void Charged(int member, bool held)
        {
            var m = party[member];
            m.Aura?.Release();
            m.Aura = held ? effects.Aura(m.Root, Frozen) : null;
        }

        // The face for as long as an action plays, then as modelled.
        static async Awaitable Mood(Member m, string face, float seconds)
        {
            m.Mood = face;
            await Anim.Wait(seconds);
            if (m.Mood == face) m.Mood = null;
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
            Move(Dashes(line), 0.35f);
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
            Move(Breach(line), 0.6f);
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

        // Low and square on to the line the 集成's dashes run along (`line`), close enough that each crosses the
        // whole frame, the noise bodies lying beyond it a little under the middle, between the stamp and the cards.
        Shot Dashes(float line)
        {
            var eye = new Vector3(0.8f, 1f, line - 4.2f);
            return new Shot(eye, Aim(eye, new Vector3(0f, 0.6f, line + 1.2f), 0.5f, 0.44f, 50f, 4f), 4f, 50f, 0.02f);
        }

        // High behind the party and over their heads, they back home under the flash and out of the bottom of the
        // frame: the noise bodies lying low across the middle, over the cards, and the whale's arc over the field
        // above them.
        Shot Breach(float line)
        {
            var eye = new Vector3(-0.3f, 3.4f, -1.6f);
            return new Shot(eye, Aim(eye, new Vector3(0f, 0.4f, line + 1.1f), 0.5f, 0.28f, 56f, 0f), 0f, 56f, 0.02f);
        }

        // Jolted a step back by a hit, wincing, sparks off where it struck: the chest or the head thrown back by
        // turns, from the waist up, the legs keeping their stance.
        public void Recoil(int member)
        {
            var m = party[member];
            m.Wince = 0.7f;
            m.Shield?.Flare();
            Punch(3f, 0.3f);
            if (m.Fall <= 0f) m.Figure.Flinch(m.Hits++ % 2 == 0 ? "HitChest" : "HitHead", FlinchPace);
            effects.Sparks(MemberPoint(member), Harm, 18, 5f);
            effects.Flash(MemberPoint(member), Harm, 1.2f, 0.2f);
            Anim.Tween(0.3f, t =>
            {
                var k = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.3f);
                m.At = m.Home + new Vector3(Random.Range(-0.02f, 0.02f), 0f, -0.15f * k);
            }, Ease.Linear).Forget();
        }

        // Down at 0 HP, up again when healed.
        public void Faint(int member, bool down)
        {
            var m = party[member];
            var from = m.Fall;
            if (down)
            {
                m.Figure.Stop();
                Unguard(member);
                Charged(member, false);
            }
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

        // A blow on a weakness or a critical, lit in the colour of what struck it.
        public void Impact(int foe, SystemPrompt.Battle.Element element, bool critical)
        {
            var color = element switch
            {
                SystemPrompt.Battle.Element.Ice => Frozen,
                SystemPrompt.Battle.Element.Fire => Flame,
                _ => critical ? Harm : Lens,
            };
            effects.Impact(FoePoint(foe), color, critical);
            Punch(6f, 0.35f);
            Shake(0.1f, 0.25f);
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

        // 转发型 calls in another noise body: forked light streams from it to the free place on the line, where the
        // new one grows out of a column of light. Added after the others, so every index stays; the new one is
        // in the list as soon as this is called, standing once it is done.
        public async Awaitable Summon(string kind, int caller)
        {
            var home = FreePlace();
            var root = Parts.Node("Noise " + foes.Count, transform, home);
            root.rotation = Quaternion.Euler(0f, 180f, 0f);
            root.localScale = Vector3.zero;
            var f = new Foe { Root = root, Look = new NoiseLook(kind, root), Home = home, At = home, Seed = foes.Count * 2.3f, Heat = 1f,
                Shadow = SeaArt.Shadow(transform, NoiseLook.ShadowSize).transform };
            foes.Add(f);
            var from = FoePoint(caller);
            var tint = foes[caller].Look.Tint;
            foes[caller].Heat = 1f;
            effects.Flash(from, tint, 1.4f, 0.25f);
            var flights = new List<Awaitable>();
            for (var i = 0; i < 4; i++)
                flights.Add(effects.Lance(from, home + Vector3.up * (NoiseLook.Height + Random.Range(-0.3f, 0.3f)), tint, i * 0.06f, 0.35f));
            foreach (var flight in flights) await flight;
            effects.Pillar(home, tint, 0.8f, 4f, 0.7f);
            effects.Pixels(home + Vector3.up * NoiseLook.Height, tint, f.Look.Tint, 36, 4f);
            Shake(0.05f, 0.2f);
            await Anim.Tween(0.45f, t =>
            {
                if (root != null) root.localScale = Vector3.one * t;
            }, Ease.OutBack);
        }

        // Of the places a line of one to three noise bodies stands on, the one furthest from those still standing.
        Vector3 FreePlace()
        {
            var best = new Vector3(0f, 0f, 6.4f);
            var room = -1f;
            foreach (var x in new[] { -2.8f, 2.8f, 0f, -1.7f, 1.7f })
            {
                var at = new Vector3(x, 0f, 6.4f);
                var nearest = float.MaxValue;
                foreach (var f in foes)
                    if (!f.Gone) nearest = Mathf.Min(nearest, Vector3.Distance(f.Home, at));
                if (nearest <= room) continue;
                room = nearest;
                best = at;
            }
            return best;
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

        // 标题党型's 震惊！: it comes at the member and flings pop-up windows at them; done as the last hits.
        public async Awaitable Bait(int foe, int member)
        {
            Lunge(foe, member).Forget();
            await Anim.Wait(0.12f);
            if (this == null) return;
            await effects.Popups(FoePoint(foe), MemberPoint(member), 4, foes[foe].Look.Tint, Palette.Hex("#FFD23F"));
        }

        // 冷知识型's 你知道吗？: it comes at the member and deals out fact cards at them; done as the last hits.
        public async Awaitable Lecture(int foe, int member)
        {
            Lunge(foe, member).Forget();
            await Anim.Wait(0.12f);
            if (this == null) return;
            await effects.Cards(FoePoint(foe), MemberPoint(member), 3, foes[foe].Look.Tint, Palette.Hex("#BFE9FF"));
        }

        // 转发型's 转发保平安: forked arrows shot up out of it that split and come down on the whole party, the
        // camera back to take in the field and the height over it. Done when all have landed.
        public async Awaitable Forward(int foe, IList<int> members)
        {
            foes[foe].Heat = 1f;
            Move(Skyward, 0.35f);
            await Anim.Wait(0.2f);
            if (this == null) return;
            var targets = new List<Vector3>();
            foreach (var i in members) targets.Add(MemberPoint(i));
            await effects.Forward(FoePoint(foe) + Vector3.up * 0.4f, targets, foes[foe].Look.Tint);
            if (this == null) return;
            Shake(0.1f, 0.3f);
        }

        public readonly struct Shot
        {
            public readonly Vector3 Position, Look, Pivot; // Pivot: what it turns round (Spin), by default Look
            public readonly float Roll, Fov, Drift, Spin; // degrees, degrees, metres, degrees a second

            public Shot(Vector3 position, Vector3 look, float roll = 0f, float fov = 46f, float drift = 0f, float spin = 0f, Vector3? pivot = null)
            {
                Position = position;
                Look = look;
                Pivot = pivot ?? look;
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
            public int Hits;     // taken so far, the jolt going by turns
            public string Mood;  // the face otherwise (SeaFigure's names), null as modelled
            public Transform Shadow; // on the seabed, apart so it stays flat when they fall
            public BattleEffects.Held Shield, Aura; // 防御's shield, 深度思考's charge
        }

        sealed class Foe
        {
            public Transform Root;
            public NoiseLook Look;
            public Vector3 Home, At;
            public float Seed, Heat, Down;
            public float Spin, Lift; // carried round in 深海潜流's whirlpool
            public bool Gone, Frozen;
            public Transform Shadow;
        }
    }
}
