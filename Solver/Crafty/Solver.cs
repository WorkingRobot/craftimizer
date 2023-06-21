using Craftimizer.Simulator;
using Craftimizer.Simulator.Actions;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Craftimizer.Solver.Crafty;

// https://github.com/alostsock/crafty/blob/cffbd0cad8bab3cef9f52a3e3d5da4f5e3781842/crafty/src/simulator.rs
public class Solver
{
    public Simulator Simulator;
    public Arena<SimulationNode> Tree;

    //public Random Random => Simulator.Input.Random;

    public const int Iterations = 30000;
    public const float ScoreStorageThreshold = 1f;
    public const float MaxScoreWeightingConstant = 0.1f;
    public const float ExplorationConstant = 4f;
    public const int MaxStepCount = 25;

    public Solver(SimulationState state, bool strict)
    {
        Simulator = new(state);
        Tree = new(new()
        {
            State = state,
            Action = null,
            SimulationCompletionState = Simulator.CompletionState,
            Data = new() { AvailableActions = Simulator.AvailableActionsHeuristic(strict) }
        });
    }

    public Solver(SimulationInput input) : this(new(input), false)
    {
    }

    private SimulationNode Execute(SimulationState state, ActionType action, bool strict)
    {
        (_, var newState) = Simulator.Execute(state, action);
        return new()
        {
            State = newState,
            Action = action,
            SimulationCompletionState = Simulator.CompletionState,
            Data = new() { AvailableActions = Simulator.AvailableActionsHeuristic(strict) }
        };
    }

