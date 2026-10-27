using System.Collections.Generic;
using SystemPrompt.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // A noise body's name plate, HP, weakness tag and the words it shouts (标题党型 「震惊！」, 转发型
    // 「转发保平安」), pinned by BattleScreen over its 3D body on the field (Dungeon/BattleStage.cs; the body is
    // NoiseLook in Dungeon/SeaActors.cs). The words' box is also what the pointer aims at.
    public sealed class EnemyView
    {
        public readonly Combatant Unit;
        public readonly VisualElement Root;
        readonly VisualElement bodyBox;
        readonly Shape plate;
        readonly VisualElement hpFill;
        readonly Label weakness;
        readonly VisualElement weakTag;
        readonly VisualElement cursor;
        readonly VisualElement downTag;
        readonly Label[] words;
        bool frozen;

        public Vector2 Center = new Vector2(960, 480); // the body's centre in the battle frame, where effects aim

        public EnemyView(Combatant unit, VisualElement parent)
        {
            Unit = unit;
            Root = Ui.El("sp-enemy", parent);
            Root.style.visibility = Visibility.Hidden; // until it is first placed

            var plateBox = Ui.El("sp-enemy__plate", Root);
            plate = Shape.Backdrop(plateBox, Shape.Slant(12), Palette.Ink).Shadow(new Vector2(6, 6), Palette.Green);
            Ui.Text(unit.Name, "sp-enemy__name", plateBox, FontKind.Black);
            var bar = Ui.El("sp-enemy__hp", plateBox);
            hpFill = Ui.El("sp-enemy__hp-fill", bar);
            weakTag = Ui.El("sp-enemy__weak", Root);
            Shape.Backdrop(weakTag, Shape.Slant(10), Palette.Paper).Stroke(Palette.Ink, 2);
            weakness = Ui.Text("", "sp-enemy__weak-text", weakTag, FontKind.Black);

            bodyBox = Ui.El("sp-enemy__body", Root);
            words = unit.Kind == "repost"
                ? new[] { Ui.Text("转发保平安", "sp-enemy__word", bodyBox, FontKind.Black) }
                : new[] { Ui.Text("震惊！", "sp-enemy__word", bodyBox, FontKind.Black), Ui.Text("点击领取", "sp-enemy__word is-small", bodyBox, FontKind.Black) };

            downTag = Ui.Text("DOWN", "sp-enemy__down", Root, FontKind.Black);
            Ui.Show(downTag, false);
            cursor = new Shape(Shape.TriangleDown(), Palette.Green).Stroke(Palette.Ink, 3);
            cursor.AddToClassList("sp-enemy__cursor");
            Root.Add(cursor);
            Ui.Show(cursor, false);

            Root.schedule.Execute(Twitch).Every(140);
            Refresh();
        }

        // With the box over the body's centre on screen, or hidden while the camera does not show the body.
        public void Place(Vector2 center, bool visible)
        {
            Root.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (!visible) return;
            Center = center;
            var box = bodyBox.layout;
            Root.style.left = center.x - 140f; // half of .sp-enemy's width
            Root.style.top = center.y - (box.height > 0f ? box.center.y : 200f);
        }

        void Twitch()
        {
            if (frozen) return;
            foreach (var w in words) w.style.translate = new Translate(Random.Range(-6f, 6f), Random.Range(-4f, 4f));
        }

        public void Refresh()
        {
            hpFill.style.width = Length.Percent(100f * Unit.Hp / Mathf.Max(1, Unit.MaxHp));
            weakness.text = $"弱点：{Unit.WeaknessLabel}";
            weakTag.EnableInClassList("is-true", Unit.WeaknessLabel == Unit.TrueWeakness);
        }

        // The aimed-at plate turns paper (USS turns its name ink and scales it up).
        public void SetTargeted(bool on)
        {
            plate.Fill(on ? Palette.Paper : Palette.Ink).Shadow(new Vector2(6, 6), on ? Palette.Red : Palette.Green);
            Ui.Show(cursor, on);
            Root.EnableInClassList("is-targeted", on);
        }

        public void SetDown(bool down)
        {
            Ui.Show(downTag, down);
            frozen = down;
            // The words go over with the body.
            var from = down ? 0f : 1f;
            Anim.Tween(0.25f, t =>
            {
                var k = Mathf.Abs(from - t);
                bodyBox.style.rotate = new Rotate(new Angle(k * -62f, AngleUnit.Degree));
                bodyBox.style.translate = new Translate(k * -30f, k * 70f);
            }, Ease.OutBack).Forget();
        }

        public async Awaitable Die()
        {
            Ui.Show(cursor, false);
            await Anim.Tween(0.35f, t =>
            {
                Root.style.opacity = 1 - t;
                Ui.Zoom(bodyBox, 1f + t * 0.6f);
            });
            Ui.Show(Root, false);
        }

        // 标题党型's label lies: 火 → ？？？ (turn 1), then it shatters into the true weakness (turn 2).
        public async Awaitable Flicker(string label)
        {
            for (var i = 0; i < 4; i++)
            {
                weakTag.style.opacity = i % 2 == 0 ? 0.2f : 1f;
                await Anim.Wait(0.06f);
            }
            Unit.WeaknessLabel = label;
            weakTag.style.opacity = 1;
            Refresh();
        }

        public async Awaitable Shatter()
        {
            await Anim.Tween(0.25f, t =>
            {
                weakTag.style.translate = new Translate(Random.Range(-8f, 8f) * (1 - t), t * 40f);
                weakTag.style.opacity = 1 - t;
            }, Ease.Linear);
            Unit.WeaknessLabel = Unit.TrueWeakness;
            Refresh();
            await Anim.Tween(0.25f, t =>
            {
                weakTag.style.translate = new Translate(0, (1 - t) * -30f);
                weakTag.style.opacity = t;
                Ui.Zoom(weakTag, Mathf.Lerp(1.8f, 1f, t));
            }, Ease.OutBack);
        }
    }

    public enum FaceAct { Attack, Hit, Victory }

    // A party member's face on their card or results row that acts out an attack, a hit or a win. An act is
    // twelve drawings of the same portrait with only the expression redrawn; where one drawing eases into the
    // next, two frames made by motion interpolation go between them (Resources/Art/Portraits/Card/
    // <who>_<drawing> and <who>_<from>-<to>-1, -2; cut 554 px square around the face so a shake or punch
    // never shows an edge, flattened onto the card's colour). Only the blow of a hit cuts in. All the while the
    // face moves: an attack trembles and punches out, a hit knocks it aside and flushes it red, a win bobs,
    // sways and hops. A win keeps looping until the face leaves the screen; the others go back to the still face.
    // The act playing is also on the window's class list (is-attack, is-hit, is-victory).
    public sealed class CardFace
    {
        const float Cut = 554f; // side of a frame, in pixels of the 1024 px portrait
        const int Between = 2;  // interpolated frames between two drawings that ease into each other

        // Each step: the drawing (null for the still face), how long it takes to ease in from the one before
        // (0 cuts to it), how long it holds, and how hard it kicks the face as it comes in.
        // Attack: a frown, a glare, breath in, teeth bared, the shout bursts out and goes twice over, panting,
        // breath caught, a smirk, breath out, settled.
        static readonly (string Frame, float In, float Hold, float Kick)[] AttackSteps =
        {
            ("attack0", 0.1f, 0.04f, 0f), ("attack1", 0.1f, 0.06f, 0f), ("attack1b", 0.08f, 0.04f, 0f), ("attack1c", 0.08f, 0.08f, 0f),
            ("attack2a", 0.06f, 0.02f, 0.16f), ("attack2", 0.06f, 0.08f, 0f), ("attack2b", 0.08f, 0.12f, 0.08f), ("attack2", 0.08f, 0.04f, 0f),
            ("attack2b", 0.08f, 0.1f, 0.05f), ("attack2c", 0.1f, 0.06f, 0f), ("attack2d", 0.1f, 0.06f, 0f), ("attack3", 0.1f, 0.14f, 0f),
            ("attack3b", 0.1f, 0.1f, 0f), ("attack3c", 0.08f, 0.06f, 0f), (null, 0.1f, 0f, 0f),
        };
        // Hit: shock, a flinch, eyes shut, a yelp, knocked aside with a cry, reeling, the pain easing, one eye
        // open, both, a breath, a glare back, an exhale.
        static readonly (string, float, float, float)[] HitSteps =
        {
            ("hit0", 0f, 0.07f, 14f), ("hit0b", 0.06f, 0.04f, 0f), ("hit1", 0.06f, 0.06f, 0f), ("hit1a", 0.06f, 0.03f, 0f),
            ("hit1b", 0.06f, 0.1f, 8f), ("hit1c", 0.08f, 0.08f, 0f), ("hit1d", 0.1f, 0.08f, 0f), ("hit2", 0.1f, 0.08f, 0f),
            ("hit2b", 0.1f, 0.1f, 0f), ("hit2c", 0.1f, 0.1f, 0f), ("hit3", 0.1f, 0.16f, 0f), ("hit3b", 0.08f, 0.08f, 0f), (null, 0.1f, 0f, 0f),
        };
        // Win: a sigh of relief, eyes opening into a smile, then round and round: a laugh, ha-ha, a grin, proud
        // with a blink, a wink, a beam, a chuckle.
        static readonly (string, float, float, float)[] VictoryIntro =
        {
            (null, 0f, 0.12f, 0f), ("victory0", 0.12f, 0.1f, 0f), ("victory0b", 0.1f, 0.08f, 0f), ("victory1", 0.1f, 0.1f, 0f),
            ("victory1b", 0.1f, 0.08f, 0f),
        };
        static readonly (string, float, float, float)[] VictoryLoop =
        {
            ("victory2b", 0.08f, 0.1f, 8f), ("victory2c", 0.06f, 0.08f, 0f), ("victory2b", 0.06f, 0.08f, 4f), ("victory2c", 0.06f, 0.1f, 0f),
            ("victory1b", 0.1f, 0.08f, 0f), ("victory3", 0.12f, 0.3f, 0f), ("victory3b", 0.06f, 0.06f, 0f), ("victory3", 0.06f, 0.25f, 0f),
            ("victory2a", 0.08f, 0.06f, 0f), ("victory2", 0.08f, 0.3f, 6f), ("victory4", 0.12f, 0.25f, 0f), ("victory4b", 0.1f, 0.2f, 0f),
            ("victory1b", 0.12f, 0.1f, 0f), ("victory1", 0.12f, 0.35f, 0f),
        };
        static readonly Color Hurt = new Color(1f, 0.5f, 0.5f, 1f);

        public readonly VisualElement Root;
        readonly string who;
        readonly float scale; // the motion below is in pixels of a 150 px card face
        readonly VisualElement still;
        readonly VisualElement reel;
        readonly Image image;
        readonly List<(float At, float Amount)> kicks = new List<(float, float)>();
        int playing;
        string acting, shown;
        float started;

        // DeepSeek's face, or the player's of their gender, in a size x size window like Ui.FaceCrop.
        public CardFace(string unitId, string playerGender, float size, string classes, VisualElement parent)
        {
            float zoom, faceY;
            if (unitId == "DeepSeek")
            {
                who = "deepseek";
                (zoom, faceY) = (2.4f, 0.2f);
                Root = Ui.FaceCrop("Art/Portraits/deepseek_battle", size, size, classes, parent, zoom, faceY);
            }
            else
            {
                who = playerGender == "female" ? "player_female" : "player_male";
                (zoom, faceY) = (GameData.PlayerFaceZoom, GameData.PlayerFaceY(playerGender));
                Root = Ui.FaceCrop(GameData.PlayerPortrait(playerGender), size, size, classes + " is-player", parent, zoom, faceY);
            }
            scale = size / 150f;
            still = Root.Q(className: "sp-facecrop__image");

            // The frames are centred on the face like the window, so the reel only needs to be wider than it.
            var side = size * zoom * Cut / 1024f;
            reel = Ui.El(null, Root);
            reel.pickingMode = PickingMode.Ignore;
            reel.style.position = Position.Absolute;
            reel.style.left = reel.style.top = (size - side) / 2f;
            reel.style.width = reel.style.height = side;
            image = Ui.Picture(null, ScaleMode.StretchToFill, null, reel);
            Ui.Fill(image);
            Ui.Show(reel, false);
            // Loaded now, not on the first swing: every drawing, and the frames between each two that ease.
            Walk(AttackSteps, null);
            Walk(HitSteps, null);
            Walk(VictoryLoop, Walk(VictoryLoop, Walk(VictoryIntro, null)));
        }

        string Walk((string Frame, float In, float Hold, float Kick)[] steps, string from)
        {
            foreach (var step in steps)
            {
                for (var i = 1; step.In > 0f && i <= Between; i++) Frame(Name(from, step.Frame, i));
                Frame(from = step.Frame);
            }
            return from;
        }

        Texture2D Frame(string name) => Ui.Tex($"Art/Portraits/Card/{who}_{name ?? "neutral"}");

        static string Name(string from, string to, int i) => $"{from ?? "neutral"}-{to ?? "neutral"}-{i}";

        // Starts the act over whatever is playing.
        public void Play(FaceAct act) => Run(act, ++playing).Forget();

        async Awaitable Run(FaceAct act, int id)
        {
            var done = false;
            bool Stale() => id != playing || Root.panel == null;
            if (acting != null) Root.RemoveFromClassList(acting);
            acting = act == FaceAct.Attack ? "is-attack" : act == FaceAct.Hit ? "is-hit" : "is-victory";
            Root.AddToClassList(acting);
            kicks.Clear();
            started = Time.unscaledTime;
            shown = null;
            image.image = Frame(null);
            still.style.visibility = Visibility.Hidden;
            Ui.Show(reel, true);
            Move(act, () => done || Stale()).Forget();
            await Steps(act == FaceAct.Attack ? AttackSteps : act == FaceAct.Hit ? HitSteps : VictoryIntro, Stale);
            if (act == FaceAct.Victory)
                while (!Stale()) await Steps(VictoryLoop, Stale);
            if (id != playing) return; // a newer act owns the face now
            done = true;
            Pose(act, -1f);
            Root.RemoveFromClassList(acting);
            Ui.Show(reel, false);
            still.style.visibility = Visibility.Visible;
        }

        async Awaitable Steps((string Frame, float In, float Hold, float Kick)[] steps, System.Func<bool> stale)
        {
            foreach (var (frame, ease, hold, kick) in steps)
            {
                if (stale()) return;
                if (kick > 0f) kicks.Add((Time.unscaledTime - started, kick));
                // The ease is split evenly between the frames in between and the drawing itself. Once a newer act
                // has the face, this one touches nothing more: the frame shown is the newer act's to ease from.
                var slot = ease / (Between + 1);
                for (var i = 1; ease > 0f && i <= Between; i++)
                {
                    image.image = Frame(Name(shown, frame, i));
                    await Anim.Wait(slot, stale);
                    if (stale()) return;
                }
                image.image = Frame(frame);
                shown = frame;
                await Anim.Wait(slot + hold, stale);
            }
        }

        async Awaitable Move(FaceAct act, System.Func<bool> stop)
        {
            while (!stop())
            {
                Pose(act, Time.unscaledTime - started);
                await Awaitable.NextFrameAsync();
            }
        }

        // Where the face is `time` seconds into the act (at rest before it starts): the kicks ring out on top
        // of the act's own motion.
        void Pose(FaceAct act, float time)
        {
            float zoom = 1f, x = 0f, y = 0f, turn = 0f, hurt = 0f;
            if (act == FaceAct.Victory && time >= 0f)
            {
                y -= 3f * Mathf.Sin(time * 5f);
                turn += 1.5f * Mathf.Sin(time * 2.5f);
            }
            foreach (var (at, amount) in kicks)
            {
                var k = time - at;
                if (k < 0f) continue;
                switch (act)
                {
                    case FaceAct.Attack when k < 0.3f:
                    {
                        var d = 1f - k / 0.3f;
                        zoom += amount * d * d * d;          // punches out at the viewer
                        x += 2.5f * d * Mathf.Sin(k * 95f);  // and trembles
                        break;
                    }
                    case FaceAct.Hit when k < 0.4f:
                    {
                        var d = 1f - k / 0.4f;
                        x += amount * d * Mathf.Sin(k * 70f);
                        y += amount * 0.4f * d * Mathf.Cos(k * 55f);
                        turn += amount * 0.45f * d * Mathf.Sin(k * 30f);
                        hurt = Mathf.Max(hurt, d);
                        break;
                    }
                    case FaceAct.Victory when k < 0.3f:
                        y -= amount * Mathf.Sin(k / 0.3f * Mathf.PI); // a hop
                        break;
                }
            }
            reel.style.translate = new Translate(x * scale, y * scale);
            reel.style.rotate = new Rotate(new Angle(turn, AngleUnit.Degree));
            Ui.Zoom(reel, zoom);
            image.tintColor = Color.Lerp(Color.white, Hurt, hurt);
        }
    }

    // A party member's card: the face from a 2D portrait that acts out attacks, hits and wins (CardFace), the
    // name on a paper strip, HP and SP as big numbers over their bars. The member whose turn it is stands up
    // out of the row with a green edge; low HP turns red.
    public sealed class PartyCard
    {
        public readonly Combatant Unit;
        public readonly VisualElement Root;
        public readonly CardFace Face;
        readonly Shape backdrop;
        readonly VisualElement hpFill;
        readonly VisualElement spFill;
        readonly Label hpText, hpMax;
        readonly Label spText, spMax;
        readonly Label status;
        readonly VisualElement turnTag;

        public PartyCard(Combatant unit, VisualElement parent, string playerGender)
        {
            Unit = unit;
            Root = Ui.El("sp-partycard", parent);
            backdrop = Shape.Backdrop(Root, Shape.Slant(26), Palette.Ink.WithAlpha(0.94f)).Stroke(Palette.Paper, 3).Shadow(new Vector2(10, 10), Palette.Dim);

            Face = new CardFace(unit.Id, playerGender, 150f, "sp-partycard__face", Root);

            var info = Ui.El("sp-partycard__info", Root);
            var nameTag = Ui.El("sp-partycard__nametag", info);
            Shape.Backdrop(nameTag, Shape.Slant(10), Palette.Paper);
            Ui.Text(unit.Name, "sp-partycard__name", nameTag, FontKind.Black);
            var stats = Ui.El("sp-partycard__stats", info);
            Stat(stats, "HP", "is-hp", out hpText, out hpMax, out hpFill);
            Stat(stats, "SP", "is-sp", out spText, out spMax, out spFill);

            status = Ui.Text("", "sp-partycard__status", Root, FontKind.Black);
            turnTag = Ui.El("sp-partycard__turn", Root);
            Shape.Backdrop(turnTag, Shape.Slant(10), Palette.Green);
            Ui.Text("行动中", "sp-partycard__turn-text", turnTag, FontKind.Black);
            Ui.Show(turnTag, false);
            Refresh();
        }

        static void Stat(VisualElement parent, string label, string modifier, out Label value, out Label max, out VisualElement fill)
        {
            var block = Ui.El("sp-partycard__stat " + modifier, parent);
            var numbers = Ui.El("sp-partycard__numbers", block);
            Ui.Text(label, "sp-partycard__label", numbers, FontKind.MonoBold);
            value = Ui.Text("", "sp-partycard__value", numbers, FontKind.Black);
            max = Ui.Text("", "sp-partycard__max", numbers, FontKind.MonoBold);
            var bar = Ui.El("sp-partycard__bar", block);
            fill = Ui.El("sp-partycard__fill", bar);
        }

        public void Refresh()
        {
            hpFill.style.width = Length.Percent(100f * Unit.Hp / Mathf.Max(1, Unit.MaxHp));
            spFill.style.width = Length.Percent(Unit.MaxSp == 0 ? 0 : 100f * Unit.Sp / Unit.MaxSp);
            hpText.text = Unit.Hp.ToString();
            hpMax.text = "/" + Unit.MaxHp;
            spText.text = Unit.Sp.ToString();
            spMax.text = "/" + Unit.MaxSp;
            Root.EnableInClassList("is-low", Unit.Hp > 0 && Unit.Hp * 10 <= Unit.MaxHp * 3);
            status.text = Unit.Charged ? "×2.5" : Unit.Guarding ? "防御" : "";
            Ui.Show(status, status.text.Length > 0);
        }

        public void SetActive(bool on)
        {
            Root.EnableInClassList("is-active", on);
            backdrop.Stroke(on ? Palette.Green : Palette.Paper, on ? 4 : 3).Shadow(new Vector2(10, 10), on ? Palette.Green : Palette.Dim);
            Ui.Show(turnTag, on);
            Root.style.translate = new Translate(0, on ? -26f : 0f);
        }

        public void Shake()
        {
            Anim.Tween(0.3f, t => Root.style.translate = new Translate(Random.Range(-10f, 10f) * (1 - t), Root.ClassListContains("is-active") ? -26f : 0f), Ease.Linear).Forget();
        }
    }
}
