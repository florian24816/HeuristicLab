using HeuristicLab.Core;
using HeuristicLab.Optimization;

namespace HeuristicLab.Next.Runtime;

/// <summary>
/// Pauses and resumes running optimizers (RunOptions.Pause; one instance may serve several runs at once). Paused, an
/// optimizer keeps its state; resumed it goes on where it was. Cancelling the run while paused stops it (its run is
/// recorded as when stopped while running).
/// </summary>
public sealed class RunPause {
  private readonly object gate = new();
  private TaskCompletionSource resumed = NewResumed();

  /// <summary>Paused (asked to pause; the optimizers pause as soon as they can).</summary>
  public bool IsPaused { get; private set; }

  /// <summary>Raised on any thread when <see cref="IsPaused"/> changes.</summary>
  public event Action? Changed;

  public void Pause() {
    lock (gate) {
      if (IsPaused) return;
      IsPaused = true;
      resumed = NewResumed();
    }
    Changed?.Invoke();
  }

  public void Resume() {
    TaskCompletionSource done;
    lock (gate) {
      if (!IsPaused) return;
      IsPaused = false;
      done = resumed;
    }
    done.TrySetResult();
    Changed?.Invoke();
  }

  /// <summary>Completes when resumed (at once when not paused); cancelled with the token.</summary>
  internal Task WaitResumedAsync(CancellationToken token) {
    lock (gate) return IsPaused ? resumed.Task.WaitAsync(token) : Task.CompletedTask;
  }

  /// <summary>Pauses the optimizer if a pause is asked for and it runs now (a request before it started is caught later).</summary>
  internal void Apply(IOptimizer optimizer) {
    if (!IsPaused) return;
    try {
      if (optimizer.ExecutionState == ExecutionState.Started) optimizer.Pause();
    } catch (InvalidOperationException) {
      // the state changed meanwhile (it finished or paused); the next check catches up
    } catch (NotSupportedException) {
      // optimizers that cannot pause run on
    }
  }

  private static TaskCompletionSource NewResumed() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
