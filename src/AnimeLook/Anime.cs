using System;
using Keystone;
using UnityEngine;

#if !DEV
[assembly: KSPAssembly("AnimeLook", 0, 1)]
[assembly: KSPAssemblyDependency("Keystone", 0, 11)]
#endif

namespace AnimeLook
{
    /// <summary>
    /// The look of drawn animation made with 3D models, by the trick its makers use: the model is not the shape it seems.
    /// In Blender a lattice that turns to face the camera flattens a character's head (or all of it) along the line from the
    /// camera, to a fraction of its depth. From where the camera stands nothing looks flat. What goes is the perspective
    /// inside the model: the near side no longer looms and the far side no longer shrinks, so it reads as a flat drawing that
    /// turns as the camera goes round it, in a scene that keeps its depth.
    ///
    /// Here the same is done to every craft and every kerbal, every frame (Squash.cs), each along the line from the camera to
    /// its own middle. Ink lines and flat paint are laid over the picture after (Ink.cs).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class AnimeAtStart : MonoBehaviour
    {
        void Awake() { Anime.Setup(); Destroy(gameObject); }
    }

    /// <summary>In the buildings where craft are made: the ink and the paint, and no squash. A part there is picked up and set down
    /// by where the pointer is on it, and the game finds that by the part's true shape, not the one drawn.</summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public sealed class AnimeInEditor : MonoBehaviour
    {
        void Awake() { Anime.Setup(); gameObject.AddComponent<Ink>(); }
        void Update() => Anime.Keys(false);
    }

