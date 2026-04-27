using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Dasam.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dasam;

public enum EditorMode { Select, AddState, Connect, Delete, Simulate }
public enum SimStatus { Idle, Running, Accepted, Rejected }

public class FsmCanvas : Control
{
    // ── Constants ────────────────────────────────────────────────────────────
    private const double R = 70;          // state radius
    private const double Arrow = 11;     // arrowhead size
    private const double ArrowAngle = Math.PI / 6;

    // ── Palette ──────────────────────────────────────────────────────────────
    private static readonly Color CBg       = Color.Parse("#0D1117");
    private static readonly Color CGrid     = Color.Parse("#2f3a63");
    private static readonly Color CState    = Color.Parse("#1C2541");
    private static readonly Color CStroke   = Color.Parse("#3A5A9B");
    private static readonly Color CActive   = Color.Parse("#1A3A6A");
    private static readonly Color CAstroke  = Color.Parse("#60A5FA");
    private static readonly Color CAccept   = Color.Parse("#14332A");
    private static readonly Color CAccstroke= Color.Parse("#34D399");
    private static readonly Color CReject   = Color.Parse("#3A1414");
    private static readonly Color CRejstroke= Color.Parse("#F87171");
    private static readonly Color CConn     = Color.Parse("#3A5A9B");
    private static readonly Color CConnact  = Color.Parse("#60A5FA");
    private static readonly Color CText     = Color.Parse("#E2E8F0");
    private static readonly Color CLabel    = Color.Parse("#94A3B8");
    private static readonly Color CInit     = Color.Parse("#60A5FA");
    private static readonly Color CSel      = Color.Parse("#60A5FA");

    private static readonly Typeface TfState = new(FontFamily.Parse("Inter,sans-serif"), FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface TfLabel = new(FontFamily.Parse("Inter,sans-serif"));

    // Data 
    public List<FsmState> States { get; } = [];
    public List<FsmTransition> Transitions { get; } = []; // Made public for serialization
    
    // ── Editor state 
    private EditorMode _mode = EditorMode.Select;
    private FsmState? _dragState;
    private Point _dragOffset;
    private FsmState? _connectFrom;
    private Point _mousePos;

    // ── Simulation state 
    public SimStatus SimulationStatus { get; private set; } = SimStatus.Idle;
    public string SimInput { get; private set; } = "";
    public int SimStep { get; private set; } = -1;
    private FsmState? _simCurrent;
    private List<(FsmState state, FsmTransition? trans, int charIdx)> _simHistory = new();

    // ── Events 
    public event Action<Point>? RequestAddState;
    public event Action<FsmState, FsmState>? RequestAddTransition;
    public event Action? SimulationChanged;

    public FsmCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    // Public API 

    public void SetMode(EditorMode mode)
    {
        _mode = mode;
        _connectFrom = null;
        _dragState = null;
        foreach (var s in States) s.IsSelected = false;
        Cursor = mode switch
        {
            EditorMode.AddState  => new Cursor(StandardCursorType.Cross),
            EditorMode.Delete    => new Cursor(StandardCursorType.No),
            _                    => new Cursor(StandardCursorType.Arrow)
        };
        InvalidateVisual();
    }

    public void AddStateAt(Point p, string name)
    {
        var s = new FsmState { Name = name, X = p.X, Y = p.Y };
        if (States.Count == 0) s.IsInitial = true;
        States.Add(s);
        InvalidateVisual();
    }

    public void AddTransition(FsmState from, FsmState to, string symbol)
    {
        // Allow multiple symbols on same arc - split by comma
        foreach (var sym in symbol.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Transitions.Any(t => t.From == from && t.To == to && t.Symbol == sym))
                Transitions.Add(new FsmTransition { From = from, To = to, Symbol = sym });
        }
        InvalidateVisual();
    }

    public void DeleteState(FsmState s)
    {
        States.Remove(s);
        Transitions.RemoveAll(t => t.From == s || t.To == s);
        if (s.IsInitial && States.Count > 0) States[0].IsInitial = true;
        InvalidateVisual();
    }

