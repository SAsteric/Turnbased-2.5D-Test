using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ItemStack
{
    public ItemData item;
    public int count;
}

[System.Serializable]
public class EquipmentStack
{
    public EquipmentData equipment;
    public int count;
}

[System.Serializable]
public class GameSettings
{
    [Range(0.05f, 1.5f)] public float cameraSmoothTime = 0.25f; // the camera "delay"
    [Range(0f, 1f)] public float masterVolume = 1f;
}

// One persistent party member. Survives battles & scene loads.
public class PartyMember
{
    public CharacterData data;
    public int level;
    public int exp;
    public int currentHP;
    public int currentMP;
    public EquipmentData armor;
    public EquipmentData accessory1;
    public EquipmentData accessory2;
    public bool isActive = true;

    // dev console overrides (runtime only — never saved into assets)
    public StatBlock devStatBonus = new StatBlock();
    public List<SkillData> bonusSkills = new List<SkillData>();
    public List<SkillData> blockedSkills = new List<SkillData>();

    public bool IsSkillActive(SkillData skill)
    {
        if (blockedSkills.Contains(skill)) return false;
        if (bonusSkills.Contains(skill)) return true;
        foreach (LearnableSkill ls in data.learnableSkills)
            if (ls != null && ls.skill == skill) return level >= ls.learnLevel;
        return false;
    }

    public void SetSkillActive(SkillData skill, bool active)
    {
        if (active)
        {
            blockedSkills.Remove(skill);
            bool knownByLevel = false;
            foreach (LearnableSkill ls in data.learnableSkills)
                if (ls != null && ls.skill == skill && level >= ls.learnLevel) { knownByLevel = true; break; }
            if (!knownByLevel && !bonusSkills.Contains(skill)) bonusSkills.Add(skill);
        }
        else
        {
            bonusSkills.Remove(skill);
            if (!blockedSkills.Contains(skill)) blockedSkills.Add(skill);
        }
    }

    public PartyMember(CharacterData d)
    {
        data = d;
        level = d.startingLevel;
        UnitStats s = CreateStats();
        currentHP = s.MaxHP;
        currentMP = s.MaxMP;
    }

    public UnitStats CreateStats()
    {
        UnitStats s = UnitStats.FromCharacter(data, level, armor, accessory1, accessory2);

        // dev console stat bonus
        s.Attack += devStatBonus.attack;
        s.Defense += devStatBonus.defense;
        s.Magic += devStatBonus.magic;
        s.Resistance += devStatBonus.resistance;
        s.Speed += devStatBonus.speed;
        s.MaxHP += devStatBonus.maxHP;
        s.MaxMP += devStatBonus.maxMP;

        // dev console skill overrides
        foreach (SkillData b in bonusSkills) if (b != null && !s.skills.Contains(b)) s.skills.Add(b);
        foreach (SkillData bl in blockedSkills) if (bl != null) s.skills.Remove(bl);
        return s;
    }

    public void SyncFrom(UnitStats s)
    {
        currentHP = s.currentHP;
        currentMP = s.currentMP;
    }

    public int ExpToNext() { return Mathf.RoundToInt(30f * Mathf.Pow(level, 1.35f)); }

    public List<SkillData> AddExp(int amount)
    {
        exp += amount;
        var learned = new List<SkillData>();
        while (exp >= ExpToNext() && level < 99)
        {
            exp -= ExpToNext();
            level++;
            foreach (LearnableSkill ls in data.learnableSkills)
                if (ls != null && ls.skill != null && ls.learnLevel == level)
                    learned.Add(ls.skill);
        }
        return learned;
    }
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Starting Party (first 4 'active' members enter battle)")]
    [SerializeField] private List<CharacterData> startingParty = new List<CharacterData>();
    [SerializeField] private int startingGold = 150;
    [SerializeField] private List<ItemStack> startingItems = new List<ItemStack>();
    [SerializeField] private List<EquipmentStack> startingEquipment = new List<EquipmentStack>();

