using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Octopath-style bottom party strip: one compact block per party member
// (name, HP, MP). Fully built from CODE — no prefab, no Inspector wiring.
public class BattlePartyHUD : MonoBehaviour
{
    public static BattlePartyHUD Instance { get; private set; }

    private class Entry
    {
        public BattleUnit unit;
        public Image bg;
        public TextMeshProUGUI nameText, hpText, mpText;
        public string lastHp, lastMp;
        public bool lastAlive;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private BattleUnit activeUnit;

    public static BattlePartyHUD EnsureBuilt(Canvas canvas)
    {
        if (Instance != null) return Instance;
        if (canvas == null) { Debug.LogError("BattlePartyHUD: no canvas found."); return null; }

        GameObject go = new GameObject("PartyHUD");
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<RectTransform>(); // convert the plain Transform into a RectTransform
        BattlePartyHUD hud = go.AddComponent<BattlePartyHUD>();
        hud.Build();
        return hud;
    }

    private void Build()
    {
        Instance = this;

        RectTransform rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 100f);

        Image bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);
        bg.raycastTarget = false;

        HorizontalLayoutGroup layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 8, 8);
        layout.spacing = 12;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
    }

    public void Bind(List<BattleUnit> players)
    {
        foreach (Entry e in entries)
            if (e.unit != null) Destroy(e.bg.gameObject);
        entries.Clear();

        foreach (BattleUnit unit in players)
        {
            GameObject go = new GameObject(unit.Stats.unitName + "_HUD");
            go.transform.SetParent(transform, false);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = false;

            TextMeshProUGUI name = CreateText(go.transform, 22, FontStyles.Bold,
                new Vector2(0.06f, 1f), new Vector2(0.94f, 1f), 30);
            TextMeshProUGUI hp = CreateText(go.transform, 20, FontStyles.Normal,
                new Vector2(0.06f, 0.55f), new Vector2(0.94f, 0.85f), 28);
            TextMeshProUGUI mp = CreateText(go.transform, 18, FontStyles.Normal,
                new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.5f), 26, new Color(0.55f, 0.75f, 1f));

            if (name != null) name.text = unit.Stats.unitName;

            entries.Add(new Entry { unit = unit, bg = bg, nameText = name, hpText = hp, mpText = mp });
        }
        Refresh();
    }

    public void SetActive(BattleUnit unit) { activeUnit = unit; Refresh(); }

    private void Update() { Refresh(); }

    private void Refresh()
    {
        foreach (Entry e in entries)
        {
            if (e.unit == null || e.unit.Stats == null) continue;
            UnitStats s = e.unit.Stats;

            string hpStr = "HP " + s.currentHP + "/" + s.MaxHP;
            string mpStr = "MP " + s.currentMP + "/" + s.MaxMP;

            if (e.hpText != null && e.hpText.text != hpStr)
            {
                e.hpText.text = hpStr;
                e.hpText.color = (s.MaxHP > 0 && (float)s.currentHP / s.MaxHP <= 0.25f)
                    ? new Color(1f, 0.45f, 0.45f) : Color.white;
            }
            if (e.mpText != null && e.mpText.text != mpStr) e.mpText.text = mpStr;

            bool alive = s.IsAlive;
            if (e.nameText != null && e.lastAlive != alive)
                e.nameText.color = alive ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            e.lastAlive = alive;

            if (e.bg != null)
            {
                Color target = (!alive) ? new Color(0.2f, 0.2f, 0.2f, 0.35f)
                    : (e.unit == activeUnit) ? new Color(1f, 0.85f, 0.3f, 0.30f)
                    : new Color(1f, 1f, 1f, 0.08f);
                e.bg.color = target;
            }
        }
    }

    private static TextMeshProUGUI CreateText(Transform parent, int size, FontStyles style,
        Vector2 anchorMin, Vector2 anchorMax, float height, Color? color = null)
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogWarning("BattlePartyHUD: TMP default font missing — did you import TMP Essential Resources?");
            return null;
        }
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Left;
        t.color = color ?? Color.white;
        t.raycastTarget = false;
        return t;
    }
}