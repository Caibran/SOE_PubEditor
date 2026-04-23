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
        { 1,  "gfx001 — Map Tiles (Layer 1)" },
        { 2,  "gfx002 — Map Tiles (Layer 2)" },
        { 3,  "gfx003 — Map Objects (Layer 3)" },
        { 4,  "gfx004 — Map Objects (Layer 4)" },
        { 5,  "gfx005 — Map Walls" },
        { 6,  "gfx006 — Map Objects (Layer 6)" },
        { 7,  "gfx007 — Map Overlays" },
        { 8,  "gfx008 — Map Shadows" },
        { 9,  "gfx009 — Map Roofs" },
        { 10, "gfx010 — Map Tiles (Layer 10)" },
        { 11, "gfx011 — Male Boots" },
        { 12, "gfx012 — Female Boots" },
        { 13, "gfx013 — Male Armor" },
        { 14, "gfx014 — Female Armor" },
        { 15, "gfx015 — Male Hats" },
        { 16, "gfx016 — Female Hats" },
        { 17, "gfx017 — Male Weapons" },
        { 18, "gfx018 — Female Weapons" },
        { 19, "gfx019 — Male Shields / Capes" },
        { 20, "gfx020 — Female Shields / Capes" },
        { 21, "gfx021 — NPC Sprites" },
        { 22, "gfx022 — Character Sprites" },
        { 23, "gfx023 — Item Icons" },
        { 24, "gfx024 — Spell Effects" },
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
            // Enumerate raw PE resource IDs directly from the file
            var rawIds = await Task.Run(() => GetRawResourceIds(egfPath), token);

            if (token.IsCancellationRequested) return;

            ResourceLoadStatus = $"Loading {rawIds.Count} resources…";
            ProgressMax = rawIds.Count;
            ProgressValue = 0;

            var slots = new List<EgfResourceSlot>(rawIds.Count);

            await Task.Run(() =>
            {
                foreach (var rawId in rawIds)
                {
                    if (token.IsCancellationRequested) break;
                    var bmp = LoadRawBitmapFromEgf(egfPath, rawId);
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
                var newBmp = await Task.Run(() => LoadRawBitmapFromEgf(egfPath, SelectedSlot.ResourceId));
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

    // ────────────────────────────── PE resource helpers ──────────────────────────────

    /// <summary>
    /// Returns all raw bitmap resource IDs present in an EGF file (PE format).
    /// </summary>
    private static List<int> GetRawResourceIds(string egfPath)
    {
        var ids = new List<int>();
        try
        {
            using var fs = new FileStream(egfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var r = new BinaryReader(fs);

            if (r.ReadUInt16() != 0x5A4D) return ids; // MZ
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOff = r.ReadUInt32();
            fs.Seek(peOff, SeekOrigin.Begin);
            if (r.ReadUInt32() != 0x00004550) return ids; // PE

            r.ReadUInt16(); // Machine
            var nSections = r.ReadUInt16();
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
            var optHdrSize = r.ReadUInt16();
            r.ReadUInt16();

            var optStart = fs.Position;
            var magic = r.ReadUInt16();
            var is64 = magic == 0x20B;

            fs.Seek(optStart + (is64 ? 112 : 96), SeekOrigin.Begin);
            r.ReadUInt64(); // export
            r.ReadUInt64(); // import
            var rsrcRva = r.ReadUInt32();
            r.ReadUInt32(); // rsrcSize

            if (rsrcRva == 0) return ids;

            fs.Seek(optStart + optHdrSize, SeekOrigin.Begin);
            for (int s = 0; s < nSections; s++)
            {
                r.ReadBytes(8); // name
                var vSize = r.ReadUInt32();
                var va = r.ReadUInt32();
                r.ReadUInt32();
                var rawOff = r.ReadUInt32();
                r.ReadBytes(16);

                if (rsrcRva >= va && rsrcRva < va + vSize)
                {
                    var rsrcFileOff = rawOff + (rsrcRva - va);
                    ids = ReadBitmapIds(r, fs, rsrcFileOff);
                    break;
                }
            }
        }
        catch { /* return whatever we have */ }
        return ids;
    }

    private static List<int> ReadBitmapIds(BinaryReader r, FileStream fs, uint rsrcOff)
    {
        var ids = new List<int>();
        fs.Seek(rsrcOff, SeekOrigin.Begin);
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
        var namedCount = r.ReadUInt16();
        var idCount = r.ReadUInt16();

        for (int i = 0; i < namedCount + idCount; i++)
        {
            var typeId = r.ReadUInt32();
            var offset = r.ReadUInt32();
            if (typeId == 2) // RT_BITMAP
            {
                var bmpDir = rsrcOff + (offset & 0x7FFFFFFF);
                fs.Seek(bmpDir, SeekOrigin.Begin);
                r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
                var n = r.ReadUInt16();
                var ni = r.ReadUInt16();
                for (int j = 0; j < n + ni; j++)
                {
                    var rid = (int)r.ReadUInt32();
                    r.ReadUInt32();
                    ids.Add(rid);
                }
                break;
            }
        }
        return ids.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Loads a bitmap directly from a raw PE resource ID (no formula applied).
    /// </summary>
    private static Bitmap? LoadRawBitmapFromEgf(string egfPath, int rawId)
    {
        try
        {
            using var fs = new FileStream(egfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var r = new BinaryReader(fs);

            if (r.ReadUInt16() != 0x5A4D) return null;
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOff = r.ReadUInt32();
            fs.Seek(peOff, SeekOrigin.Begin);
            if (r.ReadUInt32() != 0x00004550) return null;

            r.ReadUInt16();
            var nSections = r.ReadUInt16();
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
            var optHdrSize = r.ReadUInt16();
            r.ReadUInt16();

            var optStart = fs.Position;
            var magic = r.ReadUInt16();
            var is64 = magic == 0x20B;

            fs.Seek(optStart + (is64 ? 112 : 96), SeekOrigin.Begin);
            r.ReadUInt64();
            r.ReadUInt64();
            var rsrcRva = r.ReadUInt32();
            r.ReadUInt32();

            if (rsrcRva == 0) return null;

            fs.Seek(optStart + optHdrSize, SeekOrigin.Begin);
            uint rsrcFileOff = 0;
            for (int s = 0; s < nSections; s++)
            {
                r.ReadBytes(8);
                var vSize = r.ReadUInt32();
                var va = r.ReadUInt32();
                r.ReadUInt32();
                var rawOff = r.ReadUInt32();
                r.ReadBytes(16);
                if (rsrcRva >= va && rsrcRva < va + vSize)
                {
                    rsrcFileOff = rawOff + (rsrcRva - va);
                    break;
                }
            }
            if (rsrcFileOff == 0) return null;

            return ExtractBitmapById(r, fs, rsrcFileOff, rsrcRva, rawId);
        }
        catch { return null; }
    }

    private static Bitmap? ExtractBitmapById(BinaryReader r, FileStream fs, uint rsrcOff, uint rsrcRva, int targetId)
    {
        fs.Seek(rsrcOff, SeekOrigin.Begin);
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
        var nc = r.ReadUInt16(); var ic = r.ReadUInt16();
        for (int i = 0; i < nc + ic; i++)
        {
            var typeId = r.ReadUInt32(); var tOff = r.ReadUInt32();
            if (typeId == 2)
            {
                var bmpDir = rsrcOff + (tOff & 0x7FFFFFFF);
                fs.Seek(bmpDir, SeekOrigin.Begin);
                r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
                var n = r.ReadUInt16(); var ni = r.ReadUInt16();
                for (int j = 0; j < n + ni; j++)
                {
                    var rid = (int)r.ReadUInt32(); var rOff = r.ReadUInt32();
                    if (rid == targetId)
                    {
                        // Navigate to the language sub-directory
                        var langDir = rsrcOff + (rOff & 0x7FFFFFFF);
                        fs.Seek(langDir, SeekOrigin.Begin);
                        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
                        var ln = r.ReadUInt16(); var li = r.ReadUInt16();
                        if (ln + li == 0) return null;
                        r.ReadUInt32(); // lang id
                        var dataOff = r.ReadUInt32() & 0x7FFFFFFF;

                        var dataEntry = rsrcOff + dataOff;
                        fs.Seek(dataEntry, SeekOrigin.Begin);
                        var dataRva = r.ReadUInt32();
                        var dataSize = r.ReadUInt32();

                        // Convert RVA to file offset (need to re-read section table)
                        // We already know rsrcRva and rsrcOff, use delta
                        var delta = (long)rsrcOff - (long)rsrcRva;
                        var fileOffset = (long)dataRva + delta;

                        fs.Seek(fileOffset, SeekOrigin.Begin);
                        var bitmapData = r.ReadBytes((int)dataSize);

                        // EGF stores raw BITMAPINFO (no file header) — prepend BITMAPFILEHEADER
                        const int fileHeaderSize = 14;
                        // Read DIB header size to compute pixel data offset
                        var dibHeaderSize = BitConverter.ToInt32(bitmapData, 0);
                        // Count color table entries
                        var bitCount = BitConverter.ToInt16(bitmapData, 14);
                        var clrUsed = BitConverter.ToInt32(bitmapData, 32);
                        int colorTableCount = clrUsed != 0 ? clrUsed : (bitCount <= 8 ? (1 << bitCount) : 0);
                        var pixelDataOffset = fileHeaderSize + dibHeaderSize + colorTableCount * 4;

                        var fileBytes = new byte[fileHeaderSize + bitmapData.Length];
                        // BM signature
                        fileBytes[0] = 0x42; fileBytes[1] = 0x4D;
                        // File size
                        var fileSize = BitConverter.GetBytes(fileBytes.Length);
                        Array.Copy(fileSize, 0, fileBytes, 2, 4);
                        // Reserved
                        fileBytes[6] = 0; fileBytes[7] = 0; fileBytes[8] = 0; fileBytes[9] = 0;
                        // Pixel data offset
                        var pdo = BitConverter.GetBytes(pixelDataOffset);
                        Array.Copy(pdo, 0, fileBytes, 10, 4);
                        // DIB data
                        Array.Copy(bitmapData, 0, fileBytes, fileHeaderSize, bitmapData.Length);

                        using var ms = new MemoryStream(fileBytes);
                        return new Bitmap(ms);
                    }
                    else
                    {
                        // Keep iterating
                    }
                }
                break;
            }
        }
        return null;
    }

    /// <summary>
    /// Delegates to IGfxImportService to replace a single bitmap resource inside an EGF file.
    /// </summary>
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
