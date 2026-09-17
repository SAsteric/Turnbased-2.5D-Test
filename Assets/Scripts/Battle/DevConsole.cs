using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;

// ===========================================================================
// DEV CONSOLE — press F1 (or click the DEV button, top-left corner) in ANY
// scene. Persists across scenes. Fully code-built, self-bootstrapping —
// no scene edits required anywhere.
//
//   OVERWORLD: member select · level +/- · stat bonus · heal / HP=1 ·
//              skill enable/disable · grant equipment (editor only) ·
//              force an encounter in the zone you're standing in.
//   BATTLE:    heal party/enemies · HP=1 · party MP · battle log on/off ·
//              force WIN / force LOSE.
// ===========================================================================
public static class DevConsole
{
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        GameObject go = new GameObject("DevConsole");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }

    // ------------------------------------------------------------------ //

    private class Runner : MonoBehaviour
    {
        private RectTransform panel;
        private Button devButton;
        private Transform content;
        private GameObject myEventSystem;
        private int memberIndex;
        private int statIndex;

        private static readonly string[] StatNames = { "Atk", "Def", "Mag", "Res", "Spd", "MaxHP", "MaxMP" };

        private void Awake()
        {
            BuildCanvas();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            EnsureEventSystem();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (this == null) return;
            SetOpen(false);
            EnsureEventSystem();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) SetOpen(!IsOpen);

            // EVENT SYSTEM GUARD: ours is OFF while a battle is flagged
            // (the battle scene brings its own), and back ON in the overworld
            // as long as no foreign EventSystem serves the scene — so a
            // stuck flag can never leave the overworld click-dead forever.
            if (myEventSystem != null)
            {
                GameManager gm = GameManager.Instance;
                bool battleFlag = gm != null && gm.inBattle;
                if (battleFlag && myEventSystem.activeSelf)
                    myEventSystem.SetActive(false);
                else if (!battleFlag && !myEventSystem.activeSelf)
                {
                    bool foreign = false;
                    foreach (EventSystem es in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                        if (es.gameObject != myEventSystem) { foreign = true; break; }
                    if (!foreign) myEventSystem.SetActive(true);
                }
            }
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            if (panel != null) panel.gameObject.SetActive(open);
            if (devButton != null) devButton.gameObject.SetActive(open); // shows while open = a close button
            if (open) RebuildContent();
        }

        // ---------------- canvas ----------------

        private void BuildCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1050; // above battle UI + SceneLoader, below MirrorBreak

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>(); // makes buttons clickable

            // corner toggle button
            GameObject cornerGo = new GameObject("DevButton");
            cornerGo.transform.SetParent(transform, false);
            RectTransform crt = cornerGo.AddComponent<RectTransform>();   // FIXED
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.anchoredPosition = new Vector2(8f, -8f);
            crt.sizeDelta = new Vector2(96f, 38f);
            Image cimg = cornerGo.AddComponent<Image>();
            cimg.color = new Color(0f, 0f, 0f, 0.6f);
            Button cbtn = cornerGo.AddComponent<Button>();
            cbtn.onClick.AddListener(() => SetOpen(!IsOpen));
            devButton = cbtn;
            TextMeshProUGUI cl = MakeLabel(cornerGo.transform, "DEV [F1]", 18, FontStyles.Bold, 38, Color.white);
            if (cl != null) { cl.alignment = TextAlignmentOptions.Center; StretchFill(cl.rectTransform); }

            // main panel (right side)
            GameObject panelGo = new GameObject("DevPanel");
            panelGo.transform.SetParent(transform, false);
            panel = panelGo.AddComponent<RectTransform>();               // FIXED
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-12f, 0f);
            panel.sizeDelta = new Vector2(430f, 1010f);
            Image pimg = panelGo.AddComponent<Image>();
            pimg.color = new Color(0.05f, 0.06f, 0.1f, 0.93f);

            GameObject contentGo = new GameObject("Content");
            contentGo.transform.SetParent(panelGo.transform, false);
            content = contentGo.AddComponent<RectTransform>();           // FIXED
            StretchFill((RectTransform)content);
            VerticalLayoutGroup vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 5;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            panel.gameObject.SetActive(false);
            if (devButton != null) devButton.gameObject.SetActive(false); // hidden until the console opens
        }

        // ---------------- event system management ----------------
        // The overworld has no EventSystem of its own; the battle scene has one.
        // We keep our own, but disable it whenever the current scene provides one.

        private void EnsureEventSystem()
        {
            EventSystem foreign = null;
            foreach (EventSystem es in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            {
                if (myEventSystem == null || es.gameObject != myEventSystem) { foreign = es; break; }
            }

            if (foreign != null)
            {
                if (myEventSystem != null) myEventSystem.SetActive(false);
            }
            else
            {
                if (myEventSystem == null)
                {
                    myEventSystem = new GameObject("DevEventSystem");
                    myEventSystem.transform.SetParent(transform, false);
                    myEventSystem.AddComponent<EventSystem>();
                    myEventSystem.AddComponent<InputSystemUIInputModule>();
                }
                myEventSystem.SetActive(true);
            }
        }

        // ---------------- content ----------------

        private void RebuildContent()
        {
            ClearChildren(content);
            if (BattleManager.Instance != null) BuildBattleSection();
            else BuildOverworldSection();
        }

        private void BuildOverworldSection()
        {
            AddHeader("DEV — OVERWORLD");

            GameManager gm = GameManager.Instance;
            if (gm == null) { AddLabel("No GameManager (start from 00_MainMenu)."); return; }
            if (gm.party.Count == 0) { AddLabel("Party is empty."); return; }

            memberIndex = Mathf.Clamp(memberIndex, 0, gm.party.Count - 1);
            PartyMember pm = gm.party[memberIndex];

            AddButton("<  " + pm.data.characterName + "  Lv " + pm.level + "  >", 40, () =>
            {
                memberIndex = (memberIndex + 1) % gm.party.Count;
                RebuildContent();
            });

            AddLabel("— PARTY (menu/battle layout testing) —");
#if UNITY_EDITOR
            AddButton("Add party member", 32, () =>
            {
                List<CharacterData> all = FindAllCharacters();
                CharacterData pick = null;
                foreach (CharacterData cd in all)
                {
                    bool inParty = false;
                    foreach (PartyMember pmx in gm.party)
                        if (pmx != null && pmx.data == cd) { inParty = true; break; }
                    if (!inParty) { pick = cd; break; }
                }
                if (pick != null)
                {
                    gm.party.Add(new PartyMember(pick));
                    if (PlayerParty.Instance != null) PlayerParty.Instance.RebuildFromParty();
                }
                else Debug.LogWarning("DevConsole: every CharacterData asset is already in the party.");
                RebuildContent();
            });
#else
            AddLabel("(add member: editor only)");
#endif
            AddButton("Remove last member", 32, () =>
            {
                if (gm.party.Count > 1)
                {
                    gm.party.RemoveAt(gm.party.Count - 1);
                    if (PlayerParty.Instance != null) PlayerParty.Instance.RebuildFromParty();
                }
                RebuildContent();
            });
            AddButton("Gold +1000", 32, () => { gm.gold += 1000; RebuildContent(); });

            AddLabel("— LEVEL —");
            AddButton("Level +1", 32, () => { pm.level++; RebuildContent(); });
            AddButton("Level -1", 32, () => { pm.level = Mathf.Max(1, pm.level - 1); RebuildContent(); });

            AddLabel("— STAT BONUS (±5) —");
            int si = statIndex;
            AddButton("< Stat: " + StatNames[si] + " (" + GetStat(pm, si) + ") >", 34, () =>
            {
                statIndex = (statIndex + 1) % StatNames.Length;
                RebuildContent();
            });
            AddButton(StatNames[si] + " +5", 32, () => { SetStat(pm, si, GetStat(pm, si) + 5); RebuildContent(); });
            AddButton(StatNames[si] + " -5", 32, () => { SetStat(pm, si, GetStat(pm, si) - 5); RebuildContent(); });

            AddLabel("— HEALTH —");
            AddButton("Full heal (HP + MP)", 32, () =>
            {
                UnitStats s = pm.CreateStats();
                pm.currentHP = s.MaxHP; pm.currentMP = s.MaxMP;
                RebuildContent();
            });
            AddButton("Set HP to 1", 32, () => { pm.currentHP = 1; RebuildContent(); });

            AddLabel("— SKILLS (click to toggle) —");
            foreach (LearnableSkill ls in pm.data.learnableSkills)
            {
                if (ls == null || ls.skill == null) continue;
                SkillData skill = ls.skill;
                bool active = pm.IsSkillActive(skill);
                string extra = (active && pm.level < ls.learnLevel) ? "  (forced)" : "";
                AddButton((active ? "[ON]  " : "[OFF] ") + skill.skillName + extra, 30, () =>
                {
                    pm.SetSkillActive(skill, !pm.IsSkillActive(skill));
                    RebuildContent();
                });
            }
            if (pm.data.learnableSkills.Count == 0)
                AddLabel("(no learnable skills on this character)");

            AddLabel("— GRANT EQUIPMENT —");
#if UNITY_EDITOR
            foreach (EquipmentData eq in FindAllEquipment())
            {
                EquipmentData captured = eq;
                AddButton("+1  " + eq.equipName + "  (" + eq.slot + ")", 28, () =>
                {
                    gm.AddEquipment(captured, 1);
                    RebuildContent();
                });
            }

            AddLabel("— GRANT ITEMS —");
            foreach (ItemData item in FindAllItems())
            {
                ItemData captured = item;
                AddButton("+1  " + captured.itemName + GetItemSummary(captured), 28, () =>
                {
                    gm.AddItem(captured, 1);
                    RebuildContent();
                });
            }
#else
            AddLabel("(equipment list: editor only)");
#endif

            AddLabel("— ENCOUNTERS —");
            AddButton("Force battle in current zone", 38, () =>
            {
                if (EncounterManager.currentZone != null && EncounterManager.currentZone.table != null)
                {
                    SetOpen(false);
                    EncounterManager.StartBattle(EncounterManager.currentZone);
                }
                else RebuildContent();
            });
            if (EncounterManager.currentZone == null) AddLabel("Not standing in an encounter zone.");
            else if (EncounterManager.currentZone.table == null) AddLabel("This zone has no table assigned.");
        }
        private void BuildBattleSection()
        {
            AddHeader("DEV — BATTLE");

            BattleManager bm = BattleManager.Instance;
            if (bm == null) { AddLabel("BattleManager not available."); return; }

            AddLabel("— PARTY —");
            AddButton("Heal party (full HP)", 32, () =>
                ForEachAlive(bm.Players, u => { u.Stats.currentHP = u.Stats.MaxHP; u.UpdateHUD(); }));
            AddButton("Party HP = 1", 32, () =>
                ForEachAlive(bm.Players, u => { u.Stats.currentHP = 1; u.UpdateHUD(); }));
            AddButton("Party MP = full", 32, () =>
                ForEachAlive(bm.Players, u => { u.Stats.currentMP = u.Stats.MaxMP; u.UpdateHUD(); }));

            AddLabel("— ENEMIES —");
            AddButton("Heal enemies (full HP)", 32, () =>
                ForEachAlive(bm.Enemies, u => { u.Stats.currentHP = u.Stats.MaxHP; u.UpdateHUD(); }));
            AddButton("Enemies HP = 1", 32, () =>
                ForEachAlive(bm.Enemies, u => { u.Stats.currentHP = 1; u.UpdateHUD(); }));

            AddLabel("— DISPLAY —");
            bool logOn = bm.UI != null && bm.UI.BattleLogVisible;
            AddButton("Battle log:  " + (logOn ? "ON" : "OFF"), 32, () =>
            {
                if (bm.UI != null) bm.UI.ToggleBattleLog();
                RebuildContent();
            });

            AddLabel("— OUTCOME —");
            AddButton("WIN BATTLE", 40, () => { SetOpen(false); bm.DevForceWin(); });
            AddButton("LOSE BATTLE", 40, () => { SetOpen(false); bm.DevForceLose(); });
            AddLabel("(takes effect at the end of the current action)");
        }

        // ---------------- stat helpers ----------------

        private static int GetStat(PartyMember pm, int index)
        {
            switch (index)
            {
                case 0: return pm.devStatBonus.attack;
                case 1: return pm.devStatBonus.defense;
                case 2: return pm.devStatBonus.magic;
                case 3: return pm.devStatBonus.resistance;
                case 4: return pm.devStatBonus.speed;
                case 5: return pm.devStatBonus.maxHP;
                default: return pm.devStatBonus.maxMP;
            }
        }

        private static void SetStat(PartyMember pm, int index, int value)
        {
            switch (index)
            {
                case 0: pm.devStatBonus.attack = value; break;
                case 1: pm.devStatBonus.defense = value; break;
                case 2: pm.devStatBonus.magic = value; break;
                case 3: pm.devStatBonus.resistance = value; break;
                case 4: pm.devStatBonus.speed = value; break;
                case 5: pm.devStatBonus.maxHP = value; break;
                default: pm.devStatBonus.maxMP = value; break;
            }
        }

#if UNITY_EDITOR
        private static List<EquipmentData> FindAllEquipment()
        {
            var list = new List<EquipmentData>();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EquipmentData");
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                EquipmentData eq = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(path);
                if (eq != null) list.Add(eq);
            }
            return list;
        }

        private static List<ItemData> FindAllItems()
        {
            var list = new List<ItemData>();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemData");
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                ItemData item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (item != null) list.Add(item);
            }
            return list;
        }

        private static List<CharacterData> FindAllCharacters()
        {
            var list = new List<CharacterData>();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CharacterData");
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                CharacterData cd = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterData>(path);
                if (cd != null) list.Add(cd);
            }
            return list;
        }

        private static string GetItemSummary(ItemData item)
        {
            if (item == null) return "";
            if (item.revive) return "  (revive)";
            if (item.healHP > 0) return "  (HP+" + item.healHP + ")";
            if (item.healMP > 0) return "  (MP+" + item.healMP + ")";
            if (item.damagePower > 0) return "  (dmg " + item.damagePower + ")";
            return "";
        }
