using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeLook
{
    /// <summary>
    /// The squash: every craft and kerbal flattened along the line from the camera to its own middle, to a fraction of its depth.
    ///
    /// How. A flattening is one map of space for a whole craft: every point is moved toward the plane through the craft's middle
    /// that faces the camera, to "depth" of its distance from it. The game's own shaders cannot be handed such a map, so for the
    /// flight's camera each mesh of a craft is hidden and drawn again here, flattened (Graphics.DrawMesh, which takes any matrix),
    /// with the mesh's own materials and whatever the game set on them. Every other camera sees the craft as it is.
    ///
    /// A matrix alone would flatten the meshes' normals with them, and a craft would be lit as the flat sheet it has become:
    /// one brightness all over, dark whenever the sun is to one side. So each mesh has a copy whose vertices are flattened here,
    /// about its own origin, with its normals left as they were, and the copy is drawn with a matrix that only moves it to its
    /// place. The copies are reshaped as the camera goes round, the most out of shape first, so many vertices a frame; a copy
    /// not yet reshaped is drawn through a matrix that undoes the shape it has and lays on the one it should have, so every
    /// vertex is in its right place in every frame and only the shading of that mesh is a little off until its turn comes.
    ///
    /// On the ground a craft is flattened level only: along the level part of the line from the camera, so that nothing of it is
    /// moved up or down and it stands where it stood. Flattened along the whole line, a rocket seen from below had its foot
    /// drawn up and away from the pad, and floated (the owner saw it at once). The level squash turns into the whole one as
    /// the craft's lowest point leaves the ground by the craft's own size.
    ///
    /// Flames, smoke and puffs are particles, which no matrix can be laid on: each cloud of them that a craft gives off is taken
    /// as a mesh every frame (ParticleSystemRenderer.BakeMesh), its points moved as the craft's are, and drawn in the cloud's
    /// place. Within the craft's own reach that is the flattening itself, so a flame leaves the engine where the engine is
    /// drawn; beyond it every point is moved by what the craft's far end is moved by and no more, or a smoke trail a
    /// kilometre long would be gathered up to the craft.
    ///
    /// Beside Firefly (a mod that draws the glow of re-entry itself, round each part's true shape, through commands of its
    /// own on the camera, which nothing here can flatten): while its glow shows on a craft that craft is left its depth, eased
    /// out and in, or the glow stood off it as a ghost of its nose ahead of the nose drawn. The ink and the paint stay.
    ///
    /// The crew inside a craft are squashed too, where they are seen: in their portraits. Each is flattened toward its own
    /// portrait's camera, about its head, which is the trick as its makers use it: a face that does not loom at a camera half
    /// a metre from it.
    /// </summary>
    public sealed class Squash : MonoBehaviour
    {
        /// <summary>Held: every model stays flattened toward where the camera stood when it was set (kept in each craft's own frame).</summary>
        internal static bool Held;
        internal static string Said = "", Detail = "";
        /// <summary>(Development build: 1 draws by the matrix alone, the meshes' normals flattened with them, to see what the copies are for.)</summary>
        internal static int TestNormals;
        /// <summary>(Development build: 0 leaves the particles alone; 2 draws their meshes unmoved over the game's own, 4 five metres
        /// to the right of them, to see that they are the same.)</summary>
        internal static int TestParticles = 1;
#if DEV
        internal static int CloudsAsked;
        internal static string CloudsSaid = "";
#endif

        internal sealed class Piece
        {
            public Renderer Renderer;
            public MeshFilter Filter;
            public SkinnedMeshRenderer Skinned;
            public ParticleSystemRenderer Particles;    // (a cloud of particles: flames, smoke, puffs)
            public ParticleSystem System;
            public Part Part;
            public Mesh Source;             // the mesh the copy was made of
            public Mesh Copy;               // its vertices flattened about the mesh's own origin (nothing: the mesh cannot be read)
            public Vector3[] From;          // the source's vertices
            public Vector3 U, C;            // the copy's shape: each vertex v moved to v - (1 - S) U (C . v)
            public float S = 1f;
            public float Radius = 1f;       // how far from its origin the mesh reaches, in the world's metres
            public bool Boundless;          // (its mesh says it is larger than any craft, so as never to be left out of a picture: an effect's, such as a Waterfall plume)
            public int Frame = -1;          // (a kerbal's mesh: the frame its pose was last taken in)
        }

        internal sealed class Group
        {
            public Vessel Vessel;
            public readonly List<Piece> Pieces = new List<Piece>();
            public readonly Dictionary<Renderer, Piece> By = new Dictionary<Renderer, Piece>();
            public KerbalEVA Kerbal;
            public Kerbal Crew;                     // (one of the crew inside a craft, drawn so in its portrait: no vessel then)
            public Camera Portrait;
            public List<Animator> Animators;        // (a kerbal's, kept animating while its own meshes are out of the picture)
            public List<AnimatorCullingMode> AnimatorsWere;
            public int Parts = -1;
            public float Scanned = -100f;
            public bool Stale = true;
            public Vector3 PivotLocal; public bool PivotSet;
            public Vector3 EyeLocal; public bool EyeSet;
            public float Near, Radius = 5f;         // from the last frame: how far toward the camera it reaches past its middle, and how far any way
            public float Below;                     // from the last frame: how far under its middle its lowest point is (metres, below nought)
            public object Firefly;                  // (Firefly's own keeper of this craft's glow, if that mod is here)
            public bool FireflyLooked;
            public float GlowLooked = -10f;
            public bool GlowThere;
            public float Aglow;                     // 1 while another mod's glow of re-entry shows on it (it keeps its depth then), eased
            public float Fly = -1f;                 // 0 on the ground (flattened level only), 1 well clear of it (along the whole line); under nought: not yet known
            public Vector3 Up = Vector3.up;
            // this frame's
            public int Frame = -1;
            public Vector3 Pivot, Eye, Axis;
            public float Dist, Depth = 1f;
            public bool Drawn;
        }

        struct Hidden { public Renderer Renderer; public ShadowCastingMode Was; public bool Forced; public ParticleSystemRenderer Cloud; public float Size; }

        readonly Dictionary<Vessel, Group> groups = new Dictionary<Vessel, Group>();
        readonly List<Group> all = new List<Group>();
        readonly Dictionary<Part, Group> partOf = new Dictionary<Part, Group>();
        readonly Dictionary<int, Vector3[]> sources = new Dictionary<int, Vector3[]>();
        readonly List<Hidden> hidden = new List<Hidden>(1024), hiddenCrew = new List<Hidden>(64);
        readonly List<Group> crew = new List<Group>();
        static readonly Dictionary<Camera, Group> crewBy = new Dictionary<Camera, Group>();
        readonly List<Animator> animators = new List<Animator>(8);
        float crewLooked = -10f;

        /// <summary>Whether a camera is one of the crew's portraits' (those whose kerbal is squashed toward it).</summary>
        internal static bool IsPortrait(Camera cam) => crewBy.ContainsKey(cam);
        /// <summary>The portraits' cameras now.</summary>
        internal static Dictionary<Camera, Group>.KeyCollection Portraits => crewBy.Keys;
        readonly List<Renderer> found = new List<Renderer>(1024), more = new List<Renderer>(64);
        readonly List<Material> materials = new List<Material>(8);
        readonly List<Vector3> pose = new List<Vector3>(4096);
        readonly List<Vessel> gone = new List<Vessel>();
        Vector3[] scratch = new Vector3[8192];
        MaterialPropertyBlock block;
        float strength;
        readonly List<Camera> glowing = new List<Camera>(2);    // (the game's cameras for the glow of fast flight, while their picture is flattened)
        internal static int glows;                  // (how many times it has been: for the development build to say)
        int budgetFrame = -1, budget, tendFrame = -1;
        float lastChecked = -10f;
        int faults;
        int drawnPieces, drawnGroups, reshaped, unread, waiting, clouds, cloudPoints, glowLeft;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        double spent; int spentFrames; float spentSaid;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            Camera.onPreCull += Before;
            Camera.onPostRender += After;
            GameEvents.onVesselWasModified.Add(Changed);
            GameEvents.onVesselCreate.Add(Changed);
            GameEvents.onVesselPartCountChanged.Add(Changed);
        }

        void OnDestroy()
        {
            Camera.onPreCull -= Before;
            Camera.onPostRender -= After;
            GameEvents.onVesselWasModified.Remove(Changed);
            GameEvents.onVesselCreate.Remove(Changed);
            GameEvents.onVesselPartCountChanged.Remove(Changed);
            ShowAgain(hidden);
            AfterGlow();
            DropCrew();
            foreach (Group g in all) Drop(g);
            all.Clear(); groups.Clear(); partOf.Clear(); sources.Clear();
            Held = false;
            Said = ""; Detail = "";
        }

        void OnDisable() { ShowAgain(hidden); ShowAgain(hiddenCrew); AfterGlow(); }

        void Changed(Vessel v) { for (int i = 0; i < all.Count; i++) all[i].Stale = true; }

        /// <summary>A piece done with: its copy destroyed.</summary>
        static void Let(Piece p)
        {
            if (p.Copy != null) Destroy(p.Copy);
            p.Copy = null;
        }

        static void Drop(Group g)
        {
            foreach (Piece p in g.Pieces) Let(p);
            g.Pieces.Clear(); g.By.Clear();
            if (g.Animators != null)
                for (int i = 0; i < g.Animators.Count; i++) if (g.Animators[i] != null) g.Animators[i].cullingMode = g.AnimatorsWere[i];
            g.Animators = null; g.AnimatorsWere = null;
        }

        /// <summary>A kerbal's animators kept going whether its meshes are seen or not. The game animates a kerbal only while a camera
        /// sees one of its meshes, and here its own meshes are taken out of the picture and copies drawn: without this it would
        /// stand frozen in its last pose.</summary>
        void KeepAnimating(Group g, Transform root)
        {
            animators.Clear();
            root.GetComponentsInChildren(true, animators);
            if (g.Animators == null) { g.Animators = new List<Animator>(); g.AnimatorsWere = new List<AnimatorCullingMode>(); }
            for (int i = 0; i < animators.Count; i++)
            {
                Animator a = animators[i];
                if (a == null || g.Animators.Contains(a)) continue;
                g.Animators.Add(a); g.AnimatorsWere.Add(a.cullingMode);
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            animators.Clear();
        }

        void DropCrew()
        {
            ShowAgain(hiddenCrew);
            foreach (Group g in crew) Drop(g);
            crew.Clear(); crewBy.Clear();
        }

        // ---------------------------------------------------------------- which craft there are, and what each is drawn with

        void Update()
        {
            try
            {
                bool want = Anime.On != null && Anime.On && Anime.SquashOn != null && Anime.SquashOn && !MapView.MapIsEnabled;
                // (called the last of all before a camera draws: Smooth Running carries the craft's models and the camera on between
                // physics steps there, and the squash must be worked out from where they are drawn; looked at once a second)
                if (want && Time.unscaledTime - lastChecked > 1f)
                {
                    lastChecked = Time.unscaledTime;
                    Delegate[] called = Camera.onPreCull != null ? Camera.onPreCull.GetInvocationList() : null;
                    if (called != null && called.Length > 0 && !ReferenceEquals(called[called.Length - 1].Target, this)) { Camera.onPreCull -= Before; Camera.onPreCull += Before; }
                }
                strength = Mathf.MoveTowards(strength, want ? 1f : 0f, Time.unscaledDeltaTime / 0.35f);
                if (strength <= 0f)
                {
                    if (all.Count > 0) { foreach (Group g in all) Drop(g); all.Clear(); groups.Clear(); partOf.Clear(); sources.Clear(); }
                    if (crew.Count > 0) DropCrew();
                    Said = want ? "" : "the squash is off"; Detail = "";
                    return;
                }
                float now = Time.unscaledTime;
                if (now - crewLooked > 1f) { crewLooked = now; TendCrew(); }
                if (now - spentSaid > 0.5f)
                {
                    Said = drawnGroups == 0 ? (glowLeft > 0 ? "the craft left its depth while Firefly's glow shows on it" : "nothing near enough to squash") : "squashing " + drawnGroups + (drawnGroups == 1 ? " craft" : " craft and kerbals") + " (" + drawnPieces + " meshes) to " + (Mathf.Lerp(1f, Anime.Of(Anime.Depth, 0.2f), strength)).ToString("0.00") + " of their depth" + (Held ? ", held" : "") + (crew.Count > 0 ? "; " + crew.Count + " of the crew in their portraits" : "");
                    Detail = (spentFrames > 0 ? (spent / spentFrames).ToString("0.00") + " ms a frame; " : "") + reshaped + " vertices reshaped in the last frame, " + waiting + " meshes waiting their turn, " + unread + " that cannot be read (drawn by matrix, their shading flattened)" + (clouds > 0 ? "; " + clouds + " clouds of particles moved with their craft (" + cloudPoints + " points)" : "");
#if DEV
                    WhySaid = all.Count + " groups, strength " + strength.ToString("0.00") + ", mode " + (CameraManager.Instance != null ? CameraManager.Instance.currentCameraMode.ToString() : "none");
                    foreach (Group g in all) WhySaid += " || " + (g.Vessel != null ? g.Vessel.vesselName : "?") + ": " + g.Pieces.Count + " pieces, frame " + (Time.frameCount - g.Frame) + " ago, dist " + g.Dist.ToString("0.0") + ", depth " + g.Depth.ToString("0.00") + ", fly " + g.Fly.ToString("0.00") + ", aglow " + g.Aglow.ToString("0.00") + (g.GlowThere ? " (glow there)" : "") + ", radius " + g.Radius.ToString("0.0") + ", near " + g.Near.ToString("0.0") + ", drawn " + g.Drawn;
                    foreach (Group g in all)
                    {
                        Piece big = null;
                        foreach (Piece q in g.Pieces) if (q.Renderer != null && q.Renderer.enabled && q.Renderer.gameObject.activeInHierarchy && q.Particles == null && (big == null || q.Radius > big.Radius)) big = q;
                        Piece off = null; float offBy = 0f;
                        foreach (Piece q in g.Pieces)
                        {
                            if (q.Renderer == null || !q.Renderer.enabled || !q.Renderer.gameObject.activeInHierarchy || q.Particles != null) continue;
                            float d = (q.Renderer.transform.position - g.Pivot).magnitude;
                            if (d > offBy) { offBy = d; off = q; }
                        }
                        if (off != null) WhySaid += " || farthest of " + (g.Vessel != null ? g.Vessel.vesselName : "?") + ": " + off.Renderer.name + " under " + (off.Renderer.transform.parent != null ? off.Renderer.transform.parent.name : "-") + " under " + (off.Renderer.transform.parent != null && off.Renderer.transform.parent.parent != null ? off.Renderer.transform.parent.parent.name : "-") + " of " + (off.Part != null ? off.Part.name : "no part") + ", " + offBy.ToString("0.0") + " m off, radius " + off.Radius.ToString("0.0") + ", layer " + off.Renderer.gameObject.layer + ", queue " + (off.Renderer.sharedMaterial != null ? off.Renderer.sharedMaterial.renderQueue : -1) + ", shader " + (off.Renderer.sharedMaterial != null ? off.Renderer.sharedMaterial.shader.name : "-") + ", " + off.Renderer.GetType().Name;
                        if (big != null) WhySaid += " || largest of " + (g.Vessel != null ? g.Vessel.vesselName : "?") + ": " + big.Renderer.name + " under " + (big.Renderer.transform.parent != null ? big.Renderer.transform.parent.name : "-") + " of " + (big.Part != null ? big.Part.name : "no part") + ", radius " + big.Radius.ToString("0.0") + ", scale " + big.Renderer.transform.lossyScale.ToString("0.00") + ", queue " + (big.Renderer.sharedMaterial != null ? big.Renderer.sharedMaterial.renderQueue : -1) + ", shader " + (big.Renderer.sharedMaterial != null ? big.Renderer.sharedMaterial.shader.name : "-") + (big.Boundless ? ", boundless" : "") + (big.From == null ? ", unread" : "");
                    }
#endif
                    spent = 0; spentFrames = 0; spentSaid = now;
                }
            }
            catch (Exception e) { Kit.Log("Anime Look", "the squash, looking for craft: " + e); }
        }

        /// <summary>The craft there are now, each with what it is drawn with: looked at just before the frame is drawn, so that a stage
        /// that came off in this frame is a craft of its own in this frame's picture already.</summary>
        void Tend()
        {
            List<Vessel> loaded = FlightGlobals.VesselsLoaded;
            if (loaded == null) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < loaded.Count; i++)
            {
                Vessel v = loaded[i];
                if (v == null || v.rootPart == null || groups.ContainsKey(v)) continue;
                var g = new Group { Vessel = v };
                // (a craft that has just come off another begins from the other's middle and eases to its own: no jump at staging)
                if (partOf.TryGetValue(v.rootPart, out Group from) && from.PivotSet && from.Vessel != null && from.Vessel != v)
                {
                    g.PivotLocal = v.transform.InverseTransformPoint(from.Vessel.transform.TransformPoint(from.PivotLocal));
                    g.PivotSet = true;
                }
                groups[v] = g; all.Add(g);
            }
            gone.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                Group g = all[i];
                Vessel v = g.Vessel;
                if (v == null || !v.loaded || v.rootPart == null) { gone.Add(v); Drop(g); all.RemoveAt(i--); continue; }
                if (g.Stale || v.parts.Count != g.Parts || now - g.Scanned > 3f + (i % 7) * 0.2f) Scan(g, now);
            }
            if (gone.Count > 0)
            {
                // (a vessel that is no more cannot be looked up by itself: the table is made again from those left)
                groups.Clear();
                foreach (Group g in all) groups[g.Vessel] = g;
            }
            if (partOf.Count > 4096 + 4 * Mathf.Max(1, all.Count) * 256)
            {
                // (parts that are no more are still in the table: made again from the craft there are)
                partOf.Clear();
                foreach (Group g in all) for (int i = 0; i < g.Vessel.parts.Count; i++) if (g.Vessel.parts[i] != null) partOf[g.Vessel.parts[i]] = g;
            }
        }

        void Scan(Group g, float now)
        {
            Vessel v = g.Vessel;
            Transform root = v.rootPart.transform;
            found.Clear();
            root.GetComponentsInChildren(true, found);
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part part = v.parts[i];
                if (part == null) continue;
                partOf[part] = g;
                if (part.transform == root || part.transform.IsChildOf(root)) continue;
                more.Clear();
                part.transform.GetComponentsInChildren(true, more);
                found.AddRange(more);
            }
            Reconcile(g);
            g.Kerbal = v.isEVA ? v.rootPart.FindModuleImplementing<KerbalEVA>() : null;
            if (g.Kerbal != null) KeepAnimating(g, root);
            g.FireflyLooked = false;
            g.Parts = v.parts.Count; g.Scanned = now; g.Stale = false;
        }

        /// <summary>The renderers just found (in `found`) made a group's pieces: new ones added, those gone taken out with their copies.</summary>
        void Reconcile(Group g)
        {
            var seen = new HashSet<Renderer>();
            for (int i = 0; i < found.Count; i++)
            {
                Renderer r = found[i];
                if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer || r is ParticleSystemRenderer) || !seen.Add(r)) continue;
                if (g.By.ContainsKey(r)) continue;
                // (the flight's camera hangs under the craft it follows, and what another mod hangs on the camera is no part of the
                // craft: beside Deferred, whose camera carries a quad, the craft was taken to reach as far as the camera, and
                // nothing so near the camera is squashed)
                if (r.GetComponentInParent<Camera>() != null) continue;
                var p = new Piece { Renderer = r, Skinned = r as SkinnedMeshRenderer, Particles = r as ParticleSystemRenderer, Part = r.GetComponentInParent<Part>() };
                if (p.Particles != null) { p.System = r.GetComponent<ParticleSystem>(); if (p.System == null) continue; }
                else if (p.Skinned == null) { p.Filter = r.GetComponent<MeshFilter>(); if (p.Filter == null) continue; }
                g.By[r] = p; g.Pieces.Add(p);
            }
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                Piece p = g.Pieces[i];
                if (p.Renderer != null && seen.Contains(p.Renderer)) continue;
                Let(p);
                if ((object)p.Renderer != null) g.By.Remove(p.Renderer);
                g.Pieces.RemoveAt(i--);
            }
            if (g.By.Count != g.Pieces.Count)
            {
                // (a renderer destroyed since: its key can no longer be found by itself)
                g.By.Clear();
                foreach (Piece p in g.Pieces) g.By[p.Renderer] = p;
            }
        }

        /// <summary>The crew inside the craft that is flown, each with its portrait's camera: looked for once a second.</summary>
        void TendCrew()
        {
            bool want = Anime.CrewOn == null || Anime.CrewOn;
            Vessel v = want ? FlightGlobals.ActiveVessel : null;
            if (v == null || !v.loaded) { if (crew.Count > 0) DropCrew(); return; }
            var there = new HashSet<Kerbal>();
            foreach (ProtoCrewMember member in v.GetVesselCrew())
            {
                Kerbal k = member != null ? member.KerbalRef : null;
                if (k == null) continue;
                Camera cam = member.seat != null && member.seat.portraitCamera != null ? member.seat.portraitCamera : k.kerbalCam;
                if (cam == null) continue;
                there.Add(k);
                Group g = null;
                for (int i = 0; i < crew.Count; i++) if (crew[i].Crew == k) g = crew[i];
                if (g != null && g.Portrait != cam) { crewBy.Remove(g.Portrait); g.Portrait = cam; crewBy[cam] = g; }
                if (g == null) { g = new Group { Crew = k, Portrait = cam }; crew.Add(g); crewBy[cam] = g; }
                found.Clear();
                k.GetComponentsInChildren(true, found);
                Reconcile(g);
                KeepAnimating(g, k.transform);
            }
            for (int i = 0; i < crew.Count; i++)
            {
                Group g = crew[i];
                if (g.Crew != null && there.Contains(g.Crew)) continue;
                ShowAgain(hiddenCrew);
                Drop(g);
                crew.RemoveAt(i--);
                crewBy.Clear();
                foreach (Group left in crew) if (left.Portrait != null) crewBy[left.Portrait] = left;
            }
        }

        // ---------------------------------------------------------------- each frame, for the flight's camera

        static bool Flight(Camera cam)
        {
            FlightCamera fc = FlightCamera.fetch;
            if (fc == null || fc.cameras == null) return false;
            for (int i = 0; i < fc.cameras.Length; i++) if (fc.cameras[i] == cam) return true;
            return false;
        }

        /// <summary>
        /// The vapour and the heat of fast flight are drawn by two cameras of the game's own (FXDepthCamera, then FXCamera), which
        /// draw the craft again through a shader that drags each vertex out behind it. Given the flattened copies, it dragged trails of their whole
        /// length out of a craft without its depth: from behind a climbing rocket they stood out in front of it, a bright blob
        /// over its nose (seen in the first film of a launch). So those cameras are given the craft's own meshes, and their
        /// whole picture is flattened as the craft that is flown is: the trails with the craft they leave.
        /// </summary>
        void BeforeGlow(Camera cam)
        {
            for (int i = 0; i < glowing.Count; i++) if ((object)glowing[i] == (object)cam) return;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || !groups.TryGetValue(v, out Group g) || g.Frame != Time.frameCount || !g.Drawn) return;
            Vector3 a = g.Axis;
            float k = 1f - g.Depth, along = k * Vector3.Dot(a, g.Pivot);
            Matrix4x4 f = Matrix4x4.identity;
            f.m00 = 1f - k * a.x * a.x; f.m01 = -k * a.x * a.y; f.m02 = -k * a.x * a.z; f.m03 = a.x * along;
            f.m10 = -k * a.y * a.x; f.m11 = 1f - k * a.y * a.y; f.m12 = -k * a.y * a.z; f.m13 = a.y * along;
            f.m20 = -k * a.z * a.x; f.m21 = -k * a.z * a.y; f.m22 = 1f - k * a.z * a.z; f.m23 = a.z * along;
            cam.worldToCameraMatrix = cam.worldToCameraMatrix * f;
            glowing.Add(cam);
            glows++;
        }

        void AfterGlow()
        {
            for (int i = 0; i < glowing.Count; i++) if (glowing[i] != null) glowing[i].ResetWorldToCameraMatrix();
            glowing.Clear();
        }

        void Before(Camera cam)
        {
            if (strength <= 0f) return;
            if (crew.Count > 0 && crewBy.TryGetValue(cam, out Group one)) { BeforePortrait(one, cam); return; }
            if (!Flight(cam))
            {
                if (Names.Is(cam, "FXCamera") || Names.Is(cam, "FXDepthCamera")) { try { BeforeGlow(cam); } catch (Exception e) { if (faults++ < 5) Kit.Log("Anime Look", "the squash, the glow: " + e); } }
                return;
            }
            try
            {
                if (hidden.Count > 0) ShowAgain(hidden);
                if (CameraManager.Instance != null && (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA || CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal)) return;
                clock.Restart();
                int frame = Time.frameCount;
                if (budgetFrame != frame)
                {
                    budgetFrame = frame; budget = (int)(Anime.Of(Anime.Work, 300f) * 1000f);
                    drawnPieces = 0; drawnGroups = 0; reshaped = 0; unread = 0; waiting = 0; clouds = 0; cloudPoints = 0; glowLeft = 0;
                }
                if (tendFrame != frame) { tendFrame = frame; Tend(); }
                float eased = strength * strength * (3f - 2f * strength);
                float depth = Mathf.Lerp(1f, Mathf.Clamp(Anime.Of(Anime.Depth, 0.2f), 0.02f, 1f), eased);
                Vector3 camera = cam.transform.position;
                // (first every craft's own middle and line; then a kerbal on a ladder takes the craft's it is on, as a rider and its mount)
                for (int i = 0; i < all.Count; i++) if (!OnLadder(all[i])) Frame(all[i], camera, depth, frame, null);
                for (int i = 0; i < all.Count; i++) if (OnLadder(all[i])) Frame(all[i], camera, depth, frame, Lead(all[i], frame));
                bool trueShadows = Anime.Shadows == null || Anime.Shadows.Value == 0;
                for (int i = 0; i < all.Count; i++) if (all[i].Drawn) Draw(all[i], cam, trueShadows, frame, hidden);
                clock.Stop();
                spent += clock.Elapsed.TotalMilliseconds; if (cam == FlightCamera.fetch.cameras[0]) spentFrames++;
            }
            catch (Exception e) { if (faults++ < 5) Kit.Log("Anime Look", "the squash: " + e); ShowAgain(hidden); }
        }

        /// <summary>One of the crew, before its portrait's camera draws: flattened toward that camera, about its head.</summary>
        void BeforePortrait(Group g, Camera cam)
        {
            try
            {
                if (hiddenCrew.Count > 0) ShowAgain(hiddenCrew);
                Kerbal k = g.Crew;
                if (k == null) return;
                int frame = Time.frameCount;
                if (budgetFrame != frame)
                {
                    budgetFrame = frame; budget = (int)(Anime.Of(Anime.Work, 300f) * 1000f);
                    drawnPieces = 0; drawnGroups = 0; reshaped = 0; unread = 0; waiting = 0; clouds = 0; cloudPoints = 0; glowLeft = 0;
                }
                float eased = strength * strength * (3f - 2f * strength);
                Transform head = k.headTransform != null ? k.headTransform : k.transform;
                g.Pivot = head.position;
                Vector3 line = cam.transform.position - g.Pivot;
                g.Dist = line.magnitude;
                if (g.Dist < 0.02f) return;
                g.Axis = line / g.Dist;
                g.Eye = cam.transform.position;
                g.Depth = Mathf.Lerp(1f, Mathf.Clamp(Anime.Of(Anime.Depth, 0.2f), 0.02f, 1f), eased);
                g.Frame = frame;
                if (g.Depth >= 0.995f) return;
                int groupsWere = drawnGroups;
                Draw(g, cam, false, frame, hiddenCrew);
                drawnGroups = groupsWere;
            }
            catch (Exception e) { if (faults++ < 5) Kit.Log("Anime Look", "the squash, a portrait: " + e); ShowAgain(hiddenCrew); }
        }

