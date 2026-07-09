# Dyncamelo sample graphs

Four small `.dyc` graphs that exercise the headless pipeline end to end. None
of them need Navisworks or WPF — they run anywhere the `dyncamelo` CLI runs,
including Linux and CI.

Run any of them from the repository root:

```bash
dotnet run --project src/Dyncamelo.Cli -- run samples/hello-math.dyc
dotnet run --project src/Dyncamelo.Cli -- validate samples/hello-math.dyc
dotnet run --project src/Dyncamelo.Cli -- list-nodes
```

The CLI exits with `0` when no node ended in the Error state, `1` when at
least one did, and `2` for unreadable inputs — so the samples double as CI
smoke tests. `tests/Dyncamelo.Integration.Tests` loads and runs every `.dyc`
file in this directory on every test run, which pins the on-disk format
against accidental drift.

## The graphs

### hello-math.dyc

`Number Slider (12.5) + Number (30) → Add → Multiply (× 2) → Math.Round → Watch`

The classic first graph: interactive inputs flowing through zero-touch math
nodes into a Watch node. The Round node's `digits` input is left unconnected,
so it uses its default value (`0`) — a defaulted zero-touch port in action.
Expected Watch value: **85**.

### list-lacing.dyc

`List.Range` produces `[1, 2, 3]` and `[10, 20]`, which feed three `Add`
nodes whose ports are scalar (`double`) — so the engine replicates ("laces")
the node over the lists, once per lacing mode:

| Node                | Lacing        | Result                             |
| ------------------- | ------------- | ---------------------------------- |
| Add (Shortest)      | Shortest      | `[11, 22]`                         |
| Add (Longest)       | Longest       | `[11, 22, 23]` (last item repeats) |
| Add (Cross Product) | Cross-Product | `[[11, 21], [12, 22], [13, 23]]`   |

Each result lands in its own Watch List node.

### string-report.dyc

Splits a sentence into words (`String.Split`), counts them (`List.Count`) and
formats a small report (`String.FromObject` + `String.Concat`), while a second
branch joins the words back together with `String.Join`. Expected Watch
values: **`Word count: 4`** and
**`dyncamelo, makes, navisworks, programmable`**.

### csv-roundtrip.dyc

Builds a 2×3 numeric table (`List.Range` × 2 → `List.Create`), writes it with
`CSV.WriteToFile` and immediately reads it back with `CSV.ReadFromFile` into a
Watch List. Two details worth copying into your own graphs:

- The output path is **relative** (`dyncamelo-sample-output.csv`), so the file
  is created in whatever directory you run the CLI from.
- The write node's `path` **output** is wired into the read node's `path`
  **input** — that both supplies the path and forces the read to execute after
  the write (dataflow sequencing of side effects).

Because it touches the file system, this graph is saved with
`RunType: Manual`, so a UI host will not re-run it automatically on every edit.

## Regenerating the samples

The graphs are authored in code (`src/Dyncamelo.Cli/SampleGraphs.cs`), not by
hand-editing JSON. After a format or node-library change, regenerate them with:

```bash
dotnet run --project src/Dyncamelo.Cli -- write-samples samples
```
