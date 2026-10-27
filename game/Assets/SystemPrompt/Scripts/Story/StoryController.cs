using System;
using System.Collections.Generic;
using System.Linq;
using SystemPrompt.UI;
using UnityEngine;
using Yarn.Unity;

#nullable enable

namespace SystemPrompt.Story
{
    // Where a runner's lines end up: the story screen, or the battle screen for in-battle hints.
    public interface ILinePresenter
    {
        Awaitable PresentLine(LineInfo line, Func<bool> nextRequested, Func<bool> hurryRequested);
        Awaitable<int> PresentOptions(string[] texts, bool[] available, Func<bool> cancelled);
    }

    public static class LineClassifier
    {
        // Tags come from story/00-prologue.yarn: #inner, #thinking, #whisper, #face:<表情>.
        public static LineInfo Classify(LocalizedLine line, string playerName)
        {
            var speaker = line.CharacterName;
            var info = new LineInfo
            {
                Id = line.TextID,
                Speaker = speaker,
                Text = line.TextWithoutCharacterName.Text.Trim(),
            };
            var tags = new HashSet<string>();
            foreach (var raw in line.Metadata)
            {
                var tag = raw.TrimStart('#');
                if (tag.StartsWith("face:", StringComparison.Ordinal)) info.Face = tag.Substring(5);
                else tags.Add(tag);
            }
            info.Whisper = tags.Contains("whisper");

            if (speaker == null || speaker == GameData.Narrator) info.Kind = LineKind.Narration;
            else if (speaker == GameData.SystemSpeaker) info.Kind = LineKind.System;
            else if (speaker == GameData.ScreenSpeaker) info.Kind = LineKind.Screen;
            else if (speaker == GameData.FileSpeaker) info.Kind = LineKind.File;
            else if (tags.Contains("thinking")) info.Kind = LineKind.Thinking;
            else if (speaker == playerName) info.Kind = tags.Contains("inner") ? LineKind.Inner : LineKind.Player;
            else if (speaker == "影子DeepSeek") info.Kind = LineKind.Shadow;
            else info.Kind = LineKind.Speech;
            return info;
        }
    }

    public sealed class StoryPresenter : DialoguePresenterBase
    {
        public ILinePresenter? Target;
        public Action<string>? NodeEntered;
        public Action? Completed;

        public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            if (Target == null) return YarnTask.CompletedTask;
            var info = LineClassifier.Classify(line, GameRoot.Instance.State.playerName);
            return Target.PresentLine(info, () => token.IsNextContentRequested, () => token.IsHurryUpRequested);
        }

        public override YarnTask<DialogueOption?> RunOptionsAsync(DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken) =>
            RunOptions(dialogueOptions, cancellationToken);

        async Awaitable<DialogueOption?> RunOptions(DialogueOption[] options, LineCancellationToken token)
        {
            if (Target == null) return null;
            var texts = options.Select(o => o.Line.TextWithoutCharacterName.Text.Trim()).ToArray();
            var available = options.Select(o => o.IsAvailable).ToArray();
            var index = await Target.PresentOptions(texts, available, () => token.IsNextContentRequested);
            return index >= 0 ? options[index] : null;
        }

        public override YarnTask OnDialogueStartedAsync() => YarnTask.CompletedTask;

        public override YarnTask OnDialogueCompleteAsync()
        {
            Completed?.Invoke();
            return YarnTask.CompletedTask;
        }

