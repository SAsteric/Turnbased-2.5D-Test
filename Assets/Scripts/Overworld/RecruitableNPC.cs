using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// ===========================================================================
// Recruitable NPC — an overworld character you walk up to and recruit.
//
// SETUP (all of it):
//   1. In 01_Overworld: Create Empty → name it (e.g. "NPC_Aria")
//   2. Add this component, drag the CharacterData into 'character'
//   3. Position it on the ground (y = 0), near the player's spawn
// Everything else — field sprite, talk prompt, talk-range trigger,
// dialogue box, join choice — is built from code. Press F near them.
//
// After joining: added to GameManager.party, follows in the overworld,
// fights in battle, still follows after every battle. If already in the
// party when the scene loads, this NPC disables itself.
// ===========================================================================
public class RecruitableNPC : MonoBehaviour
{
    [Header("Who this is (drag the CharacterData)")]
    public CharacterData character;

    [Header("Dialogue lines")]
    [TextArea(2, 3)]
    public string[] dialogueLines = new string[]
    {
        "Oh! You must be the traveler everyone is talking about.",
        "The roads have been dangerous lately. Slimes, goblins... you name it.",
        "Say... would you let me travel with you?"
    };
    public string joinQuestion = "Will you join the party?";
    public string partyFullLine = "Your party looks full. Maybe another time.";

    [Header("Field appearance")]
    public float npcWorldHeight = 2.2f;
    public float talkRange = 2.6f;
    public string promptText = "Press [F] to talk";

    public static bool DialogueOpen { get { return activeDialogue != null; } }
    private static RecruitableNPC activeDialogue;

    private bool playerInRange;
    private int lineIndex;
    private bool showingChoice, showingEndLine;

    private TextMesh promptMesh;
    private GameObject panel;
    private TextMeshProUGUI nameText, bodyText, hintText;
    private Button yesButton, noButton;

    private void Awake()
    {
        if (character == null)
        {
            Debug.LogError("RecruitableNPC: no CharacterData assigned — NPC disabled.");
            gameObject.SetActive(false);
            return;
        }

        BuildFieldSprite();
        BuildPrompt();
        BuildTalkTrigger();
        BuildDialogueCanvas();
    }

    private void Start()
    {
        // Already recruited in this play session? (party persists in GameManager)
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            foreach (PartyMember pm in gm.party)
                if (pm != null && pm.data == character) { gameObject.SetActive(false); return; }
        }
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        bool fPressed = kb != null && kb.fKey.wasPressedThisFrame;

        // --- this NPC's dialogue is open: F advances / closes ---
        if (activeDialogue == this)
        {
            if (fPressed)
            {
                if (showingEndLine) CloseDialogue();
                else if (!showingChoice) Advance();
                // showingChoice: F does nothing — pick a button
            }
            return;
        }
        if (activeDialogue != null) return; // someone else's dialogue is running

        // --- idle: show the prompt when the player is close ---
        bool otherUI = OverworldMenu.IsOpen;
        if (promptMesh != null)
            promptMesh.gameObject.SetActive(playerInRange && !otherUI);

