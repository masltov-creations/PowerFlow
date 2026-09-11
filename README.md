# PowerFlow

**Adaptive CPU power policy for Windows, built around measured profiles instead of plan-name folklore.**

PowerFlow watches the machine, explains what it is doing, and chooses the least-expensive qualified CPU profile for the work that is actually happening. The product is intentionally small: **Live**, **Workloads**, and **Baseline**, with **Settings** as a secondary surface.

## Current UI

![PowerFlow Live dashboard with a fully populated 60-second telemetry graph](docs/assets/powerflow-fullscreen.png)

This is the current qualified full-screen **Live** view.

## Product model

PowerFlow has two kinds of authority:

- **AUTO** is the normal operating mode. It interprets telemetry through the adaptive envelope and selects one of the same qualified PowerFlow profiles available manually.
- **Manual profiles** are explicit overrides. They latch until AUTO is selected again.

AUTO maps semantic demand to measured PowerFlow profiles:

| AUTO zone | PowerFlow profile | Windows plan | Core floor | EPP | Boost |
| --- | --- | --- | ---: | ---: | ---: |
| Eco | SAVER | Power Saver | 10% | 60 | 0 |
| Efficient | BAL-E | Balanced | 25% | 35 | 3 |
| Responsive | BAL-P | Balanced | 50% | 20 | 3 |
| Boost | PERF | Balanced | 75% | 10 | 2 |

**ULTRA** is a manual-only profile: High Performance, 100% core floor, EPP 10, boost mode 2. AUTO does not enter ULTRA.

The important architectural rule is that AUTO and manual operation use the **same profile definitions and processor-policy actuator**. There is no legacy threshold controller competing for ownership.

## The four user-facing surfaces

### Live

Live is the operating cockpit. It shows current state, recent trajectory, CPU/core behavior, package power, clock/performance evidence, the adaptive envelope, the currently important actor, and why PowerFlow is or is not escalating.

The tray glance, compact dashboard, expanded view, and full-screen view are presentations of the same live state rather than separate products.

### Workloads

Workloads gives applications one understandable policy: **Low**, **Normal**, or **High** importance.

- **Low**: may use the machine, but cannot promote AUTO into Boost by itself.
- **Normal**: may earn Boost after qualified sustained demand.
- **High**: may earn Boost faster for latency-sensitive work.

New importance rules never enter the old game/performance hard-latch path. Service-specific policy and custom entitlement timing are not exposed because they are not qualified product behavior.

### Baseline

**BASELINE MACHINE** runs seven fixed five-minute profiles, in this order:

1. Windows Power Saver
2. Windows Balanced
3. PowerFlow SAVER
4. PowerFlow BAL-E
5. PowerFlow BAL-P
6. PowerFlow PERF
7. PowerFlow ULTRA

The 35-minute run records the policy signature actually applied, idle package power, throughput at 1/2/4/8/16 workers, throughput per watt, and the measured efficiency/throughput knee. It restores the exact pre-run processor policy on completion, cancellation, or failure.

AUTO is deliberately **not** a baseline leg. AUTO is dynamic and is validated through transition/scenario tests instead of pretending it is a fixed profile.

### Settings

Settings contains only durable product preferences:

- theme;
- reduced motion;
- start with Windows.

CPU behavior belongs to AUTO, Workloads, and the fixed profiles—not to a page of implementation knobs.

## Safety and ownership

PowerFlow changes Windows power plans and processor policy only through its qualified actuation path. It does **not** alter BIOS settings, voltages, fan curves, or firmware.

- one normal user process; no privileged service;
- manual authority takes precedence over AUTO;
- AUTO is confidence-gated and entitlement-gated;
- profile application is read back and failures are surfaced;
- machine baseline snapshots and restores the original policy in `finally`;
- preview paths remain read-only;
- legacy Tune/service-policy configuration is normalized out when a current build loads it, so obsolete hidden state cannot silently steer AUTO.

## Repository truth

The current product contract is [`docs/product.md`](docs/product.md). The current implementation architecture is [`docs/architecture.md`](docs/architecture.md). Current qualification evidence is under [`docs/acceptance`](docs/acceptance).

Dated design/spec/plan material from earlier iterations is retained under [`docs/history`](docs/history) as historical evidence only. It is **not** a description of current product behavior.

## Build

PowerFlow targets x64 Windows, .NET 8, and unpackaged WinUI 3.

```powershell
dotnet restore PowerFlow.sln
dotnet test PowerFlow.sln -c Release
dotnet build PowerFlow.sln -c Release
```

Run in the background:

```powershell
.\src\PowerFlow.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\PowerFlow.App.exe --background
```

Open the dashboard through the running instance:

```powershell
.\src\PowerFlow.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\PowerFlow.App.exe --dashboard
```

Per-user configuration lives at `%LOCALAPPDATA%\PowerFlow\config.json`.

## Current release bar

A PowerFlow build is not considered current merely because it compiles. The release gate requires the Core, Windows, and App test suites, a clean Release build, XAML parsing, `git diff --check`, a clean runtime launch when live UI validation is authorized, and exact policy-restoration evidence for any live baseline run.

The product bar is equally strict: **a first-time user should be able to explain every visible control, and every visible control must have a verified runtime effect.**