#endif

        private static void ForEachAlive(List<BattleUnit> units, System.Action<BattleUnit> action)
        {
            foreach (BattleUnit u in units)
                if (u != null && u.Stats != null && u.Stats.IsAlive) action(u);
        }

        // ---------------- small UI helpers ----------------

        private void AddHeader(string text) { MakeLabel(content, text, 24, FontStyles.Bold, 34, new Color(1f, 0.85f, 0.4f)); }
        private void AddLabel(string text) { MakeLabel(content, text, 16, FontStyles.Normal, 24, new Color(0.72f, 0.76f, 0.84f)); }
        private void AddButton(string label, float height, UnityEngine.Events.UnityAction onClick)
        {
            MakeButton(content, label, height, onClick);
        }

        private Button MakeButton(Transform parent, string label, float height, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.19f, 0.28f, 0.95f);
            Button btn = go.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            TextMeshProUGUI t = MakeLabel(go.transform, label, Mathf.Max(13, (int)(height - 12)), FontStyles.Normal, height, Color.white);
            if (t != null) { t.alignment = TextAlignmentOptions.Center; StretchFill(t.rectTransform); }
            return btn;
        }

        private TextMeshProUGUI MakeLabel(Transform parent, string text, int size, FontStyles style, float height, Color color)
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogError("DevConsole: TMP default font missing — import TMP Essential Resources.");
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
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            return t;
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
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }
    }
}