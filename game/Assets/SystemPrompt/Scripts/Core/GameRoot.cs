using System.Collections.Generic;
using SystemPrompt.Battle;
using SystemPrompt.Story;
using SystemPrompt.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt
{
    // Owns the UI Toolkit panel, the screens, the modal stack and the flow between title, story, the
    // latent-space maze and battle. The whole UI lives in a fixed 1920x1080 frame that is letterboxed to the
    // window; the 3D camera of the maze and of the battle field draws under it.
    public sealed class GameRoot : MonoBehaviour
    {
        public const float Width = 1920f;
        public const float Height = 1080f;

        public static GameRoot Instance { get; private set; }

        public GameSettings Settings { get; private set; }
        public SystemData Records { get; private set; }
        public AudioService Audio { get; private set; }
        public GameState State { get; private set; } = new GameState();
        public float PlaySeconds { get; private set; }

        public TitleScreen Title { get; private set; }
        public StoryScreen Story { get; private set; }
        public BattleScreen Battle { get; private set; }
        public DungeonScreen Dungeon { get; private set; }

        public Transitions Transitions { get; private set; }

        public GameMenu Menu { get; private set; }
        public BacklogModal Backlog { get; private set; }
        public PhoneModal Phone { get; private set; }
        public ProfileModal Profile { get; private set; }
        public SaveLoadModal SaveLoad { get; private set; }
        public SettingsModal SettingsModal { get; private set; }
        public NewGameModal NewGame { get; private set; }
        public ConfirmModal Confirm { get; private set; }

        VisualElement frame;
        VisualElement modalLayer;
        VisualElement noticeLayer;
        readonly List<Modal> modals = new List<Modal>();
        IInputLayer screen;
        StoryController story;

        public bool ModalOpen => modals.Count > 0;
        public VisualElement Frame => frame;
        public bool InStory => story != null;
        public bool InBattle { get; private set; }
        public bool InDungeon { get; private set; }
        public StoryController StoryRunner => story;
        public bool CanSave => story?.Checkpoint != null && !InBattle && !InDungeon;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Settings = GameSettings.Load();
            Records = SystemData.Load();
            Settings.ApplyDisplay();
            Audio = gameObject.AddComponent<AudioService>();
            Audio.Init(Settings);
            Fonts.Init();
            global::SystemPrompt.Dungeon.SeaArt.WarmText();
            BuildUi();
            EnterTitle();
            Transitions.Boot().Forget();
        }

        void BuildUi()
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "System Prompt Panel";
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/SystemPromptTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int((int)Width, (int)Height);
            panel.screenMatchMode = PanelScreenMatchMode.Expand; // never smaller than 1920x1080, so the frame always fits
            panel.clearColor = false; // the UI camera clears to black; in the maze the 3D shows through

            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            var root = document.rootVisualElement;
            Ui.Fill(root);
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/SystemPrompt"));
            root.AddToClassList("sp-root");
            if (root.panel != null) IgnoreNavigation(root.panel);
            else root.RegisterCallback<AttachToPanelEvent>(e => IgnoreNavigation(e.destinationPanel));

            frame = Ui.El("sp-frame", root);
            var screens = Ui.El("sp-screens", frame);
            Ui.Fill(screens);
            modalLayer = Ui.El("sp-modals", frame);
            Ui.Fill(modalLayer);
            modalLayer.pickingMode = PickingMode.Ignore;
            noticeLayer = Ui.El("sp-notices", frame);
            Ui.Fill(noticeLayer);
            noticeLayer.pickingMode = PickingMode.Ignore;
            Transitions = new Transitions(this, frame);

            Title = new TitleScreen(this, screens);
            Story = new StoryScreen(this, screens);
            Battle = new BattleScreen(this, screens);
            Dungeon = new DungeonScreen(this, screens);

            Menu = new GameMenu(this);
            Backlog = new BacklogModal(this);
            Phone = new PhoneModal(this);
            Profile = new ProfileModal(this);
            SaveLoad = new SaveLoadModal(this);
            SettingsModal = new SettingsModal(this);
            NewGame = new NewGameModal(this);
            Confirm = new ConfirmModal(this);
        }

        // Every menu is driven by GameInput. UI Toolkit's own arrow and Tab navigation would also move the
        // focus, and with nothing focused it lands on the one focusable element: in the registration, ←→ on
        // the gender row put the cursor back in the name field, so the arrows typed instead of switching.
        static void IgnoreNavigation(IPanel panel) =>
            panel.visualTree.RegisterCallback<NavigationMoveEvent>(e => panel.focusController.IgnoreEvent(e), TrickleDown.TrickleDown);

        void Update()
        {
            GameInput.Update();
            if (InStory && !ModalOpen) PlaySeconds += Time.unscaledDeltaTime;
            if (Transitions.Busy)
            {
                Transitions.HandleInput(); // nothing to act on while the screen is covered but hurrying it
                return;
            }
            if (modals.Count > 0) modals[modals.Count - 1].HandleInput();
            else screen?.HandleInput();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Records.Save();
        }

        void OnApplicationQuit() => Records.Save();

        // ------------------------------------------------------------------ modals

        public void OpenModal(Modal m)
        {
            modals.Remove(m);
            modals.Add(m);
            modalLayer.Add(m.Root);
            m.Root.BringToFront();
            Story.SetCovered(modals.Exists(x => x.CoversScreen));
        }

        // The modal removes its own layer once its exit animation has played.
        public void CloseModal(Modal m)
        {
            var wasTop = modals.Count > 0 && modals[modals.Count - 1] == m;
            modals.Remove(m);
            if (wasTop && modals.Count > 0) modals[modals.Count - 1].Regain();
            Story.SetCovered(modals.Exists(x => x.CoversScreen));
        }

        void CloseAllModals()
        {
            for (var i = modals.Count - 1; i >= 0; i--) modals[i].Close();
        }

        public void Notice(string text)
        {
            var n = Ui.El("sp-notice", noticeLayer);
            Shape.Backdrop(n, Shape.Slant(14), Palette.Ink.WithAlpha(0.94f)).Stroke(Palette.Green, 2);
            Ui.Text(text, "sp-notice__text", n, FontKind.Bold);
            NoticeLife(n).Forget();
        }

        static async Awaitable NoticeLife(VisualElement n)
        {
            await Anim.Tween(0.18f, t =>
            {
                n.style.opacity = t;
                n.style.translate = new Translate((1 - t) * 120f, 0);
            });
            await Anim.Wait(1.8f);
            await Ui.FadeOutAndRemove(n, 0.3f);
        }

        // ------------------------------------------------------------------ flow

        void SetScreen(IInputLayer s)
        {
            screen = s;
            Title.Show(s == Title);
            Story.Show(s == Story);
            Battle.Show(s == Battle);
            Dungeon.Show(s == Dungeon);
            // The panel's and the frame's own backdrops would hide the maze's and the battle field's camera.
            var is3d = s == Dungeon || s == Battle;
            frame.parent.EnableInClassList("is-3d", is3d);
            frame.EnableInClassList("is-3d", is3d);
        }

        // Slash from the menu; a lost battle loses the signal, the chapter's end switches off.
        public void ShowTitle(Wipe wipe = Wipe.Slash) => Transitions.Play(wipe, EnterTitle).Forget();

        void EnterTitle()
        {
            StopStory();
            CloseAllModals();
            State = new GameState();
            PlaySeconds = 0;
            SetScreen(Title);
            Title.Enter();
        }

        // The story starts once the wipe has cleared, so its first presentation is seen from the start.
        public void StartNewGame(string playerName, string gender)
        {
            StoryController run = null;
            Transitions.Play(Wipe.Slash,
                () => run = BeginStory(new GameState { playerName = playerName, playerGender = gender }, 0),
                after: () =>
                {
                    if (run == story) run.StartNew(playerName, gender);
                }).Forget();
        }

        public void LoadGame(SaveData data)
        {
            StoryController run = null;
            Transitions.Play(Wipe.Slash,
                () => run = BeginStory(data.state.Clone(), data.playSeconds),
                after: () =>
                {
                    if (run == story) run.StartFrom(data);
                }).Forget();
        }

        public void Continue()
        {
            var latest = SaveSystem.MostRecent();
            if (latest != null) LoadGame(latest.Data);
        }

        StoryController BeginStory(GameState state, float seconds)
        {
            StopStory();
            CloseAllModals();
            State = state;
            PlaySeconds = seconds;
            Phone.ResetSeen();
            Story.Reset();
            Story.RestoreScene();
            SetScreen(Story);
            UpdateMusic();
            story = new StoryController(this, Story);
            story.Finished += OnStoryFinished;
            return story;
        }

        void StopStory()
        {
            Battle.Abort();
            InBattle = false;
            Dungeon.Abort();
            InDungeon = false;
            story?.Stop();
            story = null;
        }

        void OnStoryFinished()
        {
            Records.Save();
            ShowTitle(Wipe.Shutdown);
        }

        // Battles, the maze, or else the story's current scene (StoryScreen calls this when the scene changes).
        public void UpdateMusic()
        {
            if (InBattle) Audio.PlayMusic("battle");
            else if (InDungeon) Audio.PlayMusic("sea");
            else if (GameData.Scenes.TryGetValue(State.scene ?? "", out var def)) Audio.PlayMusic(def.Music);
            else Audio.StopMusic();
        }

        // <<battle>>, or a noise body met in the maze: the caller waits here until the results screen
        // closes, then the screen it came from is back.
        public async Awaitable RunBattle(string id, BattleOpening opening = BattleOpening.Normal)
        {
            var run = story;
            var back = screen;
            InBattle = true;
            Awaitable battle = null;
            await Transitions.Cover(Wipe.Battle);
            try
            {
                if (run == story)
                {
                    SetScreen(Battle);
                    UpdateMusic();
                    battle = Battle.Run(id, run, opening); // sets up the field before its first await, so it is ready to reveal
                }
            }
            finally
            {
                await Transitions.Reveal();
            }
            try
            {
                if (battle != null) await battle;
            }
            finally
            {
                if (run == story) InBattle = false;
            }
            if (run != story) return;
            await Transitions.Play(Wipe.Slash, () =>
            {
                SetScreen(back);
                UpdateMusic();
            });
        }

        // <<dungeon>>: the story waits here while the player explores, until the area ends (its goal, or
        // the maintenance running out).
        public async Awaitable RunDungeon(string id)
        {
            var run = story;
            InDungeon = true;
            Awaitable explore = null;
            var area = global::SystemPrompt.Dungeon.Dungeons.Get(id);
            // The area is built while the dive plays, a piece a frame, and the dive holds on DIVE until it is.
            var building = area != null ? global::SystemPrompt.Dungeon.DungeonWorld.Create(area, State.playerGender, State.FormOf("DeepSeek") != null) : null;
            await Transitions.Cover(Wipe.Dive, area != null ? $"{area.Area} · {area.Layer}" : null);
            try
            {
                var world = building != null ? await building : null;
                if (run == story)
                {
                    SetScreen(Dungeon);
                    UpdateMusic();
                    explore = Dungeon.Run(id, run, world);
                }
                else if (world != null) world.Drop();
            }
            finally
            {
                await Transitions.Reveal(explore != null ? Dungeon.Arrive : null);
            }
            try
            {
                if (explore != null) await explore;
            }
            finally
            {
                if (run == story) InDungeon = false;
            }
            if (run != story) return;
            await Transitions.Play(Wipe.Surface, () =>
            {
                Dungeon.Leave();
                SetScreen(Story);
                UpdateMusic();
            });
        }

        // ------------------------------------------------------------------ saves

        public bool SaveTo(int slot)
        {
            if (!CanSave)
            {
                Notice(InBattle ? "战斗中无法存档" : InDungeon ? "潜空间里无法存档" : "现在无法存档");
                return false;
            }
            var data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(story.Checkpoint));
            data.playSeconds = PlaySeconds;
            try
            {
                SaveSystem.Write(slot, data);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Notice("存档失败：无法写入文件");
                return false;
            }
            Records.Save();
            return true;
        }

        public void QuickSave()
        {
            if (SaveTo(SaveSystem.QuickSlot)) Notice("快速存档完成（从本节开头继续）");
        }

        public void QuickLoad()
        {
            var info = SaveSystem.Read(SaveSystem.QuickSlot);
            if (info.Data == null)
            {
                Notice(info.Error ?? "还没有快速存档");
                return;
            }
            LoadGame(info.Data);
            Notice("已读取快速存档");
        }

        // The Yarn variable the phone profile shows as "how she calls you".
        public string StoryString(string variable) => story?.GetString(variable) ?? "";
        public bool StoryBool(string variable) => story?.GetBool(variable) ?? false;
    }
}
