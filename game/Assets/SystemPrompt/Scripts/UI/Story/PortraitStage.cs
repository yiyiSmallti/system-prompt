using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // Dialogue portraits. The Yarn script has no show/hide commands: a character steps in the first
    // time they speak in a scene, and a scene change clears the stage. Faces come from #face: tags.
    public sealed class PortraitStage
    {
        const float Height = 1150f;
        const float Top = 40f;
        static readonly Color SpeakerTint = Color.white;
        static readonly Color ListenerTint = new Color(0.58f, 0.6f, 0.68f, 1f);
        static readonly Color IdleTint = new Color(0.86f, 0.86f, 0.9f, 1f);

        sealed class Actor
        {
            public string Id;
            public VisualElement Root;
            public PortraitView View;
            public float X;
            public float Bob;
        }

        readonly GameRoot game;
        readonly VisualElement layer;
        readonly List<Actor> actors = new List<Actor>();

        public PortraitStage(GameRoot game, VisualElement parent)
        {
            this.game = game;
            layer = Ui.El("sp-stage", parent);
            Ui.Fill(layer);
            layer.pickingMode = PickingMode.Ignore;
            layer.schedule.Execute(Breathe).Every(33);
        }

        public IEnumerable<string> OnStage
        {
            get { foreach (var a in actors) yield return a.Id; }
        }

        public void Clear()
        {
            foreach (var a in actors) Leave(a, 0.2f).Forget();
            actors.Clear();
        }

        // Called for every line: brings the speaker in if needed, applies the face and the emphasis.
        public void OnLine(string id, string face, bool isSpeech)
        {
            var def = GameData.Character(id);
            if (def == null || !def.HasPortrait)
            {
                SetSpeaker(null, isSpeech);
                return;
            }
            var actor = actors.Find(a => a.Id == id);
            if (actor == null) actor = Enter(def, face);
            else if (face != null) actor.View.SetFace(face);
            SetSpeaker(id, isSpeech);
            Hop(actor);
        }

        // The shadow merging into DeepSeek (<<effort_max>>) leaves the stage without a scene change.
        public void Remove(string id)
        {
            var actor = actors.Find(a => a.Id == id);
            if (actor == null) return;
            actors.Remove(actor);
            Leave(actor, 0.3f).Forget();
        }

        // Horizontal centre of an actor's portrait in frame pixels, or null when they are not on stage.
        public float? CenterOf(string id)
        {
            var actor = actors.Find(a => a.Id == id);
            return actor == null ? (float?)null : actor.X + Height * 1024f / 1536f / 2f;
        }

        public void SetFace(string id, string face) => actors.Find(a => a.Id == id)?.View.SetFace(face);

        // Mouth movement for the line just started: the speaker talks, everyone else stops.
        public void Talk(string id, float seconds)
        {
            foreach (var a in actors) a.View.Talk(a.Id == id ? seconds : 0f);
        }

        // Re-dress actors whose outfit changed, e.g. after an awakening puts DeepSeek in her battle form.
        public void Refresh()
        {
            foreach (var a in actors)
            {
                var outfit = OutfitFor(a.Id);
                if (outfit == a.View.Outfit) continue;
                var old = a.View;
                a.View = Portraits.Create(old.Def, old.Face, outfit);
                a.View.Element.AddToClassList("sp-portrait__image");
                a.View.Element.tintColor = old.Element.tintColor;
                old.Element.parent.Insert(old.Element.parent.IndexOf(old.Element), a.View.Element);
                old.Element.RemoveFromHierarchy();
                old.Release();
            }
        }

        public void SetSpeaker(string id, bool isSpeech)
        {
            foreach (var a in actors)
            {
                a.View.Tint = id == null ? (isSpeech ? ListenerTint : IdleTint) : a.Id == id ? SpeakerTint : ListenerTint;
                if (a.Id == id) a.Root.BringToFront();
            }
        }

        Actor Enter(CharacterDef def, string face)
        {
            var root = Ui.El("sp-portrait", layer);
            root.style.position = Position.Absolute;
            root.style.top = Top;
            root.style.height = Height;
            root.style.width = Height * 1024f / 1536f;
            var view = Portraits.Create(def, face, OutfitFor(def.Id));
            view.Element.AddToClassList("sp-portrait__image");
            root.Add(view.Element);
            var actor = new Actor { Id = def.Id, Root = root, View = view };
            actors.Add(actor);
            Arrange(actor);
            return actor;
        }

        static async Awaitable Leave(Actor actor, float seconds)
        {
            await Ui.FadeOutAndRemove(actor.Root, seconds);
            actor.View.Release();
        }

        // DeepSeek wears her battle form inside the latent space once she has awakened.
        string OutfitFor(string id) =>
            id == "DeepSeek" && game.State.FormOf("DeepSeek") != null && IsLatentScene() ? "battle" : null;

        bool IsLatentScene() => GameData.Scenes.TryGetValue(game.State.scene ?? "", out var s) && s.Latent;

        // 1 actor: center; 2: thirds; 3: quarters. Newcomers slide in from the nearer edge.
        void Arrange(Actor newcomer)
        {
            var n = actors.Count;
            var width = Height * 1024f / 1536f;
            for (var i = 0; i < n; i++)
            {
                var a = actors[i];
                var center = n == 1 ? 0.5f : n == 2 ? (i == 0 ? 0.3f : 0.7f) : 0.2f + 0.3f * i;
                var x = 1920f * center - width / 2f;
                var from = a == newcomer ? x + (center < 0.5f ? -160f : 160f) : a.X;
                a.X = x;
                var root = a.Root;
                var startOpacity = a == newcomer ? 0f : 1f;
                Anim.Tween(0.28f, t =>
                {
                    root.style.left = Mathf.Lerp(from, x, t);
                    root.style.opacity = Mathf.Lerp(startOpacity, 1f, t);
                }).Forget();
            }
        }

        void Hop(Actor actor)
        {
            actor.Bob = 1f;
        }

        // Subtle idle breathing plus a small hop when an actor starts a line.
        void Breathe()
        {
            var t = Time.unscaledTime;
            foreach (var a in actors)
            {
                a.Bob = Mathf.Max(0f, a.Bob - 0.12f);
                var hop = Mathf.Sin(a.Bob * Mathf.PI) * -14f;
                var breath = 1f + Mathf.Sin(t * 1.6f + a.X * 0.01f) * 0.004f;
                a.Root.style.translate = new Translate(0, hop);
                a.Root.style.scale = new Scale(new Vector2(1f, breath));
            }
        }
    }
}
