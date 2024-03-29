using Craftimizer.Plugin.Utils;
using Craftimizer.Plugin.Windows;
using Craftimizer.Simulator;
using Craftimizer.Simulator.Actions;
using Craftimizer.Utils;
using Craftimizer.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Craftimizer.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    public string Version { get; }
    public string Author { get; }
    public string BuildConfiguration { get; }
    public IDalamudTextureWrap Icon { get; }

    public WindowSystem WindowSystem { get; }
    public Settings SettingsWindow { get; }
    public RecipeNote RecipeNoteWindow { get; }
    public SynthHelper SynthHelperWindow { get; }
    public MacroList ListWindow { get; private set; }
    public MacroEditor? EditorWindow { get; private set; }
    public MacroClipboard? ClipboardWindow { get; private set; }

    public Configuration Configuration { get; }
    public Hooks Hooks { get; }
    public Chat Chat { get; }
    public IconManager IconManager { get; }
    public CommunityMacros CommunityMacros { get; }

    public Plugin([RequiredVersion("1.0")] DalamudPluginInterface pluginInterface)
    {
        Service.Initialize(this, pluginInterface);

        WindowSystem = new("Craftimizer");
        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new();
        Hooks = new();
        Chat = new();
        IconManager = new();
        CommunityMacros = new();

        var assembly = Assembly.GetExecutingAssembly();
        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;
        BuildConfiguration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var now = DateTime.Now;
        if (now.Day == 1 && now.Month == 4)
            Icon = IconManager.GetAssemblyTexture("horse_icon.png");
        else
            Icon = IconManager.GetAssemblyTexture("icon.png");

        SettingsWindow = new();
        RecipeNoteWindow = new();
        SynthHelperWindow = new();
        ListWindow = new();

        // Trigger static constructors so a huge hitch doesn't occur on first RecipeNote frame.
        FoodStatus.Initialize();
        Gearsets.Initialize();
        ActionUtils.Initialize();

        Service.PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;
        Service.PluginInterface.UiBuilder.OpenMainUi += OpenCraftingLog;

        Service.CommandManager.AddHandler("/craftimizer", new CommandInfo((_, _) => OpenSettingsWindow())
        {
            HelpMessage = "Open the settings window.",
        });
        Service.CommandManager.AddHandler("/craftmacros", new CommandInfo((_, _) => OpenMacroListWindow())
        {
            HelpMessage = "Open the crafting macros window.",
        });
        Service.CommandManager.AddHandler("/crafteditor", new CommandInfo((_, _) => OpenEmptyMacroEditor())
        {
            HelpMessage = "Open the crafting macro editor.",
        });
        Service.CommandManager.AddHandler("/craftaction", new CommandInfo((_, _) => ExecuteSuggestedSynthHelperAction())
        {
            HelpMessage = "Execute the suggested action in the synthesis helper. This command mostly exists for controller players.",
        });
    }

    public (CharacterStats? Character, RecipeData? Recipe, MacroEditor.CrafterBuffs? Buffs) GetOpenedStats()
    {
        var editorWindow = (EditorWindow?.IsOpen ?? false) ? EditorWindow : null;
        var recipeData = editorWindow?.RecipeData ?? Service.Plugin.RecipeNoteWindow.RecipeData;
        var characterStats = editorWindow?.CharacterStats ?? Service.Plugin.RecipeNoteWindow.CharacterStats;
        var buffs = editorWindow?.Buffs ?? (RecipeNoteWindow.CharacterStats != null ? new(Service.ClientState.LocalPlayer?.StatusList) : null);

        return (characterStats, recipeData, buffs);
    }

    public (CharacterStats Character, RecipeData Recipe, MacroEditor.CrafterBuffs Buffs) GetDefaultStats()
    {
        var stats = GetOpenedStats();
        return (
            stats.Character ?? new()
            {
                Craftsmanship = 100,
                Control = 100,
                CP = 200,
                Level = 10,
                CanUseManipulation = false,
                HasSplendorousBuff = false,
                IsSpecialist = false,
                CLvl = 10,
            },
            stats.Recipe ?? new(1023),
            stats.Buffs ?? new(null)
        );
    }

    public void OpenEmptyMacroEditor()
    {
        var stats = GetDefaultStats();
        OpenMacroEditor(stats.Character, stats.Recipe, stats.Buffs, Enumerable.Empty<ActionType>(), null);
    }

    public void OpenMacroEditor(CharacterStats characterStats, RecipeData recipeData, MacroEditor.CrafterBuffs buffs, IEnumerable<ActionType> actions, Action<IEnumerable<ActionType>>? setter)
    {
        EditorWindow?.Dispose();
        EditorWindow = new(characterStats, recipeData, buffs, actions, setter);
    }

    public void ExecuteSuggestedSynthHelperAction() =>
        SynthHelperWindow.QueueSuggestedActionExecution();

    public void OpenSettingsWindow()
    {
        if (SettingsWindow.IsOpen ^= true)
            SettingsWindow.BringToFront();
    }

    public void OpenSettingsTab(string selectedTabLabel)
    {
        OpenSettingsWindow();
        SettingsWindow.SelectTab(selectedTabLabel);
    }

    public void OpenMacroListWindow()
    {
        ListWindow.IsOpen = true;
        ListWindow.BringToFront();
    }

    public void OpenCraftingLog()
    {
        Chat.SendMessage("/craftinglog");
    }

    public void OpenMacroClipboard(List<string> macros)
    {
        ClipboardWindow?.Dispose();
        ClipboardWindow = new(macros);
    }

    public void CopyMacro(IReadOnlyList<ActionType> actions) =>
        MacroCopy.Copy(actions);

    public IActiveNotification DisplayNotification(Notification notification)
    {
        notification.InitialDuration = TimeSpan.FromSeconds(5);
        var ret = Service.NotificationManager.AddNotification(notification);
        if (notification.Icon != null)
            ret.SetIconTexture(Icon);
        return ret;
    }

    public void Dispose()
    {
        Service.CommandManager.RemoveHandler("/craftimizer");
        Service.CommandManager.RemoveHandler("/craftmacros");
        Service.CommandManager.RemoveHandler("/crafteditor");
        Service.CommandManager.RemoveHandler("/craftaction");
        SettingsWindow.Dispose();
        RecipeNoteWindow.Dispose();
        SynthHelperWindow.Dispose();
        ListWindow.Dispose();
        EditorWindow?.Dispose();
        ClipboardWindow?.Dispose();
        Hooks.Dispose();
        IconManager.Dispose();
    }
}
