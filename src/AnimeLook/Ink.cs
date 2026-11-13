using System;
using System.IO;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeLook
{
    /// <summary>
    /// Ink lines and flat paint (tools/shaderpack/anime.glsl says how each is made): one pass over the flight camera's picture
    /// of the solid things, straight after they are drawn and before the sky's haze, the weather, smoke, flames and glass are
    /// laid on, so that those go over the lines as they go over the things.
    /// </summary>
    public sealed class Ink : MonoBehaviour
    {
        internal static string Said = "";
        /// <summary>(Development build: 1 the lines alone, 2 the depth picture's bend, 3 what counts as solid.)</summary>
        internal static int Show;
        /// <summary>(Development build: 0 the building's depth picture not asked for, the lines on the craft alone as before; 2 asked for and copied, not handed over.)</summary>
        internal static int TestBehind = 1;
        /// <summary>(Development build, for tuning without a restart: how far the steps of brightness lean to the brighter tone; from what part of
        /// their reach the lines begin to fade; how many of the depth picture's steps a bend must be to be a line.)</summary>
        internal static float Lean = 0.25f, FadeFrom = 0.25f, Grain = 24f;

        const CameraEvent At = CameraEvent.AfterForwardOpaque;

        static Shader shader;
        static Mesh sheet;
        static bool tried;
        static string why = "";
        static readonly int idScene = Shader.PropertyToID("_AnimeScene"), idInk = Shader.PropertyToID("_AnimeInk"), idPaint = Shader.PropertyToID("_AnimePaint"), idHow = Shader.PropertyToID("_AnimeHow"), idMore = Shader.PropertyToID("_AnimeMore");
        static readonly int idBehind = Shader.PropertyToID("_AnimeBehind"), idDepth = Shader.PropertyToID("_CameraDepthTexture");

        Material material;
        CommandBuffer pass;
        Camera on;
        bool askedDepth;
        // (in the buildings where craft are made: the camera that draws the building, before the one that draws the craft, and
        // its depth picture copied for the pass: see Behind)
        Camera scenery;
        bool sceneryAskedDepth;
        CommandBuffer copy;
        RenderTexture behind;
        float sceneryLooked = -10f;
        // (the crew's portraits: the same pass on each of their cameras, and whether the depth picture was asked of it here)
        readonly System.Collections.Generic.Dictionary<Camera, bool> portraits = new System.Collections.Generic.Dictionary<Camera, bool>();
        readonly System.Collections.Generic.List<Camera> gone = new System.Collections.Generic.List<Camera>();

        void Awake() { Camera.onPreCull += Before; }

        void OnDestroy()
        {
            Camera.onPreCull -= Before;
            Off();
            if (copy != null) { copy.Release(); copy = null; }
            if (pass != null) { pass.Release(); pass = null; }
            if (material != null) Destroy(material);
            Said = "";
        }

        static void Load()
        {
            tried = true;
            bool gl = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore;
            string path = KSPUtil.ApplicationRootPath + "GameData/AnimeLook/PluginData/" + (gl ? "anime.bundle" : "anime-windows.bundle");
            try
            {
                if (!File.Exists(path)) why = gl ? "this version has no ink and paint for OpenGL (the squash works there)" : "its shader's file is not there (" + Path.GetFileName(path) + ")";
                else
                {
                    AssetBundle b = AssetBundle.LoadFromFile(path);
                    if (b == null) why = "its shader's file could not be read";
                    else
                    {
                        foreach (Shader s in b.LoadAllAssets<Shader>()) if (s != null && s.name == "AnimeLook/Ink" && s.isSupported) shader = s;
                        b.Unload(false);
                        if (shader == null) why = "its shader does not run on this computer";
                    }
                }
            }
            catch (Exception e) { shader = null; why = e.Message; }
            sheet = new Mesh { name = "Anime Look: sheet" };
            sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
            sheet.triangles = new[] { 0, 1, 2 };
            sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            Kit.Log("Anime Look", "ink and paint: " + (shader != null ? "shader loaded" : "not drawn: " + why));
        }

        void Off()
        {
            if (on != null && pass != null) on.RemoveCommandBuffer(At, pass);
            if (on != null && askedDepth) on.depthTextureMode &= ~DepthTextureMode.Depth;
            on = null; askedDepth = false;
            OffBehind();
            OffPortraits(false);
        }

        void OffBehind()
        {
            if (scenery != null)
            {
                if (copy != null) scenery.RemoveCommandBuffer(At, copy);
                if (sceneryAskedDepth) scenery.depthTextureMode &= ~DepthTextureMode.Depth;
            }
            scenery = null; sceneryAskedDepth = false;
            if (behind != null) { behind.Release(); Destroy(behind); behind = null; }
        }

        /// <summary>
        /// In the buildings where craft are made the building is drawn by one camera (sceneryCam) and the craft by a second over
        /// it, whose depth picture has the craft in it and nothing of the building: but for an unseen floor that is there to
        /// catch the craft's shadow. The lines and the paint were then the craft's alone, with a line along that floor's far
        /// edge, straight across the hangar's doorway. So the first camera's depth picture is copied as it finishes the solid
        /// things and handed to the pass, which takes the nearer of the two. Only while the two cameras stand in one place
        /// and see alike, which they do as the game has them. Whether it is so.
        /// </summary>
        bool Behind(Camera cam)
        {
            if (Time.unscaledTime - sceneryLooked > 1f || (object)scenery != null && scenery == null)
            {
                sceneryLooked = Time.unscaledTime;
                Camera found = Names.CameraNamed("sceneryCam");
                if (found == cam || found != null && found.depth >= cam.depth) found = null;
                if (found != scenery) { OffBehind(); scenery = found; }
            }
            if (scenery == null) return false;
            bool alike = scenery.isActiveAndEnabled && scenery.targetTexture == cam.targetTexture
                         && (scenery.transform.position - cam.transform.position).sqrMagnitude < 1e-4f && Quaternion.Angle(scenery.transform.rotation, cam.transform.rotation) < 0.01f
                         && Mathf.Abs(scenery.fieldOfView - cam.fieldOfView) < 0.001f && Mathf.Abs(scenery.nearClipPlane - cam.nearClipPlane) < 1e-4f && Mathf.Abs(scenery.farClipPlane - cam.farClipPlane) < 0.01f
                         && scenery.pixelWidth == cam.pixelWidth && scenery.pixelHeight == cam.pixelHeight;
            if (!alike) { if (behind != null) OffBehind(); sceneryLooked = Time.unscaledTime; return false; }
            if ((scenery.depthTextureMode & DepthTextureMode.Depth) == 0) { scenery.depthTextureMode |= DepthTextureMode.Depth; sceneryAskedDepth = true; }
            if (behind == null || behind.width != cam.pixelWidth || behind.height != cam.pixelHeight)
            {
                if (behind != null) { if (copy != null) scenery.RemoveCommandBuffer(At, copy); behind.Release(); Destroy(behind); }
                behind = new RenderTexture(cam.pixelWidth, cam.pixelHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { name = "Anime Look: the building's depth", filterMode = FilterMode.Point };
                behind.Create();
                if (copy == null) copy = new CommandBuffer { name = "Anime Look: the building's depth" };
                copy.Clear();
                copy.Blit(BuiltinRenderTextureType.Depth, behind);
                copy.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                scenery.AddCommandBuffer(At, copy);
            }
            Shader.SetGlobalTexture(idBehind, behind);
            return true;
        }

        /// <summary>Whether a camera is one that draws over the flight's picture with a depth picture of its own, and gets the pass
        /// for what it draws: one of the crew's portraits, or the cockpit's camera while the view is from inside (it draws the
        /// cockpit over the world outside, which the flight's camera has drawn, inked and painted already).</summary>
        static bool Over(Camera cam)
        {
            if (Squash.IsPortrait(cam)) return true;
            if (!Names.Is(cam, "InternalCamera")) return false;
            CameraManager manager = CameraManager.Instance;
            return manager != null && (manager.currentCameraMode == CameraManager.CameraMode.IVA || manager.currentCameraMode == CameraManager.CameraMode.Internal);
        }

        /// <summary>The pass taken off the portraits' cameras: all of them, or those that are portraits no more.</summary>
        void OffPortraits(bool onlyGone)
        {
            gone.Clear();
            foreach (System.Collections.Generic.KeyValuePair<Camera, bool> p in portraits)
            {
                if (onlyGone && p.Key != null && Over(p.Key)) continue;
                if (p.Key != null)
                {
                    if (pass != null) p.Key.RemoveCommandBuffer(At, pass);
                    if (p.Value) p.Key.depthTextureMode &= ~DepthTextureMode.Depth;
                }
                gone.Add(p.Key);
            }
            foreach (Camera c in gone) portraits.Remove(c);
            gone.Clear();
        }

        /// <summary>The camera that draws what is near: in flight (on Direct3D the one that draws everything), or in the buildings
        /// where craft are made.</summary>
        static bool Ours(Camera cam)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                EditorLogic editor = EditorLogic.fetch;
                return editor != null && editor.editorCamera == cam;
            }
            // (at the space centre the near camera has the flight's name, and there is no flight to ask)
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER) return Names.Is(cam, "Camera 00");
            FlightCamera fc = FlightCamera.fetch;
            return fc != null && fc.cameras != null && fc.cameras.Length > 0 && fc.cameras[0] == cam;
        }

        void Before(Camera cam)
        {
            bool portrait = HighLogic.LoadedSceneIsFlight && Over(cam);
            if (!portrait && !Ours(cam)) return;
            try
            {
                bool ink = Anime.InkOn != null && Anime.InkOn && Anime.Of(Anime.InkStrength, 0.6f) > 0.001f;
                bool paint = Anime.PaintOn != null && Anime.PaintOn;
                float colour = Anime.Of(Anime.Colour, 1.15f);
                bool want = Anime.On != null && Anime.On && !MapView.MapIsEnabled && (ink || paint || Show > 0);
                if (!want) { if (on != null || portraits.Count > 0) Off(); Said = ""; return; }
                if (!tried) Load();
                if (shader == null) { Said = "ink and paint not drawn: " + why; return; }
                if (material == null) material = new Material(shader) { name = "Anime Look: ink and paint" };
                if (pass == null)
                {
                    pass = new CommandBuffer { name = "Anime Look: ink and paint" };
                    pass.GetTemporaryRT(idScene, -1, -1, 0, FilterMode.Point, RenderTextureFormat.DefaultHDR);
                    pass.Blit(BuiltinRenderTextureType.CameraTarget, idScene);
                    pass.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                    pass.DrawMesh(sheet, Matrix4x4.identity, material, 0, 0);
                    pass.ReleaseTemporaryRT(idScene);
                }
                if (portrait)
                {
                    if (!portraits.ContainsKey(cam)) { cam.AddCommandBuffer(At, pass); portraits[cam] = false; }
                    if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; portraits[cam] = true; }
                }
                else
                {
                    if (on != cam) { Off(); cam.AddCommandBuffer(At, pass); on = cam; }
                    if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0) { cam.depthTextureMode |= DepthTextureMode.Depth; askedDepth = true; }
                    if (portraits.Count > 0) OffPortraits(true);
                }
                // (a line as wide on a large picture as on a small one, to the eye)
                float width = Anime.Of(Anime.InkWidth, 1.2f) * Mathf.Max(cam.pixelHeight, 360) / 1080f;
                material.SetVector(idInk, new Vector4(width, ink || Show > 0 ? Anime.Of(Anime.InkStrength, 0.6f) : 0f, Anime.Of(Anime.Fold, 0.002f), Anime.Of(Anime.InkReach, 300f)));
                // (so many steps to four stops, which is about what a lit thing and its shade lie apart by)
                float step = paint ? 4f / Mathf.Clamp(Anime.Of(Anime.Bands, 6f), 2f, 12f) : 0f;
                material.SetVector(idPaint, new Vector4(step, Anime.Of(Anime.PaintStrength, 0.7f), colour, Show));
                // (the paint's patches reach as far on a large picture as on a small one, to the eye; never under two pixels)
                float patch = paint ? Mathf.Max(2f, Anime.Of(Anime.Patch, 4f) * Mathf.Max(cam.pixelHeight, 360) / 1080f) : 0f;
                bool both = !portrait && HighLogic.LoadedSceneIsEditor && TestBehind != 0 && Behind(cam);
                if (TestBehind == 0 && scenery != null) OffBehind();
                material.SetVector(idHow, new Vector4(SystemInfo.usesReversedZBuffer ? 1f : 0f, Grain, patch, both ? 1f : 0f));
                material.SetVector(idMore, new Vector4(paint ? Anime.Of(Anime.Tones, 0.3f) : 0f, Lean, FadeFrom, 0f));
                if (!portrait) Said = (ink ? "ink lines " + width.ToString("0.0") + " px" : "no ink") + (paint ? ", paint in steps of " + step.ToString("0.00") + " stops" : ", no paint")
                                     + (HighLogic.LoadedSceneIsEditor ? (both ? ", on the building and the craft" : ", on the craft alone (the building's camera is not to be had)") : "");
            }
            catch (Exception e) { Kit.Log("Anime Look", "ink and paint: " + e); }
        }
    }
}
