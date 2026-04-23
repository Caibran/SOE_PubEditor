using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;

namespace SOE_PubEditor.Services;

/// <summary>
/// Service for loading graphics from EGF (PE format) files.
/// EGF files are Windows PE format with embedded bitmap resources.
/// </summary>
public class GfxService : IGfxService
{
    private readonly ConcurrentDictionary<(GfxType, int), Bitmap?> _cache = new();
    private string? _gfxDirectory;
    
    public string? GfxDirectory => _gfxDirectory;
    
    public void SetGfxDirectory(string path)
    {
        if (_gfxDirectory != path)
        {
            _gfxDirectory = path;
            ClearCache();
        }
    }
    
    public bool IsGfxDirectoryValid()
    {
        if (string.IsNullOrEmpty(_gfxDirectory) || !Directory.Exists(_gfxDirectory))
            return false;
            
        // Check for at least one expected GFX file
        var itemsFile = GetGfxFilePath(GfxType.Items);
        return File.Exists(itemsFile);
    }
    
    public Bitmap? LoadBitmap(GfxType type, int resourceId)
    {
        if (string.IsNullOrEmpty(_gfxDirectory))
            return null;
        
        // EGF resource IDs use specific formulas based on type:
        // Items: 2 * Graphic for inventory icons (even), 2 * Graphic - 1 for ground icons (odd)
        // NPCs: Each NPC has 40 frames. Formula: (Graphic - 1) * 40 + frame_offset
        //       Frame offsets: 1-2 standing, 5-8 walk right/down, 9-12 walk left/up, 13-16 attack
        //       We use frame 1 (standing, facing down/right) for preview
        // Additionally, EndlessClient adds +100 to all resource IDs (see NativeGraphicsLoader.cs)
        int peResourceId;
        switch (type)
        {
            case GfxType.Items:
                // Formula: (2 * Graphic) + 100 for inventory icons
                peResourceId = (2 * resourceId) + 100;
                break;
            case GfxType.NPC:
                // Formula: (Graphic - 1) * 40 + frame_offset + 100, using frame 1 for standing pose
                peResourceId = ((resourceId - 1) * 40) + 1 + 100;
                break;
            case GfxType.Spells:
                // Spell effects have 3 layers per spell. Formula: (Graphic - 1) * 3 + layer_offset + 100
                // We use layer 1 (first effect layer) for preview
                // NOTE: Resource IDs in gfx024 are sparse, so we use fallback search
                peResourceId = ((resourceId - 1) * 3) + 1 + 100;
                break;
            case GfxType.SpellIcons:
                // Simple formula: resourceId + 100 (for spell bar icons)
                peResourceId = resourceId + 100;
                break;
            case GfxType.MaleWeapon:
                // Weapons: graphicId * 100 + frame (frame 1 for standing)
                peResourceId = (resourceId * 100) + 1;
                break;
            case GfxType.MaleArmor:
            case GfxType.FemaleArmor:
            case GfxType.MaleBack:
            case GfxType.FemaleBack:
                // Armor/Shield: (graphicId - 1) * 50 + frame + 100 (frame 1 for standing)
                peResourceId = ((resourceId - 1) * 50) + 1 + 100;
                break;
            case GfxType.MaleBoots:
            case GfxType.FemaleBoots:
                // Boots: (graphicId - 1) * 40 + frame + 100 (frame 1 for standing)
                peResourceId = ((resourceId - 1) * 40) + 1 + 100;
                break;
            case GfxType.MaleHat:
            case GfxType.FemaleHat:
                // Hats: (graphicId - 1) * 10 + frame + 100 (frame 1 for standing)
                peResourceId = ((resourceId - 1) * 10) + 1 + 100;
                break;
            default:
                // Generic formula: resourceId + 100
                peResourceId = resourceId + 100;
                break;
        }
            
        var cacheKey = (type, resourceId);
        
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;
        
        Bitmap? bitmap = null;
        
        // For Spells, try fallback search if calculated ID not found
        if (type == GfxType.Spells)
        {
            bitmap = LoadBitmapFromFile(type, peResourceId);
            if (bitmap == null)
            {
                // Try nearby resource IDs: +1, +2, +3, -1, -2
                int[] offsets = { 1, 2, 3, -1, -2, 4, 5 };
                foreach (var offset in offsets)
                {
                    var altId = peResourceId + offset;
                    Console.WriteLine($"GFX FALLBACK: Trying alternate resource ID {altId} (offset {offset:+#;-#;0})");
                    bitmap = LoadBitmapFromFile(type, altId);
                    if (bitmap != null)
                    {
                        Console.WriteLine($"GFX FALLBACK: Found resource at ID {altId}");
                        break;
                    }
                }
            }
        }
        else
        {
            bitmap = LoadBitmapFromFile(type, peResourceId);
        }
        
        _cache.TryAdd(cacheKey, bitmap);
        return bitmap;
    }
    
