using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moffat.EndlessOnline.SDK.Protocol.Pub;
using SOE_PubEditor.Models;
using SOE_PubEditor.Services;

namespace SOE_PubEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IPubFileService _pubFileService;
    private readonly IGfxService _gfxService;
    private readonly AppSettings _settings;
    
    [ObservableProperty]
    private string _statusText = "Ready";
    
    [ObservableProperty]
    private string? _pubDirectory;
    
    [ObservableProperty]
    private string? _gfxDirectory;
    
    [ObservableProperty]
    private string? _saveDirectory;
    
    [ObservableProperty]
    private bool _enablePubSplitting = true;
    
    [ObservableProperty]
    private int _maxEntriesPerFile = 900;
    
    [ObservableProperty]
    private int _selectedTabIndex;
    
    [ObservableProperty]
    private bool _hasUnsavedChanges;
    
    // Pub file data - using ObservableCollections for binding
    private Eif? _itemsFileData;
    private Enf? _npcsFileData;
    private Esf? _spellsFileData;
    private Ecf? _classesFileData;
    
    [ObservableProperty]
    private ObservableCollection<ItemRecordWrapper> _items = new();
    
    [ObservableProperty]
    private ObservableCollection<NpcRecordWrapper> _npcs = new();
    
    [ObservableProperty]
    private ObservableCollection<SpellRecordWrapper> _spells = new();
    
    [ObservableProperty]
    private ObservableCollection<ClassRecordWrapper> _classes = new();
    
    // Type options for filter dropdowns (nullable to allow "All" selection via PlaceholderText)
    public ItemType[] ItemTypeFilters => Enum.GetValues<ItemType>();
    public NpcType[] NpcTypeFilters => Enum.GetValues<NpcType>();
    public SkillType[] SpellTypeFilters => Enum.GetValues<SkillType>();
    
    // Type filters - null means "All"
    [ObservableProperty]
    private ItemType? _selectedItemTypeFilter;
    
    [ObservableProperty]
    private NpcType? _selectedNpcTypeFilter;
    
    [ObservableProperty]
    private SkillType? _selectedSpellTypeFilter;
    
    // Search filters
    [ObservableProperty]
    private string _itemSearchText = string.Empty;
    
    [ObservableProperty]
    private string _npcSearchText = string.Empty;
    
    [ObservableProperty]
    private string _spellSearchText = string.Empty;
    
    [ObservableProperty]
    private string _classSearchText = string.Empty;
    
    // Filtered collections for DataGrid binding (filter by type AND search text)
    public IEnumerable<ItemRecordWrapper> FilteredItems
    {
        get
        {
            var result = Items.AsEnumerable();
            if (SelectedItemTypeFilter.HasValue)
                result = result.Where(i => i.Type == SelectedItemTypeFilter.Value);
            if (!string.IsNullOrWhiteSpace(ItemSearchText))
                result = result.Where(i => i.Name.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
    
    public IEnumerable<NpcRecordWrapper> FilteredNpcs
    {
        get
        {
            var result = Npcs.AsEnumerable();
            if (SelectedNpcTypeFilter.HasValue)
                result = result.Where(n => n.Type == SelectedNpcTypeFilter.Value);
            if (!string.IsNullOrWhiteSpace(NpcSearchText))
                result = result.Where(n => n.Name.Contains(NpcSearchText, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
    
    public IEnumerable<SpellRecordWrapper> FilteredSpells
    {
        get
        {
            var result = Spells.AsEnumerable();
            if (SelectedSpellTypeFilter.HasValue)
                result = result.Where(s => s.Type == SelectedSpellTypeFilter.Value);
            if (!string.IsNullOrWhiteSpace(SpellSearchText))
                result = result.Where(s => s.Name.Contains(SpellSearchText, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
    
    public IEnumerable<ClassRecordWrapper> FilteredClasses
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ClassSearchText))
                return Classes.Where(c => c.Name.Contains(ClassSearchText, StringComparison.OrdinalIgnoreCase));
            return Classes;
        }
    }
    
    // Selected records
    [ObservableProperty]
    private ItemRecordWrapper? _selectedItem;
    
    [ObservableProperty]
    private NpcRecordWrapper? _selectedNpc;
    
    [ObservableProperty]
    private SpellRecordWrapper? _selectedSpell;
    
    [ObservableProperty]
    private ClassRecordWrapper? _selectedClass;
    
    // Preview images
    [ObservableProperty]
    private Bitmap? _itemPreview;
    
    [ObservableProperty]
    private Bitmap? _npcPreview;
    
    [ObservableProperty]
    private Bitmap? _spellPreview;
    
    [ObservableProperty]
    private Bitmap? _spellIconPreview;
    
    [ObservableProperty]
    private Bitmap? _equipmentPreview;
    
    // Helper to check if current item is an equipment type
    public bool IsEquipmentType => SelectedItem != null && IsItemEquipmentType(SelectedItem.Type);
    
    private static bool IsItemEquipmentType(ItemType type) =>
        type == ItemType.Armor || type == ItemType.Weapon || 
        type == ItemType.Hat || type == ItemType.Boots || type == ItemType.Shield ||
        type == ItemType.Gloves || type == ItemType.Accessory || type == ItemType.Belt ||
        type == ItemType.Necklace || type == ItemType.Ring || type == ItemType.Armlet ||
        type == ItemType.Bracer;
    
    // Type-based visibility properties for item editor sections
    public bool ShowWeaponStats => SelectedItem?.Type == ItemType.Weapon;
    
    public bool ShowDefenseStats => SelectedItem != null && IsDefenseEquipmentType(SelectedItem.Type);
    
    private static bool IsDefenseEquipmentType(ItemType type) =>
        type == ItemType.Armor || type == ItemType.Shield || 
        type == ItemType.Hat || type == ItemType.Boots || type == ItemType.Gloves;
    
    // Equipment types that need the visual GFX picker (excludes accessories like rings, bracers, etc.)
    public bool ShowEquipmentPreview => SelectedItem != null && IsVisualEquipmentType(SelectedItem.Type);
    
    // Gender selector is shown for equipment except weapons (weapons import to both male/female EGF files)
    public bool ShowGenderSelector => SelectedItem != null && 
        IsVisualEquipmentType(SelectedItem.Type) && 
        SelectedItem.Type != ItemType.Weapon;
    
    private static bool IsVisualEquipmentType(ItemType type) =>
        type == ItemType.Armor || type == ItemType.Weapon || 
        type == ItemType.Hat || type == ItemType.Boots || type == ItemType.Shield ||
        type == ItemType.Accessory; // Wings are Accessory type with Wings subtype
    
    public bool ShowStatBonuses => IsEquipmentType;
    
    public bool ShowRequirements => IsEquipmentType;
    
    public bool ShowHealStats => SelectedItem?.Type == ItemType.Heal;
    
    public bool ShowTeleportFields => SelectedItem?.Type == ItemType.Teleport;
    
    public bool ShowSpellField => SelectedItem?.Type == ItemType.Reserved7; // skill book
    
    public bool ShowExpRewardField => SelectedItem?.Type == ItemType.ExpReward;
    
    public bool ShowKeyField => SelectedItem?.Type == ItemType.Key;
    
    public bool ShowEffectField => SelectedItem != null && 
        (SelectedItem.Type == ItemType.Alcohol || SelectedItem.Type == ItemType.EffectPotion);
    
    public bool ShowHairDyeField => SelectedItem?.Type == ItemType.HairDye;
    
    public bool ShowCureCurseField => SelectedItem?.Type == ItemType.CureCurse;
    
    // Notify all visibility properties when selected item changes
    private void NotifyItemTypeVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsEquipmentType));
        OnPropertyChanged(nameof(ShowEquipmentPreview));
        OnPropertyChanged(nameof(ShowGenderSelector));
        OnPropertyChanged(nameof(ShowWeaponStats));
        OnPropertyChanged(nameof(ShowDefenseStats));
        OnPropertyChanged(nameof(ShowStatBonuses));
        OnPropertyChanged(nameof(ShowRequirements));
        OnPropertyChanged(nameof(ShowHealStats));
        OnPropertyChanged(nameof(ShowTeleportFields));
        OnPropertyChanged(nameof(ShowSpellField));
        OnPropertyChanged(nameof(ShowExpRewardField));
        OnPropertyChanged(nameof(ShowKeyField));
        OnPropertyChanged(nameof(ShowEffectField));
        OnPropertyChanged(nameof(ShowHairDyeField));
        OnPropertyChanged(nameof(ShowCureCurseField));
    }
    
    // Get the GFX type for equipment based on item type
    private static GfxType GetEquipmentGfxType(ItemType type, bool isFemale = false) => type switch
    {
        ItemType.Weapon => isFemale ? GfxType.FemaleWeapon : GfxType.MaleWeapon,
        ItemType.Shield => GfxType.MaleBack,
        ItemType.Armor => isFemale ? GfxType.FemaleArmor : GfxType.MaleArmor,
        ItemType.Hat => isFemale ? GfxType.FemaleHat : GfxType.MaleHat,
        ItemType.Boots => isFemale ? GfxType.FemaleBoots : GfxType.MaleBoots,
        _ => GfxType.Items
    };
    
    // Commands - these use Func callbacks that will be set by the view for folder picking
    private Func<Task>? _selectPubDirectoryFunc;
    private Func<Task>? _selectGfxDirectoryFunc;
    
    public IAsyncRelayCommand SelectPubDirectoryCommand { get; }
    public IAsyncRelayCommand SelectGfxDirectoryCommand { get; }
    
    // Enum values for ComboBox binding
    public Array ItemTypes => Enum.GetValues(typeof(ItemType));
    public Array ItemSubtypes => Enum.GetValues(typeof(ItemSubtype));
    public Array ItemSpecials => Enum.GetValues(typeof(ItemSpecial));
    public Array ItemSizes => Enum.GetValues(typeof(ItemSize));
    public Array NpcTypes => Enum.GetValues(typeof(NpcType));
    public Array SkillTypes => Enum.GetValues(typeof(SkillType));
    public Array SkillTargetTypes => Enum.GetValues(typeof(SkillTargetType));
    public Array SkillTargetRestricts => Enum.GetValues(typeof(SkillTargetRestrict));
    public Array Elements => Enum.GetValues(typeof(Element));
    
    public MainWindowViewModel()
    {
        _pubFileService = new PubFileService();
        _gfxService = new GfxService();
        _settings = AppSettings.Load();
        
        // Initialize commands
        SelectPubDirectoryCommand = new AsyncRelayCommand(ExecuteSelectPubDirectoryAsync);
        SelectGfxDirectoryCommand = new AsyncRelayCommand(ExecuteSelectGfxDirectoryAsync);
        
        PubDirectory = _settings.PubDirectory;
        GfxDirectory = _settings.GfxDirectory;
        SaveDirectory = _settings.SaveDirectory;
        EnablePubSplitting = _settings.EnablePubSplitting;
        MaxEntriesPerFile = _settings.MaxEntriesPerFile;
        
        if (!string.IsNullOrEmpty(GfxDirectory))
        {
            _gfxService.SetGfxDirectory(GfxDirectory);
        }
    }
    
    partial void OnSaveDirectoryChanged(string? value)
    {
        _settings.SaveDirectory = value;
        _settings.Save();
    }
    
    partial void OnEnablePubSplittingChanged(bool value)
    {
        _settings.EnablePubSplitting = value;
        _settings.Save();
    }
    
    partial void OnMaxEntriesPerFileChanged(int value)
    {
        _settings.MaxEntriesPerFile = value;
        _settings.Save();
    }
    
    // Methods to set the folder picker callbacks from the view
    public void SetPubDirectoryPicker(Func<Task> picker) => _selectPubDirectoryFunc = picker;
    public void SetGfxDirectoryPicker(Func<Task> picker) => _selectGfxDirectoryFunc = picker;
    
    // Graphic picker callback - set by the view to open the picker dialog
    private Func<GfxType, int?, Task<int?>>? _openGraphicPickerFunc;
    public void SetGraphicPickerFunc(Func<GfxType, int?, Task<int?>> func) => _openGraphicPickerFunc = func;
    
    // Expose GfxService for picker dialog
    public IGfxService GfxService => _gfxService;
    
    // Public methods for opening graphic pickers - called from view code-behind
    public async Task OpenItemGraphicPickerAsync()
    {
        if (_openGraphicPickerFunc != null && SelectedItem != null)
        {
            var newId = await _openGraphicPickerFunc(GfxType.Items, SelectedItem.GraphicId);
            if (newId.HasValue)
            {
                var item = SelectedItem;
                item.GraphicId = newId.Value;
                // Force UI refresh by clearing and re-setting (SDK records don't implement INPC)
                SelectedItem = null;
                SelectedItem = item;
                // Reload the preview image
                ItemPreview = _gfxService.LoadBitmap(GfxType.Items, item.GraphicId);
            }
        }
    }
    
    public async Task OpenNpcGraphicPickerAsync()
    {
        if (_openGraphicPickerFunc != null && SelectedNpc != null)
        {
            var newId = await _openGraphicPickerFunc(GfxType.NPC, SelectedNpc.GraphicId);
            if (newId.HasValue)
            {
                var npc = SelectedNpc;
                npc.GraphicId = newId.Value;
                // Force UI refresh by clearing and re-setting (SDK records don't implement INPC)
                SelectedNpc = null;
                SelectedNpc = npc;
                // Reload the preview image
                NpcPreview = _gfxService.LoadBitmap(GfxType.NPC, npc.GraphicId);
            }
        }
    }
    
    public async Task OpenSpellGraphicPickerAsync()
    {
        if (_openGraphicPickerFunc != null && SelectedSpell != null)
        {
            var newId = await _openGraphicPickerFunc(GfxType.Spells, SelectedSpell.GraphicId);
            if (newId.HasValue)
            {
                var spell = SelectedSpell;
                spell.GraphicId = (short)newId.Value;
                // Force UI refresh by clearing and re-setting (SDK records don't implement INPC)
                SelectedSpell = null;
                SelectedSpell = spell;
                // Reload the preview image (extract first frame for spells)
                var bitmap = _gfxService.LoadBitmap(GfxType.Spells, spell.GraphicId);
                SpellPreview = bitmap != null ? _gfxService.ExtractFirstFrame(bitmap) : null;
            }
        }
    }
    
    public async Task OpenSpellIconPickerAsync()
    {
        if (_openGraphicPickerFunc != null && SelectedSpell != null)
        {
            var newId = await _openGraphicPickerFunc(GfxType.SpellIcons, SelectedSpell.IconId);
            if (newId.HasValue)
            {
                var spell = SelectedSpell;
                spell.IconId = newId.Value;
                // Force UI refresh by clearing and re-setting (SDK records don't implement INPC)
                SelectedSpell = null;
                SelectedSpell = spell;
                // Reload the icon preview image
                SpellIconPreview = _gfxService.LoadBitmap(GfxType.SpellIcons, spell.IconId);
            }
        }
    }
    
    public async Task OpenEquipmentGraphicPickerAsync()
    {
        if (_openGraphicPickerFunc != null && SelectedItem != null && IsItemEquipmentType(SelectedItem.Type))
        {
            var gfxType = GetEquipmentGfxType(SelectedItem.Type, SelectedItem.IsFemaleEquipment);
            var newId = await _openGraphicPickerFunc(gfxType, SelectedItem.Spec1 > 0 ? SelectedItem.Spec1 : null);
            if (newId.HasValue)
            {
                var item = SelectedItem;
                item.Spec1 = newId.Value;
                // Force UI refresh by clearing and re-setting (SDK records don't implement INPC)
                SelectedItem = null;
                SelectedItem = item;
                HasUnsavedChanges = true;
                Console.WriteLine($"Equipment graphic set: Spec1={item.Spec1}");
            }
        }
    }
    
    // Load equipment graphic frame (uses first/standing frame for preview)
    private Bitmap? LoadEquipmentFrame(GfxType gfxType, int dollGraphicId, ItemType itemType)
    {
        // Calculate resource ID for standing frame (frame 0)
        int resourceId = itemType switch
        {
            ItemType.Weapon => (dollGraphicId * 100) + 1,          // Weapon frame 0
            ItemType.Armor => ((dollGraphicId - 1) * 50) + 101,    // Armor frame 0
            ItemType.Boots => ((dollGraphicId - 1) * 40) + 101,    // Boots frame 0
            ItemType.Hat => ((dollGraphicId - 1) * 10) + 101,      // Hat frame 0
            ItemType.Shield => ((dollGraphicId - 1) * 50) + 101,   // Shield frame 0
            _ => 0
        };
        
        if (resourceId <= 0) return null;
        
        try
        {
            return _gfxService.LoadBitmapByResourceId(gfxType, resourceId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load equipment frame: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Called from the View's OnLoaded to load data if directories are already configured.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(PubDirectory))
        {
            await LoadAllFilesAsync();
        }
    }
    
    private async Task ExecuteSelectPubDirectoryAsync()
    {
        if (_selectPubDirectoryFunc != null)
            await _selectPubDirectoryFunc();
    }
    
    private async Task ExecuteSelectGfxDirectoryAsync()
    {
        if (_selectGfxDirectoryFunc != null)
            await _selectGfxDirectoryFunc();
    }
    
    // Notify UI when filter changes
    partial void OnSelectedItemTypeFilterChanged(ItemType? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
    }
    
    partial void OnSelectedNpcTypeFilterChanged(NpcType? value)
    {
        OnPropertyChanged(nameof(FilteredNpcs));
    }
    
    partial void OnSelectedSpellTypeFilterChanged(SkillType? value)
    {
        OnPropertyChanged(nameof(FilteredSpells));
    }
    
    // Notify UI when search text changes
    partial void OnItemSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredItems));
    }
    
    partial void OnNpcSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredNpcs));
    }
    
    partial void OnSpellSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredSpells));
    }
    
    partial void OnClassSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredClasses));
    }
    
    partial void OnSelectedItemChanged(ItemRecordWrapper? oldValue, ItemRecordWrapper? newValue)
    {
        // Unsubscribe from old item
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSelectedItemPropertyChanged;
        }
        
        // Subscribe to new item
        if (newValue != null)
        {
            newValue.PropertyChanged += OnSelectedItemPropertyChanged;
        }
        
        // Update visibility properties for type-based field display
        NotifyItemTypeVisibilityChanged();
        
        if (newValue != null && _gfxService.IsGfxDirectoryValid())
        {
            Console.WriteLine($"Loading GFX for '{newValue.Name}' GraphicId={newValue.GraphicId}, peResourceId={(2 * newValue.GraphicId) + 100}");
            ItemPreview = _gfxService.LoadBitmap(GfxType.Items, newValue.GraphicId);
            Console.WriteLine($"ItemPreview loaded: {ItemPreview != null}");
            
            // Load equipment preview if this is an equipment type with Spec1 set
            if (IsItemEquipmentType(newValue.Type) && newValue.Spec1 > 0)
            {
                var gfxType = GetEquipmentGfxType(newValue.Type, newValue.IsFemaleEquipment);
                EquipmentPreview = LoadEquipmentFrame(gfxType, newValue.Spec1, newValue.Type);
                Console.WriteLine($"EquipmentPreview loaded: {EquipmentPreview != null} (Type={newValue.Type}, Spec1={newValue.Spec1}, Female={newValue.IsFemaleEquipment})");
            }
            else
            {
                EquipmentPreview = null;
            }
        }
        else
        {
            Console.WriteLine($"GFX not loaded: value={newValue != null}, GfxValid={_gfxService.IsGfxDirectoryValid()}");
            ItemPreview = null;
            EquipmentPreview = null;
        }
    }
    
    private void OnSelectedItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemRecordWrapper.Type))
        {
            // Type changed, update all type-based visibility properties
            NotifyItemTypeVisibilityChanged();
            
            // Also update equipment preview visibility (clear it if no longer equipment type)
            if (SelectedItem != null && !IsItemEquipmentType(SelectedItem.Type))
            {
                EquipmentPreview = null;
            }
        }
    }
    
    partial void OnSelectedNpcChanged(NpcRecordWrapper? value)
    {
        if (value != null && _gfxService.IsGfxDirectoryValid())
        {
            Console.WriteLine($"Loading NPC GFX for '{value.Name}' GraphicId={value.GraphicId}");
            NpcPreview = _gfxService.LoadBitmap(GfxType.NPC, value.GraphicId);
            Console.WriteLine($"NpcPreview loaded: {NpcPreview != null}, size: {NpcPreview?.Size}");
        }
        else
        {
            NpcPreview = null;
        }
    }
    
    partial void OnSelectedSpellChanged(SpellRecordWrapper? value)
    {
        if (value != null && _gfxService.IsGfxDirectoryValid())
        {
            // Use GraphicId with GfxType.Spells (gfx024) for cast effect graphic
            Console.WriteLine($"Loading Spell GFX for '{value.Name}' GraphicId={value.GraphicId}");
            var fullSpritesheet = _gfxService.LoadBitmap(GfxType.Spells, value.GraphicId);
            // Extract just the first frame from the spritesheet for preview
            SpellPreview = _gfxService.ExtractFirstFrame(fullSpritesheet);
            Console.WriteLine($"SpellPreview loaded: {SpellPreview != null}, size: {SpellPreview?.Size}");
            
            // IconId with GfxType.SpellIcons (gfx025) is used for spell bar icons
            Console.WriteLine($"Loading Spell Icon for '{value.Name}' IconId={value.IconId}");
            SpellIconPreview = _gfxService.LoadBitmap(GfxType.SpellIcons, value.IconId);
            Console.WriteLine($"SpellIconPreview loaded: {SpellIconPreview != null}, size: {SpellIconPreview?.Size}");
        }
        else
        {
            SpellPreview = null;
            SpellIconPreview = null;
        }
    }
    
    public async Task LoadPubDirectoryAsync(string path)
    {
        PubDirectory = path;
        _settings.PubDirectory = path;
        _settings.Save();
        
        await LoadAllFilesAsync();
    }
    
    public void LoadGfxDirectory(string path)
    {
        GfxDirectory = path;
        _gfxService.SetGfxDirectory(path);
        _settings.GfxDirectory = path;
        _settings.Save();
        
        StatusText = _gfxService.IsGfxDirectoryValid() 
            ? "GFX directory loaded" 
            : "GFX directory invalid - preview unavailable";
    }
    
    private async Task LoadAllFilesAsync()
    {
        if (string.IsNullOrEmpty(PubDirectory))
            return;
            
        try
        {
            StatusText = "Loading pub files...";
            
            int loaded = 0;
            
            // Load items from all .eif files (supports split pubs: dat001.eif, dat002.eif, etc.)
            var eifFiles = Directory.GetFiles(PubDirectory, "*.eif").OrderBy(f => f).ToArray();
            if (eifFiles.Length > 0)
            {
                Console.WriteLine($"Found {eifFiles.Length} EIF files: {string.Join(", ", eifFiles.Select(Path.GetFileName))}");
                
                // Load first file as the base
                _itemsFileData = await _pubFileService.LoadItemsAsync(eifFiles[0]);
                var allItems = _itemsFileData.Items.ToList();
                Console.WriteLine($"Loaded {allItems.Count} items from {Path.GetFileName(eifFiles[0])}");
                
                // Merge additional split files
                for (int i = 1; i < eifFiles.Length; i++)
                {
                    var additionalFile = await _pubFileService.LoadItemsAsync(eifFiles[i]);
                    Console.WriteLine($"Loaded {additionalFile.Items.Count} items from {Path.GetFileName(eifFiles[i])}");
                    allItems.AddRange(additionalFile.Items);
                }
                
                // Update the consolidated items list
                _itemsFileData.Items.Clear();
                foreach (var item in allItems)
                    _itemsFileData.Items.Add(item);
                
                Console.WriteLine($"Total merged items: {_itemsFileData.Items.Count}");
                
                // Force UI thread update - wrap items with 1-based IDs
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var wrappedItems = _itemsFileData.Items
                        .Select((record, index) => new ItemRecordWrapper(record, index + 1))
                        .ToList();
                    Items = new ObservableCollection<ItemRecordWrapper>(wrappedItems);
                    OnPropertyChanged(nameof(FilteredItems)); // Notify filtered collection
                    Console.WriteLine($"Items collection now has {Items.Count} items (on UI thread)");
                });
                loaded++;
                StatusText = $"Loaded {Items.Count} items from {eifFiles.Length} file(s)...";
            }
            
            // Load NPCs from all .enf files (supports split pubs)
            var enfFiles = Directory.GetFiles(PubDirectory, "*.enf").OrderBy(f => f).ToArray();
            if (enfFiles.Length > 0)
            {
                _npcsFileData = await _pubFileService.LoadNpcsAsync(enfFiles[0]);
                var allNpcs = _npcsFileData.Npcs.ToList();
                
                for (int i = 1; i < enfFiles.Length; i++)
                {
                    var additionalFile = await _pubFileService.LoadNpcsAsync(enfFiles[i]);
                    allNpcs.AddRange(additionalFile.Npcs);
                }
                
                _npcsFileData.Npcs.Clear();
                foreach (var npc in allNpcs)
                    _npcsFileData.Npcs.Add(npc);
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var wrappedNpcs = _npcsFileData.Npcs
                        .Select((record, index) => new NpcRecordWrapper(record, index + 1))
                        .ToList();
                    Npcs = new ObservableCollection<NpcRecordWrapper>(wrappedNpcs);
                    OnPropertyChanged(nameof(FilteredNpcs));
                });
                loaded++;
                StatusText = $"Loaded {Npcs.Count} NPCs from {enfFiles.Length} file(s)...";
            }
            
            // Load Spells from all .esf files (supports split pubs)
            var esfFiles = Directory.GetFiles(PubDirectory, "*.esf").OrderBy(f => f).ToArray();
            if (esfFiles.Length > 0)
            {
                _spellsFileData = await _pubFileService.LoadSpellsAsync(esfFiles[0]);
                var allSpells = _spellsFileData.Skills.ToList();
                
                for (int i = 1; i < esfFiles.Length; i++)
                {
                    var additionalFile = await _pubFileService.LoadSpellsAsync(esfFiles[i]);
                    allSpells.AddRange(additionalFile.Skills);
                }
                
                _spellsFileData.Skills.Clear();
                foreach (var spell in allSpells)
                    _spellsFileData.Skills.Add(spell);
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var wrappedSpells = _spellsFileData.Skills
                        .Select((record, index) => new SpellRecordWrapper(record, index + 1))
                        .ToList();
                    Spells = new ObservableCollection<SpellRecordWrapper>(wrappedSpells);
                    OnPropertyChanged(nameof(FilteredSpells));
                });
                loaded++;
                StatusText = $"Loaded {Spells.Count} spells from {esfFiles.Length} file(s)...";
            }
            
            // Load Classes from all .ecf files (supports split pubs)
            var ecfFiles = Directory.GetFiles(PubDirectory, "*.ecf").OrderBy(f => f).ToArray();
            if (ecfFiles.Length > 0)
            {
                _classesFileData = await _pubFileService.LoadClassesAsync(ecfFiles[0]);
                var allClasses = _classesFileData.Classes.ToList();
                
                for (int i = 1; i < ecfFiles.Length; i++)
                {
                    var additionalFile = await _pubFileService.LoadClassesAsync(ecfFiles[i]);
                    allClasses.AddRange(additionalFile.Classes);
                }
                
                _classesFileData.Classes.Clear();
                foreach (var cls in allClasses)
                    _classesFileData.Classes.Add(cls);
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var wrappedClasses = _classesFileData.Classes
                        .Select((record, index) => new ClassRecordWrapper(record, index + 1))
                        .ToList();
                    Classes = new ObservableCollection<ClassRecordWrapper>(wrappedClasses);
                    OnPropertyChanged(nameof(FilteredClasses));
                });
                loaded++;
                StatusText = $"Loaded {Classes.Count} classes from {ecfFiles.Length} file(s)...";
            }
            
            StatusText = loaded > 0 
                ? $"Loaded {loaded} pub files ({Items.Count} items, {Npcs.Count} NPCs, {Spells.Count} spells, {Classes.Count} classes)"
                : "No pub files found in directory";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading files: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (string.IsNullOrEmpty(PubDirectory))
        {
            StatusText = "No pub directory selected";
            return;
        }
        
        try
        {
            // Use SaveDirectory if set, otherwise save to PubDirectory
            var savePath = !string.IsNullOrEmpty(SaveDirectory) ? SaveDirectory : PubDirectory;
            
            // Ensure save directory exists
            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath!);
            
            var filesCreated = new List<string>();
            
            // Save Items with optional splitting
            if (_itemsFileData != null && Items.Count > 0)
            {
                var itemRecords = Items.Select(i => i.Record).ToList();
                filesCreated.AddRange(await SaveWithSplittingAsync(
                    savePath!, "dat", ".eif", itemRecords,
                    (records, startIdx, endIdx) => {
                        var eif = new Eif();
                        eif.Rid.Add(startIdx);
                        eif.Rid.Add(endIdx);
                        eif.TotalItemsCount = itemRecords.Count;
                        eif.Version = _itemsFileData?.Version ?? 1;
                        eif.Items.AddRange(records);
                        return eif;
                    },
                    (path, data) => _pubFileService.SaveItemsAsync(path, data)));
            }
            
            // Save NPCs with optional splitting
            if (_npcsFileData != null && Npcs.Count > 0)
            {
                var npcRecords = Npcs.Select(n => n.Record).ToList();
                filesCreated.AddRange(await SaveWithSplittingAsync(
                    savePath!, "dtn", ".enf", npcRecords,
                    (records, startIdx, endIdx) => {
                        var enf = new Enf();
                        enf.Rid.Add(startIdx);
                        enf.Rid.Add(endIdx);
                        enf.TotalNpcsCount = npcRecords.Count;
                        enf.Version = _npcsFileData?.Version ?? 1;
                        enf.Npcs.AddRange(records);
                        return enf;
                    },
                    (path, data) => _pubFileService.SaveNpcsAsync(path, data)));
            }
            
            // Save Spells with optional splitting
            if (_spellsFileData != null && Spells.Count > 0)
            {
                var spellRecords = Spells.Select(s => s.Record).ToList();
                filesCreated.AddRange(await SaveWithSplittingAsync(
                    savePath!, "dsl", ".esf", spellRecords,
                    (records, startIdx, endIdx) => {
                        var esf = new Esf();
                        esf.Rid.Add(startIdx);
                        esf.Rid.Add(endIdx);
                        esf.TotalSkillsCount = spellRecords.Count;
                        esf.Version = _spellsFileData?.Version ?? 1;
                        esf.Skills.AddRange(records);
                        return esf;
                    },
                    (path, data) => _pubFileService.SaveSpellsAsync(path, data)));
            }
            
            // Save Classes with optional splitting
            if (_classesFileData != null && Classes.Count > 0)
            {
                var classRecords = Classes.Select(c => c.Record).ToList();
                filesCreated.AddRange(await SaveWithSplittingAsync(
                    savePath!, "dat", ".ecf", classRecords,
                    (records, startIdx, endIdx) => {
                        var ecf = new Ecf();
                        ecf.Rid.Add(startIdx);
                        ecf.Rid.Add(endIdx);
                        ecf.TotalClassesCount = classRecords.Count;
                        ecf.Version = _classesFileData?.Version ?? 1;
                        ecf.Classes.AddRange(records);
                        return ecf;
                    },
                    (path, data) => _pubFileService.SaveClassesAsync(path, data)));
            }
            
            HasUnsavedChanges = false;
            
            var fileList = string.Join(", ", filesCreated.Select(Path.GetFileName));
            StatusText = $"Saved {filesCreated.Count} file(s): {fileList}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving files: {ex.Message}";
        }
    }
    
    private async Task<List<string>> SaveWithSplittingAsync<TRecord, TFile>(
        string savePath,
        string prefix,
        string extension,
        List<TRecord> allRecords,
        Func<IEnumerable<TRecord>, int, int, TFile> createFile,
        Func<string, TFile, Task> saveFunc)
    {
        var filesCreated = new List<string>();
        
        if (!EnablePubSplitting || allRecords.Count <= MaxEntriesPerFile)
        {
            // No splitting needed - save as single file
            // Rid is 1-based: [1, count]
            var fileName = $"{prefix}001{extension}";
            var filePath = Path.Combine(savePath, fileName);
            var file = createFile(allRecords, 1, allRecords.Count);
            await saveFunc(filePath, file);
            filesCreated.Add(filePath);
            StatusText = $"Saving {fileName}...";
        }
        else
        {
            // Split into multiple files
            var fileNumber = 1;
            for (int i = 0; i < allRecords.Count; i += MaxEntriesPerFile)
            {
                var chunk = allRecords.Skip(i).Take(MaxEntriesPerFile).ToList();
                var startIdx = i + 1; // 1-based index
                var endIdx = i + chunk.Count; // 1-based index
                var fileName = $"{prefix}{fileNumber:D3}{extension}";
                var filePath = Path.Combine(savePath, fileName);
                var file = createFile(chunk, startIdx, endIdx);
                await saveFunc(filePath, file);
                filesCreated.Add(filePath);
                StatusText = $"Saving {fileName}...";
                fileNumber++;
            }
        }
        
        return filesCreated;
    }
    
    [RelayCommand]
    private void AddItem()
    {
        var newRecord = new EifRecord
        {
            Name = "New Item",
            GraphicId = 1,
            Spec1 = 1  // Default doll graphic ID to avoid resource ID 0 issue
        };
        
        var wrapper = new ItemRecordWrapper(newRecord, Items.Count + 1);
        Items.Add(wrapper);
        SelectedItem = wrapper;
        HasUnsavedChanges = true;
        StatusText = "Added new item";
    }
    
    [RelayCommand]
    private void DeleteItem()
    {
        if (SelectedItem == null) return;
        
        Items.Remove(SelectedItem);
        SelectedItem = Items.FirstOrDefault();
        HasUnsavedChanges = true;
        StatusText = "Deleted item";
    }
    
    [RelayCommand]
    private void AddNpc()
    {
        var newRecord = new EnfRecord
        {
            Name = "New NPC",
            GraphicId = 1
        };
        
        var wrapper = new NpcRecordWrapper(newRecord, Npcs.Count + 1);
        Npcs.Add(wrapper);
        SelectedNpc = wrapper;
        HasUnsavedChanges = true;
        StatusText = "Added new NPC";
    }
    
    [RelayCommand]
    private void DeleteNpc()
    {
        if (SelectedNpc == null) return;
        
        Npcs.Remove(SelectedNpc);
        SelectedNpc = Npcs.FirstOrDefault();
        HasUnsavedChanges = true;
        StatusText = "Deleted NPC";
    }
    
    [RelayCommand]
    private void AddSpell()
    {
        var newRecord = new EsfRecord
        {
            Name = "New Spell",
            IconId = 1
        };
        
        var wrapper = new SpellRecordWrapper(newRecord, Spells.Count + 1);
        Spells.Add(wrapper);
        SelectedSpell = wrapper;
        HasUnsavedChanges = true;
        StatusText = "Added new spell";
    }
    
    [RelayCommand]
    private void DeleteSpell()
    {
        if (SelectedSpell == null) return;
        
        Spells.Remove(SelectedSpell);
        SelectedSpell = Spells.FirstOrDefault();
        HasUnsavedChanges = true;
        StatusText = "Deleted spell";
    }
    
    [RelayCommand]
    private void AddClass()
    {
        var newRecord = new EcfRecord
        {
            Name = "New Class"
        };
        
        var wrapper = new ClassRecordWrapper(newRecord, Classes.Count + 1);
        Classes.Add(wrapper);
        SelectedClass = wrapper;
        HasUnsavedChanges = true;
        StatusText = "Added new class";
    }
    
    [RelayCommand]
    private void DeleteClass()
    {
        if (SelectedClass == null) return;
        
        Classes.Remove(SelectedClass);
        SelectedClass = Classes.FirstOrDefault();
        HasUnsavedChanges = true;
        StatusText = "Deleted class";
    }
}
