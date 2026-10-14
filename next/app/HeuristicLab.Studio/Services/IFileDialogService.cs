using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace HeuristicLab.Studio.Services;

/// <summary>File dialogs behind an interface, so view models can be tested without a window.</summary>
public interface IFileDialogService {
  Task<string?> OpenHlFileAsync();
  /// <summary>Several .hl files at once (an empty list if cancelled).</summary>
  async Task<System.Collections.Generic.IReadOnlyList<string>> OpenHlFilesAsync() => await OpenHlFileAsync() is string path ? [path] : [];
  Task<string?> SaveHlFileAsync(string suggestedName);
  Task<string?> OpenDataFileAsync();
  Task<string?> PickFolderAsync(string title);
  Task<string?> SaveCsvFileAsync(string suggestedName);
  /// <summary>Where to write a Python module (exported runs).</summary>
  Task<string?> SavePythonFileAsync(string suggestedName) => Task.FromResult<string?>(null);
}

public sealed class FileDialogService(TopLevel topLevel) : IFileDialogService {
  private static readonly FilePickerFileType HlFiles = new("HeuristicLab files") { Patterns = ["*.hl"] };
  private static readonly FilePickerFileType DataFiles = new("Data files (CSV, Parquet)") { Patterns = ["*.csv", "*.parquet", "*.txt", "*.tsv"] };

  public async Task<string?> OpenDataFileAsync() {
    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = "Open data file", AllowMultiple = false, FileTypeFilter = [DataFiles]
    });
    return files.Count > 0 ? files[0].TryGetLocalPath() : null;
  }

  public async Task<string?> PickFolderAsync(string title) {
    var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
    return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
  }

  public async Task<System.Collections.Generic.IReadOnlyList<string>> OpenHlFilesAsync() {
    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = "Open HeuristicLab files", AllowMultiple = true, FileTypeFilter = [HlFiles]
    });
    return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
  }

  public async Task<string?> OpenHlFileAsync() {
    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = "Open HeuristicLab file", AllowMultiple = false, FileTypeFilter = [HlFiles]
    });
    return files.Count > 0 ? files[0].TryGetLocalPath() : null;
  }

  public async Task<string?> SaveCsvFileAsync(string suggestedName) {
    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
      Title = "Export table", SuggestedFileName = suggestedName, DefaultExtension = "csv",
      FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }]
    });
    return file?.TryGetLocalPath();
  }

  public async Task<string?> SavePythonFileAsync(string suggestedName) {
    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
      Title = "Export to Python", SuggestedFileName = suggestedName, DefaultExtension = "py",
      FileTypeChoices = [new FilePickerFileType("Python files") { Patterns = ["*.py"] }]
    });
    return file?.TryGetLocalPath();
  }

  public async Task<string?> SaveHlFileAsync(string suggestedName) {
    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
      Title = "Save HeuristicLab file", SuggestedFileName = suggestedName, DefaultExtension = "hl",
      FileTypeChoices = [HlFiles]
    });
    return file?.TryGetLocalPath();
  }
}
