using System;
using System.Collections.Generic;
using System.Linq;
using SystemPrompt.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Battle presentation, following the PV's battle act (pv/README.md 第六幕): screenshot viewfinder with a
    // shutter flash and the shot's thumbnail, the eye cut-in before a skill, and the 集成's title and freeze
    // frame, over the light of the fighting on the 3D field (Dungeon/BattleStage.cs, BattleEffects.cs; each
    // skill's look in design/skills.md 战斗特效).
    public sealed partial class BattleScreen
    {
        Vector2 CenterOf(VisualElement e) => Root.WorldToLocal(e.worldBound.center);

        async Awaitable Intro()
        {
            // Runs while the encounter wipe splits open; the party and enemies enter once it has cleared.
            await Anim.Wait(0.3f);
            stage?.Opening();
            game.Audio.Sfx("whoosh");
            foreach (var card in party)
            {
                var c = card.Root;
                Anim.Tween(0.25f, t => c.style.translate = new Translate((1 - t) * 700f, 0)).Forget();
            }
            foreach (var view in enemies)
            {
                game.Audio.Sfx("cursor");
                var r = view.Root;
                await Anim.Tween(0.18f, t =>
                {
                    r.style.opacity = t;
                    Ui.Zoom(r, Mathf.Lerp(0.4f, 1f, t));
                }, Ease.OutBack);
            }
            await Anim.Wait(0.3f);
        }

        async Awaitable Banner(string text)
        {
            var band = Ui.El("sp-turnbanner", fx);
            Shape.Backdrop(band, Shape.Slant(40), Palette.Ink).Shadow(new Vector2(12, 12), Palette.Red);
            Ui.Text(text, "sp-turnbanner__text", band, FontKind.Black);
            game.Audio.Sfx("whoosh", 0.7f);
            await Anim.Tween(0.2f, t => band.style.translate = new Translate((1 - t) * -2400f, 0));
            await Anim.Wait(0.45f);
            await Anim.Tween(0.18f, t => band.style.translate = new Translate(t * 2400f, 0), Ease.InCubic);
            band.RemoveFromHierarchy();
        }

        async Awaitable SkillBar(Combatant unit, string name)
        {
            var bar = Ui.El("sp-skillbar", fx);
            Shape.Backdrop(bar, Shape.Slant(22), Palette.Ink).Stroke(Palette.Green, 3).Shadow(new Vector2(8, 8), Palette.Green);
            Ui.Text(unit.Name, "sp-skillbar__who", bar, FontKind.Bold);
            Ui.Text(name, "sp-skillbar__name", bar, FontKind.Black);
            game.Audio.Sfx("confirm", 0.7f);
            await Anim.Tween(0.14f, t =>
            {
                bar.style.opacity = t;
                bar.style.translate = new Translate(0, (1 - t) * -60f);
            });
            await Anim.Wait(0.4f);
            Ui.FadeOutAndRemove(bar, 0.2f).Forget();
        }

        async Awaitable EnemyBar(EnemyView view, string name)
        {
            var bar = Ui.El("sp-skillbar is-enemy", fx);
            Shape.Backdrop(bar, Shape.Slant(22), Palette.Ink).Stroke(Palette.Red, 3).Shadow(new Vector2(8, 8), Palette.Red);
            Ui.Text(view.Unit.Name, "sp-skillbar__who", bar, FontKind.Bold);
            Ui.Text(name, "sp-skillbar__name", bar, FontKind.Black);
            await Anim.Tween(0.14f, t => bar.style.opacity = t);
            await Anim.Wait(0.35f);
            Ui.FadeOutAndRemove(bar, 0.2f).Forget();
        }

        // 截图: a first-person viewfinder locks onto the target, then the shutter flashes, on the field too.
        async Awaitable ScreenshotFx(EnemyView target, int member)
        {
            const float w = 440f, h = 340f;
            var frame = Ui.El("sp-viewfinder", fx);
            // On the target wherever the camera, coming round over the shoulder, has it.
            void Follow()
            {
                frame.style.left = target.Center.x - w / 2f;
                frame.style.top = target.Center.y - h / 2f;
            }
            Follow();
            frame.schedule.Execute(Follow).Every(0);
            frame.style.width = w;
            frame.style.height = h;
            foreach (var (fx0, fy0) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                var corner = new Shape(Corner(fx0, fy0, 10f), Palette.Green).Shadow(new Vector2(4, 4), Palette.Ink);
                corner.AddToClassList("sp-viewfinder__corner");
                corner.style.left = fx0 ? w - 70f : 0f;
                corner.style.top = fy0 ? h - 70f : 0f;
                frame.Add(corner);
            }
            Ui.El("sp-viewfinder__cross-h", frame);
            Ui.El("sp-viewfinder__cross-v", frame);
            var rec = Ui.El("sp-viewfinder__rec", frame);
            Ui.El("sp-viewfinder__dot", rec);
            Ui.Text("REC", "sp-viewfinder__rec-text", rec, FontKind.MonoBold);

            game.Audio.Sfx("cursor");
            await Anim.Tween(0.25f, t => Ui.Zoom(frame, Mathf.Lerp(1.9f, 1f, t)));
            await Anim.Wait(0.15f);
            game.Audio.Sfx("shutter");
            var photo = stage?.Photo(member, enemies.IndexOf(target), 640, 360);
            stage?.Capture(member, enemies.IndexOf(target));
            Fx.Flash(fx, Color.white, 0.22f);
            Fx.Shake(Root, 12f, 0.2f);
            if (photo != null) Thumbnail(photo).Forget();
            await Ui.FadeOutAndRemove(frame, 0.15f);
        }

        // The shot just taken, like a phone's: big in the middle of the screen, then down to the bottom left
        // corner, tilted, a moment there and away; the picture released after.
        async Awaitable Thumbnail(RenderTexture photo)
        {
            const float w = 352f, h = 198f;
            var shot = Ui.El("sp-shot", fx);
            Ui.Picture(photo, ScaleMode.ScaleAndCrop, "sp-shot__image", shot);
            try
            {
                Vector2 from = new Vector2(960f - w / 2f, 540f - h / 2f), to = new Vector2(60f, 790f);
                await Anim.Tween(0.45f, t =>
                {
                    shot.style.left = Mathf.Lerp(from.x, to.x, t);
                    shot.style.top = Mathf.Lerp(from.y, to.y, t);
                    Ui.Zoom(shot, Mathf.Lerp(2.4f, 1f, t));
                    Ui.Turn(shot, -4f * t);
                });
                await Anim.Wait(1f);
                await Anim.Tween(0.25f, t => shot.style.translate = new Translate(-520f * t, 0), Ease.InCubic);
            }
            finally
            {
                shot.RemoveFromHierarchy();
                photo.Release();
                UnityEngine.Object.Destroy(photo);
            }
        }

        // An L-shaped viewfinder corner; the flips pick which corner.
        static Func<Rect, Vector2[]> Corner(bool flipX, bool flipY, float thick) => r =>
        {
            var pts = new[]
            {
                new Vector2(0, 0), new Vector2(r.width, 0), new Vector2(r.width, thick),
                new Vector2(thick, thick), new Vector2(thick, r.height), new Vector2(0, r.height),
            };
            for (var i = 0; i < pts.Length; i++)
                pts[i] = new Vector2(r.xMin + (flipX ? r.width - pts[i].x : pts[i].x), r.yMin + (flipY ? r.height - pts[i].y : pts[i].y));
            return pts;
        };

        async Awaitable SlashFx(EnemyView target)
        {
            game.Audio.Sfx("whoosh", 0.8f);
            stage?.Slash(enemies.IndexOf(target));
            await Anim.Wait(0.18f);
        }

        // The eye cut-in band before a skill (PV U13).
        async Awaitable CutIn(Combatant unit, string skillName)
        {
            var def = GameData.Character(unit.Id);
            var band = Ui.El("sp-cutin", fx);
            Shape.Backdrop(band, Shape.Slant(60), Palette.Ink).Stroke(def?.Color ?? Palette.Green, 5);
            if (unit.Id == "DeepSeek") Ui.FaceCrop("Art/Portraits/deepseek_battle", 1300f, 190f, "sp-cutin__eyes", band, 2.1f, 0.17f);
            Ui.Text(skillName, "sp-cutin__name", band, FontKind.Black);
            game.Audio.Sfx("rise");
            await Anim.Tween(0.18f, t => band.style.translate = new Translate((1 - t) * 2400f, 0));
            await Anim.Wait(0.55f);
            await Anim.Tween(0.16f, t => band.style.translate = new Translate(t * -2400f, 0), Ease.InCubic);
            band.RemoveFromHierarchy();
        }

        // 寒流 at one noise body, 深海潜流 at all (BattleStage.Current, Maelstrom): frozen, the ice cracking as it
        // closes and again as it bursts.
        async Awaitable SpellFx(SkillDef skill, int member, List<EnemyView> views, bool charged)
        {
            game.Audio.Sfx("water", 0.8f);
            var targets = views.Where(v => v != null).ToList();
            void Frozen() => game.Audio.Sfx("crack", 0.6f);
            if (stage != null && targets.Count > 0)
            {
                if (skill.AllTargets) await stage.Maelstrom(member, targets.Select(v => enemies.IndexOf(v)).ToList(), charged, Frozen);
                else await stage.Current(member, enemies.IndexOf(targets[0]), charged, Frozen);
            }
            game.Audio.Sfx("crack");
            foreach (var view in targets) Fx.Shake(view.Root, 16f, 0.25f);
        }

        async Awaitable ThinkFx(PartyCard card)
        {
            PlaceThinkBubble(card);
            thinkLabel.text = "深度思考：下一次技能伤害 ×2.5";
            Ui.Show(thinkBubble, true);
            game.Audio.Sfx("heartbeat", 0.8f);
            await Anim.Tween(0.2f, t => Ui.Zoom(thinkBubble, Mathf.Lerp(0.6f, 1f, t)), Ease.OutBack);
            await Anim.Wait(1.1f);
            Ui.Show(thinkBubble, false);
        }

        // The noise body's attack on the field: 转发保平安's arrows down on the whole party, 震惊！'s pop-up windows
        // at the one member it picked.
        async Awaitable StrikeFx(EnemyView view, SkillDef skill, List<PartyCard> victims)
        {
            game.Audio.Sfx("glitch", 0.5f);
            if (stage == null || victims.Count == 0) return;
            var foe = enemies.IndexOf(view);
            if (skill.AllTargets) await stage.Forward(foe, victims.Select(c => party.IndexOf(c)).ToList());
            else await stage.Bait(foe, party.IndexOf(victims[0]));
        }

        void Popup(Vector2 at, string text, Color color, string tag)
        {
            var root = Ui.El("sp-dmg", fx);
            root.style.left = at.x;
            root.style.top = at.y;
            if (tag != null)
            {
                var t = Ui.Text(tag, "sp-dmg__tag", root, FontKind.Black);
                t.style.color = tag == "WEAK" ? Palette.Green : tag == "CRITICAL" ? Palette.Red : Palette.Gray;
            }
            var number = Ui.Text(text, "sp-dmg__num", root, FontKind.Black);
            number.style.color = color;
            PopupLife(root).Forget();
        }

        static async Awaitable PopupLife(VisualElement root)
        {
            await Anim.Tween(0.18f, t => Ui.Zoom(root, Mathf.Lerp(1.9f, 1f, t)), Ease.OutBack);
            await Anim.Wait(0.55f);
            await Anim.Tween(0.35f, t =>
            {
                root.style.translate = new Translate(0, -40f * t);
                root.style.opacity = 1 - t;
            });
            root.RemoveFromHierarchy();
        }

        static void DismissLater(VisualElement stamp, float seconds) => DismissAfter(stamp, seconds).Forget();

        static async Awaitable DismissAfter(VisualElement stamp, float seconds)
        {
            await Anim.Wait(seconds);
            await Fx.Dismiss(stamp);
        }

        // Waits for a click/confirm, or gives up after `seconds`.
        async Awaitable Hold(float seconds)
        {
            var s = session;
            var previous = mode;
            mode = Mode.Hint;
            advance = false;
            var t = 0f;
            while (!s.Over && !advance && t < seconds)
            {
                t += Time.unscaledDeltaTime;
                await Awaitable.NextFrameAsync();
            }
            if (mode == Mode.Hint) mode = previous;
        }

        // 集成 (PV U14): the title slams in, the party dash across the field cutting every noise body and
        // DeepSeek's whale of light swims over them (BattleStage.AllOut), then it freezes on DeepSeek with her
        // rice bowl.
        async Awaitable AllOutFx(List<Combatant> participants, int damage)
        {
            var layer = Ui.El("sp-allout", fx);
            Ui.Fill(layer);
            game.Audio.Sfx("whoosh");
            Fx.Flash(fx, Color.white);
            foreach (var card in party.Where(c => participants.Contains(c.Unit))) card.Face.Play(FaceAct.Attack);
            var title = Fx.Stamp(layer, "集成！", Palette.Paper, 150f, -8f, new Vector2(960, 170));
            game.Audio.Sfx("slam");

            void Strike()
            {
                game.Audio.Sfx("crack");
                foreach (var view in enemies.Where(v => !v.Unit.Dead))
                {
                    view.Unit.HpFloor = 0;
                    ApplyHit(view.Unit, new Hit { Damage = damage, Affinity = Affinity.Normal }).Forget();
                }
                Fx.Shake(Root, 20f, 0.4f);
            }

            if (stage != null)
            {
                var members = participants.Select(p => party.FindIndex(c => c.Unit == p)).Where(i => i >= 0).ToList();
                var whale = false;
                await stage.AllOut(members, () => game.Audio.Sfx("slap", 0.7f), () =>
                {
                    whale = true;
                    Strike();
                });
                if (!whale) Strike();
            }
            else Strike();
            await Fx.Dismiss(title);
            await Anim.Wait(0.5f);

            // The freeze frame: DeepSeek with her rice bowl, her line in a speech bubble in front of her.
            // The victory itself is announced by the results.
            var withDeepSeek = participants.Any(p => p.Id == "DeepSeek");
            finisherShown = withDeepSeek;
            if (withDeepSeek)
            {
                var finish = Ui.Picture(Ui.Tex("Art/CutIns/deepseek_finish"), ScaleMode.ScaleToFit, "sp-allout__finish", layer);
                var bubble = SpeechBubble(layer, "吃饭饭！", "sp-allout__bubble");
                bubble.style.opacity = 0;
                game.Audio.Sfx("coin");
                await Anim.Tween(0.3f, t =>
                {
                    finish.style.translate = new Translate((1 - t) * 900f, 0);
                    finish.style.opacity = t;
                }, Ease.OutBack);
                game.Audio.Sfx("slap", 0.7f);
                await Anim.Tween(0.2f, t =>
                {
                    bubble.style.opacity = Mathf.Clamp01(t * 3f);
                    Ui.Zoom(bubble, Mathf.Lerp(0.2f, 1f, t));
                }, Ease.OutBack);
            }
            await Hold(2.4f);
        }

        // A P5 speech bubble: a crooked black box with a white edge and a spike out of its lower right
        // corner toward the speaker.
        static VisualElement SpeechBubble(VisualElement parent, string text, string classes)
        {
            var bubble = Ui.El("sp-bubble " + classes, parent);
            Shape.Backdrop(bubble, r => new[]
            {
                new Vector2(r.xMin + 22f, r.yMin + 16f), new Vector2(r.xMax - 12f, r.yMin), new Vector2(r.xMax, r.yMax - 44f),
                new Vector2(r.xMax + 80f, r.yMax + 60f), new Vector2(r.xMax - 120f, r.yMax - 10f), new Vector2(r.xMin, r.yMax),
            }, Palette.Ink).Stroke(Palette.Paper, 6).Shadow(new Vector2(12, 12), Palette.Green);
            Ui.Text(text, "sp-bubble__text", bubble, FontKind.Black);
            return bubble;
        }

        // P5-style results over the battlefield: ink and green bands slam across, a paper brush stroke
        // carries the giant RESULT, the victory art slides in behind the panels, the EXP burst counts up,
        // each member's EXP bar fills (a LEVEL UP! burst when it rolls over), the drops flip in as cards
        // and a blinking prompt waits for Enter.
        async Awaitable ResultsFx(List<Reward> rewards)
        {
            const float Total = 2.1f;
            fx.Q(className: "sp-allout")?.RemoveFromHierarchy();
            var layer = Ui.El("sp-results", fx);
            Ui.Fill(layer);
            results = layer;
            var dim = Ui.El("sp-results__dim", layer);
            Ui.Fill(dim);
            var inkBand = new Shape(Shape.Slant(260), Palette.Ink.WithAlpha(0.94f));
            inkBand.AddToClassList("sp-results__band");
            layer.Add(inkBand);
            var greenBand = new Shape(Shape.Slant(60), Palette.Green);
            greenBand.AddToClassList("sp-results__band");
            greenBand.AddToClassList("is-green");
            layer.Add(greenBand);

            // The art: whoever in the party has a portrait (DeepSeek in the prologue), with a green echo.
            var hero = party.Select(c => c.Unit.Id).FirstOrDefault(id => id == "DeepSeek");
            Image art = null, echo = null;
            if (hero != null)
            {
                echo = Ui.Picture(Ui.Tex("Art/Portraits/deepseek_battle"), ScaleMode.ScaleToFit, "sp-results__art is-echo", layer);
                echo.tintColor = Palette.Green;
                art = Ui.Picture(Ui.Tex("Art/Portraits/deepseek_battle"), ScaleMode.ScaleToFit, "sp-results__art", layer);
            }

            var brush = new Shape(Shape.Brush(31), Palette.Paper);
            brush.AddToClassList("sp-results__brush");
            layer.Add(brush);
            var title = Ui.El("sp-results__title", layer);
            var letters = new List<Label>();
            foreach (var ch in "RESULT") letters.Add(Ui.Text(ch.ToString(), "sp-results__letter", title, FontKind.Black));
            var win = Ui.El("sp-results__win", layer);
            Shape.Backdrop(win, Shape.Slant(14), Palette.Green).Shadow(new Vector2(8, 8), Palette.Ink);
            Ui.Text("战斗胜利", "sp-results__win-text", win, FontKind.Black);

            var exp = Ui.El("sp-results__exp", layer);
            var burst = new Shape(Shape.Burst(16, 0.72f, 5), Palette.Green).Stroke(Palette.Ink, 6).Shadow(new Vector2(12, 12), Palette.Ink);
            Ui.Fill(burst);
            exp.Add(burst);
            Ui.Text("EXP", "sp-results__exp-label", exp, FontKind.MonoBold);
            var expNumber = Ui.Text("+0", "sp-results__exp-number", exp, FontKind.Black);

            var rows = new List<VisualElement>();
            var fills = new List<VisualElement>();
            var levelLabels = new List<Label>();
            var nextLabels = new List<Label>();
            var ups = new List<VisualElement>();
            var list = Ui.El("sp-results__party", layer);
            foreach (var r in rewards)
            {
                var row = Ui.El("sp-results__member", list);
                Shape.Backdrop(row, Shape.Slant(30), Palette.Ink.WithAlpha(0.96f)).Stroke(Palette.Paper, 3).Shadow(new Vector2(10, 10), Palette.Green);
                if (r.Id == GameState.PlayerId || r.Id == "DeepSeek")
                    new CardFace(r.Id, game.State.playerGender, 100f, "sp-results__face", row).Play(FaceAct.Victory);
                else
                {
                    var who = GameData.Character(r.Id);
                    Ui.FaceCrop(who.PortraitKey(who.DefaultFace), 100f, 100f, "sp-results__face", row, 2.4f, 0.2f);
                }
                var info = Ui.El("sp-results__member-info", row);
                var top = Ui.El("sp-results__member-top", info);
                Ui.Text(r.Name, "sp-results__name", top, FontKind.Black);
                levelLabels.Add(Ui.Text($"Lv {r.From}", "sp-results__level", top, FontKind.MonoBold));
                var barRow = Ui.El("sp-results__bar-row", info);
                var bar = Ui.El("sp-results__bar", barRow);
                fills.Add(Ui.El("sp-results__fill", bar));
                nextLabels.Add(Ui.Text("", "sp-results__next-exp", barRow, FontKind.MonoBold));
                var up = Ui.El("sp-results__levelup", row);
                var upBurst = new Shape(Shape.Burst(12, 0.7f, r.Name.Length + 3), Palette.Paper).Stroke(Palette.Ink, 5).Shadow(new Vector2(8, 8), Palette.Red);
                Ui.Fill(upBurst);
                up.Add(upBurst);
                Ui.Text("LEVEL UP!", "sp-results__levelup-text", up, FontKind.Black);
                up.style.opacity = 0;
                ups.Add(up);
                rows.Add(row);
            }

            var drops = new List<VisualElement>();
            VisualElement shelfTag = null;
            if (def.Drops != null && def.Drops.Length > 0)
            {
                var shelf = Ui.El("sp-results__drops", layer);
                var get = shelfTag = Ui.El("sp-results__get", shelf);
                Shape.Backdrop(get, Shape.Slant(12), Palette.Red);
                Ui.Text("GET!", "sp-results__get-text", get, FontKind.Black);
                var seed = 0;
                foreach (var d in def.Drops)
                {
                    var card = Ui.El("sp-results__drop", shelf);
                    Shape.Backdrop(card, Shape.Jag(++seed * 11, 9f), Palette.Paper).Shadow(new Vector2(8, 8), Palette.Green);
                    Ui.Text(GameData.Items.TryGetValue(d.Id, out var item) ? item.Name : d.Id, "sp-results__drop-name", card, FontKind.Black);
                    Ui.Text($"×{d.Count}", "sp-results__drop-count", card, FontKind.MonoBold);
                    drops.Add(card);
                }
            }

            VisualElement bubble = null;
            if (hero != null && !finisherShown) bubble = SpeechBubble(layer, "吃饭饭！", "sp-results__bubble");

            var prompt = Ui.El("sp-results__next", layer);
            Shape.Backdrop(prompt, Shape.Slant(16), Palette.Paper).Shadow(new Vector2(8, 8), Palette.Green);
            Ui.Text("▶ NEXT", "sp-results__next-text", prompt, FontKind.Black);
            Ui.Text("Enter", "sp-results__next-key", prompt, FontKind.MonoBold);

            game.Audio.Sfx("slam");
            Fx.Flash(fx, Color.white);
            var whooshed = false;
            var levelsShown = rewards.Select(r => r.From).ToArray();
            var dropsHeard = 0;
            var bubbleHeard = false;
            await Anim.Tween(Total, t =>
            {
                var time = t * Total;
                dim.style.opacity = Mathf.Clamp01(time / 0.15f);
                inkBand.style.scale = new Scale(new Vector2(Ease.OutCubic(Mathf.Clamp01(time / 0.18f)), 1f));
                greenBand.style.scale = new Scale(new Vector2(Ease.OutCubic(Mathf.Clamp01((time - 0.06f) / 0.18f)), 1f));
                if (!whooshed && time >= 0.1f)
                {
                    whooshed = true;
                    game.Audio.Sfx("whoosh");
                }
                brush.style.scale = new Scale(new Vector2(Ease.OutCubic(Mathf.Clamp01((time - 0.1f) / 0.2f)), 1f));
                for (var i = 0; i < letters.Count; i++)
                {
                    var k = Mathf.Clamp01((time - 0.18f - i * 0.05f) / 0.18f);
                    Ui.Zoom(letters[i], Mathf.Lerp(2.6f, 1f, Ease.OutBack(k)));
                    letters[i].style.opacity = Mathf.Clamp01(k * 3f);
                }
                win.style.translate = new Translate((1 - Ease.OutBack(Mathf.Clamp01((time - 0.4f) / 0.22f))) * -900f, 0);
                if (art != null)
                {
                    var a = Ease.OutCubic(Mathf.Clamp01((time - 0.2f) / 0.35f));
                    art.style.translate = new Translate((1 - a) * 700f, 0);
                    art.style.opacity = a;
                    var e = Ease.OutCubic(Mathf.Clamp01((time - 0.28f) / 0.35f));
                    echo.style.translate = new Translate((1 - e) * 900f + 34f, 18f);
                    echo.style.opacity = e * 0.9f;
                }
                var b = Ease.OutBack(Mathf.Clamp01((time - 0.45f) / 0.25f));
                Ui.Zoom(exp, b);
                Ui.Turn(exp, Mathf.Lerp(-30f, -6f, b));
                expNumber.text = $"+{Mathf.RoundToInt(earned * Ease.OutCubic(Mathf.Clamp01((time - 0.55f) / 0.6f)))}";
                partyRow.style.opacity = 1 - Mathf.Clamp01(time / 0.15f);
                for (var i = 0; i < rows.Count; i++)
                {
                    var k = Ease.OutBack(Mathf.Clamp01((time - 0.7f - i * 0.12f) / 0.25f));
                    rows[i].style.translate = new Translate((1 - k) * -1600f, 0);
                    rows[i].style.opacity = k > 0 ? 1f : 0f;
                    var r = rewards[i];
                    var p = Mathf.Lerp(r.ExpFrom, r.ExpTo, Ease.InOutCubic(Mathf.Clamp01((time - 0.95f - i * 0.12f) / 0.6f)));
                    var level = Mathf.Max(r.From, CombatStats.LevelForExp(Mathf.FloorToInt(p)));
                    var floor = CombatStats.ExpForLevel(level);
                    var ceiling = CombatStats.ExpForLevel(level + 1);
                    fills[i].style.width = Length.Percent(100f * Mathf.Clamp01((p - floor) / Mathf.Max(1, ceiling - floor)));
                    nextLabels[i].text = $"NEXT {Mathf.Max(0, ceiling - Mathf.FloorToInt(p))}";
                    if (level > levelsShown[i])
                    {
                        levelsShown[i] = level;
                        levelLabels[i].text = $"Lv {level}";
                        game.Audio.Sfx("levelup");
                        PopLevelUp(ups[i]).Forget();
                    }
                }
                if (shelfTag != null) shelfTag.style.opacity = time < 1.3f ? 0f : 1f;
                for (var i = 0; i < drops.Count; i++)
                {
                    var k = Mathf.Clamp01((time - 1.3f - i * 0.1f) / 0.22f);
                    drops[i].style.scale = new Scale(new Vector2(Ease.OutBack(k), 1f));
                    drops[i].style.opacity = k > 0 ? 1f : 0f;
                    if (k > 0 && dropsHeard <= i)
                    {
                        dropsHeard = i + 1;
                        game.Audio.Sfx("coin", 0.8f);
                    }
                }
                if (bubble != null)
                {
                    var k = Mathf.Clamp01((time - 1.5f) / 0.2f);
                    Ui.Zoom(bubble, Mathf.Lerp(0.2f, 1f, Ease.OutBack(k)));
                    bubble.style.opacity = k > 0 ? 1f : 0f;
                    if (k > 0 && !bubbleHeard)
                    {
                        bubbleHeard = true;
                        game.Audio.Sfx("slap", 0.6f);
                    }
                }
                prompt.style.opacity = time < 1.8f ? 0f : 1f;
            }, Ease.Linear);
            layer.schedule.Execute(() => prompt.style.opacity = prompt.resolvedStyle.opacity > 0.5f ? 0.25f : 1f).Every(450);
        }

        static async Awaitable PopLevelUp(VisualElement up)
        {
            await Anim.Tween(0.3f, t =>
            {
                up.style.opacity = Mathf.Clamp01(t * 3f);
                Ui.Zoom(up, Mathf.Lerp(2.2f, 1f, t));
                Ui.Turn(up, Mathf.Lerp(-30f, -8f, t));
            }, Ease.OutBack);
        }

        // Enter on the results: the panels are thrown off to the left before the field is cleared.
        async Awaitable ResultsOut()
        {
            var layer = results;
            results = null;
            if (layer == null) return;
            game.Audio.Sfx("whoosh", 0.7f);
            await Anim.Tween(0.24f, t =>
            {
                layer.style.translate = new Translate(t * -2200f, 0);
                layer.style.opacity = 1 - t;
            }, Ease.InCubic);
        }
    }
}
