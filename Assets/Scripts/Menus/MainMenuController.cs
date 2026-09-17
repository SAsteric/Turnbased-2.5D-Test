using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ===========================================================================
// Drives YOUR scene-built title menu (your ALTERIA title, your buttons).
// Wire the buttons in the Inspector. The only code-built UI is the LOAD
// screen overlay (99 slots + delete), which appears when Load is clicked.
// ===========================================================================
public class MainMenuController : MonoBehaviour
{
    public static MainMenuController Instance { get; private set; }

    [Header("Wire your scene buttons here")]
    public Button newGameButton;
    public Button loadGameButton;   // your "Load Game" button
    public Button quitButton;

    private RectTransform loadScreen;
    private Transform loadList;

    private void Start()
    {
        Instance = this;

        if (newGameButton != null) newGameButton.onClick.AddListener(NewGame);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
        if (loadGameButton != null) loadGameButton.onClick.AddListener(ShowLoad);

        BuildLoadScreen();
        RefreshLoadButton();
    }

    // ---------------- main menu ----------------

    private void RefreshLoadButton()
    {
        if (loadGameButton != null)
            loadGameButton.interactable = SaveSystem.AnySaveExists();
    }

    private void NewGame()
    {
        // fresh state — reset THIS GameManager (it holds your Starting Party
        // setup), don't destroy it
        GameManager.EnsureExists();
        GameManager.Instance.ResetToNewGame();
        SceneLoader.Load(GameManager.Instance.overworldSceneName);
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------- load screen (code-built, hidden until needed) ----------------

    private void BuildLoadScreen()
    {
        GameObject rootGo = new GameObject("LoadScreenCanvas");
        Canvas canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;   // above your menu canvas, below the SceneLoader fade (1000)
        CanvasScaler scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        rootGo.AddComponent<GraphicRaycaster>();

        loadScreen = CreatePanel(rootGo.transform, "Panel", new Color(0.05f, 0.06f, 0.12f, 0.97f));
        SetFull(loadScreen);

        TextMeshProUGUI header = MakeText(loadScreen, "LOAD GAME", 36, FontStyles.Bold, 50, Color.white);
        if (header != null)
        {
            header.alignment = TextAlignmentOptions.Left;
            RectTransform hrt = (RectTransform)header.transform;
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.offsetMin = new Vector2(20, -60); hrt.offsetMax = new Vector2(-20, -14);
        }

        GameObject listGo = new GameObject("List");
        listGo.transform.SetParent(loadScreen, false);
        loadList = listGo.transform;
        RectTransform lrt = listGo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(20, 80); lrt.offsetMax = new Vector2(-20, -20);
        VerticalLayoutGroup lvg = listGo.AddComponent<VerticalLayoutGroup>();
        lvg.spacing = 6;
        lvg.childControlWidth = true; lvg.childControlHeight = true;
        lvg.childForceExpandWidth = true; lvg.childForceExpandHeight = false;

        Button back = MakeButton(loadScreen, "←  Back", 44, HideLoad);
        RectTransform brt = (RectTransform)back.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.sizeDelta = new Vector2(220, 44);
        brt.anchoredPosition = new Vector2(0, 16);

        loadScreen.gameObject.SetActive(false);
    }

    private void RebuildLoadList()
    {
        ClearChildren(loadList);
        SaveSlotUI.Build(loadList,
            slot => SaveSystem.LoadIntoGame(slot),
            true,    // delete buttons
            false,   // empty slots are not clickable
            RebuildLoadList);
    }

    private void ShowLoad()
    {
        if (loadScreen == null) return;
        RebuildLoadList();
        loadScreen.gameObject.SetActive(true);
    }

    private void HideLoad()
    {
        if (loadScreen == null) return;
        loadScreen.gameObject.SetActive(false);
        RefreshLoadButton();   // in case every save was deleted from the list
    }

    // ---------------- small helpers ----------------

    private static RectTransform CreatePanel(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        return (RectTransform)go.transform;
    }

    private static void SetFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Button MakeButton(Transform parent, string label, float height, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.16f, 0.19f, 0.28f, 0.95f);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        TextMeshProUGUI t = MakeText(go.transform, label, Mathf.Max(15, (int)(height - 14)), FontStyles.Normal, height, Color.white);
        if (t != null)
        {
            t.alignment = TextAlignmentOptions.Center;
            SetFull((RectTransform)t.transform);
        }
        return btn;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string text, int size, FontStyles style, float height, Color color)
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("MainMenuController: TMP default font missing — import TMP Essential Resources.");
            return null;
        }
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        go.AddComponent<LayoutElement>().preferredHeight = height;
        return t;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
    }
}