    public void DeleteTransition(FsmTransition t)
    {
        Transitions.Remove(t);
        InvalidateVisual();
    }

    public void ToggleAccepting(FsmState s)
    {
        s.IsAccepting = !s.IsAccepting;
        InvalidateVisual();
    }

    public void ToggleInitial(FsmState s)
    {
        foreach (var st in States) st.IsInitial = false;
        s.IsInitial = true;
        InvalidateVisual();
    }

    public void Clear()
    {
        States.Clear();
        Transitions.Clear();
        ResetSim();
        InvalidateVisual();
    }

    // Simulation 

    public bool StartSimulation(string input)
    {
        var init = States.FirstOrDefault(s => s.IsInitial);
        if (init == null) return false;

        SimInput = input;
        SimStep = 0;
        SimulationStatus = SimStatus.Running;
        _simHistory.Clear();
        _simCurrent = init;

        foreach (var s in States) s.IsActive = false;
        foreach (var t in Transitions) t.IsActive = false;

        _simCurrent.IsActive = true;
        _simHistory.Add((_simCurrent, null, -1));

        if (SimInput.Length == 0)
        {
            SimulationStatus = _simCurrent.IsAccepting ? SimStatus.Accepted : SimStatus.Rejected;
        }

        SimulationChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    public bool StepForward()
    {
        if (SimulationStatus != SimStatus.Running) return false;
        if (SimStep >= SimInput.Length) return false;

        char sym = SimInput[SimStep];
        SimStep++;

        var trans = Transitions.FirstOrDefault(t => t.From == _simCurrent && t.Symbol == sym.ToString());

        foreach (var s in States) s.IsActive = false;
        foreach (var t in Transitions) t.IsActive = false;

        if (trans == null)
        {
            SimulationStatus = SimStatus.Rejected;
        }
        else
        {
            trans.IsActive = true;
            _simCurrent = trans.To;
            _simCurrent.IsActive = true;
            _simHistory.Add((_simCurrent, trans, SimStep - 1));

            if (SimStep >= SimInput.Length)
                SimulationStatus = _simCurrent.IsAccepting ? SimStatus.Accepted : SimStatus.Rejected;
        }

        SimulationChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    public bool StepBack()
    {
        if (_simHistory.Count <= 1) return false;
        _simHistory.RemoveAt(_simHistory.Count - 1);
        var (state, _, charIdx) = _simHistory[^1];

        foreach (var s in States) s.IsActive = false;
        foreach (var t in Transitions) t.IsActive = false;

        _simCurrent = state;
        _simCurrent.IsActive = true;
        SimStep = charIdx + 1;
        SimulationStatus = SimStatus.Running;

        SimulationChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    public void ResetSim()
    {
        _simHistory.Clear();
        _simCurrent = null;
        SimStep = -1;
        SimInput = "";
        SimulationStatus = SimStatus.Idle;
        foreach (var s in States) s.IsActive = false;
        foreach (var t in Transitions) t.IsActive = false;
        SimulationChanged?.Invoke();
        InvalidateVisual();
    }

    // Pointer Events 

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // Right click → context actions
        if (props.IsRightButtonPressed)
        {
            var hitState = HitTestState(pt);
            if (hitState != null)
            {
                ShowStateContextMenu(hitState, e);
            }
            else
            {
                var hitTrans = HitTestTransition(pt);
                if (hitTrans != null) DeleteTransition(hitTrans);
            }
            return;
        }

        if (_mode == EditorMode.AddState)
        {
            RequestAddState?.Invoke(pt);
            return;
        }

        var hit = HitTestState(pt);

        if (_mode == EditorMode.Delete)
        {
            if (hit != null) DeleteState(hit);
            else
            {
                var th = HitTestTransition(pt);
                if (th != null) DeleteTransition(th);
            }
            return;
        }

        if (_mode == EditorMode.Connect)
        {
            if (hit != null)
            {
                if (_connectFrom == null)
                {
                    _connectFrom = hit;
                    hit.IsSelected = true;
                    InvalidateVisual();
                }
                else
                {
                    var from = _connectFrom;
                    _connectFrom = null;
                    foreach (var s in States) s.IsSelected = false;
                    RequestAddTransition?.Invoke(from, hit);
                }
            }
            return;
        }

        if (_mode == EditorMode.Select)
        {
            if (hit != null)
            {
                _dragState = hit;
                _dragOffset = new Point(pt.X - hit.X, pt.Y - hit.Y);
                e.Pointer.Capture(this);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mousePos = e.GetPosition(this);

        if (_dragState != null)
        {
            _dragState.X = _mousePos.X - _dragOffset.X;
            _dragState.Y = _mousePos.Y - _dragOffset.Y;
            InvalidateVisual();
        }
        else if (_connectFrom != null)
        {
            InvalidateVisual(); // redraw the rubber-band line
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragState = null;
        e.Pointer.Capture(null);
    }

    private void ShowStateContextMenu(FsmState state, PointerPressedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = state.IsAccepting ? "Remove Accepting" : "Mark Accepting",
                    Command = new RelayCommand(() => { ToggleAccepting(state); }) },
                new MenuItem { Header = state.IsInitial ? "(Already Initial)" : "Set as Initial",
                    IsEnabled = !state.IsInitial,
                    Command = new RelayCommand(() => { ToggleInitial(state); }) },
                new MenuItem { Header = "-" },
                new MenuItem { Header = "Delete State",
                    Command = new RelayCommand(() => { DeleteState(state); }) }
            }
        };
        menu.Open(this);
    }

    // Hit Testing 

    private FsmState? HitTestState(Point p)
    {
        // Iterate in reverse so topmost (last drawn) is hit first
        for (int i = States.Count - 1; i >= 0; i--)
        {
            var s = States[i];
            double dx = p.X - s.X, dy = p.Y - s.Y;
            if (dx * dx + dy * dy <= R * R) return s;
        }
        return null;
    }

    private FsmTransition? HitTestTransition(Point p)
    {
        foreach (var t in Transitions)
        {
            if (IsNearTransition(t, p, 8)) return t;
        }
        return null;
    }

    private bool IsNearTransition(FsmTransition t, Point p, double threshold)
    {
        if (t.From == t.To)
        {
            // self loop: approximate as a small circle above state
            double lx = t.From.X, ly = t.From.Y - R - 28;
            double dx = p.X - lx, dy = p.Y - ly;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            return Math.Abs(dist - 22) < threshold;
        }

        GetTransitionPoints(t, out var src, out var dst, out var ctrl);
        // Sample curve
        for (int i = 0; i <= 20; i++)
        {
            double tt = i / 20.0;
            double bx = (1 - tt) * (1 - tt) * src.X + 2 * (1 - tt) * tt * ctrl.X + tt * tt * dst.X;
            double by = (1 - tt) * (1 - tt) * src.Y + 2 * (1 - tt) * tt * ctrl.Y + tt * tt * dst.Y;
            double dx = p.X - bx, dy = p.Y - by;
            if (dx * dx + dy * dy <= threshold * threshold) return true;
        }
        return false;
    }

    // Rendering 

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);

        // Background
        ctx.FillRectangle(new SolidColorBrush(CBg), bounds);

        // State-Driven Glow Layer
        if (SimulationStatus == SimStatus.Accepted)
        {
            // Green glow, approx 12% opacity over #0D1117 background
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(30, 52, 211, 153)), bounds);
        }
        else if (SimulationStatus == SimStatus.Rejected)
        {
            // Red glow, approx 12% opacity over #0D1117 background
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(30, 248, 113, 113)), bounds);
        }

        // Subtle dot grid
        DrawGrid(ctx, bounds);

        // Transitions
        foreach (var t in Transitions)
        {
            // Group parallel transitions (A↔B)
            bool hasBidi = Transitions.Any(o => o.From == t.To && o.To == t.From);
            DrawTransition(ctx, t, hasBidi);
        }

        // Rubber-band line when connecting
        if (_connectFrom != null && _mode == EditorMode.Connect)
        {
            var src = new Point(_connectFrom.X, _connectFrom.Y);
            var pen = new Pen(new SolidColorBrush(CConnact) { Opacity = 0.6 }, 1.5,
                              new DashStyle(new double[] { 4, 4 }, 0));
            ctx.DrawLine(pen, src, _mousePos);
        }

        // States (drawn over transitions)
        foreach (var s in States)
            DrawState(ctx, s);
    }

    private void DrawGrid(DrawingContext ctx, Rect bounds)
    {
        var dotBrush = new SolidColorBrush(CGrid);
        const double spacing = 40;
        for (double x = 0; x < bounds.Width; x += spacing)
            for (double y = 0; y < bounds.Height; y += spacing)
                ctx.FillRectangle(dotBrush, new Rect(x - 1, y - 1, 2, 2));
    }

    private void DrawState(DrawingContext ctx, FsmState s)
    {
        var center = new Point(s.X, s.Y);

        // Determine color scheme
        Color fill, stroke;
        double strokeWidth = 2;

        if (s.IsActive && SimulationStatus == SimStatus.Accepted)
        { fill = CAccept; stroke = CAccstroke; strokeWidth = 2.5; }
        else if (s.IsActive && SimulationStatus == SimStatus.Rejected)
        { fill = CReject; stroke = CRejstroke; strokeWidth = 2.5; }
        else if (s.IsActive)
        { fill = CActive; stroke = CAstroke; strokeWidth = 2.5; }
        else if (s.IsSelected)
        { fill = CState; stroke = CSel; strokeWidth = 2.5; }
        else
        { fill = CState; stroke = CStroke; }

        var fillBrush   = new SolidColorBrush(fill);
        var strokeBrush = new SolidColorBrush(stroke);
        var pen         = new Pen(strokeBrush, strokeWidth);

        // Glow for active state
        if (s.IsActive || s.IsSelected)
        {
            ctx.DrawEllipse(new SolidColorBrush(stroke) { Opacity = 0.12 },
                            null, center, R + 8, R + 8);
        }

        // Main circle
        ctx.DrawEllipse(fillBrush, pen, center, R, R);

        // Accepting state: inner ring
        if (s.IsAccepting)
        {
            var innerPen = new Pen(strokeBrush, 1.5);
            ctx.DrawEllipse(null, innerPen, center, R - 5, R - 5);
        }

        // Initial state: incoming arrow from left
        if (s.IsInitial)
        {
            var arrowBrush = new SolidColorBrush(CInit);
            var arrowPen = new Pen(arrowBrush, 2);
            var arrowStart = new Point(s.X - R - 28, s.Y);
            var arrowTip   = new Point(s.X - R, s.Y);
            ctx.DrawLine(arrowPen, arrowStart, arrowTip);
            DrawArrowhead(ctx, arrowBrush, arrowStart, arrowTip);
        }

        // Label
        var ft = new FormattedText(
            s.Name, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, TfState, 13, new SolidColorBrush(CText));
        ctx.DrawText(ft, new Point(s.X - ft.Width / 2, s.Y - ft.Height / 2));
    }

    private void DrawTransition(DrawingContext ctx, FsmTransition t, bool bidi)
    {
        var isActive = t.IsActive;
        var lineBrush = new SolidColorBrush(isActive ? CConnact : CConn);
        var labelBrush = new SolidColorBrush(isActive ? CConnact : CLabel);
        var pen = new Pen(lineBrush, isActive ? 2 : 1.5);

        if (t.From == t.To)
        {
            DrawSelfLoop(ctx, t, lineBrush, labelBrush, pen);
            return;
        }

        GetTransitionPoints(t, out var src, out var dst, out var ctrl);

        // Bezier path
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = src, IsClosed = false };
        fig.Segments!.Add(new QuadraticBezierSegment { Point1 = ctrl, Point2 = dst });
        geo.Figures!.Add(fig);
        ctx.DrawGeometry(null, pen, geo);

        DrawArrowhead(ctx, lineBrush, ctrl, dst);

        // Label at midpoint of bezier
        double mx = 0.25 * src.X + 0.5 * ctrl.X + 0.25 * dst.X;
        double my = 0.25 * src.Y + 0.5 * ctrl.Y + 0.25 * dst.Y;

        // Offset label perpendicularly away from line
        double dx = dst.X - src.X, dy = dst.Y - src.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 0)
        {
            double perpX = -dy / len, perpY = dx / len;
            mx += perpX * 14;
            my += perpY * 14;
        }

        DrawLabel(ctx, t.Symbol, new Point(mx, my), labelBrush, isActive);
    }

    private void DrawSelfLoop(DrawingContext ctx, FsmTransition t, IBrush lineBrush, IBrush labelBrush, Pen pen)
    {
        double cx = t.From.X, cy = t.From.Y;
        double loopR = 24;
        double loopCy = cy - R - loopR;

        // Circle loop above state
        ctx.DrawEllipse(null, pen, new Point(cx, loopCy), loopR, loopR);

        // Arrowhead where the loop meets the state
        var tipAngle = -Math.PI / 2 + 0.6;
        var fromAngle = tipAngle - 0.4;
        var tip  = new Point(cx + loopR * Math.Cos(tipAngle - Math.PI), cy - R + 2);
        var from = new Point(cx + loopR * Math.Cos(fromAngle - Math.PI), loopCy + loopR * Math.Sin(fromAngle));
        DrawArrowhead(ctx, lineBrush, from, tip);

        // Label above loop
        DrawLabel(ctx, t.Symbol, new Point(cx, loopCy - loopR - 8), labelBrush, t.IsActive);
    }

    private void GetTransitionPoints(FsmTransition t, out Point src, out Point dst, out Point ctrl)
    {
        bool bidi = Transitions.Any(o => o.From == t.To && o.To == t.From);
        double angle = Math.Atan2(t.To.Y - t.From.Y, t.To.X - t.From.X);

        double offset = bidi ? 0.35 : 0.0; // curve offset for bidirectional

        // Source edge point
        double srcAngle = angle + offset;
        src = new Point(t.From.X + R * Math.Cos(srcAngle), t.From.Y + R * Math.Sin(srcAngle));

        // Dest edge point  
        double dstAngle = angle + Math.PI - offset;
        dst = new Point(t.To.X + R * Math.Cos(dstAngle), t.To.Y + R * Math.Sin(dstAngle));

        // Control point: perpendicular offset from midpoint
        double mx = (src.X + dst.X) / 2;
        double my = (src.Y + dst.Y) / 2;
        double perpX = -Math.Sin(angle);
        double perpY =  Math.Cos(angle);
        double curvature = bidi ? 40 : 0;
        ctrl = new Point(mx + perpX * curvature, my + perpY * curvature);
    }

    private void DrawArrowhead(DrawingContext ctx, IBrush brush, Point from, Point tip)
    {
        double angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
        var p1 = new Point(tip.X - Arrow * Math.Cos(angle - ArrowAngle),
                           tip.Y - Arrow * Math.Sin(angle - ArrowAngle));
        var p2 = new Point(tip.X - Arrow * Math.Cos(angle + ArrowAngle),
                           tip.Y - Arrow * Math.Sin(angle + ArrowAngle));

        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = tip, IsClosed = true };
        fig.Segments!.Add(new LineSegment { Point = p1 });
        fig.Segments!.Add(new LineSegment { Point = p2 });
        geo.Figures!.Add(fig);
        ctx.DrawGeometry(brush, null, geo);
    }

    private void DrawLabel(DrawingContext ctx, string text, Point center, IBrush brush, bool active)
    {
        if (string.IsNullOrEmpty(text)) return;

        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, TfLabel, 12, brush);

        double px = center.X - ft.Width / 2;
        double py = center.Y - ft.Height / 2;

        // Small background pill
        var bgColor = active ? Color.FromArgb(200, 26, 58, 106) : Color.FromArgb(200, 19, 25, 43);
        ctx.FillRectangle(new SolidColorBrush(bgColor),
                          new Rect(px - 4, py - 2, ft.Width + 8, ft.Height + 4),
                          4);
        ctx.DrawText(ft, new Point(px, py));
    }
}

// helper command for context menus
public class RelayCommand(Action execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } } // Suppressed warning
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => execute();
}