using UnityEngine;

namespace SystemPrompt.Dungeon
{
    // The white door at the deep exit turned into a mirror (story: Prologue_Deep_Exit, <<mirror on|off>>).
    // A second camera stands where the area's camera would be reflected through the glass and draws the
    // sea from there, clipped at the glass; the glass shows that picture (Resources/Shaders/Mirror.shader).
    // While the double is up, the reflection is not the player's avatar but the same player in the
    // latent-space battle coat (5-battle) they have not awakened into yet: the avatar is on a layer the
    // reflection camera skips, the double on one only it draws.
    public sealed class SeaMirror : MonoBehaviour
    {
        const int DoubleLayer = 28;  // drawn only in the mirror
        const int AvatarLayer = 29;  // drawn everywhere but the mirror
        const float RevealSeconds = 0.8f, ScanSeconds = 0.5f;

        Camera view;           // the area's camera
        Camera reflection;
        RenderTexture picture;
        Material glass;
        Transform avatar;
        SeaFigure twin;
        Vector3 point, normal; // the glass (the middle of its foot), and the side it faces
        float height;
        float show, scan = 2f, scanPeak; // the ripple's way out (0-1); the band's height on the glass (0-1) and brightness

        // The glass is width × height, standing on the ground at `centre` and facing `facing`.
        public static SeaMirror Create(Transform parent, Camera view, Vector3 centre, Vector3 facing, float width, float height, SeaAvatar avatar, string gender)
        {
            var go = new GameObject("Mirror");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(-facing));
            var mirror = go.AddComponent<SeaMirror>();
            mirror.Build(view, centre, facing, width, height, avatar, gender);
            return mirror;
        }

        void Build(Camera view, Vector3 centre, Vector3 facing, float width, float height, SeaAvatar avatarOf, string gender)
        {
            this.view = view;
            point = centre;
            normal = facing.normalized;
            this.height = height;
            avatar = avatarOf.Root;

            // As sharp as the picture round it, antialiased as it is: the double in it stands beside the player.
            picture = new RenderTexture(Mathf.Max(256, Screen.width), Mathf.Max(144, Screen.height), 24)
            {
                name = "Mirror",
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
            };
            var camGo = new GameObject("Mirror Camera");
            camGo.transform.SetParent(transform, false);
            reflection = camGo.AddComponent<Camera>();
            reflection.CopyFrom(view);
            reflection.targetTexture = picture;
            reflection.depth = view.depth - 1f;
            reflection.cullingMask = ~(1 << AvatarLayer);
            view.cullingMask &= ~(1 << DoubleLayer);

            glass = SeaArt.Plain("Mirror");
            glass.mainTexture = picture;
            glass.SetFloat("_Aspect", width / height);
            Parts.Shape("Glass", transform, SeaArt.Quad(width, height), glass, new Vector3(0f, height / 2f, 0f));

            twin = SeaFigure.Player(gender, true, transform);
            SetLayer(twin.Root, DoubleLayer);
            twin.Root.gameObject.SetActive(false);
            Apply();
        }

        void OnDestroy()
        {
            twin?.Dispose();
            if (picture != null) Destroy(picture); // releases it too; Release alone leaves the texture object
            if (view != null) view.cullingMask |= 1 << DoubleLayer;
            if (avatar != null) SetLayer(avatar, 0);
        }

        // The reflection spreads over the door from its middle as a ripple, the double in it, a bright ring at its
        // front (half as bright with 减弱闪光) instead of a flash over the whole glass, which is most of the picture.
        public async Awaitable Open()
        {
            SetLayer(avatar, AvatarLayer);
            twin.Root.gameObject.SetActive(true);
            glass.SetFloat("_Edge", Calm ? 0.5f : 1f);
            await Anim.Tween(RevealSeconds, t =>
            {
                show = t;
                Apply();
            }, Ease.Linear);
        }

        // A band of light runs down the glass, and below it the reflection is only the player again (the story's
        // 一闪 when they look back). There is one picture, so the double goes as the band crosses the middle of the
        // reflection, under it. Well short of white, half as bright with 减弱闪光.
        public async Awaitable Settle()
        {
            var peak = Calm ? 0.35f : 0.7f;
            var swap = Across(avatar.position + Vector3.up);
            await Anim.Tween(ScanSeconds, t =>
            {
                scan = Mathf.Lerp(1.05f, -0.05f, t);
                scanPeak = peak;
                if (scan <= swap && twin.Root.gameObject.activeSelf) Unswap();
                Apply();
            }, Ease.Linear);
            if (twin.Root.gameObject.activeSelf) Unswap();
            scanPeak = 0f;
            Apply();
        }

        void Unswap()
        {
            SetLayer(avatar, 0);
            twin.Root.gameObject.SetActive(false);
        }

        // How high up the glass (0-1) the area's camera sees the reflection of `p`: where its line of sight crosses it.
        float Across(Vector3 p)
        {
            var eye = view.transform.position;
            var to = Reflect(p) - eye;
            var into = Vector3.Dot(to, normal);
            if (Mathf.Abs(into) < 1e-4f) return 0.5f;
            return (eye.y + to.y * Vector3.Dot(point - eye, normal) / into - point.y) / height;
        }

        static bool Calm => GameRoot.Instance != null && GameRoot.Instance.Settings.reduceFlash;

        void LateUpdate()
        {
            if (view == null) return;
            twin.Root.SetPositionAndRotation(avatar.position, avatar.rotation);
            twin.Animate(Time.unscaledDeltaTime, 0f);

            // The area's camera reflected through the glass, looking back through it at the sea in front.
            var eye = view.transform;
            reflection.transform.SetPositionAndRotation(Reflect(eye.position), Quaternion.LookRotation(ReflectDirection(eye.forward), ReflectDirection(eye.up)));
            reflection.fieldOfView = view.fieldOfView;
            reflection.aspect = (float)picture.width / picture.height;
            reflection.ResetProjectionMatrix();
            // Near plane on the glass, so nothing behind the door (the door itself) gets in the way.
            var toCamera = reflection.worldToCameraMatrix;
            var p = toCamera.MultiplyPoint(point + normal * 0.02f);
            var n = toCamera.MultiplyVector(normal).normalized;
            reflection.projectionMatrix = reflection.CalculateObliqueMatrix(new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n)));
            glass.SetMatrix("_MirrorVP", reflection.projectionMatrix * reflection.worldToCameraMatrix);
        }

        void Apply()
        {
            glass.SetFloat("_Show", show);
            glass.SetFloat("_Scan", scan);
            glass.SetFloat("_ScanPeak", scanPeak);
        }

        Vector3 Reflect(Vector3 p) => p - 2f * Vector3.Dot(p - point, normal) * normal;

        Vector3 ReflectDirection(Vector3 d) => d - 2f * Vector3.Dot(d, normal) * normal;

        static void SetLayer(Transform root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
