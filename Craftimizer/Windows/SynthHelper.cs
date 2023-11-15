using Craftimizer.Plugin;
using Craftimizer.Plugin.Utils;
using Craftimizer.Simulator;
using Craftimizer.Simulator.Actions;
using Craftimizer.Utils;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ActionType = Craftimizer.Simulator.Actions.ActionType;
using Sim = Craftimizer.Simulator.Simulator;
using SimNoRandom = Craftimizer.Simulator.SimulatorNoRandom;

namespace Craftimizer.Windows;

public sealed unsafe class SynthHelper : Window, IDisposable
{
    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoDecoration
      | ImGuiWindowFlags.AlwaysAutoResize
      | ImGuiWindowFlags.NoSavedSettings
      | ImGuiWindowFlags.NoFocusOnAppearing
      | ImGuiWindowFlags.NoNavFocus;

    public AddonSynthesis* Addon { get; private set; }
    public RecipeData? RecipeData { get; private set; }
    public CharacterStats? CharacterStats { get; private set; }
    public SimulationInput? SimulationInput { get; private set; }

    public bool IsCrafting { get; private set; }
    private int CurrentActionCount { get; set; }
    private ActionStates CurrentActionStates { get; set; }
    private SimulationState CurrentState
    {
        get => currentState;
        set
        {
            if (currentState != value)
            {
                currentState = value;
                OnStateUpdated();
            }
        }
    }
    private SimulationState currentState;
    private SimulatedMacro Macro { get; } = new();

    private CancellationTokenSource? HelperTaskTokenSource { get; set; }
    private Exception? HelperTaskException { get; set; }
    private Solver.Solver? HelperTaskObject { get; set; }
    private bool HelperTaskRunning => HelperTaskTokenSource != null;

    private GameFontHandle AxisFont { get; }

    public SynthHelper() : base("Craftimizer SynthHelper", WindowFlags)
    {
        AxisFont = Service.PluginInterface.UiBuilder.GetGameFontHandle(new(GameFontFamilyAndSize.Axis14));

        Service.Plugin.Hooks.OnActionUsed += OnUseAction;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ShowCloseButton = false;
        IsOpen = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(494, -1),
            MaximumSize = new(494, 10000)
        };

