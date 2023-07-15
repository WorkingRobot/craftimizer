using Dalamud.Interface;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace Craftimizer.Plugin;

internal static class ImGuiUtils
{
    private static readonly Stack<(Vector2 Min, Vector2 Max)> GroupPanelLabelStack = new();

    // Adapted from https://github.com/ocornut/imgui/issues/1496#issuecomment-655048353
    public static void BeginGroupPanel(float width = -1, bool addPadding = true)
    {
        ImGui.BeginGroup();

        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        var frameHeight = ImGui.GetFrameHeight();

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(width < 0 ? ImGui.GetContentRegionAvail().X : width, 0));
        ImGui.Dummy(new Vector2(frameHeight * 0.5f, 0));
        ImGui.SameLine(0, 0);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(frameHeight * 0.5f, 0));
        GroupPanelLabelStack.Push((ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
        ImGui.SameLine(0, 0);
        ImGui.Dummy(new Vector2(0f, frameHeight * (addPadding ? 1 : .5f) + itemSpacing.Y));

        ImGui.BeginGroup();

        ImGui.PopStyleVar(2);

        ImGui.PushItemWidth(MathF.Max(0, ImGui.CalcItemWidth() - frameHeight));
    }

    public static void BeginGroupPanel(string name, float width = -1, bool addPadding = true)
    {
        ImGui.BeginGroup();

        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        var frameHeight = ImGui.GetFrameHeight();

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(width < 0 ? ImGui.GetContentRegionAvail().X : width, 0));
        ImGui.Dummy(new Vector2(frameHeight * 0.5f, 0));
        ImGui.SameLine(0, 0);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(frameHeight * 0.5f, 0));
        ImGui.SameLine(0, 0);
        ImGui.TextUnformatted(name);
        GroupPanelLabelStack.Push((ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
        ImGui.SameLine(0, 0);
        ImGui.Dummy(new Vector2(0f, frameHeight * (addPadding ? 1 : .5f) + itemSpacing.Y));

        ImGui.BeginGroup();

        ImGui.PopStyleVar(2);

        ImGui.PushItemWidth(MathF.Max(0, ImGui.CalcItemWidth() - frameHeight));
    }

    public static void EndGroupPanel()
    {
        ImGui.PopItemWidth();

        var itemSpacing = ImGui.GetStyle().ItemSpacing;

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        var frameHeight = ImGui.GetFrameHeight();

        ImGui.EndGroup();

        ImGui.EndGroup();

        ImGui.SameLine(0, 0);
        ImGui.Dummy(new Vector2(frameHeight * 0.5f, 0));
        ImGui.Dummy(new Vector2(0f, frameHeight * 0.5f - itemSpacing.Y));

        ImGui.EndGroup();

        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var labelRect = GroupPanelLabelStack.Pop();

        var halfFrame = new Vector2(frameHeight * 0.25f, frameHeight) * 0.5f;
        (Vector2 Min, Vector2 Max) frameRect = (itemMin + halfFrame, itemMax - new Vector2(halfFrame.X, 0));
        labelRect.Min.X -= itemSpacing.X;
        labelRect.Max.X += itemSpacing.X;
        for (var i = 0; i < 4; ++i)
        {
            var (minClip, maxClip) = i switch
            {
                0 => (new Vector2(float.NegativeInfinity), new Vector2(labelRect.Min.X, float.PositiveInfinity)),
                1 => (new Vector2(labelRect.Max.X, float.NegativeInfinity), new Vector2(float.PositiveInfinity)),
                2 => (new Vector2(labelRect.Min.X, float.NegativeInfinity), new Vector2(labelRect.Max.X, labelRect.Min.Y)),
                3 => (new Vector2(labelRect.Min.X, labelRect.Max.Y), new Vector2(labelRect.Max.X, float.PositiveInfinity)),
                _ => (Vector2.Zero, Vector2.Zero)
            };

            ImGui.PushClipRect(minClip, maxClip, true);
            ImGui.GetWindowDrawList().AddRect(
                frameRect.Min, frameRect.Max,
                ImGui.GetColorU32(ImGuiCol.Border),
                halfFrame.X);
            ImGui.PopClipRect();
        }

        ImGui.PopStyleVar(2);

        ImGui.Dummy(Vector2.Zero);

        ImGui.EndGroup();
    }

    public static bool IconButtonSized(FontAwesomeIcon icon, Vector2 size)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var ret = ImGui.Button(icon.ToIconString(), size);
        ImGui.PopFont();
        return ret;
    }

    // https://gist.github.com/dougbinks/ef0962ef6ebe2cadae76c4e9f0586c69#file-imguiutils-h-L219
    private static void UnderlineLastItem(Vector4 color)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        min.Y = max.Y;
        ImGui.GetWindowDrawList().AddLine(min, max, ImGui.ColorConvertFloat4ToU32(color), 1);
    }

    // https://gist.github.com/dougbinks/ef0962ef6ebe2cadae76c4e9f0586c69#file-imguiutils-h-L228
    public static unsafe void Hyperlink(string text, string url)
    {
        ImGui.TextUnformatted(text);
        UnderlineLastItem(*ImGui.GetStyleColorVec4(ImGuiCol.Button));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            ImGui.SetTooltip("Open in Browser");
        }
    }
}
