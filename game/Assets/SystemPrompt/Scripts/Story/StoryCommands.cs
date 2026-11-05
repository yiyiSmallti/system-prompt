using System;
using SystemPrompt.UI;
using UnityEngine;
using Yarn.Unity;

namespace SystemPrompt.Story
{
    // The commands listed in the header of story/00-prologue.yarn. Each one changes GameState first and
    // then plays its presentation, so skipping or a missing asset never loses progress.
    public static class StoryCommands
    {
        // The Yarn variable <<nickname_ask 角色>> writes and the phone profile reads.
        public static string NicknameVariable(string who) => "$player_calls_" + who.ToLowerInvariant();

        // setString and setBool write Yarn variables of the run these commands belong to.
        public static void Register(DialogueRunner runner, GameRoot game, StoryScreen screen, Func<Func<Awaitable>, Awaitable> guard,
            Action<string, string> setString, Action<string, bool> setBool)
        {
            var state = (Func<GameState>)(() => game.State);

            runner.AddCommandHandler("calendar", (Func<int, int, string, Awaitable>)((month, day, slot) => guard(() =>
            {
                state().SetDate(month, day, slot);
                return screen.ShowCalendar(month, day, slot);
            })));

            runner.AddCommandHandler("scene", (Func<string, Awaitable>)(id => guard(() => screen.ChangeScene(id))));

            runner.AddCommandHandler("contact_add", (Func<string, Awaitable>)(who => guard(() =>
            {
                if (state().AddContact(who))
                {
                    game.Phone.MarkUnseen(PhoneModal.ContactsApp);
                    screen.PulsePhone();
                }
                return Done();
            })));

            runner.AddCommandHandler("effort_max", (Func<string, string, Awaitable>)((who, form) => guard(() =>
            {
                state().Awaken(who, form);
                return screen.Awakening(who, form);
            })));

            runner.AddCommandHandler("party_join", (Func<string, Awaitable>)(who => guard(() =>
            {
                state().JoinParty(who);
                return Done();
            })));

            runner.AddCommandHandler("battle", (Func<string, Awaitable>)(id => guard(() => game.RunBattle(id))));

            runner.AddCommandHandler("dungeon", (Func<string, Awaitable>)(id => guard(() => game.RunDungeon(id))));

            runner.AddCommandHandler("nickname_ask", (Func<string, Awaitable>)(who => guard(async () =>
            {
                var name = await game.Nickname.Ask(who);
                setString(NicknameVariable(who), name);
                setBool(NicknameVariable(who) + "_sore", GameData.NicknameSore(who, name));
            })));

            runner.AddCommandHandler("coop_rank", (Func<string, int, Awaitable>)((who, rank) => guard(() =>
            {
                state().SetCoopRank(who, rank);
                game.Phone.MarkUnseen(PhoneModal.CoopApp);
                return screen.CoopCard(who, rank);
            })));

            runner.AddCommandHandler("ability_unlock", (Func<string, Awaitable>)(id => guard(() =>
            {
                if (!GameData.Abilities.ContainsKey(id)) Debug.LogError($"<<ability_unlock {id}>>：GameData.Abilities 里没有这个能力");
                state().UnlockAbility(id);
                game.Phone.MarkUnseen(PhoneModal.InventoryApp);
                return Done();
            })));

            runner.AddCommandHandler("deadline_set", (Func<string, int, int, Awaitable>)((id, month, day) => guard(() =>
            {
                state().SetDeadline(id, month, day);
                return screen.DeadlineSet();
            })));

            runner.AddCommandHandler("chapter_end", (Func<string, Awaitable>)(id => guard(() => screen.ChapterEnd(id))));
        }

        // The commands of the nodes played inside the maze or a battle (StoryController.RunHint).
        public static void RegisterHints(DialogueRunner runner, GameRoot game, Func<Func<Awaitable>, Awaitable> guard)
        {
            runner.AddCommandHandler("mirror", (Func<string, Awaitable>)(state => guard(() => game.Dungeon.Mirror(state == "on"))));
        }

        static async Awaitable Done()
        {
            await Awaitable.NextFrameAsync();
        }
    }
}
