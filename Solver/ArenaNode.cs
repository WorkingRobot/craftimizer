using System.Runtime.CompilerServices;

namespace Craftimizer.Solver;

public sealed class ArenaNode<T>(in T state, ArenaNode<T>? parent = null) where T : struct
{
    public T State = state;
    public ArenaBuffer<T> Children;
    public NodeScoresBuffer ChildScores;
    public (int arrayIdx, int subIdx) ChildIdx;
    public readonly ArenaNode<T>? Parent = parent;

    public NodeScoresBuffer? ParentScores => Parent?.ChildScores;

    public ArenaNode<T>? ChildAt((int arrayIdx, int subIdx) at) =>
        Children.Data?[at.arrayIdx]?[at.subIdx];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArenaNode<T> Add(in T state)
    {
        var node = new ArenaNode<T>(in state, this);
        ChildScores.Add();
        Children.Add(node);
        return node;
    }
}
