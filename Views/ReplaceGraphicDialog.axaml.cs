using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Moffat.EndlessOnline.SDK.Protocol.Pub;
using SOE_PubEditor.Models;
using SOE_PubEditor.Services;

namespace SOE_PubEditor.Views;

/// <summary>
/// Represents a single graphic frame slot that can be replaced.
/// </summary>
public class GraphicSlot
{
    public string Label { get; set; } = "";
    public GfxType GfxType { get; set; }
    public int ResourceId { get; set; }
    public string? SelectedFilePath { get; set; }
    public Bitmap? CurrentPreview { get; set; }
}

/// <summary>
/// Dialog for replacing individual graphic frames with BMP files.
/// Dynamically builds UI based on item/NPC/spell type.
/// </summary>
public partial class ReplaceGraphicDialog : Window
{
    private readonly IGfxService _gfxService;
    private readonly List<GraphicSlot> _slots = new();
    private readonly Dictionary<GraphicSlot, TextBlock> _fileLabels = new();
    private readonly Dictionary<GraphicSlot, Image> _previewImages = new();

    /// <summary>
    /// After the dialog closes with true, this contains the slots that had files loaded.
    /// </summary>
    public List<GraphicSlot> SlotsToReplace => _slots.Where(s => s.SelectedFilePath != null).ToList();

    public ReplaceGraphicDialog()
    {
        InitializeComponent();
        _gfxService = null!;
    }

    public ReplaceGraphicDialog(IGfxService gfxService) : this()
    {
        _gfxService = gfxService;
    }

    /// <summary>
    /// Configure for an Item record.
    /// </summary>
    public void ConfigureForItem(ItemRecordWrapper item)
    {
        var isEquipment = item.Type is ItemType.Weapon or ItemType.Shield
            or ItemType.Armor or ItemType.Hat or ItemType.Boots;

        DialogTitle.Text = $"Replace Graphics — {item.Name}";

        if (item.GraphicId > 0)
        {
            // Section: Item Views (gfx023)
            AddSectionHeader("Item Views (gfx023)");

            // Drop / Ground view
            var dropResourceId = (2 * item.GraphicId - 1) + 100;
            AddSlot("Drop View", GfxType.Items, dropResourceId);

            // Inventory view
            var invResourceId = (2 * item.GraphicId) + 100;
            AddSlot("Inventory View", GfxType.Items, invResourceId);
        }

        if (isEquipment && item.Spec1 > 0)
        {
            var dollGraphic = item.Spec1;
            var isFemale = item.IsFemaleEquipment;

            var (gfxType, typeName, maxFrames, baseResourceId) = GetEquipmentInfo(item.Type, dollGraphic, isFemale);

            AddSectionHeader($"Equipment Frames — {typeName} (gfx{(int)gfxType:D3})");

            for (int frame = 0; frame < maxFrames; frame++)
            {
                var resourceId = baseResourceId + frame;
                var label = GetEquipmentFrameLabel(item.Type, frame);
                AddSlot($"{label} (frame {frame})", gfxType, resourceId);
            }

            // For weapons, also show female weapon frames if male weapon shown
            if (item.Type == ItemType.Weapon && !isFemale)
            {
                var femaleGfxType = GfxType.FemaleWeapon;
                var femaleBase = (dollGraphic * 100) + 1;
                AddSectionHeader($"Female Weapon Frames (gfx{(int)femaleGfxType:D3})");
                for (int frame = 0; frame < maxFrames; frame++)
                {
                    var resourceId = femaleBase + frame;
                    var label = GetEquipmentFrameLabel(item.Type, frame);
                    AddSlot($"{label} (frame {frame})", femaleGfxType, resourceId);
                }
            }
        }

        if (_slots.Count == 0)
        {
            DialogSubtitle.Text = "This item has no graphics assigned (GraphicId = 0).";
        }
        else
        {
            DialogSubtitle.Text = $"Select BMP files to replace individual frames. {_slots.Count} slot(s) available.";
        }
    }

    /// <summary>
    /// Configure for an NPC record.
    /// </summary>
    public void ConfigureForNpc(NpcRecordWrapper npc)
    {
        DialogTitle.Text = $"Replace Graphics — {npc.Name}";

        if (npc.GraphicId <= 0)
        {
            DialogSubtitle.Text = "This NPC has no graphics assigned (GraphicId = 0).";
            return;
        }

        var frameCategories = new (string Category, int[] Frames)[]
        {
            ("Standing Down/Right", new[] { 1, 2 }),
            ("Standing Up/Left", new[] { 3, 4 }),
            ("Walking Down/Right", new[] { 5, 6, 7, 8 }),
            ("Walking Up/Left", new[] { 9, 10, 11, 12 }),
            ("Attacking Down/Right", new[] { 13, 14 }),
            ("Attacking Up/Left", new[] { 15, 16 }),
        };

        AddSectionHeader("NPC Frames (gfx021)");

        foreach (var (category, frames) in frameCategories)
        {
            foreach (var frame in frames)
            {
                var resourceId = ((npc.GraphicId - 1) * 40) + frame + 100;
                AddSlot($"{category} — frame {frame}", GfxType.NPC, resourceId);
            }
        }

        DialogSubtitle.Text = $"Select BMP files to replace individual frames. {_slots.Count} slot(s) available.";
    }

