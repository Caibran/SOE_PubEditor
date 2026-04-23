using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SOE_PubEditor.Services;

namespace SOE_PubEditor.ViewModels;

/// <summary>
/// Represents a single EGF file entry in the sidebar list.
/// </summary>
public partial class EgfFileEntry : ObservableObject
{
    public int FileNumber { get; init; }
    public string FileName => $"gfx{FileNumber:D3}.egf";
    public string Label { get; init; } = "";
    public bool Exists { get; set; }

    public void RaiseExistsChanged() => OnPropertyChanged(nameof(Exists));
}

/// <summary>
/// Represents one resource slot inside an EGF file.
/// </summary>
public partial class EgfResourceSlot : ObservableObject
{
    public int ResourceId { get; init; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// ViewModel for the GFX Editor tab — browse, replace, and mass-import EGF resources.
/// </summary>
public partial class GfxEditorViewModel : ViewModelBase
{
    // ────────────────────────────── Labels for each gfx###.egf ──────────────────────────────
    private static readonly Dictionary<int, string> FileLabels = new()
    {
        { 1,  "gfx001 — Login Interface" },
        { 2,  "gfx002 — Game Interface" },
        { 3,  "gfx003 — Map Tiles" },
        { 4,  "gfx004 — Map Objects" },
        { 5,  "gfx005 — Map Overlay" },
        { 6,  "gfx006 — Map Walls" },
        { 7,  "gfx007 — Map Top" },
        { 8,  "gfx008 — Skins" },
        { 9,  "gfx009 — Hair (Male)" },
        { 10, "gfx010 — Hair (Female)" },
        { 11, "gfx011 — Boots (Zoomed-In)" },
        { 12, "gfx012 — Boots" },
        { 13, "gfx013 — Male Armor" },
        { 14, "gfx014 — Female Armor" },
        { 15, "gfx015 — Hats" },
        { 16, "gfx016 — Hats" },
        { 17, "gfx017 — Weapons" },
        { 18, "gfx018 — Weapons" },
        { 19, "gfx019 — Shields" },
        { 20, "gfx020 — Shields" },
        { 21, "gfx021 — Monsters" },
        { 22, "gfx022 — Shades (Maps)" },
        { 23, "gfx023 — Inventory Icons / Drop" },
        { 24, "gfx024 — Effects / Spells" },
        { 25, "gfx025 — Spell Icons" },
    };

    private readonly IGfxService _gfxService;
    private readonly IGfxImportService _importService;
    private CancellationTokenSource? _loadCts;

    // ────────────────────────────── Sidebar: EGF file list ──────────────────────────────

    public ObservableCollection<EgfFileEntry> EgfFiles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(MassImportCommand))]
    private EgfFileEntry? _selectedEgfFile;

    // ────────────────────────────── Resource grid ──────────────────────────────

