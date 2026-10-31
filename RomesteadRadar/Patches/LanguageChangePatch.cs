using System;
using Candide;
using HarmonyLib;
using RomesteadRadar.UI;

namespace RomesteadRadar.Patches
{
    /// <summary>
    /// 游戏内切换语言后刷新雷达窗口里已经显示的文字（小地图追踪文字每帧重建，无需刷新）。
    /// </summary>
    [HarmonyPatch(typeof(Globals), nameof(Globals.SetLanguageCode))]
    internal static class LanguageChangePatch
    {
        private static void Postfix()
        {
            try
            {
                ItemRadarWindow.OnLanguageChanged();
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning("Failed to refresh radar UI after language change: " + ex.Message);
            }
        }
    }
}