#if DEV
        internal static string GlowSaid = "";
        internal static string WhySaid = "";
#endif
        static bool fireflyAsked;
        static Type fireflyKind;
        static System.Reflection.FieldInfo fireflyStrength;

        /// <summary>Whether Firefly's glow of re-entry is showing on a craft now: its keeper of that craft's glow (a VesselModule,
        /// Firefly.AtmoFxModule) holds how strong it last was (lastStrength; read as of Firefly 1.0.6). Without Firefly, or with one
        /// that keeps it otherwise: no.</summary>
        static bool OthersGlow(Group g)
        {
            try
            {
                if (!fireflyAsked)
                {
                    fireflyAsked = true;
                    foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    {
                        if (a == null || a.assembly == null || a.name != "Firefly") continue;
                        fireflyKind = a.assembly.GetType("Firefly.AtmoFxModule");
                        if (fireflyKind != null) fireflyStrength = fireflyKind.GetField("lastStrength", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (fireflyStrength != null && fireflyStrength.FieldType != typeof(float)) fireflyStrength = null;
                        Kit.Log("Anime Look", fireflyStrength != null ? "Firefly is here: a craft is left its depth while Firefly's glow shows on it" : "Firefly is here, and its glow's strength is not where it was: its glow will stand off a squashed craft");
                    }
                }
                if (fireflyStrength == null || g.Vessel == null) return false;
                if (!g.FireflyLooked)
                {
                    g.FireflyLooked = true; g.Firefly = null;
                    List<VesselModule> modules = g.Vessel.vesselModules;
                    if (modules != null) for (int i = 0; i < modules.Count; i++) if (modules[i] != null && fireflyKind.IsInstanceOfType(modules[i])) g.Firefly = modules[i];
                }
                if (g.Firefly == null || (g.Firefly as UnityEngine.Object) == null) return false;
                float strength = (float)fireflyStrength.GetValue(g.Firefly);
#if DEV
                GlowSaid = "Firefly's strength " + strength.ToString("0.0000");
#endif
                return strength > 0.001f;
            }
            catch (Exception) { fireflyStrength = null; return false; }
        }

        static bool OnLadder(Group g)
        {
            try { return g.Kerbal != null && g.Kerbal.OnALadder; } catch (Exception) { return false; }
        }

        /// <summary>The craft whose ladder a kerbal is on, if its middle and line have been worked out this frame.</summary>
        Group Lead(Group g, int frame)
        {
            try
            {
                Part on = g.Kerbal.LadderPart;
                if (on == null || on.vessel == null || on.vessel == g.Vessel) return null;
                return groups.TryGetValue(on.vessel, out Group lead) && lead.Frame == frame && lead.Dist >= 0.05f ? lead : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A craft's middle, the line it is flattened along and how much, for this frame.</summary>
        void Frame(Group g, Vector3 camera, float depth, int frame, Group lead)
        {
            Vessel v = g.Vessel;
            if (v == null) { g.Drawn = false; return; }
            Transform root = v.transform;
            bool first = g.Frame != frame;
            if (lead == null)
            {
                Vector3 target = v.localCoM;
                if (!g.PivotSet) { g.PivotLocal = target; g.PivotSet = true; }
                else if (first) g.PivotLocal = Vector3.Lerp(g.PivotLocal, target, 1f - Mathf.Exp(-6f * Mathf.Min(Time.deltaTime, 0.1f)));
                if (Held) { if (!g.EyeSet) { g.EyeLocal = root.InverseTransformPoint(camera); g.EyeSet = true; } g.Eye = root.TransformPoint(g.EyeLocal); }
                else { g.EyeSet = false; g.Eye = camera; }
            }
            else
            {
                // (toward the craft's middle, eased, so that taking hold of a ladder and letting go are no jump)
                Vector3 target = root.InverseTransformPoint(lead.Pivot);
                if (!g.PivotSet) { g.PivotLocal = target; g.PivotSet = true; }
                else if (first) g.PivotLocal = Vector3.Lerp(g.PivotLocal, target, 1f - Mathf.Exp(-6f * Mathf.Min(Time.deltaTime, 0.1f)));
                g.Eye = lead.Eye;
            }
            g.Frame = frame;
            g.Pivot = root.TransformPoint(g.PivotLocal);
            Vector3 line = g.Eye - g.Pivot;
            g.Dist = line.magnitude;
            g.Drawn = false;
            if (g.Dist < 0.05f) return;
            g.Axis = line / g.Dist;
            float s = depth;
            // (on the ground, along the level part of the line alone, and by as much less as the line is steep: straight down on a
            // landed craft there is no level part, and it keeps its depth)
            Group ground = lead ?? g;
            if (lead == null)
            {
                g.Up = v.upAxis;
                float clear = (float)v.radarAltitude + g.Below;
                float target = v.Landed || v.Splashed ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, Mathf.Max(8f, g.Radius), clear));
                if (g.Fly < 0f) g.Fly = target;
                else if (first) g.Fly = Mathf.MoveTowards(g.Fly, target, 1.5f * Mathf.Min(Time.deltaTime, 0.1f));
            }
            else { g.Up = lead.Up; g.Fly = lead.Fly; }
            // (under another mod's glow of re-entry, which is drawn round the true shape: its depth back, eased)
            if (lead == null && first)
            {
                float now = Time.unscaledTime;
                if (now - g.GlowLooked > 0.2f) { g.GlowLooked = now; g.GlowThere = OthersGlow(g); }
                g.Aglow = Mathf.MoveTowards(g.Aglow, g.GlowThere ? 1f : 0f, 1.2f * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            }
            if (ground.Fly < 0.999f)
            {
                Vector3 level = g.Axis - g.Up * Vector3.Dot(g.Axis, g.Up);
                Vector3 along = Vector3.Lerp(level, g.Axis, Mathf.Max(ground.Fly, 0f));
                float much = along.magnitude;
                if (much < 0.03f) return;
                g.Axis = along / much;
                s = 1f - (1f - s) * Mathf.Min(much, 1f);
            }
            // (what is near the camera is moved away from it and so drawn smaller: never smaller than "shrink" of its true size)
            float least = Mathf.Clamp(Anime.Of(Anime.Shrink, 0.6f), 0.2f, 0.99f);
            if (g.Near > (1f - least) * g.Dist) s = Mathf.Max(s, (g.Near - (1f - least) * g.Dist) / (least * g.Near));
            if (lead != null) s = Mathf.Max(s, lead.Depth);
            else if (g.Aglow > 0f) { float back = g.Aglow * g.Aglow * (3f - 2f * g.Aglow); s = Mathf.Lerp(s, 1f, back); if (back > 0.5f) glowLeft++; }
            if (!Held && lead == null && g.Dist > Anime.Of(Anime.Reach, 40f) * Mathf.Max(g.Radius, 0.5f)) s = 1f;
            g.Depth = Mathf.Min(s, 1f);
            g.Drawn = g.Depth < 0.995f;
        }

        void Draw(Group g, Camera cam, bool trueShadows, int frame, List<Hidden> hidden)
        {
            Vector3 a = g.Axis, pivot = g.Pivot;
            float s = g.Depth, k = 1f - s;
            float near = 0f, radius = 0f, below = 0f;
            Vector3 up = g.Up;
            int mask = cam.cullingMask;
            bool flames = g.Crew == null && (Anime.FlamesOn == null || Anime.FlamesOn) && TestParticles > 0;
            drawnGroups++;
            for (int n = 0; n < g.Pieces.Count; n++)
            {
                Piece p = g.Pieces[n];
                Renderer r = p.Renderer;
                if (r == null) { g.Stale = true; continue; }
                if ((object)p.Part != null && p.Part.vessel != g.Vessel) { g.Stale = true; continue; }
                if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                int layer = r.gameObject.layer;
                if ((mask & (1 << layer)) == 0) continue;
                if (p.Particles != null) { if (flames) Cloud(g, p, cam, layer, hidden); continue; }
                Mesh source = p.Skinned != null ? p.Skinned.sharedMesh : p.Filter.sharedMesh;
                if (source == null) continue;
                ShadowCastingMode was = r.shadowCastingMode;
                // (a mesh that is there only for its shadow: the true shape's shadow is what it casts already)
                if (trueShadows && was == ShadowCastingMode.ShadowsOnly) continue;

                // the mesh's place, and the line in the mesh's own frame
                Matrix4x4 m; Vector3 u, c;
                if (p.Skinned != null)
                {
                    Transform t = r.transform;
                    Quaternion q = t.rotation;
                    m = Matrix4x4.TRS(t.position, q, Vector3.one);
                    u = Quaternion.Inverse(q) * a; c = u;
                }
                else
                {
                    m = r.localToWorldMatrix;
                    u = r.worldToLocalMatrix.MultiplyVector(a);
                    c = new Vector3(m.m00 * a.x + m.m10 * a.y + m.m20 * a.z, m.m01 * a.x + m.m11 * a.y + m.m21 * a.z, m.m02 * a.x + m.m12 * a.y + m.m22 * a.z);
                }
                Vector3 o = new Vector3(m.m03, m.m13, m.m23);

                // its copy, reshaped if it is its turn
                Mesh drawn;
                if (p.Skinned != null) drawn = Pose(p, source, u, c, s, frame);
                else drawn = Shape(p, source, u, c, s);

                // the matrix: the copy's own shape undone, the mesh put in its place, the craft's flattening laid on
                float g0 = p.S < 0.999f ? (1f - p.S) / p.S : 0f;
                Vector3 mu = g0 > 0f ? m.MultiplyVector(p.U) * g0 : Vector3.zero;
                Vector3 c0 = p.C;
                float x00 = m.m00 + mu.x * c0.x, x01 = m.m01 + mu.x * c0.y, x02 = m.m02 + mu.x * c0.z;
                float x10 = m.m10 + mu.y * c0.x, x11 = m.m11 + mu.y * c0.y, x12 = m.m12 + mu.y * c0.z;
                float x20 = m.m20 + mu.z * c0.x, x21 = m.m21 + mu.z * c0.y, x22 = m.m22 + mu.z * c0.z;
                float q0 = a.x * x00 + a.y * x10 + a.z * x20, q1 = a.x * x01 + a.y * x11 + a.z * x21, q2 = a.x * x02 + a.y * x12 + a.z * x22;
                float along = Vector3.Dot(a, o - pivot);
                Matrix4x4 d = default;
                d.m00 = x00 - k * a.x * q0; d.m01 = x01 - k * a.x * q1; d.m02 = x02 - k * a.x * q2; d.m03 = o.x - k * a.x * along;
                d.m10 = x10 - k * a.y * q0; d.m11 = x11 - k * a.y * q1; d.m12 = x12 - k * a.y * q2; d.m13 = o.y - k * a.y * along;
                d.m20 = x20 - k * a.z * q0; d.m21 = x21 - k * a.z * q1; d.m22 = x22 - k * a.z * q2; d.m23 = o.z - k * a.z * along;
                d.m33 = 1f;

                r.GetSharedMaterials(materials);
                // (the craft's size, its near side and its foot are its solid things': a plume or a glow, drawn after them, is no
                // part of what stands on the ground or of what the camera may come near)
                if (materials.Count > 0 && materials[0] != null && materials[0].renderQueue <= 2500)
                {
                    float reach = along + p.Radius; if (reach > near) near = reach;
                    float far = (o - pivot).magnitude + p.Radius; if (far > radius) radius = far;
                    float low = Vector3.Dot(o - pivot, up); if (low < below) below = low;
                }
                MaterialPropertyBlock with = null;
                if (r.HasPropertyBlock()) { r.GetPropertyBlock(block); with = block; }
                ShadowCastingMode casts = trueShadows ? ShadowCastingMode.Off : was;
                bool takes = !trueShadows && r.receiveShadows;
                int subs = drawn.subMeshCount;
                for (int i = 0; i < materials.Count; i++)
                {
                    Material material = materials[i];
                    if (material == null || subs == 0) continue;
                    Graphics.DrawMesh(drawn, d, material, layer, cam, i < subs ? i : subs - 1, with, casts, takes, null, r.lightProbeUsage);
                }
                materials.Clear();

                // the mesh itself out of this camera's picture (its shadow left, where the true shape's is wanted)
                if (trueShadows && was != ShadowCastingMode.Off) { r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; hidden.Add(new Hidden { Renderer = r, Was = was }); }
                else { r.forceRenderingOff = true; hidden.Add(new Hidden { Renderer = r, Forced = true }); }
                drawnPieces++;
            }
            g.Near = near; g.Radius = radius; g.Below = below;
        }

        /// <summary>A cloud of particles (a flame, smoke, a puff) taken as a mesh, its points moved as the craft's are, and drawn in its place.</summary>
        void Cloud(Group g, Piece p, Camera cam, int layer, List<Hidden> hidden)
        {
            ParticleSystemRenderer r = p.Particles;
            // (particles that are whole meshes are left where they are: they cannot be hidden by their size, which is how the
            // cloud's own picture is kept out of this camera's below)
            if (r.renderMode == ParticleSystemRenderMode.None || r.renderMode == ParticleSystemRenderMode.Mesh || p.System.particleCount == 0) return;
            Material material = r.sharedMaterial;
            if (material == null) return;
            if (p.Copy == null) { p.Copy = new Mesh { name = "Anime Look: particles" }; p.Copy.MarkDynamic(); }
            // (taken as this camera sees them: a particle is a card turned to the camera. The mesh's points are in the world's
            // own places where the particles are let go into the world, about the cloud's own place where they stay with it)
            // (a cloud that stays with its engine is taken with the engine's turn and size in it: taken without, its cards are
            // turned to where the camera would be were the engine not turned, and a flame was a sheaf of slivers)
            bool world = p.System.main.simulationSpace == ParticleSystemSimulationSpace.World;
            r.BakeMesh(p.Copy, cam, !world);
            p.Copy.GetVertices(pose);
            int count = pose.Count;
            if (count == 0) return;
            Matrix4x4 m = world ? Matrix4x4.identity : Matrix4x4.Translate(r.transform.position);
            if (TestParticles == 4) { m = Matrix4x4.Translate(cam.transform.right * 5f) * m; }
            else if (TestParticles != 2)
            {
                Vector3 a = g.Axis, pivot = g.Pivot;
                float k = 1f - g.Depth, reach = Mathf.Max(g.Radius, 0.5f);
                Vector3 least = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), most = -least;
                for (int i = 0; i < count; i++)
                {
                    Vector3 w = world ? pose[i] : m.MultiplyPoint3x4(pose[i]);
                    float depth = a.x * (w.x - pivot.x) + a.y * (w.y - pivot.y) + a.z * (w.z - pivot.z);
                    float by = k * (depth > reach ? reach : depth < -reach ? -reach : depth);
                    w.x -= a.x * by; w.y -= a.y * by; w.z -= a.z * by;
                    pose[i] = w;
                    if (w.x < least.x) least.x = w.x; if (w.y < least.y) least.y = w.y; if (w.z < least.z) least.z = w.z;
                    if (w.x > most.x) most.x = w.x; if (w.y > most.y) most.y = w.y; if (w.z > most.z) most.z = w.z;
                }
                p.Copy.SetVertices(pose);
                // (said outright: left to Unity, the mesh kept the bounds it was taken with, in the cloud's own frame, and a flame
                // moved into the world's was thought out of the picture and not drawn at all)
                p.Copy.bounds = new Bounds((least + most) * 0.5f, most - least);
                m = Matrix4x4.identity;
            }
            clouds++; cloudPoints += count;
#if DEV
            if (CloudsAsked > 0)
            {
                CloudsAsked--;
                CloudsSaid += " || " + r.name + " (under " + (r.transform.parent != null ? r.transform.parent.name : "nothing") + "): " + p.System.particleCount + " particles, " + r.renderMode + ", space " + p.System.main.simulationSpace
                    + ", " + count + " points, " + p.Copy.GetIndexCount(0) + " indices, " + p.Copy.subMeshCount + " submeshes, bounds " + p.Copy.bounds.center.ToString("0.0") + " +- " + p.Copy.bounds.extents.ToString("0.0")
                    + ", at " + r.transform.position.ToString("0.0") + ", layer " + layer + ", material " + material.name + " / " + material.shader.name + ", queue " + material.renderQueue + ", scale " + r.transform.lossyScale.ToString("0.00");
            }
#endif
            MaterialPropertyBlock with = null;
            if (r.HasPropertyBlock()) { r.GetPropertyBlock(block); with = block; }
            Graphics.DrawMesh(p.Copy, m, material, layer, cam, 0, with, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            if (TestParticles == 2 || TestParticles == 4) return;
            // (the cloud's own picture out of this camera's: its particles drawn at no size, not switched off. Switched off, the
            // game takes the cloud for unseen and leaves off tending it: a flame came out short and dim.)
            hidden.Add(new Hidden { Renderer = r, Cloud = r, Size = r.maxParticleSize });
            r.maxParticleSize = 0f;
        }

        /// <summary>A mesh's copy, its vertices flattened about the mesh's own origin: made, and reshaped when it is out of shape and there is work left this frame.</summary>
        Mesh Shape(Piece p, Mesh source, Vector3 u, Vector3 c, float s)
        {
            if (p.Source != source)
            {
                if (p.Copy != null) Destroy(p.Copy);
                p.Copy = null; p.Source = source; p.S = 1f; p.From = null;
                // (how far it reaches: from its points where they can be read. A mesh's own bounds can be a statement, not a
                // measure: Waterfall gives a plume bounds of 1e18 m so that it is never left out of a picture, and taken for
                // the craft's size that switched the whole squash off, since nothing so near so large a thing may be shrunk)
                Bounds b = source.bounds;
                float said = b.center.magnitude + b.extents.magnitude;
                p.Boundless = !(said < 2000f);
                p.Radius = (p.Boundless ? 1f : said) * MaxScale(p.Renderer.transform);
                int id = source.GetInstanceID();
                if (!sources.TryGetValue(id, out p.From) || (p.From != null && p.From.Length != source.vertexCount))
                {
                    p.From = source.isReadable && source.vertexCount > 0 ? source.vertices : null;
                    sources[id] = p.From;
                }
                if (p.From != null)
                {
                    float far = 0f;
                    Vector3[] points = p.From;
                    for (int i = 0; i < points.Length; i++) { float d = points[i].sqrMagnitude; if (d > far) far = d; }
                    p.Radius = Mathf.Sqrt(far) * MaxScale(p.Renderer.transform);
                    p.Copy = Instantiate(source);
                    p.Copy.name = "Anime Look: " + source.name;
                    p.Copy.MarkDynamic();
                }
            }
            if (p.Copy == null || TestNormals == 1) { unread++; p.S = 1f; return source; }
            // how far out of shape: the line's turn since, and the depth's change, as they would tilt a normal
            bool reshape;
            if (p.S >= 0.999f) reshape = true;
            else
            {
                float cos = Vector3.Dot(c, p.C) / Mathf.Sqrt(c.sqrMagnitude * p.C.sqrMagnitude + 1e-30f);
                float turned = Mathf.Sqrt(Mathf.Max(0f, 2f - 2f * cos));
                reshape = turned * (1f - s) / s + Mathf.Abs(s - p.S) / Mathf.Min(s, p.S) > 0.03f;
            }
            if (!reshape) return p.Copy;
            Vector3[] from = p.From;
            int count = from.Length;
            if (budget <= 0) { waiting++; return p.Copy; }
            budget -= count; reshaped += count;
            if (scratch.Length < count) scratch = new Vector3[Mathf.NextPowerOfTwo(count)];
            Vector3[] to = scratch;
            float k = 1f - s, ux = u.x * k, uy = u.y * k, uz = u.z * k, cx = c.x, cy = c.y, cz = c.z;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = from[i];
                float along = cx * v.x + cy * v.y + cz * v.z;
                v.x -= ux * along; v.y -= uy * along; v.z -= uz * along;
                to[i] = v;
            }
            p.Copy.SetVertices(to, 0, count);          // (Unity works the copy's bounds out again from them)
            if (p.Boundless) p.Copy.bounds = source.bounds;
            p.U = u; p.C = c; p.S = s;
            return p.Copy;
        }

        /// <summary>A kerbal's mesh as it is posed now, flattened: taken once a frame.</summary>
        Mesh Pose(Piece p, Mesh source, Vector3 u, Vector3 c, float s, int frame)
        {
            if (p.Frame == frame && p.Copy != null) return p.Copy;
            if (p.Copy == null)
            {
                p.Copy = new Mesh { name = "Anime Look: " + source.name };
                p.Copy.MarkDynamic();
            }
            p.Source = source;
            p.Skinned.BakeMesh(p.Copy);
            p.Copy.GetVertices(pose);
            int count = pose.Count;
            budget -= count; reshaped += count;
            float k = 1f - s, ux = u.x * k, uy = u.y * k, uz = u.z * k, cx = c.x, cy = c.y, cz = c.z;
            float reach = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = pose[i];
                float far = v.sqrMagnitude; if (far > reach) reach = far;
                float along = cx * v.x + cy * v.y + cz * v.z;
                v.x -= ux * along; v.y -= uy * along; v.z -= uz * along;
                pose[i] = v;
            }
            p.Copy.SetVertices(pose);
            p.Radius = Mathf.Sqrt(reach);
            p.U = u; p.C = c; p.S = s; p.Frame = frame;
            return p.Copy;
        }

        static float MaxScale(Transform t)
        {
            Vector3 scale = t.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        void After(Camera cam)
        {
            for (int i = 0; i < glowing.Count; i++)
                if ((object)glowing[i] == (object)cam) { cam.ResetWorldToCameraMatrix(); glowing.RemoveAt(i); return; }
            if (hiddenCrew.Count > 0 && crewBy.ContainsKey(cam)) { ShowAgain(hiddenCrew); return; }
            if (hidden.Count == 0 || !Flight(cam)) return;
            ShowAgain(hidden);
        }

        /// <summary>The craft's own meshes back in the picture, for every other camera and for the game.</summary>
        static void ShowAgain(List<Hidden> hidden)
        {
            for (int i = 0; i < hidden.Count; i++)
            {
                Hidden h = hidden[i];
                if (h.Renderer == null) continue;
                if ((object)h.Cloud != null) h.Cloud.maxParticleSize = h.Size;
                else if (h.Forced) h.Renderer.forceRenderingOff = false; else h.Renderer.shadowCastingMode = h.Was;
            }
            hidden.Clear();
        }
    }
}
