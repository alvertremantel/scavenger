# Repository Guidelines

## Project Structure & Module Organization

- `src/Scavenger.Sim/`: authoritative simulation, organized into `Physical/`, `Industry/`, `Scavenging/`, and `Region/`; includes scenarios, commands, reports, and serialization.
- `src/Scavenger.Desktop/`: Avalonia UI with four workspaces: Dashboard, Region, Compound, and Registers. Maps and panels are primarily C#; application styling lives in `App.axaml`.
- `src/Scavenger.Cli/`: scripted scenarios, JSON reports, and save/load demonstrations.
- `tests/Scavenger.Sim.Tests/` and `tests/Scavenger.Desktop.Tests/`: simulation and headless UI tests.
- `docs/`: provisional design, implementation plans, and play guides. Start with `docs/MAP-CENTERED-UI.md`.
- `haldane/haldane/`: independently versioned, ignored engine checkout, referenced directly by project files. Match `haldane-revision.txt`; commit engine changes in its own repository.

Generated screenshots/maps belong in ignored `artifacts/`; local saves belong in ignored `saves/`.

## Build, Test, and Development Commands

Requires the .NET 10 SDK and the compatible local Haldane checkout.

```sh
dotnet build Scavenger.slnx
dotnet test Scavenger.slnx
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Cli -- --industry --roundtrip --ticks 2400
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/region.png --view region --width 1050 --height 780
dotnet test haldane/haldane/Haldane.slnx
```

These build the solution, run game tests, launch the UI, verify the industrial walkthrough across save/load, render a headless screenshot, and test the independent engine, respectively.

## Coding Style & Naming Conventions

Use four-space indentation, file-scoped namespaces, PascalCase types/methods/properties, and camelCase locals/parameters. Match nearby C# conventions; nullable reference types and implicit usings are enabled. Application projects treat warnings as errors. No repository-wide formatter configuration is currently defined.

Keep gameplay rules in simulation modules and commands; UI panels should submit commands and display reports. Preserve deterministic seeded generation, save/replay behavior, mass accounting, and resource ownership.

## Testing Guidelines

Tests use xUnit `[Fact]`/`[Theory]`; name files `*Tests.cs` and methods for observable behavior. No numeric coverage threshold is configured. Add meaningful regression tests for changed mechanics, including rejection paths and save/load where relevant. Desktop tests use Avalonia headless input/rendering; check actual mouse flows and layout at minimum and standard window sizes when changing UI.

## Commit & Pull Request Guidelines

History uses imperative, descriptive subjects, such as “Generate seeded regional towns…”; conventional-commit prefixes are not required. Keep commits focused. PR descriptions should explain the problem, resulting behavior, validation commands/results, and material limitations. Link relevant issues/design documents and include screenshots for visible UI changes. Update play guides when workflows change.