        public override void OnNodeEnter(string nodeName) => NodeEntered?.Invoke(nodeName);
    }

    // Runs the Yarn story for one play session (a new game or a load). A fresh pair of runners is built
    // for every session and destroyed on stop, so an abandoned run can never continue into the next one.
    public sealed class StoryController
    {
        const string ProjectPath = "Story/Prologue";

        readonly GameRoot game;
        readonly StoryScreen screen;
        readonly YarnProject project;
        readonly GameObject host;
        readonly DialogueRunner main;
        readonly DialogueRunner hints;
        readonly InMemoryVariableStorage storage;
        readonly StoryPresenter hintPresenter;
        bool stopped;

        public SaveData? Checkpoint { get; private set; }
        public string? CurrentNode { get; private set; }
        public event Action? Finished;

        public StoryController(GameRoot game, StoryScreen screen)
        {
            this.game = game;
            this.screen = screen;
            project = Resources.Load<YarnProject>(ProjectPath);
            if (project == null) throw new InvalidOperationException($"找不到 Resources/{ProjectPath}.yarnproject，或剧本编译失败（看 Console 里的 Yarn 报错）");

            // Inactive while wiring, so the runners' Awake sees the shared storage instead of making their own.
            host = new GameObject("Story");
            host.SetActive(false);
            host.transform.SetParent(game.transform);
            storage = host.AddComponent<InMemoryVariableStorage>();

            var mainPresenter = host.AddComponent<StoryPresenter>();
            mainPresenter.Target = screen;
            mainPresenter.NodeEntered = OnNodeEntered;
            mainPresenter.Completed = OnCompleted;
            main = CreateRunner(mainPresenter);
            StoryCommands.Register(main, game, screen, Guard);

            hintPresenter = host.AddComponent<StoryPresenter>();
            hints = CreateRunner(hintPresenter);
            StoryCommands.RegisterHints(hints, game, Guard);

            host.SetActive(true);
        }

        DialogueRunner CreateRunner(StoryPresenter presenter)
        {
            var runner = host.AddComponent<DialogueRunner>();
            runner.VariableStorage = storage;
            runner.SetProject(project);
            runner.DialoguePresenters = new DialoguePresenterBase[] { presenter };
            return runner;
        }

        public void StartNew(string playerName, string gender)
        {
            storage.Clear();
            storage.SetValue("$player_name", playerName);
            storage.SetValue("$player_gender", gender);
            main.StartDialogue("Prologue").Forget();
        }

        public void StartFrom(SaveData save)
        {
            var floats = save.floats.ToDictionary(v => v.name, v => v.value);
            var strings = save.strings.ToDictionary(v => v.name, v => v.value);
            var bools = save.bools.ToDictionary(v => v.name, v => v.value);
            storage.SetAllVariables(floats, strings, bools, true);
            if (!project.NodeNames.Contains(save.node))
            {
                Debug.LogError($"存档里的节点 {save.node} 已不在剧本中，从序章开头开始");
                main.StartDialogue("Prologue").Forget();
                return;
            }
            main.StartDialogue(save.node).Forget();
        }

        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            UnityEngine.Object.Destroy(host);
        }

        public string GetString(string variable, string fallback = "") =>
            storage.TryGetValue<string>(variable, out var v) ? v : fallback;

        public bool GetBool(string variable) => storage.TryGetValue<bool>(variable, out var v) && v;

        // Battle hint nodes (Prologue_Battle_Turn1...). Missing nodes are skipped so a battle can be
        // tested before its script exists.
        public async Awaitable RunHint(string node, ILinePresenter target)
        {
            if (stopped) return;
            if (!project.NodeNames.Contains(node))
            {
                Debug.LogWarning($"剧本里没有战斗提示节点 {node}");
                return;
            }
            hintPresenter.Target = target;
            await hints.StartDialogue(node);
            await hints.DialogueTask;
        }

        // Commands that finish after a stop must not hand control back to the destroyed runner.
        async Awaitable Guard(Func<Awaitable> body)
        {
            if (stopped) await Never();
            await body();
            if (stopped) await Never();
        }

        static Awaitable Never() => new AwaitableCompletionSource().Awaitable;

        // Checkpoint at every node start: saving stores the state as it was when the node began,
        // and loading replays that node from its first line.
        void OnNodeEntered(string node)
        {
            CurrentNode = node;
            var (floats, strings, bools) = storage.GetAllVariables();
            Checkpoint = new SaveData
            {
                node = node,
                state = game.State.Clone(),
                floats = floats.Select(kv => new FloatVar { name = kv.Key, value = kv.Value }).ToList(),
                strings = strings.Select(kv => new StringVar { name = kv.Key, value = kv.Value }).ToList(),
                bools = bools.Select(kv => new BoolVar { name = kv.Key, value = kv.Value }).ToList(),
            };
        }

        void OnCompleted()
        {
            if (stopped) return;
            Finished?.Invoke();
        }
    }
}
