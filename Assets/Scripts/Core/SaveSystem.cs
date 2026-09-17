using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// ---------------------------------------------------------------------------
// The "slight curve": fans a vertical list of option buttons into a gentle
// arc. Labels and non-buttons are left untouched.
// ---------------------------------------------------------------------------
public static class MenuCurve
{
    public static void Apply(Transform list, float maxTilt = 2f)
    {
        if (list == null) return;
        int n = list.childCount;
        if (n <= 1) return;
        for (int i = 0; i < n; i++)
        {
            RectTransform rt = list.GetChild(i) as RectTransform;
            if (rt == null || rt.GetComponent<Button>() == null) continue;
            float k = (i / (float)(n - 1)) * 2f - 1f;   // -1 top … +1 bottom
            rt.localRotation = Quaternion.Euler(0f, 0f, k * maxTilt);
        }
    }
}

// ---------------------------------------------------------------------------
// Save data (plain serializable classes — names are resolved to assets via
// the GameManager registries on load).
// ---------------------------------------------------------------------------
[System.Serializable]
public class SaveData
{
    public string savedAt;
    public string sceneName;
    public Vector3 position;
    public int gold;
    public int memberCount;
    public List<SavedMember> members = new List<SavedMember>();
    public List<SavedItem> items = new List<SavedItem>();
    public List<SavedEquip> equipment = new List<SavedEquip>();
}
[System.Serializable]
public class SavedMember
{
    public string character;
    public int level, exp, hp, mp;
    public bool active;
    public string armor, accessory1, accessory2;
}
[System.Serializable]
public class SavedItem { public string item; public int count; }
[System.Serializable]
public class SavedEquip { public string equip; public int count; }

// ---------------------------------------------------------------------------
// Save / load / delete — 99 slots, JSON files in persistentDataPath.
// ---------------------------------------------------------------------------
public static class SaveSystem
{
    public const int SlotCount = 99;

    public static bool SlotExists(int slot) { return File.Exists(PathFor(slot)); }

    public static bool AnySaveExists()
    {
        for (int i = 1; i <= SlotCount; i++) if (SlotExists(i)) return true;
        return false;
    }

    private static string PathFor(int slot)
    {
        return Path.Combine(Application.persistentDataPath, "save_" + slot + ".json");
    }

    public static SaveData Peek(int slot)
    {
        if (!SlotExists(slot)) return null;
        try { return JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFor(slot))); }
        catch { return null; }
    }

    public static void Save(int slot)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) { Debug.LogError("SaveSystem: no GameManager."); return; }

        var d = new SaveData();
        d.savedAt = System.DateTime.Now.ToString("yyyy/MM/dd  HH:mm");
        d.sceneName = SceneManager.GetActiveScene().name;
        d.position = PlayerParty.Instance != null ? PlayerParty.Instance.GetLeaderPosition() : Vector3.zero;
        d.gold = gm.gold;

        foreach (PartyMember pm in gm.party)
        {
            if (pm == null || pm.data == null) continue;
            d.memberCount++;
            d.members.Add(new SavedMember
            {
                character = pm.data.characterName,
                level = pm.level, exp = pm.exp, hp = pm.currentHP, mp = pm.currentMP,
                active = pm.isActive,
                armor = Name(pm.armor), accessory1 = Name(pm.accessory1), accessory2 = Name(pm.accessory2)
            });
        }
        foreach (ItemStack s in gm.items)
            if (s != null && s.item != null && s.count > 0)
                d.items.Add(new SavedItem { item = s.item.itemName, count = s.count });
        foreach (EquipmentStack s in gm.equipmentInventory)
            if (s != null && s.equipment != null && s.count > 0)
                d.equipment.Add(new SavedEquip { equip = s.equipment.equipName, count = s.count });

        File.WriteAllText(PathFor(slot), JsonUtility.ToJson(d, true));
        Debug.Log("Saved to slot " + slot + " (" + PathFor(slot) + ")");
    }

    private static string Name(EquipmentData eq) { return eq != null ? eq.equipName : ""; }

    public static void Delete(int slot)
    {
        if (SlotExists(slot)) File.Delete(PathFor(slot));
    }

    /// Restores the whole session from a slot and loads the saved scene,
    /// spawning the party at the saved position.
    public static void LoadIntoGame(int slot)
    {
        SaveData d = Peek(slot);
        if (d == null) { Debug.LogError("SaveSystem: slot " + slot + " is empty."); return; }
        GameManager gm = GameManager.Instance;
        if (gm == null) { Debug.LogError("SaveSystem: no GameManager."); return; }

        gm.party.Clear();
        foreach (SavedMember m in d.members)
        {
            CharacterData cd = gm.FindCharacter(m.character);
            if (cd == null) { Debug.LogWarning("Save: unknown character '" + m.character + "' — skipped."); continue; }
            PartyMember pm = new PartyMember(cd);
            pm.level = Mathf.Max(1, m.level);
            pm.exp = m.exp;
            pm.isActive = m.active;
            pm.armor = gm.FindEquipment(m.armor);
            pm.accessory1 = gm.FindEquipment(m.accessory1);
            pm.accessory2 = gm.FindEquipment(m.accessory2);
            UnitStats s = pm.CreateStats();
            pm.currentHP = Mathf.Clamp(m.hp, 0, s.MaxHP);
            pm.currentMP = Mathf.Clamp(m.mp, 0, s.MaxMP);
            gm.party.Add(pm);
        }

        gm.items = new List<ItemStack>();
        foreach (SavedItem si in d.items)
        {
            ItemData id = gm.FindItem(si.item);
            if (id == null) { Debug.LogWarning("Save: unknown item '" + si.item + "' — skipped."); continue; }
            gm.items.Add(new ItemStack { item = id, count = si.count });
        }
        gm.equipmentInventory = new List<EquipmentStack>();
        foreach (SavedEquip se in d.equipment)
        {
            EquipmentData ed = gm.FindEquipment(se.equip);
            if (ed == null) continue;
            gm.equipmentInventory.Add(new EquipmentStack { equipment = ed, count = se.count });
        }

        gm.gold = d.gold;
        gm.inBattle = false;
        gm.hasReturnPosition = true;
        gm.returnPosition = d.position;
        SceneLoader.Load(d.sceneName);
    }
}

