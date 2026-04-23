using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace SOE_PubEditor.Services;

/// <summary>
/// GFX file types matching Endless Online's gfx###.egf file naming.
/// </summary>
public enum GfxType
{
    // Equipment graphics (011-020)
    MaleBoots = 11,       // gfx011.egf - Male character boots
    FemaleBoots = 12,     // gfx012.egf - Female character boots
    MaleArmor = 13,       // gfx013.egf - Male character armor  
    FemaleArmor = 14,     // gfx014.egf - Female character armor
    MaleHat = 15,         // gfx015.egf - Male character hats
    FemaleHat = 16,       // gfx016.egf - Female character hats
    MaleWeapon = 17,      // gfx017.egf - Male character weapons
    FemaleWeapon = 18,    // gfx018.egf - Female character weapons
    MaleBack = 19,        // gfx019.egf - Male shields/capes
    FemaleBack = 20,      // gfx020.egf - Female shields/capes
    
    // Entity graphics (021-025)
    NPC = 21,             // gfx021.egf - NPC sprites
    Items = 23,           // gfx023.egf - Item icons
    Spells = 24,          // gfx024.egf - Spell effects
    SpellIcons = 25       // gfx025.egf - Spell icons (for UI)
}

/// <summary>
/// Service for loading graphics from EGF (PE format) files.
/// </summary>
public interface IGfxService
{
    /// <summary>
    /// Sets the GFX directory containing gfx###.egf files.
    /// </summary>
    void SetGfxDirectory(string path);
    
    /// <summary>
    /// Gets the currently set GFX directory.
    /// </summary>
    string? GfxDirectory { get; }
    
    /// <summary>
    /// Loads a bitmap from the specified GFX file and resource ID.
    /// </summary>
    /// <param name="type">The GFX file type.</param>
    /// <param name="resourceId">The resource ID within the file.</param>
    /// <returns>The loaded bitmap, or null if not found.</returns>
    Bitmap? LoadBitmap(GfxType type, int resourceId);
    
    /// <summary>
    /// Checks if the GFX directory is valid (contains expected files).
    /// </summary>
    bool IsGfxDirectoryValid();
    
    /// <summary>
    /// Loads a bitmap from the specified GFX file using a raw PE resource ID.
    /// Unlike LoadBitmap, this does not apply any formula transformation - it uses the ID directly (+100 for PE resources).
    /// </summary>
    /// <param name="type">The GFX file type.</param>
    /// <param name="rawResourceId">The raw resource ID (PE resource ID will be this value + 100).</param>
    /// <returns>The loaded bitmap, or null if not found.</returns>
    Bitmap? LoadBitmapByResourceId(GfxType type, int rawResourceId);
    
    /// <summary>
    /// Clears the bitmap cache.
    /// </summary>
    void ClearCache();
    
    /// <summary>
    /// Gets all available resource IDs from a GFX file.
    /// </summary>
    /// <param name="type">The GFX file type.</param>
    /// <returns>List of available resource IDs.</returns>
    List<int> GetAvailableResourceIds(GfxType type);
    
    /// <summary>
    /// Gets the full path to a GFX file.
    /// </summary>
    /// <param name="type">The GFX file type.</param>
    /// <returns>The full path to the EGF file.</returns>
    string GetGfxFilePath(GfxType type);
    
    /// <summary>
    /// Extracts the first frame from a spritesheet bitmap.
    /// Spell effects are stored as horizontal spritesheets with multiple animation frames.
    /// </summary>
    /// <param name="spritesheet">The full spritesheet bitmap.</param>
    /// <param name="frameWidth">Optional frame width. If not specified, uses the height (assumes square frames).</param>
    /// <returns>A new bitmap containing only the first frame.</returns>
    Bitmap? ExtractFirstFrame(Bitmap? spritesheet, int? frameWidth = null);
    
    /// <summary>
    /// Finds the next available doll graphic ID for a given equipment GFX type.
    /// Scans existing resources and returns max + 1.
    /// </summary>
    /// <param name="type">The equipment GFX file type.</param>
    /// <returns>The next available doll graphic ID.</returns>
    int GetNextAvailableDollGraphicId(GfxType type);
    
    /// <summary>
    /// Finds the next available inventory/ground graphic ID (for gfx023).
    /// </summary>
    /// <returns>The next available item graphic ID.</returns>
    int GetNextAvailableItemGraphicId();

    /// <summary>
    /// Returns all raw RT_BITMAP resource IDs present in an EGF file by path.
    /// Does not apply any formula transformation.
    /// </summary>
    List<int> GetRawBitmapResourceIds(string egfPath);

    /// <summary>
    /// Loads a bitmap from any EGF file by full path and raw PE resource ID.
    /// Does not apply any formula transformation or cache the result.
    /// </summary>
    Bitmap? LoadBitmapFromEgfPath(string egfPath, int resourceId);
}
