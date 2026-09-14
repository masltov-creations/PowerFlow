# PowerFlow

PowerFlow is a tray-first Windows CPU power manager. It combines live Windows telemetry, measured machine behavior, per-app importance, and reversible processor-policy changes to move between efficient and responsive operating profiles without touching BIOS or firmware settings.

![PowerFlow Live dashboard](docs/assets/powerflow-fullscreen.png)

## What it does

- Runs in **Auto** or a manually selected power profile.
- Observes CPU demand, runnable-queue pressure, core parking, processor performance, and package power when Windows exposes those counters.
- Applies Windows power schemes plus AC processor-policy settings for core-parking floor, Energy Performance Preference (EPP), and boost mode.
- Lets workloads be marked Low, Normal, or High importance so latency-sensitive apps can qualify for performance sooner.
- Includes a built-in machine baseline that compares Windows-native and PowerFlow profiles at several worker counts.
- Uses one persistent WinUI motion shell that moves through Hover, Compact, Expanded, Workspace, and Full Screen views.
- Captures policy state before temporary tuning and uses write/readback verification plus restore paths rather than blind writes.

## Operating profiles

In **Auto**, PowerFlow currently moves between four profiles:

| Profile | Intended use | Windows plan | Core floor | EPP | Boost |
| --- | --- | --- | ---: | ---: | ---: |
| SAVER | Quiet/background work | Power Saver | 10% | 60 | 0 |
| BAL-E | Efficient everyday work | Balanced | 25% | 35 | 3 |
| BAL-P | Responsive everyday work | Balanced | 50% | 20 | 3 |
| PERF | Sustained high performance | Balanced | 75% | 10 | 2 |

**ULTRA** is manual-only. It uses High Performance with a 100% core floor, EPP 10, and boost mode 2. Auto does not select ULTRA.

The Live view deliberately separates **Model Zone** (what demand currently looks like) from **Applied Profile** (what Windows is actually running), because qualification, residency, and recovery rules can make those differ briefly.

## Live shell

The dashboard is one window rather than a family of popups:

- **Hover** is the minimum pinned view (currently 320 x 219 logical pixels) with state, core count, key telemetry, and a compact trend view.
- **Compact** adds the primary controls and more telemetry.
- **Expanded / Workspace / Full Screen** progressively disclose navigation, context, tuning, and analysis surfaces.
- Pinned views grow from their current on-screen position and clamp only as needed to remain visible.
- Automatic view changes use a minimum-jerk native bounds curve with a slightly trailing monotonic semantic/layout curve. Reduced Motion makes these transitions immediate.

## Workloads

Apps can be assigned three importance levels:

- **Low** - background or non-urgent work; it cannot push Auto into PERF by itself.
- **Normal** - default behavior.
- **High** - latency-sensitive work that may reach higher performance sooner.

Workload rules influence Auto. They do not permanently lock the machine to a profile.

## Baseline Machine

The standard baseline compares:

1. Windows Power Saver
2. Windows Balanced
3. PowerFlow SAVER
4. PowerFlow BAL-E
5. PowerFlow BAL-P
6. PowerFlow PERF
7. PowerFlow ULTRA

Each profile runs for five minutes. PowerFlow records idle behavior and CPU throughput at 1, 2, 4, 8, and 16 software workers, plots the curves, and identifies useful efficiency/performance knees. The processor policy that was active before the run is restored when the run completes or is cancelled.

The current profiler stops at **16 worker threads**. That is a characterization ceiling, not a requirement that the CPU have 16 cores; very large CPUs simply are not fully saturated by this baseline yet.

## Platform and hardware compatibility

PowerFlow is intentionally Windows-specific. The current application target is:

- x64 only (`win-x64`)
- .NET 8
- WinUI 3 / Windows App SDK 2.4
- target framework `net8.0-windows10.0.19041.0`
- minimum Windows platform declared by the project: `10.0.17763.0`

Important implementation limits:

- **AC processor policy only.** PowerFlow currently reads and writes AC values (`PowerReadACValueIndex` / `PowerWriteACValueIndex`). It does not change DC/battery processor-policy values. A laptop running on battery can therefore behave differently from the same profile while plugged in.
- **Single processor group for detailed core telemetry.** Per-logical-processor parking/utilization telemetry is currently built for Windows processor group 0. Systems that require multiple processor groups (commonly >64 logical processors) do not get the detailed core map rather than receiving misleading partial data.
- **Package watts are optional.** Package power uses the Windows PDH `Energy Meter(rapl_package0_pkg)\Power` counter. Some CPUs, firmware, drivers, virtual machines, and Windows configurations do not expose it; the dashboard then omits package watts.
- **Other PDH counters are optional.** Core parking, processor utility/frequency/performance, and queue counters can be unavailable. Rich pressure modeling degrades to ordinary CPU-demand input where possible.
- **Power-plan availability varies.** OEM/Modern Standby systems or managed enterprise PCs may hide or restrict Power Saver/High Performance schemes or processor settings. PowerFlow verifies writes/readback and reports failure instead of bypassing Windows policy.
- **Firmware remains authoritative.** CPU firmware, chipset drivers, BIOS settings, thermal limits, OEM power management, and silicon behavior can change how EPP, core parking, and boost settings behave. PowerFlow does not write voltage, P-states directly, fan curves, BIOS options, or firmware.

These are capability differences, not reasons to fabricate telemetry. Missing optional sensors should result in reduced information rather than guessed values.

## Safety and recovery

PowerFlow changes Windows power configuration, so the write path is deliberately conservative:

- captures the active scheme/policy before changes where a reversible operation requires it;
- verifies processor-policy writes by reading them back;
- attempts to restore the prior values if a multi-setting apply fails;
- restores the pre-baseline policy after completion/cancellation;
- uses a graceful `--shutdown` path for durable deployment instead of force-killing the runtime.

PowerFlow does **not** change BIOS settings, CPU voltage, fan curves, or firmware.

## Configuration and local state

Per-user configuration lives outside the repository at:

```text
%LOCALAPPDATA%\PowerFlow\config.json
```

Durable deployments use:

```text
%LOCALAPPDATA%\PowerFlow\App\releases\<release-id>\
%LOCALAPPDATA%\PowerFlow\App\current.txt
```

Start-with-Windows registration is per-user under the standard `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key.

No credentials or secrets are required by PowerFlow.

## Build and test

Prerequisites for source builds:

- Windows x64
- .NET 8 SDK
- network access for the initial NuGet restore (unless packages are already cached)

```powershell
dotnet restore PowerFlow.sln
dotnet test PowerFlow.sln -c Release
dotnet build PowerFlow.sln -c Release
```

The app project packages the Windows App SDK self-contained. A normal `dotnet build` is still a .NET application build; target machines should have a compatible .NET 8 runtime unless you explicitly publish the application self-contained.

Run the Release build in the background:

```powershell
.\src\PowerFlow.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\PowerFlow.App.exe --background
```

Open the dashboard through the running instance:

```powershell
.\src\PowerFlow.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\PowerFlow.App.exe --dashboard
```

## Durable local deployment

The repository includes a deployment script that builds a versioned per-user release, gracefully replaces an existing PowerFlow process, verifies the startup registration, updates `current.txt`, and keeps a bounded number of releases:

```powershell
.\scripts\Deploy-PowerFlow.ps1
```

By default it runs the App and Windows test projects and retains two releases. After an independently completed full test gate, deployment can skip the duplicate test pass:

```powershell
.\scripts\Deploy-PowerFlow.ps1 -SkipTests -KeepReleases 2
```

The script intentionally refuses ambiguous multi-runtime states and does not force-kill PowerFlow if graceful shutdown fails.

## Repository layout

```text
src/PowerFlow.Core/       Policy, envelope, rules, profiling models
src/PowerFlow.Windows/    Windows telemetry and power-policy adapters
src/PowerFlow.App/        WinUI application, controller runtime, tray, dashboard
tests/                    Core, Windows, and application tests
scripts/                  Deployment/maintenance scripts
docs/product.md           Current product behavior
docs/architecture.md      Current architecture and system boundaries
docs/design/              Current focused design notes
docs/history/             Superseded design/implementation material retained for traceability
```

Historical documents are not current requirements. Use this README, `docs/product.md`, and `docs/architecture.md` as the current source of truth.

## More detail

- [Product behavior](docs/product.md)
- [Architecture](docs/architecture.md)
- [Current design notes](docs/design/)
- [Acceptance evidence](docs/acceptance/)
- [Historical material](docs/history/)