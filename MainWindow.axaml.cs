using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Dasam.Models;
using System;
using System.IO;
using Avalonia.Platform.Storage;

namespace Dasam;

public partial class MainWindow : Window
{
    private Point _pendingStatePos;
    private FsmState? _pendingTransFrom;
    private FsmState? _pendingTransTo;

    public MainWindow()
    {
        InitializeComponent();
        WireCanvas();
        WireKeyboard();
        SetActiveToolButton(BtnSelect);
    }

    private void WireCanvas()
    {
        FsmView.RequestAddState += pos =>
        {
            _pendingStatePos = pos;
            ShowNameOverlay(pos);
        };

        FsmView.RequestAddTransition += (from, to) =>
        {
            _pendingTransFrom = from;
            _pendingTransTo = to;
            ShowSymbolOverlay();
        };

        FsmView.SimulationChanged += UpdateSimUI;

        // Hide empty hint when states exist
        FsmView.PointerPressed += (_, _) =>
        {
            EmptyHint.IsVisible = FsmView.States.Count == 0;
        };
    }

    private void WireKeyboard()
    {
        KeyDown += (_, e) =>
        {
            // Only handle if no overlay is open
            var focusManager = GetTopLevel(this)?.FocusManager;
            if (focusManager?.GetFocusedElement() is TextBox) 
                return;

            if (NameOverlay.IsVisible || SymbolOverlay.IsVisible) return;
            if (NameOverlay.IsVisible || SymbolOverlay.IsVisible) return;
            switch (e.Key)
            {
                case Key.S: SetMode(EditorMode.Select); break;
                case Key.A: SetMode(EditorMode.AddState); break;
                case Key.C: SetMode(EditorMode.Connect); break;
                case Key.D: SetMode(EditorMode.Delete); break;
                case Key.Space:
                    if (FsmView.SimulationStatus == SimStatus.Idle)
                        SetMode(EditorMode.Simulate);
                    break;
                case Key.Escape:
                    if (FsmView.SimulationStatus != SimStatus.Idle)
                        OnStopSim(null, null!);
                    else
                        SetMode(EditorMode.Select);
                    break;
                case Key.Right:
                    if (FsmView.SimulationStatus == SimStatus.Running)
                        FsmView.StepForward();
                    break;
                case Key.Left:
                    FsmView.StepBack();
                    break;
            }
        };
    }

    // ─────────────────────────────── Mode Buttons ─────────────────────────────

    private void OnModeSelect(object? s, RoutedEventArgs e)  => SetMode(EditorMode.Select);
    private void OnModeAddState(object? s, RoutedEventArgs e)=> SetMode(EditorMode.AddState);
    private void OnModeConnect(object? s, RoutedEventArgs e) => SetMode(EditorMode.Connect);
    private void OnModeDelete(object? s, RoutedEventArgs e)  => SetMode(EditorMode.Delete);
    private void OnModeSimulate(object? s, RoutedEventArgs e)=> SetMode(EditorMode.Simulate);

    private void OnClear(object? s, RoutedEventArgs e)
    {
        FsmView.Clear();
        EmptyHint.IsVisible = true;
        SetMode(EditorMode.Select);
    }

    private void SetMode(EditorMode mode)
    {
        FsmView.SetMode(mode);
        FsmView.ResetSim();

        SimPanel.IsVisible      = false;
        SimStartPanel.IsVisible = mode == EditorMode.Simulate;

        if (mode == EditorMode.Simulate)
        {
            SimInputBox.Text = "";
            SimInputBox.Focus();
        }

        // Update status bar
        (StatusText.Text, StatusDot.Fill) = mode switch
        {
            EditorMode.Select   => ("Select / move",   new SolidColorBrush(Color.Parse("#3A5A9B"))),
            EditorMode.AddState => ("Click to add state", new SolidColorBrush(Color.Parse("#34D399"))),
            EditorMode.Connect  => ("Click source → target", new SolidColorBrush(Color.Parse("#FBBF24"))),
            EditorMode.Delete   => ("Click to delete", new SolidColorBrush(Color.Parse("#F87171"))),
            EditorMode.Simulate => ("Enter input string", new SolidColorBrush(Color.Parse("#60A5FA"))),
            _ => ("Ready", new SolidColorBrush(Color.Parse("#3A5A9B")))
        };

        SetActiveToolButton(mode switch
        {
            EditorMode.Select   => BtnSelect,
            EditorMode.AddState => BtnAddState,
            EditorMode.Connect  => BtnConnect,
            EditorMode.Delete   => BtnDelete,
            EditorMode.Simulate => BtnSimulate,
            _ => BtnSelect
        });
    }
    