    [Header("Save registries — right-click this component → Auto-Fill Save Registries (once!)")]
    [SerializeField] private List<CharacterData> allCharacters = new List<CharacterData>();
    [SerializeField] private List<ItemData> allItems = new List<ItemData>();
    [SerializeField] private List<EquipmentData> allEquipment = new List<EquipmentData>();

    [Header("Scene Names — must EXACTLY match names in Build Profiles")]
    public string mainMenuSceneName = "00_MainMenu";
    public string overworldSceneName = "01_Overworld";
    public string battleSceneName = "02_Battle";
    
    [Header("Runtime State")]
    public List<PartyMember> party = new List<PartyMember>();
    public List<ItemStack> items = new List<ItemStack>();
    public List<EquipmentStack> equipmentInventory = new List<EquipmentStack>();
    public GameSettings settings = new GameSettings();
    public int gold = 0;

    // Battle hand-off
    [System.NonSerialized] public Vector3 returnPosition;
    [System.NonSerialized] public bool hasReturnPosition;
    [System.NonSerialized] public string returnScene;
    [System.NonSerialized] public bool inBattle;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (party.Count == 0)
        {
            foreach (CharacterData cd in startingParty)
                if (cd != null) party.Add(new PartyMember(cd));

            gold = startingGold;

            items = new List<ItemStack>();
            foreach (ItemStack s in startingItems)
                if (s != null && s.item != null) items.Add(new ItemStack { item = s.item, count = s.count });

            equipmentInventory = new List<EquipmentStack>();
            foreach (EquipmentStack s in startingEquipment)
                if (s != null && s.equipment != null) equipmentInventory.Add(new EquipmentStack { equipment = s.equipment, count = s.count });
        }

