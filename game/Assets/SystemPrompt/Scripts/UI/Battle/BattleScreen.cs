using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SystemPrompt.Battle;
using SystemPrompt.Dungeon;
using SystemPrompt.Story;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Turn-based battle (design/systems.md 战斗): the party acts in 响应 order, then the noise bodies.
    // The field is 3D (Dungeon/BattleStage.cs): the party's models and the noise bodies stand on it and its
    // camera follows the turns; this screen draws the menus, the plates pinned over the noise bodies and the
    // effects on top. In the tutorial, Tutorial.Rule limits the commands each turn and the hint nodes run as scripted.
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
        const int MaxSummons = 2; // 标题党型 the 转发型 call in, in one battle
        static readonly Regex ThinkTimer = new Regex(@"^思考中……（已思考 (.+) 秒）$");

        public readonly VisualElement Root;
        readonly GameRoot game;
        BattleStage stage;       // the 3D field while a battle is set up
        int actor = -1;          // the party member choosing, whose shoulder the camera looks over
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
        bool hushed;             // the plates are put away (Quiet)
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
        bool seenThrough;        // 标题党型's fake weakness is exposed: ones called in later show the true one
        int summons;             // called in by the 转发型 this battle
        public int CalledIn { get; private set; } // in every battle so far, for the end-to-end run
        public int Exposures { get; private set; } // lying weakness tags shattered by a hit on the truth, so far
        public int LiesDoubted { get; private set; } // lying tags turned 「？？？」 by a hit on their own element, so far
        int earned;              // EXP for each member standing at the end, the called-in ones' included
        public int Earned => earned;
        BattleOpening opening;   // how this battle began: a preemptive strike pays more
        public bool Preemptive => opening == BattleOpening.Preemptive;
        (string Id, int Count)[] dropped; // what the victory gave, the preemptive bonus included
        public IReadOnlyList<string> FoeKinds => enemies.Select(v => v.Unit.Kind).ToList();
        public int FoesStanding => enemies.Count(v => !v.Unit.Dead);
        public bool FoeStands(string kind) => enemies.Any(v => !v.Unit.Dead && v.Unit.Kind == kind); // for the end-to-end run
        // A member's HP, max HP and SP in the battle going on, for the end-to-end run's choices.
        public (int Hp, int MaxHp, int Sp) Vital(string id)
        {
            var card = party.Find(p => p.Unit.Id == id);
            return card == null ? (0, 0, 0) : (card.Unit.Hp, card.Unit.MaxHp, card.Unit.Sp);
        }
        Element? aiming;         // the element of the action whose target is being picked, for the plates' forecast
        readonly HashSet<Combatant> calledLastTurn = new HashSet<Combatant>();
        readonly Dictionary<string, Combatant> lastTarget = new Dictionary<string, Combatant>(); // per member, this battle

        public BattleScreen(GameRoot game, VisualElement parent)
        {
            this.game = game;
            Root = Ui.El("sp-battle", parent);
            Ui.Fill(Root);

            enemyLayer = Ui.El("sp-battle__enemies", Root);
            Ui.Fill(enemyLayer);
            enemyLayer.pickingMode = PickingMode.Ignore;

            partyRow = Ui.El("sp-battle__party", Root);

            menuRoot = Ui.El("sp-battle__menu", Root);
            var bannerBox = Ui.El("sp-battle__banner", menuRoot);
            Shape.Backdrop(bannerBox, Shape.Slant(18), Palette.Green).Shadow(new Vector2(8, 8), Palette.Ink);
            actorBanner = Ui.Text("", "sp-battle__banner-text", bannerBox, FontKind.Black);
            commandBox = Ui.El("sp-battle__commands", menuRoot);
            Ui.Text("↑↓ 选择　Enter 确定", "sp-battle__keys", menuRoot, FontKind.MonoBold);
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
            Root.schedule.Execute(Pin).Every(0);
            HideMenus();
        }

        // Hidden when the battle is over and its screen is left: the field goes with it.
        public void Show(bool on)
        {
            Ui.Show(Root, on);
            if (!on) DropStage();
        }

        void DropStage()
        {
            if (stage != null) stage.Drop();
            stage = null;
        }

        public void Abort()
        {
            session.Over = true;
            mode = Mode.Idle;
        }

        // ------------------------------------------------------------------ flow

        public async Awaitable Run(BattleDef battle, StoryController run, BattleOpening opening = BattleOpening.Normal)
        {
            Abort();
            var s = session = new Session();
            def = battle;
            this.opening = opening;
            story = run;
            oneMoreHintShown = false;
            finisherShown = false;
            results = null;
            seenThrough = false;
            summons = 0;
            calledLastTurn.Clear();
            lastTarget.Clear();
            numbers.Clear();
            hushed = false;
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
                await TurnBeats(turn);
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

        // Every noise body acts once, after a beat for the party's last blow to play out (the throw, the number, the
        // way back) and the band that says so. True when the battle ended there (lost, or abandoned).
        async Awaitable<bool> EnemyPhase(Session s)
        {
            await Anim.Wait(0.2f);
            await Banner("敌人的回合", true);
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

            var units = def.Enemies.Select(Battles.Spawn).ToList();
            DropStage();
            stage = BattleStage.Create(game.State.playerGender, game.State.party, units.Select(u => u.Kind).ToList());
            SeaFloaters.Stage(stage.transform, stage.Camera); // its shafts and near motes (Dungeon/SeaFloaters.cs)
            foreach (var unit in units) AddEnemy(unit);

            var state = game.State;
            foreach (var id in state.party)
            {
                var member = state.Member(id);
                var unit = Combatant.FromMember(member, id == GameState.PlayerId ? state.playerName : id);
                unit.HpFloor = def.Tutorial ? 1 : 0;
                var card = new PartyCard(unit, partyRow, state.playerGender);
                card.Root.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    ClickTarget(card.Unit);
                });
                party.Add(card);
            }
        }

        // A noise body's plate, after the stage's noise bodies: the plate of the nth goes over the nth on the field.
        EnemyView AddEnemy(Combatant unit)
        {
            unit.HpFloor = def.Tutorial ? 1 : 0;
            var view = new EnemyView(unit, enemyLayer);
            view.SetQuiet(hushed);
            view.Root.RegisterCallback<PointerEnterEvent>(_ => HoverTarget(view.Unit));
            view.Root.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                ClickTarget(view.Unit);
            });
            enemies.Add(view);
            return view;
        }

        bool AllEnemiesDead => enemies.All(v => v.Unit.Dead);
        bool AllEnemiesDown => enemies.Where(v => !v.Unit.Dead).All(v => v.Unit.Down);

        // The plates are up while the party chooses and put away while anyone acts (EnemyView.SetQuiet); one called
        // in mid-turn comes in as the others are.
        void Quiet(bool quiet, bool hideDown = false)
        {
            hushed = quiet;
            foreach (var v in enemies) v.SetQuiet(quiet, hideDown);
        }

        EnemyView ViewOf(Combatant c) => enemies.Find(v => v.Unit == c);
        PartyCard CardOf(Combatant c) => party.Find(p => p.Unit == c);

        // Every frame: the plates onto the noise bodies and the numbers onto what they landed on, wherever the camera
        // has gone (a number out of the picture with its body is hidden till it comes back).
        void Pin()
        {
            if (stage == null) return;
            for (var i = 0; i < enemies.Count; i++)
            {
                var shown = OnScreen(stage.FoePoint(i), out var at);
                enemies[i].Place(at, shown);
            }
            for (var i = 0; i < numbers.Count; i++)
            {
                var n = numbers[i];
                if (n.Foe < 0 && n.World == null) continue;
                var shown = OnScreen(n.Foe >= 0 ? stage.FoePoint(n.Foe) : n.World.Value, out var at);
                n.Root.style.visibility = shown ? Visibility.Visible : Visibility.Hidden;
                if (!shown) continue;
                n.Root.style.left = at.x;
                n.Root.style.top = at.y - n.Lift;
            }
        }

        // A point on the field in the battle frame's coordinates; false behind the camera or well off the frame.
        bool OnScreen(Vector3 world, out Vector2 local)
        {
            local = default;
            if (stage == null || Root.panel == null || !stage.ToScreen(world, out var screen)) return false;
            var top = Root.panel.visualTree.layout;
            local = Root.WorldToLocal(new Vector2(screen.x * top.width / Screen.width, screen.y * top.height / Screen.height));
            return local.x > -200f && local.x < GameRoot.Width + 200f && local.y > -200f && local.y < GameRoot.Height + 200f;
        }

        // Where what happens to someone shows, `at` now: on a noise body's middle on the field; over a party member's
        // model while the camera has it, else on their card. The point on the field, if it is on one, for Pin.
        Vector3? Anchor(Combatant c, out Vector2 at)
        {
            var view = ViewOf(c);
            if (view != null)
            {
                at = view.Center;
                return stage != null ? stage.FoePoint(enemies.IndexOf(view)) : (Vector3?)null;
            }
            var card = CardOf(c);
            var i = party.IndexOf(card);
            if (stage != null && i >= 0 && OnScreen(stage.MemberPoint(i), out at) && at.x > 80f && at.x < GameRoot.Width - 80f && at.y > 80f && at.y < GameRoot.Height - 80f)
                return stage.MemberPoint(i);
            at = card != null ? CenterOf(card.Root) : new Vector2(1500, 840);
            return null;
        }

        // 标题党型's weakness tag lies until a hit shows the truth (design/systems.md 假弱点): a hit on its true
        // weakness shatters the tag, one on the element it names turns the tag to 「？？？」 (ApplyHit). Only the
        // scripted tutorial still breaks the tags at the second turn, as DeepSeek's line there says.
        async Awaitable TurnBeats(int turn)
        {
            var s = session;
            Quiet(false);
            if (turn == 2 && def.Tutorial) await Expose();
            if (!def.Tutorial || s.Over) return;
            var node = Tutorial.HintAtTurnStart(turn);
            if (node != null && !s.Over) await story.RunHint(node, this);
            if (turn == 1 && !s.Over)
            {
                // DeepSeek's warning plants the doubt: the fake 「弱点：火」 flickers to 「？？？」.
                game.Audio.Sfx("glitch", 0.6f);
                foreach (var v in enemies.Where(v => v.Unit.WeaknessLabel != v.Unit.TrueWeakness)) await v.Flicker("？？？");
            }
        }

        // The lying weakness tags shatter into the truth on every noise body standing, as soon as a hit lands on
        // the true weakness (or at the tutorial's second turn).
        async Awaitable Expose()
        {
            seenThrough = true;
            var liars = enemies.Where(v => !v.Unit.Dead && v.Unit.WeaknessLabel != v.Unit.TrueWeakness).ToList();
            if (liars.Count == 0) return;
            Exposures++;
            game.Audio.Sfx("crack");
            foreach (var v in liars)
            {
                v.Peek(0.9f);
                v.Shatter().Forget();
            }
            await Anim.Wait(0.6f);
        }

        // Returns true when the battle is won.
        async Awaitable<bool> PartyTurn(PartyCard card, int turn)
        {
            var s = session;
            var unit = card.Unit;
            unit.Guarding = false;
            stage?.Unguard(party.IndexOf(card));
            // Knocked down by a blow since their last turn, they get up now, and can be knocked down again.
            unit.Down = false;
            stage?.Rise(party.IndexOf(card));
            card.Refresh();
            var oneMore = false;
            try
            {
                while (!s.Over)
                {
                    foreach (var p in party) p.SetActive(p == card);
                    actor = party.IndexOf(card);
                    stage?.Move(stage.Behind(actor), 0.45f);
                    stage?.Ready(actor, oneMore);
                    stage?.Facing(actor);
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
                    stage?.Punch(7f, 0.6f);
                    // In the 对抗样本！'s place, clear of the bodies lying there and their DOWN.
                    ClearWeakStamp();
                    var spot = OneMoreSpot(out var zoom);
                    var stamp = Fx.Stamp(fx, "ONE MORE!", Palette.Green, 120f, -8f, spot, zoomFrom: zoom);
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
                actor = -1;
                stage?.Ready(-1);
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
                        var attack = Skills.NormalAttack(unit.Id);
                        var t = await ChooseTarget(enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit), attack.Element);
                        if (t != null) return new BattleAction { Command = "attack", Skill = attack, Target = t };
                        break;
                    }
                    case "skill":
                    {
                        var skill = await ChooseSkill(unit, rule);
                        if (skill == null) break;
                        if (skill.AllTargets) return new BattleAction { Command = "skill", Skill = skill };
                        var t = await ChooseTarget(enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit), skill.Element);
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
                        // Food for a member, an attack item at a noise body.
                        var used = GameData.Items[item];
                        var t = used.Attacks
                            ? await ChooseTarget(enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit), used.Element)
                            : await ChooseTarget(party.Where(p => !p.Unit.Dead).Select(p => p.Unit));
                        if (t != null) return new BattleAction { Command = "item", ItemId = item, Target = t };
                        break;
                    }
                }
            }
            return null;
        }

        void ShowCommands(Combatant unit, TurnRule rule)
        {
            Quiet(false);
            commandBox.Clear();
            commands.Clear();
            commandIds.Clear();
            actorBanner.text = unit.Id == GameState.PlayerId ? "我的回合" : $"{unit.Name} 的回合";
            var options = new List<(string Id, string Label, string Tag)> { ("attack", unit.Id == GameState.PlayerId ? "攻击（截图）" : "攻击", "ATTACK") };
            if (Skills.MagicOf(unit.Id).Count > 0) options.Add(("skill", "技能", "SKILL"));
            if (Skills.CanThink(unit.Id)) options.Add(("think", "深度思考", "THINK"));
            options.Add(("guard", "防御", "GUARD"));
            options.Add(("item", "道具", "ITEM"));
            foreach (var (id, label, tag) in options)
            {
                // Each row a step further right, so the list leans like the slants.
                var item = Ui.El("sp-command", commandBox);
                item.style.marginLeft = commandIds.Count * 16f;
                Shape.Backdrop(item, Shape.Slant(18), Palette.Ink.WithAlpha(0.92f)).Stroke(Palette.Paper, 2).Shadow(new Vector2(6, 6), Palette.Dim);
                Ui.El("sp-command__mark", item);
                Ui.Text(label, "sp-command__text", item, FontKind.Black);
                Ui.Text(tag, "sp-command__tag", item, FontKind.MonoBold);
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
            item.Q<Shape>().Fill(on ? Palette.Green : Palette.Ink.WithAlpha(0.92f)).Shadow(new Vector2(6, 6), on ? Palette.Ink : Palette.Dim);
            item.style.translate = new Translate(on ? 34f : 0f, 0);
        }

        bool HasUsableItems() => game.State.inventory.Any(st => GameData.Items.TryGetValue(st.id, out var d) && (d.Heals || d.Attacks) && st.count > 0);

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
                if (!GameData.Items.TryGetValue(stack.id, out var item) || !item.Heals && !item.Attacks) continue;
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

        // `element` is what the action would hit with, for the forecast over the plate aimed at. The cursor starts
        // on the noise body this member went for last while it still stands (not in the scripted tutorial), else
        // on the first standing.
        async Awaitable<Combatant> ChooseTarget(IEnumerable<Combatant> candidates, Element? element = null)
        {
            var list = candidates.ToList();
            if (list.Count == 0) return null;
            Quiet(false);
            targets = list;
            aiming = element;
            var who = actor >= 0 ? party[actor].Unit.Id : null;
            var last = !def.Tutorial && who != null && lastTarget.TryGetValue(who, out var before) && !before.Down ? list.IndexOf(before) : -1;
            targetIndex = last >= 0 ? last : Mathf.Max(0, list.FindIndex(t => !t.Down));
            RefreshTargets();
            var i = await WaitPick(Mode.Target);
            targets = new List<Combatant>();
            aiming = null;
            RefreshTargets();
            if (i < 0 && actor >= 0) stage?.Move(stage.Behind(actor), 0.3f);
            if (i < 0 || i >= list.Count) return null;
            if (who != null && list[i].IsEnemy) lastTarget[who] = list[i];
            return list[i];
        }

        // What the aimed element is known to do to a noise body: shown by a hit before, or by its weakness tag
        // once that tells the truth. Unknown till then, so a 标题党型's fake 「弱点：火」 gets no WEAK.
        string Forecast(Combatant c)
        {
            if (aiming == null || !c.IsEnemy) return null;
            var e = aiming.Value;
            var known = c.Known.Contains(e) || c.WeaknessLabel == c.TrueWeakness && Battles.ElementLabel(e) == c.TrueWeakness;
            if (!known) return null;
            return c.AffinityTo(e) switch { Affinity.Weak => "WEAK", Affinity.Resist => "RESIST", Affinity.Null => "BLOCK", _ => null };
        }

        void RefreshTargets()
        {
            var current = targets.Count > 0 ? targets[targetIndex] : null;
            foreach (var v in enemies) v.SetTargeted(v.Unit == current, v.Unit == current ? Forecast(v.Unit) : null);
            foreach (var p in party) p.Root.EnableInClassList("is-targeted", p.Unit == current);
            // The camera turns over the shoulder to the noise body under the cursor, which looks up, wary.
            var foe = enemies.FindIndex(v => v.Unit == current);
            stage?.Pick(foe);
            if (foe >= 0 && actor >= 0) stage?.Move(stage.Behind(actor, foe), 0.3f);
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
            var member = party.IndexOf(card);
            Quiet(true);
            switch (action.Command)
            {
                case "attack":
                {
                    var view = ViewOf(action.Target);
                    SkillBar(unit, action.Skill.Name).Forget();
                    card?.Face.Play(FaceAct.Attack);
                    // Known before the blow, so a miss isn't held; the hit lands on the frame the blow does.
                    var hit = DamageCalc.Compute(unit, action.Target, action.Skill, def);
                    if (unit.Id == GameState.PlayerId)
                    {
                        // The player shoots from where they stand, the phone up; DeepSeek runs in and strikes, a
                        // whoosh as each swing comes round.
                        stage?.Shoot(member, enemies.IndexOf(view));
                        await ScreenshotFx(view, member, hit.Missed);
                    }
                    else if (stage != null)
                    {
                        await stage.Dash(member, enemies.IndexOf(view), () => game.Audio.Sfx("whoosh", 0.8f), hit.Missed);
                        stage.Slash(enemies.IndexOf(view), hit.Missed);
                    }
                    else game.Audio.Sfx("whoosh", 0.8f);
                    await ApplyHit(action.Target, hit, false, true);
                    return hit.KnockedDown;
                }
                case "skill":
                {
                    var skill = action.Skill;
                    unit.Sp -= skill.SpCost;
                    card?.Refresh();
                    await CutIn(unit, skill.Name);
                    var hitTargets = skill.AllTargets ? enemies.Where(v => !v.Unit.Dead).Select(v => v.Unit).ToList() : new List<Combatant> { action.Target };
                    card?.Face.Play(FaceAct.Attack);
                    // The charge of 深度思考 goes into the cast.
                    if (unit.Charged) stage?.Charged(member, false);
                    await SpellFx(skill, member, hitTargets.Select(ViewOf).ToList(), unit.Charged);
                    var hits = hitTargets.Select(t => (t, DamageCalc.Compute(unit, t, skill, def))).ToList();
                    if (unit.Charged)
                    {
                        unit.Charged = false;
                        card?.Refresh();
                    }
                    // A blow that misses shows nothing of the weakness it would have found: the stamp went up over a MISS.
                    var stamped = hits.Any(h => h.Item2.Affinity == Affinity.Weak && !h.Item2.Missed);
                    if (stamped) WeakStamp(hits.Where(h => !h.Item2.Missed).Select(h => h.t), 0.16f);
                    var anyDown = false;
                    foreach (var (t, hit) in hits)
                    {
                        ApplyHit(t, hit, stamped, true).Forget();
                        anyDown |= hit.KnockedDown;
                        await Anim.Wait(0.12f);
                    }
                    if (!skill.AllTargets && hits[0].Item2.KnockedDown && !action.Target.Dead) stage?.Floored(enemies.IndexOf(ViewOf(action.Target)));
                    await Aftermath(0.7f);
                    return anyDown;
                }
                // Struck where they stand on the field (BattleStage.Think, Guard, UseItem) under the skill bar.
                case "think":
                {
                    var posed = stage?.Think(member);
                    SkillBar(unit, "深度思考").Forget();
                    unit.Charged = true;
                    card?.Refresh();
                    stage?.Charged(member, true);
                    await ThinkFx(card);
                    if (posed != null) await posed;
                    return false;
                }
                case "guard":
                {
                    unit.Guarding = true;
                    card?.Refresh();
                    var posed = stage?.Guard(member, unit.Id == GameState.PlayerId); // the player's things are green
                    SkillBar(unit, "防御").Forget();
                    if (posed != null) await posed;
                    return false;
                }
                case "item":
                {
                    var item = GameData.Items[action.ItemId];
                    if (!game.State.UseItem(action.ItemId)) return false;
                    if (item.Attacks) return await Throw(unit, item, action.Target);
                    var target = CardOf(action.Target);
                    var posed = stage?.UseItem(member, party.IndexOf(target));
                    SkillBar(unit, item.Name).Forget();
                    if (posed != null) await posed; // healed as the rice ball goes
                    var heal = action.Target.Id == "DeepSeek" ? item.HealHpForDeepSeek : item.HealHp;
                    action.Target.Hp = Mathf.Min(action.Target.MaxHp, action.Target.Hp + heal);
                    target?.Refresh();
                    game.Audio.Sfx("levelup", 0.6f);
                    if (target != null) Popup(target.Unit, $"+{heal}", Palette.Green, null);
                    await Anim.Wait(0.6f);
                    return false;
                }
            }
            return false;
        }

        // An attack item thrown at one noise body, bursting on it in foam and frost (冰镇汽水) or in flame (辣条).
        // True when it knocked the noise body down (One More).
        async Awaitable<bool> Throw(Combatant unit, ItemDef item, Combatant target)
        {
            var card = CardOf(unit);
            var member = party.IndexOf(card);
            var view = ViewOf(target);
            var fire = item.Element == Element.Fire;
            SkillBar(unit, item.Name).Forget();
            card?.Face.Play(FaceAct.Attack);
            var hit = DamageCalc.Compute(unit, target, Skills.OfItem(item), def);
            if (stage != null)
            {
                await stage.Toss(member, enemies.IndexOf(view), fire, hit.Missed);
                game.Audio.Sfx(fire ? "whoosh" : "water", 0.8f);
            }
            // Thrown wide (hit.Missed) it bursts on nothing: no crack, no jolt, and no stamp for the weakness it missed.
            var weak = hit.Affinity == Affinity.Weak && !hit.Missed;
            if (!hit.Missed)
            {
                game.Audio.Sfx("crack");
                if (view != null) Fx.Shake(view.Root, 16f, 0.25f);
            }
            if (weak) WeakStamp(new[] { target }, 0.16f);
            await ApplyHit(target, hit, weak, true);
            await Aftermath(0.3f);
            return hit.KnockedDown;
        }

        // A beat after an action for its numbers and bursts to be seen, but not after the one that won the battle:
        // Victory waits for its last blow itself (BattleStage.Dying) and goes up while the cubes still fly.
        Awaitable Aftermath(float seconds) => Anim.Wait(AllEnemiesDead && stage != null ? 0f : seconds);

        // The 对抗样本！ over the blow being dealt, and where it is in the frame from the moment WeakStamp places it.
        VisualElement weakStamp;
        Rect? weakArea;
        int weakStamps;

        // About half a stamp's size at rest on its burst: 对抗样本！ at 110 px, ONE MORE! at 120.
        static readonly Vector2 StampHalf = new Vector2(360f, 170f);
        const float OneMoreHalf = 440f, OneMoreHigh = 170f;
        // The heights ONE MORE! may go at: its own, then over and under it.
        static readonly float[] OneMoreRows = { 380f, 220f, 560f };
        // The 集成！ title's band over the middle of the top of the frame (AllOutFx).
        static readonly Rect AllOutTitle = Rect.MinMaxRect(557f, 0f, 1363f, 367f);

        // 对抗样本！ a beat after the blow (`delay`), so the hit is seen before the stamp: high on the side of the
        // picture away from what was hit (StampSpot), dropping from less far than the other stamps, as it lands
        // beside the action, not over it. Not waited for; it goes 1.1 s later, or when ONE MORE! takes its place.
        void WeakStamp(IEnumerable<Combatant> hit, float delay)
        {
            var at = StampSpot(hit, 250f, StampHalf.x, out var angle);
            var id = ++weakStamps;
            weakArea = new Rect(at - StampHalf, StampHalf * 2f);
            Go().Forget();

            async Awaitable Go()
            {
                await Anim.Wait(delay);
                if (id != weakStamps || fx.panel == null) return;
                game.Audio.Sfx("slam");
                weakStamp = Fx.Stamp(fx, "对抗样本！", Palette.Paper, 110f, angle, at, zoomFrom: 1.35f);
                await DismissAfter(weakStamp, 1.1f);
                if (id != weakStamps) return;
                weakStamp = null;
                weakArea = null;
            }
        }

        void ClearWeakStamp()
        {
            weakStamps++;
            Fx.Dismiss(weakStamp).Forget();
            weakStamp = null;
            weakArea = null;
        }

        // Where a stamp `half` wide either way goes over a blow, `y` down the frame: on the side of the picture away
        // from the noise bodies `on`, clear of their plates, numbers and kill windows (all within about 140 px of
        // their middles), and on the picture; on the side with more room, the right when even. `angle` leans it
        // toward the middle.
        Vector2 StampSpot(IEnumerable<Combatant> on, float y, float half, out float angle)
        {
            var xs = on.Select(ViewOf).Where(v => v != null).Select(v => v.Center.x).DefaultIfEmpty(GameRoot.Width / 2f).ToList();
            const float Clear = 180f;
            var left = Mathf.Max(half + 10f, Mathf.Min(110f + half, xs.Min() - Clear - half));
            var right = Mathf.Min(GameRoot.Width - half - 10f, Mathf.Max(GameRoot.Width - 110f - half, xs.Max() + Clear + half));
            var toRight = right - half - xs.Max() >= xs.Min() - left - half;
            angle = toRight ? -10f : 8f;
            return new Vector2(toRight ? right : left, y);
        }

        // Where ONE MORE! goes: where StampSpot puts it, unless that covers a noise body lying knocked down, its plate
        // or its DOWN; a body lying in a close shot lies well beside its plate and can fill half the picture
        // (BattleStage.FoeOnScreen). Then wherever across the picture, at that height or over or under it, covers
        // least of them, the nearest to that first place of those about as clear. `zoomFrom`: how far it drops from,
        // no more than its nearest edge leaves room for, so the frame never cuts it.
        Vector2 OneMoreSpot(out float zoomFrom)
        {
            var downed = new List<int>();
            for (var i = 0; i < enemies.Count; i++)
                if (enemies[i].Unit.Down && !enemies[i].Unit.Dead) downed.Add(i);
            var first = StampSpot(downed.Select(i => enemies[i].Unit), OneMoreRows[0], OneMoreHalf, out _);
            var keep = new List<Rect>();
            foreach (var i in downed)
            {
                var c = enemies[i].Center;
                keep.Add(new Rect(c.x - 180f, c.y - 140f, 360f, 280f)); // its plate, numbers and DOWN
                if (stage == null || Root.panel == null || !stage.FoeOnScreen(i, out var body)) continue;
                var top = Root.panel.visualTree.layout;
                var scale = new Vector2(top.width / Screen.width, top.height / Screen.height);
                Vector2 a = Root.WorldToLocal(Vector2.Scale(body.min, scale)), b = Root.WorldToLocal(Vector2.Scale(body.max, scale));
                keep.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }
            var half = new Vector2(OneMoreHalf, OneMoreHigh);
            var at = first;
            if (Covered(first) > 0f)
            {
                var best = Mathf.Round(Covered(first) / 4000f) * 1e6f;
                foreach (var y in OneMoreRows)
                for (var col = 0; col <= 8; col++)
                {
                    var p = new Vector2(Mathf.Lerp(half.x + 10f, GameRoot.Width - half.x - 10f, col / 8f), y);
                    // About as clear (to 4000 px²) counts as clear, then the nearest wins.
                    var score = Mathf.Round(Covered(p) / 4000f) * 1e6f + (p - first).magnitude;
                    if (score < best)
                    {
                        best = score;
                        at = p;
                    }
                }
            }
            var room = Mathf.Min(Mathf.Min(at.x, GameRoot.Width - at.x) / half.x, Mathf.Min(at.y, GameRoot.Height - at.y) / half.y);
            zoomFrom = Mathf.Clamp(room, 1f, 2.4f);
            return at;

            float Covered(Vector2 p)
            {
                var r = new Rect(p - half, half * 2f);
                var sum = 0f;
                foreach (var k in keep)
                {
                    float w = Mathf.Min(r.xMax, k.xMax) - Mathf.Max(r.xMin, k.xMin), h = Mathf.Min(r.yMax, k.yMax) - Mathf.Max(r.yMin, k.yMin);
                    if (w > 0f && h > 0f) sum += w * h;
                }
                return sum;
            }
        }

        // The stamp up over this blow in the viewport (y up), for a kill's window to keep clear of: the 对抗样本！
        // where WeakStamp put it, else the 集成！ title.
        Rect StampViewport()
        {
            var a = weakArea ?? AllOutTitle;
            if (Root.panel == null) return Rect.MinMaxRect(a.xMin / GameRoot.Width, 1f - a.yMax / GameRoot.Height, a.xMax / GameRoot.Width, 1f - a.yMin / GameRoot.Height);
            var top = Root.panel.visualTree.layout;
            Vector2 lo = Root.LocalToWorld(new Vector2(a.xMin, a.yMax)), hi = Root.LocalToWorld(new Vector2(a.xMax, a.yMin));
            return Rect.MinMaxRect(lo.x / top.width, 1f - lo.y / top.height, hi.x / top.width, 1f - hi.y / top.height);
        }

        // `stamped`: a stamp is up for this blow (StampViewport: BattleStage.Vanish keeps the kill's window clear of it).
        // `finisher`: a party member's own blow (not the 集成's): its target's plate flashes up through the quiet, and
        // it gets the kill shot when it leaves no noise body standing (BattleStage.Finish; of several falling to one
        // skill, the last of them).
        async Awaitable ApplyHit(Combatant target, Hit hit, bool stamped = false, bool finisher = false)
        {
            var view = ViewOf(target);
            var card = CardOf(target);
            if (hit.Missed)
            {
                Popup(target, "MISS", Palette.Gray, null);
                return;
            }
            target.Hp = Mathf.Max(target.HpFloor, target.Hp - hit.Damage);
            var tag = hit.Critical ? "CRITICAL" : hit.Affinity switch { Affinity.Weak => "WEAK", Affinity.Resist => "RESIST", Affinity.Null => "BLOCK", _ => null };
            Popup(target, hit.Damage.ToString(), view != null ? Palette.Paper : Palette.Red, tag);
            game.Audio.Sfx(hit.Affinity == Affinity.Weak || hit.Critical ? "crack" : "slap", 0.8f);
            if (view != null)
            {
                var foe = enemies.IndexOf(view);
                if (finisher) view.Peek(0.8f);
                // What the hit showed stays on its plate (耐性), and one on the true weakness gives the lie away. One
                // on the element the tag names that turns out resisted shows the tag lied, not what the truth is.
                if (hit.Affinity != Affinity.Normal) target.Known.Add(hit.Element);
                if (hit.Affinity == Affinity.Weak && target.WeaknessLabel != target.TrueWeakness) Expose().Forget();
                else if (hit.Affinity == Affinity.Resist && target.WeaknessLabel != target.TrueWeakness && target.WeaknessLabel != "？？？"
                    && Battles.ElementLabel(hit.Element) == target.WeaknessLabel)
                {
                    LiesDoubted++;
                    game.Audio.Sfx("glitch", 0.6f);
                    view.Flicker("？？？").Forget();
                }
                view.Refresh();
                Fx.Shake(view.Root, 18f, 0.3f);
                stage?.Hit(foe);
                if (hit.Affinity == Affinity.Weak || hit.Critical) stage?.Impact(foe, hit.Element, hit.Critical);
                if (hit.KnockedDown)
                {
                    target.Down = true;
                    view.SetDown(true);
                    stage?.Down(foe, true);
                }
                if (target.Dead)
                {
                    Rect? stamp = stamped ? StampViewport() : null;
                    if (finisher && AllEnemiesDead) stage?.Finish(foe, actor, stamp).Forget();
                    else stage?.Vanish(foe, stamp).Forget();
                    await view.Die();
                }
            }
            else if (card != null)
            {
                var member = party.IndexOf(card);
                card.Refresh();
                card.Shake();
                card.Face.Play(FaceAct.Hit);
                stage?.Recoil(member, hit.KnockedDown && !target.Dead);
                if (target.Dead) stage?.Faint(member, true);
                if (hit.KnockedDown) target.Down = true;
            }
            await Anim.Wait(0.25f);
        }

        async Awaitable EnemyTurn(EnemyView view)
        {
            var e = view.Unit;
            var foe = enemies.IndexOf(view);
            Quiet(true);
            if (e.Down)
            {
                stage?.Move(stage.Watch(foe), 0.4f);
                // Downed enemies spend their turn getting up.
                e.Down = false;
                view.SetDown(false);
                stage?.Down(foe, false);
                calledLastTurn.Remove(e);
                await Anim.Wait(0.55f);
                return;
            }
            // 转发型 calls in a 标题党型 every other turn while the line has room (design/systems.md; never in the
            // scripted tutorial), and hits the party in between.
            if (e.Kind == "repost" && !def.Tutorial && !calledLastTurn.Contains(e) && summons < MaxSummons && enemies.Count(v => !v.Unit.Dead) < 3)
            {
                calledLastTurn.Add(e);
                await Call(view);
                return;
            }
            calledLastTurn.Remove(e);
            var skill = e.Kind == "repost" ? Skills.Repost : Skills.NoiseStrike;
            var alive = party.Where(p => !p.Unit.Dead).ToList();
            if (alive.Count == 0) return;
            var victims = skill.AllTargets ? alive : new List<PartyCard> { alive[UnityEngine.Random.Range(0, alive.Count)] };
            // At one member: in behind them, the noise body over them; at the party: beside them, at it.
            if (victims.Count == 1) stage?.Move(stage.Threat(foe, party.IndexOf(victims[0])), 0.35f);
            else stage?.Move(stage.Watch(foe), 0.4f);
            stage?.Brace(foe, victims.Count == 1 ? party.IndexOf(victims[0]) : -1); // winds up under its skill's name
            await EnemyBar(view, skill.Name);
            await StrikeFx(view, skill, victims);
            foreach (var card in victims)
            {
                var hit = DamageCalc.Compute(e, card.Unit, skill, def);
                await ApplyHit(card.Unit, hit);
            }
            await Anim.Wait(0.25f);
        }

        // A weaker 标题党型 joins the line, its weakness tag lying like the others' until the party has seen through it.
        async Awaitable Call(EnemyView caller)
        {
            var from = enemies.IndexOf(caller);
            stage?.Move(stage.Watch(from), 0.4f);
            stage?.Brace(from);
            await EnemyBar(caller, "转发到十个群");
            var unit = Battles.Spawn(Battles.Summoned);
            unit.Summoned = true;
            if (seenThrough) unit.WeaknessLabel = unit.TrueWeakness;
            summons++;
            CalledIn++;
            game.Audio.Sfx("glitch");
            stage?.Move(stage.Overview, 0.5f);
            var rise = stage?.Summon(unit.Kind, from); // on the field before its plate, which Pin puts over it
            var view = AddEnemy(unit);
            if (rise != null) await rise;
            Fx.Shake(view.Root, 12f, 0.25f);
            await Anim.Wait(0.3f);
        }

        async Awaitable<bool> AllOut()
        {
            var s = session;
            game.Audio.Sfx("slam");
            Quiet(false); // every DOWN in view for the hold-up
            stage?.Move(stage.HoldUp, 0.6f);
            stage?.Cower(true);
            var stamp = Fx.Stamp(fx, "全员倒地！", Palette.Paper, 120f, -6f, new Vector2(960, 300));
            await Anim.Wait(1f);
            await Fx.Dismiss(stamp);
            if (def.Tutorial && !s.Over) await story.RunHint(Tutorial.AllOutHint, this);
            if (s.Over)
            {
                stage?.Cower(false);
                return false;
            }

            promptMenu.SetEnabled(1, !def.Tutorial);
            promptMenu.Select(0, false);
            Ui.Show(promptBox, true);
            Anim.Tween(0.2f, t => Ui.Zoom(promptBox, Mathf.Lerp(1.4f, 1f, t)), Ease.OutBack).Forget();
            var choice = await WaitPick(Mode.Prompt);
            Ui.Show(promptBox, false);
            stage?.Cower(false);
            if (s.Over || choice != 0) return false;

            var participants = party.Where(p => !p.Unit.Dead).Select(p => p.Unit).ToList();
            foreach (var u in participants) u.Down = false; // up for it, knocked down or not (BattleStage.AllOut)
            var damage = participants.Sum(u => Mathf.RoundToInt(u.Str + u.Mag)) * 4;
            Quiet(true, true);
            await AllOutFx(participants, damage);
            if (!AllEnemiesDead) Quiet(false);
            return AllEnemiesDead;
        }

        async Awaitable Victory()
        {
            var s = session;
            // The noise bodies burst first; the battle's last blow is seen a moment, its cubes still flying under the
            // camera going up behind the party (BattleStage.Dying).
            while (stage != null && stage.Dying && !s.Over) await Awaitable.NextFrameAsync();
            stage?.Move(stage.Victory, 1.2f); // up behind the party, under the results
            var shutter = stage != null ? stage.Cheer() : -1f;
            foreach (var card in party.Where(c => !c.Unit.Dead)) card.Face.Play(FaceAct.Victory);
            // The cards' faces win before the results cover them; the player's phone takes the field on the way.
            if (shutter >= 0f)
            {
                await Anim.Wait(shutter);
                if (s.Over) return;
                game.Audio.Sfx("shutter", 0.5f);
            }
            await Anim.Wait(1.2f - Mathf.Max(0f, shutter));
            if (s.Over) return;
            var rewards = ApplyRewards();
            game.ResultsMusic();
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

        // EXP and drops go into GameState; HP/SP carry over and grow with a level up. A battle opened from behind
        // pays the preemptive bonus (Battles.PreemptiveExp, PreemptiveDrops).
        List<Reward> ApplyRewards()
        {
            var state = game.State;
            var rewards = new List<Reward>();
            earned = def.Exp + enemies.Count(v => v.Unit.Summoned && v.Unit.Dead) * Battles.SummonedExp;
            if (Preemptive) earned = Mathf.RoundToInt(earned * Battles.PreemptiveExp);
            foreach (var card in party)
            {
                var u = card.Unit;
                var m = state.Member(u.Id);
                m.hp = Mathf.Max(1, u.Hp);
                m.sp = u.Sp;
                var from = m.level;
                var expFrom = m.exp;
                if (!u.Dead) m.exp += earned;
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
            dropped = (def.Drops ?? new (string, int)[0]).Select(d => (d.Id, Preemptive ? d.Count * Battles.PreemptiveDrops : d.Count)).ToArray();
            foreach (var (id, count) in dropped) state.AddItem(id, count);
            return rewards;
        }

        async Awaitable Defeat()
        {
            game.Audio.Sfx("glitch");
            stage?.Move(stage.Overview, 1f);
            Fx.Stamp(fx, "掉线……", Palette.Red, 140f, -6f, new Vector2(960, 480));
            await Anim.Wait(2.5f);
            game.ShowTitle(Wipe.Lost);
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
