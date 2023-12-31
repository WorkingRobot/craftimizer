namespace Craftimizer.Simulator.Actions;

internal sealed class WasteNot : BaseBuffAction
{
    public override ActionCategory Category => ActionCategory.Durability;
    public override int Level => 15;
    public override uint ActionId => 4631;

    public override EffectType Effect => EffectType.WasteNot;
    public override byte Duration => 4;

    public override int CPCost(Simulator s) => 56;

    public override void UseSuccess(Simulator s)
    {
        base.UseSuccess(s);
        s.RemoveEffect(EffectType.WasteNot2);
    }
}