    /// <summary>
    /// Loads a bitmap from the specified GFX file using a raw PE resource ID.
    /// Unlike LoadBitmap, this does not apply any formula transformation.
    /// </summary>
    public Bitmap? LoadBitmapByResourceId(GfxType type, int rawResourceId)
    {
        if (string.IsNullOrEmpty(_gfxDirectory))
            return null;
        
        // The PE resource ID is just the raw value (already formulated by caller)
        // We don't add +100 again here - the caller should include it in the formula
        var cacheKey = (type, rawResourceId);
        
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;
        
        var bitmap = LoadBitmapFromFile(type, rawResourceId);
        _cache.TryAdd(cacheKey, bitmap);
        return bitmap;
    }
    
    public void ClearCache()
    {
        foreach (var kvp in _cache)
        {
            kvp.Value?.Dispose();
        }
        _cache.Clear();
    }
    
    /// <summary>
    /// Finds the next available doll graphic ID for a given equipment GFX type.
    /// Scans existing resources and returns max + 1.
    /// </summary>
    public int GetNextAvailableDollGraphicId(GfxType type)
    {
        var existingIds = GetAvailableResourceIds(type);
        if (existingIds.Count == 0)
            return 1; // Start at 1 if empty (0 causes Windows PE resource issues)
        return existingIds.Max() + 1;
    }
    
    /// <summary>
    /// Finds the next available inventory/ground graphic ID (for gfx023).
    /// </summary>
    public int GetNextAvailableItemGraphicId()
    {
        var existingIds = GetAvailableResourceIds(GfxType.Items);
        if (existingIds.Count == 0)
            return 1;
        return existingIds.Max() + 1;
    }
    
    public List<int> GetAvailableResourceIds(GfxType type)
    {
        var ids = new List<int>();
        if (string.IsNullOrEmpty(_gfxDirectory))
            return ids;
            
        var filePath = GetGfxFilePath(type);
        if (!File.Exists(filePath))
            return ids;
            
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);
            
            // Read DOS header
            if (reader.ReadUInt16() != 0x5A4D) return ids; // "MZ"
            
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peHeaderOffset = reader.ReadUInt32();
            fs.Seek(peHeaderOffset, SeekOrigin.Begin);
            
            if (reader.ReadUInt32() != 0x00004550) return ids; // "PE\0\0"
            
            // Skip COFF header
            reader.ReadUInt16(); // Machine
            var numberOfSections = reader.ReadUInt16();
            reader.ReadUInt32(); // TimeDateStamp
            reader.ReadUInt32(); // PointerToSymbolTable
            reader.ReadUInt32(); // NumberOfSymbols
            var sizeOfOptionalHeader = reader.ReadUInt16();
            reader.ReadUInt16(); // Characteristics
            
            // Find optional header and resource section
            var optionalHeaderStart = fs.Position;
            var magic = reader.ReadUInt16();
            var is64bit = magic == 0x20B;
            
            // Skip to data directories
            fs.Seek(optionalHeaderStart + (is64bit ? 112 : 96), SeekOrigin.Begin);
            
            // Get resource directory RVA (3rd entry)
            reader.ReadUInt64(); // Export table
            reader.ReadUInt64(); // Import table
            var resourceRVA = reader.ReadUInt32();
            var resourceSize = reader.ReadUInt32();
            
            if (resourceRVA == 0) return ids;
            
            // Find section containing resources
            fs.Seek(optionalHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);
            
