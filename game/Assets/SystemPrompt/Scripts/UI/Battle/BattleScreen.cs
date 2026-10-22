using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SystemPrompt.Battle;
using SystemPrompt.Story;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Turn-based battle (design/systems.md 战斗): the party acts in 响应 order, then the noise bodies.
    // In the tutorial, Tutorial.Rule limits the commands each turn and the hint nodes run as scripted.
    public sealed partial class BattleScreen : IInputLayer, ILinePresenter
    {
        enum Mode { Idle, Command, Skill, Item, Target, Hint, Prompt, Results }

        struct BattleAction
        {
            public string Command;
            public SkillDef Skill;
            public Combatant Target;
            public string ItemId;
        }

        // Abort() ends a session; every await loop checks the session it started with, so a battle
        // abandoned by loading a save can never act on the next one.
        sealed class Session { public bool Over; }

        const int NoPick = -2;
        const int Cancelled = -1;
        static readonly Regex ThinkTimer = new Regex(@"^思考中……（已思考 (.+) 秒）$");
        static readonly Vector2[] EnemySpots = { new Vector2(560, 470), new Vector2(960, 420), new Vector2(1360, 470) };

        public readonly VisualElement Root;
        readonly GameRoot game;
        readonly Image background;
        readonly VisualElement enemyLayer;
        readonly VisualElement partyRow;
        readonly VisualElement menuRoot;
        readonly Label actorBanner;
        readonly VisualElement commandBox;
        readonly VisualElement skillBox;
        readonly VisualElement skillList;
        readonly Label skillDescription;
        readonly VisualElement itemBox;
        readonly VisualElement itemList;
        readonly Label tip;
        readonly VisualElement promptBox;
        readonly VisualElement fx;
        VisualElement results;   // the results layer while it is up
        bool finisherShown;      // the 集成 ended on DeepSeek's line, so the results don't repeat it
        readonly VisualElement tutorialBox;
        readonly Label tutorialText;
        readonly VisualElement speechBox;
        readonly Label speechName;
        readonly Label speechText;
        readonly VisualElement thinkBubble;
        readonly Label thinkLabel;

        readonly MenuList commands = new MenuList { Styler = StyleCommand };
        readonly MenuList skillMenu = new MenuList { Styler = ModalParts.StyleRow };
        readonly MenuList itemMenu = new MenuList { Styler = ModalParts.StyleRow };
        readonly MenuList promptMenu = new MenuList { Horizontal = true, Styler = ModalParts.StyleButton };
        readonly List<EnemyView> enemies = new List<EnemyView>();
        readonly List<PartyCard> party = new List<PartyCard>();
        readonly List<string> commandIds = new List<string>();
        readonly List<SkillDef> skillChoices = new List<SkillDef>();
        readonly List<string> itemChoices = new List<string>();

        Session session = new Session { Over = true };
        BattleDef def;
        StoryController story;
        Mode mode;
        int picked = NoPick;
        bool advance;
        List<Combatant> targets = new List<Combatant>();
        int targetIndex;
        bool oneMoreHintShown;

        public BattleScreen(GameRoot game, VisualElement parent)
        {
            this.game = game;
            Root = Ui.El("sp-battle", parent);
            Ui.Fill(Root);
            background = Ui.Picture(Ui.Tex("Art/Backgrounds/cache_sea"), ScaleMode.ScaleAndCrop, "sp-battle__bg", Root);
            Ui.Fill(background);
            ParticleField.Drift(background, 24f, 1.07f);
            var tint = Ui.El("sp-battle__tint", Root);
            Ui.Fill(tint);
            var dots = new Halftone(Palette.Sea.WithAlpha(0.16f), 22f, 6f, false);
            Ui.Fill(dots);
            Root.Add(dots);
            new AmbientLayer(Root, false).Set(Ambient.Latent);

            enemyLayer = Ui.El("sp-battle__enemies", Root);
            Ui.Fill(enemyLayer);
            enemyLayer.pickingMode = PickingMode.Ignore;

            partyRow = Ui.El("sp-battle__party", Root);

            menuRoot = Ui.El("sp-battle__menu", Root);
            var bannerBox = Ui.El("sp-battle__banner", menuRoot);
            Shape.Backdrop(bannerBox, Shape.Slant(18), Palette.Green).Shadow(new Vector2(8, 8), Palette.Ink);
            actorBanner = Ui.Text("", "sp-battle__banner-text", bannerBox, FontKind.Black);
            commandBox = Ui.El("sp-battle__commands", menuRoot);
            tip = Ui.Text("", "sp-battle__tip", menuRoot, FontKind.Bold);

            skillBox = ModalParts.Panel(Root, "技能", "SKILL", "sp-battle__skills");
            skillList = Ui.El("sp-battle__skill-list", skillBox);
            skillDescription = Ui.Text("", "sp-battle__skill-desc", skillBox, FontKind.Bold);
            skillMenu.SelectionChanged += i => skillDescription.text = i < skillChoices.Count ? skillChoices[i].Description : "";
            itemBox = ModalParts.Panel(Root, "道具", "ITEM", "sp-battle__items");
            itemList = Ui.El("sp-battle__item-list", itemBox);

            promptBox = Ui.El("sp-battle__prompt", Root);
            Shape.Backdrop(promptBox, Shape.Slant(30), Palette.Ink).Stroke(Palette.Green, 4).Shadow(new Vector2(14, 14), Palette.Green);
            Ui.Text("可发动「集成」", "sp-battle__prompt-title", promptBox, FontKind.Black);
            var promptRow = Ui.El("sp-battle__prompt-row", promptBox);
            promptMenu.Add(ModalParts.Button(promptRow, "▶ 发动"), () => picked = 0);
            promptMenu.Add(ModalParts.Button(promptRow, "不发动"), () => picked = 1);

            fx = Ui.El("sp-battle__fx", Root);
            Ui.Fill(fx);
            fx.pickingMode = PickingMode.Ignore;

            tutorialBox = Ui.El("sp-battle__tutorial", Root);
            Shape.Backdrop(tutorialBox, Shape.Slant(20), Palette.Ink.WithAlpha(0.95f)).Stroke(Palette.Green, 3).Shadow(new Vector2(10, 10), Palette.Green);
            var tutorialTag = Ui.El("sp-battle__tutorial-tag", tutorialBox);
            Shape.Backdrop(tutorialTag, Shape.Slant(12), Palette.Green);
            Ui.Text("TUTORIAL", "sp-battle__tutorial-tag-text", tutorialTag, FontKind.MonoBold);
            tutorialText = Ui.Text("", "sp-battle__tutorial-text", tutorialBox, FontKind.Bold);

            speechBox = Ui.El("sp-battle__speech", Root);
            Shape.Backdrop(speechBox, Shape.Crooked(12), Palette.Paper).Stroke(Palette.Ink, 4).Shadow(new Vector2(10, 10), Palette.Ink);
            var plate = Ui.El("sp-battle__speech-plate", speechBox);
            Shape.Backdrop(plate, Shape.Slant(14), Palette.Ink);
            speechName = Ui.Text("", "sp-battle__speech-name", plate, FontKind.Black);
            speechText = Ui.Text("", "sp-battle__speech-text", speechBox, FontKind.Regular);

            thinkBubble = Ui.El("sp-battle__think", Root);
            thinkLabel = Ui.Text("", "sp-battle__think-text", thinkBubble, FontKind.Bold);

            Root.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (mode == Mode.Hint) advance = true;
                else if (mode == Mode.Results) picked = 0;
            });
            HideMenus();
        }

        public void Show(bool on) => Ui.Show(Root, on);

        public void Abort()
        {
            session.Over = true;
            mode = Mode.Idle;
        }

        // ------------------------------------------------------------------ flow

        public async Awaitable Run(string id, StoryController run, BattleOpening opening = BattleOpening.Normal)
        {
            Abort();
            var s = session = new Session();
            def = Battles.Get(id);
            if (def == null)
            {
                Debug.LogError($"<<battle {id}>>：Battles 里没有这场战斗，直接跳过");
                return;
            }
            story = run;
            oneMoreHintShown = false;
            finisherShown = false;
            results = null;
            Setup();
            await Intro();
            if (s.Over) return;
            if (opening == BattleOpening.Preemptive) await Banner("先制攻击！");
            if (opening == BattleOpening.Ambush)
            {
                await Banner("被偷袭！");
                if (await EnemyPhase(s)) return;
            }

            for (var turn = 1; !s.Over; turn++)
            {
                if (def.Tutorial) await TutorialBeats(turn);
                foreach (var card in party.Where(c => !c.Unit.Dead).OrderByDescending(c => c.Unit.Agi).ToList())
                {
                    if (s.Over) return;
                    if (await PartyTurn(card, turn))
                    {
                        if (!s.Over) await Victory();
                        return;
                    }
                }
                if (s.Over) return;
                if (turn == 1 && opening == BattleOpening.Preemptive) continue; // their first turn is lost
                if (await EnemyPhase(s)) return;
            }
        }

        // Every noise body acts once. True when the battle ended there (lost, or abandoned).
        async Awaitable<bool> EnemyPhase(Session s)
        {
            await Banner("敌人的回合");
            foreach (var view in enemies.Where(v => !v.Unit.Dead).ToList())
            {
                if (s.Over) return true;
                await EnemyTurn(view);
            }
            if (s.Over) return true;
            if (party.All(c => c.Unit.Dead) || party[0].Unit.Dead)
            {
                await Defeat();
                return true;
            }
            return false;
        }

        void Setup()
        {
            HideMenus();
            HideHints();
            fx.Clear();
            partyRow.style.opacity = StyleKeyword.Null; // the results hide the party cards
            enemyLayer.Clear();
            partyRow.Clear();
            enemies.Clear();
            party.Clear();

            for (var i = 0; i < def.Enemies.Length; i++)
            {
                var unit = Battles.Spawn(def.Enemies[i]);
                unit.HpFloor = def.Tutorial ? 1 : 0;
                var spot = EnemySpots[Mathf.Min(i, EnemySpots.Length - 1)];
                var view = new EnemyView(unit, enemyLayer, spot);
                var index = i;
                view.Root.RegisterCallback<PointerEnterEvent>(_ => HoverTarget(view.Unit));
                view.Root.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    ClickTarget(view.Unit);
                });
                enemies.Add(view);
            }

            var state = game.State;
            foreach (var id in state.party)
            {
                var member = state.Member(id);
                var unit = Combatant.FromMember(member, id == GameState.PlayerId ? state.playerName : id);
                unit.HpFloor = def.Tutorial ? 1 : 0;
                var card = new PartyCard(unit, partyRow);
                card.Root.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    ClickTarget(card.Unit);
                });
                party.Add(card);
            }
        }

        bool AllEnemiesDead => enemies.All(v => v.Unit.Dead);
        bool AllEnemiesDown => enemies.Where(v => !v.Unit.Dead).All(v => v.Unit.Down);

        EnemyView ViewOf(Combatant c) => enemies.Find(v => v.Unit == c);
        PartyCard CardOf(Combatant c) => party.Find(p => p.Unit == c);

        async Awaitable TutorialBeats(int turn)
        {
            var s = session;
            if (turn == 2)
            {
                game.Audio.Sfx("crack");
                foreach (var v in enemies.Where(v => v.Unit.WeaknessLabel != v.Unit.TrueWeakness)) v.Shatter().Forget();
                await Anim.Wait(0.6f);
            }
            var node = Tutorial.HintAtTurnStart(turn);
            if (node != null && !s.Over) await story.RunHint(node, this);
            if (turn == 1 && !s.Over)
            {
                // DeepSeek's warning plants the doubt: the fake 「弱点：火」 flickers to 「？？？」.
                game.Audio.Sfx("glitch", 0.6f);
                foreach (var v in enemies.Where(v => v.Unit.WeaknessLabel != v.Unit.TrueWeakness)) await v.Flicker("？？？");
            }
        }

        // Returns true when the battle is won.
        async Awaitable<bool> PartyTurn(PartyCard card, int turn)
        {
            var s = session;
            var unit = card.Unit;
            unit.Guarding = false;
            card.Refresh();
            var oneMore = false;
            try
            {
                while (!s.Over)
                {
                    foreach (var p in party) p.SetActive(p == card);
                    var rule = def.Tutorial ? Tutorial.Rule(turn, unit.Id, oneMore) : null;
                    var action = await ChooseAction(unit, rule);
                    if (s.Over || action == null) return false;
                    var knockedDown = await Execute(unit, action.Value);
                    if (s.Over) return false;
                    if (AllEnemiesDead) return true;
                    if (AllEnemiesDown) return await AllOut();
                    if (!knockedDown) return false;

                    oneMore = true;
                    game.Audio.Sfx("rise");
                    var stamp = Fx.Stamp(fx, "ONE MORE!", Palette.Green, 120f, -8f, new Vector2(960, 380));
                    await Anim.Wait(0.9f);
                    await Fx.Dismiss(stamp);
                    if (def.Tutorial && !oneMoreHintShown)
                    {
                        oneMoreHintShown = true;
                        await story.RunHint(Tutorial.OneMoreHint, this);
                    }
                }
                return false;
            }
            finally
            {
                card.SetActive(false);
            }
        }

        async Awaitable<BattleAction?> ChooseAction(Combatant unit, TurnRule rule)
        {
            var s = session;
            while (!s.Over)
            {
                ShowCommands(unit, rule);
                var i = await WaitPick(Mode.Command);
                HideMenus();
                if (s.Over) return null;
                if (i < 0) continue;
                switch (commandIds[i])
                {
                    case "attack":
                    {
                        var t = await ChooseTarget(enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit));
                        if (t != null) return new BattleAction { Command = "attack", Skill = Skills.NormalAttack(unit.Id), Target = t };
                        break;
                    }
                    case "skill":
                    {
                        var skill = await ChooseSkill(unit, rule);
                        if (skill == null) break;
                        if (skill.AllTargets) return new BattleAction { Command = "skill", Skill = skill };
                        var t = await ChooseTarget(enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit));
                        if (t != null) return new BattleAction { Command = "skill", Skill = skill, Target = t };
                        break;
                    }
                    case "think":
                        return new BattleAction { Command = "think", Skill = Skills.DeepThink };
                    case "guard":
                        return new BattleAction { Command = "guard" };
                    case "item":
                    {
                        var item = await ChooseItem();
                        if (item == null) break;
                        var t = await ChooseTarget(party.Where(p => !p.Unit.Dead).Select(p => p.Unit));
                        if (t != null) return new BattleAction { Command = "item", ItemId = item, Target = t };
                        break;
                    }
                }
            }
            return null;
        }

        void ShowCommands(Combatant unit, TurnRule rule)
        {
            commandBox.Clear();
            commands.Clear();
            commandIds.Clear();
            actorBanner.text = unit.Id == GameState.PlayerId ? "我的回合" : $"{unit.Name} 的回合";
            var options = new List<(string Id, string Label)> { ("attack", unit.Id == GameState.PlayerId ? "攻击（截图）" : "攻击") };
            if (Skills.MagicOf(unit.Id).Count > 0) options.Add(("skill", "技能"));
            if (Skills.CanThink(unit.Id)) options.Add(("think", "深度思考"));
            options.Add(("guard", "防御"));
            options.Add(("item", "道具"));
            foreach (var (id, label) in options)
            {
                var item = Ui.El("sp-command", commandBox);
                Shape.Backdrop(item, Shape.Slant(18), Palette.Ink).Stroke(Palette.Paper, 2);
                Ui.Text(label, "sp-command__text", item, FontKind.Black);
                var allowed = rule == null || Array.IndexOf(rule.Commands, id) >= 0;
                if (id == "item" && !HasUsableItems()) allowed = false;
                if (id == "skill" && Skills.MagicOf(unit.Id).All(sk => sk.SpCost > unit.Sp)) allowed = false;
                var index = commandIds.Count;
                commandIds.Add(id);
                commands.Add(item, () => picked = index, allowed);
            }
            tip.text = rule?.Tip ?? "";
            Ui.Show(tip, tip.text.Length > 0);
            Ui.Show(menuRoot, true);
            Anim.Tween(0.16f, t =>
            {
                menuRoot.style.opacity = t;
                menuRoot.style.translate = new Translate((1 - t) * -120f, 0);
            }).Forget();
        }

        static void StyleCommand(VisualElement item, bool on)
        {
            item.Q<Shape>().Fill(on ? Palette.Green : Palette.Ink);
            item.style.translate = new Translate(on ? 34f : 0f, 0);
        }

        bool HasUsableItems() => game.State.inventory.Any(st => GameData.Items.TryGetValue(st.id, out var d) && d.HealHp > 0 && st.count > 0);

        async Awaitable<SkillDef> ChooseSkill(Combatant unit, TurnRule rule)
        {
            skillList.Clear();
            skillMenu.Clear();
            skillChoices.Clear();
            foreach (var sk in Skills.MagicOf(unit.Id))
            {
                var row = ModalParts.Row(skillList, "sp-skillrow");
                Ui.Text(sk.Name, "sp-skillrow__name", row, FontKind.Black);
                Ui.Text(Battles.ElementLabel(sk.Element) + (sk.AllTargets ? "·全体" : "·单体"), "sp-skillrow__element", row, FontKind.Bold);
                Ui.Text($"SP {sk.SpCost}", "sp-skillrow__cost", row, FontKind.MonoBold);
                var allowed = sk.SpCost <= unit.Sp && (rule?.Skill == null || rule.Skill == sk.Id);
                var index = skillChoices.Count;
                skillChoices.Add(sk);
                skillMenu.Add(row, () => picked = index, allowed);
            }
            if (skillMenu.Index >= 0) skillDescription.text = skillChoices[skillMenu.Index].Description;
            Ui.Show(skillBox, true);
            var i = await WaitPick(Mode.Skill);
            Ui.Show(skillBox, false);
            return i >= 0 ? skillChoices[i] : null;
        }

        async Awaitable<string> ChooseItem()
        {
            itemList.Clear();
            itemMenu.Clear();
            itemChoices.Clear();
            foreach (var stack in game.State.inventory)
            {
                if (!GameData.Items.TryGetValue(stack.id, out var item) || item.HealHp <= 0) continue;
                var row = ModalParts.Row(itemList, "sp-skillrow");
                Ui.Text(item.Name, "sp-skillrow__name", row, FontKind.Black);
                Ui.Text(item.Description, "sp-skillrow__element", row, FontKind.Bold);
                Ui.Text($"×{stack.count}", "sp-skillrow__cost", row, FontKind.MonoBold);
                var index = itemChoices.Count;
                itemChoices.Add(stack.id);
                itemMenu.Add(row, () => picked = index);
            }
            Ui.Show(itemBox, true);
            var i = await WaitPick(Mode.Item);
            Ui.Show(itemBox, false);
            return i >= 0 ? itemChoices[i] : null;
        }

        async Awaitable<Combatant> ChooseTarget(IEnumerable<Combatant> candidates)
        {
            var list = candidates.ToList();
            if (list.Count == 0) return null;
            targets = list;
            targetIndex = Mathf.Max(0, list.FindIndex(t => !t.Down));
            RefreshTargets();
            var i = await WaitPick(Mode.Target);
            targets = new List<Combatant>();
            RefreshTargets();
            return i >= 0 && i < list.Count ? list[i] : null;
        }

        void RefreshTargets()
        {
            var current = targets.Count > 0 ? targets[targetIndex] : null;
            foreach (var v in enemies) v.SetTargeted(v.Unit == current);
            foreach (var p in party) p.Root.EnableInClassList("is-targeted", p.Unit == current);
        }

        void HoverTarget(Combatant c)
        {
            if (mode != Mode.Target) return;
            var i = targets.IndexOf(c);
            if (i < 0 || i == targetIndex) return;
            targetIndex = i;
            game.Audio.Sfx("cursor", 0.5f);
            RefreshTargets();
        }

        void ClickTarget(Combatant c)
        {
            if (mode != Mode.Target) return;
            var i = targets.IndexOf(c);
            if (i < 0) return;
            targetIndex = i;
            game.Audio.Sfx("confirm");
            picked = i;
        }

        async Awaitable<int> WaitPick(Mode m)
        {
            var s = session;
            picked = NoPick;
            mode = m;
            while (picked == NoPick && !s.Over) await Awaitable.NextFrameAsync();
            if (mode == m) mode = Mode.Idle;
            return s.Over ? Cancelled : picked;
        }

        void HideMenus()
        {
            Ui.Show(menuRoot, false);
            Ui.Show(skillBox, false);
            Ui.Show(itemBox, false);
            Ui.Show(promptBox, false);
        }

        // Returns true when this action knocked down an enemy that was standing (One More).
        async Awaitable<bool> Execute(Combatant unit, BattleAction action)
        {
            var card = CardOf(unit);
            switch (action.Command)
            {
                case "attack":
                {
                    await SkillBar(unit, action.Skill.Name);
                    if (unit.Id == GameState.PlayerId) await ScreenshotFx(ViewOf(action.Target));
                    else await SlashFx(ViewOf(action.Target));
                    var hit = DamageCalc.Compute(unit, action.Target, action.Skill, def);
                    await ApplyHit(action.Target, hit);
                    return hit.KnockedDown;
                }
                case "skill":
                {
                    var skill = action.Skill;
                    unit.Sp -= skill.SpCost;
                    card?.Refresh();
                    await CutIn(unit, skill.Name);
                    var hitTargets = skill.AllTargets ? enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit).ToList() : new List<Combatant> { action.Target };
                    await IceFx(hitTargets.Select(ViewOf).ToList());
                    var hits = hitTargets.Select(t => (t, DamageCalc.Compute(unit, t, skill, def))).ToList();
                    if (unit.Charged)
                    {
                        unit.Charged = false;
                        card?.Refresh();
                    }
                    if (hits.Any(h => h.Item2.Affinity == Affinity.Weak))
                    {
                        game.Audio.Sfx("slam");
                        var stamp = Fx.Stamp(fx, "对抗样本！", Palette.Paper, 110f, -10f, new Vector2(960, 230));
                        DismissLater(stamp, 1.1f);
                    }
                    var anyDown = false;
                    foreach (var (t, hit) in hits)
                    {
                        ApplyHit(t, hit).Forget();
                        anyDown |= hit.KnockedDown;
                        await Anim.Wait(0.12f);
                    }
                    await Anim.Wait(0.7f);
                    return anyDown;
                }
                case "think":
                    await SkillBar(unit, "深度思考");
                    unit.Charged = true;
                    card?.Refresh();
                    await ThinkFx(card);
                    return false;
                case "guard":
                    unit.Guarding = true;
                    card?.Refresh();
                    await SkillBar(unit, "防御");
                    return false;
                case "item":
                {
                    var item = GameData.Items[action.ItemId];
                    if (!game.State.UseItem(action.ItemId)) return false;
                    await SkillBar(unit, item.Name);
                    var heal = action.Target.Id == "DeepSeek" ? item.HealHpForDeepSeek : item.HealHp;
                    action.Target.Hp = Mathf.Min(action.Target.MaxHp, action.Target.Hp + heal);
                    var target = CardOf(action.Target);
                    target?.Refresh();
                    game.Audio.Sfx("levelup", 0.6f);
                    if (target != null) Popup(CenterOf(target.Root), $"+{heal}", Palette.Green, null);
                    await Anim.Wait(0.6f);
                    return false;
                }
            }
            return false;
        }

        async Awaitable ApplyHit(Combatant target, Hit hit)
        {
            var view = ViewOf(target);
            var card = CardOf(target);
            var at = view != null ? view.Center : card != null ? CenterOf(card.Root) : new Vector2(960, 540);
            if (hit.Missed)
            {
                Popup(at, "MISS", Palette.Gray, null);
                return;
            }
            target.Hp = Mathf.Max(target.HpFloor, target.Hp - hit.Damage);
            var tag = hit.Critical ? "CRITICAL" : hit.Affinity switch { Affinity.Weak => "WEAK", Affinity.Resist => "RESIST", Affinity.Null => "BLOCK", _ => null };
            Popup(at, hit.Damage.ToString(), Palette.Paper, tag);
            game.Audio.Sfx(hit.Affinity == Affinity.Weak || hit.Critical ? "crack" : "slap", 0.8f);
            if (view != null)
            {
                view.Refresh();
                Fx.Shake(view.Root, 18f, 0.3f);
                if (hit.KnockedDown)
                {
                    target.Down = true;
                    view.SetDown(true);
                }
                if (target.Dead) await view.Die();
            }
            else if (card != null)
            {
                card.Refresh();
                card.Shake();
                if (hit.KnockedDown) target.Down = true;
            }
            await Anim.Wait(0.25f);
        }

        async Awaitable EnemyTurn(EnemyView view)
        {
            var e = view.Unit;
            if (e.Down)
            {
                // Downed enemies spend their turn getting up.
                e.Down = false;
                view.SetDown(false);
                await Anim.Wait(0.4f);
                return;
            }
            var skill = e.Kind == "repost" ? Skills.Repost : Skills.NoiseStrike;
            var alive = party.Where(p => !p.Unit.Dead).ToList();
            if (alive.Count == 0) return;
            var victims = skill.AllTargets ? alive : new List<PartyCard> { alive[UnityEngine.Random.Range(0, alive.Count)] };
            await EnemyBar(view, skill.Name);
            await Lunge(view);
            foreach (var card in victims)
            {
                var hit = DamageCalc.Compute(e, card.Unit, skill, def);
                await ApplyHit(card.Unit, hit);
            }
            await Anim.Wait(0.25f);
        }

        async Awaitable<bool> AllOut()
        {
            var s = session;
            game.Audio.Sfx("slam");
            var stamp = Fx.Stamp(fx, "全员倒地！", Palette.Paper, 120f, -6f, new Vector2(960, 300));
            await Anim.Wait(1f);
            await Fx.Dismiss(stamp);
            if (def.Tutorial && !s.Over) await story.RunHint(Tutorial.AllOutHint, this);
            if (s.Over) return false;

            promptMenu.SetEnabled(1, !def.Tutorial);
            promptMenu.Select(0, false);
            Ui.Show(promptBox, true);
            Anim.Tween(0.2f, t => Ui.Zoom(promptBox, Mathf.Lerp(1.4f, 1f, t)), Ease.OutBack).Forget();
            var choice = await WaitPick(Mode.Prompt);
            Ui.Show(promptBox, false);
            if (s.Over || choice != 0) return false;

            var participants = party.Where(p => !p.Unit.Dead).Select(p => p.Unit).ToList();
            var damage = participants.Sum(u => Mathf.RoundToInt(u.Str + u.Mag)) * 4;
            await AllOutFx(participants, damage);
            return AllEnemiesDead;
        }

        async Awaitable Victory()
        {
            var s = session;
            var rewards = ApplyRewards();
            await ResultsFx(rewards);
            if (s.Over) return;
            picked = NoPick;
            await WaitPick(Mode.Results);
            await ResultsOut();
            fx.Clear();
        }

        readonly struct Reward
        {
            public readonly string Id, Name;
            public readonly int ExpFrom, ExpTo, From, To;

            public Reward(string id, string name, int expFrom, int expTo, int from, int to)
            {
                Id = id;
                Name = name;
                ExpFrom = expFrom;
                ExpTo = expTo;
                From = from;
                To = to;
            }
        }

        // EXP and drops go into GameState; HP/SP carry over and grow with a level up.
        List<Reward> ApplyRewards()
        {
            var state = game.State;
            var rewards = new List<Reward>();
            foreach (var card in party)
            {
                var u = card.Unit;
                var m = state.Member(u.Id);
                m.hp = Mathf.Max(1, u.Hp);
                m.sp = u.Sp;
                var from = m.level;
                var expFrom = m.exp;
                if (!u.Dead) m.exp += def.Exp;
                m.level = CombatStats.LevelForExp(m.exp);
                if (m.level > from)
                {
                    var before = CombatStats.ForCharacter(u.Id, from);
                    var after = CombatStats.ForCharacter(u.Id, m.level);
                    m.hp += after.MaxHp - before.MaxHp;
                    m.sp += after.MaxSp - before.MaxSp;
                }
                rewards.Add(new Reward(u.Id, u.Name, expFrom, m.exp, from, m.level));
            }
            if (def.Drops != null)
                foreach (var (id, count) in def.Drops) state.AddItem(id, count);
            return rewards;
        }

        async Awaitable Defeat()
        {
            game.Audio.Sfx("glitch");
            Fx.Stamp(fx, "掉线……", Palette.Red, 140f, -6f, new Vector2(960, 480));
            await Anim.Wait(2.5f);
            game.ShowTitle();
        }

        // ------------------------------------------------------------------ hints (ILinePresenter)

        public async Awaitable PresentLine(LineInfo line, Func<bool> nextRequested, Func<bool> hurryRequested)
        {
            var s = session;
            var previous = mode;
            mode = Mode.Hint;
            advance = false;
            try
            {
                var timer = line.Kind == LineKind.System ? ThinkTimer.Match(line.Text) : null;
                if (timer != null && timer.Success)
                {
                    await ThinkCount(timer.Groups[1].Value, nextRequested);
                }
                else
                {
                    Label target;
                    if (line.Kind == LineKind.System || line.Kind == LineKind.Narration)
                    {
                        Ui.Show(tutorialBox, true);
                        target = tutorialText;
                        game.Audio.Sfx("confirm", 0.6f);
                    }
                    else
                    {
                        Ui.Show(speechBox, true);
                        var def2 = GameData.Character(line.Speaker);
                        speechName.text = line.Speaker;
                        speechName.style.color = def2?.Color ?? Palette.Green;
                        target = speechText;
                    }
                    var cps = game.Settings.CharsPerSecond;
                    var shown = float.IsInfinity(cps) ? line.Text.Length : 0f;
                    while (shown < line.Text.Length && !s.Over && !nextRequested())
                    {
                        if (advance || SkipHeld(line))
                        {
                            advance = false;
                            break;
                        }
                        shown += Time.unscaledDeltaTime * cps;
                        target.text = line.Text.Substring(0, Mathf.Min(line.Text.Length, (int)shown));
                        await Awaitable.NextFrameAsync();
                    }
                    target.text = line.Text;
                    while (!s.Over && !nextRequested() && !advance && !SkipHeld(line)) await Awaitable.NextFrameAsync();
                }
                game.Records.MarkRead(line.Id);
                game.Story.Backlog.Add(new BacklogEntry
                {
                    Speaker = line.Kind == LineKind.Narration ? null : line.Speaker,
                    Text = line.Text,
                    Kind = line.Kind,
                });
            }
            finally
            {
                HideHints();
                if (mode == Mode.Hint) mode = previous;
            }
        }

        public async Awaitable<int> PresentOptions(string[] texts, bool[] available, Func<bool> cancelled)
        {
            Debug.LogWarning("战斗提示节点里不支持选项，已跳过");
            await Awaitable.NextFrameAsync();
            return -1;
        }

        bool SkipHeld(LineInfo line) => GameInput.SkipHeld && (game.Settings.skipUnread || game.Records.IsRead(line.Id));

        async Awaitable ThinkCount(string seconds, Func<bool> nextRequested)
        {
            var s = session;
            var deepSeek = party.Find(p => p.Unit.Id == "DeepSeek");
            PlaceThinkBubble(deepSeek);
            Ui.Show(thinkBubble, true);
            var target = float.TryParse(seconds, out var n) ? n : 3f;
            var t = 0f;
            const float duration = 1.4f;
            while (t < duration && !s.Over && !nextRequested() && !advance)
            {
                t += Time.unscaledDeltaTime;
                thinkLabel.text = $"思考中……（已思考 {Mathf.FloorToInt(target * Mathf.Clamp01(t / duration))} 秒）";
                await Awaitable.NextFrameAsync();
            }
            advance = false;
            thinkLabel.text = $"思考中……（已思考 {seconds} 秒）";
            while (!s.Over && !nextRequested() && !advance) await Awaitable.NextFrameAsync();
        }

        void PlaceThinkBubble(PartyCard card)
        {
            var at = card != null ? CenterOf(card.Root) : new Vector2(1500, 840);
            thinkBubble.style.left = at.x - 260f;
            thinkBubble.style.top = at.y - 260f;
        }

        void HideHints()
        {
            Ui.Show(tutorialBox, false);
            Ui.Show(speechBox, false);
            Ui.Show(thinkBubble, false);
        }

        // ------------------------------------------------------------------ input

        public void HandleInput()
        {
            switch (mode)
            {
                case Mode.Hint:
                    if (GameInput.Consume(Btn.Menu)) { game.Menu.Open(); return; }
                    if (GameInput.Consume(Btn.Confirm) || GameInput.ScrollY < 0) advance = true;
                    break;
                case Mode.Command:
                    if (GameInput.Consume(Btn.Menu)) { game.Menu.Open(); return; }
                    commands.HandleInput();
                    break;
                case Mode.Skill:
                    if (GameInput.Consume(Btn.Cancel)) { Back(); return; }
                    skillMenu.HandleInput();
                    break;
                case Mode.Item:
                    if (GameInput.Consume(Btn.Cancel)) { Back(); return; }
                    itemMenu.HandleInput();
                    break;
                case Mode.Target:
                    if (GameInput.Consume(Btn.Cancel)) { Back(); return; }
                    var delta = GameInput.Consume(Btn.Left) || GameInput.Consume(Btn.Up) ? -1 : GameInput.Consume(Btn.Right) || GameInput.Consume(Btn.Down) ? 1 : 0;
                    if (delta != 0 && targets.Count > 0)
                    {
                        targetIndex = (targetIndex + delta + targets.Count) % targets.Count;
                        game.Audio.Sfx("cursor");
                        RefreshTargets();
                    }
                    if (GameInput.Consume(Btn.Confirm) && targets.Count > 0)
                    {
                        game.Audio.Sfx("confirm");
                        picked = targetIndex;
                    }
                    break;
                case Mode.Prompt:
                    promptMenu.HandleInput();
                    break;
                case Mode.Results:
                    if (GameInput.Consume(Btn.Confirm)) picked = 0;
                    break;
                default:
                    if (GameInput.Consume(Btn.Menu)) game.Menu.Open();
                    break;
            }
        }

        void Back()
        {
            game.Audio.Sfx("cursor");
            picked = Cancelled;
        }
    }
}
