using System;

namespace Dasam.Models;

public class FsmTransition
{
    public Guid Id { get; } = Guid.NewGuid();
    public FsmState From { get; set; } = null!;
    public FsmState To { get; set; } = null!;
    public string Symbol { get; set; } = "";
    public bool IsActive { get; set; }
}