            for (int i = 0; i < numberOfSections; i++)
            {
                var sectionName = new string(reader.ReadChars(8)).TrimEnd('\0');
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                reader.ReadUInt32(); // SizeOfRawData
                var pointerToRawData = reader.ReadUInt32();
                reader.ReadBytes(16); // Skip rest
                
                if (resourceRVA >= virtualAddress && resourceRVA < virtualAddress + virtualSize)
                {
                    var resourceFileOffset = pointerToRawData + (resourceRVA - virtualAddress);
                    ids = EnumerateResourceIds(reader, fs, resourceFileOffset, resourceRVA);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating resources: {ex.Message}");
        }
        
        // Convert PE resource IDs to logical graphic IDs based on type
        // Using the reverse of the formulas in LoadBitmap
        var graphicIds = new HashSet<int>();
        foreach (var peId in ids)
        {
            int? graphicId = type switch
            {
                // Items: peId = (2 * graphicId) + 100
                // Reverse: graphicId = (peId - 100) / 2 (only for even peIds >= 100)
                GfxType.Items when peId >= 100 && (peId - 100) % 2 == 0 => (peId - 100) / 2,
                
                // NPCs: peId = (graphicId - 1) * 40 + frameOffset + 100
                // Frame offset ranges from 1-40, so we find which "base" this belongs to
                // Reverse: graphicId = ((peId - 100 - 1) / 40) + 1 (for frame 1 entries)
                GfxType.NPC when peId >= 101 => ((peId - 101) / 40) + 1,
                
                // Spells: peId = (graphicId - 1) * 3 + layerOffset + 100
                // layerOffset is 1-3, so: graphicId = ((peId - 100 - 1) / 3) + 1
                GfxType.Spells when peId >= 101 => ((peId - 101) / 3) + 1,
                
                // SpellIcons: peId = graphicId + 100
                // Reverse: graphicId = peId - 100
                GfxType.SpellIcons when peId >= 100 => peId - 100,
                
                // Weapons: peId = graphicId * 100 + frame (frame 1-100)
                // Reverse: graphicId = peId / 100 (when peId >= 100)
                GfxType.MaleWeapon when peId >= 100 => peId / 100,
                
                // Armor/Shield: peId = (graphicId - 1) * 50 + frame + 100 (frame 1-50)
                // Reverse: graphicId = (peId - 101) / 50 + 1
                GfxType.MaleArmor when peId >= 101 => ((peId - 101) / 50) + 1,
                GfxType.FemaleArmor when peId >= 101 => ((peId - 101) / 50) + 1,
                GfxType.MaleBack when peId >= 101 => ((peId - 101) / 50) + 1,
                GfxType.FemaleBack when peId >= 101 => ((peId - 101) / 50) + 1,
                
                // Boots: peId = (graphicId - 1) * 40 + frame + 100 (frame 1-40)
                // Reverse: graphicId = (peId - 101) / 40 + 1
                GfxType.MaleBoots when peId >= 101 => ((peId - 101) / 40) + 1,
                GfxType.FemaleBoots when peId >= 101 => ((peId - 101) / 40) + 1,
                
                // Hats: peId = (graphicId - 1) * 10 + frame + 100 (frame 1-10)
                // Reverse: graphicId = (peId - 101) / 10 + 1
                GfxType.MaleHat when peId >= 101 => ((peId - 101) / 10) + 1,
                GfxType.FemaleHat when peId >= 101 => ((peId - 101) / 10) + 1,
                
                _ => null
            };
            
            if (graphicId.HasValue && graphicId.Value > 0)
            {
                graphicIds.Add(graphicId.Value);
            }
        }
        
        return graphicIds.OrderBy(id => id).ToList();
    }
    
    private List<int> EnumerateResourceIds(BinaryReader reader, FileStream fs, uint resourceFileOffset, uint resourceRVA)
    {
        var ids = new List<int>();
        
        // Navigate to RT_BITMAP (type 2) in resource directory
        fs.Seek(resourceFileOffset, SeekOrigin.Begin);
        
        reader.ReadUInt32(); // Characteristics
        reader.ReadUInt32(); // TimeDateStamp
        reader.ReadUInt16(); // MajorVersion
        reader.ReadUInt16(); // MinorVersion
        var numberOfNamedEntries = reader.ReadUInt16();
        var numberOfIdEntries = reader.ReadUInt16();
        
        // Look for RT_BITMAP (type 2)
        for (int i = 0; i < numberOfNamedEntries + numberOfIdEntries; i++)
        {
            var typeId = reader.ReadUInt32();
            var offsetToData = reader.ReadUInt32();
            
            if (typeId == 2) // RT_BITMAP
            {
                var bitmapDirOffset = resourceFileOffset + (offsetToData & 0x7FFFFFFF);
                fs.Seek(bitmapDirOffset, SeekOrigin.Begin);
                
                reader.ReadUInt32(); // Characteristics
                reader.ReadUInt32(); // TimeDateStamp
                reader.ReadUInt16(); // MajorVersion
                reader.ReadUInt16(); // MinorVersion
                var numNamed = reader.ReadUInt16();
                var numId = reader.ReadUInt16();
                
                for (int j = 0; j < numNamed + numId; j++)
                {
                    var resId = reader.ReadUInt32();
                    reader.ReadUInt32(); // Skip offset
                    ids.Add((int)resId);
                }
                break;
            }
        }
        
        return ids.OrderBy(id => id).ToList();
    }
    
