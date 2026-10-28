# HeuristicLab Fork — Project Instructions

## Context
- Fork of HeuristicLab 3.3.16 (legacy net472, dormant upstream) for building a new
  Avalonia-based quantitative research environment.
- Legacy tree = reference oracle. NEVER modify legacy project files unless the task
  explicitly says so. New work lives in next/.

## Branches and safety
- main is the integration branch (the user merges feature branches via pull requests).
  Never commit to main directly; start feature branches from origin/main.
  (deepseek-development is historical: it predates the port.)
- Never push without the user's explicit approval. The user has authorized committing
  finished ports in next/ on feature branches without asking (one commit per project).
- No destructive git commands (reset --hard, clean -fd) or rm -rf, ever.
- Before suggesting any modification, run: git status --short, git diff --stat.
- Do not run legacy scripts: prepareProjectsForMono.sh, Build.cmd/ps1, Test.cmd/ps1,
  MergeConfigs.sh, PreBuildEvent.sh (outside builds), xbuild.

## Environment
- .NET SDKs 8.0.425 + 10.0.401 in ~/.dotnet (PATH/DOTNET_ROOT in ~/.bashrc)
- Mono 6.14.1 present; xbuild deprecated, do not use
- Ported libraries target netstandard2.0; apps and tests target net10.0 (LTS).
  Legacy probe recipe: see docs/linux-build-probe.md

