using System;
using System.Collections.Generic;
using System.Reflection;
using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Components.Buttons;
using Candide.CandideUI.Containers;
using Candide.CandideUI.Windows;
using CandideCreator.Shared.Graphics;
using HarmonyLib;
using RomesteadRadar.UI;

namespace RomesteadRadar.Patches
{
    /// <summary>
    /// 在游戏顶栏（按 M/背包等窗口出现时上方那排图标）里，
    /// 地图按钮后面插入一个“寻找物品”按钮。
    /// </summary>
    [HarmonyPatch(typeof(TopPanelWindow), MethodType.Constructor, typeof(CandideDesktop))]
    internal static class TopBarItemRadarPatch
    {
        private static void Postfix(TopPanelWindow __instance)
        {
            try
            {
                if (__instance.Child is not CandideHorizontalStackPanel panel)
                    return;

                var button = new CandideIconButton("magnifying_glass", IconFlag.Medium)
                {
                    Padding = new CandideThickness(2, 2, 3, 3),
                    GetTooltipContent = () => new List<CandideUiElement>
                    {
                        new CandideTextLabel
                        {
                            Text = Loc.T("物品雷达", "Item Radar")
                        }
                    }
                };
                button.OnClickAction = delegate
                {
                    ItemRadarWindow.Open(__instance);
                };

                panel.AddChild(button);

                FieldInfo childrenField = AccessTools.Field(typeof(CandideUiContainerMultipleItems), "Children");
                List<CandideUiElement> children = childrenField?.GetValue(panel) as List<CandideUiElement>;
                if (children == null)
                    return;

                children.Remove(button);

                int mapIndex = -1;
                FieldInfo mapButtonField = AccessTools.Field(typeof(TopPanelWindow), "_mapButton");
                IconToggleButton mapButton = mapButtonField?.GetValue(__instance) as IconToggleButton;
                if (mapButton != null)
                    mapIndex = children.IndexOf(mapButton);

                children.Insert(mapIndex >= 0 ? mapIndex + 1 : Math.Max(0, children.Count - 1), button);
                panel.InvalidateArrange();
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning("Failed to inject radar button: " + ex.Message);
            }
        }
    }
}
