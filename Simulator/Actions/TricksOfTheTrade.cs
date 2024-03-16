namespace Craftimizer.Simulator.Actions;

internal sealed class TricksOfTheTrade() : BaseAction(
    ActionCategory.Other, 13, 100371,
    durabilityCost: 0,
    defaultCPCost: 0
    )
{
    public override bool CouldUse(Simulator s) =>
        (s.Condition == Condition.Good || s.Condition == Condition.Excellent || s.HasEffect(EffectType.HeartAndSoul))
        && base.CouldUse(s);

    public override void UseSuccess(Simulator s)
    {
        s.RestoreCP(20);
        if (s.Condition != Condition.Good && s.Condition != Condition.Excellent)
            s.RemoveEffect(EffectType.HeartAndSoul);
    }
}
