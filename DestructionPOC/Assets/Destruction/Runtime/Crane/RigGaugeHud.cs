using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Small dial in the bottom-right corner of the player's view while operating a machine that needs one:
    ///   Bucket machines (skid steer, wheel loader): side view of the bucket against the horizon, with the pour angle
    ///     marked, so the operator can see how far the bucket is curled or tipped without looking at the load.
    ///   Machines whose upper body turns on the tracks (wrecking crane, excavator): top view with the cab fixed
    ///     pointing up and the tracks turned to where they actually point, so the driver knows which way "forward" goes.
    /// Hidden on foot and in machines with neither. Drawn inside this player's own viewport.
    /// </summary>
    [RequireComponent(typeof(CranePlayer))]
    public sealed class RigGaugeHud : MonoBehaviour
    {
        public float margin = 14f;
        [Range(10, 24)] public int fontSize = 13;

        const float Size = 176f;

        CranePlayer player;
        GUIStyle title, caption;
        Texture2D bg, white;

        /// <summary>What the gauge shows right now, for tests: "bucket", "tracks" or null.</summary>
        public string Mode { get; private set; }
        /// <summary>Bucket opening pitch below horizontal (+ tipped forward), degrees. Valid while Mode is "bucket".</summary>
        public float BucketPitch { get; private set; }
        /// <summary>Degrees the tracks point right of the cab. Valid while Mode is "tracks".</summary>
        public float TracksYaw { get; private set; }

        void Awake() => player = GetComponent<CranePlayer>();

        void Update()
        {
            Mode = null;
            switch (player != null ? player.Current : null)
            {
                case LoaderRig loader:
                    Mode = "bucket";
                    BucketPitch = loader.Load.PitchDown;
                    break;
                case CraneOperable crane:
                    Mode = "tracks";
                    TracksYaw = Wrap(-crane.UpperYaw);
                    break;
                case ExcavatorRig excavator:
                    Mode = "tracks";
                    TracksYaw = Wrap(-excavator.SwingAngle);
                    break;
            }
        }

        static float Wrap(float deg) => Mathf.DeltaAngle(0f, deg);

        void OnGUI()
        {
            if (Mode == null) return;
            EnsureStyles();
            var view = player.GuiRect;
            GUI.BeginGroup(view);
            var r = new Rect(view.width - Size - margin, view.height - Size - margin, Size, Size);
            GUI.DrawTexture(r, bg);
            if (Mode == "bucket") DrawBucket(r);
            else DrawTracks(r);
            GUI.EndGroup();
        }

        void DrawBucket(Rect r)
        {
            var loader = (LoaderRig)player.Current;
            float dump = loader.Load.DumpAngle;
            bool pouring = loader.Load.Spilling || BucketPitch > dump;
            bool near = BucketPitch > dump - 5f;

            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 22f), "BUCKET TILT", title);
            Vector2 p = new Vector2(r.x + r.width * 0.42f, r.y + 84f);

            Fill(new Rect(r.x + 10f, p.y - 0.5f, r.width - 20f, 1f), new Color(1f, 1f, 1f, 0.35f));   // horizon
            Rotated(p, dump, () => Fill(new Rect(p.x, p.y - 1f, 62f, 2f), new Color(1f, 0.3f, 0.2f, 0.7f))); // pour angle

            Color c = pouring ? new Color(1f, 0.3f, 0.2f) : near ? new Color(1f, 0.6f, 0.2f) : new Color(0.98f, 0.8f, 0.2f);
            Rotated(p, BucketPitch, () =>
            {
                Fill(new Rect(p.x, p.y - 3f, 56f, 6f), c);            // floor, the opening faces up and forward at 0
                Fill(new Rect(p.x - 3f, p.y - 32f, 6f, 35f), c);      // back wall
                Fill(new Rect(p.x + 50f, p.y - 15f, 5f, 18f), c);     // lip
            });
            Fill(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), Color.white);  // pivot

            string state = pouring ? "POURING" : BucketPitch > 4f ? "tipped forward" : BucketPitch < -4f ? "curled back" : "level";
            GUI.Label(new Rect(r.x + 10f, r.y + r.height - 50f, r.width - 20f, 22f), $"{BucketPitch:+0;-0;0}°   {state}", title);
            GUI.Label(new Rect(r.x + 10f, r.y + r.height - 28f, r.width - 20f, 20f), $"pours past +{dump:0}°", caption);
        }

        void DrawTracks(Rect r)
        {
            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 22f), "TRACKS VS CAB", title);
            Vector2 c = new Vector2(r.x + r.width * 0.5f, r.y + 82f);

            // Cab (upper body): fixed, forward is up.
            Fill(new Rect(c.x - 22f, c.y - 26f, 44f, 52f), new Color(0.98f, 0.8f, 0.2f, 0.9f));
            Fill(new Rect(c.x - 12f, c.y - 34f, 24f, 12f), new Color(0.98f, 0.8f, 0.2f, 0.9f)); // nose, so up is obviously forward
            Fill(new Rect(c.x - 12f, c.y - 18f, 24f, 14f), new Color(0.2f, 0.28f, 0.34f, 0.9f)); // cab glass

            // Tracks: two bars and a drive arrow, turned to where they really point.
            Color t = new Color(0.82f, 0.86f, 0.9f, 0.95f);
            Rotated(c, TracksYaw, () =>
            {
                Fill(new Rect(c.x - 40f, c.y - 36f, 12f, 72f), t);
                Fill(new Rect(c.x + 28f, c.y - 36f, 12f, 72f), t);
                Fill(new Rect(c.x - 2f, c.y - 58f, 4f, 22f), new Color(0.4f, 0.9f, 0.5f));          // drive-forward arrow
                Fill(new Rect(c.x - 10f, c.y - 62f, 20f, 4f), new Color(0.4f, 0.9f, 0.5f));
            });

            float a = Mathf.Abs(TracksYaw);
            string state = a < 8f ? "aligned" : a > 172f ? "reversed" : $"{a:0}° {(TracksYaw > 0f ? "right" : "left")}";
            GUI.Label(new Rect(r.x + 10f, r.y + r.height - 50f, r.width - 20f, 22f), state, title);
            GUI.Label(new Rect(r.x + 10f, r.y + r.height - 28f, r.width - 20f, 20f), "green arrow: drive forward", caption);
        }

        void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = prev;
        }

        /// <summary>Run a drawing callback with the GUI rotated clockwise by the angle about a pivot.</summary>
        static void Rotated(Vector2 pivot, float degrees, System.Action draw)
        {
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, pivot);
            draw();
            GUI.matrix = saved;
        }

        void EnsureStyles()
        {
            if (title != null && title.fontSize == fontSize) return;
            bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            bg.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.62f));
            bg.Apply();
            white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            title = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            title.normal.textColor = new Color(1f, 0.82f, 0.3f);
            caption = new GUIStyle(GUI.skin.label) { fontSize = fontSize - 2, fontStyle = FontStyle.Italic, clipping = TextClipping.Clip };
            caption.normal.textColor = new Color(0.86f, 0.88f, 0.9f, 0.8f);
        }

        void OnDestroy()
        {
            if (bg != null) Destroy(bg);
            if (white != null) Destroy(white);
        }
    }
}