    /// <summary>At the space centre: the ink and the paint on its buildings and its ground.</summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public sealed class AnimeAtCentre : MonoBehaviour
    {
        void Awake() { Anime.Setup(); gameObject.AddComponent<Ink>(); }
        void Update() => Anime.Keys(false);
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Anime : MonoBehaviour
    {
        public const string Version = "0.1.0";

        static Mod mod;
        internal static Toggle On, SquashOn, CrewOn, FlamesOn, InkOn, PaintOn;
        internal static Slider Depth, Shrink, Work, Reach;
        internal static Choice Shadows;
        internal static Hotkey HoldKey, OnKey;
        internal static Slider InkWidth, InkStrength, Fold, InkReach, Bands, Tones, PaintStrength, Patch, Colour;

        internal static float Of(Slider slider, float installed) => slider != null ? (float)slider : installed;

        static void Fine(bool fine) { try { if (fine) Advanced(mod); else Simple(mod); } catch (Exception) { } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static void Advanced(Mod page) => page.Advanced();
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static void Simple(Mod page) => page.Simple();

        public static void Setup()
        {
            if (mod != null && Kit.Find("Anime Look") == mod) return;
            mod = Kit.Register("Anime Look", Version, "Every craft and kerbal squashed flat toward the camera, as the 3D models of drawn animation are: from where you look nothing seems flat, but the perspective inside each model is gone and it reads as a drawing. With ink lines and flat paint over the picture.");
            On = mod.Toggle("on", "Anime look", true, "Off: the game as it is.");
            mod.Heading("The squash");
            SquashOn = mod.Toggle("squash", "Squash craft and kerbals", true, "Each craft and each kerbal is flattened along the line from the camera to its middle. Off: they keep their depth.");
            Depth = mod.Slider("depth", "Depth kept", 0.2f, 0.02f, 1f, "", 2, "How much of a model's depth is left: 1 is untouched, 0.02 is paper.");
            CrewOn = mod.Toggle("crew", "The crew in their portraits", true, "Each of the crew inside the craft is flattened toward its own portrait's camera, about its head, and the portraits are drawn in ink and paint with the rest. Off: the portraits as the game has them.");
            FlamesOn = mod.Toggle("flames", "Flames and smoke with the craft", true, "The game's flames, smoke and puffs are moved with the craft they come from, so that a flame leaves the engine where the engine is drawn. Off: they are drawn where they truly are, a little off from a squashed engine.");
            HoldKey = mod.Hotkey("hold", "Hold the squash where it is", KeyCode.J, true, "Holds every model flattened toward where the camera stands now. Move the camera after it and you see them for the flat cut-outs they are. The same key lets go.");
            OnKey = mod.Hotkey("onkey", "Anime look on and off", KeyCode.None, true, "A key for the first switch on this page.");
            Shadows = mod.Choice("shadows", "Shadows", 0, new[] { "of the true shape, on the ground", "of the squashed shape" }, "A flattened model's own shadow is a sliver whenever the sun is to one side. The first keeps each craft's shadow on the ground as it was and has no craft shade itself or another. The second casts and takes the squashed shapes' shadows.");
            Fine(true);
            Shrink = mod.Slider("shrink", "Nearest part never smaller than", 0.6f, 0.2f, 1f, "x", 2, "Squashing moves what is near the camera away from it, so it is drawn smaller. With the camera close to one end of a long craft that end would shrink to a fraction. The squash eases off so that nothing is drawn smaller than this against its true size.");
            Reach = mod.Slider("reach", "Out to", 40f, 5f, 200f, " widths", 0, "A craft farther off than this many times its own size is left alone: from there no difference could be seen.");
            Work = mod.Slider("work", "Vertices reshaped a frame", 300f, 50f, 3000f, " thousand", 0, "The models' shapes are worked out again as the camera goes round them, at most this many vertices a frame. Their places on the screen are right in every frame whatever this is. Only their shading can lag on a very large craft.");
            Fine(false);
            mod.Heading("Ink and paint");
            InkOn = mod.Toggle("ink", "Ink lines", true, "Lines drawn round every shape and along its hard edges.");
            InkWidth = mod.Slider("inkwidth", "Line width", 1.2f, 0.5f, 4f, " px", 1, "How wide the lines are, on a picture 1080 high (wider on a larger one).");
            InkStrength = mod.Slider("inkstrength", "Line strength", 0.6f, 0f, 1f, "", 2, "How dark the lines are: 1 is black ink.");
            Fine(true);
            Fold = mod.Slider("fold", "A line from how sharp an edge", 0.002f, 0.0003f, 0.01f, "", 4, "How sharp a fold in a shape, or how large a step from one thing to the one behind, is drawn as a line: less, more lines (and specks far off); more, outlines only.");
            InkReach = mod.Slider("inkreach", "Lines out to", 300f, 50f, 20000f, " m", 0, "Lines fade out toward this distance, from a quarter of it on: the craft and what stands near are drawn in ink, the landscape is left to its colours.");
            Fine(false);
            PaintOn = mod.Toggle("paint", "Flat paint", true, "The grain of every texture smoothed into flat patches of colour, and the shading drawn a little toward a few steps of brightness, as paint on a cel.");
            Tones = mod.Slider("tones", "Steps of brightness", 0.3f, 0f, 1f, "", 2, "How strongly the shading is drawn toward flat steps of brightness: 0 leaves it smooth, 1 is hard steps (and darker shade).");
            Bands = mod.Slider("bands", "How many steps", 6f, 2f, 12f, "", 0, "How many steps there are to four stops of brightness.");
            PaintStrength = mod.Slider("paintstrength", "Smoothing", 0.7f, 0f, 1f, "", 2, "How much of the smoothing into patches is laid on: 0 leaves every texture its grain.");
            Fine(true);
            Patch = mod.Slider("patch", "Patches of paint", 4f, 1f, 10f, " px", 1, "How far the smoothing of a texture's grain reaches, on a picture 1080 high: more, larger and flatter patches, and a softer picture.");
            Fine(false);
            Colour = mod.Slider("colour", "Colour", 1.15f, 0.5f, 2f, "x", 2, "How strong the colours are: 1 as the game has them.");
            mod.Status = () => (On != null && On ? Squash.Said + (Ink.Said.Length > 0 ? "\n" + Ink.Said : "") : "off");
        }

        void Awake()
        {
            Setup();
            gameObject.AddComponent<Squash>();
            gameObject.AddComponent<Ink>();
        }

        void Update() => Keys(true);

        /// <summary>The mod's keys, looked at every frame in a scene it draws in.</summary>
        internal static void Keys(bool squashes)
        {
            try
            {
                if (OnKey != null && OnKey.Pressed && On != null)
                {
                    On.Value = !On.Value;
                    ScreenMessages.PostScreenMessage("Anime look " + (On.Value ? "on" : "off"), 2f, ScreenMessageStyle.UPPER_CENTER);
                }
                if (squashes && HoldKey != null && HoldKey.Pressed && On != null && On.Value)
                {
                    Squash.Held = !Squash.Held;
                    ScreenMessages.PostScreenMessage(Squash.Held ? "Squash held where the camera stood: look round it (" + HoldKey.Shown + " lets go)" : "Squash follows the camera again", 3f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            catch (Exception) { }
        }

#if DEV
        /// <summary>For the development build: what the squash and the ink last did.</summary>
        public static string Says(string arg) => Squash.Said + " | " + Squash.Detail + " | " + Ink.Said;

        /// <summary>For the development build: every camera there is, in the order they draw, and how many times the glow's camera has been flattened.</summary>
        public static string Cams(string arg)
        {
            var all = new System.Collections.Generic.List<Camera>(Camera.allCameras);
            all.Sort((x, y) => x.depth.CompareTo(y.depth));
            var said = new System.Text.StringBuilder("glow flattened " + Squash.glows + " times");
            foreach (Camera c in all)
                said.Append(" || " + c.name + ": depth " + c.depth + ", clears " + c.clearFlags + ", layers " + c.cullingMask.ToString("x") + (c.targetTexture != null ? ", to a texture" : "")
                            + ", near " + c.nearClipPlane + ", far " + c.farClipPlane);
            if (arg == "clouds") { Squash.CloudsSaid = ""; Squash.CloudsAsked = 20; return "asked"; }
            if (arg == "clouds said") return Squash.CloudsSaid;
            if (arg == "glow") return Squash.GlowSaid + " || " + Squash.WhySaid;
            if (arg == "buffers")
            {
                // (every command a mod has hung on a camera, and what the depth picture and the target are: to find what blackens a picture)
                foreach (Camera c in all)
                    foreach (UnityEngine.Rendering.CameraEvent e in Enum.GetValues(typeof(UnityEngine.Rendering.CameraEvent)))
                        foreach (UnityEngine.Rendering.CommandBuffer b in c.GetCommandBuffers(e)) said.Append(" || " + c.name + " " + e + ": " + b.name + " (" + b.sizeInBytes + ")");
                Texture d = Shader.GetGlobalTexture("_CameraDepthTexture"), behind = Shader.GetGlobalTexture("_AnimeBehind");
                said.Append(" || depth picture: " + (d != null ? d.name + " " + d.width + "x" + d.height : "none") + "; behind: " + ((object)behind == null ? "never set" : behind == null ? "destroyed" : behind.name)
                            + "; active target: " + (RenderTexture.active != null ? RenderTexture.active.name : "the screen"));
                foreach (Camera c in all) said.Append(" || " + c.name + " hdr " + c.allowHDR + " msaa " + c.allowMSAA + " depth mode " + c.depthTextureMode + " path " + c.actualRenderingPath + (c.targetTexture != null ? " to " + c.targetTexture.name : ""));
            }
            if (arg == "big")
            {
                // (what a camera of the buildings draws that is no part of a craft and is large: the floor whose far edge got a line)
                Camera main = EditorLogic.fetch != null ? EditorLogic.fetch.editorCamera : null;
                if (main != null)
                {
                    said.Append(" || editor camera at " + main.transform.position.ToString("0.00") + " fov " + main.fieldOfView);
                    foreach (Camera c in all) if (c.name == "sceneryCam") said.Append(" || sceneryCam at " + c.transform.position.ToString("0.00") + " fov " + c.fieldOfView + " turned " + Quaternion.Angle(c.transform.rotation, main.transform.rotation).ToString("0.000"));
                    foreach (Renderer r in FindObjectsOfType<Renderer>())
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || (main.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                        if (r.GetComponentInParent<Part>() != null || r.bounds.size.magnitude < 15f) continue;
                        Material m = r.sharedMaterial;
                        said.Append(" || " + r.name + " (under " + (r.transform.parent != null ? r.transform.parent.name : "nothing") + "), layer " + r.gameObject.layer + ", " + r.GetType().Name + ", size " + r.bounds.size.ToString("0.0")
                                    + ", shader " + (m != null && m.shader != null ? m.shader.name : "none") + ", queue " + (m != null ? m.renderQueue : -1) + ", casts " + r.shadowCastingMode);
                    }
                }
            }
            return said.ToString();
        }

        /// <summary>For the development build: "hold" or "free" (the squash held where the camera stands, or following it); "on"/"off";
        /// "depth=0.1", "squash=0", "ink=0", "paint=0", "shadows=1", "bands=4" and the like set a setting for this run (written to the mod's settings as any change is).</summary>
        public static string Try(string arg)
        {
            string said = "";
            foreach (string word in (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = word.Split('=');
                string key = kv[0].Trim().ToLowerInvariant();
                float v = kv.Length > 1 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
                switch (key)
                {
                    case "hold": Squash.Held = true; break;
                    case "free": Squash.Held = false; break;
                    case "on": On.Value = kv.Length > 1 ? v > 0.5f : true; break;
                    case "off": On.Value = false; break;
                    case "squash": SquashOn.Value = v > 0.5f; break;
                    case "crew": CrewOn.Value = v > 0.5f; break;
                    case "ink": InkOn.Value = v > 0.5f; break;
                    case "paint": PaintOn.Value = v > 0.5f; break;
                    case "depth": Depth.Value = v; break;
                    case "shadows": Shadows.Value = (int)v; break;
                    case "shrink": Shrink.Value = v; break;
                    case "reach": Reach.Value = v; break;
                    case "work": Work.Value = v; break;
                    case "inkwidth": InkWidth.Value = v; break;
                    case "inkstrength": InkStrength.Value = v; break;
                    case "bands": Bands.Value = v; break;
                    case "paintstrength": PaintStrength.Value = v; break;
                    case "colour": Colour.Value = v; break;
                    case "patch": Patch.Value = v; break;
                    case "tones": Tones.Value = v; break;
                    case "lean": Ink.Lean = v; break;
                    case "fadefrom": Ink.FadeFrom = v; break;
                    case "grain": Ink.Grain = v; break;
                    case "behind": Ink.TestBehind = (int)v; break;
                    case "flames": FlamesOn.Value = v > 0.5f; break;
                    case "particles": Squash.TestParticles = (int)v; break;
                    case "iva": if (CameraManager.Instance != null) said += CameraManager.Instance.SetCameraIVA() ? "in the cockpit; " : "no cockpit to go to; "; break;
                    case "outside": if (CameraManager.Instance != null) CameraManager.Instance.SetCameraFlight(); break;
                    case "fold": Fold.Value = v; break;
                    case "inkreach": InkReach.Value = v; break;
                    case "show": Ink.Show = (int)v; break;
                    case "normals": Squash.TestNormals = (int)v; break;
                    default: said += "(unknown: " + key + ") "; break;
                }
            }
            return said + "on " + On.Value + ", squash " + SquashOn.Value + " depth " + ((float)Depth).ToString("0.00") + (Squash.Held ? " held" : "") + ", shadows " + Shadows.Value + ", ink " + InkOn.Value + ", paint " + PaintOn.Value;
        }
#endif
    }
}
