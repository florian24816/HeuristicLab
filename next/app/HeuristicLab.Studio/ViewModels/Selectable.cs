namespace HeuristicLab.Studio.ViewModels;

/// <summary>A row that can be one of several selected (Ctrl+click, Shift+click, press and sweep); shown with inverted colors.</summary>
public interface IPickable {
  bool IsSelected { get; set; }
}