## Current state
- next/app/HeuristicLab.Studio (Avalonia 12 MVVM, CommunityToolkit) on the runtime:
  Experiments tab (first): several experiments in one workspace, each a tree of building blocks
  (algorithm + its problem, batch run, time-limit run) over plain HeuristicLab objects via
  runtime ExperimentTree (a batch run with several children wraps them in an inner Experiment
  named "Batch content"; a time-limit run is HeuristicLab's TimeLimitRun and takes one algorithm -
  kept that way on purpose so .hl files stay compatible with HeuristicLab 3.3 (user's decision
  2026-10-11; a multi-block time-limit container was built and reverted).
  While anything runs, blocks stay clickable (details shown) but nothing changes: menus offer no
  edits, names/limits/repetitions are ignored, add buttons disabled, no saving, dragging, copying
  or editing across algorithms; value and choice parameters show parameter_currently_not_editable. Block design (user's request,
  no rounded corners, no green/yellow): orange (#F7931D) square with the kind's pictogram, a dark
  square with the state (experiment: saved/unsaved; algorithm: status), the name; batch/time-limit
  runs end in an orange field (repetitions / time limit; while running "3 / 10" or "01:23 / 05:00").
  Pictograms are the user's SVGs in Assets/Pictograms (Controls.Pictogram reads the paths; the
  working status turns as its animateTransform says). Algorithm status from the algorithm's
  events: no problem, not run / stopped by the user, waiting (in a running experiment, paused),
  working, finished (also stopped by a time limit), failed. Unsaved = changed since opened/saved
  (BlockViewModel.MarkModified; detail edits via EditContext.BeginEdit; runs count as changes).
  Start all / Start selected / Stop are drawn like blocks (Button.picto: orange square with
  Experiment_run_all / Experiment_run_selected / experiment_stop, then a grey rectangle).
  Eject (next to Stop: grey #A8A8A8 square with the eject pictogram; not while anything runs): EjectWindow -
  tick experiments ("Eject selected") or "Eject all", Autosave switch; ExperimentWorkspaceViewModel.EjectAsync:
  unsaved ones (!IsSaved) are saved first with autosave (never saved: "Save as..." file), else UnsavedWindow
  asks per experiment (Save / Eject without saving / Keep it open); one not saved stays. Files stay on disk.
  The Experiments tab's status (Workspace.Status) sits in MainWindow's tab row between the tabs and the
  logo (TextBlock.runstatus); the bottom status bar still shows it too. Checkbox labels with names use a
  TextBlock: a string's '_' would be an access key.
  Runs tab: "Delete runs..." next to Refresh (ChooseWindow: pick an entry, the delete button is disabled
  until then): per experiment "the experiment's runs" (ExperimentTree.DeleteRuns clears every optimizer's
  runs without Prepare; its single runs stay) or "only its single runs" (DeleteSingleRuns), and "All single
  runs of all experiments" when several have some; never while running; marks it unsaved. Several entries
  at once (IDialogService.ChooseAsync returns the picked indices).
  Selecting several (user's request): Controls/SweepSelection - click = one, Ctrl+click toggles, Shift+click
  a range from the last clicked, press and sweep = the rows passed over; rows are visible "selectrow" Borders
  whose DataContext is ViewModels.IPickable (tick boxes / expanders keep their own clicks). Selected rows
  inverted by theme (App.axaml InvertBrush/InvertInkBrush: light mode #1E1E1E/white, dark #F2F2F2/#1E1E1E).
  Used by ChooseWindow (ChoiceRow) and the Results tab's source tree (RunSourceViewModel.IsSelected; the
  TreeView's own selection highlight is turned off; buttons "Tick selected" / "Untick selected" / "Only
  selected" - tick them and untick all others). Experiments tab: Ctrl+click picks blocks (BlockViewModel.
  IsPicked, name field inverted to #1E1E1E/white in both modes - blocks keep fixed light fields), pressing on
  empty space draws a rectangle (BandLayer) that picks the blocks it covers (Ctrl: adds); a plain click on a
  block or on empty space clears them; dragging a picked block moves all picked ones (PickedGroup: tree
  order, a block inside a picked container goes with it) one after the other to the target (MoveGroup;
  not into a time-limit run).
  Each experiment's area is filled (Border.experiment, no border; App.axaml ThemeDictionaries): light mode
  warm stone #D5CFC6 with dark writing, dark mode #36322E with light writing (ExperimentBrush/InkBrush/
  HoverBrush; missing-text in WarningBrush). Gunmetal #3E474D was replaced (fit neither mode). "Open..." takes several .hl files at once
  (IFileDialogService.OpenHlFilesAsync, AllowMultiple): each opens as its own experiment.
  Clicking the unsaved disk saves (SaveChangesAsync): to its file, or "Save as..." without one.
  "Rename file..." (experiment detail next to "Save as...", experiment menu; only with a file): renames
  the .hl file in its folder (TextWindow / IDialogService.AskTextAsync; never over another file; the
  experiment keeps its name; recent files follow). Icon: HeuristicLab/3.3/HeuristicLab.ico, linked
  into the app (window icon via a :is(Window) style, ApplicationIcon, desktop entry; also drawn at the
  top right of the tab row in MainWindow).
  Click: details/parameters, double-click: rename, right-click: menu.
  The selected block is covered by a see-through orange layer (Border.selection, #F7931D at 0.38, not
  hit-testable, under the purple multi-edit layer), besides the name's dark frame.
  Drag and drop (own pointer handling in ExperimentWorkspaceView, logic in the workspace VM:
  BeginDrag/TargetAt/PreviewDrop/EndDrag/Move): algorithms (also dragged by their problem), batch
  runs and time-limit runs (with their content) move anywhere, also into other experiments; upper
  half of a row = before it, lower half = after it or first inside a batch/time-limit run that
  takes it; add button / experiment border = at the end. A faded copy (opacity 0.4) shows where it
  lands, and the original is hidden meanwhile (BlockViewModel.IsMoving; shown again when there is
  no valid target); Escape cancels. Not into itself; a time-limit run holds one algorithm. Moves go through
  ExperimentTree.Remove/Insert (HeuristicLab prepares an added optimizer; runs go with it).
  Copy / Copy n times... (menu): ExperimentTree.Copy = deep clone without runs, inserted after the
  original as "Name (copy k)"; not for an algorithm inside a time-limit run (copy the run).
  Edit across algorithms (ViewModels/MultiEdit.cs): Shift+click on a value (text/checkbox) or a
  choice (ComboBox) in the Algorithm or Operator Graph tab (tunnel handler in AlgorithmDetailView,
  fields carry class "field") -> every algorithm block of the same type in the same experiment that
  has that field gets a purple overlay (shown one stronger; problem blocks untouched) and the field
  is purple (App.axaml styles, class "multi"). Clicking a purple block shows the same field there
  (AlgorithmDetailViewModel.Reveal: tab, parameter, analyzer entry, graph node) and focuses it
  (text selected; a choice's list opens: arrows + Enter or click). Shift+click on a purple block or
  the purple field ends it. Shift+Enter in the purple field takes the value and names that algorithm
  after it (ViewModels/AutoName.cs; bubble KeyDown with handledEventsToo in AlgorithmDetailView):
  default/copy/empty name -> "<algorithm tag>_<tag>" (LS_maxIt300), own name kept in front
  (Randomname_LS_maxIt300), a name in this form gets the tag added or that parameter's tag replaced
  (LS_maxIt300_MG-StochInvMultiMG). Tags: value = words cut to their first syllable, lower camel +
  value (never starting lower case); choice = initials (one word: first syllable) + "-" + operator
  type name shortened, its ending repeating the parameter written as the parameter's tag; algorithm
  tag = the "(..)" of its name, else initials. Fields are addressed by keys like a/p:Analyzer/e:Name/p:X#value or
  g/o:2:MainLoop/n:5:Name/p:Y#choice (EditContext.MultiTarget/ToggleMulti; editors rebuilt when
  MultiVersion changes).
  Parameter lists show the parameter kind's pictogram before the name (constrained = choice,
  optional = may be empty, else value).
  Move operators that cannot work (runtime MoveChecks; LS/TS/SA and their improvement operators):
  HeuristicLab offers every move generator of the encoding, also those for which the problem has no
  move evaluator/maker/tabu operator (TSP: swap2, scramble -> MoveEvaluator "none"; the run "completes"
  with 0 evaluated moves and no results, silently), and the generic SingleObjectiveMoveGenerator of
  encoding-based problems needs an overridden GetNeighbors (none of the built-in ones has it: BestSelector
  crashes). Such choices are yellow (#F4E803, the unsaved pictogram's, dark writing, both modes) in the
  open list (ComboBoxItem.warned via ContainerPrepared), the chosen one in the ComboBox, its row's summary
  in the list (Border.caution) and a text below; a choice with a warning reports it in the detail's message.
  Choice lists are read again after every edit in their list (ParameterNodeViewModel.RefreshSummary):
  they used to stay as built, so after a new move generator the old evaluators were still offered and
  choosing one failed silently (ConstrainedValueParameter "Invalid value."). An empty list is not shown.
  Runtime ParameterChecks (Warning / ParameterWarning / AlgorithmWarning / ProblemWarning) extends this to
  LS, TS, SA, GA, Island GA, OSGA, Island OSGA, SASEGASA and NSGA-II; every rule comes from running every
  choice and edge value on HeuristicLab's problems (probe programs, not in the repo): DiversitySelector
  without expression trees, the integer SelfAdaptive manipulator outside an ES, PMX/MPX on permutations
  shorter than 4, context-aware / deterministic-best crossovers on time series prognosis, pair selectors
  (GenderSpecificSelector, NoSameMates) picking an odd number (Island GA: ceil(15 % of 100) = 15 emigrants),
  the exponential comparison factor modifier with HeuristicLab's default lower bound 0, AlbaTranslocationMoveGenerator
  (HeuristicLab bug), CopyCrossover without mutation, GeneralizedRankSelector on multi-objective problems (NSGA-II), NSGA-II on the multi-objective shape-constrained regression (crashes whatever the settings); values: population < 2, elites < 0 or >= population,
  0 generations, shares outside 0..1, OSGA MaximumSelectionPressure <= 1, odd/0 SelectedParents, < 1 islands,
  temperatures <= 0, InnerIterations/SampleSize 0, negative tabu tenure, a move Sigma of 0, and
  FillPopulationWithParents with a population under 100 (OffspringSelector index bug, seen at 20-50).
  A required choice with nothing offered = HeuristicLab never prepares the algorithm and Start fails
  "not allowed in execution state Stopped" (SA/TS on job shop, linear assignment, regression, ...):
  AlgorithmDetailViewModel.AlgorithmWarning (yellow box above the parameters; Start shows it instead).
  TS cannot even attach some problems (its default move generator is not among its valid ones: "Invalid
  value."): the problem picker (NewSetupViewModel, checked off the UI thread, WarningsReady) shows such
  problems yellow with the reason (CatalogItem.Warning). Warnings of nested parameters look at the algorithm
  (EditContext.Root); every edit refreshes all lists shown (EditContext.Edited). Yellow value fields:
  Border.warnedvalue around the editor. Checked operator lists (analyzers) are numbered: a
  MultiAnalyzer runs its ticked operators in list order. Nested editors below a list get a
  "<name> selected. Details/Options:" heading. Result lists (algorithm Results tab, Results tab's
  Run view) show the result's kind first: chart (result_diagram_logo: first visualization is a
  ChartVisual), picture (result_visualisation_logo: any other visualization), value (the optional
  value parameter's pictogram); decided once per value type. Start all / start selected (ticked) experiments, enabled only
  when complete; no seed is imposed (a fixed seed would make batch repetitions identical).
  Legacy algorithms crash on Problem = null (OnProblemChanged): clone problems instead of
  detaching them; "Remove problem" tolerates exactly that crash.
  Algorithm detail (click an algorithm or its problem): tabs Problem (library/instance, typed
  parameters, coordinates + best known tour), Algorithm, Results (live, tables as charts), Runs,
  Operator Graph (layered from the initial operator, nested graphs, breakpoints; blocks = orange
  square with operator_single/operator_multiple (own graph inside), corners rounded like the block (10) + rounded block with a gradient
  #EDEDED top right -> #F7931D bottom left; layers centered, ordered by predecessors; curved arrows
  with arrowheads, loops dashed along the right side, labels only when not "Successor", a "Start"
  arrow; node list order = ItemInspector.Layout order, which MultiEdit keys rely on; "Fullscreen" / F11:
  OperatorGraphFullscreenWindow - the graph (OperatorGraphView(showsEditor: false)) fills the screen, the
  selected operator's parameters in a minimizable FloatingPanel), Engine (choice,
  log); Start/Pause/Stop/Prepare. Parameter editing via runtime ItemInspector/CheckedList:
  bool, text, choices with nested parameters, arrays/matrices, checked lists (analyzers), Show in
  Run (GetsCollected). Views are found by ViewLocator (XyzViewModel -> XyzView), so nested
  editors recurse.
  Live monitoring: while anything runs (experiments or a single algorithm), a workspace timer
  (500 ms, ExperimentWorkspaceViewModel.RefreshLive) refreshes the shown detail: results in place
  (selection stays), the selected result's quality/visualization/value (tours via
  ItemInspector.TourOf, tables as charts, structured values via ItemInspector.Members), runs
  tables and tree counters. Reads tolerate the running algorithm changing the data.
  Visualizations (results and the Problem tab) come from runtime Visuals.Visualizations, a toolkit-
  free picture model mirroring HeuristicLab's default views: ChartVisual (DataTable chart types incl.
  histograms with HeuristicLab's binning, second y axis, scatter plots), SceneVisual (2D: tours, VRP
  routes, orienteering, QAP via multidimensional scaling, LAP, knapsack, 2D packing, Gantt charts,
  test function landscapes, expression trees, ant trail replayed with the ant interpreter, lawn),
  BoxesVisual (3D packing) and TextVisual (formulas, robot code). Studio renders them with LineChart,
  SceneView (zoom/pan) and BoxesView (software 3D, drag to rotate) in VisualsView; a picker chooses
  among several pictures and keeps the choice while results update. Orienteering has no crossover or
  mutation: run it with Variable Neighborhood Search.
  Samples (HeuristicLab's start page): runtime Samples embeds the 37 .hl files of
  HeuristicLab.Optimizer/3.3/Documents (linked) in the start page's groups; Studio lists them on the
  Experiments tab when nothing is selected ("Samples..."): algorithms open as experiments, scripts in
  the Scripts tab (code, run on a thread, output, compile errors, variables drawn like results; no
  kill - .NET has no thread abort). All run except SGP_Robocode (needs Robocode) and
  GUI_Automation_Script (WinForms MainForm, does not compile). Older .hl files lost the tree
  length analyzer's lookup on load (upstream #3139): fixed by a replacement
  SymbolicExpressionTreeLengthAnalyzer.cs in next/core (keeps the parameter's ActualName).
  Detailed analysis (algorithm detail tab): runtime Tracing.DetailedRun runs a copy of an operator-
  based algorithm on a RecordingEngine (sequential engine + a look at each operator's scope and its
  ancestors afterwards): every new or changed solution is a step (operator, parents, quality,
  iteration, time). Copies made by selection are recognized by content fingerprint (not new);
  crossover parents are the scope's sub-scopes. Iterations end at the algorithm's Analyzer (as
  HeuristicLab's quality charts index them), else at the Generations/Iterations counter; an iteration
  may create nothing (local search at its local optimum keeps iterating) and still counts - the view
  then shows the solution kept from before. Raw
  solutions are drawn with their problem via Visualizations.ForSolution (decoders run in a scope
  holding the problem's parameters) and combined by Visuals.Composition: current solution blue (Rgb.Blue, the
  TSP tours' blue; user's choice 2026-10-15), what it has beyond its ancestors from before its iteration
  yellow, earlier iterations red shades, later ones (finished runs) green shades, a selected range's start
  (dark red, wide) / end (dark green, medium) as strokes under it; no transparency. Only shapes marked IsSolution are recolored (builders mark them;
  trees and the lawn are not layered). A selected range plays as an animation (iterations/s);
  "Earlier/Later iterations" inputs always apply (within a range too). A detailed analysis run lives only
  in memory: a note says it is not saved (AnalysisViewModel.TemporaryNote) and "Delete" (enabled once it
  finished, not while recording) throws it away. Every chart, picture and 3D
  view has a clickable legend: series / shape groups (Shape.Group, else by kind) / items can be
  hidden; the hidden set can be shared via HiddenSet (VisualsViewModel.Hidden, analysis series).
  Fullscreen (button or F11; Esc or F11 leaves): AnalysisFullscreenWindow, picture fills the
  screen, AnalysisControlsView (navigation + chart) in a movable, resizable floating panel. Not for
  BasicAlgorithms or data analysis (GPR): no candidate solutions in scopes. HeuristicLab's GA
  default crossover for real vectors is CopyCrossover: only mutations create new solutions there.
  Results tab (RunAnalysisView, runtime Runs.*): ticked sources (any experiment / batch run /
  time-limit run / algorithm of the workspace, results folders) -> RunTable (source, origin
  relative to the source, run, param:*, result:*; numbers or text). Ticks cascade (RunSourceViewModel:
  Own per block, IsChecked = true/false/null over the blocks without children): ticking a box ticks
  everything below, unticking a part leaves its runs out and the boxes above show partly ticked; a
  run is shown if the block it was made in is ticked (deepest optimizer holding it; a time-limit run's
  runs belong to its algorithm) and listed under the highest fully ticked box above it (source),
  filters (column op value), grouping by any combination of columns (numbers in N ranges),
  Table (varying parameters or all, sort, CSV export - kept as is on purpose: one-to-one comparison with
  HeuristicLab's; "Export to Python..." next to it: runtime Runs.PythonExport writes three files named after the
  experiment (ModuleName: importable): <name>_structure.py (blocks as attributes of the blocks above them,
  exp.batch_run_1.genetic_algorithm_ga - generated classes so editors complete them, checked with pyright;
  Run.params / results dataclasses typed int/float/bool/str by the values, NAMES map HeuristicLab names,
  curves = DataTable rows; Block.values/curves/df/curve_df/walk/made_here/label/find/tree, compare(),
  compare_curves(), PARAMETERS/RESULTS; no values), <name>_data.json (all values, loaded on import) and
  <name>_analysis.py ("# %%" cells: the syntax on one run, pandas table, seaborn box plot of the quality
  per block that made runs, curve per step; only fields every exported run has, BestQuality / Qualities
  preferred, min/max by Maximization; written only if missing - it is the user's), Charts (box plot, scatter, histogram,
  cumulative distribution, curves of a DataTable row per run + group mean; NaN = line gap; the box plot's
  numbers per group - RunCharts.BoxStatistics, shared with the chart - in a table below it (BoxTable);
  "Fullscreen" / F11: ChartsFullscreenWindow, chart fills the screen, legend (ChartLegendView over
  VisualsViewModel.LegendItems/Toggle; charts' own legend off via ShowLegend, redraw via HiddenVersion)
  and the box table (minimizable to its title bar) in FloatingPanels - Views/FloatingPanel moves by its
  title bar, resizes at its corner, also used by the detailed analysis fullscreen; the Fullscreen button sits
  at the right end of the chart choices),
  Filters (column, operator, value; FilterViewModel keeps ColumnKey; operators by the column's kind -
  RunFilter.OperatorsFor: numbers = ≠ < ≤ > ≥, switches (RunColumn.IsYesNo: only True/False) = ≠, text
  = ≠ contains / does not contain - no "<" on names; an operator that does not fit becomes "="): Rebuild is guarded against reentrancy
  and replaces AllColumns/NumericColumns/XChoices/CurveTables only when they change - a new list under a
  filter's ComboBox reset its selection, which rebuilt and replaced the list again: endless recursion, a
  stack overflow that closed Studio without a message (test StudioFilterTests types filters key by key).
  Tick shortcuts (Button.ticks: pale logo orange #FDE5C8, full #F7931D when it is the state now - AllTicked /
  AllButSingleTicked / NoneTicked): "Select all", "Select all (without single runs)", "Unselect all" between
  Refresh and the multi-select buttons. Results windows (user's request): "Add results window" in the Results
  tab's header while it is shown (MainViewModel.IsResultsTab) opens another Results tab in its own window
  (Views/ResultsWindow, not owned; ResultsWindowViewModel, MainViewModel.ResultsWindows, refreshed with the main
  tab when runs finish): instead of the main tabs a "Window name" field bound live to the title ("HL-Results: n",
  n counting up), ticked as the main tab at first (RunAnalysisViewModel.Main / CopySelectionFromMain, by source
  key), then on its own; "Copy selection from main results tab" next to its Refresh (IsWindow).
  Statistics (HeuristicLab.Analysis.Statistics: Kruskal-Wallis, Mann-Whitney U with
  Bonferroni-Holm, t-test, Cohen's d, Hedges' g; SummaryGrid / PairsGrid in the table design, "*" = adjusted p < 0.05), Run (one run's results with visualizations).
  Resources tab (runtime Resources/ResourceMonitor, remembered in settings.json): CPU cores =
  process affinity (hard limit, Linux/Windows), threads per algorithm (1 = HeuristicLab's default
  SequentialEngine; more = ParallelEngine with that DegreeOfParallelism, set by Resources.Configure
  before a start, only on prepared algorithms, Debug Engine kept), experiments at the same time
  (semaphore in StartAsync), memory limit (monitor stops running work), priority. Nothing in
  HeuristicLab uses the GPU. Runs get "Resources: ..." parameters (Resources.Tag) to compare in the
  Results tab. Same seed, same result on any threads: the parallel engine only parallelizes
  evaluation (tested).
  HeuristicLab facts behind it: only the Debug Engine honors breakpoints; Prepare() gives an
  algorithm a new Results collection (never cache it); an algorithm's valid operators (move
  generators, ...) come from its problem, so rebuild parameter views after a problem change.
  No toolbar (removed 2026-10-11, user's request): everything starts in the Experiments tab -
  "+ New experiment / open..." offers New experiment, New from data or benchmark... (the New dialog:
  algorithm -> compatible problem -> benchmark instance or CSV/Parquet data via Setups.Create like
  "hl new", opened as a new experiment), Open..., Open recent (workspace RecentFiles) and Samples...
  Seeds are the algorithms' own Seed/SetSeedRandomly parameters, parallelism is the Resources tab.
  No results folder: runs live in their optimizers and are saved in the .hl file (the Results tab's
  "+ Results folder..." button was removed 2026-10-15; RunAnalysisViewModel.AddFolder still reads "hl run --store" folders). Runs tab (RunsBrowserViewModel): tree of the
  workspace's experiments and blocks, the selected block's runs (RunsViewModel table), the selected
  run's parameters and results with pictures; "Show runs" (block menu / experiment detail) jumps
  there. No Solution tab (removed 2026-10-11; runtime Solutions stays for the CLI). Sweeps and
  walk-forward are CLI-only now (hl run --sweep, hl walkforward).
  Run tables (Runs tab / algorithm Runs, Results tab Table) are TableGridViewModel/TableGridView:
  columns alternate grey #D6D6D6 / light grey #EBEBEB, every other row at strength 0.55 (blended
  over white, opaque); fixed columns stay in view when scrolling sideways (Controls.Sticky moves
  them by the ScrollViewer offset, ZIndex above; the header sticks the same way vertically):
  Results: Source #FDE5C8, Origin #FBCD96, Run #F9B462 (palest to closest to the logo orange);
  RunsView: Runs tab (ShowsDetails false) the table at the top (max 300 px); Experiments tab (experiment,
  batch/time-limit run, algorithm Runs tab) the table fills the height down to the window's edge, the selected
  run's details below it (max 240 px; RunsView.axaml.cs sets the rows).
  Single runs (user's decision 2026-10-15): Start on one algorithm (its detail) makes single runs - when it
  ends (not paused) the runs it made leave the algorithm (HeuristicLab removes them from its batch run,
  time-limit run and experiment too) and go to ExperimentBlockViewModel.SingleRuns (ViewModels/SingleRuns.cs),
  named "Single run k" per algorithm (grouped by the runs' "Algorithm Name"). The algorithm's Runs tab lists
  its runs and its single runs (RunsViewModel takes a run list). Runs and Results tabs: a "Single runs"
  folder > experiment > algorithm after the experiments (RunsSourceViewModel/RunSourceViewModel Key/Group +
  run list); in the Results tab not ticked at first. Saved with the experiment as a run collection
  "<name>.single-runs.hl" next to its .hl (HeuristicLab opens it; written also when emptied), loaded when the
  experiment is opened, renamed with "Rename file...". RunValueCache reads a run again when its numbers of
  parameters/results changed (Studio tags runs with "Resources: ..." after they were first read).
  Runs: Run #F9B462; the header row one shade darker (TableGridViewModel.Darker, x0.9). Click a row to select
  it (blue overlay); Results header click sorts.
  Long tables (user's request): the first 50 rows (TableGridViewModel.Limit, ShownRows), then a button as
  wide as the table ("Show all N rows" / "Show only the first 50"); rows are drawn only where in view
  (VirtualizingStackPanel) and their cells built when first drawn (GridRow.Cells lazy).
  Reading a run (OptimizerRunner.ToRecord: all parameters and results, incl. tours and tables) is the slow
  part (~0.6 ms per GA run, measured: 1.2 s for 2000): runtime RunValueCache.Shared keeps each run's cells
  (ConditionalWeakTable, thread-safe), used by the Results tab and the runs tables. Results tab: a rebuild
  with more than 100 unread runs or 3000 rows is built in the background (RunTable.FromRuns with progress
  and cancellation; filter, grouping) while a bright layer (Border.busy, #C8FFFFFF) with an orange progress bar
  ("Processing runs: x of y") covers the views; the filter bar stays usable, each change cancels the running
  build and starts again; Settled completes when the shown table is current (tests).
  Pickers keep one searchable list (not HeuristicLab's collapsed tree) but show its categories
  (CatalogEntry.Category/CategoryOrder/Priority from [Creatable], Catalog.ByCategory): CatalogItem rows
  tinted per category (CategoryColors: light tint + dark heading color, no orange), the first row of
  a category carries its name (CatalogItemTemplate / ListBox.catalog in App.axaml). Algorithm
  picker (CatalogPickerWindow); problem dialog (NewSetupWindow): problem list (searchable, grouped)
  and benchmark instances (searchable, "(none)" always first) side by side.
  Light/dark mode: square button at the top left of the tab row (tabs moved right of it), showing the
  mode it switches to - darkmode pictogram black on grey #BDBDBD, or lightmode on the logo orange;
  MainViewModel.IsDarkMode sets Application.RequestedThemeVariant (light by default, saved in settings).
  Theme-dependent colors live in App.axaml ThemeDictionaries (WindowBrush: dark #1E1E1E instead of
  Fluent's black; WarningBrush, ErrorBrush); blocks, tables and charts keep their own fixed colors.
  Hover texts (ToolTip style in App.axaml) are dark on #F2F2F2 in both modes (dark mode's were unreadable).
  Problem tab arrays/matrices: flat square cells (TextBox.cell, no border; editing underlined orange),
  shaded like the run tables in darker greys (CellViewModel.Grey #B5B5B5 / LightGrey #CACACA, every
  other row at TableGridViewModel.WeakRow), row numbers kept.
  Matrices wider than 40 columns (e.g. a distance matrix) use the same cells read-only (TextBlock.cell,
  width fitted to the longest value, ValueEditorViewModel.WideRows); above 300 columns text rows.
  Notifications: square button right of the light/dark one (notification pictogram; off: SwitchOffBrush
  light grey #E0E0E0 / dark mode #6B6B6B, on: logo orange; MainViewModel.IsNotifying, saved). When on,
  everything started that finishes (start all/selected experiments, an algorithm's Start, a detailed
  analysis run; completed, stopped or failed, not a pause) raises ExperimentWorkspaceViewModel.Finished
  -> Services.DesktopNotifications: Linux notify-send (else gdbus to org.freedesktop.Notifications),
  Windows a toast via Windows PowerShell's WinRT (PowerShell's AppId, text in env variables), macOS
  osascript. A failure shows in the status bar. Tests use a fake IDesktopNotifications.
  Settings (~/.config/HeuristicLab.Studio/settings.json): recent files, resource limits, dark mode, notifications.
  Own LineChart control. Chart text (LineChart, SceneView legend/scales/titles): 13 px in the full text color
  (SystemControlForegroundBaseHigh), axis titles semi-bold, y titles vertical beside their scales, margins
  from the labels' sizes; SceneVisual.XCategories names a category axis under the drawing (wrapped to
  its slot; the box plot's groups), XRange/YRange widen the drawn area; notes one per line.
  Install for the user: next/tools/install.sh [--prefix DIR] [--uninstall] (Release publish,
  launchers hl and hl-studio in ~/.local/bin, desktop entry; needs the .NET 10 runtime).
  DataFiles.Read/Write block on Parquet.Net's async API: keep that work on the thread pool
  (Task.Run), else it deadlocks on the UI thread.
  Tests (next/tests/HeuristicLab.Studio.Tests) run headless (Avalonia.Headless + Skia) and
  save screenshots (studio-*.png in the test bin folder) for visual review.
  Note: HeadlessUnitTestSession.Dispose() hangs (12.1.3) - never dispose it in tests. In one test class
  either every test has [Timeout] or none: a test without one before a test with one hangs the session.
- next/runtime/HeuristicLab.Next.Runtime (net10.0): the only way new front ends use the core.
  HlRuntime.Initialize, Catalog, Documents (.hl), Setups (shared "new" logic), ParameterEditor
  (set/describe), ProblemInstances, DataFiles/TabularData (CSV via HeuristicLab's parser with
  header-based separator check; Parquet via Parquet.Net 5.6.1 - 6.x dropped the column API),
  DataProblems (table -> regression/classification/time series/trading; kind decided by exactly
  consumed data types, never by contravariant pattern matching), Features (return, logreturn,
  diff, lag, lead, rolling mean/std/min/max, zscore; composable), OptimizerRunner, BatchRunner,
  ResultStore (label:/param:/result: columns), ItemValues, Provenance, Solutions (models,
  metrics, predictions, trading equity, tours), WalkForward (rolling/expanding windows; also sets
  a symbolic problem's FitnessCalculationPartition), Sweeps (full factorial, common seeds, runs
  labelled "config"). Run labels (RunOptions.Labels) end up in reports and store rows.
  Front ends must not use WinForms-era APIs (ItemImage).
- next/app/HeuristicLab.Cli ("hl"), research workflow on the runtime:
  hl list algorithms|problems; hl instances <problem>;
  hl new <algorithm> [--problem P] [--instance I | --data f --target y] [--set N=V]... --out f.hl;
  hl data info f; hl data derive f --add EXPR... [--dropna] --out f2 (CSV or Parquet);
  hl new ... --data f --target y [--inputs a,lag(x,1..3)] [--training 66 --training-start 3];
  hl walkforward f.hl --train N --test M [--step --expanding --start --seed --parallel --store --out];
  hl show f.hl [--solution NAME] [--predictions out.csv] [--json];
  hl run f.hl --sweep "Name=v1,v2" [--sweep ...] --repeat N (full factorial, same seeds per config);
  hl info f.hl; hl run f.hl [--seed S] [--set N=V]... [--repeat N --parallel K] [--timeout] [--out report.json]
  [--store DIR] [--save f.hl]; hl store list|summary [--metric M --by param:X]|export [--csv f] DIR.
  Exit 0 completed, 1 failed, 2 usage, 3 stopped (timeout/Ctrl+C; reports still written).
  Batches: explicit seeds S..S+N-1 (base seed drawn and recorded if not given), deep clones per
  run; parallel and sequential give identical results. Results store = folder of JSON reports.
- next/tools/HeuristicLab.Examples: builds the course's example experiments (HEU5 exercises 3-6)
  as .hl files (dotnet run --project next/tools/HeuristicLab.Examples -- <folder>; "probe" lists an
  algorithm's choices, "show f.hl" what a file holds). Each configuration gets a tiny trial run first.
  Batch runs hold the algorithm directly (as HeuristicLab builds them; Studio's ExperimentTree wraps
  in "Batch content"). HeuristicLab's TSP has no swap2 move evaluator: a swap2 tabu search "completes"
  evaluating nothing; the tabu search uses inversion (2-opt) moves instead.
- Not ported yet: Problems.ExternalEvaluation* (protobuf 2.4 / Matlab COM / Scilab),
  ExactOptimization (OR-Tools, Windows-only native runtime), all Views/GUI, Hive/OKB/Services.
- next/tests/HeuristicLab.Tests: legacy tests linked unchanged incl. samples, run on
  net10.0 (MSTest 3.x). See "Running tests".

## Known runtime facts (verified on Linux / net10.0)
- HEAL.Attic is vendored (next/extlibs/HEAL.Attic, upstream v1.8 + patches in VENDORED.md):
  thread-safe caches/type registry. Same assembly identity as the NuGet package, same format.
- HEAL.Attic does not know .NET 5+'s StringEqualityComparer (default comparer of
  Dictionary<string,T>/HashSet<string>); next/core/HeuristicLab.Common/AtticRuntimeTypes.cs
  registers it in a module initializer.
  It is written as GenericEqualityComparer<string> (vendored Attic patch 3, PortableComparers.cs),
  so files saved here open in HeuristicLab 3.3 on .NET Framework (test
  SavedFilesContainNoModernDotNetOnlyTypes); files written before still load. StringComparer.Ordinal(IgnoreCase) dictionaries
  still cannot be persisted on .NET Core (Attic re-creates comparers via Activator).
- Item.ItemImage and VSImageLibrary throw PlatformNotSupportedException off Windows
  (System.Drawing.Common). Front-end code must never touch ItemImage until decoupled.
  The CA1416 analyzer does NOT flag this path (Bitmaps come from ResourceManager).
  Decision (2026-10-07): decoupling deferred until a view needs per-type icons.
- A failed Debug.Assert/Contract.Assert kills the process on modern .NET (legacy suite ran
  Release). next/tests turns them into exceptions (AssertionsAsExceptions.cs).
- CSharpCodeProvider cannot compile on .NET Core. Scripting and Operators.Programmable use
  replacement files that compile with Roslyn (next/core/Shared/RoslynCompiler.cs).
- Library code calls ErrorHandling.ShowErrorDialog; the headless version forwards to the
  settable ErrorHandling.ErrorDisplay hook (front ends set it), else writes to Trace.
- Plugin type discovery (LightweightApplicationManager replacement) scans assemblies in
  name order: AppDomain order differs between runtimes, and defaults are picked from the
  first discovered type.
- Trading (symbolic): the target (price change) must be among the inputs, but the grammar only
  allows lagged variables with lag <= -1, so models cannot read the present (tested). Signals
  are path-dependent: HeuristicLab evaluates training and test each from a fresh start.
- Autoregressive Modeling reads y[row - offset] without checking: set a training start
  (--training-start / DataProblems trainingStart) of at least its maximum time offset.
- Seeded runs are reproducible per runtime and platform, not across them: .NET Framework,
  .NET on Windows and .NET on Linux (glibc libm) can differ in the last bit of math results,
  and searches with near-ties amplify that into different but equally valid trajectories.
- Compile order matters: ports compile the legacy csproj's Compile items in their order
  (Directory.Build.targets). Type order drives plugin discovery and thus default operators;
  an alphabetical glob made PSO and GA grouping pick different defaults than legacy.

## Running tests
- next/tools/run-tests.sh [quick|daily|all]: quick skips the long Run.Daily category; quick and
  all also run next/tests/HeuristicLab.Next.Runtime.Tests (runtime + CLI).
- On Linux it excludes next/tests/known-failures-linux.txt (each entry with its reason:
  Windows-only GDI+/native DLL, Framework-specific expectations, or seeded Run.Daily
  results recorded on .NET Framework). Any other failure is a regression. Keep the list
  short and justified.
- Status 2026-10-09 on Linux/net10.0: quick 427/427, daily 75/75 (442 + 82 tests; 22
  excluded). Port fidelity verified on Windows (docs/windows-baseline.md): the ported
  libraries on .NET Framework 4.7.2 reproduce the recorded seeded sample results exactly;
  the remaining Run.Daily deviations are .NET runtime / platform libm differences.
- Fidelity check on Windows (opt-in net472 target of the tests):
  dotnet test next/tests/HeuristicLab.Tests -f net472 -p:HlFramework=true --filter <tests>

## Porting conventions
- Generate projects with next/tools/port-project.py <legacy csproj>: it maps references to
  ported projects (unported ones are an error), resources, aliases, InternalsVisibleTo,
  packages, dead files, and reports what it drops. Hand-tune only what it reports.
- Port via linked files — never copy files; legacy tree stays source of truth until a
  file needs changes. next/core/Directory.Build.props/.targets do the linking: a project
  sets LegacyDir, AssemblyName, RootNamespace, AssemblyTitle, Description, and
  LegacyExclude ($(LegacyDir)-prefixed, semicolon-separated) for files to leave out.
- Package versions live only in next/core/Directory.Packages.props (central management
  with transitive pinning); PackageReference items carry no Version.
- Compiled sources are exactly the legacy csproj's Compile items, in their order (read by
  Directory.Build.targets). After every port run next/tools/check-linked-sources.py.
- After every port also run the platform check:
  dotnet build <project> -p:HlPlatformCheck=true  (net10.0, enables CA1416).
- Replacement files (when a legacy file needs changes) live in the next/ project folder,
  e.g. next/core/HeuristicLab.PluginInfrastructure/ErrorHandling.cs.
- Link a legacy test folder into next/tests as soon as all its dependencies are ported.
- Keep original assembly names (HEAL.Attic .hl compatibility depends on it).
- Decouple as you go: Item.ItemImage → icon registry; AppDomain plugin loading →
  assembly scanning; native dlls → optional providers with managed fallbacks.
- Compile against direct references only (DisableTransitiveProjectReferences), as the
  legacy build did.
- One ported project = one commit.