    public (int Index, CompletionState State) ExecuteActions(int startIndex, List<ActionType> actions, bool strict = false)
    {
        var currentIndex = startIndex;
        foreach (var action in actions)
        {
            var node = Tree.Get(currentIndex).State;
            if (node.IsComplete)
                return (currentIndex, node.CompletionState);

            if (!node.Data.AvailableActions.HasAction(action))
                return (currentIndex, CompletionState.InvalidAction);
            node.Data.AvailableActions.RemoveAction(action);

            currentIndex = Tree.Insert(currentIndex, Execute(node.State, action, strict));
        }

        var currentNode = Tree.Get(currentIndex).State;
        return (currentIndex, currentNode.CompletionState);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RustMaxBy(List<int> source, Func<int, float> into)
    {
        var max = 0;
        var maxV = into(source[0]);
        for (var i = 1; i < source.Count; ++i)
        {
            var nextV = into(source[i]);
            if (maxV <= nextV)
            {
                max = i;
                maxV = nextV;
            }
        }
        return source[max];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> EvalBestChildVectorized(float w, float W, Vector<float> C, Vector<float> scoreSums, Vector<float> visits, Vector<float> maxScores)
    {
        var exploitation = W * (scoreSums / visits) + w * maxScores;
        var exploration = Vector.SquareRoot(C / visits);
        return exploitation + exploration;
    }

    private static int AlignToVectorLength(int length) =>
        (length + (Vector<float>.Count - 1)) & ~(Vector<float>.Count - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EvalBestChild(float parentVisits, List<int> children)
    {
        var length = children.Count;

        var C = ExplorationConstant * MathF.Log(parentVisits);
        var w = MaxScoreWeightingConstant;
        var W = 1f - w;
        var CVector = new Vector<float>(C);

        Span<float> scoreSums = stackalloc float[Vector<float>.Count];
        Span<float> visits = stackalloc float[Vector<float>.Count];
        Span<float> maxScores = stackalloc float[Vector<float>.Count];

        var max = 0;
        var maxScore = 0f;
        for (var i = 0; i < length; i += Vector<float>.Count)
        {
            var iterCount = i + Vector<float>.Count > length ?
                length - i :
                Vector<float>.Count;

            for (var j = 0; j < iterCount; ++j)
            {
                var node = Tree.Get(children[i + j]).State.Data.Scores;
                scoreSums[j] = node.ScoreSum;
                visits[j] = node.Visits;
                maxScores[j] = node.MaxScore;
            }
            var evalScores = EvalBestChildVectorized(w, W, CVector, new(scoreSums), new(visits), new(maxScores));

            for (var j = 0; j < iterCount; ++j)
            {
                if (evalScores[j] >= maxScore)
                {
                    max = i + j;
                    maxScore = evalScores[j];
                }
            }
        }

        return children[max];
    }

    public int Select(int selectedIndex)
    {
        while (true)
        {
            var selectedNode = Tree.Get(selectedIndex);

            var expandable = selectedNode.State.Data.AvailableActions.Count != 0;
            var likelyTerminal = selectedNode.Children.Count == 0;
            if (expandable || likelyTerminal)
            {
                return selectedIndex;
            }

            // select the node with the highest score
            selectedIndex = EvalBestChild(selectedNode.State.Data.Scores.Visits, selectedNode.Children);
        }
    }

    public (int Index, CompletionState State, float Score) ExpandAndRollout(int initialIndex)
    {
        // expand once
        var initialNode = Tree.Get(initialIndex).State;
        if (initialNode.IsComplete)
            return (initialIndex, initialNode.CompletionState, initialNode.CalculateScore() ?? 0);

        var randomAction = initialNode.Data.AvailableActions.ElementAt(0);
        initialNode.Data.AvailableActions.RemoveAction(randomAction);
        var expandedState = Execute(initialNode.State, randomAction, true);
        var expandedIndex = Tree.Insert(initialIndex, expandedState);

        // playout to a terminal state
        var currentState = Tree.Get(expandedIndex).State;
        var actions = new List<ActionType>();
        while (true)
        {
            if (currentState.IsComplete)
                break;
            randomAction = currentState.Data.AvailableActions.ElementAt(0);
            actions.Add(randomAction);
            currentState = Execute(currentState.State, randomAction, true);
        }

        // store the result if a max score was reached
        var score = currentState.CalculateScore() ?? 0;
        if (currentState.CompletionState == CompletionState.ProgressComplete)
        {
            if (score >= ScoreStorageThreshold && score >= Tree.Get(0).State.Data.Scores.MaxScore)
            {
                (var terminalIndex, _) = ExecuteActions(expandedIndex, actions, true);
                return (terminalIndex, currentState.CompletionState, score);
            }
        }
        return (expandedIndex, currentState.CompletionState, score);
    }

    public void Backpropagate(int startIndex, int targetIndex, float score)
    {
        var currentIndex = startIndex;
        while (true)
        {
            var currentNode = Tree.Get(currentIndex);
            currentNode.State.Data.Scores.Visit(score);

            if (currentIndex == targetIndex)
                break;

            currentIndex = currentNode.Parent;
        }
    }

    public void Search(int startIndex)
    {
        for (var i = 0; i < Iterations; i++)
        {
            var selectedIndex = Select(startIndex);
            var (endIndex, _, score) = ExpandAndRollout(selectedIndex);

            Backpropagate(endIndex, startIndex, score);
        }
    }

    public (List<ActionType> Actions, SimulationNode Node) Solution()
    {
        var actions = new List<ActionType>();
        var node = Tree.Get(0);
        while (node.Children.Count != 0)
        {
            var next_index = RustMaxBy(node.Children, n => Tree.Get(n).State.Data.Scores.MaxScore);
            node = Tree.Get(next_index);
            if (node.State.Action != null)
                actions.Add(node.State.Action.Value);
        }

        return (actions, node.State);
    }

    public static (SimulationState SimState, CompletionState State) Simulate(SimulationInput input, List<ActionType> actions)
    {
        var solver = new Solver(input);
        var (index, result) = solver.ExecuteActions(0, actions);
        return (solver.Tree.Get(index).State.State, result);
    }

    public static (List<ActionType> Actions, SimulationState State) SearchStepwise(SimulationInput input, List<ActionType> actions, Action<ActionType>? actionCallback)
    {
        var (state, result) = Simulate(input, actions);
        if (result != CompletionState.Incomplete)
        {
            return (actions, state);
        }

        //Debugger.Break();
        var solver = new Solver(state, true);
        while (!solver.Simulator.IsComplete)
        {
            solver.Search(0);
            var (solution_actions, solution_node) = solver.Solution();

            if (solution_node.Data.Scores.MaxScore >= 1.0)
            {
                actions.AddRange(solution_actions);
                return (actions, solution_node.State);
            }

            var chosen_action = solution_actions[0];
            (_, state) = solver.Simulator.Execute(state, chosen_action);
            actions.Add(chosen_action);

            actionCallback?.Invoke(chosen_action);

            solver = new Solver(state, true);
        }
        Debugger.Break();

        return (actions, state);
    }

    public static (List<ActionType> Actions, SimulationState State) SearchOneshot(SimulationInput input, List<ActionType> actions)
    {
        var solver = new Solver(input);
        solver.Search(0);
        var (solution_actions, solution_node) = solver.Solution();
        actions.AddRange(solution_actions);
        return (actions, solution_node.State);
    }
}
