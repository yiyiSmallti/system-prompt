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
        // The motions' pace and the moment in each (seconds at that pace) that lands: 横斩's three cuts across her
        // front (SlashCombo: the first, the second that long after it, the third, the blow, after that), the
        // sweep's arm coming round across her front, the jab's and the cross's fist out, the leap's feet leaving the
        // seabed and the landing's crouch, the cast's palm pushed out.
        const float ComboPace = 1.4f, ComboFirst = 0.19f, ComboSecond = 0.33f, ComboThird = 0.6f;
        const float SweepPace = 1.35f, SweepLands = 0.48f;
        const float RollPace = 1.7f, RollSeconds = 0.5f; // the dive and the roll over, across the floor
        const float JabPace = 1.6f, JabLands = 0.17f, CrossPace = 1.5f, CrossLands = 0.22f;
        const float LeapPace = 1.6f, LeapOff = 0.1f, LeapSeconds = 0.5f, LeapHeight = 1.5f, LandPace = 1.5f, LandLands = 0.04f;
        const float CastPace = 1.6f, CastLands = 0.55f;
        const float ThrowPace = 1.2f, ThrowRelease = 0.32f; // overarm, the can or the 辣条 leaving the hand
        const float ShootSeconds = 1.1f; // the phone up, the shutter and down again
        // Down on one knee for the low shot, a little ahead of the phone coming up, held there till the shot is
        // taken, and up off it again.
        const float KneelPace = 2.1f, KneelUpPace = 1.8f;
        // A party member going down at 0 HP, and knocked off their feet (lying till their turn) and up again.
        const float FaintPace = 1.2f, KnockPace = 1.1f, GetUpPace = 1.4f;
        const float FlinchPace = 0.8f; // a hit's jolt, a little slower than the library's
        // The poses struck in place (SeaFigure.Act): how long after the start each has been reached and seen,
        // when the command goes on, the 道具's rice ball used up at that moment; the way back follows by itself.
        const float GuardSeen = 0.75f, ItemUsed = 0.75f, ThinkSeen = 0.6f, FaceRound = 60f;
        // The hit-stops (Freeze), three to ten frames, the heavier the blow the longer: the jab; the slash and the
        // sweep; the cross; the leap's landing; the shutter; 寒流's ice bursting, and each of 深海潜流's; a thrown
        // can or 辣条 bursting; a weakness or critical on top of its blow; a blow on a party member.
        const float StopJab = 0.05f, StopSlash = 0.07f, StopCross = 0.08f, StopLeap = 0.11f, StopShutter = 0.06f,
            StopIce = 0.08f, StopShard = 0.04f, StopToss = 0.06f, StopWeak = 0.04f, StopTaken = 0.05f;
        // The battle's last blow (Finish): held longest, the kill shot cut to partway through the hold, the win taking
        // over a little later with the cubes still in the air (the camera would otherwise sit on bare racks once they
        // have flown out of the low shot).
        const float StopKill = 0.16f, KillCut = 0.1f, KillSeen = 0.45f;
        const float Whoosh = 0.12f; // how long before a swing's blow its whoosh is heard
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
        int moves; // how many times the camera has been sent somewhere (Move), so a late move can see it was overtaken
        Vector3 standing, look;  // where the shot puts the camera, before its drift and shake
        float roll, fov, drift;
        // A shake (Shake): metres, the way it throws the picture across the view (x right, y up), how far in; how
        // many there have been, which picks a shake's own way and tremor without drawing on UnityEngine.Random
        // (the damage rolls draw on it).
        float shake, shakeTime, shakeLength, shakeSeed;
        Vector2 shakeDir;
        int shakes;
        int jitters; // how many Jitter has given, the same way: a cut's slant, a blow's twist, a lance's height
        float punch, punchLeft, punchLength;
        float time;
        // The hit-stop (Freeze): while it holds, the figures, the noise bodies and every move they make on the field
        // (Paced) stand still where the blow caught them; the camera, the sea and the light of the blow go on.
        // `paced` is the field's own clock, game time less the hit-stops.
        float hold, paced;
        bool finishing; // the battle's last blow being seen (Finish)
        BattleEffects effects;
        SeaLens seaLens;
        SeaScene ring;
        // DeepSeek's 饭勺 and the player's 截图 each go through their ways in turn, picking up in the next battle
        // where the last left off, so no two in a row look alike.
        enum Strike { Slash, Sweep, Rush, Leap }
        static int strikes, shots;
        Strike strike; // the 饭勺 under way, whose blow Slash lights
        // Where the party look (Update): at a noise body coming at them for ThreatSeen seconds, at the one the turn's
        // cursor is on, else at the nearest, glancing now and then at whoever's turn it is (actor).
        const float ThreatSeen = 2.5f, GlanceSeen = 0.8f;
        int actor = -1;
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
            Shader.SetGlobalFloat(InkId, 0f); // a battle torn down mid-集成 leaves no ink on whatever is drawn next
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
            seaLens = SeaLens.Fit(Camera, SeaLens.Battle);
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
                // Out of step with each other, and down into the knees: the player lower than the caster, and leant
                // toward the noise bodies over the phone (as the library has it the stance stands straight-legged,
                // leaning back).
                figure.Stance(i * 0.37f, 1f - 0.035f * i);
                figure.Crouch = partyIds[i] != "DeepSeek" ? 0.06f : 0.02f;
                figure.Forward = partyIds[i] != "DeepSeek" ? 9f : 0f;
                figure.Attentive = true;
                // A tail hooks round to the side with no one standing: the last in the line to their right.
                figure.TailTo = i > 0 && i == partyIds.Count - 1 ? 1f : -1f;
                // The player, boy or girl, holds the phone ready before the chest in both hands.
                figure.Ready = partyIds[i] != "DeepSeek";
                party.Add(new Member { Root = root, Figure = figure, Home = home, At = home, Was = home, Glance = 3f + 2.3f * i,
                    Shadow = SeaArt.Shadow(transform, 1f).transform });
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
                var look = new NoiseLook(foeKinds[i], root, i * 2.3f, true);
                look.Unform(); // till the opening brings it in (Opening)
                foes.Add(new Foe { Root = root, Look = look, Home = home, At = home, Shadow = SeaArt.Shadow(transform, NoiseLook.ShadowSize).transform });
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
            // What of this frame the hit-stop leaves the bodies (to the fraction of a frame, so a hold lasts as asked).
            var act = Mathf.Max(0f, dt - hold);
            hold = Mathf.Max(0f, hold - dt);
            paced += act;
            DungeonWorld.ApplySea(time, Vector3.zero, 9f);
            ink = Mathf.MoveTowards(ink, inkTo, inkRate * dt);
            Shader.SetGlobalFloat(InkId, ink);
            effects.Shine();
            ring.Animate(time);
            foreach (var m in party)
            {
                // Held, nobody moves: the walk's blend keeps where it was rather than easing toward standing. Gliding
                // (pushed by a blow, hopping back) the feet don't step.
                if (act > 0f)
                {
                    var moved = Vector3.Distance(m.Was, m.At) / act;
                    m.Was = m.At;
                    m.Speed = m.Glide ? 0f : Mathf.Lerp(m.Speed, moved, 1f - Mathf.Exp(-14f * act));
                }
                m.Root.localPosition = m.At + m.Jolt;
                m.Root.localRotation = Quaternion.Euler(m.Lean, m.Yaw + m.YawJolt, 0f);
                m.Wince -= dt;
                Watch(m, act);
                m.Figure.SetFace(m.Fall > 0.5f ? "down" : m.Wince > 0f ? "hurt" : m.Mood);
                m.Figure.Lying = m.Knocked || m.Fall > 0f;
                m.Figure.Animate(act, m.Fall > 0f ? 0f : m.Speed);
                // Lying, the shadow is under the body where it fell (the fall carries it a step back from where the
                // feet stood) and as long as it is; the first time the hips come down to the seabed, dust rings out.
                var hips = transform.InverseTransformPoint(m.Figure.Hips.position);
                m.Laid = Mathf.MoveTowards(m.Laid, m.Figure.Lying ? 1f : 0f, act / 0.3f);
                var laid = Mathf.SmoothStep(0f, 1f, m.Laid);
                m.Shadow.localPosition = Vector3.Lerp(new Vector3(m.At.x, 0.02f, m.At.z), new Vector3(hips.x, 0.02f, hips.z), laid);
                m.Shadow.localScale = new Vector3(1f + 0.35f * laid, 1f, 1f + 0.35f * laid);
                if (!m.Figure.Lying) m.Landed = false;
                else if (!m.Landed && hips.y < 0.3f)
                {
                    m.Landed = true;
                    var floor = transform.TransformPoint(new Vector3(hips.x, 0.05f, hips.z));
                    effects.Ring(floor, Vector3.up, Sea * 0.7f, 1.5f, 0.45f);
                    effects.Sparks(floor + Vector3.up * 0.1f, Color.Lerp(Sea, Color.white, 0.5f), 10, 3.5f);
                    Punch(2.5f, 0.25f);
                }
            }
            foreach (var f in foes)
            {
                if (f.Gone) continue;
                // Dizzy while it lies knocked down, wary while picked out. Frozen solid: still, as it was when the
                // ice closed on it. Its heat is read before it cools, or a blow's never shows. Held by the field's
                // hit-stop, unless it is in its own (the rig's, kicked by the blow), which runs out alongside.
                f.Look.BaseMood = f.Down > 0.5f ? cowering ? NoiseMood.Cower : NoiseMood.Dizzy : f.Picked ? NoiseMood.Wary : NoiseMood.Calm;
                f.Look.Pace = f.Frozen ? 0f : 1f;
                var own = f.Look.Rig.Freeze > 0f ? dt : act;
                f.Look.Animate(f.Frozen ? 0f : own, f.Heat, f.Down);
                f.Heat = Mathf.MoveTowards(f.Heat, 0f, own * 2.5f);
                if (!f.Frozen) f.Turn = Mathf.LerpAngle(f.Turn, f.TurnWant, 1f - Mathf.Exp(-6f * own));
                // Knocked down: over onto its side, sunk into the seabed a little. Carried round in a whirlpool:
                // lifted off the seabed, turning. Its shadow shrinks as it rises, and goes as it bursts.
                f.Root.localPosition = f.At + new Vector3(0f, f.Lift - 0.55f * f.Down, 0f);
                f.Root.localRotation = Quaternion.Euler(0f, 180f + f.Spin + f.Turn, f.Down * 70f);
                f.Shadow.localPosition = new Vector3(f.At.x, 0.02f, f.At.z);
                var lift = Mathf.Max(0f, f.Lift + f.Look.Rig.Offset.y);
                f.Shadow.localScale = new Vector3(1f, 0f, 1f) * (NoiseLook.ShadowSize * f.Root.localScale.x * (1f - f.Burst) * f.Look.Formed / (1f + 0.6f * lift)) + Vector3.up;
            }
            UpdateCamera(dt);
            // Held by hand: never quite still. A shake throws the picture the way the blow went and lets it swing
            // back and settle, a little roll with it, a breath of tremor left over; a punch narrows the view at once
            // and lets it back out once the hit-stop lets go.
            var sway = new Vector3(Mathf.Sin(time * 0.71f) + 0.5f * Mathf.Sin(time * 1.37f), 0.6f * Mathf.Sin(time * 0.93f + 1f), 0.5f * Mathf.Sin(time * 0.57f + 2f)) * drift;
            var aim = new Vector3(Mathf.Sin(time * 0.61f + 3f), 0.5f * Mathf.Sin(time * 0.83f), 0f) * drift * 0.5f;
            var turn = Quaternion.LookRotation(look + aim - standing) * Quaternion.Euler(0f, 0f, roll);
            var jolt = Vector3.zero;
            var tilt = 0f;
            if (shakeTime < shakeLength)
            {
                shakeTime += dt;
                var k = Mathf.Clamp01(shakeTime / shakeLength);
                var swing = shake * Mathf.Exp(-5f * k) * Mathf.Cos(2f * Mathf.PI * 11f * shakeTime);
                var tremor = shake * 0.5f * (1f - k);
                // The camera goes against the throw, so the picture goes with it.
                jolt = turn * new Vector3(
                    -shakeDir.x * swing + (Mathf.PerlinNoise(shakeTime * 25f, shakeSeed) - 0.5f) * tremor,
                    -shakeDir.y * swing + (Mathf.PerlinNoise(shakeSeed, shakeTime * 25f) - 0.5f) * tremor, 0f);
                tilt = -15f * swing * shakeDir.x; // 1.5° for a 0.1 m shake thrown square across
            }
            punchLeft -= act;
            var kick = punchLeft > 0f ? punch * Mathf.Pow(punchLeft / punchLength, 2f) : 0f;
            Camera.transform.position = standing + sway + jolt;
            Camera.transform.rotation = turn * Quaternion.Euler(0f, 0f, tilt);
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

        // Low behind the party, near enough to see the whole line take what came down on all of them (转发保平安's
        // arrows), the noise bodies beyond, the party up off the cards.
        Shot Struck
        {
            get
            {
                var eye = PartyCentre + new Vector3(-1f, 1.3f, -4.4f);
                return new Shot(eye, Aim(eye, PartyCentre + Vector3.up * 1f, 0.52f, 0.56f, 48f, -4f), -4f, 48f, 0.02f);
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

        // The charge's shot pushed in on a longer lens through the wind-up, there as the blow lands: by half a metre,
        // less where that brings another noise body up by the camera or the camera within 2.2 m of her at `stop`.
        Shot Closing(int member, int foe, float side, Vector3 stop)
        {
            var shot = Charge(member, foe, side);
            for (var k = 5; k > 0; k--)
            {
                var pushed = Pushed(shot, 0.1f * k, 48f);
                var apart = pushed.Position - stop;
                apart.y = 0f;
                if (!Crowded(pushed.Position, foe, 4.4f) && apart.magnitude >= 2.2f) return pushed;
            }
            return shot;
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
        // 60 the one beside them comes in behind them, and it never gets round to the face. A pose with a hand up
        // `overhead` (the player's rice ball) is taken from further off and aimed higher, so the hand comes under
        // the skill bar.
        public Shot Aside(int member, float round = 45f, bool overhead = false)
        {
            var m = party[member].Home;
            var side = m.x >= 0f ? 1f : -1f;
            var eye = m + Quaternion.Euler(0f, -round * side, 0f) * Vector3.back * (overhead ? 3.8f : 3.4f) + Vector3.up * 1.4f;
            // Off the middle toward the field, standing up off the cards.
            return new Shot(eye, Aim(eye, m + Vector3.up * (overhead ? 1.25f : 0.95f), side > 0f ? 0.6f : 0.4f, overhead ? 0.64f : 0.57f, 42f, -5f * side), -5f * side, 42f, 0.02f);
        }

        // Low and off to the left of the noise bodies, all down, turning slowly round them toward the front of the
        // line (round to its end instead, the nearest would come in between): the hold-up before the 集成. They
        // lie across the frame under the prompt that asks for it (BattleScreen), the nearest one down on the
        // left where there are no cards.
        // The hold-up: every body that is down cowers under the party's eyes (NoiseLook's Cower) till the prompt
        // is answered.
        public void Cower(bool on) => cowering = on;
        bool cowering;

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

        // The noise bodies close from down at their feet, the camera pushing in as they come together out of the
        // pixels one after another (Materialise), then round to `end` (the first to act's shot, or the overview when
        // they act first) — unless something has moved the camera since (the first turn's own move): that stands.
        public void Opening(Shot end, SystemPrompt.Battle.BattleOpening how)
        {
            Run().Forget();
            for (var i = 0; i < foes.Count; i++) Materialise(foes[i], 0.1f * i, how).Forget();

            async Awaitable Run()
            {
                Move(Reveal(1f), 0.75f, Ease.Linear);
                var mine = moves;
                await Anim.Wait(0.75f);
                if (this != null && moves == mine) Move(end, 0.65f);
            }
        }

        // A noise body coming together after `delay` (NoiseLook.Assemble, 0.45 s), showing itself a moment after it
        // is whole (Roar). Caught by a 先制攻击 it reels instead, rocked twice, its eyes 「> <」 through the banner; in
        // a 被偷袭 it roars the louder, leaning at the party.
        async Awaitable Materialise(Foe f, float delay, SystemPrompt.Battle.BattleOpening how)
        {
            var look = f.Look;
            look.Assemble(delay);
            while (!look.Whole)
            {
                await Awaitable.NextFrameAsync();
                if (this == null || f.Gone) return;
            }
            await PacedWait(0.1f);
            if (this == null || f.Gone) return;
            if (how != SystemPrompt.Battle.BattleOpening.Preemptive)
            {
                await Roar(f, how == SystemPrompt.Battle.BattleOpening.Ambush);
                return;
            }
            look.SetMood(NoiseMood.Hurt, 1f);
            look.Rig.Kick(Vector3.right, 0.06f, 8f, 0f, 0f, 15f);
            await PacedWait(0.2f);
            if (this == null) return;
            look.Rig.Kick(Vector3.left, 0.06f, 8f, 0f, 0f, -15f);
        }

        // Whole, it shows itself: drawn up tall and slammed down, angry a moment with everything it has open (its
        // pop-ups, arrows or cards: Windup) and its mouth shouting, a ring of its colour going out over the seabed
        // under it. `loud`: further both ways, and leaning at the party a while. Left wound up if its own attack
        // has started meanwhile (Brace).
        async Awaitable Roar(Foe f, bool loud)
        {
            var look = f.Look;
            var size = loud ? 0.3f : 0.2f;
            look.SetMood(NoiseMood.Angry, 0.6f);
            look.Windup(PartyCentre - f.Home);
            look.Rig.Kick(Vector3.up, 0f, 0f, -size);
            if (loud) look.Rig.Hold(0f, 15f);
            effects.Ring(f.Home + Vector3.up * 0.05f, Vector3.up, look.Tint, 1.6f, 0.4f);
            await PacedWait(0.1f);
            if (this == null || f.Gone) return;
            look.Rig.Kick(Vector3.up, 0f, 0f, size);
            await PacedWait(loud ? 0.5f : 0.4f);
            if (this == null || f.Gone || f.Bracing) return;
            look.Unwind();
            if (loud) look.Rig.Hold(0f);
        }

        // Everyone still standing smiles at the win and strikes a pose held as long as the results stay up, shields
        // lowered (the camera going up behind the party would look through them): the player the phone held up high,
        // taking the cleared field, its flash going off once it is up; DeepSeek a moment after with the 饭勺 held up
        // at the sky, her tail flung. Returns the seconds to the player's shutter, for its sound, or -1 when they are
        // down.
        public float Cheer()
        {
            var shutter = -1f;
            for (var i = 0; i < party.Count; i++)
            {
                var m = party[i];
                m.Shield?.Release();
                m.Shield = null;
                m.Mood = "smile";
                if (m.Fall >= 0.5f) continue;
                // Still lying where a blow knocked them: they get up instead.
                if (m.Knocked)
                {
                    Rise(i);
                    continue;
                }
                if (m.Figure.CarriesPhone)
                {
                    m.Figure.Act("Victory", true);
                    shutter = SeaFigure.PoseTimes["Victory"].Rise + 0.15f;
                    Snapshot(m, shutter).Forget();
                }
                else Pose(m).Forget();
            }
            return shutter;

            async Awaitable Snapshot(Member m, float after)
            {
                await Anim.Wait(after);
                if (this == null) return;
                effects.Flash(m.Figure.PhoneLens, Color.white, 0.5f, 0.15f); // dimmed under 减弱闪光 by Flash
            }

            async Awaitable Pose(Member m)
            {
                await Anim.Wait(0.15f);
                if (this == null || m.Fall >= 0.5f) return;
                m.Figure.Wield(true);
                m.Figure.Act("Victory", true);
                m.Figure.Flick(15f);
            }
        }

        // The view jolted by up to `metres` over `seconds`, the picture thrown the way `push` goes in the world as
        // the view sees it (straight down when it goes into the view or away out of it, any way at all without
        // one); a lighter one doesn't cut short a heavier still under way. Not with 画面震动 turned off in the
        // settings, like the 2D effects' Fx.Shake.
        public void Shake(float metres, float seconds, Vector3? push = null)
        {
            if (!GameRoot.Instance.Settings.screenShake) return;
            if (shakeTime < shakeLength && metres < shake * Mathf.Exp(-5f * shakeTime / shakeLength)) return;
            var view = Camera.transform;
            var p = push.GetValueOrDefault().normalized;
            var k = ++shakes;
            // Without a push, each a golden angle round from the last.
            var across = push.HasValue ? new Vector2(Vector3.Dot(p, view.right), Vector3.Dot(p, view.up)) : new Vector2(Mathf.Cos(k * 2.4f), Mathf.Sin(k * 2.4f));
            if (push.HasValue && across.magnitude < 0.25f) across = new Vector2(0.3f * Mathf.Sin(k * 12.9898f), -1f);
            shakeDir = across.sqrMagnitude > 0.0001f ? across.normalized : Vector2.down;
            shake = metres;
            shakeTime = 0f;
            shakeLength = seconds;
            shakeSeed = k * 7.31f % 100f;
        }

        // Somewhere between -most and most, scattered from one call to the next by a hash of how many there have
        // been: for how a thing looks only, so the damage rolls' UnityEngine.Random is left alone.
        float Jitter(float most) => most * (2f * Mathf.Repeat(Mathf.Sin(++jitters * 12.9898f) * 43758.547f, 1f) - 1f);

        // The view narrowed by degrees at once, opening back out over seconds: the jolt of a blow landing. With
        // 画面震动 turned off a little of it is left, so a blow still lands.
        public void Punch(float degrees, float seconds)
        {
            punch = GameRoot.Instance.Settings.screenShake ? degrees : degrees * 0.4f;
            punchLeft = punchLength = seconds;
        }

        // A blow landing: everything on the field held for `seconds` (a few frames), the longer of two holds winning.
        // Not a flash nor a shake, so the settings leave it be.
        public void Freeze(float seconds) => hold = Mathf.Max(hold, seconds);

        // A move of a body on the field over `seconds` of the field's clock: held with the rest at a hit-stop, so
        // nothing slides on while its pose stands still.
        public async Awaitable Paced(float seconds, System.Action<float> step, System.Func<float, float> ease = null)
        {
            ease ??= Ease.OutCubic;
            var start = paced;
            while (paced - start < seconds)
            {
                step(ease(Mathf.Clamp01((paced - start) / seconds)));
                await Awaitable.NextFrameAsync();
                if (this == null) return;
            }
            step(1f);
        }

        // `seconds` of the field's clock, for what comes at a moment of a motion (the blow of a swing).
        public async Awaitable PacedWait(float seconds)
        {
            var start = paced;
            while (paced - start < seconds)
            {
                await Awaitable.NextFrameAsync();
                if (this == null) return;
            }
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
            moves++;
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

        // Where noise body `foe` is in the picture as the camera has it now (pixels, y from the top), standing or
        // lying: the box round its body and what stands off it. Knocked down it lies well beside its plate, which
        // stays over its root (FoePoint). False when it is gone or partly behind the camera.
        public bool FoeOnScreen(int foe, out Rect screen)
        {
            screen = default;
            var f = foes[foe];
            if (f.Gone || f.Root == null) return false;
            var body = f.Look.Body;
            const float Side = NoiseLook.Radius + 0.15f;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var corner = body.position + body.right * ((i & 1) == 0 ? -Side : Side) + body.forward * ((i & 2) == 0 ? -Side : Side)
                    + body.up * ((i & 4) == 0 ? -Side : NoiseLook.Top);
                if (!ToScreen(corner, out var s)) return false;
                xMin = Mathf.Min(xMin, s.x);
                xMax = Mathf.Max(xMax, s.x);
                yMin = Mathf.Min(yMin, s.y);
                yMax = Mathf.Max(yMax, s.y);
            }
            screen = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        public Vector3 MemberPoint(int member) => party[member].Root.position + Vector3.up * 1.15f;

        // Where a member's hips are, standing or lying where they fell.
        public Vector3 MemberHips(int member) => party[member].Figure.Hips.position;

        // A bone of a member as posed now, for the lab's close-ups of a face or a pair of hands.
        public Vector3 MemberBone(int member, HumanBodyBones bone) => party[member].Figure.Animator.GetBoneTransform(bone).position;

        // The face a member shows now, as SeaFigure.FaceCells (eyes, mouth, blush; -1 none), and where it is and looks.
        public Vector4 MemberFace(int member) => party[member].Figure.FaceCells;
        public (Vector3 at, Vector3 forward) MemberFaceAt(int member) => (party[member].Figure.FaceCentre, party[member].Figure.FaceForward);

        // Where a blow or a heal lands on a member: MemberPoint while they stand, their chest while they lie (knocked
        // down or gone down: Knockback and Faint carry the body back and down off that spot, so a blow aimed there
        // would burst in the air over them).
        Vector3 Body(int member)
        {
            var m = party[member];
            return m.Knocked || m.Fall > 0f ? m.Figure.Animator.GetBoneTransform(HumanBodyBones.Chest).position : MemberPoint(member);
        }

        Vector3 Hand(int member) => party[member].Figure.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;

        // ------------------------------------------------------------------ party

        // DeepSeek's 饭勺 (design/skills.md 战斗特效), the next of its four ways in: runs at the noise body and
        // slashes across it; runs in from her other side and sweeps low round on one foot; dives across the floor,
        // rolls up and jabs left, right; jumps and comes down on it. The spoon comes into her hand as she goes and
        // its bowl trails light through the swing. Done as the blow lands (Slash lights it), the way back follows
        // once the motion is through. `swing` is called a moment before each blow lands, and as she jumps. When the
        // strike `missed`, the jab before the cross goes past too.
        public async Awaitable Dash(int member, int foe, System.Action swing = null, bool missed = false)
        {
            var m = party[member];
            strike = (Strike)(strikes++ % 4);
            var target = foes[foe].Home;
            var to = target - m.Home;
            to.y = 0f;
            var stop = m.Home + to.normalized * Mathf.Max(0f, to.magnitude - (strike == Strike.Rush ? 1.3f : strike == Strike.Leap ? 1.1f : 1.6f));
            foes[foe].LastStrike = stop;
            var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            // Turned toward it as she sets off, not in one frame, the tail swinging out the other way.
            Paced(0.1f, t => m.Yaw = Mathf.LerpAngle(0f, yaw, t)).Forget();
            m.Figure.Flick(-12f * Mathf.Sign(yaw));
            m.Figure.Wield(true);
            var trail = effects.Streak(m.Figure.SpoonTip, Sea, 0.16f, 0.2f, "Spoon " + strike);
            if (trail != null)
            {
                trail.transform.localPosition = Vector3.zero;
                // The tip's light is for the swings (and the leap's arc): on through the run in it was a straight
                // white line across the field, and again all the way back.
                trail.emitting = strike == Strike.Leap;
            }
            // The blade of water and the tip's light, once she is there and swinging (her arm coming down out of the
            // stance into the run drew a stray arc of its own).
            BattleEffects.Held blade = null;
            void Draw()
            {
                if (trail != null) trail.emitting = true;
                blade ??= effects.Swipe(m.Figure.SpoonBowl, m.Figure.SpoonTip, m.Root, Sea, () => paced);
            }
            float length, lands;
            switch (strike)
            {
                case Strike.Sweep:
                    Cut(Charge(member, foe, -1f));
                    await RunIn(m, stop);
                    Draw();
                    Move(Closing(member, foe, -1f, stop), SweepLands, Ease.InCubic, true);
                    length = m.Figure.Play("Sweep", SweepPace);
                    await Swing(lands = SweepLands, swing);
                    break;
                case Strike.Rush:
                {
                    Move(Chase(stop, foe), RollSeconds + 0.1f, Ease.OutCubic, true);
                    m.Figure.Play("Roll", RollPace);
                    var bubbles = effects.Streak(m.Root, Sea, 0.5f, 0.25f, "Wake");
                    if (bubbles != null) bubbles.transform.localPosition = Vector3.up * 0.4f;
                    await Paced(RollSeconds, t => m.At = Vector3.Lerp(m.Home, stop, t), Ease.OutCubic);
                    if (bubbles != null) Destroy(bubbles.gameObject, 0.25f);
                    Draw();
                    m.Figure.Play("Jab", JabPace);
                    await Swing(JabLands, swing);
                    Poke(foe, missed);
                    Move(Charge(member, foe), 0.35f);
                    length = m.Figure.Play("Cross", CrossPace);
                    await Swing(lands = CrossLands, swing);
                    break;
                }
                case Strike.Leap:
                {
                    Move(Under(member, foe, false), 0.25f);
                    Draw();
                    m.Figure.Play("Leap", LeapPace);
                    swing?.Invoke();
                    await PacedWait(LeapOff);
                    var landing = false;
                    length = 0f;
                    await Paced(LeapSeconds, t =>
                    {
                        m.At = Vector3.Lerp(m.Home, stop, Ease.InOutCubic(t)) + Vector3.up * (LeapHeight * 4f * t * (1f - t));
                        // The landing's own drop starts a little before the feet are down.
                        if (t < 0.8f || landing) return;
                        landing = true;
                        length = m.Figure.Play("Land", LandPace);
                        m.Figure.Flick(10f);
                        Move(Under(member, foe, true), 0.2f);
                    });
                    m.At = stop;
                    await PacedWait(lands = LandLands);
                    break;
                }
                default:
                    // Three cuts, the first two only nicking it, the third the blow.
                    Cut(Charge(member, foe));
                    await RunIn(m, stop);
                    Draw();
                    Move(Closing(member, foe, 1f, stop), ComboFirst + ComboSecond + ComboThird, Ease.InCubic, true);
                    length = m.Figure.Play("SlashCombo", ComboPace);
                    await Swing(ComboFirst, swing);
                    Nick(foe, 0);
                    await Swing(ComboSecond, swing);
                    Nick(foe, 1);
                    await Swing(ComboThird, swing);
                    lands = ComboFirst + ComboSecond + ComboThird;
                    break;
            }
            Back().Forget();

            // A leap back home once the motion is nearly through, still facing the noise body (turned and run back, her
            // face would come at the camera over her shoulder): a crouch and a push off, then up and back in an arc
            // with her legs drawn up under her (the jump's airborne pose, held till she comes down), leaning back at
            // the top, landing into a crouch where she stood. A real jump rather than a slide: carried over the floor
            // upright, it read as her gliding backwards. The 饭勺's light ends with the blow; the 饭勺 goes as she lands.
            async Awaitable Back()
            {
                await PacedWait(Mathf.Max(0.15f, length - lands - 0.35f));
                if (this == null) return;
                if (trail != null) trail.emitting = false;
                blade?.Release();
                var far = Vector3.Distance(stop, m.Home);
                float seconds = far > 3f ? 0.56f : 0.42f, height = far > 3f ? 1.15f : 0.6f;
                m.Glide = true;
                m.Figure.Play("Leap", LeapPace * 1.5f, fadeIn: 0.06f);
                m.Figure.HoldEnd();
                await PacedWait(LeapOff / 1.5f);
                if (this == null) return;
                var landed = false;
                await Paced(seconds, t =>
                {
                    m.At = Vector3.Lerp(stop, m.Home, t * t * (3f - 2f * t)) + Vector3.up * (height * 4f * t * (1f - t));
                    m.Yaw = Mathf.LerpAngle(yaw, 0f, t);
                    m.Lean = -16f * Mathf.Sin(Mathf.PI * t);
                    if (landed || t < 0.78f) return;
                    landed = true;
                    // Up out of the landing's crouch a little quicker than off the leaping blow: she only hopped home.
                    m.Figure.Play("Land", LandPace * 1.35f);
                    m.Figure.Flick(10f);
                    m.Figure.Wield(false);
                }, Ease.Linear);
                if (this == null) return;
                m.At = m.Home;
                m.Lean = 0f;
                m.Glide = false;
                if (trail != null) Destroy(trail.gameObject);
            }
        }

        // A motion played up to the moment its blow lands, `lands` seconds in, the swing's whoosh a moment before.
        async Awaitable Swing(float lands, System.Action swing)
        {
            await PacedWait(lands - Whoosh);
            swing?.Invoke();
            await PacedWait(Mathf.Min(lands, Whoosh));
        }

        // The run in up to `stop`, leaning into it, a wake of water behind.
        async Awaitable RunIn(Member m, Vector3 stop)
        {
            var wake = effects.Streak(m.Root, Sea, 0.7f, 0.22f, "Wake");
            await Paced(0.3f, t =>
            {
                m.At = Vector3.Lerp(m.Home, stop, t);
                m.Lean = 14f * Mathf.Sin(Mathf.PI * t);
            }, Ease.InOutCubic);
            m.Lean = 0f;
            if (wake != null) Destroy(wake.gameObject, 0.25f);
        }

        // One of 横斩's first two cuts going across the noise body: its crescent (the first the sea-blue one, the
        // second the deep blue one back the other way), a little water and a jolt; the blow itself is the third (Slash).
        void Nick(int foe, int cut)
        {
            var f = foes[foe];
            var at = FoePoint(foe) + Vector3.down * 0.15f;
            if (cut == 0) effects.Slash(at, Sea, 1.25f, -32f);
            else effects.Slash(at, new Color(0.15f, 0.45f, 1f), 1.5f, 150f, 0.4f);
            effects.Sparks(at, Sea, 10, 5f);
            f.Look.Rig.Kick(Local(f, BlowDirection(f)), 0.06f, 4f, 0.08f, 0.04f);
            f.Look.Flash(0.4f);
            Shake(0.03f, 0.08f, BlowDirection(f));
        }

        // The jab before the cross: a small burst of water off the noise body and a jolt; held a moment and the noise
        // body knocked and lit, unless it `missed` (the water still thrown, as the slash's is).
        void Poke(int foe, bool missed)
        {
            var at = FoePoint(foe) + Vector3.down * 0.1f;
            var f = foes[foe];
            if (!missed)
            {
                Freeze(StopJab);
                f.Look.Rig.Kick(Local(f, BlowDirection(f)), 0.1f, 6f, 0.08f, 0.04f);
                f.Look.Flash(0.6f);
            }
            effects.Flash(at, Sea, 1.3f, 0.18f);
            effects.Sparks(at, Sea, 12, 6f);
            effects.Ring(at, (Camera.transform.position - at).normalized, Color.white, 0.7f, 0.25f);
            Shake(0.03f, 0.1f, BlowDirection(f));
        }

        // The 饭勺's blow landing on the noise body, after the way it came in. The slash: three crescents of
        // seawater across it, the deep blue one back the other way. The sweep: two crescents low round its
        // foot and one back over the top, a ring out across the seabed. The cross: two short cuts crossing.
        // The leap: crescents straight down, a ring and a spout of water out of the seabed.
        // Each with water thrown off, sparks and a ring, and held a moment as it lands (not when it `missed`).
        public void Slash(int foe, bool missed = false)
        {
            if (!missed) Freeze(strike switch { Strike.Rush => StopCross, Strike.Leap => StopLeap, _ => StopSlash });
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
                    effects.WaterCrown(feet, Sea);
                    seaLens.Wave(feet); // the seabed struck
                    break;
                default:
                    // The third of 横斩's cuts (the first two are Nick's).
                    effects.Slash(at, Color.white, 1.0f, -20f, 0.26f);
                    effects.Slash(at, Sea, 1.5f, 35f);
                    break;
            }
            effects.Splash(at, Sea);
            effects.Sparks(at, Sea, 28, 8f);
            effects.Flash(at, Sea, 2.2f);
            effects.Ring(at, facing, Sea, 1.3f, 0.35f);
            if (strike == Strike.Leap) Shake(0.1f, 0.3f, Vector3.down);
            else Shake(0.06f, 0.2f, BlowDirection(foes[foe]));
        }

        // The player's 截图: turns to the noise body where they stand and brings the phone up, the next of three
        // ways in turn: in both hands at the eyes, out at arm's length in one, or low from down on one knee.
        public void Shoot(int member, int foe)
        {
            var m = party[member];
            var hold = Held = (SeaFigure.PhoneHold)(shots++ % 3);
            foes[foe].LastStrike = m.Home;
            var to = foes[foe].Home - m.Home;
            var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Paced(0.2f, t => m.Yaw = Mathf.LerpAngle(0f, yaw, t)).Forget();
            Move(OverPhone(member, foe, hold), 0.3f);
            if (hold == SeaFigure.PhoneHold.Low)
            {
                m.Figure.Play("Kneel", KneelPace, fadeIn: 0.2f);
                m.Figure.HoldEnd();
            }
            m.Figure.RaisePhone(ShootSeconds, hold);
            Turn().Forget();

            async Awaitable Turn()
            {
                // Pushing up off the knee as the phone comes down, rather than the kneel let go into the stance.
                await PacedWait(ShootSeconds - 0.1f);
                if (this == null) return;
                if (hold == SeaFigure.PhoneHold.Low) m.Figure.Play("KneelUp", KneelUpPace, fadeIn: 0.12f);
                await PacedWait(0.1f);
                if (this == null) return;
                await Paced(0.3f, t => m.Yaw = Mathf.LerpAngle(yaw, 0f, t));
            }
        }

        // The shutter: a flash at the phone, and a frame of light snapping shut on the noise body, lit white, and
        // flying back into the phone with the pixels it took off it; held a moment, unless the shot `missed`.
        public void Capture(int member, int foe, bool missed = false)
        {
            if (!missed) Freeze(StopShutter);
            var at = FoePoint(foe);
            var phone = Hand(member);
            effects.Flash(phone, Color.white, 0.9f, 0.2f);
            effects.Flash(at, Color.white, 1.8f, 0.24f, 1.7f);
            effects.Snap(at, phone, Lens, foes[foe].Look.Tint, Lens);
            effects.Ring(at, (Camera.transform.position - at).normalized, Lens, 1.6f, 0.4f);
            Shake(0.05f, 0.15f, BlowDirection(foes[foe]));
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
        // cast up the field to it (back down the run from another noise body standing by there): 寒流
        // (pv/README.md S15, 镜头快速横移跟随).
        Shot Alongside(int member, int foe, Vector3 target, bool there)
        {
            var m = party[member].Home;
            var run = target - m;
            run.y = 0f;
            run.Normalize();
            var side = new Vector3(run.z, 0f, -run.x) * (m.x >= 0f ? 1f : -1f);
            var roll = m.x >= 0f ? -8f : 8f;
            if (!there) return new Shot(m + side * 3.4f - run * 1.3f + Vector3.up * 1.1f, Vector3.Lerp(m + Vector3.up * 1.2f, target, 0.35f), roll, 50f, 0.02f);
            var eye = target - run * 2.2f + side * 3.4f + Vector3.down * 0.2f;
            for (var k = 0; k < 8 && Crowded(eye, foe, 4f); k++) eye -= run * 0.3f;
            return new Shot(eye, target, roll * 0.75f, 46f);
        }

        // Down onto a noise body 寒流 has knocked flat, from where the camera is, turning as it falls: where its middle
        // will be lying (Update's pose at Down 1) a little under the middle of the picture.
        public void Floored(int foe)
        {
            var f = foes[foe];
            var lying = f.At + new Vector3(0f, f.Lift - 0.55f, 0f) + Quaternion.Euler(0f, 180f + f.Spin, 70f) * new Vector3(0f, NoiseLook.Height, 0f);
            Move(new Shot(standing, Aim(standing, lying + Vector3.up * 0.2f, 0.5f, 0.42f, fov, roll), roll, fov, 0.02f), 0.3f, Ease.OutCubic, true);
        }

        // Casts where she stands, from the stance straight into the gathering before the palm goes out, a ring of
        // cold at her feet, wider charged; done as the palm is pushed out.
        async Awaitable Cast(int member, bool charged)
        {
            var m = party[member];
            m.Figure.Play("Cast", CastPace, fadeIn: 0.15f);
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, charged ? 2.1f : 1.4f, 0.8f);
            if (charged) effects.Pillar(m.Home, Frozen, 0.9f, 3.5f, 0.7f);
            await PacedWait(CastLands);
        }

        // 寒流 (S15): her palm thrown out at the noise body and a current of cold carrying crystals of ice from
        // it, the camera running along beside it; the noise body frozen solid where it stands (`frozen` called
        // then), and a moment later the ice bursting. Done as it bursts, for the hit.
        public async Awaitable Current(int member, int foe, bool charged, System.Action frozen)
        {
            var target = FoePoint(foe);
            Move(Alongside(member, foe, target, false), 0.35f);
            await Cast(member, charged);
            if (this == null) return;
            const float Flight = 0.42f;
            var hand = Hand(member);
            effects.Flash(hand, Frozen, charged ? 1.8f : 1.3f, 0.25f);
            Move(Alongside(member, foe, target, true), Flight + 0.15f, Ease.InOutCubic);
            await effects.Stream(hand, target, Frozen, charged ? 0.6f : 0.38f, Flight);
            if (this == null) return;
            var f = foes[foe];
            f.LastStrike = party[member].Home;
            var shell = effects.Freeze(target, Frozen, charged ? 1.25f : 1.05f);
            f.Frozen = true;
            Frost(f, true);
            frozen?.Invoke();
            Shake(0.08f, 0.2f, BlowDirection(f));
            Punch(4f, 0.3f);
            await PacedWait(0.5f); // long enough to see it stopped in the ice
            if (this == null) return;
            Break(f, shell, StopIce);
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
            foreach (var f in carried) f.LastStrike = centre; // flung out of the whirlpool
            var wide = new Shot(centre + new Vector3(-3.2f, 4.4f, -10f), centre + Vector3.up * 1.2f, -6f, 56f, 0.03f);
            await effects.Maelstrom(centre, 4.2f * (charged ? 1.15f : 1f), Frozen, 1.25f, t =>
            {
                if (!pulled && t > 0.16f)
                {
                    pulled = true;
                    Move(wide, 1f);
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
            seaLens.Wave(centre + Vector3.up * 1.4f); // the whirl bursting
            var shells = new List<Transform>();
            var mid = Vector3.zero;
            foreach (var f in carried)
            {
                var at = f.Root.position + Vector3.up * NoiseLook.Height;
                shells.Add(effects.Freeze(at, Frozen, charged ? 1.2f : 1f));
                f.Frozen = true;
                Frost(f, true);
                mid += at;
            }
            // Cut in to them as they freeze up in the air, held a moment, the ice and its bursting near enough to see
            // (the whirl brings one round toward the party, so not too near); back out to the whole field once the
            // last has burst.
            if (carried.Count > 0) Cut(new Shot(centre + new Vector3(-2.4f, 1.75f, -5.7f), mid / carried.Count, -8f, 58f, 0.02f));
            Freeze(0.1f);
            frozen?.Invoke();
            Shake(0.12f, 0.3f);
            Punch(5f, 0.35f);
            await PacedWait(0.42f);
            for (var i = 0; i < carried.Count; i++)
            {
                if (this == null) return;
                Break(carried[i], shells[i], StopShard);
                Settle(carried[i]).Forget();
                await PacedWait(0.1f);
            }
            if (this != null) Move(wide, 0.5f);
        }

        // The ice round a frozen noise body bursting and the noise body free again, for the hit, held `stop`.
        void Break(Foe f, Transform shell, float stop)
        {
            Freeze(stop);
            effects.Shatter(shell, Frozen);
            if (shell != null) seaLens.Wave(shell.position);
            Frost(f, false);
            f.Frozen = false;
            f.Heat = 1f;
            Shake(0.1f, 0.25f);
            Punch(5f, 0.3f);
        }

        // A noise body's skin frozen in its ice (NoiseLook.Frost) over 0.12 s, its clock already held (Frozen); or
        // let go at once. Only Break lets a freeze go, before the hit that could knock it down or end it; the skin
        // goes with the noise body when the field is taken down.
        void Frost(Foe f, bool on)
        {
            if (f.Root == null) return;
            if (!on)
            {
                f.Look.Frost = 0f;
                return;
            }
            Anim.Tween(0.12f, t =>
            {
                if (f.Frozen) f.Look.Frost = t;
            }, Ease.Linear, () => f.Root == null || !f.Frozen).Forget();
        }

        // A noise body the whirlpool carried off back where it stood, set down and turned to face the party.
        async Awaitable Settle(Foe f)
        {
            var at = f.At;
            var spin = f.Spin;
            var lift = f.Lift;
            var facing = Mathf.Round(spin / 360f) * 360f;
            await Paced(0.35f, t =>
            {
                f.At = Vector3.Lerp(at, f.Home, t);
                f.Spin = Mathf.Lerp(spin, facing, t);
                f.Lift = Mathf.Lerp(lift, 0f, t);
            });
            f.Spin = 0f;
        }

        // A 冰镇汽水 (or a 辣条, alight) thrown overarm at the noise body, in the hand from the start of the throw (the
        // player's left, the phone staying in the right): it arcs over, turning, and bursts on it in foam and frost
        // (or a ball of flame), held a moment unless it `missed`. Done as it bursts, for the hit.
        public async Awaitable Toss(int member, int foe, bool fire, bool missed = false)
        {
            var m = party[member];
            var color = fire ? Flame : Frozen;
            var left = m.Figure.CarriesPhone;
            Move(Beside(member), 0.4f);
            m.Figure.Play(left ? "ThrowLeft" : "Throw", ThrowPace, fadeIn: 0.1f);
            var target = FoePoint(foe);
            foes[foe].LastStrike = m.Home;
            var hand = m.Figure.Animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            await effects.Toss(fire, hand, ThrowRelease, target, color, 0.5f);
            if (this == null) return;
            if (!missed) Freeze(StopToss);
            if (fire) effects.Blaze(target, color);
            else effects.Fizz(target, color);
            foes[foe].Heat = 1f;
            Shake(0.08f, 0.25f, BlowDirection(foes[foe]));
            Punch(3f, 0.25f);
        }

        // 防御: braced where they stand, a shield of cells of light raised between them and the noise bodies and
        // held there until their next turn (Unguard), flaring where a blow lands on it. Not from the floor.
        public async Awaitable Guard(int member, bool lens = false)
        {
            var m = party[member];
            if (m.Fall > 0f || m.Knocked) return;
            Move(Aside(member), 0.35f);
            m.Figure.Act("Guard", true);
            await PacedWait(0.2f);
            if (this == null) return;
            var at = MemberPoint(member) + Vector3.forward * 0.75f;
            var color = lens ? Lens : Sea;
            m.Shield?.Release();
            m.Shield = effects.Barrier(at, Vector3.forward, color);
            effects.Flash(at, color, 1.2f, 0.25f);
            await PacedWait(GuardSeen - 0.2f);
        }

        // Out of the guard (its pose let go) and the shield down. Knocked down or fainted since, the motion held is
        // the one they lie in, not the guard's, and stays: they get up with Rise, or not at all.
        public void Unguard(int member)
        {
            var m = party[member];
            if (m.Shield == null) return;
            if (!m.Knocked && m.Fall <= 0f) m.Figure.Release();
            m.Shield.Release();
            m.Shield = null;
        }

        // 道具: the 白米饭 held up as a rice ball (eaten, by DeepSeek), gone in a flash as the one it is for is lit
        // green, the hands coming down as it goes; done then. The player holds it up overhead, so their shot is
        // raised to keep it under the skill bar.
        public async Awaitable UseItem(int member, int target)
        {
            var m = party[member];
            Move(Aside(member, FaceRound, m.Figure.CarriesPhone), 0.35f);
            m.Mood = "smile";
            m.Figure.Act("Item", true);
            m.Figure.HoldRiceBall(ItemUsed + 0.1f);
            await PacedWait(ItemUsed);
            if (this == null) return;
            Mood(m, "smile", m.Figure.Release()).Forget();
            effects.Flash(m.Figure.RiceBallAt, Color.white, 0.6f, 0.25f);
            effects.Ring(party[target].Root.position + Vector3.up * 0.05f, Vector3.up, Heal, 1.1f, 0.6f);
            effects.Sparks(Body(target), Heal, 18, 2.5f);
        }

        // 深度思考: DeepSeek stops to think, eyes shut, rings of cold light spreading at her feet.
        public async Awaitable Think(int member)
        {
            var m = party[member];
            Move(Aside(member, FaceRound), 0.4f);
            Mood(m, "think", m.Figure.Act("Think")).Forget();
            await PacedWait(0.35f);
            if (this == null) return;
            effects.Ring(m.Home + Vector3.up * 0.05f, Vector3.up, Frozen, 1.2f, 0.7f);
            effects.Thoughts(MemberPoint(member) + Vector3.up * 0.75f, Frozen, 1.4f);
            await PacedWait(ThinkSeen - 0.35f);
            if (this == null) return;
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

        // The 集成's black and white (SeaCommon.cginc SeaInk): how far the frame is inked, eased toward `to` over
        // `seconds`. The party's and the noise bodies' renderers say what they are (Role), the field is whatever says
        // nothing. ApplySea clears the global every frame, so it is gone with the stage.
        static readonly int InkId = Shader.PropertyToID("_SeaInk"), RoleId = Shader.PropertyToID("_InkRole");
        float ink, inkTo, inkRate;
        MaterialPropertyBlock roleBlock;

        public void Ink(float to, float seconds)
        {
            inkTo = to;
            inkRate = seconds > 0f ? Mathf.Abs(to - ink) / seconds : 1000f;
            if (seconds <= 0f) ink = to;
        }

        void Role(Transform root, float role)
        {
            roleBlock ??= new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                r.GetPropertyBlock(roleBlock);
                roleBlock.SetFloat(RoleId, role);
                r.SetPropertyBlock(roleBlock);
            }
        }

        void Roles(bool on)
        {
            foreach (var m in party) Role(m.Root, on ? 1f : 0f);
            foreach (var f in foes) if (f.Root != null) Role(f.Root, on ? 0.5f : 0f);
        }

        // 集成 (PV U14): the frame goes black and white, the party paper-white on an ink field, and they dash across in
        // front of the noise bodies one after another, each from its own camera, trailing light and cutting each as
        // they pass (`pass` as each dash sets off, `hit` with the noise body each blow lands on, held a moment): the
        // player sprinting by, the phone's shutter going at each of them; DeepSeek leant into her run with the 饭勺. Then a cut to the field from over the party, back in their places, and DeepSeek's whale
        // of light swims up out of the seabed and over the noise bodies, colour coming back as it passes (`strike`).
        // Ends with everyone home and the camera on the overview.
        public async Awaitable AllOut(IList<int> members, System.Action pass, System.Action strike, System.Action<int> hit = null)
        {
            var line = float.MaxValue;
            foreach (var f in foes) if (!f.Gone) line = Mathf.Min(line, f.Home.z);
            line = (line == float.MaxValue ? 6.4f : line) - 1.1f;
            // Under the 2D layer's flash they are all off the field, so nobody stands between the camera and
            // the dashes; whoever has fainted stays where they lie, under the bottom of the view. Knocked down by a
            // blow, they are up for it all the same: the lying let go out there, before their dash.
            foreach (var i in members)
            {
                var m = party[i];
                m.At = m.Was = m.Root.localPosition = new Vector3(-12f, 0f, line);
                if (!m.Knocked) continue;
                m.Knocked = false;
                m.Figure.Stop(0.15f);
            }
            Move(Dashes(0, line), 0.35f);
            await Anim.Wait(0.35f);
            if (this == null) return;
            // 减弱闪光: the change of the whole frame is slower and stops short of full black and white.
            Roles(true);
            Ink(BattleEffects.Calm ? 0.75f : 1f, BattleEffects.Calm ? 0.25f : 0.08f);
            for (var k = 0; k < 3 && members.Count > 0; k++)
            {
                var member = members[k % members.Count];
                var m = party[member];
                var dir = k % 2 == 0 ? 1f : -1f;
                Vector3 from = new Vector3(-9f * dir, 0f, line + k * 0.25f), to = new Vector3(9f * dir, 0f, line + k * 0.25f);
                if (k > 0) Cut(Dashes(k, line));
                m.At = m.Was = m.Root.localPosition = from;
                m.Glide = true; // carried: the dash's own motion does the legs
                var phone = m.Figure.CarriesPhone;
                var color = phone ? Lens : Sea;
                // Each faces the way they run, leant into it: the player sprinting by with the phone in their fist,
                // its shutter going at each noise body they pass; DeepSeek cutting as she goes.
                m.Yaw = 90f * dir;
                m.Lean = 14f;
                if (phone) m.Figure.Play("Sprint", 1.3f, fadeIn: 0.05f);
                else
                {
                    m.Figure.Wield(true);
                    m.Figure.Play("SlashCombo", 1.4f, fadeIn: 0.05f);
                }
                var trail = effects.Streak(m.Root, color, 1.1f, 0.3f);
                var blade = phone ? null : effects.Streak(m.Figure.SpoonTip, Sea, 0.16f, 0.2f, "Spoon Dash");
                if (blade != null) blade.transform.localPosition = Vector3.zero;
                pass?.Invoke();
                var cut = new HashSet<int>();
                await Paced(0.42f, t =>
                {
                    m.At = Vector3.Lerp(from, to, t);
                    for (var i = 0; i < foes.Count; i++)
                    {
                        if (foes[i].Gone || cut.Contains(i) || (m.At.x - foes[i].Home.x) * dir < 0f) continue;
                        cut.Add(i);
                        var f = foes[i];
                        var at = FoePoint(i);
                        if (phone)
                        {
                            effects.Flash(Hand(member), Color.white, 0.5f, 0.1f);
                            effects.Ring(at, (Camera.transform.position - at).normalized, Lens, 1.2f, 0.3f);
                        }
                        effects.Slash(at, color, 1.4f, Jitter(60f) + (dir > 0 ? 0f : 180f), 0.3f);
                        effects.Sparks(at, Color.white, 16, 9f);
                        f.Heat = 1f;
                        f.LastStrike = from;
                        f.Look.Rig.Kick(Local(f, new Vector3(dir, 0.6f, 0f).normalized), 0.15f, 25f, 0.2f, 0.05f, 30f * dir);
                        f.Look.Flash(0.6f);
                        Freeze(0.05f);
                        hit?.Invoke(i);
                    }
                }, Ease.Linear);
                if (this == null) return;
                // The last dash's light goes with the cut that follows it, the others' fade behind them.
                var last = k == 2 || members.Count == 0;
                if (trail != null) Destroy(trail.gameObject, last ? 0f : 0.35f);
                if (blade != null) Destroy(blade.gameObject, last ? 0f : 0.25f);
                m.Figure.Stop(0.08f);
                m.Glide = false;
                m.Lean = 0f;
                if (!phone) m.Figure.Wield(false);
            }

            // A cut, and they are back in their places under the bottom of the frame: whoever guarded is behind their
            // shield, braced again. The whale swims over an inked field, brighter for it, and colour floods back as it
            // passes over the noise bodies.
            foreach (var m in party)
            {
                m.At = m.Was = m.Root.localPosition = m.Home;
                m.Yaw = 0f;
                if (m.Shield != null && m.Fall <= 0f && !m.Knocked) m.Figure.Act("Guard", true);
            }
            Cut(Breach(line));
            await effects.Whale(new Vector3(-16f, -5f, line + 12f), new Vector3(0f, 9f, line + 1.5f), new Vector3(16f, -5f, line - 4f),
                10f, 1.6f, new Color(Sea.r * 1.4f, Sea.g * 1.4f, Sea.b * 1.4f, Sea.a), () =>
                {
                    var centre = Vector3.zero;
                    var n = 0;
                    for (var i = 0; i < foes.Count; i++)
                    {
                        if (foes[i].Gone) continue;
                        foes[i].LastStrike = new Vector3(foes[i].Home.x, -5f, foes[i].Home.z - 3f); // up out of the seabed and back
                        effects.Pillar(foes[i].Home, Sea, 0.9f, 6.5f, 1f);
                        effects.Sparks(FoePoint(i), Sea, 30, 10f, 0.12f);
                        effects.Flash(FoePoint(i), Color.white, 2f, 0.35f);
                        centre += foes[i].Home;
                        n++;
                    }
                    if (n > 0) effects.Ring(centre / n + Vector3.up * 0.05f, Vector3.up, Sea, 9f, 0.7f);
                    Shake(0.15f, 0.5f, Vector3.up);
                    Ink(0f, 0.35f);
                    strike?.Invoke();
                });
            if (this == null) return;
            Ink(0f, 0.2f);
            await Anim.Wait(0.3f);
            if (this == null) return;
            Roles(false);
            Move(Overview, 0.6f);
        }

        // The 集成's dashes, each from its own camera, all side on to the line they run along (`line`) and three
        // metres or more from it: low and square on, close enough that the dash crosses the whole frame, the noise
        // bodies lying beyond it a little under the middle; low from the other side, tipped over; high and three
        // quarters on.
        Shot Dashes(int k, float line)
        {
            switch (k)
            {
                case 1:
                    return new Shot(new Vector3(-0.8f, 0.45f, line - 3.6f), new Vector3(0f, 0.8f, line + 1f), -14f, 58f, 0.02f);
                case 2:
                    return new Shot(new Vector3(-3.2f, 2.6f, line - 3f), new Vector3(0.5f, 0.5f, line + 1.4f), 10f, 50f, 0.02f);
                default:
                    var eye = new Vector3(0.8f, 1f, line - 4.2f);
                    return new Shot(eye, Aim(eye, new Vector3(0f, 0.6f, line + 1.2f), 0.5f, 0.44f, 50f, 4f), 4f, 50f, 0.02f);
            }
        }

        // High behind the party and over their heads, they back home at the cut and out of the bottom of the
        // frame: the noise bodies lying low across the middle, over the cards, and the whale's arc over the field
        // above them.
        Shot Breach(float line)
        {
            var eye = new Vector3(-0.3f, 3.4f, -1.6f);
            return new Shot(eye, Aim(eye, new Vector3(0f, 0.4f, line + 1.1f), 0.5f, 0.28f, 56f, 0f), 0f, 56f, 0.02f);
        }

        // Jolted back by a hit, away from the noise body that came at them (straight back if none did), held a moment
        // as it lands, wincing, sparks off where it struck: the chest or the head thrown back by turns, added over the
        // pose; the pelvis pushed and the body carried a little way (not walked, so the legs keep their stance) and
        // back, turned aside from the blow, the tail flung away from it. Behind a shield, only a shudder and a little
        // push.
        public void Recoil(int member, bool knocked = false)
        {
            var m = party[member];
            Freeze(StopTaken);
            m.Wince = 0.7f;
            m.Shield?.Flare();
            Punch(3f, 0.3f);
            Shake(0.04f, 0.15f, Vector3.ProjectOnPlane(m.Home - FoeCentre, Vector3.up)); // away from the noise bodies
            effects.Sparks(Body(member), Harm, 18, 5f);
            effects.Flash(Body(member), Harm, 1.2f, 0.2f);
            if (m.Fall > 0f) return;
            var away = Vector3.back;
            if (m.Striker != null)
            {
                var from = m.At - m.Striker.localPosition;
                from.y = 0f;
                if (from.sqrMagnitude > 1e-4f) away = from.normalized;
            }
            // Turned and flung toward the side away from the blow (+x is their right).
            var aside = Mathf.Clamp(away.x * 3f, -1f, 1f);
            if (knocked && !m.Knocked)
            {
                m.Figure.Flick(18f * aside);
                Knock(m);
                return;
            }
            var guarded = m.Shield != null;
            var hit = ++m.Hits;
            m.Figure.Flinch(hit % 2 == 1 ? "HitChest" : "HitHead", FlinchPace, guarded ? 0.35f : 1f);
            if (m.Knocked) return;
            if (!guarded)
            {
                m.Figure.Nudge(transform.TransformDirection(away) * 0.06f, 0.06f, 0.32f);
                m.Figure.Flick(18f * aside);
            }
            var turn = guarded ? 0f : 6f * aside;
            // Out sharply, a beat, and eased back; the turn back sooner. A later blow takes over.
            Paced(0.41f, t =>
            {
                if (m.Hits != hit) return;
                var s = t * 0.41f;
                var push = s < 0.06f ? Ease.OutCubic(s / 0.06f) : s < 0.09f ? 1f : 0.5f + 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01((s - 0.09f) / 0.32f));
                m.Jolt = away * (0.04f * push);
                m.YawJolt = turn * (s < 0.06f ? push : 1f - Mathf.SmoothStep(0f, 1f, (s - 0.06f) / 0.35f));
            }, Ease.Linear).Forget();
        }

        // Down at 0 HP, falling back onto the seabed and lying there; up again when healed.
        public void Faint(int member, bool down)
        {
            var m = party[member];
            var from = m.Fall;
            if (down)
            {
                Unguard(member);
                Charged(member, false);
                // Already lying where a blow knocked them, they stay there.
                if (!m.Knocked)
                {
                    m.Figure.Play("Faint", FaintPace, fadeIn: 0.1f);
                    m.Figure.HoldEnd();
                }
                m.Knocked = false;
            }
            else m.Figure.Play("GetUp", GetUpPace, fadeIn: 0.3f);
            Paced(0.35f, t => m.Fall = Mathf.Lerp(from, down ? 1f : 0f, t)).Forget();
        }

        // Knocked off their feet by the blow: thrown onto their back on the seabed (the motion carries them some
        // 40 cm back, and the get-up as far forward again) and kept lying there until their turn (Rise).
        void Knock(Member m)
        {
            m.Knocked = true;
            m.Hits++; // an earlier blow's push gives way
            m.Figure.Play("Knockback", KnockPace, fadeIn: 0.1f);
            m.Figure.HoldEnd();
            var jolt = m.Jolt;
            var turn = m.YawJolt;
            Paced(0.3f, t =>
            {
                m.Jolt = jolt * (1f - t);
                m.YawJolt = turn * (1f - t);
            }).Forget();
        }

        // Up off the seabed at the start of their turn, if a blow knocked them down (BattleScreen.PartyTurn).
        public void Rise(int member)
        {
            var m = party[member];
            if (!m.Knocked) return;
            m.Knocked = false;
            if (m.Fall <= 0f) m.Figure.Play("GetUp", GetUpPace, fadeIn: 0.15f);
        }

        // ------------------------------------------------------------------ noise bodies

        // Thrown back off the blow and squashed, held there a moment (its own hit-stop), lit white, its cubes
        // knocked loose and its rows torn, its eyes squeezed shut; then wobbling back.
        public void Hit(int foe)
        {
            var f = foes[foe];
            f.Heat = 1f;
            Punch(4f, 0.3f);
            var look = f.Look;
            look.Rig.Kick(Local(f, BlowDirection(f)), 0.35f, 18f, 0.22f, 0.07f);
            look.Flash(1f);
            look.Tear(0.22f, 0.25f);
            look.Loosen(1f, 0.15f);
            look.SetMood(NoiseMood.Hurt, 0.45f);
        }

        // A blow on a weakness or a critical, lit in the colour of what struck it: thrown further and turned, held
        // longer (the field too, on top of the blow's own hold), flashing that colour and its cells taken into it.
        public void Impact(int foe, SystemPrompt.Battle.Element element, bool critical)
        {
            Freeze(hold + StopWeak);
            var color = element switch
            {
                SystemPrompt.Battle.Element.Ice => Frozen,
                SystemPrompt.Battle.Element.Fire => Flame,
                _ => critical ? Harm : Lens,
            };
            var f = foes[foe];
            effects.Impact(FoePoint(foe), color, critical);
            seaLens.Wave(FoePoint(foe));
            Punch(6f, 0.35f);
            Shake(0.1f, 0.25f, BlowDirection(f));
            f.Look.Rig.Kick(Local(f, BlowDirection(f)), 0.3f, 22f, 0.16f, 0.12f, Jitter(25f));
            // The colour itself, only a little paled, its brightest channel just over 1: the flash lays one flat colour
            // over the body and stays near full through the hit-stop, and the lens rolls every channel near 1 off to
            // white, so a paler or brighter one (ice above all, pale already) burns the body out to a white shape, the
            // look of a plain blow's flash (Hit).
            var lit = Color.Lerp(color, Color.white, 0.2f);
            f.Look.Flash(1f, lit * (1.1f / lit.maxColorComponent));
            f.Look.Loosen(0.8f, 0.2f);
            f.Look.Tinge(color, 0.6f);
        }

        // Knocked over onto its side and bouncing where it lands, dust ringing out; or (`down` false) getting up:
        // the spinner over it popping, a hop up off the seabed drawn up tall, a landing squash and a shake.
        public void Down(int foe, bool down)
        {
            var f = foes[foe];
            f.Look.Restore(); // lying without what it threw would read as a part knocked off
            if (down) Fall().Forget();
            else Rise().Forget();

            async Awaitable Fall()
            {
                var from = f.Down;
                await Paced(0.3f, t => f.Down = Mathf.Lerp(from, 1f, t), Ease.OutBack);
                if (f.Root == null || f.Dying || f.Gone) return;
                f.Look.Rig.Kick(Local(f, Vector3.up), 0.18f, 0f, 0.25f);
                effects.Ring(f.Home + Vector3.up * 0.05f, Vector3.up, f.Look.Tint * 0.8f, 1.4f, 0.4f);
            }

            async Awaitable Rise()
            {
                var look = f.Look;
                look.SetMood(NoiseMood.Angry, 1.15f);
                effects.Pixels(look.Crown, look.Tint, look.TintB, 12, 2.5f);
                look.Rig.Hold(0.2f);
                await PacedWait(0.12f);
                if (f.Root == null) return;
                var from = f.Down;
                look.Rig.Hold(-0.2f);
                await Paced(0.2f, t =>
                {
                    f.Down = Mathf.Lerp(from, 0f, Ease.InOutCubic(t));
                    f.Lift = 0.45f * Mathf.Sin(t * Mathf.PI);
                }, Ease.Linear);
                if (f.Root == null) return;
                f.Lift = 0f;
                look.Rig.Hold(0f);
                look.Rig.Kick(Vector3.down, 0.05f, 0f, 0.15f);
                look.Rig.Kick(Vector3.zero, 0f, 0f, 0f, 0f, 12f);
                await PacedWait(0.1f);
                if (f.Root != null) look.Rig.Kick(Vector3.zero, 0f, 0f, 0f, 0f, -12f);
            }
        }

        // Which way the last blow on it sent it, in the world: away from where it came from (the party, when that
        // isn't known), flat, but up as well when it came from under the seabed (the 集成's whale).
        Vector3 BlowDirection(Foe f)
        {
            var from = float.IsInfinity(f.LastStrike.x) ? PartyCentre : f.LastStrike;
            var d = f.Home - from;
            d.y = Mathf.Max(0f, d.y) * 0.5f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        // A direction in the world as the noise body's rig takes it, turned with it (it faces the party).
        static Vector3 Local(Foe f, Vector3 world) => f.Root.InverseTransformDirection(world);

        // The noise body under the cursor (-1 none): it looks up, wary, flinching back once as it is picked.
        public void Pick(int foe)
        {
            for (var i = 0; i < foes.Count; i++)
            {
                var f = foes[i];
                var picked = i == foe;
                if (picked && !f.Picked && !f.Gone) f.Look.Rig.Kick(Vector3.back, 0.05f, 4f, 0.06f);
                f.Picked = picked;
            }
        }

        // Any noise body still bursting (Vanish), or the battle's last blow not yet seen (Finish): the win waits for
        // them. The last blow's own burst plays on under the win's camera.
        public bool Dying
        {
            get
            {
                if (finishing) return true;
                foreach (var f in foes) if (f.Dying && !f.Last) return true;
                return false;
            }
        }

        // The battle's last blow (BattleScreen: one of the party's own that leaves no noise body standing, not the
        // 集成's): the field held a long moment on it, seen first where it was struck, then from low between the
        // party and the noise body, off the side away from the one who struck (`attacker`), pushing slowly in as it
        // is deleted (Vanish's `last`). Done once it has burst, its cubes still flying: the win's camera takes over.
        public async Awaitable Finish(int foe, int attacker, Rect? stamp = null)
        {
            finishing = true;
            Freeze(StopKill);
            Vanish(foe, stamp, true).Forget();
            await Anim.Wait(KillCut);
            if (this == null) return;
            var shot = KillShot(foe, attacker);
            Cut(shot);
            Move(Pushed(shot, 0.35f), 0.9f, Ease.OutCubic, true);
            await Anim.Wait(KillSeen);
            if (this != null) finishing = false;
        }

        // Low, between the party and the noise body and looking at it, a longish lens, four metres off it and out to
        // the side away from the one who struck it, so they are behind the camera or off its side and seen from behind
        // if at all; back toward the party until it stands clear of all of them by the usual 2.2 m.
        Shot KillShot(int foe, int attacker)
        {
            var f = foes[foe].Home;
            var back = PartyCentre - f;
            back.y = 0f;
            back.Normalize();
            var off = new Vector3(back.z, 0f, -back.x);
            var a = party[Mathf.Clamp(attacker, 0, party.Count - 1)].At - f;
            if (Vector3.Dot(a, off) > 0f) off = -off;
            var eye = f + back * 4.2f + off * 1.4f + Vector3.up * 0.7f;
            for (var k = 0; k < 8 && party.Exists(m => Vector3.Distance(m.At, eye) < 2.2f); k++) eye += back * 0.3f;
            var roll = 8f * Mathf.Sign(Vector3.Dot(off, Vector3.right) + 0.001f);
            // Wide enough, and down far enough on the seabed, to keep its cubes in view as they fly and land.
            return new Shot(eye, FoePoint(foe) + Vector3.down * 0.4f, roll, 44f, 0.01f);
        }

        // The shot `metres` nearer what it looks at, through a lens `fov` degrees high (its own unless given).
        static Shot Pushed(Shot s, float metres, float fov = 0f) =>
            new Shot(s.Position + (s.Look - s.Position).normalized * metres, s.Look, s.Roll, fov > 0f ? fov : s.Fov, s.Drift, s.Spin, s.Pivot);

        // Deleted: a last blow holding it, lit white, its eyes flickering out; torn, it bursts in its own pixels and
        // a flash of light as a window pops up over it 「已删除」 (「已辟谣」 for a 冷知识型; slid aside, off the stamp up
        // over the blow if it would open under it, `stamp` in the viewport); its cubes fly apart and tumble onto the seabed, going
        // dark, its loose parts flung off, and it is gone. The battle's `last` (Finish) bursts slower, a glint and a
        // thin column of its light standing up out of it.
        public async Awaitable Vanish(int foe, Rect? stamp = null, bool last = false)
        {
            var f = foes[foe];
            if (f.Dying || f.Gone) return;
            f.Dying = true;
            f.Last = last;
            f.Heat = 1f;
            var look = f.Look;
            var dir = BlowDirection(f);
            look.Restore(); // back for its debris to fling
            look.SetMood(NoiseMood.Dying, 0f);
            look.Rig.Kick(Local(f, dir), 0.25f, 20f, 0.3f, 0.08f);
            look.Flash(1f);
            await PacedWait(0.08f);
            if (f.Root == null) return; // the field went while it burst
            var at = FoePoint(foe);
            look.Tear(0.35f, 0.15f);
            effects.Crash(at, look.Tint, look.TintB);
            if (last)
            {
                effects.Star(at, look.Tint, 3f);
                effects.Pillar(f.Home, look.Tint, 0.25f, 5f, 0.6f);
            }
            effects.Notice(at, look.Kind == "coldfact" ? "已辟谣" : "已删除", stamp);
            Shake(0.06f, 0.2f, dir);
            await PacedWait(0.12f);
            if (f.Root == null) return;
            look.BurstFrom(dir, 1f);
            look.Debris(transform, dir);
            await Paced(last ? 0.75f : 0.55f, t =>
            {
                if (f.Root == null) return;
                look.Burst = t;
                f.Burst = t;
            }, t => 1f - (1f - t) * (1f - t)); // fast out, slowing as they fall
            if (f.Root == null) return;
            f.Gone = true;
            f.Dying = false;
            f.Root.gameObject.SetActive(false);
            f.Shadow.gameObject.SetActive(false);
        }

        // 转发型 calls in another noise body (转发到十个群): ten of its own forked arrows shot off it, arcing over to the
        // free place on the line, where the new one comes together out of the pixels as they pour in, then shows
        // itself (Roar) in a column of light. Added after the others, so every index stays; the new one is in the
        // list as soon as this is called, whole once it is done.
        public async Awaitable Summon(string kind, int caller)
        {
            var home = FreePlace();
            var root = Parts.Node("Noise " + foes.Count, transform, home);
            root.rotation = Quaternion.Euler(0f, 180f, 0f);
            var f = new Foe { Root = root, Look = new NoiseLook(kind, root, foes.Count * 2.3f, true), Home = home, At = home, Heat = 1f,
                Shadow = SeaArt.Shadow(transform, NoiseLook.ShadowSize).transform };
            f.Look.Unform(); // in the frame it is made, before anything of it is drawn
            foes.Add(f);
            var c = foes[caller];
            var look = c.Look;
            var tint = look.Tint;
            c.Heat = 1f;
            look.SetMood(NoiseMood.Angry, 1f);
            Unbrace(c, false);
            var braced = c.At;
            Paced(0.38f, t => c.At = Vector3.LerpUnclamped(braced, c.Home, t), Ease.OutBack).Forget();
            effects.Flash(FoePoint(caller), tint, 1.4f, 0.25f);
            f.Look.Assemble(0.4f); // as the first land
            await effects.Relay(k =>
            {
                if (k < 3) look.Rig.Kick(Vector3.down, 0.12f, 0f, 0.1f);
                return look.Kind == "repost" ? look.Throw(k) : look.Face();
            }, home + Vector3.up * NoiseLook.Height, 10, tint);
            look.Unwind();
            while (this != null && !f.Look.Whole) await Awaitable.NextFrameAsync();
            if (this == null) return;
            effects.Pillar(home, tint, 0.8f, 4f, 0.7f);
            effects.Pixels(home + Vector3.up * NoiseLook.Height, tint, f.Look.Tint, 36, 4f);
            Shake(0.05f, 0.2f);
            Roar(f, false).Forget();
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

        // Where a member looks this frame (SeaFigure.LookAt): nowhere while down. The glances 5 to 8 s apart, varied
        // without drawing on UnityEngine.Random, which the damage rolls draw on.
        void Watch(Member m, float dt)
        {
            var me = party.IndexOf(m);
            m.Threatened -= dt;
            m.Glance -= dt;
            if (m.Glance < -GlanceSeen) m.Glance = 5f + 3f * Mathf.Repeat(time * 0.618f + me * 0.37f, 1f);
            if (m.Fall > 0f)
            {
                m.Figure.LookAt(null);
                return;
            }
            if (m.Threatened > 0f && m.Striker != null)
            {
                m.Figure.LookAt(m.Striker.position + Vector3.up * NoiseLook.Height, true);
                return;
            }
            if (actor >= 0 && actor != me && m.Glance < 0f && party[actor].Fall <= 0f)
            {
                m.Figure.LookAt(MemberPoint(actor) + Vector3.up * 0.4f, false, true);
                return;
            }
            int picked = -1, nearest = -1;
            var near = float.MaxValue;
            for (var i = 0; i < foes.Count; i++)
            {
                var f = foes[i];
                if (f.Gone || f.Dying) continue;
                if (f.Picked) picked = i;
                var d = (f.At - m.At).sqrMagnitude;
                if (d < near)
                {
                    near = d;
                    nearest = i;
                }
            }
            if (picked >= 0) m.Figure.LookAt(FoePoint(picked));
            else m.Figure.LookAt(nearest >= 0 ? FoePoint(nearest) : (Vector3?)null, false, true);
        }

        // Their turn come round (BattleScreen.PartyTurn; -1 when it is over): done watching what last came at them, a
        // breath gathered (a deeper one for ONE MORE!), DeepSeek's tail flicked.
        public void Ready(int member, bool oneMore = false)
        {
            actor = member;
            if (member < 0) return;
            var m = party[member];
            m.Threatened = 0f;
            m.Striker = null; // a blow after this that nothing came at them for is taken from ahead
            if (m.Fall > 0f) return;
            var k = oneMore ? 1.5f : 1f;
            m.Figure.Nudge(Vector3.down * (0.02f * k), 0.15f, 0.35f);
            m.Figure.Flick(8f * k);
        }

        // A noise body coming at a member (or all of them): they watch it (Update), flinch a little as it picks them
        // out, and are pushed away from it by the blow (Recoil).
        void Threaten(int foe, int member)
        {
            for (var i = 0; i < party.Count; i++)
            {
                if (member >= 0 && i != member) continue;
                var m = party[i];
                m.Striker = foes[foe].Root;
                m.Threatened = ThreatSeen;
                if (m.Fall <= 0f) m.Figure.Nudge(-m.Root.forward * 0.02f - Vector3.up * 0.025f, 0.12f, 0.3f);
            }
        }

        // Its attack coming (BattleScreen.EnemyTurn and Call start it as its skill's name comes up, not waited for: it
        // lives inside the name's time): it backs off from whoever it goes for (member -1: the whole party, or nobody
        // for a call), crouched and leant back, heating up and shaking, turning to them, what it throws drawn out
        // ready (NoiseLook.Windup); held there till the attack lets it go (Lunge, Forward, Summon), and back where it
        // stood if none comes.
        public void Brace(int foe, int member = -1)
        {
            var f = foes[foe];
            if (f.Gone || f.Dying || f.Down > 0.5f) return;
            Facing(member);
            var target = member >= 0 ? party[member].Home : PartyCentre;
            var away = f.Home - target;
            away.y = 0f;
            away.Normalize();
            // The one it goes for itself, all the way round to them: the others only glance (Facing).
            if (member >= 0) f.TurnWant = Mathf.Clamp(Yaw(f, target), -40f, 40f);
            var brace = ++f.Braces;
            f.Bracing = true;
            var look = f.Look;
            look.SetMood(NoiseMood.Angry, 1.6f);
            look.Windup(-away);
            look.Rig.Hold(0.14f, -12f);
            look.Rig.Tremble = 0.02f;
            Hold().Forget();

            async Awaitable Hold()
            {
                Vector3 from = f.At, back = f.Home + away * 0.35f;
                var start = paced;
                while (f.Braces == brace)
                {
                    var t = Mathf.Clamp01((paced - start) / 0.3f);
                    f.At = Vector3.Lerp(from, back, Ease.OutCubic(t));
                    f.Heat = Mathf.Max(f.Heat, t);
                    if (paced - start > 1.6f)
                    {
                        Unbrace(f);
                        await Paced(0.3f, u => f.At = Vector3.Lerp(back, f.Home, u));
                        return;
                    }
                    await Awaitable.NextFrameAsync();
                    if (this == null || f.Root == null) return;
                }
            }
        }

        // The brace let go, by its attack or by none coming: upright and still, its pose given back unless the attack
        // still has to throw what it drew out (it unwinds once it has). The attack moves it back home itself.
        void Unbrace(Foe f, bool unwind = true)
        {
            f.Braces++;
            f.Bracing = false;
            f.Look.Rig.Tremble = 0f;
            f.Look.Rig.Hold(0f);
            if (unwind) f.Look.Unwind();
        }

        // The noise bodies standing watch `member` (whoever's turn it is, whoever one goes for): each turned toward them
        // a little, never more than 20°; -1 straight at the party.
        public void Facing(int member)
        {
            foreach (var f in foes)
            {
                if (f.Gone || f.Dying || f.Down > 0.5f) continue;
                f.TurnWant = member >= 0 ? Mathf.Clamp(Yaw(f, party[member].Home), -20f, 20f) : 0f;
            }
        }

        // Degrees a noise body turns from facing straight at the party's line to face `at`.
        static float Yaw(Foe f, Vector3 at)
        {
            var d = at - f.Home;
            return Mathf.DeltaAngle(0f, Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg);
        }

        // Comes at the party (or the member it picked) from where it braced: a dash drawn out long at them, its
        // front squashed in on the contact, and back where it stands, overshooting a little, its pose given back.
        // Done at the contact.
        public async Awaitable Lunge(int foe, int member = -1)
        {
            var f = foes[foe];
            Threaten(foe, member);
            f.Heat = 1f;
            var look = f.Look;
            look.SetMood(NoiseMood.Angry, 0.9f);
            var target = member >= 0 ? party[member].Home : PartyCentre;
            var to = target - f.Home;
            to.y = 0f;
            var reach = f.Home + to.normalized * Mathf.Max(0f, to.magnitude - 1.6f);
            var from = f.At;
            Unbrace(f, false);
            look.Rig.Hold(0f, 0f, 0.35f);
            await Paced(0.16f, t => f.At = Vector3.Lerp(from, reach, t), t => t * t);
            if (f.Root == null) return;
            look.Rig.Hold(0f);
            look.Rig.Kick(Vector3.forward, 0.1f, 10f, 0f, 0.05f, 0f, -0.3f);
            Back().Forget();

            async Awaitable Back()
            {
                await Paced(0.38f, t => f.At = Vector3.LerpUnclamped(reach, f.Home, t), Ease.OutBack);
                look.Unwind();
            }
        }

        // 标题党型's 震惊！: it comes at the member and flings its pop-ups at them one after another as it goes, the
        // last out of its mouth (another kind, from its face); done as the last hits.
        public async Awaitable Bait(int foe, int member)
        {
            var look = foes[foe].Look;
            Lunge(foe, member).Forget();
            await PacedWait(0.06f);
            if (this == null) return;
            await effects.Popups(look.Kind == "clickbait" ? look.Throw : _ => look.Face(), Body(member), 4, look.Tint, Palette.Hex("#FFD23F"));
        }

        // 冷知识型's 你知道吗？: it comes at the member and deals the cards off its stack at them; done as the last hits.
        public async Awaitable Lecture(int foe, int member)
        {
            var look = foes[foe].Look;
            Lunge(foe, member).Forget();
            await PacedWait(0.06f);
            if (this == null) return;
            await effects.Cards(look.Kind == "coldfact" ? look.Throw : _ => look.Face(), Body(member), 3, look.Tint, Palette.Hex("#BFE9FF"));
        }

        // 转发型's 转发保平安: three of its arrows shot off it one after another, each sending it down a little as it
        // goes, that split and come down on the whole party, the camera back to take in the field and the height
        // over it; it settles back where it stands as it shoots. Done when all have landed.
        public async Awaitable Forward(int foe, IList<int> members)
        {
            var f = foes[foe];
            foreach (var i in members) Threaten(foe, i);
            f.Heat = 1f;
            var look = f.Look;
            look.SetMood(NoiseMood.Angry, 1.4f);
            Move(Skyward, 0.35f);
            await Anim.Wait(0.2f);
            if (this == null) return;
            var targets = new List<Vector3>();
            foreach (var i in members) targets.Add(Body(i));
            Unbrace(f, false);
            var from = f.At;
            Paced(0.38f, t => f.At = Vector3.LerpUnclamped(from, f.Home, t), Ease.OutBack).Forget();
            await effects.Forward(FoePoint(foe) + Vector3.up * 0.4f, k =>
            {
                look.Rig.Kick(Vector3.down, 0.25f, 0f, 0.2f);
                return look.Kind == "repost" ? look.Throw(k) : look.Face();
            }, targets, look.Tint);
            look.Unwind();
            if (this == null) return;
            // Down behind them as they land, to see them take it: behind the one, or the whole line.
            if (members.Count == 1) Move(Threat(foe, members[0]), 0.25f);
            else if (members.Count > 1) Move(Struck, 0.25f);
            Shake(0.1f, 0.3f, Vector3.down);
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
            // Over where they stand and face: pushed by a blow and back (not walked, so the legs don't step), leant
            // forward (degrees, into a sprint) and turned by it; gliding, carried without stepping; where At was last
            // frame, the speed the legs are blended by.
            public Vector3 Jolt, Was;
            public float Lean, YawJolt;
            public bool Glide;
            public Transform Striker;  // the last noise body to come at them
            public float Threatened;   // seconds left of watching it
            public float Glance;       // seconds to the next glance at whoever's turn it is, below 0 while glancing
            public bool Knocked;       // lying where a blow knocked them, till their turn (Rise)
            public float Laid;         // how far the shadow has gone from under the feet to under the lying body
            public bool Landed;        // the fall has reached the seabed (its dust has gone up)
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
            public float Heat, Down;
            public float Spin, Lift; // carried round in 深海潜流's whirlpool
            public bool Gone, Frozen;
            public bool Dying, Picked; // bursting (Vanish); under the cursor (Pick)
            public bool Last;          // the battle's last, bursting on under the win (Finish)
            public float Burst;        // how far it has burst, 0 to 1
            public bool Bracing;       // wound up for its attack (Brace), till the attack lets it go
            public int Braces;         // how many braces and lettings go there have been, so a brace's hold knows it is over
            public float Turn, TurnWant; // degrees it is turned off facing the party, toward who it watches (Facing)
            public Vector3 LastStrike = Vector3.positiveInfinity; // where the last blow on it came from, in the world
            public Transform Shadow;
        }
    }
}