        Service.WindowSystem.AddWindow(this);
    }

    private bool wasInCraftAction;
    public override void Update()
    {
        Addon = (AddonSynthesis*)Service.GameGui.GetAddonByName("Synthesis");

        if (Addon != null)
        {
            var agent = AgentRecipeNote.Instance();
            var recipeId = (ushort)agent->ActiveCraftRecipeId;

            if (agent->ActiveCraftRecipeId == 0)
                IsCrafting = false;
            else if (!IsCrafting)
            {
                IsCrafting = true;
                OnStartCrafting(recipeId);
            }
        }
        else
            IsCrafting = false;

        Macro.FlushQueue();

        var isInCraftAction = Service.Condition[ConditionFlag.Crafting40];
        if (!isInCraftAction && wasInCraftAction)
            OnFinishedUsingAction();
        wasInCraftAction = isInCraftAction;
    }

    private bool wasOpen;
    public override bool DrawConditions()
    {
        var isOpen = ShouldDraw();
        if (isOpen != wasOpen)
        {
            if (wasOpen)
                HelperTaskTokenSource?.Cancel();
        }

        wasOpen = isOpen;
        return isOpen;
    }

    private bool ShouldDraw()
    {
        if (Service.ClientState.LocalPlayer == null)
            return false;

        if (Addon == null)
            return false;

        if (!IsCrafting)
            return false;

        // Check if Synthesis addon is visible
        if (Addon->AtkUnitBase.WindowNode == null)
            return false;

        return true;
    }

    public override void PreDraw()
    {
        ref var unit = ref Addon->AtkUnitBase;
        var scale = unit.Scale;
        var pos = new Vector2(unit.X, unit.Y);
        var size = new Vector2(unit.WindowNode->AtkResNode.Width, unit.WindowNode->AtkResNode.Height) * scale;

        var offset = 5;

        Position = ImGuiHelpers.MainViewport.Pos + pos + new Vector2(size.X, offset * scale);
    }

    public override void Draw()
    {
        DrawMacro();

        DrawMacroInfo();

        ImGuiHelpers.ScaledDummy(5);

        DrawMacroActions();

        if (HelperTaskRunning && HelperTaskObject is { } solver)
        {
            ImGuiHelpers.ScaledDummy(5);
            DrawHelperTaskProgress(solver);
        }
    }

    private SimulationState? hoveredState;
    private SimulationState DisplayedState => hoveredState ?? Macro.State;
    private void DrawMacro()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var imageSize = ImGui.GetFrameHeight() * 2;
        var lastState = Macro.InitialState;
        hoveredState = null;

        var itemsPerRow = (int)Math.Max(1, MathF.Floor((ImGui.GetContentRegionAvail().X + spacing) / (imageSize + spacing)));

        using var _color = ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero);
        using var _color3 = ImRaii.PushColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        using var _color2 = ImRaii.PushColor(ImGuiCol.ButtonActive, Vector4.Zero);
        for (var i = 0; i < Macro.Count; i++)
        {
            if (i % itemsPerRow != 0)
                ImGui.SameLine(0, spacing);
            var (action, response, state) = (Macro[i].Action, Macro[i].Response, Macro[i].State);
            var actionBase = action.Base();
            var failedAction = response != ActionResponse.UsedAction;
            using var id = ImRaii.PushId(i);
            if (i == 0)
            {
                var pos = ImGui.GetCursorScreenPos();
                var offset = new Vector2(3);
                ImGui.GetWindowDrawList().AddRectFilled(pos - offset, pos + new Vector2(imageSize) + offset, ImGui.GetColorU32(ImGuiColors.DalamudWhite2), 4);
            }
            if (ImGui.ImageButton(action.GetIcon(RecipeData!.ClassJob).ImGuiHandle, new(imageSize), default, Vector2.One, 0, default, failedAction ? new(1, 1, 1, ImGui.GetStyle().DisabledAlpha) : Vector4.One))
            {
                if (i == 0)
                    Chat.SendMessage($"/ac \"{action.GetName(RecipeData.ClassJob)}\"");
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip($"{action.GetName(RecipeData!.ClassJob)}\n" +
                    $"{actionBase.GetTooltip(CreateSim(lastState), true)}" +
                    $"{(i == 0 ? "Click to Execute" : string.Empty)}");
                hoveredState = state;
            }
            lastState = state;
        }

        var rows = (int)Math.Max(1, MathF.Ceiling(Service.Configuration.SynthHelperStepCount / itemsPerRow));
        for (var i = 0; i < rows; ++i)
        {
            if (Macro.Count <= i * itemsPerRow)
                ImGui.Dummy(new(0, imageSize));
        }
    }

    private void DrawMacroInfo()
    {
        var state = DisplayedState;

        using (var panel = ImRaii2.GroupPanel("Buffs", -1, out _))
        {
            using var _font = ImRaii.PushFont(AxisFont.ImFont);

            var iconHeight = ImGui.GetFrameHeight() * 1.75f;
            var durationShift = iconHeight * .2f;

            ImGui.Dummy(new(0, iconHeight + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeight() - durationShift));
            ImGui.SameLine(0, 0);

            var effects = state.ActiveEffects;
            foreach (var effect in Enum.GetValues<EffectType>())
            {
                if (!effects.HasEffect(effect))
                    continue;

                using (var group = ImRaii.Group())
                {
                    var icon = effect.GetIcon(effects.GetStrength(effect));
                    var size = new Vector2(iconHeight * icon.Width / icon.Height, iconHeight);

                    ImGui.Image(icon.ImGuiHandle, size);
                    if (!effect.IsIndefinite())
                    {
                        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - durationShift);
                        ImGuiUtils.TextCentered($"{effects.GetDuration(effect)}", size.X);
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    var status = effect.Status();
                    using var _reset = ImRaii.DefaultFont();
                    ImGui.SetTooltip($"{status.Name.ToDalamudString()}\n{status.Description.ToDalamudString()}");
                }
                ImGui.SameLine();
            }
        }

        var reliability = Macro.GetReliability(RecipeData!);
        {
            var mainBars = new List<DynamicBars.BarData>()
            {
                new("Progress", Colors.Progress, reliability.Progress, state.Progress, RecipeData!.RecipeInfo.MaxProgress),
                new("Quality", Colors.Quality, reliability.Quality, state.Quality, RecipeData.RecipeInfo.MaxQuality),
                new("CP", Colors.CP, state.CP, CharacterStats!.CP),
            };
            if (RecipeData.RecipeInfo.MaxQuality <= 0)
                mainBars.RemoveAt(1);
            var halfBars = new List<DynamicBars.BarData>()
            {
                new("Durability", Colors.Durability, state.Durability, RecipeData.RecipeInfo.MaxDurability),
            };
            if (RecipeData.Recipe.ItemResult.Value!.IsCollectable)
                halfBars.Add(new("Collectability", Colors.HQ, reliability.ParamScore, state.Collectability, state.MaxCollectability, $"{state.Collectability}", null));
            else if (RecipeData.Recipe.RequiredQuality > 0)
            {
                var qualityPercent = (float)state.Quality / RecipeData.Recipe.RequiredQuality * 100;
                halfBars.Add(new("Quality %%", Colors.HQ, reliability.ParamScore, qualityPercent, 100, $"{qualityPercent:0}%", null));
            }
            else if (RecipeData.RecipeInfo.MaxQuality > 0)
                halfBars.Add(new("HQ %%", Colors.HQ, reliability.ParamScore, state.HQPercent, 100, $"{state.HQPercent}%", null));

            if (halfBars.Count > 1)
            {
                var textSize = DynamicBars.GetTextSize(mainBars.Concat(halfBars));
                DynamicBars.Draw(mainBars, textSize);
                using var table = ImRaii.Table($"##{nameof(SynthHelper)}_halfbars", halfBars.Count, ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchSame);
                if (table)
                {
                    foreach (var bar in halfBars)
                    {
                        ImGui.TableNextColumn();
                        DynamicBars.Draw(new[] { bar });
                    }
                }
            }
            else
            {
                DynamicBars.Draw(mainBars.Concat(halfBars));
            }
        }
    }

    private void DrawHelperTaskProgress(Solver.Solver solver)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var availSpace = ImGui.GetContentRegionAvail().X;

        var percentWidth = ImGui.CalcTextSize("100%").X;
        var progressWidth = availSpace - percentWidth - spacing;
        var fraction = Math.Clamp((float)solver.ProgressValue / solver.ProgressMax, 0, 1);
        using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, ImGuiColors.DalamudGrey3))
            ImGui.ProgressBar(fraction, new(progressWidth, ImGui.GetFrameHeight()), string.Empty);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Solver Progress: {solver.ProgressValue} / {solver.ProgressMax}");
        ImGui.SameLine(0, spacing);
        ImGui.AlignTextToFramePadding();
        ImGuiUtils.TextRight($"{fraction * 100:0}%", percentWidth);
    }

    private void DrawMacroActions()
    {
        if (HelperTaskRunning)
        {
            if (HelperTaskTokenSource?.IsCancellationRequested ?? false)
            {
                using var _disabled = ImRaii.Disabled();
                ImGui.Button("Stopping", new(-1, 0));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("This might could a while, sorry! Please report\n" +
                                     "if this takes longer than a second.");
            }
            else
            {
                if (ImGui.Button("Stop", new(-1, 0)))
                    HelperTaskTokenSource?.Cancel();
            }
        }
        else
        {
            if (ImGui.Button("Retry", new(-1, 0)))
                CalculateBestMacro();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Suggest a way to finish the crafting recipe.\n" +
                                 "Results aren't perfect, and levels of success\n" +
                                 "can vary wildly depending on the solver's settings.");
        }

        if (ImGui.Button("Open in Simulator", new(-1, 0)))
            Service.Plugin.OpenMacroEditor(CharacterStats!, RecipeData!, new(Service.ClientState.LocalPlayer!.StatusList), Enumerable.Empty<ActionType>(), null);
    }

    private void OnStartCrafting(ushort recipeId)
    {
        var shouldUpdateInput = false;
        if (recipeId != RecipeData?.RecipeId)
        {
            RecipeData = new(recipeId);
            shouldUpdateInput = true;
        }

        {
            var gearStats = Gearsets.CalculateGearsetCurrentStats();

            var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
            if (container == null)
                throw new InvalidOperationException("Could not get inventory container");

            var gearItems = Gearsets.GetGearsetItems(container);

            var characterStats = Gearsets.CalculateCharacterStats(gearStats, gearItems, RecipeData.ClassJob.GetPlayerLevel(), RecipeData.ClassJob.CanPlayerUseManipulation());
            if (characterStats != CharacterStats)
            {
                CharacterStats = characterStats;
                shouldUpdateInput = true;
            }
        }

        if (shouldUpdateInput)
            SimulationInput = new(CharacterStats, RecipeData.RecipeInfo);

        CurrentActionCount = 0;
        CurrentActionStates = new();
        CurrentState = GetCurrentState();
    }

    private void OnUseAction(ActionType action)
    {
        if (!IsCrafting)
            return;

        (_, CurrentState) = new SimNoRandom().Execute(GetCurrentState(), action);
        CurrentActionCount = CurrentState.ActionCount;
        CurrentActionStates = CurrentState.ActionStates;
    }

    private void OnFinishedUsingAction()
    {
        if (!IsCrafting)
            return;

        CurrentState = GetCurrentState();
    }

    private SimulationState GetCurrentState()
    {
        var player = Service.ClientState.LocalPlayer!;
        var values = new SynthesisValues(Addon);
        var statusManager = ((Character*)player.Address)->GetStatusManager();

        byte GetEffectStack(ushort id)
        {
            foreach (var status in statusManager->StatusSpan)
                if (status.StatusID == id)
                    return status.StackCount;
            return 0;
        }
        bool HasEffect(ushort id)
        {
            foreach (var status in statusManager->StatusSpan)
                if (status.StatusID == id)
                    return true;
            return false;
        }

        return new(SimulationInput!)
        {
            ActionCount = CurrentActionCount,
            StepCount = (int)values.StepCount - 1,
            Progress = (int)values.Progress,
            Quality = (int)values.Quality,
            Durability = (int)values.Durability,
            CP = (int)player.CurrentCp,
            Condition = values.Condition,
            ActiveEffects = new()
            {
                InnerQuiet = GetEffectStack((ushort)EffectType.InnerQuiet.StatusId()),
                WasteNot = GetEffectStack((ushort)EffectType.WasteNot.StatusId()),
                Veneration = GetEffectStack((ushort)EffectType.Veneration.StatusId()),
                GreatStrides = GetEffectStack((ushort)EffectType.GreatStrides.StatusId()),
                Innovation = GetEffectStack((ushort)EffectType.Innovation.StatusId()),
                FinalAppraisal = GetEffectStack((ushort)EffectType.FinalAppraisal.StatusId()),
                WasteNot2 = GetEffectStack((ushort)EffectType.WasteNot2.StatusId()),
                MuscleMemory = GetEffectStack((ushort)EffectType.MuscleMemory.StatusId()),
                Manipulation = GetEffectStack((ushort)EffectType.Manipulation.StatusId()),
                HeartAndSoul = HasEffect((ushort)EffectType.HeartAndSoul.StatusId()),
            },
            ActionStates = CurrentActionStates
        };
    }

    private void OnStateUpdated()
    {
        if (!IsCrafting)
            return;

        Macro.Clear();
        Macro.InitialState = CurrentState;
        CalculateBestMacro();
    }

    private void CalculateBestMacro()
    {
        HelperTaskTokenSource?.Cancel();
        HelperTaskTokenSource = new();
        HelperTaskException = null;
        Macro.ClearQueue();
        Macro.Clear();

        if (Service.Configuration.ConditionRandomness)
        {
            Service.Configuration.ConditionRandomness = false;
            Service.Configuration.Save();
            Macro.RecalculateState();
        }

        var token = HelperTaskTokenSource.Token;
        var state = CurrentState;
        var task = Task.Run(() => CalculateBestMacroTask(state, token), token);
        _ = task.ContinueWith(t =>
        {
            if (token == HelperTaskTokenSource.Token)
            {
                HelperTaskTokenSource = null;
                HelperTaskObject = null;
            }
        });
        _ = task.ContinueWith(t =>
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                t.Exception!.Flatten().Handle(ex => ex is TaskCanceledException or OperationCanceledException);
            }
            catch (AggregateException e)
            {
                HelperTaskException = e;
                Log.Error(e, "Calculating macro failed");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void CalculateBestMacroTask(SimulationState state, CancellationToken token)
    {
        var config = Service.Configuration.SimulatorSolverConfig;

        token.ThrowIfCancellationRequested();

        using (HelperTaskObject = new Solver.Solver(config, state) { Token = token })
        {
            HelperTaskObject.OnLog += Log.Debug;
            HelperTaskObject.OnNewAction += EnqueueAction;
            HelperTaskObject.Start();
            _ = HelperTaskObject.GetTask().GetAwaiter().GetResult();
        }

        token.ThrowIfCancellationRequested();
    }

    private void EnqueueAction(ActionType action)
    {
        if (Macro.Enqueue(action) >= Service.Configuration.SynthHelperStepCount)
            HelperTaskTokenSource?.Cancel();
    }

    private static Sim CreateSim(in SimulationState state) =>
        Service.Configuration.ConditionRandomness ? new Sim() { State = state } : new SimNoRandom() { State = state };

    public void Dispose()
    {
        Service.Plugin.Hooks.OnActionUsed -= OnUseAction;

        Service.WindowSystem.RemoveWindow(this);

        AxisFont.Dispose();
    }
}
