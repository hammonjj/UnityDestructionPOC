using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Two small bars in the top-right corner of CraneTest: cleanup progress (mass accepted by the container against
    /// the staged loader debris) always, and the occupied loader's bucket load against its capacity while operating.
    /// Kept clear of the working area and of the controls card (bottom-left).
    /// </summary>
    [RequireComponent(typeof(CranePlayer))]
    public sealed class LoaderHud : MonoBehaviour
    {
        public CleanupLedger ledger;
        public float margin = 14f;
        [Range(10, 24)] public int fontSize = 13;

        CranePlayer player;
        GUIStyle label, small;
        Texture2D bg, fill;

        /// <summary>Text last drawn, for tests.</summary>
        public string CleanupText { get; private set; }
        public string BucketText { get; private set; }

        void Awake() => player = GetComponent<CranePlayer>();

        public static string Tonnes(float kg) => kg >= 1000f ? $"{kg / 1000f:0.0} t" : $"{kg:0} kg";

        void Update()
        {
            CleanupText = ledger == null ? null : $"CLEANUP  {Tonnes(ledger.ClearedMassKg)} / {Tonnes(ledger.StagedMassKg)}  ({Mathf.RoundToInt(ledger.Progress * 100f)}%)";
            var loader = player != null ? player.Current as LoaderRig : null;
            BucketText = loader == null ? null
                : $"BUCKET  {loader.Load.MassKg:N0} / {loader.Load.CapacityKg:N0} kg" + (loader.Load.Spilling ? "  pouring" : loader.Load.Fill >= 0.98f ? "  full" : "");
        }

        void OnGUI()
        {
            if (ledger == null) return;
            EnsureStyles();
            const float w = 280f, bar = 10f;
            float line = fontSize + 6f;
            bool loader = BucketText != null;
            float h = 12f + line + bar + (ledger.OversizedPieces > 0 ? line : 0f) + (loader ? line + bar + 10f : 0f);
            var r = new Rect(Screen.width - w - margin, margin, w, h);
            GUI.DrawTexture(r, bg);
            float y = r.y + 6f;
            float x = r.x + 10f, iw = w - 20f;

            GUI.Label(new Rect(x, y, iw, line), CleanupText, label);
            y += line;
            Bar(new Rect(x, y, iw, bar), ledger.Progress, new Color(0.35f, 0.8f, 0.45f));
            y += bar + 2f;
            if (ledger.OversizedPieces > 0)
            {
                GUI.Label(new Rect(x, y, iw, line), $"{ledger.OversizedPieces} oversized chunks need breaking first", small);
                y += line;
            }
            if (loader)
            {
                var l = (LoaderRig)player.Current;
                y += 8f;
                GUI.Label(new Rect(x, y, iw, line), BucketText, label);
                y += line;
                Bar(new Rect(x, y, iw, bar), l.Load.Fill, l.Load.Fill >= 0.98f ? new Color(0.95f, 0.45f, 0.25f) : new Color(0.95f, 0.75f, 0.2f));
            }
        }

        void Bar(Rect r, float t, Color c)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(r, fill);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), fill);
            GUI.color = prev;
        }

        void EnsureStyles()
        {
            if (label != null && label.fontSize == fontSize) return;
            bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            bg.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.62f));
            bg.Apply();
            fill = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            fill.SetPixel(0, 0, Color.white);
            fill.Apply();
            label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            label.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = fontSize - 2, fontStyle = FontStyle.Italic, clipping = TextClipping.Clip };
            small.normal.textColor = new Color(0.86f, 0.88f, 0.9f, 0.8f);
        }

        void OnDestroy()
        {
            if (bg != null) Destroy(bg);
            if (fill != null) Destroy(fill);
        }
    }
}
