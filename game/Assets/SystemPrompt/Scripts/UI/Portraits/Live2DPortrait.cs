#if SP_LIVE2D
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.MouthMovement;
using UnityEngine;
using UnityEngine.UIElements;

namespace SystemPrompt.UI
{
    // A Live2D model as a portrait. A camera of its own renders the model into a texture that the portrait
    // Image shows, so the UI treats it like a picture: layout, tint and screen effects work as for the PNGs.
    //
    // The model is exported from Cubism Editor into Resources/Live2D/<Id>/<Id>.model3.json (an outfit goes to
    // <Id>_<outfit>/<Id>_<outfit>.model3.json); the Cubism importer turns it into the prefab loaded here.
    // Faces map to expressions by file name (smile → smile.exp3.json); a face without one shows the model's
    // default look. Eye blinks come from the model's EyeBlink group, the mouth from its LipSync group
    // (ParamMouthOpenY if it has none); breathing and a slow sway are added on top.
    public sealed class Live2DPortrait : PortraitView
    {
        const int Layer = 31; // drawn only by portrait cameras
        const int TextureWidth = 1024, TextureHeight = 1536; // the PNG portraits' size, so layouts match
        static int slots;

        readonly GameObject rig;
        readonly RenderTexture texture;
        readonly Live2DPortraitDriver driver;
        readonly CubismExpressionController expressions;

        public static Live2DPortrait TryCreate(CharacterDef def, string outfit)
        {
            var name = outfit == null ? def.Id : $"{def.Id}_{outfit}";
            var prefab = Resources.Load<GameObject>($"Live2D/{name}/{name}");
            return prefab != null ? new Live2DPortrait(def, outfit, prefab) : null;
        }

        Live2DPortrait(CharacterDef def, string outfit, GameObject prefab) : base(def, outfit)
        {
            rig = new GameObject($"Live2D Portrait {def.Id}");
            Object.DontDestroyOnLoad(rig);
            // Far from everything and from each other, so each camera sees only its own model.
            rig.transform.position = new Vector3(1000f + 100f * slots++, -1000f, 0f);

            var model = Object.Instantiate(prefab, rig.transform).GetComponent<CubismModel>();
            model.transform.localPosition = Vector3.zero;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            if (model.GetComponent<CubismEyeBlinkController>() != null && model.GetComponent<CubismAutoEyeBlinkInput>() == null)
                model.gameObject.AddComponent<CubismAutoEyeBlinkInput>();
            expressions = model.GetComponent<CubismExpressionController>();
            driver = model.gameObject.AddComponent<Live2DPortraitDriver>();
            driver.Init(model);

            texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32) { name = $"Live2D {def.Id}" };
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(rig.transform, false);
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << Layer;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = texture;
            camera.gameObject.AddComponent<Live2DPortraitUnpremultiply>();
            Element.image = texture;

            rig.AddComponent<Live2DPortraitRig>().Init(Element, model, camera);
        }

        protected override void ShowFace(string face)
        {
            if (expressions == null || expressions.ExpressionsList == null) return;
            var list = expressions.ExpressionsList.CubismExpressionObjects;
            var index = -1;
            for (var i = 0; i < list.Length; i++)
                if (list[i] != null && list[i].name == face + ".exp3") index = i;
            expressions.CurrentExpressionIndex = index;
        }

        public override bool HasIdleMotion => true;

        public override void Talk(float seconds) => driver.TalkUntil = Time.unscaledTime + seconds;