        if (fPressed && playerInRange && !otherUI)
            OpenDialogue();
    }

    // ---------------- triggers ----------------

    private void OnTriggerEnter(Collider other)
    {
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc != null && pc.enabled) playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc != null && pc.enabled) playerInRange = false;
    }

    // ---------------- dialogue flow ----------------

    private void OpenDialogue()
    {
        activeDialogue = this;
        lineIndex = 0;
        showingChoice = false;
        showingEndLine = false;
        if (promptMesh != null) promptMesh.gameObject.SetActive(false);
        if (panel != null) panel.SetActive(true);
        if (nameText != null) nameText.text = character.characterName;

        if (dialogueLines == null || dialogueLines.Length == 0) ShowChoice();
        else ShowLine(0);
    }

    private void ShowLine(int index)
    {
        if (bodyText != null) bodyText.text = dialogueLines[index];
        SetHint("[F]  Continue");
        SetButtons(false);
    }

    private void Advance()
    {
        lineIndex++;
        if (lineIndex < dialogueLines.Length) ShowLine(lineIndex);
        else ShowChoice();
    }

    private void ShowChoice()
    {
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.ActiveParty().Count >= 4)
        {
            showingEndLine = true;
            if (bodyText != null) bodyText.text = partyFullLine;
            SetHint("[F]  Close");
            SetButtons(false);
            return;
        }

        showingChoice = true;
        if (bodyText != null) bodyText.text = joinQuestion;
        SetHint(null);
        SetButtons(true);
    }

    private void SetHint(string text)
    {
        if (hintText == null) return;
        hintText.gameObject.SetActive(text != null);
        if (text != null) hintText.text = text;
    }

    private void SetButtons(bool on)
    {
        if (yesButton != null) yesButton.gameObject.SetActive(on);
        if (noButton != null) noButton.gameObject.SetActive(on);
    }

    private void CloseDialogue()
    {
        if (activeDialogue == this) activeDialogue = null;
        showingChoice = false;
        showingEndLine = false;
        if (panel != null) panel.SetActive(false);
    }

    // ---------------- choice handlers ----------------

    private void OnYesClicked()
    {
        Recruit();
    }

    private void OnNoClicked()
    {
        CloseDialogue();
    }

    private void Recruit()
    {
        CloseDialogue();
        GameManager gm = GameManager.Instance;
        if (gm == null || character == null) return;

        gm.party.Add(new PartyMember(character)); // full HP/MP, level from the asset

        if (PlayerParty.Instance != null)
            PlayerParty.Instance.RebuildFromParty(); // she spawns as a follower right behind you

        Destroy(gameObject); // the NPC leaves the field
    }

    // ---------------- construction ----------------

    private void BuildFieldSprite()
    {
        GameObject spriteGo = new GameObject("Sprite");
        spriteGo.transform.SetParent(transform, false);
        SpriteRenderer sr = spriteGo.AddComponent<SpriteRenderer>();

        Sprite sprite = character.portrait;
        if (sprite == null)
        {
            Debug.LogWarning("RecruitableNPC: '" + character.characterName +
                "' has no Portrait — showing a white block. Assign a portrait sprite.");
            sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        }
        sr.sprite = sprite;

        float srcH = sprite.bounds.size.y;
        float s = srcH > 0.001f ? npcWorldHeight / srcH : 1f;
        spriteGo.transform.localScale = new Vector3(s, s, 1f);
        spriteGo.transform.localPosition = new Vector3(0f, npcWorldHeight * 0.5f, 0f);
    }

    private void BuildPrompt()
    {
        Font font = GetLegacyFont();
        if (font == null) return;

        GameObject go = new GameObject("TalkPrompt");
        go.transform.SetParent(transform, false);
        promptMesh = go.AddComponent<TextMesh>();
        promptMesh.font = font;
        promptMesh.text = promptText;
        promptMesh.fontSize = 40;
        promptMesh.characterSize = 0.085f;
        promptMesh.anchor = TextAnchor.LowerCenter;
        promptMesh.alignment = TextAlignment.Center;
        promptMesh.color = Color.white;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = font.material;
        go.transform.localPosition = new Vector3(0f, npcWorldHeight + 0.35f, 0f);
        go.SetActive(false);
    }

    private void BuildTalkTrigger()
    {
        SphereCollider col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = talkRange;
        col.center = new Vector3(0f, 1f, 0f);
    }

    private void BuildDialogueCanvas()
    {
        GameObject canvasGo = new GameObject("DialogueCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600; // above FieldEquipmentUI (500), below SceneLoader

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>(); // buttons need it

        GameObject panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panel = panelGo;
        RectTransform prt = panelGo.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0f);
        prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 24f);
        prt.sizeDelta = new Vector2(880f, 230f);
        Image pimg = panelGo.AddComponent<Image>();
        pimg.color = new Color(0.06f, 0.07f, 0.12f, 0.95f);

        nameText = MakeLabel(panelGo.transform, "", 26, FontStyles.Bold, 0.74f, 0.96f, Color.white);
        bodyText = MakeLabel(panelGo.transform, "", 23, FontStyles.Normal, 0.28f, 0.72f, Color.white);
        if (bodyText != null) bodyText.alignment = TextAlignmentOptions.TopLeft;
        hintText = MakeLabel(panelGo.transform, "[F]  Continue", 19, FontStyles.Normal,
            0.03f, 0.24f, new Color(0.72f, 0.76f, 0.84f));

        GameObject rowGo = new GameObject("ChoiceRow");
        rowGo.transform.SetParent(panelGo.transform, false);
        RectTransform rrt = rowGo.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.52f, 0.04f);
        rrt.anchorMax = new Vector2(0.97f, 0.25f);
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;
        HorizontalLayoutGroup hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleRight;

        yesButton = MakeButton(rowGo.transform, "Yes, join us", OnYesClicked);
        noButton = MakeButton(rowGo.transform, "Not now", OnNoClicked);
        SetButtons(false);

        panel.SetActive(false);
    }

    // ---------------- small UI helpers ----------------

    private TextMeshProUGUI MakeLabel(Transform parent, string text, int size, FontStyles style,
                                      float ay0, float ay1, Color color)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>(); // creates the RectTransform
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.03f, ay0);
        rt.anchorMax = new Vector2(0.97f, ay1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("RecruitableNPC: TMP default font missing — import TMP Essential Resources.");
            return null;
        }
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        t.raycastTarget = false;
        return t;
    }

    private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>(); // creates the RectTransform
        img.color = new Color(0.16f, 0.19f, 0.28f, 0.95f);
        Button btn = go.AddComponent<Button>();
        if (onClick != null) btn.onClick.AddListener(onClick);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 44f;

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        TextMeshProUGUI t = textGo.AddComponent<TextMeshProUGUI>();
        RectTransform trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        t.font = TMP_Settings.defaultFontAsset;
        t.text = label;
        t.fontSize = 20;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return btn;
    }

    private static Font legacyFont;
    private static Font GetLegacyFont()
    {
        if (legacyFont == null)
        {
            try { legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { legacyFont = null; }
        }
        return legacyFont;
    }
}