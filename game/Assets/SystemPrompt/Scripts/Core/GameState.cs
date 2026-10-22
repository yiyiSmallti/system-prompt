using System;
using System.Collections.Generic;
using UnityEngine;

namespace SystemPrompt
{
    [Serializable]
    public class CoopEntry
    {
        public string character;
        public int rank;
        public int points;
    }

    [Serializable]
    public class PartyMember
    {
        public string id;
        public int level = 1;
        public int exp;
        public int hp;
        public int sp;
    }

    [Serializable]
    public class DeadlineEntry
    {
        public string id;
        public int month;
        public int day;
    }

    [Serializable]
    public class ItemStack
    {
        public string id;
        public int count;
    }

    [Serializable]
    public class FormEntry
    {
        public string character;
        public string form;
    }

    // Everything the story commands change. Saved as JSON, so only lists of plain fields.
    [Serializable]
    public class GameState
    {
        public const string PlayerId = "player";

        public string playerName = "";
        public string playerGender = "male";
        public int month = 4;
        public int day = 7;
        public string slot = "morning";
        public string scene = "";
        public string chapter = "prologue";
        public List<string> contacts = new List<string>();
        public List<string> met = new List<string>();
        public List<CoopEntry> coops = new List<CoopEntry>();
        public List<string> abilities = new List<string>();
        public List<string> party = new List<string> { PlayerId };
        public List<PartyMember> members = new List<PartyMember>();
        public List<FormEntry> forms = new List<FormEntry>();
        public List<DeadlineEntry> deadlines = new List<DeadlineEntry>();
        public List<ItemStack> inventory = new List<ItemStack>();
        public int tokens;

        public GameState Clone() => JsonUtility.FromJson<GameState>(JsonUtility.ToJson(this));

        public string DateLabel => $"{month}/{day} {GameData.WeekdayLabel(month, day)}";

        public void SetDate(int newMonth, int newDay, string newSlot)
        {
            month = newMonth;
            day = newDay;
            slot = newSlot;
        }

        public bool AddContact(string id)
        {
            Meet(id);
            if (contacts.Contains(id)) return false;
            contacts.Add(id);
            return true;
        }

        public void Meet(string id)
        {
            if (GameData.Character(id) == null || id == "影子DeepSeek") return;
            if (!met.Contains(id)) met.Add(id);
        }

        public CoopEntry Coop(string character) => coops.Find(c => c.character == character);

        public CoopEntry SetCoopRank(string character, int rank)
        {
            var entry = Coop(character);
            if (entry == null)
            {
                entry = new CoopEntry { character = character };
                coops.Add(entry);
            }
            if (rank > entry.rank) entry.points = 0;
            entry.rank = rank;
            return entry;
        }

        public bool UnlockAbility(string id)
        {
            if (abilities.Contains(id)) return false;
            abilities.Add(id);
            return true;
        }

        public string FormOf(string character) => forms.Find(f => f.character == character)?.form;

        public void Awaken(string character, string form)
        {
            var entry = forms.Find(f => f.character == character);
            if (entry == null) forms.Add(new FormEntry { character = character, form = form });
            else entry.form = form;
        }

        public void JoinParty(string id)
        {
            if (!party.Contains(id)) party.Add(id);
            Member(id);
        }

        public PartyMember Member(string id)
        {
            var m = members.Find(x => x.id == id);
            if (m != null) return m;
            var stats = Battle.CombatStats.ForCharacter(id, 1);
            m = new PartyMember { id = id, level = 1, hp = stats.MaxHp, sp = stats.MaxSp };
            members.Add(m);
            return m;
        }

        public void SetDeadline(string id, int dMonth, int dDay)
        {
            var entry = deadlines.Find(d => d.id == id);
            if (entry == null) deadlines.Add(new DeadlineEntry { id = id, month = dMonth, day = dDay });
            else { entry.month = dMonth; entry.day = dDay; }
        }

        public DeadlineEntry NextDeadline()
        {
            DeadlineEntry best = null;
            var bestDays = int.MaxValue;
            foreach (var d in deadlines)
            {
                var days = DaysUntil(d);
                if (days < 0 || days >= bestDays) continue;
                best = d;
                bestDays = days;
            }
            return best;
        }

        public int DaysUntil(DeadlineEntry d) => GameData.DaysBetween(month, day, d.month, d.day);

        public int ItemCount(string id) => inventory.Find(i => i.id == id)?.count ?? 0;

        public void AddItem(string id, int count)
        {
            var stack = inventory.Find(i => i.id == id);
            if (stack == null) inventory.Add(new ItemStack { id = id, count = count });
            else stack.count += count;
        }

        public bool UseItem(string id)
        {
            var stack = inventory.Find(i => i.id == id);
            if (stack == null || stack.count <= 0) return false;
            stack.count--;
            if (stack.count == 0) inventory.Remove(stack);
            return true;
        }
    }
}
