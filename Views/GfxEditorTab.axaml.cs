using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SOE_PubEditor.ViewModels;

namespace SOE_PubEditor.Views;

public partial class GfxEditorTab : UserControl
{
    public GfxEditorTab()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is GfxEditorViewModel vm)
        {
            vm.SetPickBmpFileFunc(PickBmpFileAsync);
            vm.SetPickFolderFunc(PickFolderAsync);
        }
    }

    private async Task<string?> PickBmpFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select BMP Image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("BMP Images") { Patterns = new[] { "*.bmp", "*.BMP" } },
                FilePickerFileTypes.All
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder Containing BMP Files",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    // Handle click on resource slots to set SelectedSlot
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Walk up visual tree from the pressed element to find EgfResourceSlot
        if (e.Source is Avalonia.Visual visual)
        {
            var dataContext = GetDataContextFromVisual(visual);
            if (dataContext is EgfResourceSlot slot && DataContext is GfxEditorViewModel vm)
            {
                // Deselect previous
                if (vm.SelectedSlot != null)
                    vm.SelectedSlot.IsSelected = false;

                slot.IsSelected = true;
                vm.SelectedSlot = slot;
            }
        }
    }

    private static object? GetDataContextFromVisual(Avalonia.Visual? v)
    {
        while (v != null)
        {
            if (v is Avalonia.Controls.Control ctrl && ctrl.DataContext is EgfResourceSlot slot)
                return slot;
            v = v.GetVisualParent() as Avalonia.Visual;
        }
        return null;
    }
}
