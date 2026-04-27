using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dasam.Models;

namespace Dasam;

public class FsmDto
{
    public List<StateDto> States { get; set; } = [];
    public List<TransitionDto> Transitions { get; set; } = [];
}

public class StateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsInitial { get; set; }
    public bool IsAccepting { get; set; }
}

public class TransitionDto
{
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public string Symbol { get; set; } = string.Empty;
}

public static class FsmSerializer
{
    // FIX: Replaced 'Transitions?' with 'IEnumerable<FsmTransition>'
    public static string Serialize(IEnumerable<FsmState> states, IEnumerable<FsmTransition> transitions)
    {
        var dto = new FsmDto
        {
            States = states.Select(s => new StateDto
            {
                Id = s.Id, Name = s.Name, X = s.X, Y = s.Y,
                IsInitial = s.IsInitial, IsAccepting = s.IsAccepting
            }).ToList(),
            
            Transitions = transitions.Select(t => new TransitionDto
            {
                FromId = t.From.Id, ToId = t.To.Id, Symbol = t.Symbol
            }).ToList()
        };
        
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void Deserialize(string json, FsmCanvas canvas)
    {
        var dto = JsonSerializer.Deserialize<FsmDto>(json);
        if (dto == null) return;

        canvas.Clear();
        var stateMap = new Dictionary<Guid, FsmState>();

        foreach (var s in dto.States)
        {
            var state = new FsmState { Name = s.Name, X = s.X, Y = s.Y, IsInitial = s.IsInitial, IsAccepting = s.IsAccepting };
            canvas.States.Add(state);
            stateMap[s.Id] = state; 
        }

        foreach (var t in dto.Transitions)
        {
            if (stateMap.TryGetValue(t.FromId, out var from) && stateMap.TryGetValue(t.ToId, out var to))
            {
                canvas.AddTransition(from, to, t.Symbol);
            }
        }
        canvas.InvalidateVisual();
    }
}