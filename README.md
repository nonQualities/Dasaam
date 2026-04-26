# Daśam — FSM Simulator

A beautiful, minimal finite state machine designer and simulator built with C# and Avalonia UI.
The name *Daśam* (दशाम्) is Sanskrit for "the state" — the accusative form of *daśā*.

## Requirements (for building)

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Works on Windows, macOS, and Linux

## Build & Run

```bash
cd Dasam
dotnet run
```

---

## Usage Guide

### Toolbar (or keyboard shortcuts)

| Tool | Key | Action |
|------|-----|--------|
| Select | `S` | Click and drag states to move them |
| Add State | `A` | Click anywhere on canvas to place a state |
| Connect | `C` | Click source state, then target state |
| Delete | `D` | Click any state or transition to remove it |
| Simulate | `Space` | Enter an input string and step through |

### Building an FSM

1. Press **A** and click the canvas to add states — give each a name (e.g. `q0`, `q1`)
2. The **first state** added is automatically the initial state
3. **Right-click** any state to:
   - Toggle it as an **accepting** (final) state
   - Reassign the **initial** state
4. Press **C** and click source → target to add a transition
   - Enter the symbol (e.g. `0`) or comma-separated symbols (e.g. `0,1`)
   - **Right-click** a transition arrow to delete it
5. Press **Space** to switch to Simulate mode

### Simulation

1. Type an input string in the bottom bar and press **Run ▶**
2. Use **Step ▶** (or `→`) to advance one symbol at a time
3. Use **◀ Back** (or `←`) to step backwards
4. The canvas highlights the current active state:
   - **Blue glow** — actively running
   - **Green glow** — string accepted ✓
   - **Red glow** — string rejected ✗
5. The input tape at the bottom shows consumed vs. pending characters

---

## Project Structure

```
Dasam/
├── Dasam.csproj          Project file (Avalonia 11)
├── Program.cs            Entry point
├── App.axaml             Application + theme registration
├── App.axaml.cs
├── Styles.axaml          Custom dark theme styles
├── FsmCanvas.cs          Core control: FSM rendering + all interaction
├── MainWindow.axaml      Layout: toolbar, canvas, simulation bar
├── MainWindow.axaml.cs   UI logic: dialogs, mode switching, sim display
└── Models/
    ├── FsmState.cs
    └── FsmTransition.cs
```

## Design Notes

- **Color palette**: Deep navy (`#0D1117`) canvas with electric-blue (`#60A5FA`) accents
- **Transitions**: Quadratic Bézier curves; bidirectional pairs curve away from each other
- **Self-loops**: Rendered as a small circle above the state node
- **Arrowheads**: Filled triangles computed from the tangent angle at the transition endpoint
- **Grid**: Subtle dot grid for spatial orientation without visual noise
- **No external icon fonts** — all toolbar icons are inline SVG paths

---

*Project for SWD0400404: Software Product Development (GUI)*  
*Gauhati University — Department of Information Technology*
