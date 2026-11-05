using AlienCat.Core.Pet;

namespace AlienCat.Core.Tests.Pet;

/// <summary>Returns queued values, then a fixed fallback.</summary>
public sealed class ScriptedRandom(params double[] values) : IRandom
{
    readonly Queue<double> _values = new(values);
    public double Fallback { get; set; } = 0.5;
    public void Push(params double[] v) { foreach (var x in v) _values.Enqueue(x); }
    public double NextDouble() => _values.Count > 0 ? _values.Dequeue() : Fallback;
}