    public ObservableCollection<EgfResourceSlot> ResourceSlots { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceSelectedCommand))]
    private EgfResourceSlot? _selectedSlot;

    [ObservableProperty]
    private bool _isLoadingResources;

    [ObservableProperty]
    private string _resourceLoadStatus = "";

    // ────────────────────────────── Mass-import range ──────────────────────────────

    [ObservableProperty]
    private int _massImportStartId = 100;

    [ObservableProperty]
    private int _massImportEndId = 200;

    [ObservableProperty]
    private string _massImportFolder = "";

    // ────────────────────────────── Progress / status ──────────────────────────────

    [ObservableProperty]
    private string _statusText = "Select an EGF file to browse its resources.";

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    [ObservableProperty]
    private bool _importAvailable;

    // ────────────────────────────── Callbacks wired from View ──────────────────────────────

    private Func<Task<string?>>? _pickBmpFileFunc;
    private Func<Task<string?>>? _pickFolderFunc;

    public void SetPickBmpFileFunc(Func<Task<string?>> f) => _pickBmpFileFunc = f;
    public void SetPickFolderFunc(Func<Task<string?>> f) => _pickFolderFunc = f;

    // ────────────────────────────── Constructor ──────────────────────────────

    public GfxEditorViewModel(IGfxService gfxService, IGfxImportService importService)
    {
        _gfxService = gfxService;
        _importService = importService;
        ImportAvailable = importService.IsAvailable;

        for (int i = 1; i <= 25; i++)
        {
            EgfFiles.Add(new EgfFileEntry
            {
                FileNumber = i,
                Label = FileLabels.TryGetValue(i, out var lbl) ? lbl : $"gfx{i:D3}.egf",
                Exists = false
            });
        }
    }

    /// <summary>
    /// Called when the GFX directory changes — refreshes which files exist.
    /// </summary>
    public void RefreshFileList()
    {
        foreach (var entry in EgfFiles)
        {
            var path = Path.Combine(_gfxService.GfxDirectory ?? "", entry.FileName);
            entry.Exists = File.Exists(path);
            // Trigger property change notification directly on the entry object
            entry.RaiseExistsChanged();
        }
    }

    // ────────────────────────────── Load resources when EGF selected ──────────────────────────────

    partial void OnSelectedEgfFileChanged(EgfFileEntry? value)
    {
        _ = LoadResourcesAsync(value);
    }

    private async Task LoadResourcesAsync(EgfFileEntry? entry)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        ResourceSlots.Clear();
        SelectedSlot = null;

        if (entry == null || string.IsNullOrEmpty(_gfxService.GfxDirectory))
            return;

        var egfPath = Path.Combine(_gfxService.GfxDirectory, entry.FileName);
        if (!File.Exists(egfPath))
        {
            StatusText = $"{entry.FileName} not found in GFX directory.";
            return;
        }

        IsLoadingResources = true;
        ResourceLoadStatus = $"Reading {entry.FileName}…";

        try
        {
            // Enumerate raw PE resource IDs using the shared GfxService
            var rawIds = await Task.Run(() => _gfxService.GetRawBitmapResourceIds(egfPath), token);

            if (token.IsCancellationRequested) return;

            ResourceLoadStatus = $"Loading {rawIds.Count} resources\u2026";
            ProgressMax = rawIds.Count;
            ProgressValue = 0;

            var slots = new List<EgfResourceSlot>(rawIds.Count);

            await Task.Run(() =>
            {
                foreach (var rawId in rawIds)
                {
                    if (token.IsCancellationRequested) break;
                    var bmp = _gfxService.LoadBitmapFromEgfPath(egfPath, rawId);
                    slots.Add(new EgfResourceSlot { ResourceId = rawId, Thumbnail = bmp });
                }
            }, token);

            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResourceSlots.Clear();
                foreach (var slot in slots)
                    ResourceSlots.Add(slot);

                ProgressValue = slots.Count;
                ResourceLoadStatus = $"{slots.Count} resources in {entry.FileName}";
                StatusText = $"Browsing {entry.FileName} — {slots.Count} resources loaded.";
                IsLoadingResources = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected on tab switch
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = $"Error loading {entry.FileName}: {ex.Message}";
                IsLoadingResources = false;
            });
        }
    }

    // ────────────────────────────── Replace single resource ──────────────────────────────

    [RelayCommand(CanExecute = nameof(CanReplaceSingle))]
    private async Task ReplaceSelectedAsync()
    {
        if (SelectedEgfFile == null || SelectedSlot == null || _pickBmpFileFunc == null)
            return;

        var bmpPath = await _pickBmpFileFunc();
        if (string.IsNullOrEmpty(bmpPath)) return;

        var egfPath = Path.Combine(_gfxService.GfxDirectory!, SelectedEgfFile.FileName);
        if (!File.Exists(egfPath))
        {
            StatusText = $"EGF file not found: {egfPath}";
            return;
        }

        IsImporting = true;
        StatusText = $"Replacing resource {SelectedSlot.ResourceId}…";

        try
        {
            var ok = await Task.Run(() => ImportBmpIntoEgf(egfPath, SelectedSlot.ResourceId, bmpPath));

            if (ok)
            {
                // Reload the thumbnail for just this slot
                var newBmp = await Task.Run(() => _gfxService.LoadBitmapFromEgfPath(egfPath, SelectedSlot.ResourceId));
                SelectedSlot.Thumbnail?.Dispose();
                SelectedSlot.Thumbnail = newBmp;
                _gfxService.ClearCache();
                StatusText = $"Replaced resource {SelectedSlot.ResourceId} in {SelectedEgfFile.FileName}.";
            }
            else
            {
                StatusText = $"Failed to replace resource {SelectedSlot.ResourceId}.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanReplaceSingle() =>
        SelectedEgfFile != null && SelectedSlot != null && !IsImporting && ImportAvailable;

    // ────────────────────────────── Pick mass-import folder ──────────────────────────────

    [RelayCommand]
    private async Task PickMassImportFolderAsync()
    {
        if (_pickFolderFunc == null) return;
        var folder = await _pickFolderFunc();
        if (!string.IsNullOrEmpty(folder))
            MassImportFolder = folder;
    }

    // ────────────────────────────── Mass import ──────────────────────────────

    [RelayCommand(CanExecute = nameof(CanMassImport))]
    private async Task MassImportAsync()
    {
        if (SelectedEgfFile == null || string.IsNullOrEmpty(MassImportFolder))
            return;

        var egfPath = Path.Combine(_gfxService.GfxDirectory!, SelectedEgfFile.FileName);
        if (!File.Exists(egfPath))
        {
            StatusText = $"EGF file not found: {egfPath}";
            return;
        }

        // Collect BMP files sorted naturally
        var bmps = Directory.GetFiles(MassImportFolder, "*.bmp")
            .Concat(Directory.GetFiles(MassImportFolder, "*.BMP"))
            .Distinct()
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bmps.Count == 0)
        {
            StatusText = "No BMP files found in the selected folder.";
            return;
        }

        // Build resource ID sequence from start to end (inclusive)
        var targetIds = Enumerable
            .Range(MassImportStartId, Math.Max(1, MassImportEndId - MassImportStartId + 1))
            .ToList();

        var pairs = bmps.Zip(targetIds, (bmp, id) => (bmp, id)).ToList();

        IsImporting = true;
        ProgressMax = pairs.Count;
        ProgressValue = 0;
        int success = 0, fail = 0;

        try
        {
            await Task.Run(() =>
            {
                foreach (var (bmpPath, resourceId) in pairs)
                {
                    if (ImportBmpIntoEgf(egfPath, resourceId, bmpPath))
                        success++;
                    else
                        fail++;

                    Dispatcher.UIThread.Post(() =>
                    {
                        ProgressValue++;
                        StatusText = $"Mass import: {ProgressValue}/{pairs.Count} — {Path.GetFileName(bmpPath)} → ID {resourceId}";
                    });
                }
            });

            _gfxService.ClearCache();

            // Reload the resource grid
            StatusText = $"Mass import complete: {success} replaced, {fail} failed.";
            await LoadResourcesAsync(SelectedEgfFile);
        }
        catch (Exception ex)
        {
            StatusText = $"Mass import error: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanMassImport() =>
        SelectedEgfFile != null && !string.IsNullOrEmpty(MassImportFolder) &&
        !IsImporting && ImportAvailable;

    // ────────────────────────────── Import helper ──────────────────────────────

    private bool ImportBmpIntoEgf(string egfPath, int resourceId, string bmpPath)
    {
        try
        {
            return _importService.ReplaceBitmapResource(egfPath, resourceId, bmpPath);
        }
        catch (Exception ex)
        {
            FileLogger.LogError($"GFX EDITOR: ImportBmpIntoEgf failed: {ex.Message}");
            return false;
        }
    }
}