// ---------------------------------------------------------------------------
// Scrollable 99-slot list, shared by the Save tab and the main-menu Load
// screen. Rows show slot number, member count and save time (or "Empty"),
// with a delete button per occupied slot.
// ---------------------------------------------------------------------------
public static class SaveSlotUI
{
    public static void Build(Transform parent, System.Action<int> onPick,
                             bool showDelete, bool pickEmpty, System.Action onChanged)
    {
        GameObject scrollGo = new GameObject("SlotScroll");
        scrollGo.transform.SetParent(parent, false);
        Image bg = scrollGo.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.35f);
        ScrollRect sr = scrollGo.AddComponent<ScrollRect>();
        scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1;

        GameObject vpGo = new GameObject("Viewport");
        vpGo.transform.SetParent(scrollGo.transform, false);
        RectTransform vp = vpGo.AddComponent<RectTransform>();
        Stretch(vp);
        vpGo.AddComponent<RectMask2D>();
        sr.viewport = vp;

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(vpGo.transform, false);
        RectTransform content = contentGo.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        VerticalLayoutGroup vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.scrollSensitivity = 40;

        for (int slot = 1; slot <= SaveSystem.SlotCount; slot++)
        {
            int captured = slot;
            SaveData d = SaveSystem.Peek(captured);

            GameObject row = new GameObject("Slot" + captured);
            row.transform.SetParent(content, false);
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;

            string label = "Slot " + captured.ToString("00")
                + (d != null ? "   —   " + d.memberCount + (d.memberCount == 1 ? " member" : " members")
                              + "   —   " + d.savedAt
                            : "   —   Empty");
            Button main = RowButton(row.transform, label, 0,
                (d != null || pickEmpty) ? () => onPick(captured) : null);
            main.interactable = (d != null) || pickEmpty;

            if (showDelete && d != null)
            {
                Button del = RowButton(row.transform, "✕", 54, () =>
                {
                    SaveSystem.Delete(captured);
                    if (onChanged != null) onChanged();
                });
                del.GetComponent<Image>().color = new Color(0.35f, 0.12f, 0.15f, 0.95f);
            }
        }
    }

    private static Button RowButton(Transform parent, string label, float width,
                                    UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.16f, 0.19f, 0.28f, 0.95f);
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        if (onClick != null) b.onClick.AddListener(onClick);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 42;
        if (width > 0) le.preferredWidth = width;
        if (TMP_Settings.defaultFontAsset != null)
        {
            GameObject txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            TextMeshProUGUI t = txtGo.AddComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset;
            t.text = label;
            t.fontSize = 18;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            Stretch((RectTransform)txtGo.transform);
        }
        return b;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}