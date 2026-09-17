using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class BattleUIController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject commandPanel;
    public GameObject skillPanel;
    public GameObject itemPanel;
    public GameObject targetPanel;
    public GameObject resultPanel;

    [Header("Command Buttons")]
    public Button attackButton;
    public Button skillButton;
    public Button defendButton;
    public Button itemButton;

    [Header("Descriptions")]
    public TextMeshProUGUI commandDescriptionText;
    public TextMeshProUGUI skillDescriptionText;
    public TextMeshProUGUI itemDescriptionText;

    [Header("List Panels")]
    public Transform skillListParent;
    public Transform itemListParent;
    public Button listButtonPrefab;

    [Header("Targeting")]
    public TextMeshProUGUI targetLabel;

    [Header("Readouts")]
    public TextMeshProUGUI battleLog;
    public TextMeshProUGUI turnOrderText;

    [Header("Result")]
    public TextMeshProUGUI resultTitle;
    public TextMeshProUGUI resultBody;
    public Button resultContinueButton;

    [Header("Damage Popups")]
    public TextMeshProUGUI damageTextPrefab;

    public BattleAction LastAction { get; private set; }

    private enum SubState { None, Command, SkillList, ItemList, Targeting }
    private SubState state = SubState.None;

    private BattleUnit currentUnit;
    private List<BattleUnit> players = new List<BattleUnit>();
    private List<BattleUnit> enemies = new List<BattleUnit>();

    private ActionType pendingType;
    private SkillData pendingSkill;
    private ItemData pendingItem;
    private TargetSide pendingSide;
    private bool pendingAllowDead;

    private readonly List<string> logLines = new List<string>();
    private const int MaxLogLines = 7;

    private Camera worldCam;
    private BattleUnit hoveredUnit;

    private void Awake()
    {
        if (attackButton != null) attackButton.onClick.AddListener(OnAttackClicked);
        if (skillButton != null) skillButton.onClick.AddListener(OnSkillClicked);
        if (defendButton != null) defendButton.onClick.AddListener(OnDefendClicked);
        if (itemButton != null) itemButton.onClick.AddListener(OnItemClicked);
        if (resultContinueButton != null)
            resultContinueButton.onClick.AddListener(() =>
            {
                if (BattleManager.Instance != null) BattleManager.Instance.ReturnToOverworld();
            });

        BindCommandDescriptions();
        HideAllPanels();
        SetBattleLogVisible(false); // hidden by default — the dev console toggles it
    }

    private void BindCommandDescriptions()
    {
        BindHover(attackButton, "Strike one enemy with your signature weapon.");
        BindHover(skillButton, "Use a learned skill. Most skills cost MP.");
        BindHover(defendButton, "Go on the defensive to reduce damage taken until your next turn.");
        BindHover(itemButton, "Use an item from the party's bag.");
    }

    private void BindHover(Button btn, string description)
    {
        if (btn == null) return;
        HoverDescription h = btn.gameObject.GetComponent<HoverDescription>();
        if (h == null) h = btn.gameObject.AddComponent<HoverDescription>();
        h.Bind(description, s => { if (commandDescriptionText != null) commandDescriptionText.text = s; });
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool cancel = (kb != null && kb.escapeKey.wasPressedThisFrame)
                   || (mouse != null && mouse.rightButton.wasPressedThisFrame);
        if (cancel) CancelCurrent();

        HandleWorldPointer(mouse);   // hover always active (HP bars); clicks are gated inside
    }

    // ---------------- WORLD targeting (raycast clicks + hover) ----------------

    private void HandleWorldPointer(Mouse mouse)
    {
        if (mouse == null) return;
        if (worldCam == null) worldCam = Camera.main;
        if (worldCam == null) return;

        // clicks that land on UI (panels) must not pierce into the world
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // HOVER (any time): first unit under the pointer — outline, HP bar, name.
        // While targeting, only VALID targets get the hover highlight.
        BattleUnit hoverCandidate = overUI ? null : FirstUnitUnderPointer(mouse.position.ReadValue());
        if (hoverCandidate != null)
        {
            if (state == SubState.Targeting)
            {
                // while targeting: only VALID targets get hover feedback
                List<BattleUnit> validNow = GetValidTargets(pendingSide, pendingAllowDead);
                if (!validNow.Contains(hoverCandidate)) hoverCandidate = null;
            }
            else if (hoverCandidate.IsPlayerSide)
            {
                // outside targeting, hover exists to inspect ENEMIES (HP bar,
                // name) — your own party must never light up from an idle cursor
                hoverCandidate = null;
            }
        }
        if (hoverCandidate != hoveredUnit)
        {
            if (hoveredUnit != null) hoveredUnit.SetHover(false);
            hoveredUnit = hoverCandidate;
            if (hoveredUnit != null) hoveredUnit.SetHover(true);
        }

        // CLICK (only while targeting, and only VALID targets).
        if (state == SubState.Targeting && !overUI && mouse.leftButton.wasPressedThisFrame)
        {
            BattleUnit clicked = FirstValidUnderPointer(mouse.position.ReadValue());
            if (clicked != null) OnUnitClicked(clicked);
        }
    }

    private BattleUnit FirstUnitUnderPointer(Vector2 screenPos)
    {
        List<BattleUnit> ordered = GetUnitsUnderPointer(screenPos);
        return ordered.Count > 0 ? ordered[0] : null;
    }

    private BattleUnit FirstValidUnderPointer(Vector2 screenPos)
    {
        List<BattleUnit> ordered = GetUnitsUnderPointer(screenPos);
        List<BattleUnit> valid = GetValidTargets(pendingSide, pendingAllowDead);
        foreach (BattleUnit u in ordered)
            if (valid.Contains(u)) return u;
        return null;
    }

    private List<BattleUnit> GetUnitsUnderPointer(Vector2 screenPos)
    {
        var result = new List<BattleUnit>();
        Ray ray = worldCam.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            BattleUnit u = h.collider.GetComponentInParent<BattleUnit>();
            if (u != null && !result.Contains(u)) result.Add(u);
        }
        return result;
    }

    // ---------------- main entry ----------------

    public IEnumerator WaitForPlayerCommand(BattleUnit unit, List<BattleUnit> playerUnits, List<BattleUnit> enemyUnits)
    {
        currentUnit = unit;
        players = playerUnits;
        enemies = enemyUnits;
        LastAction = null;

        unit.SetHighlight(true);
        yield return unit.StepToActionSpot(BattleManager.Instance.GetActionSpot(unit));
        unit.SetActiveTurn(true);   // plays IdleActive while choosing
        if (BattleCameraController.Instance != null)
            BattleCameraController.Instance.FocusUnit(unit); // re-aim after the step

        PositionCommandPanelNear(unit);
        if (BattlePartyHUD.Instance != null) BattlePartyHUD.Instance.SetActive(unit);
        if (commandDescriptionText != null)
            commandDescriptionText.text = "Choose an action for " + unit.Stats.unitName + ".";

        EnterCommandState();
        Log("<b>" + unit.Stats.unitName + "</b>: choose a command.");

        while (LastAction == null) yield return null;

        // The unit STAYS stepped forward — its action executes from there,
        // and ExecuteAction's single StepBack returns it home afterwards.
        // (The chooser's outline also stays on until the action completes.)
        ClearAllHighlights();
        HideAllPanels();
        if (BattlePartyHUD.Instance != null) BattlePartyHUD.Instance.SetActive(null);
        unit.SetActiveTurn(false);

        state = SubState.None;
        currentUnit = null;
        pendingSkill = null;
        pendingItem = null;
    }

    private void PositionCommandPanelNear(BattleUnit unit)
    {
        if (commandPanel == null || unit == null) return;
        if (worldCam == null) worldCam = Camera.main;
        if (worldCam == null) return;

        RectTransform panel = commandPanel.transform as RectTransform;
        RectTransform root = panel.parent as RectTransform;
        if (root == null) return;

        // project the unit's chest into canvas space
        Vector3 screen = worldCam.WorldToScreenPoint(unit.FocusPoint);
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local);

        float halfW = panel.rect.width * 0.5f;
        float halfH = panel.rect.height * 0.5f;
        Vector2 pos = local + new Vector2(halfW + 90f, 0f);

        Rect r = root.rect;
        pos.x = Mathf.Clamp(pos.x, r.xMin + halfW + 8f, r.xMax - halfW - 8f);
        pos.y = Mathf.Clamp(pos.y, r.yMin + halfH + 120f, r.yMax - halfH - 8f);
        panel.anchoredPosition = pos;
    }

    private void EnterCommandState()
    {
        state = SubState.Command;
        HideAllPanels();
        if (commandPanel != null) commandPanel.SetActive(true);
        if (currentUnit != null) currentUnit.SetHighlight(true);   // panel open = deciding = outlined
    }

    // ---------------- button handlers ----------------

    private void OnAttackClicked()
    {
        if (state != SubState.Command || currentUnit == null) return;
        currentUnit.SetHighlight(false);   // command selected — outline off
        pendingType = ActionType.Attack;
        pendingSkill = null;
        pendingItem = null;
        BeginTargeting(TargetSide.Enemies, false);
    }

    private void OnDefendClicked()
    {
        if (state != SubState.Command || currentUnit == null) return;
        currentUnit.SetHighlight(false);   // command selected — outline off
        FinishAction(new BattleAction
        {
            actor = currentUnit,
            type = ActionType.Defend,
            targets = new List<BattleUnit>()
        });
    }

    private void OnSkillClicked()
    {
        if (state != SubState.Command || currentUnit == null) return;
        currentUnit.SetHighlight(false);   // command selected — outline off
        BuildSkillList();
        state = SubState.SkillList;
        HideAllPanels();
        if (skillPanel != null) skillPanel.SetActive(true);
        if (skillDescriptionText != null) skillDescriptionText.text = "Select a skill.";
    }

    private void OnItemClicked()
    {
        if (state != SubState.Command || currentUnit == null) return;
        currentUnit.SetHighlight(false);   // command selected — outline off
        BuildItemList();
        state = SubState.ItemList;
        HideAllPanels();
        if (itemPanel != null) itemPanel.SetActive(true);
        if (itemDescriptionText != null) itemDescriptionText.text = "Select an item.";
    }

    private void OnSkillSelected(SkillData skill)
    {
        if (state != SubState.SkillList || currentUnit == null) return;
        if (currentUnit.Stats.currentMP < skill.mpCost) return;

        pendingType = ActionType.Skill;
        pendingSkill = skill;
        pendingItem = null;

        switch (skill.scope)
        {
            case TargetScope.Self:
                FinishAction(SkillOn(skill, new List<BattleUnit> { currentUnit }));
                break;
            case TargetScope.AllEnemies:
                FinishAction(SkillOn(skill, GetValidTargets(TargetSide.Enemies, false)));
                break;
            case TargetScope.AllAllies:
                FinishAction(SkillOn(skill, GetValidTargets(TargetSide.Allies, false)));
                break;
            default:
                BeginTargeting(skill.side, false);
                break;
        }
    }

    private void OnItemSelected(ItemData item)
    {
        if (state != SubState.ItemList || currentUnit == null) return;
        pendingType = ActionType.Item;
        pendingItem = item;
        pendingSkill = null;

        if (item.revive) { BeginTargeting(TargetSide.Allies, true); return; }

        if (item.damagePower > 0)
        {
            if (item.scope == TargetScope.AllEnemies)
            { FinishAction(ItemOn(item, GetValidTargets(TargetSide.Enemies, false))); return; }
            BeginTargeting(TargetSide.Enemies, false);
            return;
        }

        if (item.scope == TargetScope.AllAllies)
        { FinishAction(ItemOn(item, GetValidTargets(TargetSide.Allies, false))); return; }
        BeginTargeting(TargetSide.Allies, false);
    }

    // ---------------- list building ----------------

    private void BuildSkillList()
    {
        ClearChildren(skillListParent);
        foreach (SkillData skill in currentUnit.Stats.skills)
        {
            SkillData captured = skill;
            Button btn = Instantiate(listButtonPrefab, skillListParent);
            SetListButton(btn, skill.skillName,
                skill.mpCost > 0 ? skill.mpCost + " MP" : "",
                skill.mpCost <= currentUnit.Stats.currentMP,
                captured.description, skillDescriptionText);
            btn.onClick.AddListener(() => OnSkillSelected(captured));
        }
        if (currentUnit.Stats.skills.Count == 0)
        {
            Button b = Instantiate(listButtonPrefab, skillListParent);
            SetListButton(b, "No skills learned", "", false, "", skillDescriptionText);
        }
    }

    private void BuildItemList()
    {
        ClearChildren(itemListParent);
        bool any = false;
        foreach (ItemStack stack in GameManager.Instance.items)
        {
            if (stack == null || stack.item == null || stack.count <= 0) continue;
            any = true;
            ItemData captured = stack.item;
            Button btn = Instantiate(listButtonPrefab, itemListParent);

            string sub = stack.count + "x";
            if (captured.healHP > 0) sub += "  HP+" + captured.healHP;
            if (captured.healMP > 0) sub += "  MP+" + captured.healMP;
            if (captured.revive) sub += "  Revive";
            if (captured.damagePower > 0) sub += "  Dmg " + captured.damagePower;
            SetListButton(btn, captured.itemName, sub, true, captured.description, itemDescriptionText);
            btn.onClick.AddListener(() => OnItemSelected(captured));
        }
        if (!any)
        {
            Button b = Instantiate(listButtonPrefab, itemListParent);
            SetListButton(b, "No items", "", false, "", itemDescriptionText);
        }
    }

    // ---------------- targeting ----------------

    private void BeginTargeting(TargetSide side, bool allowDead)
    {
        pendingSide = side;
        pendingAllowDead = allowDead;
        state = SubState.Targeting;
        HideAllPanels();
        if (targetPanel != null) targetPanel.SetActive(true);
        if (targetLabel != null)
        {
            string who = side == TargetSide.Enemies ? "Select an enemy (click it in the arena)"
                : allowDead ? "Select a fallen ally" : "Select an ally";
            targetLabel.text = who + "    (Right-click / Esc = back)";
        }
        // No broadcast highlight — targets are indicated by the hover
        // outline / HP bar / name only.
        PanCameraToTargets(side, allowDead);
    }

    // Camera choreography: enemy targets -> frame the enemy side;
    // ally targets (heal/buff/revive) -> frame the whole party.
    private void PanCameraToTargets(TargetSide side, bool allowDead)
    {
        BattleCameraController cam = BattleCameraController.Instance;
        if (cam == null) return;

        List<BattleUnit> valid = GetValidTargets(side, allowDead);
        List<BattleUnit> pool = valid.Count > 0 ? valid
            : (side == TargetSide.Enemies ? enemies : players);

        Vector3 centroid = Vector3.zero;
        int count = 0;
        foreach (BattleUnit u in pool)
            if (u != null && u.Stats != null) { centroid += u.FocusPoint; count++; }
        if (count == 0) return;

        cam.FocusGroup(centroid / count);
    }

    public void OnUnitClicked(BattleUnit unit)
    {
        if (state != SubState.Targeting || unit == null) return;
        List<BattleUnit> valid = GetValidTargets(pendingSide, pendingAllowDead);
        if (!valid.Contains(unit)) return;

        var action = new BattleAction
        {
            actor = currentUnit,
            type = pendingType,
            targets = new List<BattleUnit> { unit }
        };
        if (pendingType == ActionType.Skill) action.skill = pendingSkill;
        if (pendingType == ActionType.Item) action.item = pendingItem;
        FinishAction(action);
    }

    private List<BattleUnit> GetValidTargets(TargetSide side, bool allowDead)
    {
        List<BattleUnit> pool = side == TargetSide.Enemies ? enemies : players;
        var result = new List<BattleUnit>();
        foreach (BattleUnit u in pool)
            if (allowDead ? !u.Stats.IsAlive : u.Stats.IsAlive) result.Add(u);
        return result;
    }

    private void CancelCurrent()
    {
        if (state == SubState.Targeting)
        {
            ClearAllHighlights();
            if (hoveredUnit != null) { hoveredUnit.SetHover(false); hoveredUnit = null; }
            if (currentUnit != null && BattleCameraController.Instance != null)
                BattleCameraController.Instance.FocusUnit(currentUnit);

            // Return to the list we came from. The handlers guard on
            // state == Command, so set it FIRST — otherwise they silently
            // return and the player gets stuck in targeting.
            if (pendingType == ActionType.Skill)
            {
                state = SubState.Command;
                OnSkillClicked();   // reopens the skill list
            }
            else if (pendingType == ActionType.Item)
            {
                state = SubState.Command;
                OnItemClicked();    // reopens the item list
            }
            else
            {
                EnterCommandState();
            }
        }
        else if (state == SubState.SkillList || state == SubState.ItemList)
        {
            EnterCommandState();
        }
    }

    private void FinishAction(BattleAction action)
    {
        ClearAllHighlights();
        if (hoveredUnit != null) { hoveredUnit.SetHover(false); hoveredUnit = null; }
        HideAllPanels();
        LastAction = action;
        state = SubState.None;
    }

    // ---------------- readouts & FX ----------------

    public void HideAllPanels()
    {
        if (commandPanel != null) commandPanel.SetActive(false);
        if (skillPanel != null) skillPanel.SetActive(false);
        if (itemPanel != null) itemPanel.SetActive(false);
        if (targetPanel != null) targetPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        // Any panel closing = this unit is no longer deciding — the turn
        // outline dies here, guaranteed, in ONE place. (EnterCommandState
        // re-lights it whenever the command panel reopens.)
        if (currentUnit != null) currentUnit.SetHighlight(false);
    }

    // ---------------- battle log visibility (dev console controls this) ----------------

    public void SetBattleLogVisible(bool visible)
    {
        if (battleLog != null) battleLog.gameObject.SetActive(visible);
    }

    public bool BattleLogVisible
    {
        get { return battleLog != null && battleLog.gameObject.activeSelf; }
    }

    public bool ToggleBattleLog()
    {
        SetBattleLogVisible(!BattleLogVisible);
        return BattleLogVisible;
    }

    /// Dev console: instantly resolve a pending command selection (used by force win/lose).
    public void DevResolvePendingCommand()
    {
        if (currentUnit != null && LastAction == null)
        {
            FinishAction(new BattleAction
            {
                actor = currentUnit,
                type = ActionType.Defend,
                targets = new List<BattleUnit>()
            });
        }
    }
    private void ClearAllHighlights()
    {
        // skips the active chooser — their outline stays until their action ends
        foreach (BattleUnit u in players) if (u != null && u != currentUnit) u.SetHighlight(false);
        foreach (BattleUnit u in enemies) if (u != null) u.SetHighlight(false);
    }

    public void Log(string line)
    {
        logLines.Add(line);
        while (logLines.Count > MaxLogLines) logLines.RemoveAt(0);
        if (battleLog != null) battleLog.text = string.Join("\n", logLines);
    }

    // ---------------- turn queue display (FFX-style unit order) ----------------

    private readonly List<BattleUnit> turnQueue = new List<BattleUnit>();
    private BattleUnit currentTurnUnit;
    private int roundNumber;

    public void ShowTurnQueue(List<BattleUnit> queue, int round)
    {
        turnQueue.Clear();
        if (queue != null) turnQueue.AddRange(queue);
        roundNumber = round;
        currentTurnUnit = null;
        RenderTurnQueue();
    }

    public void SetCurrentTurn(BattleUnit unit)
    {
        currentTurnUnit = unit;
        RenderTurnQueue();
    }

    private void RenderTurnQueue()
    {
        if (turnOrderText == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("— Round " + roundNumber + " —");
        int num = 0;
        foreach (BattleUnit u in turnQueue)
        {
            if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
            num++;
            string marker = (u == currentTurnUnit) ? "> " : "  ";
            string boost = (u.PriorityBoost > 0) ? "  [+" + u.PriorityBoost + "]" : "";
            sb.AppendLine(marker + num + ". " + u.Stats.unitName + boost);
        }
        turnOrderText.text = sb.ToString();
    }

    public void SpawnDamageText(BattleUnit unit, string text, Color color)
    {
        if (damageTextPrefab == null || unit == null) return;
        if (worldCam == null) worldCam = Camera.main;
        if (worldCam == null) return;

        TextMeshProUGUI t = Instantiate(damageTextPrefab, transform);
        t.transform.SetAsLastSibling();

        // popup is anchored to a WORLD point and re-projected every frame —
        // it stays glued to the unit through pans, drift and shake
        // Random horizontal offset so multi-hit popups don't stack on each other
        Vector3 worldAnchor = unit.FocusPoint + Vector3.up * 0.6f
            + Vector3.right * Random.Range(-0.35f, 0.35f);
        t.transform.position = worldCam.WorldToScreenPoint(worldAnchor);
        t.text = text;
        t.color = color;
        StartCoroutine(FadeDamageText(t, worldAnchor));
    }

    private IEnumerator FadeDamageText(TextMeshProUGUI t, Vector3 worldAnchor)
    {
        float time = 0f, duration = 0.9f;
        while (time < duration)
        {
            time += Time.deltaTime;
            if (worldCam != null)
                t.transform.position = worldCam.WorldToScreenPoint(worldAnchor)
                    + Vector3.up * (45f * (time / duration));
            t.alpha = 1f - (time / duration);
            yield return null;
        }
        Destroy(t.gameObject);
    }

    public void ShowResult(bool isVictory, string body)
    {
        HideAllPanels();
        if (BattlePartyHUD.Instance != null) BattlePartyHUD.Instance.SetActive(null);
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultTitle != null)
        {
            resultTitle.text = isVictory ? "VICTORY" : "DEFEAT";
            resultTitle.color = isVictory ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.4f);
        }
        if (resultBody != null) resultBody.text = body;
    }

    // ---------------- helpers ----------------

    private BattleAction SkillOn(SkillData skill, List<BattleUnit> targets)
    {
        return new BattleAction { actor = currentUnit, type = ActionType.Skill, skill = skill, targets = targets };
    }

    private BattleAction ItemOn(ItemData item, List<BattleUnit> targets)
    {
        return new BattleAction { actor = currentUnit, type = ActionType.Item, item = item, targets = targets };
    }

    private static void SetListButton(Button btn, string label, string sub, bool interactable,
                                      string description, TextMeshProUGUI descriptionTarget)
    {
        if (btn == null) return;
        btn.interactable = interactable;
        var l = btn.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (l != null) l.text = label;
        var s = btn.transform.Find("Cost")?.GetComponent<TextMeshProUGUI>();
        if (s != null) s.text = sub;

        if (descriptionTarget != null)
        {
            HoverDescription h = btn.gameObject.GetComponent<HoverDescription>();
            if (h == null) h = btn.gameObject.AddComponent<HoverDescription>();
            h.Bind(description, txt => descriptionTarget.text = txt);
        }
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
    }
}