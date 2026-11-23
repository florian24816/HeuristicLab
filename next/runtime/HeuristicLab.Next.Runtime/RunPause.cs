using HeuristicLab.Core;

namespace HeuristicLab.Next.Runtime;

/// <summary>
/// Pauses and resumes running optimizers (RunOptions.Pause; one instance may serve several runs at once). Paused, the
/// engines of the run's algorithms wait before their next operation (HeuristicLab.Core EngineGate): experiments, batch
/// runs and time-limit runs above them never notice, so none of their pause races can happen (HeuristicLab's own Pause
/// left them in wrong states when it arrived between two optimizers or as one finished). Resumed, every engine goes on
/// where it was; time spent paused is no execution time. Stop while paused stops the run as usual (the waiting engines
/// wake up on the cancellation). Algorithms that run without an engine (BasicAlgorithm) and the Debug Engine are not held.
/// </summary>
public sealed class RunPause {
  /// <summary>The hold the run's engines are attached to.</summary>
  internal EngineHold Hold { get; } = new();

  /// <summary>Paused (the engines wait at their next operation).</summary>
  public bool IsPaused => Hold.IsClosed;

  /// <summary>How many engine threads wait now (tests: the pause has reached them).</summary>
  public int Waiting => Hold.Waiting;

  public void Pause() => Hold.Close();

  public void Resume() => Hold.Open();
}
