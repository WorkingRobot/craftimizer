using Craftimizer.Simulator;
using Craftimizer.Simulator.Actions;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Craftimizer.Solver;

internal sealed class Simulator : SimulatorNoRandom
{
    private readonly int maxStepCount;

    public override CompletionState CompletionState
    {
        get
        {
            var b = base.CompletionState;
            if (b == CompletionState.Incomplete && (ActionCount + 1) >= maxStepCount)
                return CompletionState.MaxActionCountReached;
            return b;
        }
    }

    public Simulator(int maxStepCount)
    {
        this.maxStepCount = maxStepCount;
    }

    // https://github.com/alostsock/crafty/blob/cffbd0cad8bab3cef9f52a3e3d5da4f5e3781842/crafty/src/craft_state.rs#L146
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // It's just a bunch of if statements, I would assume this is actually quite simple to follow
#pragma warning disable MA0051 // Method is too long
    private bool CanUseAction(ActionType action, bool strict)
#pragma warning restore MA0051 // Method is too long
    {
        var baseAction = action.Base();

        if (CalculateSuccessRate(baseAction.SuccessRate(this)) != 1)
            return false;

        // don't allow quality moves at max quality
        if (Quality >= Input.Recipe.MaxQuality && baseAction.IncreasesQuality)
            return false;

        if (strict)
        {
            // always use Trained Eye if it's available
            if (action == ActionType.TrainedEye)
                return baseAction.CanUse(this);

            // don't allow quality moves under Muscle Memory for difficult crafts
            if (Input.Recipe.ClassJobLevel == 90 &&
                HasEffect(EffectType.MuscleMemory) &&
                baseAction.IncreasesQuality)
                return false;

            // use First Turn actions if it's available and the craft is difficult
            if (IsFirstStep &&
                Input.Recipe.ClassJobLevel == 90 &&
                baseAction.Category != ActionCategory.FirstTurn &&
                CP > 10)
                return false;

            // don't allow combo actions if the combo is already in progress
            if (ActionStates.TouchComboIdx != 0 &&
                (action == ActionType.StandardTouchCombo || action == ActionType.AdvancedTouchCombo))
                return false;

            // don't allow pure quality moves under Veneration
            if (HasEffect(EffectType.Veneration) &&
                !baseAction.IncreasesProgress &&
                baseAction.IncreasesQuality)
                return false;

            // don't allow pure quality moves when it won't be able to finish the craft
            if (baseAction.IncreasesQuality &&
                CalculateDurabilityCost(baseAction.DurabilityCost) > Durability)
                return false;

            if (baseAction.IncreasesProgress)
            {
                var progressIncrease = CalculateProgressGain(baseAction.Efficiency(this));
                var wouldFinish = Progress + progressIncrease >= Input.Recipe.MaxProgress;

                if (wouldFinish)
                {
                    // don't allow finishing the craft if there is significant quality remaining
                    if (Quality < Input.Recipe.MaxQuality / 5)
                        return false;
                }
                else
                {
                    // don't allow pure progress moves under Innovation, if it wouldn't finish the craft
                    if (HasEffect(EffectType.Innovation) &&
                        !baseAction.IncreasesQuality &&
                        baseAction.IncreasesProgress)
                        return false;
                }
            }

            if (action == ActionType.ByregotsBlessing &&
                GetEffectStrength(EffectType.InnerQuiet) <= 1)
                return false;

            if ((action == ActionType.WasteNot || action == ActionType.WasteNot2) &&
                (HasEffect(EffectType.WasteNot) || HasEffect(EffectType.WasteNot2)))
                return false;

            if (action == ActionType.MastersMend &&
                Input.Recipe.MaxDurability - Durability < 25)
                return false;

            if (action == ActionType.Manipulation &&
                HasEffect(EffectType.Manipulation))
                return false;

            if (action == ActionType.GreatStrides &&
                HasEffect(EffectType.GreatStrides))
                return false;

            if ((action == ActionType.Veneration || action == ActionType.Innovation) &&
                (GetEffectDuration(EffectType.Veneration) > 1 || GetEffectDuration(EffectType.Innovation) > 1))
                return false;
        }

        return baseAction.CanUse(this);
    }

    // https://github.com/alostsock/crafty/blob/cffbd0cad8bab3cef9f52a3e3d5da4f5e3781842/crafty/src/craft_state.rs#L137
    public ActionSet AvailableActionsHeuristic(bool strict)
    {
        if (IsComplete)
            return new();

        var ret = new ActionSet();
        foreach (var action in ActionSet.AcceptedActions)
            if (CanUseAction(action, strict))
                ret.AddAction(action);
        return ret;
    }

}