    /// <summary>
    /// Configure for a Spell record.
    /// </summary>
    public void ConfigureForSpell(SpellRecordWrapper spell)
    {
        DialogTitle.Text = $"Replace Graphics — {spell.Name}";

        if (spell.GraphicId > 0)
        {
            AddSectionHeader("Spell Effect Layers (gfx024)");
            for (int layer = 1; layer <= 3; layer++)
            {
                var resourceId = ((spell.GraphicId - 1) * 3) + layer + 100;
                AddSlot($"Effect Layer {layer}", GfxType.Spells, resourceId);
            }
        }

        if (spell.IconId > 0)
        {
            AddSectionHeader("Spell Icon (gfx025)");
            var iconResourceId = spell.IconId + 100;
            AddSlot("Spell Icon", GfxType.SpellIcons, iconResourceId);
        }

        if (_slots.Count == 0)
        {
            DialogSubtitle.Text = "This spell has no graphics assigned.";
        }
        else
        {
            DialogSubtitle.Text = $"Select BMP files to replace individual frames. {_slots.Count} slot(s) available.";
        }
    }

    private void AddSectionHeader(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            Classes = { "section-header" }
        };
        FrameListPanel.Children.Add(header);
    }

    private void AddSlot(string label, GfxType gfxType, int resourceId)
    {
        var slot = new GraphicSlot
        {
            Label = label,
            GfxType = gfxType,
            ResourceId = resourceId
        };

        // Try to load current preview
        slot.CurrentPreview = _gfxService.LoadBitmapByResourceId(gfxType, resourceId);

        _slots.Add(slot);

        // Build UI row
        var border = new Border();
        border.Classes.Add("frame-row");

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto")
        };

        // Preview thumbnail
        var previewImage = new Image
        {
            Source = slot.CurrentPreview,
            Width = 36,
            Height = 36,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(previewImage, 0);
        grid.Children.Add(previewImage);
        _previewImages[slot] = previewImage;

        // Label
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        labelBlock.Classes.Add("frame-label");
        Grid.SetColumn(labelBlock, 1);
        grid.Children.Add(labelBlock);

        // File path display
        var filePathBlock = new TextBlock
        {
            Text = "(no file selected)",
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150
        };
        Grid.SetColumn(filePathBlock, 2);
        grid.Children.Add(filePathBlock);
        _fileLabels[slot] = filePathBlock;

        // Load File button
        var loadBtn = new Button
        {
            Content = "Load File",
            Tag = slot,
            Margin = new Thickness(4, 0, 0, 0)
        };
        loadBtn.Classes.Add("load-btn");
        loadBtn.Click += OnLoadFileClick;
        Grid.SetColumn(loadBtn, 3);
        grid.Children.Add(loadBtn);

        border.Child = grid;
        FrameListPanel.Children.Add(border);
    }

    private async void OnLoadFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GraphicSlot slot)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select Image — {slot.Label}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image Files") { Patterns = new[] { "*.bmp", "*.png" } }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            slot.SelectedFilePath = path;

            if (_fileLabels.TryGetValue(slot, out var label))
            {
                label.Text = Path.GetFileName(path);
                label.Foreground = new SolidColorBrush(Color.Parse("#2a9d8f"));
            }

            // Update preview thumbnail with the loaded BMP
            if (_previewImages.TryGetValue(slot, out var img))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    img.Source = new Bitmap(stream);
                }
                catch
                {
                    // Leave existing preview if load fails
                }
            }

            UpdateReplaceButtonState();
        }
    }

    private void UpdateReplaceButtonState()
    {
        ReplaceButton.IsEnabled = _slots.Any(s => s.SelectedFilePath != null);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnReplace(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private static (GfxType gfxType, string typeName, int maxFrames, int baseResourceId) GetEquipmentInfo(
        ItemType type, int dollGraphic, bool isFemale)
    {
        return type switch
        {
            ItemType.Weapon => (
                isFemale ? GfxType.FemaleWeapon : GfxType.MaleWeapon,
                "Weapon",
                17,
                (dollGraphic * 100) + 1),
            ItemType.Shield => (
                GfxType.MaleBack,
                "Shield",
                22,
                ((dollGraphic - 1) * 50) + 101),
            ItemType.Armor => (
                isFemale ? GfxType.FemaleArmor : GfxType.MaleArmor,
                "Armor",
                22,
                ((dollGraphic - 1) * 50) + 101),
            ItemType.Boots => (
                isFemale ? GfxType.FemaleBoots : GfxType.MaleBoots,
                "Boots",
                16,
                ((dollGraphic - 1) * 40) + 101),
            ItemType.Hat => (
                isFemale ? GfxType.FemaleHat : GfxType.MaleHat,
                "Hat",
                3,
                ((dollGraphic - 1) * 10) + 101),
            _ => (GfxType.Items, "", 0, 0)
        };
    }

    private static string GetEquipmentFrameLabel(ItemType type, int frame)
    {
        // Provide human-readable labels for known frame indices
        return type switch
        {
            ItemType.Weapon => frame switch
            {
                0 => "Standing",
                >= 1 and <= 4 => $"Walk {frame}",
                >= 5 and <= 8 => $"Attack {frame - 4}",
                >= 9 and <= 12 => $"Extra {frame - 8}",
                _ => $"Frame {frame}"
            },
            ItemType.Armor or ItemType.Shield => frame switch
            {
                0 => "Standing",
                >= 1 and <= 4 => $"Walk {frame}",
                >= 5 and <= 10 => $"Sit/Emote {frame - 4}",
                >= 11 and <= 16 => $"Attack {frame - 10}",
                _ => $"Frame {frame}"
            },
            ItemType.Boots => frame switch
            {
                0 => "Standing",
                >= 1 and <= 4 => $"Walk {frame}",
                >= 5 and <= 10 => $"Sit/Emote {frame - 4}",
                _ => $"Frame {frame}"
            },
            ItemType.Hat => frame switch
            {
                0 => "Standing",
                1 => "Frame 1",
                2 => "Frame 2",
                _ => $"Frame {frame}"
            },
            _ => $"Frame {frame}"
        };
    }
}