    public string GetGfxFilePath(GfxType type)
    {
        var fileNumber = (int)type;
        return Path.Combine(_gfxDirectory!, $"gfx{fileNumber:D3}.egf");
    }

    public List<int> GetRawBitmapResourceIds(string egfPath)
    {
        var ids = new List<int>();
        if (!File.Exists(egfPath)) return ids;
        try
        {
            using var fs = new FileStream(egfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);
            if (reader.ReadUInt16() != 0x5A4D) return ids;
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOff = reader.ReadUInt32();
            fs.Seek(peOff, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return ids;
            reader.ReadUInt16(); var nSections = reader.ReadUInt16();
            reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32();
            var optHdrSize = reader.ReadUInt16(); reader.ReadUInt16();
            var optStart = fs.Position;
            var magic = reader.ReadUInt16();
            var is64 = magic == 0x20B;
            fs.Seek(optStart + (is64 ? 112 : 96), SeekOrigin.Begin);
            reader.ReadUInt64(); reader.ReadUInt64();
            var rsrcRva = reader.ReadUInt32(); reader.ReadUInt32();
            if (rsrcRva == 0) return ids;
            fs.Seek(optStart + optHdrSize, SeekOrigin.Begin);
            for (int s = 0; s < nSections; s++)
            {
                reader.ReadBytes(8); var vSize = reader.ReadUInt32(); var va = reader.ReadUInt32();
                reader.ReadUInt32(); var rawOff = reader.ReadUInt32(); reader.ReadBytes(16);
                if (rsrcRva >= va && rsrcRva < va + vSize)
                {
                    ids = EnumerateResourceIds(reader, fs, rawOff + (rsrcRva - va), rsrcRva);
                    break;
                }
            }
        }
        catch { /* return whatever we have */ }
        return ids;
    }

    public Bitmap? LoadBitmapFromEgfPath(string egfPath, int resourceId)
    {
        if (!File.Exists(egfPath)) return null;
        return LoadBitmapFromPE(egfPath, resourceId);
    }
    
    private Bitmap? LoadBitmapFromFile(GfxType type, int resourceId)
    {
        try
        {
            var filePath = GetGfxFilePath(type);
            Console.WriteLine($"GFX: Loading from {filePath}, resourceId={resourceId}");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("GFX: File does not exist");
                return null;
            }
                
            // EGF files are PE format - we need to extract bitmap resources
            return LoadBitmapFromPE(filePath, resourceId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GFX: Exception: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Loads a bitmap resource from a PE (EGF) file.
    /// </summary>
    private Bitmap? LoadBitmapFromPE(string filePath, int resourceId)
    {
        // PE files contain bitmap resources that we need to extract
        // The resource section contains BITMAPINFO structures
        
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);
            
            // Read DOS header
            if (reader.ReadUInt16() != 0x5A4D) // "MZ"
            {
                Console.WriteLine("GFX PE: Not a valid MZ executable");
                return null;
            }
                
            // Seek to PE header offset (at 0x3C)
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            
            // Read PE signature
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x4550) // "PE\0\0"
            {
                Console.WriteLine("GFX PE: Not a valid PE file");
                return null;
            }
                
            // Read COFF header
            var machine = reader.ReadUInt16();
            var numberOfSections = reader.ReadUInt16();
            reader.ReadUInt32(); // TimeDateStamp
            reader.ReadUInt32(); // PointerToSymbolTable
            reader.ReadUInt32(); // NumberOfSymbols
            var sizeOfOptionalHeader = reader.ReadUInt16();
            reader.ReadUInt16(); // Characteristics
            
            // Skip optional header to get to section headers
            var optionalHeaderStart = fs.Position;
            
            // Read magic to determine PE32 or PE32+
            var magic = reader.ReadUInt16();
            var isPE32Plus = magic == 0x20B;
            Console.WriteLine($"GFX PE: PE32+ = {isPE32Plus}, sections = {numberOfSections}");
            
            // Skip to data directories
            // PE32: offset 96 from optional header start
            // PE32+: offset 112 from optional header start
            var dataDirectoryOffset = isPE32Plus ? 112 : 96;
            fs.Seek(optionalHeaderStart + dataDirectoryOffset, SeekOrigin.Begin);
            
            // Read resource directory (index 2)
            fs.Seek(16, SeekOrigin.Current); // Skip export (0) and import (1) directories (8 bytes each)
            var resourceRVA = reader.ReadUInt32();
            var resourceSize = reader.ReadUInt32();
            
            Console.WriteLine($"GFX PE: Resource RVA = 0x{resourceRVA:X}, Size = {resourceSize}");
            
            if (resourceRVA == 0)
            {
                Console.WriteLine("GFX PE: No resource section found");
                return null;
            }
                
            // Skip to section headers
            fs.Seek(optionalHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);
            
            // Find the section containing the resource directory
            uint resourceFileOffset = 0;
            for (int i = 0; i < numberOfSections; i++)
            {
                var sectionName = new byte[8];
                reader.Read(sectionName, 0, 8);
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                var sizeOfRawData = reader.ReadUInt32();
                var pointerToRawData = reader.ReadUInt32();
                reader.ReadBytes(16); // Skip rest of section header
                
                if (resourceRVA >= virtualAddress && resourceRVA < virtualAddress + virtualSize)
                {
                    resourceFileOffset = pointerToRawData + (resourceRVA - virtualAddress);
                    Console.WriteLine($"GFX PE: Found resource section at file offset 0x{resourceFileOffset:X}");
                    break;
                }
            }
            
            if (resourceFileOffset == 0)
            {
                Console.WriteLine("GFX PE: Could not find resource section");
                return null;
            }
                
            // Parse resource directory to find bitmap
            return ParseResourceDirectory(reader, fs, resourceFileOffset, resourceRVA, resourceId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GFX PE: Exception: {ex.Message}");
            return null;
        }
    }
    
    private Bitmap? ParseResourceDirectory(BinaryReader reader, FileStream fs, uint resourceFileOffset, uint resourceRVA, int targetResourceId)
    {
        // This is a simplified resource parser - full implementation would need
        // to traverse the resource tree structure properly
        
        fs.Seek(resourceFileOffset, SeekOrigin.Begin);
        
        // IMAGE_RESOURCE_DIRECTORY
        reader.ReadUInt32(); // Characteristics
        reader.ReadUInt32(); // TimeDateStamp
        reader.ReadUInt16(); // MajorVersion
        reader.ReadUInt16(); // MinorVersion
        var numberOfNamedEntries = reader.ReadUInt16();
        var numberOfIdEntries = reader.ReadUInt16();
        
        Console.WriteLine($"GFX RES: Named entries={numberOfNamedEntries}, ID entries={numberOfIdEntries}");
        
        // Look for RT_BITMAP (type 2)
        for (int i = 0; i < numberOfNamedEntries + numberOfIdEntries; i++)
        {
            var nameOrId = reader.ReadUInt32();
            var offsetToData = reader.ReadUInt32();
            
            Console.WriteLine($"GFX RES: Entry {i}: nameOrId={nameOrId}, offset=0x{offsetToData:X}");
            
            if (nameOrId == 2) // RT_BITMAP
            {
                Console.WriteLine("GFX RES: Found RT_BITMAP directory");
                // This is a directory - follow it
                var subdirOffset = resourceFileOffset + (offsetToData & 0x7FFFFFFF);
                return FindBitmapResource(reader, fs, subdirOffset, resourceFileOffset, resourceRVA, targetResourceId);
            }
        }
        
        Console.WriteLine("GFX RES: RT_BITMAP (type 2) not found in resource directory");
        return null;
    }
    
    private Bitmap? FindBitmapResource(BinaryReader reader, FileStream fs, uint dirOffset, uint resourceFileOffset, uint resourceRVA, int targetResourceId)
    {
        fs.Seek(dirOffset, SeekOrigin.Begin);
        
        reader.ReadUInt32(); // Characteristics
        reader.ReadUInt32(); // TimeDateStamp
        reader.ReadUInt16(); // MajorVersion
        reader.ReadUInt16(); // MinorVersion
        var numberOfNamedEntries = reader.ReadUInt16();
        var numberOfIdEntries = reader.ReadUInt16();
        
        Console.WriteLine($"GFX FIND: Looking for ID {targetResourceId} in {numberOfNamedEntries + numberOfIdEntries} entries");
        
        // Debug: Scan all entries to find ones near the target
        var savedPos = fs.Position;
        Console.WriteLine($"GFX FIND: First 5 IDs and IDs near target ({targetResourceId}):");
        var allIds = new List<uint>();
        for (int d = 0; d < numberOfNamedEntries + numberOfIdEntries; d++)
        {
            var debugId = reader.ReadUInt32();
            reader.ReadUInt32(); // skip offset
            allIds.Add(debugId);
            if (d < 5 || Math.Abs((int)debugId - targetResourceId) <= 5)
            {
                Console.WriteLine($"GFX FIND: Entry {d}: ID={debugId}");
            }
        }
        // Also show the count of IDs in ranges
        Console.WriteLine($"GFX FIND: IDs 1-100: {allIds.Count(id => id >= 1 && id <= 100)}, IDs 101-500: {allIds.Count(id => id >= 101 && id <= 500)}, IDs 501+: {allIds.Count(id => id > 500)}");
        fs.Seek(savedPos, SeekOrigin.Begin);
        
        for (int i = 0; i < numberOfNamedEntries + numberOfIdEntries; i++)
        {
            var nameOrId = reader.ReadUInt32();
            var offsetToData = reader.ReadUInt32();
            
            if (nameOrId == (uint)targetResourceId)
            {
                Console.WriteLine($"GFX FIND: Found target resource ID {targetResourceId}");
                // Found our resource - now get the actual data
                var dataEntryOffset = resourceFileOffset + (offsetToData & 0x7FFFFFFF);
                
                // Navigate to language directory first
                fs.Seek(dataEntryOffset, SeekOrigin.Begin);
                
                if ((offsetToData & 0x80000000) != 0)
                {
                    Console.WriteLine("GFX FIND: Following language directory");
                    // It's a directory, get first entry
                    reader.ReadUInt32(); // Characteristics
                    reader.ReadUInt32(); // TimeDateStamp
                    reader.ReadUInt16(); // MajorVersion
                    reader.ReadUInt16(); // MinorVersion
                    var numNamed = reader.ReadUInt16();
                    var numId = reader.ReadUInt16();
                    
                    Console.WriteLine($"GFX FIND: Language dir has {numNamed + numId} entries");
                    
                    if (numNamed + numId > 0)
                    {
                        reader.ReadUInt32(); // Skip name/id
                        var langOffset = reader.ReadUInt32();
                        dataEntryOffset = resourceFileOffset + (langOffset & 0x7FFFFFFF);
                    }
                }
                
                fs.Seek(dataEntryOffset, SeekOrigin.Begin);
                var dataRVA = reader.ReadUInt32();
                var dataSize = reader.ReadUInt32();
                
                Console.WriteLine($"GFX FIND: Data RVA=0x{dataRVA:X}, Size={dataSize}");
                
                // Calculate file offset from RVA
                var dataFileOffset = resourceFileOffset + (dataRVA - resourceRVA);
                
                Console.WriteLine($"GFX FIND: Loading from file offset 0x{dataFileOffset:X}");
                
                return LoadBitmapData(reader, fs, dataFileOffset, dataSize);
            }
        }
        
        Console.WriteLine($"GFX FIND: Resource ID {targetResourceId} not found");
        return null;
    }
    
    private Bitmap? LoadBitmapData(BinaryReader reader, FileStream fs, uint dataOffset, uint dataSize)
    {
        try
        {
            fs.Seek(dataOffset, SeekOrigin.Begin);
            
            // Resource bitmaps don't have the BITMAPFILEHEADER, just BITMAPINFO
            // We need to construct a valid BMP file
            var bitmapInfo = reader.ReadBytes((int)dataSize);
            
            // Read BITMAPINFOHEADER to get dimensions and compression
            using var infoReader = new BinaryReader(new MemoryStream(bitmapInfo));
            var headerSize = infoReader.ReadUInt32();
            var width = infoReader.ReadInt32();
            var height = infoReader.ReadInt32();
            infoReader.ReadUInt16(); // planes
            var bitsPerPixel = infoReader.ReadUInt16();
            var compression = infoReader.ReadUInt32(); // 0=BI_RGB, 3=BI_BITFIELDS
            
            Console.WriteLine($"GFX BMP: headerSize={headerSize}, width={width}, height={height}, bpp={bitsPerPixel}, compression={compression}, dataSize={dataSize}");
            
            // Calculate pixel data offset
            // The offset depends on the header size and color table/masks
            uint pixelDataOffset = 14 + headerSize; // 14 = BITMAPFILEHEADER size
            
            // For BI_BITFIELDS compression (type 3), there are color masks after the header
            if (compression == 3 && headerSize == 40)
            {
                // BITMAPINFOHEADER with separate color masks (12 bytes for RGB)
                pixelDataOffset += 12;
                Console.WriteLine("GFX BMP: Adding 12 bytes for BI_BITFIELDS color masks");
            }
            
            // For 8-bit or less, there's a color table
            if (bitsPerPixel <= 8)
            {
                uint colorTableSize = (uint)(1 << bitsPerPixel) * 4; // Each entry is 4 bytes (RGBQUAD)
                pixelDataOffset += colorTableSize;
                Console.WriteLine($"GFX BMP: Adding {colorTableSize} bytes for color table");
            }
            
            // Create BMP file with header
            using var bmpStream = new MemoryStream();
            using var bmpWriter = new BinaryWriter(bmpStream);
            
            // BITMAPFILEHEADER (14 bytes)
            bmpWriter.Write((ushort)0x4D42); // "BM"
            bmpWriter.Write((uint)(14 + dataSize)); // File size
            bmpWriter.Write((ushort)0); // Reserved
            bmpWriter.Write((ushort)0); // Reserved
            bmpWriter.Write(pixelDataOffset); // Offset to pixel data
            
            // Write the BITMAPINFO data
            bmpWriter.Write(bitmapInfo);
            
            bmpStream.Seek(0, SeekOrigin.Begin);
            var bitmap = new Bitmap(bmpStream);
            Console.WriteLine($"GFX BMP: Successfully created bitmap {bitmap.Size}");
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GFX BMP: Exception creating bitmap: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Extracts a representative frame from a spritesheet bitmap.
    /// Spell effects are stored as horizontal spritesheets with multiple animation frames.
    /// Uses the middle frame instead of first frame since first frames are often empty/black.
    /// </summary>
    public Bitmap? ExtractFirstFrame(Bitmap? spritesheet, int? frameWidth = null)
    {
        if (spritesheet == null)
            return null;
            
        try
        {
            int sheetWidth = (int)spritesheet.Size.Width;
            int sheetHeight = (int)spritesheet.Size.Height;
            
            Console.WriteLine($"GFX FRAME: Sheet size {sheetWidth}x{sheetHeight}");
            
            // Determine frame/crop dimensions
            int width;
            int numFrames;
            
            if (frameWidth.HasValue)
            {
                // Use explicitly specified frame width
                width = frameWidth.Value;
                numFrames = sheetWidth / width;
                Console.WriteLine($"GFX FRAME: Using specified width {width}, {numFrames} frames");
            }
            else
            {
                // Hybrid approach:
                // 1. Try to find a valid frame division with good aspect ratio (nearly square)
                // 2. If no good frame division found, use centered crop
                
                int[] commonFrameCounts = { 4, 5, 6, 8, 10, 3, 7, 12, 2 };
                
                int bestWidth = -1;
                int bestFrameCount = 1;
                double bestAspectDiff = double.MaxValue;
                
                foreach (var count in commonFrameCounts)
                {
                    if (sheetWidth % count == 0)
                    {
                        int candidateWidth = sheetWidth / count;
                        double aspectRatio = (double)candidateWidth / sheetHeight;
                        
                        // Accept frames with aspect ratio between 0.7 and 1.5 (reasonably square)
                        if (aspectRatio >= 0.7 && aspectRatio <= 1.5)
                        {
                            double aspectDiff = Math.Abs(aspectRatio - 1.0); // How close to square
                            if (aspectDiff < bestAspectDiff)
                            {
                                bestWidth = candidateWidth;
                                bestFrameCount = count;
                                bestAspectDiff = aspectDiff;
                            }
                        }
                    }
                }
                
                // Also check if sheet naturally divides by height (square frames)
                if (sheetWidth % sheetHeight == 0)
                {
                    int count = sheetWidth / sheetHeight;
                    if (count >= 2 && count <= 12)
                    {
                        bestWidth = sheetHeight;
                        bestFrameCount = count;
                        Console.WriteLine($"GFX FRAME: Perfect square frames: {count} frames of {sheetHeight}x{sheetHeight}");
                    }
                }
                
                if (bestWidth > 0)
                {
                    // Found a good frame division - use middle frame extraction
                    width = bestWidth;
                    numFrames = bestFrameCount;
                    Console.WriteLine($"GFX FRAME: Detected {numFrames} frames of {width}x{sheetHeight} (aspect: {(double)width/sheetHeight:F2})");
                }
                else
                {
                    // No good frame division - use centered crop
                    // Make the crop width based on height for wide sheets, or full width otherwise
                    if (sheetWidth > sheetHeight * 1.5)
                    {
                        width = sheetHeight; // Square-ish centered crop
                        Console.WriteLine($"GFX FRAME: No good frames, using centered {width}x{sheetHeight} crop");
                    }
                    else
                    {
                        width = sheetWidth; // Not that wide, show it all
                        Console.WriteLine($"GFX FRAME: No good frames, showing full {width}x{sheetHeight}");
                    }
                    numFrames = 1;
                }
            }
            
            int height = sheetHeight;
            
            // For single-frame or centered crop fallback, do a centered crop
            if (numFrames <= 1)
            {
                // If sheet is wider than our target width, do a centered crop
                if (sheetWidth > width)
                {
                    int centerXOffset = (sheetWidth - width) / 2;
                    Console.WriteLine($"GFX CROP: Centered crop at x={centerXOffset} ({width}x{height})");
                    
                    var centerPixelSize = new Avalonia.PixelSize(width, height);
                    var centerDpi = new Avalonia.Vector(96, 96);
                    var centerRenderTarget = new Avalonia.Media.Imaging.RenderTargetBitmap(centerPixelSize, centerDpi);
                    
                    using (var ctx = centerRenderTarget.CreateDrawingContext())
                    {
                        var cropped = new CroppedBitmap(spritesheet, new Avalonia.PixelRect(centerXOffset, 0, width, height));
                        ctx.DrawImage(cropped, new Avalonia.Rect(0, 0, width, height));
                    }
                    
                    using var memStream = new MemoryStream();
                    centerRenderTarget.Save(memStream);
                    memStream.Seek(0, SeekOrigin.Begin);
                    return new Bitmap(memStream);
                }
                
                Console.WriteLine($"GFX CROP: Single frame detected, returning as-is");
                return spritesheet;
            }
            
            // Pick middle frame (where the effect is usually visible)
            int targetFrame = numFrames / 2;
            int frameXOffset = targetFrame * width;
            
            // Ensure we don't exceed bounds
            if (frameXOffset + width > sheetWidth)
                frameXOffset = Math.Max(0, sheetWidth - width);
            
            Console.WriteLine($"GFX CROP: Extracting frame {targetFrame + 1}/{numFrames} at x={frameXOffset} ({width}x{height})");
            
            // Use RenderTargetBitmap to crop the image
            var pixelSize = new Avalonia.PixelSize(width, height);
            var dpi = new Avalonia.Vector(96, 96);
            var renderTarget = new Avalonia.Media.Imaging.RenderTargetBitmap(pixelSize, dpi);
            
            using (var context = renderTarget.CreateDrawingContext())
            {
                var croppedBitmap = new CroppedBitmap(spritesheet, new Avalonia.PixelRect(frameXOffset, 0, width, height));
                context.DrawImage(croppedBitmap, new Avalonia.Rect(0, 0, width, height));
            }
            
            // Convert RenderTargetBitmap to regular Bitmap
            using var ms = new MemoryStream();
            renderTarget.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            var result = new Bitmap(ms);
            
            Console.WriteLine($"GFX CROP: Result bitmap size: {result.Size}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GFX CROP: Error extracting frame: {ex.Message}");
            return spritesheet;
        }
    }
}
