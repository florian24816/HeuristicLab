# HeuristicLab (fork) – HeuristicLab Studio

A fork of [HeuristicLab](https://dev.heuristiclab.com) 3.3.16 ("Prague", 2019),
the open-source framework for heuristic and evolutionary optimization. Upstream
development has been dormant since 2024; this fork modernizes the platform while
keeping the original code as a reference.

It contains:

- **HeuristicLab Studio**, a new cross-platform desktop application (Windows and Linux)
  built with [Avalonia](https://avaloniaui.net)
- **`hl`**, a command-line tool for scripted research workflows
- the original algorithms and libraries, running on .NET 10

> HeuristicLab Studio is under active development. Experiments are saved as `.hl`
> files that HeuristicLab 3.3 can still open.

## Goals

- Port the reusable computational core (optimization engine, algorithms, data
  analysis, symbolic regression) to .NET Standard 2.0 / .NET 10
- Build a new cross-platform GUI with Avalonia, running on Windows and Linux
- Keep the legacy .NET Framework 4.7.2 tree untouched as a reference oracle

## Installation

### What you need

  | | Windows | Linux |
  |---|---|---|
  | .NET 10 SDK (required) | winget install Microsoft.DotNet.SDK.10 | see below |
  | Git (required) | winget install Git.Git | sudo apt install git / sudo dnf install git |
  | Python 3 + pandas, seaborn (optional, for the Python export) | winget install Python.Python.3.12, then pip install pandas seaborn matplotlib | sudo apt install python3-pandas python3-seaborn or pip install pandas seaborn matplotlib |
  | Desktop notifications (optional) | built in | notify-send (package libnotify-bin on Debian/Ubuntu, libnotify on Fedora) |

#### Windows

1. Open PowerShell or Windows Terminal and install the .NET 10 SDK and Git:
   ```powershell
   winget install Microsoft.DotNet.SDK.10
   winget install Git.Git
   ```
   Alternatively, download the SDK installer from/download/dotnet/10.0.
2. Open a new terminal and check the installation: `dotnet --version` should print `10.0.x`.

#### Linux

1. Install the .NET 10 SDK, either from your distribution
   (Fedora: `sudo dnf install dotnet-sdk-10.0`;tnet-sdk-10.0`,
   on older releases from the `ppa:dotnet/backports` PPA) or for your user only with
   Microsoft's install script:
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.0
   echo 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
   source ~/.bashrc
   ```
2. Avalonia needs a few X11 and font libraries. them already;
   otherwise:
   ```bash
   sudo apt install libx11-6 libice6 libsm6 libfontconfig1     # Debian / Ubuntu
   sudo dnf install libX11 libICE libSM fontcon
   ```
3. Check: `dotnet --version` should print `10.0

### Get, build and run (preferred method of running the project)

```bash
git clone https://github.com/florian24816/HeuristicLab.git
cd HeuristicLab

dotnet build next/HeuristicLab.Next.slnx
dotnet run --project next/app/HeuristicLab.Studio/HeuristicLab.Studio.csproj
```

The command-line tool:

```bash
dotnet run --project next/app/HeuristicLab.Cli -- list algorithms
```

### Install for everyday use (not tested)

On Linux:

```bash
next/tools/install.sh              # installs hl and hl-studio into ~/.local/bin, plus a desktop entry
next/tools/install.sh --uninstall
```

This publishes Release builds; afterwards Studio starts from the application menu or with
`hl-studio`. It needs the .NET 10 runtime (incl

On Windows, publish a Release build and start ` the output folder:

```powershell
dotnet publish next/app/HeuristicLab.Studio -c Release -o publish\studio
```

### Run the tests

```bash
next/tools/run-tests.sh quick     # Linux / Git Bash: legacy, runtime/CLI and Studio tests
dotnet test next/tests/HeuristicLab.Studio.Testt (any OS)
```

## HeuristicLab Studio – features

### Experiments
- Several experiments open side by side, each aorithms with
  their problems, batch runs (repetitions) and time-limit runs.
- Building blocks are moved by drag and drop, a a faded
  preview where they land. Several blocks can be selected (Ctrl+click or a selection
  rectangle) and moved together. Blocks can be
- A status per algorithm (not run, waiting, working, finished, failed), live progress of
  batch and time-limit runs, and saved/unsaved
- Edit a parameter across all algorithms of an experiment at once (Shift+click), with
  automatic naming of the algorithms after the
- "Eject" takes experiments out of the workspace, saving unsaved ones first if wanted.
- Open several `.hl` files at once, recent filele experiments,
  and a "New" dialog: pick an algorithm, a compatible problem, then a benchmark instance or
  your own CSV/Parquet data.

### Warnings before a run fails
HeuristicLab offers many combinations that crash or silently do nothing. Studio marks them
in **yellow**, with an explanation, before you y running every
choice and edge value of these algorithms on HeuristicLab's problems:
- move generators without a matching evaluator  in local
  search, tabu search and simulated annealing
- operators and settings that crash or search n, Island OSGA,
  SASEGASA and NSGA-II (e.g. tree-only selectors on vector problems, odd numbers of
  parents, populations below 2, zero temperatur
- problems an algorithm cannot run at all, already in the problem picker

Choice lists always show only the operators that fit the current settings.

### Algorithm details
- Problem view with benchmark data, best known  problem
  (tours, routes, packings, schedules, landscapes, expression trees, ...).
- Every parameter editable, including nested op
- An interactive operator graph with breakpoints and a fullscreen view.
- Live results and charts while the algorithm r
- **Detailed analysis:** records every solution an algorithm creates (operator, parents,
  quality, iteration) and replays it step by strawn in blue,
  earlier iterations in shades of red, what changed in yellow, with animation and
  fullscreen. The recording is temporary and ca

### Runs and results
- **Single runs:** pressing Start on one algorithm creates "single runs". They are kept
  apart from the experiment's runs, so trials dyou really made.
  They are saved next to the experiment (`name.single-runs.hl`).
- **Runs tab:** all runs of all experiments witnd pictures.
  Runs can be deleted per experiment, or only the single runs.
- **Results tab:** tick any experiments, batch ns to compare.
  - filters (operators matching the column type)
  - grouping by any columns
  - a table (50 rows at a time, expandable)
  - charts: box plots, scatter, histograms, cumy curves
  - statistics: Kruskal-Wallis, Mann-Whitney U with Bonferroni-Holm, t-test,
    Cohen's d, Hedges' g
- Shortcuts: "Select all", "Select all (without single runs)", "Unselect all"; select
  several sources (Ctrl+click, Shift+click, preogether.
- **More results views:** "Add window" opens further results tabs in a second tab bar.
  Tabs can be reordered, dragged out into theird screen)
  and docked back.
- Large tables are processed in the background indow stays
  responsive.
- **Export:** CSV (as in HeuristicLab), or **Pyr experiment's
  structure, the data as JSON, and an analysis notebook with pandas and seaborn examples.

### And more
- **Scripts tab** (HeuristicLab's C# scripts).
- **Resources tab:** CPU cores, threads per algorithm, parallel experiments, memory limit
  and priority.
- **Light and dark mode**, and desktop notifications when runs finish.

## The `hl` command line

```
hl list algorithms|problems           hl instan
hl new <algorithm> --problem P --instance I --out f.hl
hl new <algorithm> --data prices.csv --target y
hl run f.hl --repeat 30 --parallel 8 --store results/
hl run f.hl --sweep "PopulationSize=50,100,200"
hl walkforward f.hl --train 500 --test 100
hl store summary results/ --metric BestQuality
```

Batches use explicit seeds, so results are reproducible, and parallel and sequential
runs give identical results.

## Current state of the port

- The original algorithm and library source com
  with essentially zero changes to the C# code itself. The ported projects
  reuse the legacy source files via file linkin
- The build system had to be redone: new SDK-style project files, modern
  NuGet references (HEAL.Attic, Google.Protobuf
  a fix to the compile order.
- Files saved by the fork stay compatible with
- Tests were migrated from MSTest v1 to modern MSTest. The suite passes on
  .NET 10 on Windows and Linux (427 legacy testtudio
  tests), except:
  - Windows-only tests (System.Drawing Bitmap/F
    interpreter), which are platform-gated
  - 5 seeded sample tests whose recorded expect
    .NET Framework. They deviate on .NET 10 because floating-point results
    of transcendental math functions differ sli
    which sends stochastic algorithms onto different (but equally valid)
    trajectories. In one case (PSO on Rastrigin
    found a significantly better solution than the recorded baseline.
- Not ported yet: external evaluation problems ab), exact
  optimization (OR-Tools), Hive/OKB services, and the original Windows Forms views
  (replaced by Studio).

---

# HeuristicLab
[![TeamCity build status](https://builder.heuristiclab.com/app/rest/builds/buildType:id:HeuristicLab_Test/statusIcon.svg)](https://builder.heuristiclab.com/?guest=1)

## About HeuristicLab

[HeuristicLab](https://dev.heuristiclab.com) is an open-source environment for heuristic optimization that features several metaheuristic optimization algorithms as well as optimization problems. It is developed by the research group [Heuristic and Evolutionary Algorithms Laboratory (HEAL)](https://heal.heuristiclab.com) of the [University of Applied Sciences Upper Austria](https://www.fh-ooe.at/en) and is based on C# and Microsoft .NET. 

We know that many people are using HeuristicLab in business, research and teaching activities. Please contact us, if you're using HeuristicLab in your teaching activities, if you have interesting business cases, or if you would like to get in contact for a research collaboration. It would be great to hear from you!

## Core Features

  * Graphical User Interface
  * Algorithm Prototyping
  * Evolutionary Algorithms
  * Genetic Programming
  * Data Analysis and Symbolic Regression
  * Simulation-based Optimization
  * Experiment Design and Analysis
  * Plugin-based Architecture

## Awards

In May 2009 we submitted HeuristicLab for the **Microsoft Innovation Award 2009**. Both, an expert jury and a public audience, perceived HeuristicLab as one of the three most innovative software projects from 64 submissions. We are of course very happy about such a recognition and the award itself, which was presented by Austria's Federal Minister for Science and Research Dr. Johannes Hahn.

## System Requirements

Starting from 3.3.16 HeuristicLab requires a 64-bit operating system to use all features. Specifically, the OR-Tools integration is affected. You can still run HeuristicLab in 32-bit environments, but you have to remove Google.OrTools.dll from the folder containing the HeuristicLab.exe file before you start it.

HeuristicLab is based on the Microsoft .NET Framework. In order to be able to run HeuristicLab properly, you have to download and install the following software package:
  * [​Microsoft .NET Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)

## HeuristicLab Google Group

If you have any questions, comments, recommendations, or feedback in general, you can also join our [​HeuristicLab Google Group](https://groups.google.com/g/heuristiclab).

## License

HeuristicLab is free software: you can redistribute it and/or modify  it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

HeuristicLab is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.

See the [GNU General Public License v3](LICENSE.txt) for more details.
