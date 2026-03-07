using Moffat.EndlessOnline.SDK.Protocol.Pub;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SOE_PubEditor.Models;

/// <summary>
/// Wrapper for EifRecord that adds an Id property (1-based index in the collection)
/// </summary>
public class ItemRecordWrapper : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    public int Id { get; set; }
    public EifRecord Record { get; }
    
    // Forwarded properties for DataGrid binding - include setters for editable fields
    public string Name
    {
        get => Record.Name;
        set => Record.Name = value;
    }
    public int GraphicId
    {
        get => Record.GraphicId;
        set => Record.GraphicId = value;
    }
    public ItemType Type
    {
        get => Record.Type;
        set
        {
            if (Record.Type != value)
            {
                Record.Type = value;
                OnPropertyChanged();
            }
        }
    }
    public ItemSubtype Subtype
    {
        get => Record.Subtype;
        set => Record.Subtype = value;
    }
    public ItemSpecial Special
    {
        get => Record.Special;
        set => Record.Special = value;
    }
    public int Hp
    {
        get => Record.Hp;
        set => Record.Hp = value;
    }
    public int Tp
    {
        get => Record.Tp;
        set => Record.Tp = value;
    }
    public int MinDamage
    {
        get => Record.MinDamage;
        set => Record.MinDamage = value;
    }
    public int MaxDamage
    {
        get => Record.MaxDamage;
        set => Record.MaxDamage = value;
    }
    public int Accuracy
    {
        get => Record.Accuracy;
        set => Record.Accuracy = value;
    }
    public int Evade
    {
        get => Record.Evade;
        set => Record.Evade = value;
    }
    public int Armor
    {
        get => Record.Armor;
        set => Record.Armor = value;
    }
    public int ReturnDamage
    {
        get => Record.ReturnDamage;
        set => Record.ReturnDamage = value;
    }
    public int Str
    {
        get => Record.Str;
        set => Record.Str = value;
    }
    public int Intl
    {
        get => Record.Intl;
        set => Record.Intl = value;
    }
    public int Wis
    {
        get => Record.Wis;
        set => Record.Wis = value;
    }
    public int Agi
    {
        get => Record.Agi;
        set => Record.Agi = value;
    }
    public int Con
    {
        get => Record.Con;
        set => Record.Con = value;
    }
    public int Cha
    {
        get => Record.Cha;
        set => Record.Cha = value;
    }
    public int LevelRequirement
    {
        get => Record.LevelRequirement;
        set => Record.LevelRequirement = value;
    }
    public int ClassRequirement
    {
        get => Record.ClassRequirement;
        set => Record.ClassRequirement = value;
    }
    public int StrRequirement
    {
        get => Record.StrRequirement;
        set => Record.StrRequirement = value;
    }
    public int IntRequirement
    {
        get => Record.IntRequirement;
        set => Record.IntRequirement = value;
    }
    public int WisRequirement
    {
        get => Record.WisRequirement;
        set => Record.WisRequirement = value;
    }
    public int AgiRequirement
    {
        get => Record.AgiRequirement;
        set => Record.AgiRequirement = value;
    }
    public int ConRequirement
    {
        get => Record.ConRequirement;
        set => Record.ConRequirement = value;
    }
    public int ChaRequirement
    {
        get => Record.ChaRequirement;
        set => Record.ChaRequirement = value;
    }
    public int Weight
    {
        get => Record.Weight;
        set => Record.Weight = value;
    }
    public ItemSize Size
    {
        get => Record.Size;
        set => Record.Size = value;
    }
    // Multi-purpose Spec fields (contains DollGraphic for equipment, ScrollMap for scrolls, etc.)
    public int Spec1
    {
        get => Record.Spec1;
        set => Record.Spec1 = value;
    }
    public int Spec2
    {
        get => Record.Spec2;
        set => Record.Spec2 = value;
    }
    public int Spec3
    {
        get => Record.Spec3;
        set => Record.Spec3 = value;
    }
    
    /// <summary>
    /// Returns true if this equipment is for female characters.
    /// Based on Spec2 value (0 = male/default, 1 = female) or name patterns.
    /// </summary>
    public bool IsFemaleEquipment
    {
        get
        {
            // Spec2 == 1 indicates female equipment, Spec2 == 0 is male (default)
            if (Spec2 == 1) return true;
            if (Spec2 == 0) return false;
            
            // Also check name patterns as fallback for items without Spec2 set correctly
            var name = Name ?? "";
            return name.Contains("(F)") || 
                   name.Contains("Female") || 
                   name.Contains(" F)") ||
                   name.EndsWith(" F");
        }
    }
    
    /// <summary>
    /// Gets a display string for the equipment gender.
    /// </summary>
    public string GenderDisplay => IsFemaleEquipment ? "Female" : "Male";
    
    public ItemRecordWrapper(EifRecord record, int id)
    {
        Record = record;
        Id = id;
    }
}

/// <summary>
/// Wrapper for EnfRecord that adds an Id property (1-based index in the collection)
/// </summary>
public class NpcRecordWrapper
{
    public int Id { get; set; }
    public EnfRecord Record { get; }
    