    private async void OnSaveFile(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Machine",
            DefaultExtension = "das",
            FileTypeChoices = new[] { new FilePickerFileType("Daśam Files") { Patterns = new[] { "*.das" } } }
        });

        if (file != null)
        {
            var json = FsmSerializer.Serialize(FsmView.States, FsmView.Transitions);
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
        }
    }

    private async void OnLoadFile(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Machine",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Daśam Files") { Patterns = new[] { "*.das" } } }
        });

        if (files.Count >= 1)
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            FsmSerializer.Deserialize(json, FsmView);
        }
    }

    private void SetActiveToolButton(Button active)
    {
        Button[] all = { BtnSelect, BtnAddState, BtnConnect, BtnDelete, BtnSimulate };
        foreach (var b in all)
        {
            b.Classes.Remove("active");
            if (b == active) b.Classes.Add("active");
        }
    }

    // ─────────────────────────────── Name Overlay ─────────────────────────────

    private void ShowNameOverlay(Point canvasPos)
    {
        // Position overlay near the click, clamped inside window
        double ox = Math.Min(canvasPos.X + 12, Bounds.Width - 200);
        double oy = Math.Min(canvasPos.Y + 12, Bounds.Height - 120);

        // The overlay is in the Grid row=1 (canvas area); offset relative to that row's bounds
        var canvasBounds = FsmView.Bounds;
        ox = Math.Max(8, Math.Min(ox, canvasBounds.Width - 180));
        oy = Math.Max(8, Math.Min(oy, canvasBounds.Height - 100));

        NameOverlay.Margin = new Thickness(ox, oy, 0, 0);
        NameOverlay.IsVisible = true;

        // Default name = "q{n}"
        NameBox.Text = $"q{FsmView.States.Count}";
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnNameBoxKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var name = NameBox.Text?.Trim() ?? $"q{FsmView.States.Count}";
            if (string.IsNullOrEmpty(name)) name = $"q{FsmView.States.Count}";
            NameOverlay.IsVisible = false;
            FsmView.AddStateAt(_pendingStatePos, name);
            EmptyHint.IsVisible = FsmView.States.Count == 0;
        }
        else if (e.Key == Key.Escape)
        {
            NameOverlay.IsVisible = false;
        }
    }

    // ─────────────────────────────── Symbol Overlay ───────────────────────────

    private void ShowSymbolOverlay()
    {
        SymbolOverlay.IsVisible = true;
        SymbolBox.Text = "";
        SymbolBox.Focus();
    }

    private void OnSymbolBoxKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var sym = SymbolBox.Text?.Trim() ?? "";
            SymbolOverlay.IsVisible = false;
            if (!string.IsNullOrEmpty(sym) && _pendingTransFrom != null && _pendingTransTo != null)
                FsmView.AddTransition(_pendingTransFrom, _pendingTransTo, sym);
            _pendingTransFrom = _pendingTransTo = null;
        }
        else if (e.Key == Key.Escape)
        {
            SymbolOverlay.IsVisible = false;
            _pendingTransFrom = _pendingTransTo = null;
            FsmView.SetMode(EditorMode.Connect); // stay in connect mode
        }
    }

    // ─────────────────────────────── Simulation ───────────────────────────────

    private void OnSimInputKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnRunSim(null, null!);
    }

    private void OnRunSim(object? s, RoutedEventArgs e)
    {
        var input = SimInputBox.Text ?? "";
        if (!FsmView.StartSimulation(input))
        {
            StatusText.Text = "No initial state defined";
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#F87171"));
            return;
        }
        SimStartPanel.IsVisible = false;
        SimPanel.IsVisible = true;
        UpdateSimUI();
    }

    private void OnStepForward(object? s, RoutedEventArgs e)  => FsmView.StepForward();
    private void OnStepBack(object? s, RoutedEventArgs e)     => FsmView.StepBack();
    private void OnStopSim(object? s, RoutedEventArgs e)
    {
        FsmView.ResetSim();
        SimPanel.IsVisible      = false;
        SimStartPanel.IsVisible = false;
        SetMode(EditorMode.Select);
    }

    private void UpdateSimUI()
    {
        var status = FsmView.SimulationStatus;
        var input  = FsmView.SimInput;
        var step   = FsmView.SimStep;

        // Rebuild input tape
        InputTape.Children.Clear();
        for (int i = 0; i < input.Length; i++)
        {
            bool past    = i < step;
            bool current = i == step - 1 && step > 0;
            bool future  = i >= step;

            var cell = new Border
            {
                Width = 28, Height = 28,
                Margin = new Thickness(1, 0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(current ? Color.Parse("#1A3A6A")
                                                 : past   ? Color.Parse("#111827")
                                                          : Color.Parse("#13192B")),
                BorderBrush = new SolidColorBrush(current ? Color.Parse("#60A5FA")
                                                  : Color.Parse("#1C2541")),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = input[i].ToString(),
                    Foreground = new SolidColorBrush(current ? Color.Parse("#E2E8F0")
                                                     : past   ? Color.Parse("#374151")
                                                              : Color.Parse("#94A3B8")),
                    FontFamily = new FontFamily("Courier New,monospace"),
                    FontSize = 13,
                    FontWeight = current ? FontWeight.Bold : FontWeight.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            InputTape.Children.Add(cell);
        }

        // Empty input label
        if (input.Length == 0)
        {
            InputTape.Children.Add(new TextBlock
            {
                Text = "ε (empty)",
                Foreground = new SolidColorBrush(Color.Parse("#4A6FA5")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // Status badge
        (SimBadgeText.Text, SimBadge.Background, SimBadgeText.Foreground) = status switch
        {
            SimStatus.Running  => ($"Step {step}/{input.Length}",
                                   new SolidColorBrush(Color.Parse("#1A2A4A")),
                                   new SolidColorBrush(Color.Parse("#60A5FA"))),
            SimStatus.Accepted => ("✓ Accepted",
                                   new SolidColorBrush(Color.Parse("#14332A")),
                                   new SolidColorBrush(Color.Parse("#34D399"))),
            SimStatus.Rejected => ("✗ Rejected",
                                   new SolidColorBrush(Color.Parse("#3A1414")),
                                   new SolidColorBrush(Color.Parse("#F87171"))),
            _                  => ("—",
                                   new SolidColorBrush(Color.Parse("#1C2541")),
                                   new SolidColorBrush(Color.Parse("#4A6FA5")))
        };

        BtnStep.IsEnabled = status == SimStatus.Running && step < input.Length;
        BtnBack.IsEnabled = step > 0;

        // Main status bar
        StatusText.Text = status switch
        {
            SimStatus.Running  => $"Step {step} / {input.Length}",
            SimStatus.Accepted => "String accepted ✓",
            SimStatus.Rejected => "String rejected ✗",
            _ => "Simulating"
        };
        StatusDot.Fill = status switch
        {
            SimStatus.Running  => new SolidColorBrush(Color.Parse("#60A5FA")),
            SimStatus.Accepted => new SolidColorBrush(Color.Parse("#34D399")),
            SimStatus.Rejected => new SolidColorBrush(Color.Parse("#F87171")),
            _ => new SolidColorBrush(Color.Parse("#3A5A9B"))
        };
    }
}