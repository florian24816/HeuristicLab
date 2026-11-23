// HeuristicLab.Next (2026-11-15): pausing a run below the optimizers. HeuristicLab's own Pause travels down through
// experiments, batch runs and time-limit runs, and each of them has moments where a pause arriving at the wrong instant
// leaves it in a wrong state (an experiment Started though nothing runs, a batch run preparing an experiment that is
// still Started, a repetition started over). A held engine simply waits before its next operation: the optimizers
// above it never notice, and releasing the hold lets it go on exactly where it was. Its execution time does not grow
// while it is held (Engine.WaitWhileHeld).

using System.Runtime.CompilerServices;
using System.Threading;

namespace HeuristicLab.Core {
  /// <summary>A hold for engines: closed, the engines attached to it wait before their next operation.</summary>
  public sealed class EngineHold {
    private readonly ManualResetEventSlim open = new ManualResetEventSlim(true);
    private int waiting;

    /// <summary>Engines wait at their next operation.</summary>
    public bool IsClosed { get { return !open.IsSet; } }
    /// <summary>How many engine threads wait at the hold now.</summary>
    public int Waiting { get { return Volatile.Read(ref waiting); } }

    public void Close() { open.Reset(); }
    public void Open() { open.Set(); }

    internal void Wait(CancellationToken cancellationToken) {
      Interlocked.Increment(ref waiting);
      try {
        open.Wait(cancellationToken);
      } finally {
        Interlocked.Decrement(ref waiting);
      }
    }
  }

  /// <summary>Which engines a hold applies to (engines of the optimizers of one run).</summary>
  public static class EngineGate {
    private static readonly ConditionalWeakTable<IEngine, EngineHold> holds = new ConditionalWeakTable<IEngine, EngineHold>();
    private static readonly object gate = new object();
    private static int attached;

    public static void Attach(IEngine engine, EngineHold hold) {
      lock (gate) {
        if (holds.TryGetValue(engine, out _)) holds.Remove(engine);
        else attached++;
        holds.Add(engine, hold);
      }
    }

    public static void Detach(IEngine engine) {
      lock (gate) {
        if (holds.Remove(engine)) attached--;
      }
    }

    /// <summary>The hold of this engine, if it has one (cheap while no engine has one).</summary>
    public static EngineHold HoldOf(IEngine engine) {
      if (Volatile.Read(ref attached) == 0) return null;
      EngineHold hold;
      return holds.TryGetValue(engine, out hold) ? hold : null;
    }
  }
}