        LoadSettings();
        ApplyVolume();
    }

    public static void EnsureExists()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("GameManager (Runtime)");
            go.AddComponent<GameManager>();
            Debug.LogWarning("GameManager was auto-created with an EMPTY party. Launch the game from the Main Menu scene.");
        }
    }

    // Wipes everything and re-creates the "new game" state from the
    // starting fields you set up on this object in the scene.
    public void ResetToNewGame()
    {
        party.Clear();
        foreach (CharacterData cd in startingParty)
            if (cd != null) party.Add(new PartyMember(cd));

        gold = startingGold;

        items = new List<ItemStack>();
        foreach (ItemStack s in startingItems)
            if (s != null && s.item != null) items.Add(new ItemStack { item = s.item, count = s.count });

        equipmentInventory = new List<EquipmentStack>();
        foreach (EquipmentStack s in startingEquipment)
            if (s != null && s.equipment != null) equipmentInventory.Add(new EquipmentStack { equipment = s.equipment, count = s.count });

        inBattle = false;
        hasReturnPosition = false;
    }
    // ---------- Party ----------
    public List<PartyMember> ActiveParty()
    {
        var list = new List<PartyMember>();
        foreach (PartyMember pm in party) if (pm.isActive) list.Add(pm);
        return list;
    }

    // ---------- Item inventory ----------
    public void AddItem(ItemData item, int count = 1)
    {
        if (item == null) return;
        foreach (ItemStack s in items) if (s.item == item) { s.count += count; return; }
        items.Add(new ItemStack { item = item, count = count });
    }

    public bool ConsumeItem(ItemData item, int count = 1)
    {
        foreach (ItemStack s in items)
        {
            if (s.item != item || s.count < count) continue;
            s.count -= count;
            if (s.count <= 0) items.Remove(s);
            return true;
        }
        return false;
    }

    // ---------- Equipment inventory ----------
    public void AddEquipment(EquipmentData eq, int count = 1)
    {
        if (eq == null) return;
        foreach (EquipmentStack s in equipmentInventory) if (s.equipment == eq) { s.count += count; return; }
        equipmentInventory.Add(new EquipmentStack { equipment = eq, count = count });
    }

    public void RemoveEquipment(EquipmentData eq, int count = 1)
    {
        foreach (EquipmentStack s in equipmentInventory)
        {
            if (s.equipment != eq) continue;
            s.count -= count;
            if (s.count <= 0) equipmentInventory.Remove(s);
            return;
        }
    }

    public List<EquipmentStack> GetEquipmentForSlot(EquipSlot slot)
    {
        var list = new List<EquipmentStack>();
        foreach (EquipmentStack s in equipmentInventory)
            if (s.equipment != null && s.equipment.slot == slot) list.Add(s);
        return list;
    }

    public void Equip(PartyMember member, EquipSlot slot, EquipmentData newEquip, int accessoryIndex = 1)
    {
        if (member == null || newEquip == null || newEquip.slot != slot) return;
        if (slot == EquipSlot.Weapon) return; // signature weapons are locked

        EquipmentData old = GetEquipped(member, slot, accessoryIndex);
        RemoveEquipment(newEquip, 1);
        if (old != null) AddEquipment(old, 1);
        SetEquipped(member, slot, accessoryIndex, newEquip);

        UnitStats s = member.CreateStats();
        member.currentHP = Mathf.Clamp(member.currentHP, 0, s.MaxHP);
        member.currentMP = Mathf.Clamp(member.currentMP, 0, s.MaxMP);
    }

    public bool Unequip(PartyMember member, EquipSlot slot, int accessoryIndex = 1)
    {
        if (member == null || slot == EquipSlot.Weapon) return false;
        EquipmentData old = GetEquipped(member, slot, accessoryIndex);
        if (old == null) return false;
        SetEquipped(member, slot, accessoryIndex, null);
        AddEquipment(old, 1);

        UnitStats s = member.CreateStats();
        member.currentHP = Mathf.Clamp(member.currentHP, 0, s.MaxHP);
        member.currentMP = Mathf.Clamp(member.currentMP, 0, s.MaxMP);
        return true;
    }

    public EquipmentData GetEquipped(PartyMember member, EquipSlot slot, int accessoryIndex = 1)
    {
        if (member == null) return null;
        if (slot == EquipSlot.Weapon) return member.data.signatureWeapon;
        if (slot == EquipSlot.Armor) return member.armor;
        return accessoryIndex == 1 ? member.accessory1 : member.accessory2;
    }

    private static void SetEquipped(PartyMember member, EquipSlot slot, int accessoryIndex, EquipmentData eq)
    {
        if (slot == EquipSlot.Armor) member.armor = eq;
        else if (accessoryIndex == 1) member.accessory1 = eq;
        else member.accessory2 = eq;
    }

    public CharacterData FindCharacter(string name)
    {
        foreach (CharacterData c in allCharacters) if (c != null && c.characterName == name) return c;
        return null;
    }
    public ItemData FindItem(string name)
    {
        foreach (ItemData i in allItems) if (i != null && i.itemName == name) return i;
        return null;
    }
    public EquipmentData FindEquipment(string name)
    {
        foreach (EquipmentData e in allEquipment) if (e != null && e.equipName == name) return e;
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-Fill Save Registries")]
    private void AutoFillRegistries()
    {
        allCharacters = LoadAllAssets<CharacterData>();
        allItems = LoadAllAssets<ItemData>();
        allEquipment = LoadAllAssets<EquipmentData>();
        Debug.Log("Save registries filled: " + allCharacters.Count + " characters, "
            + allItems.Count + " items, " + allEquipment.Count + " equipment.");
        UnityEditor.EditorUtility.SetDirty(this);
    }
    private static List<T> LoadAllAssets<T>() where T : UnityEngine.Object
    {
        var list = new List<T>();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name);
        foreach (string g in guids)
        {
            string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            T a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(p);
            if (a != null) list.Add(a);
        }
        return list;
    }
#endif

    // ---------- Settings ----------
    public void ApplyVolume() { AudioListener.volume = settings.masterVolume; }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("RPG_CameraSmooth", settings.cameraSmoothTime);
        PlayerPrefs.SetFloat("RPG_Volume", settings.masterVolume);
        PlayerPrefs.Save();
    }

    private void LoadSettings()
    {
        if (PlayerPrefs.HasKey("RPG_CameraSmooth")) settings.cameraSmoothTime = PlayerPrefs.GetFloat("RPG_CameraSmooth");
        if (PlayerPrefs.HasKey("RPG_Volume")) settings.masterVolume = PlayerPrefs.GetFloat("RPG_Volume");
    }
}