        public override void Release()
        {
            Object.Destroy(rig);
            texture.Release();
            Object.Destroy(texture);
        }
    }

    // Frames the model once its meshes exist, and stops the camera and the model while the portrait is off
    // screen (a hidden title screen, a closed profile).
    sealed class Live2DPortraitRig : MonoBehaviour
    {
        VisualElement element;
        CubismModel model;
        Camera view;
        bool framed;

        public void Init(VisualElement element, CubismModel model, Camera view)
        {
            this.element = element;
            this.model = model;
            this.view = view;
        }

        void LateUpdate()
        {
            var shown = Shown();
            if (view.enabled != shown)
            {
                view.enabled = shown;
                model.gameObject.SetActive(shown);
            }
            if (shown && !framed) framed = Frame();
        }

        bool Shown()
        {
            if (element.panel == null) return false;
            for (var e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        // Fits the model's canvas into the texture, so the figure sits in it as the artist framed the canvas
        // in Cubism Editor. Model space is in units of PixelsPerUnit around the canvas origin, y up.
        bool Frame()
        {
            var canvas = model.CanvasInformation;
            if (canvas == null || canvas.PixelsPerUnit <= 0f) return false;
            var ppu = canvas.PixelsPerUnit;
            var center = new Vector3((canvas.CanvasWidth / 2f - canvas.CanvasOriginX) / ppu, (canvas.CanvasOriginY - canvas.CanvasHeight / 2f) / ppu, 0f);
            var aspect = (float)view.targetTexture.width / view.targetTexture.height;
            view.orthographicSize = Mathf.Max(canvas.CanvasHeight / 2f, canvas.CanvasWidth / 2f / aspect) / ppu * model.transform.lossyScale.y;
            center = model.transform.TransformPoint(center);
            view.transform.position = new Vector3(center.x, center.y, model.transform.position.z - 10f);
            return true;
        }
    }

    // Cubism draws with premultiplied alpha but UI Toolkit images blend straight alpha, which would darken the
    // model's soft outline a second time. This converts the camera's output on its way into the texture.
    sealed class Live2DPortraitUnpremultiply : MonoBehaviour
    {
        static Material material;

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
                material = new Material(Resources.Load<Shader>("Live2D/Unpremultiply")) { hideFlags = HideFlags.HideAndDontSave };
            Graphics.Blit(source, destination, material);
        }
    }

    // Runs inside the model's Cubism update, after expressions and before physics, so hair follows the sway.
    sealed class Live2DPortraitDriver : MonoBehaviour, ICubismUpdatable
    {
        public float TalkUntil;

        CubismParameter[] mouth;
        CubismParameter breath, angleX, angleY, angleZ, bodyX;
        float mouthValue;
        float seed;

        public int ExecutionOrder => CubismUpdateExecutionOrder.CubismLookController - 50;
        public bool NeedsUpdateOnEditing => false;
        public bool HasUpdateController { get; set; }

        // Called right after AddComponent: the update controller may run OnLateUpdate before Start would have.
        public void Init(CubismModel model)
        {
            var parameters = model.Parameters;
            mouth = System.Array.FindAll(parameters, p => p.GetComponent<CubismMouthParameter>() != null);
            if (mouth.Length == 0 && parameters.FindById("ParamMouthOpenY") is CubismParameter open) mouth = new[] { open };
            breath = parameters.FindById("ParamBreath");
            angleX = parameters.FindById("ParamAngleX");
            angleY = parameters.FindById("ParamAngleY");
            angleZ = parameters.FindById("ParamAngleZ");
            bodyX = parameters.FindById("ParamBodyAngleX");
            seed = Random.value * 100f;
            HasUpdateController = GetComponent<CubismUpdateController>() != null;
        }

        void LateUpdate()
        {
            if (!HasUpdateController) OnLateUpdate();
        }

        public void OnLateUpdate()
        {
            var now = Time.unscaledTime;
            var t = now + seed;
            // Lip flap with an uneven rhythm while talking; the mouth eases shut afterwards.
            var target = now < TalkUntil ? Mathf.Abs(Mathf.Sin(t * 12f)) * Mathf.Lerp(0.5f, 1f, Mathf.PerlinNoise(t * 4f, seed)) : 0f;
            mouthValue = Mathf.MoveTowards(mouthValue, target, Time.unscaledDeltaTime * 10f);
            foreach (var p in mouth) p.BlendToValue(CubismParameterBlendMode.Override, mouthValue);

            breath?.BlendToValue(CubismParameterBlendMode.Override, 0.5f + 0.5f * Mathf.Sin(t * 1.5f));
            angleX?.BlendToValue(CubismParameterBlendMode.Additive, Mathf.Sin(t * 0.5f) * 5f);
            angleY?.BlendToValue(CubismParameterBlendMode.Additive, Mathf.Sin(t * 0.37f) * 3f);
            angleZ?.BlendToValue(CubismParameterBlendMode.Additive, Mathf.Sin(t * 0.43f) * 2.5f);
            bodyX?.BlendToValue(CubismParameterBlendMode.Additive, Mathf.Sin(t * 0.5f + 0.6f) * 3f);
        }
    }
}
#endif
