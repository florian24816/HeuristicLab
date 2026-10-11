using System.ComponentModel;
using HEAL.Attic;
using HeuristicLab.Common;
using HeuristicLab.Core;
using HeuristicLab.Optimization;

namespace HeuristicLab.Next.Runtime;

/// <summary>
/// A time-limit run that holds any building blocks (algorithms, batch runs, other time-limit runs):
/// they run one after another, as in an experiment, and whatever still runs when the content has
/// run for MaximumExecutionTime is stopped. HeuristicLab's own TimeLimitRun takes exactly one
/// algorithm (it pauses it for snapshots and keeps its last run), so several blocks need this.
/// The content is a plain HeuristicLab Experiment; its runs are this optimizer's runs.
/// .hl files containing it open in Studio and hl, not in HeuristicLab 3.3.
/// </summary>
[Item("Time-limit run", "Runs its building blocks one after another and stops them when the time limit is reached.")]
[StorableType("6E0F3D1B-7A54-4C1E-9B2D-3F8A9C51D7E4")]
public sealed class TimeLimitExperiment : NamedItem, IOptimizer, IStorableContent, INotifyPropertyChanged {
  public string Filename { get; set; } = "";

  [Storable]
  private Experiment content;

  [Storable]
  private TimeSpan maximumExecutionTime;

  private volatile bool stopping;

  public TimeLimitExperiment() : this(TimeSpan.FromMinutes(1)) { }

  public TimeLimitExperiment(TimeSpan maximumExecutionTime) : base("Time-limit run") {
    this.maximumExecutionTime = maximumExecutionTime;
    content = new Experiment(Name);
    Register();
  }

  [StorableConstructor]
  private TimeLimitExperiment(StorableConstructorFlag _) : base(_) { content = null!; }

  [StorableHook(HookType.AfterDeserialization)]
  private void AfterDeserialization() => Register();

  private TimeLimitExperiment(TimeLimitExperiment original, Cloner cloner) : base(original, cloner) {
    content = cloner.Clone(original.content);
    maximumExecutionTime = original.maximumExecutionTime;
    Register();
  }

  public override IDeepCloneable Clone(Cloner cloner) {
    if (ExecutionState == ExecutionState.Started) throw new InvalidOperationException($"Clone not allowed in execution state \"{ExecutionState}\".");
    return new TimeLimitExperiment(this, cloner);
  }

  /// <summary>The building blocks, run one after another.</summary>
  public Experiment Content => content;

  /// <summary>How long the content may run in total; then what still runs is stopped.</summary>
  public TimeSpan MaximumExecutionTime {
    get => maximumExecutionTime;
    set {
      if (value == maximumExecutionTime) return;
      maximumExecutionTime = value;
      OnPropertyChanged(nameof(MaximumExecutionTime));
    }
  }

  /// <summary>Whether the last run ended because the time was up (not because all blocks finished or it was stopped).</summary>
  public bool StoppedByTimeLimit { get; private set; }

  public ExecutionState ExecutionState => content.ExecutionState;
  public TimeSpan ExecutionTime => content.ExecutionTime;
  public RunCollection Runs => content.Runs;
  public IEnumerable<IOptimizer> NestedOptimizers => content.NestedOptimizers.Prepend(content);

  public void Prepare() => Prepare(false);
  public void Prepare(bool clearRuns) => content.Prepare(clearRuns);
  public void Start() => Start(CancellationToken.None);

  public void Start(CancellationToken cancellationToken) {
    stopping = false;
    StoppedByTimeLimit = false;
    content.Start(cancellationToken);
  }

  public Task StartAsync() => StartAsync(CancellationToken.None);
  public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() => Start(cancellationToken), cancellationToken);
  public void Pause() => content.Pause();
  public void Stop() => content.Stop();

  protected override void OnNameChanged() {
    base.OnNameChanged();
    if (content != null) content.Name = Name;
  }

  private void Register() {
    content.ExecutionStateChanged += (_, _) => ExecutionStateChanged?.Invoke(this, EventArgs.Empty);
    content.ExecutionTimeChanged += (_, _) => {
      CheckTime();
      ExecutionTimeChanged?.Invoke(this, EventArgs.Empty);
    };
    content.Prepared += (_, _) => { stopping = false; Prepared?.Invoke(this, EventArgs.Empty); };
    content.Started += (_, _) => Started?.Invoke(this, EventArgs.Empty);
    content.Paused += (_, _) => Paused?.Invoke(this, EventArgs.Empty);
    content.Stopped += (_, _) => Stopped?.Invoke(this, EventArgs.Empty);
    content.ExceptionOccurred += (_, e) => ExceptionOccurred?.Invoke(this, e);
  }

  /// <summary>Time up: stop the content (on another thread; this is called from the content's own events).</summary>
  private void CheckTime() {
    if (stopping || content.ExecutionState != ExecutionState.Started || content.ExecutionTime < maximumExecutionTime) return;
    stopping = true;
    StoppedByTimeLimit = true;
    Task.Run(() => {
      try {
        content.Stop();
      } catch (InvalidOperationException) {
        // already stopped or paused by now
      }
    });
  }

  public event EventHandler? ExecutionStateChanged;
  public event EventHandler? ExecutionTimeChanged;
  public event EventHandler? Prepared;
  public event EventHandler? Started;
  public event EventHandler? Paused;
  public event EventHandler? Stopped;
  public event EventHandler<EventArgs<Exception>>? ExceptionOccurred;
  public event PropertyChangedEventHandler? PropertyChanged;

  private void OnPropertyChanged(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
