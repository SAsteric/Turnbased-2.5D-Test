using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class OverworldMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static OverworldMenu Instance { get; private set; }
    public static string LocationOverride = "";

    public static void EnsureBuilt()
    {
        // healthy menu already alive? keep it
        if (Instance != null && Instance.enabled
            && Instance.gameObject.activeInHierarchy
            && Instance.root != null)
            return;

        // zombie instance (disabled / inactive / broken canvas) squatting on
        // Instance? destroy it and build a fresh one.
        if (Instance != null)
        {
            Destroy(Instance.gameObject);
            Instance = null;
        }

        GameObject go = new GameObject("OverworldMenu");
        go.AddComponent<OverworldMenu>();
    }

    private enum Tab { Inventory, Equipment, Party, Save, Load, Settings }

    private RectTransform root;
    private TextMeshProUGUI descriptionText;
    private TextMeshProUGUI goldText, locationText;
    private Transform partyRow;
    private Transform contentList;

    private readonly Dictionary<Tab, Image> tabImages = new Dictionary<Tab, Image>();
    private Tab currentTab = Tab.Inventory;
    private PartyMember selectedMember;
    private bool partyDirty;

    private ItemCategory currentCategory = ItemCategory.Consumable;
    private UnityEngine.Object inventoryDetail;
    private EquipSlot selectedSlot = EquipSlot.Armor;
    private int selectedAcc = 1;

    private Button menuToggleButton;

    private static readonly Color[] CardAccents =
    {
        new Color(1f, 0.45f, 0.65f),
        new Color(0.65f, 0.45f, 1f),
        new Color(0.45f, 0.85f, 1f),
        new Color(1f, 0.75f, 0.35f),
    };

    private void Awake()
    {
        Instance = this;
        BuildCanvas();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (IsOpen) { IsOpen = false; Time.timeScale = 1f; }
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        bool pressed = (kb != null && (kb.iKey.wasPressedThisFrame || kb.mKey.wasPressedThisFrame
                 || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                    || (pad != null && pad.startButton.wasPressedThisFrame);
        if (pressed) { if (!IsOpen) Open(); else Close(); }

        if (IsOpen) UpdateFooter();
    }

    private void Open()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.inBattle)
        {
            // self-heal a stale battle flag: we're in the overworld, no battle
            // is running, no transition is playing — the flag is a leftover
            bool stale = BattleManager.Instance == null
                      && !MirrorBreakTransition.IsActive
                      && SceneManager.GetActiveScene().name != gm.battleSceneName;
            if (!stale) return;
            gm.inBattle = false;
        }
        if (DevConsole.IsOpen) return;
        if (RecruitableNPC.DialogueOpen) return;

        IsOpen = true;
        Time.timeScale = 0f;
        root.gameObject.SetActive(true);
        SelectTab(Tab.Inventory);
    }

    public void Close()
    {
        IsOpen = false;
        Time.timeScale = 1f;
        root.gameObject.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.SaveSettings();
        if (partyDirty)
        {
            partyDirty = false;
            if (PlayerParty.Instance != null) PlayerParty.Instance.RebuildFromParty();
        }
    }

    // ---------------- corner toggle button ----------------

    private void BuildToggleCornerButton()
    {
        GameObject go = new GameObject("MenuToggle");
        go.transform.SetParent(transform, false);   // NOT under Root — stays visible while closed
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(8f, -50f);
        rt.sizeDelta = new Vector2(160f, 40f);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.65f);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(TryToggleFromButton);
        menuToggleButton = btn;
        TextMeshProUGUI t = MakeLabel(go.transform, "MENU", 17, FontStyles.Bold, 40, Color.white);
        if (t != null)
        {
            t.alignment = TextAlignmentOptions.Center;
            StretchFill(t.rectTransform);
        }
    }

    private void TryToggleFromButton()
    {
        if (IsOpen) { Close(); return; }

        GameManager gm = GameManager.Instance;
        string reason = null;
        if (gm == null) reason = "no GameManager";
        else if (gm.inBattle) reason = "inBattle stuck";
        else if (DevConsole.IsOpen) reason = "dev console open";
        else if (RecruitableNPC.DialogueOpen) reason = "dialogue open";

        if (reason != null)
        {
            if (menuToggleButton != null)
            {
                TextMeshProUGUI t = menuToggleButton.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null) t.text = "BLOCKED: " + reason;
            }
            return;
        }
        Open();
    }

    private void SelectTab(Tab tab)
    {
        currentTab = tab;
        inventoryDetail = null;
        if (descriptionText != null) descriptionText.text = TabDescription(tab);
        foreach (KeyValuePair<Tab, Image> kv in tabImages)
            kv.Value.color = (kv.Key == tab)
                ? new Color(0.30f, 0.35f, 0.52f, 0.95f)
                : new Color(0.16f, 0.19f, 0.28f, 0.85f);
        if (selectedMember == null || !GameManager.Instance.party.Contains(selectedMember))
            selectedMember = GameManager.Instance.party.Count > 0 ? GameManager.Instance.party[0] : null;
        RefreshAll();
    }

    private static string TabDescription(Tab tab)
    {
        switch (tab)
        {
            case Tab.Inventory:  return "Stores all your items and other belongings.";
            case Tab.Equipment:  return "Equip gear — hover anything to preview the stat change first.";
            case Tab.Party:      return "Arrange your party — up to four members enter battle.";
            case Tab.Save:       return "Record your journey in one of 99 save slots.";
            case Tab.Load:       return "Restore a recorded journey — the current session is replaced.";
            case Tab.Settings:   return "Settings — coming soon.";
            default: return "";
        }
    }

    private void RefreshAll()
    {
        if (!IsOpen) return;
        BuildPartyColumn();
        BuildContentPane();
        UpdateFooter();
    }

    // ---------------- construction ----------------

    private void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        gameObject.AddComponent<GraphicRaycaster>();

        root = CreatePanel("Root", transform, new Color(0.05f, 0.06f, 0.12f, 0.97f));
        StretchFill(root);
        root.gameObject.SetActive(false);

        RectTransform top = CreatePanel("TopBar", root, new Color(0.08f, 0.09f, 0.16f, 0.95f));
        SetAnchors(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), Vector2.zero);
        descriptionText = AnchoredText(top, "Description", "Choose a command.", 21, FontStyles.Normal,
            new Color(0.85f, 0.88f, 0.94f),
            Vector2.zero, Vector2.one, new Vector2(28, 4), new Vector2(-28, -4), TextAlignmentOptions.Left);

        RectTransform footer = CreatePanel("Footer", root, new Color(0.08f, 0.09f, 0.16f, 0.95f));
        SetAnchors(footer, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 64));
        goldText = AnchoredText(footer, "Gold", "0 G", 22, FontStyles.Bold, new Color(1f, 0.85f, 0.4f),
            new Vector2(0, 0), new Vector2(0.45f, 1f), new Vector2(28, 0), Vector2.zero, TextAlignmentOptions.Left);
        locationText = AnchoredText(footer, "Location", "Overworld", 20, FontStyles.Normal, Color.white,
            new Vector2(0.55f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-28, 0), TextAlignmentOptions.Right);

        RectTransform tabs = CreatePanel("Tabs", root, new Color(0.07f, 0.08f, 0.14f, 0.9f));
        SetAnchors(tabs, new Vector2(0, 0), new Vector2(0, 1), new Vector2(20, 74), new Vector2(260, -84));

        GameObject tabListGo = new GameObject("List");
        tabListGo.transform.SetParent(tabs, false);
        StretchFill(tabListGo.AddComponent<RectTransform>());
        VerticalLayoutGroup vlg = tabListGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 12, 12);
        vlg.spacing = 8;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        MakeTabButton(tabListGo.transform, "Inventory", 52, () => SelectTab(Tab.Inventory), Tab.Inventory);
        MakeTabButton(tabListGo.transform, "Equipment", 52, () => SelectTab(Tab.Equipment), Tab.Equipment);
        MakeTabButton(tabListGo.transform, "Party", 52, () => SelectTab(Tab.Party), Tab.Party);
        MakeTabButton(tabListGo.transform, "Save", 52, () => SelectTab(Tab.Save), Tab.Save);
        MakeTabButton(tabListGo.transform, "Load", 52, () => SelectTab(Tab.Load), Tab.Load);
        MakeTabButton(tabListGo.transform, "Settings", 52, () => SelectTab(Tab.Settings), Tab.Settings);

        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(tabListGo.transform, false);
        spacer.AddComponent<RectTransform>();
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        Button quit = MakeButton(tabListGo.transform, "QUIT  (to title)", 52,
            () => { Close(); SceneLoader.Load(GameManager.Instance.mainMenuSceneName); });
        quit.GetComponent<Image>().color = new Color(0.35f, 0.12f, 0.15f, 0.95f);

        RectTransform content = CreatePanel("Content", root, new Color(0.07f, 0.08f, 0.14f, 0.95f));
        SetAnchors(content, new Vector2(0, 0), new Vector2(1, 1), new Vector2(290, 74), new Vector2(-350, -84));

        GameObject listGo = new GameObject("List");
        listGo.transform.SetParent(content, false);
        contentList = listGo.transform;
        StretchFill(listGo.AddComponent<RectTransform>());
        VerticalLayoutGroup clg = listGo.AddComponent<VerticalLayoutGroup>();
        clg.padding = new RectOffset(14, 14, 12, 12);
        clg.spacing = 6;
        clg.childControlWidth = true; clg.childControlHeight = true;
        clg.childForceExpandWidth = true; clg.childForceExpandHeight = false;

        RectTransform partyCol = CreatePanel("PartyColumn", root, new Color(0.07f, 0.08f, 0.14f, 0.9f));
        SetAnchors(partyCol, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-330, 74), new Vector2(-20, -84));

        GameObject rowGo = new GameObject("PartyList");
        rowGo.transform.SetParent(partyCol, false);
        partyRow = rowGo.transform;
        StretchFill(rowGo.AddComponent<RectTransform>());
        VerticalLayoutGroup plg = rowGo.AddComponent<VerticalLayoutGroup>();
        plg.padding = new RectOffset(10, 10, 10, 10);
        plg.spacing = 8;
        plg.childControlWidth = true; plg.childControlHeight = true;
        plg.childForceExpandWidth = false; plg.childForceExpandHeight = false;
        plg.childAlignment = TextAnchor.UpperCenter;

        BuildToggleCornerButton();
    }

    private void MakeTabButton(Transform parent, string label, float height,
                               UnityEngine.Events.UnityAction onClick, Tab tab)
    {
        Button b = MakeButton(parent, label, height, onClick);
        tabImages[tab] = b.GetComponent<Image>();
    }

    // ---------------- party column ----------------

    private void BuildPartyColumn()
    {
        ClearChildren(partyRow);
        GameManager gm = GameManager.Instance;

        List<PartyMember> all = gm.party;
        int activeCount = gm.ActiveParty().Count;

        int total = all.Count + Mathf.Max(0, 4 - activeCount);
        float h = Mathf.Clamp(860f / Mathf.Max(1, total) - 8f, 118f, 140f);

        int accent = 0;
        foreach (PartyMember pm in all)
        {
            BuildMemberCard(pm, h, accent);
            accent++;
        }
        for (int i = all.Count; i < total; i++) BuildEmptyCard(h);
    }

    private void BuildMemberCard(PartyMember pm, float height, int accentIndex)
    {
        bool selected = (pm == selectedMember);
        Color accent = selected ? new Color(1f, 0.85f, 0.3f) : CardAccents[accentIndex % CardAccents.Length];

        GameObject card = new GameObject("Card_" + pm.data.characterName);
        card.transform.SetParent(partyRow, false);

        Image bg = card.AddComponent<Image>();
        bg.color = pm.isActive
            ? (selected ? new Color(0.16f, 0.18f, 0.26f, 0.95f) : new Color(0.09f, 0.10f, 0.17f, 0.95f))
            : new Color(0.06f, 0.07f, 0.12f, 0.9f);

        Button btn = card.AddComponent<Button>();
        btn.targetGraphic = bg;
        PartyMember captured = pm;
        btn.onClick.AddListener(() => { selectedMember = captured; RefreshAll(); });

        LayoutElement le = card.AddComponent<LayoutElement>();
        le.preferredWidth = 292;
        le.preferredHeight = height;

        GameObject stripGo = new GameObject("Accent");
        stripGo.transform.SetParent(card.transform, false);
        Image strip = stripGo.AddComponent<Image>();
        strip.color = accent;
        SetAnchors((RectTransform)stripGo.transform, new Vector2(0, 0), new Vector2(0, 1),
            Vector2.zero, new Vector2(8, 0));

        GameObject portraitGo = new GameObject("Portrait");
        portraitGo.transform.SetParent(card.transform, false);
        Image portrait = portraitGo.AddComponent<Image>();
        portrait.sprite = pm.data.portrait;
        portrait.color = pm.data.portrait != null ? (pm.isActive ? Color.white : new Color(0.6f, 0.6f, 0.6f)) : accent;
        RectTransform prt = (RectTransform)portraitGo.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
        prt.pivot = new Vector2(0f, 0.5f);
        prt.sizeDelta = new Vector2(92, 92);
        prt.anchoredPosition = new Vector2(22, 0);

        UnitStats stats = pm.CreateStats();
        int hp = Mathf.Clamp(pm.currentHP, 0, stats.MaxHP);
        int mp = Mathf.Clamp(pm.currentMP, 0, stats.MaxMP);
        float hpFrac = stats.MaxHP > 0 ? (float)hp / stats.MaxHP : 0f;
        float mpFrac = stats.MaxMP > 0 ? (float)mp / stats.MaxMP : 0f;

        Color nameColor = pm.isActive ? Color.white : new Color(0.55f, 0.58f, 0.65f);
        AnchoredText(card.transform, "Name", pm.data.characterName, 19, FontStyles.Bold, nameColor,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -30), new Vector2(-8, -8),
            TextAlignmentOptions.Left);
        AnchoredText(card.transform, "Lv", "Lv " + pm.level + (pm.isActive ? "" : "  (benched)"), 14,
            FontStyles.Normal, new Color(0.72f, 0.76f, 0.84f),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -52), new Vector2(-8, -34),
            TextAlignmentOptions.Left);

        AnchoredText(card.transform, "HPText", "HP  " + hp + "/" + stats.MaxHP, 13, FontStyles.Bold, Color.white,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -72), new Vector2(-8, -56),
            TextAlignmentOptions.Left);
        CreateBar(card.transform, "HPBar", 124, -82, -74, hpFrac, new Color(0.2f, 0.85f, 0.35f));

        AnchoredText(card.transform, "MPText", "MP  " + mp + "/" + stats.MaxMP, 13, FontStyles.Bold, Color.white,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -100), new Vector2(-8, -84),
            TextAlignmentOptions.Left);
        CreateBar(card.transform, "MPBar", 124, -110, -102, mpFrac, new Color(0.35f, 0.6f, 1f));
    }

    private void BuildEmptyCard(float height)
    {
        GameObject card = new GameObject("Card_Empty");
        card.transform.SetParent(partyRow, false);
        Image bg = card.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.08f, 0.14f, 0.55f);
        bg.raycastTarget = false;

        LayoutElement le = card.AddComponent<LayoutElement>();
        le.preferredWidth = 292;
        le.preferredHeight = height;

        GameObject stripGo = new GameObject("Accent");
        stripGo.transform.SetParent(card.transform, false);
        Image strip = stripGo.AddComponent<Image>();
        strip.color = new Color(0.3f, 0.32f, 0.38f, 0.8f);
        SetAnchors((RectTransform)stripGo.transform, new Vector2(0, 0), new Vector2(0, 1),
            Vector2.zero, new Vector2(8, 0));

        AnchoredText(card.transform, "Empty", "Empty", 22, FontStyles.Italic, new Color(0.45f, 0.48f, 0.55f),
            Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-8, 0), TextAlignmentOptions.Center);
    }

    private static void CreateBar(Transform card, string name, float left, float bottom, float top, float frac, Color color)
    {
        GameObject bgGo = new GameObject(name);
        bgGo.transform.SetParent(card, false);
        Image bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.55f);
        bg.raycastTarget = false;
        RectTransform bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = new Vector2(0, 1); bgRt.anchorMax = new Vector2(1, 1);
        bgRt.offsetMin = new Vector2(left, bottom);
        bgRt.offsetMax = new Vector2(-8, top);

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(bgGo.transform, false);
        Image fill = fillGo.AddComponent<Image>();
        fill.color = color;
        fill.raycastTarget = false;
        RectTransform frt = (RectTransform)fillGo.transform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
    }

    // ---------------- content panes ----------------

    private void BuildContentPane()
    {
        ClearChildren(contentList);
        switch (currentTab)
        {
            case Tab.Inventory:  BuildInventoryPane();  break;
            case Tab.Equipment:  BuildEquipmentPane();  break;
            case Tab.Party:      BuildPartyPane();      break;
            case Tab.Save:       BuildSavePane();       break;
            case Tab.Load:       BuildLoadPane();       break;
            case Tab.Settings:   BuildSettingsPane();   break;
        }
    }

    private void BuildInventoryPane()
    {
        if (inventoryDetail != null) { BuildItemDetailPane(); return; }
        GameManager gm = GameManager.Instance;

        GameObject catRow = new GameObject("Categories");
        catRow.transform.SetParent(contentList, false);
        catRow.AddComponent<RectTransform>();
        catRow.AddComponent<LayoutElement>().preferredHeight = 34;
        HorizontalLayoutGroup hlg = catRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;

        foreach (ItemCategory cat in System.Enum.GetValues(typeof(ItemCategory)))
        {
            ItemCategory captured = cat;
            Button b = MakeButton(catRow.transform, CategoryName(cat), 32,
                () => { currentCategory = captured; inventoryDetail = null; BuildContentPane(); });
            b.GetComponent<Image>().color = (cat == currentCategory)
                ? new Color(0.30f, 0.35f, 0.52f, 0.95f)
                : new Color(0.16f, 0.19f, 0.28f, 0.85f);
        }

        if (currentCategory == ItemCategory.Equipment)
        {
            AddContentLabel("EQUIPMENT", 26);
            bool any = false;
            foreach (EquipmentStack st in gm.equipmentInventory)
            {
                if (st == null || st.equipment == null || st.count <= 0) continue;
                any = true;
                EquipmentData captured = st.equipment;
                Button b = AddContentButton(captured.equipName + "  x" + st.count, 38,
                    () => { inventoryDetail = captured; BuildContentPane(); });
                BindHover(b, captured.description + "   (" + DescribeEquipment(captured) + ")");
            }
            if (!any) AddContentLabel("(no equipment in the bag)", 24);
        }
        else
        {
            AddContentLabel(currentCategory.ToString().ToUpper() + "S", 26);
            bool any = false;
            foreach (ItemStack st in gm.items)
            {
                if (st == null || st.item == null || st.count <= 0) continue;
                if (st.item.category != currentCategory) continue;
                any = true;
                ItemData captured = st.item;
                Button b = AddContentButton(captured.itemName + "    x" + st.count, 38,
                    () => { inventoryDetail = captured; BuildContentPane(); });
                BindHover(b, captured.description);
            }
            if (!any) AddContentLabel("(nothing here yet)", 24);
        }
    }

    private static string CategoryName(ItemCategory c)
    {
        switch (c)
        {
            case ItemCategory.Item:       return "Items";
            case ItemCategory.Consumable: return "Consumables";
            case ItemCategory.Equipment:  return "Equipment";
            default:                      return "Key Items";
        }
    }

    private void BuildItemDetailPane()
    {
        AddContentButton("←  Back", 34, () => { inventoryDetail = null; BuildContentPane(); });

        ItemData item = inventoryDetail as ItemData;
        EquipmentData eq = inventoryDetail as EquipmentData;

        if (item != null)
        {
            AddContentLabel(item.itemName, 30);
            AddContentLabel(item.description, 56);
            AddContentLabel(EffectSummary(item), 40);

            if (item.category == ItemCategory.Consumable)
            {
                AddContentLabel("— USE ON —", 24);
                foreach (PartyMember pm in GameManager.Instance.party)
                {
                    PartyMember captured = pm;
                    UnitStats s = pm.CreateStats();
                    AddContentButton(captured.data.characterName + "    HP " + captured.currentHP + "/" + s.MaxHP
                        + "   MP " + captured.currentMP + "/" + s.MaxMP, 36,
                        () => UseConsumable(captured, item));
                }
            }
        }
        else if (eq != null)
        {
            AddContentLabel(eq.equipName, 30);
            AddContentLabel(eq.description, 56);
            string bonus = DescribeEquipment(eq);
            AddContentLabel(bonus != "" ? "Bonuses: " + bonus : "No stat bonuses.", 32);
            if (eq.grantedResistances != null && eq.grantedResistances.Length > 0)
                AddContentLabel("Grants resistance: "
                    + string.Join(", ", System.Array.ConvertAll(eq.grantedResistances, r => r.ToString())), 28);
            AddContentLabel("Equip and compare stats from the Equipment tab.", 24);
        }
    }

    private static string EffectSummary(ItemData item)
    {
        var parts = new List<string>();
        if (item.healHP > 0) parts.Add("Restores " + item.healHP + " HP");
        if (item.healMP > 0) parts.Add("Restores " + item.healMP + " MP");
        if (item.revive) parts.Add("Revives a fallen ally");
        if (item.damagePower > 0) parts.Add("Deals " + item.damagePower + " " + item.damageType + " damage in battle");
        return parts.Count > 0 ? string.Join(". ", parts) + "." : "No overworld effect.";
    }

    private void UseConsumable(PartyMember pm, ItemData item)
    {
        GameManager gm = GameManager.Instance;
        UnitStats s = pm.CreateStats();

        if (pm.currentHP <= 0)
        {
            if (!item.revive)
            {
                if (descriptionText != null) descriptionText.text = pm.data.characterName + " needs a reviving item.";
                return;
            }
            if (!gm.ConsumeItem(item, 1)) return;
            pm.currentHP = Mathf.Max(1, Mathf.RoundToInt(s.MaxHP * 0.5f) + item.healHP);
            if (descriptionText != null) descriptionText.text = pm.data.characterName + " is revived!";
        }
        else
        {
            if (item.healHP <= 0 && item.healMP <= 0)
            {
                if (descriptionText != null) descriptionText.text = "That item has no effect here.";
                return;
            }
            if (!gm.ConsumeItem(item, 1)) return;
            pm.currentHP = Mathf.Min(s.MaxHP, pm.currentHP + item.healHP);
            pm.currentMP = Mathf.Min(s.MaxMP, pm.currentMP + item.healMP);
            if (descriptionText != null) descriptionText.text = pm.data.characterName + " feels better!";
        }
        RefreshAll();
    }

    private void BuildEquipmentPane()
    {
        GameManager gm = GameManager.Instance;
        if (selectedMember == null) { AddContentLabel("(no party members)", 26); return; }
        PartyMember pm = selectedMember;

        AddContentLabel("— " + pm.data.characterName + " —   (click a character on the right to switch)", 30);

        TextMeshProUGUI preview = AddContentLabel("Hover equipment to preview the stat change.", 56);

        string w = pm.data.signatureWeapon != null ? pm.data.signatureWeapon.equipName : "—";
        Button wb = AddContentButton("Weapon:  " + w + "   (unique)", 40, null);
        wb.interactable = false;

        bool selA = selectedSlot == EquipSlot.Armor;
        AddContentButton((selA ? ">  " : "") + "Armor:  " + EqName(pm.armor), 40,
            () => { selectedSlot = EquipSlot.Armor; BuildContentPane(); });

        bool sel1 = selectedSlot == EquipSlot.Accessory && selectedAcc == 1;
        AddContentButton((sel1 ? ">  " : "") + "Accessory 1:  " + EqName(pm.accessory1), 40,
            () => { selectedSlot = EquipSlot.Accessory; selectedAcc = 1; BuildContentPane(); });

        bool sel2 = selectedSlot == EquipSlot.Accessory && selectedAcc == 2;
        AddContentButton((sel2 ? ">  " : "") + "Accessory 2:  " + EqName(pm.accessory2), 40,
            () => { selectedSlot = EquipSlot.Accessory; selectedAcc = 2; BuildContentPane(); });

        if (selectedSlot != EquipSlot.Weapon)
        {
            EquipmentData cur = gm.GetEquipped(pm, selectedSlot, selectedAcc);
            if (cur != null)
            {
                Button un = AddContentButton("✕  Unequip " + (selectedSlot == EquipSlot.Armor ? "armor" : "accessory"), 36,
                    () => { gm.Unequip(pm, selectedSlot, selectedAcc); RefreshAll(); });
                BindPreview(un, "Unequipping " + cur.equipName + "\n" + EquipDiff(pm, selectedSlot, selectedAcc, null), preview);
            }

            AddContentLabel((selectedSlot == EquipSlot.Armor ? "ARMOR" : "ACCESSORY " + selectedAcc) + " — click to equip:", 24);
            List<EquipmentStack> options = gm.GetEquipmentForSlot(selectedSlot);
            if (options.Count == 0) AddContentLabel("(nothing of this type in the bag)", 24);
            foreach (EquipmentStack stack in options)
            {
                EquipmentData captured = stack.equipment;
                string bonus = DescribeEquipment(captured);
                Button b = AddContentButton(captured.equipName + "  x" + stack.count
                    + (bonus != "" ? "   " + bonus : ""), 38,
                    () => { gm.Equip(pm, selectedSlot, captured, selectedAcc); RefreshAll(); });
                BindPreview(b, captured.equipName + "\n" + captured.description + "\n"
                    + EquipDiff(pm, selectedSlot, selectedAcc, captured), preview);
            }
        }

        AddContentLabel(StatsText(pm), 170);
    }

    private static string EqName(EquipmentData eq) { return eq != null ? eq.equipName : "—"; }

    private void BindPreview(Button btn, string text, TextMeshProUGUI target)
    {
        if (btn == null || target == null) return;
        HoverDescription h = btn.gameObject.GetComponent<HoverDescription>();
        if (h == null) h = btn.gameObject.AddComponent<HoverDescription>();
        h.Bind(text, s => target.text = s);
    }

    private static string EquipDiff(PartyMember pm, EquipSlot slot, int accIndex, EquipmentData candidate)
    {
        UnitStats before = UnitStats.FromCharacter(pm.data, pm.level, pm.armor, pm.accessory1, pm.accessory2);

        EquipmentData armor = pm.armor, a1 = pm.accessory1, a2 = pm.accessory2;
        if (slot == EquipSlot.Armor) armor = candidate;
        else if (accIndex == 1) a1 = candidate;
        else a2 = candidate;

        UnitStats after = UnitStats.FromCharacter(pm.data, pm.level, armor, a1, a2);

        var sb = new System.Text.StringBuilder("Equipping: ");
        bool any = false;
        any |= Diff(sb, "HP", before.MaxHP, after.MaxHP);
        any |= Diff(sb, "MP", before.MaxMP, after.MaxMP);
        any |= Diff(sb, "Atk", before.Attack, after.Attack);
        any |= Diff(sb, "Def", before.Defense, after.Defense);
        any |= Diff(sb, "Mag", before.Magic, after.Magic);
        any |= Diff(sb, "Res", before.Resistance, after.Resistance);
        any |= Diff(sb, "Spd", before.Speed, after.Speed);
        if (!any) sb.Append("no stat change.");
        return sb.ToString();
    }

    private static bool Diff(System.Text.StringBuilder sb, string label, int b, int a)
    {
        if (a == b) return false;
        if (sb[sb.Length - 1] != ' ') sb.Append("   ");
        sb.Append(label).Append(a > b ? " +" : " ").Append(a - b);
        return true;
    }

    private void BuildPartyPane()
    {
        if (selectedMember == null) { AddContentLabel("(no party members)", 26); return; }

        AddContentLabel("— " + selectedMember.data.characterName + " —", 32);
        AddContentLabel(selectedMember.isActive ? "In the battle team" : "Benched — does not enter battle", 26);

        AddContentButton("Move up in lineup", 40, () => MoveMember(-1));
        AddContentButton("Move down in lineup", 40, () => MoveMember(1));
        AddContentButton(selectedMember.isActive ? "Bench (leave battle team)" : "Join the battle team", 44,
            () => ToggleActive());

        AddContentLabel("Click a character on the right to select them.\nUp to four members enter battle.", 60);
    }

    private void MoveMember(int direction)
    {
        GameManager gm = GameManager.Instance;
        int index = gm.party.IndexOf(selectedMember);
        int newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= gm.party.Count) return;
        var temp = gm.party[index];
        gm.party[index] = gm.party[newIndex];
        gm.party[newIndex] = temp;
        partyDirty = true;
        RefreshAll();
    }

    private void ToggleActive()
    {
        GameManager gm = GameManager.Instance;
        if (selectedMember.isActive)
        {
            if (gm.ActiveParty().Count <= 1)
            {
                if (descriptionText != null) descriptionText.text = "At least one member must stay in the battle team!";
                return;
            }
            selectedMember.isActive = false;
        }
        else
        {
            if (gm.ActiveParty().Count >= 4)
            {
                if (descriptionText != null) descriptionText.text = "The battle team is full (4 members max).";
                return;
            }
            selectedMember.isActive = true;
        }
        partyDirty = true;
        RefreshAll();
    }

    private void BuildSavePane()
    {
        AddContentLabel("SAVE — click a slot to record your journey", 24);
        SaveSlotUI.Build(contentList,
            slot => { SaveSystem.Save(slot); BuildContentPane(); },
            true, true, BuildContentPane);
    }

    private void BuildLoadPane()
    {
        AddContentLabel("LOAD — click a save to restore it (replaces the current session)", 24);
        SaveSlotUI.Build(contentList,
            slot => { Close(); SaveSystem.LoadIntoGame(slot); },
            true, false, BuildContentPane);
    }

    private void BuildSettingsPane()
    {
        AddContentLabel("Nothing here yet — settings are coming soon.", 30);
    }

    // ---------------- footer ----------------

    private void UpdateFooter()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        if (goldText != null) goldText.text = gm.gold + " G";
        if (locationText != null) locationText.text = CurrentLocation();
    }

    private static string CurrentLocation()
    {
        if (!string.IsNullOrEmpty(LocationOverride)) return LocationOverride;
        string n = SceneManager.GetActiveScene().name;
        int cut = n.IndexOf('_');
        if (cut > 0 && cut <= 3) n = n.Substring(cut + 1);
        return n;
    }

    // ---------------- small UI helpers ----------------

    private Button AddContentButton(string label, float height, UnityEngine.Events.UnityAction onClick)
    {
        return MakeButton(contentList, label, height, onClick);
    }

    private TextMeshProUGUI AddContentLabel(string text, float height)
    {
        return MakeLabel(contentList, text, 16, FontStyles.Normal, height, new Color(0.72f, 0.76f, 0.84f));
    }

    private void BindHover(Button btn, string description)
    {
        if (btn == null) return;
        HoverDescription h = btn.gameObject.GetComponent<HoverDescription>();
        if (h == null) h = btn.gameObject.AddComponent<HoverDescription>();
        h.Bind(description, s => { if (descriptionText != null) descriptionText.text = s; });
    }

    private Button MakeButton(Transform parent, string label, float height, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.16f, 0.19f, 0.28f, 0.95f);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        TextMeshProUGUI t = MakeLabel(go.transform, label, Mathf.Max(13, (int)(height - 12)), FontStyles.Normal, height, Color.white);
        if (t != null)
        {
            t.alignment = TextAlignmentOptions.Center;
            StretchFill(t.rectTransform);
        }
        return btn;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string text, int size, FontStyles style, float height, Color color)
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("OverworldMenu: TMP default font missing — import TMP Essential Resources.");
            return null;
        }
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        return t;
    }

    private TextMeshProUGUI AnchoredText(Transform parent, string name, string text, int size,
        FontStyles style, Color color, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align)
    {
        if (TMP_Settings.defaultFontAsset == null) return null;
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        RectTransform rt = (RectTransform)go.transform;
        SetAnchors(rt, anchorMin, anchorMax, offsetMin, offsetMax);
        return t;
    }

    private RectTransform CreatePanel(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        return (RectTransform)go.transform;
    }

    private static void SetAnchors(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
    }

    // ---------------- text builders ----------------

    private static string StatsText(PartyMember pm)
    {
        UnitStats s = pm.CreateStats();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(pm.data.characterName + "   Lv " + pm.level);
        sb.AppendLine("HP " + pm.currentHP + "/" + s.MaxHP + "    MP " + pm.currentMP + "/" + s.MaxMP);
        sb.AppendLine("Atk " + s.Attack + "  Def " + s.Defense + "  Mag " + s.Magic
            + "  Res " + s.Resistance + "  Spd " + s.Speed);
        sb.AppendLine("Weak:   " + FormatTypes(s.weaknesses));
        sb.AppendLine("Resist: " + FormatTypes(s.resistances));
        return sb.ToString();
    }

    private static string FormatTypes(List<DamageType> types)
    {
        if (types == null || types.Count == 0) return "—";
        var names = new List<string>();
        foreach (DamageType t in types) names.Add(t.ToString());
        return string.Join(", ", names);
    }

    private static string DescribeEquipment(EquipmentData eq)
    {
        if (eq == null) return "";
        var parts = new List<string>();
        StatBlock s = eq.statBonus;
        if (s.maxHP != 0) parts.Add("HP" + s.maxHP);
        if (s.maxMP != 0) parts.Add("MP" + s.maxMP);
        if (s.attack != 0) parts.Add("Atk" + s.attack);
        if (s.defense != 0) parts.Add("Def" + s.defense);
        if (s.magic != 0) parts.Add("Mag" + s.magic);
        if (s.resistance != 0) parts.Add("Res" + s.resistance);
        if (s.speed != 0) parts.Add("Spd" + s.speed);
        return parts.Count > 0 ? string.Join(" ", parts) : "";
    }
}