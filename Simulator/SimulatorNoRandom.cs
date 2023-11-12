namespace Craftimizer.Simulator;

public class SimulatorNoRandom : Simulator
{
    public SimulatorNoRandom(in SimulationState state) : base(state)
    {
    }

    public sealed override bool RollSuccessRaw(float successRate) => successRate == 1;
    public sealed override Condition GetNextRandomCondition() => Condition.Normal;
}
