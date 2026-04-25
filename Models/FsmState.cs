using System;

namespace Dasam.Models;

public class FsmState
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "q";
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsInitial { get; set; }
    public bool IsAccepting { get; set; }

    // Visual-only flags (not part of FSM definition)
    public bool IsActive { get; set; }    
    public bool IsSelected { get; set; }
}
