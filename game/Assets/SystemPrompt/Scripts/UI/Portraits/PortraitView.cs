using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // A character portrait as the UI uses it: an Image that fills its box, shows one of the character's
    // faces, takes a tint (speaker emphasis, hover dimming) and moves its mouth while the character talks.
    // Portraits.Create picks the implementation per character.
    public abstract class PortraitView
    {
        public readonly CharacterDef Def;
        public readonly string Outfit;
        public readonly Image Element;
        public string Face { get; private set; }

        protected PortraitView(CharacterDef def, string outfit)
        {
            Def = def;
            Outfit = outfit;
            Element = Ui.Picture(null, ScaleMode.ScaleToFit);
            Ui.Fill(Element);
        }

        public virtual Color Tint
        {
            set => Element.tintColor = value;
        }

        // Breathes and sways by itself (a Live2D model, a rigged PNG), so callers add no idle motion of their own.
        public virtual bool HasIdleMotion => false;

        public void SetFace(string face)
        {
            if (Array.IndexOf(Def.Faces, face) < 0)
            {
                Debug.LogWarning($"{Def.Id} 没有表情 {face}，用 {Def.DefaultFace} 代替");
                face = Def.DefaultFace;
            }
            if (face == Face) return;
            Face = face;
            ShowFace(face);
        }

        protected abstract void ShowFace(string face);

        // Moves the mouth for this many seconds from now; 0 closes it.
        public virtual void Talk(float seconds) { }

        // Turns toward a point on the panel (the pointer).
        public virtual void LookAt(Vector2 panelPosition) { }

        // The player poked her: a start that shakes the hair.
        public virtual void Poke() { }

        // Frees what the portrait holds outside the UI tree. Call once it is off screen for good.
        public virtual void Release() { }
    }

    // One picture per face: Art/Portraits/<prefix>_<face>. An outfit (DeepSeek's battle form) has a single
    // picture that stands in for every face.
    public sealed class PngPortrait : PortraitView
    {
        public PngPortrait(CharacterDef def, string outfit) : base(def, outfit) { }

        protected override void ShowFace(string face) => Element.image = Ui.Tex(Def.PortraitKey(Outfit ?? face));
    }

    public static class Portraits
    {
        // Live2D when the SDK is in the project and Resources holds a model for this character and outfit
        // (see Live2DPortrait); the PNG portraits otherwise.
        public static PortraitView Create(CharacterDef def, string face, string outfit = null)
        {
            PortraitView view = null;
#if SP_LIVE2D
            view = Live2DPortrait.TryCreate(def, outfit);
#endif
            view ??= new PngPortrait(def, outfit);
            view.SetFace(face ?? def.DefaultFace);
            return view;
        }

        // For a portrait shown on its own to be looked at and poked (title, phone profile): the Live2D model
        // when there is one, else the PNG moved by the character's rig, else the still PNG.
        public static PortraitView CreateAnimated(CharacterDef def, string face)
        {
            PortraitView view = null;
#if SP_LIVE2D
            view = Live2DPortrait.TryCreate(def, null);
#endif
            if (view == null && def.Rig != null) view = new RiggedPortrait(def);
            view ??= new PngPortrait(def, null);
            view.SetFace(face ?? def.DefaultFace);
            return view;
        }
    }
}