    // Forwarded properties for DataGrid binding - include setters for editable fields
    public string Name
    {
        get => Record.Name;
        set => Record.Name = value;
    }
    public int GraphicId
    {
        get => Record.GraphicId;
        set => Record.GraphicId = value;
    }
    public NpcType Type
    {
        get => Record.Type;
        set => Record.Type = value;
    }
    public int Race
    {
        get => Record.Race;
        set => Record.Race = value;
    }
    public int BehaviorId
    {
        get => Record.BehaviorId;
        set => Record.BehaviorId = value;
    }
    public int Hp
    {
        get => Record.Hp;
        set => Record.Hp = value;
    }
    public int Tp
    {
        get => Record.Tp;
        set => Record.Tp = value;
    }
    public int MinDamage
    {
        get => Record.MinDamage;
        set => Record.MinDamage = value;
    }
    public int MaxDamage
    {
        get => Record.MaxDamage;
        set => Record.MaxDamage = value;
    }
    public int Accuracy
    {
        get => Record.Accuracy;
        set => Record.Accuracy = value;
    }
    public int Evade
    {
        get => Record.Evade;
        set => Record.Evade = value;
    }
    public int Armor
    {
        get => Record.Armor;
        set => Record.Armor = value;
    }
    public int ReturnDamage
    {
        get => Record.ReturnDamage;
        set => Record.ReturnDamage = value;
    }
    public int Experience
    {
        get => Record.Experience;
        set => Record.Experience = value;
    }
    public Element ElementWeakness
    {
        get => Record.ElementWeakness;
        set => Record.ElementWeakness = value;
    }
    public int ElementWeaknessDamage
    {
        get => Record.ElementWeaknessDamage;
        set => Record.ElementWeaknessDamage = value;
    }
    public bool Boss
    {
        get => Record.Boss;
        set => Record.Boss = value;
    }
    public bool Child
    {
        get => Record.Child;
        set => Record.Child = value;
    }
    
    public NpcRecordWrapper(EnfRecord record, int id)
    {
        Record = record;
        Id = id;
    }
}

/// <summary>
/// Wrapper for EsfRecord that adds an Id property (1-based index in the collection)
/// </summary>
public class SpellRecordWrapper
{
    public int Id { get; set; }
    public EsfRecord Record { get; }
    
    // Forwarded properties for DataGrid binding - include setters for editable fields
    public string Name
    {
        get => Record.Name;
        set => Record.Name = value;
    }
    public short GraphicId
    {
        get => (short)Record.GraphicId;
        set => Record.GraphicId = value;
    }
    public SkillType Type
    {
        get => Record.Type;
        set => Record.Type = value;
    }
    public SkillTargetType TargetType
    {
        get => Record.TargetType;
        set => Record.TargetType = value;
    }
    public int TpCost
    {
        get => Record.TpCost;
        set => Record.TpCost = value;
    }
    public int SpCost
    {
        get => Record.SpCost;
        set => Record.SpCost = value;
    }
    public int HpHeal
    {
        get => Record.HpHeal;
        set => Record.HpHeal = value;
    }
    public int TpHeal
    {
        get => Record.TpHeal;
        set => Record.TpHeal = value;
    }
    public int MinDamage
    {
        get => Record.MinDamage;
        set => Record.MinDamage = value;
    }
    public int MaxDamage
    {
        get => Record.MaxDamage;
        set => Record.MaxDamage = value;
    }
    public int Accuracy
    {
        get => Record.Accuracy;
        set => Record.Accuracy = value;
    }
    public int CastTime
    {
        get => Record.CastTime;
        set => Record.CastTime = value;
    }
    public string Chant
    {
        get => Record.Chant;
        set => Record.Chant = value;
    }
    public int IconId
    {
        get => Record.IconId;
        set => Record.IconId = value;
    }
    public SkillTargetRestrict TargetRestrict
    {
        get => Record.TargetRestrict;
        set => Record.TargetRestrict = value;
    }
    
    public SpellRecordWrapper(EsfRecord record, int id)
    {
        Record = record;
        Id = id;
    }
}

/// <summary>
/// Wrapper for EcfRecord that adds an Id property (1-based index in the collection)
/// </summary>
public class ClassRecordWrapper
{
    public int Id { get; set; }
    public EcfRecord Record { get; }
    
    // Forwarded properties for DataGrid binding - include setters for editable fields
    public string Name
    {
        get => Record.Name;
        set => Record.Name = value;
    }
    public int ParentType
    {
        get => Record.ParentType;
        set => Record.ParentType = value;
    }
    public int Str
    {
        get => Record.Str;
        set => Record.Str = value;
    }
    public int Intl
    {
        get => Record.Intl;
        set => Record.Intl = value;
    }
    public int Wis
    {
        get => Record.Wis;
        set => Record.Wis = value;
    }
    public int Agi
    {
        get => Record.Agi;
        set => Record.Agi = value;
    }
    public int Con
    {
        get => Record.Con;
        set => Record.Con = value;
    }
    public int Cha
    {
        get => Record.Cha;
        set => Record.Cha = value;
    }
    
    public ClassRecordWrapper(EcfRecord record, int id)
    {
        Record = record;
        Id = id;
    }
}
