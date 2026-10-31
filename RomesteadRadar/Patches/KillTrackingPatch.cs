using HarmonyLib;
using RomesteadRadar;
using Shared.Combat;

namespace RomesteadRadar.Patches
{
    /// <summary>
    /// 在伤害结算前后检查目标是否被本地玩家击杀，用于“击杀生物”雷达列表。
    /// 目标死亡后可能立刻被移除，所以必须在结算前抓取快照。
    /// </summary>
    [HarmonyPatch(typeof(SharedCombatHandler), nameof(SharedCombatHandler.DoDamage))]
    internal static class KillTrackingPatch
    {
        private static void Prefix(DamageInfo __0, out KillSnapshot __state)
        {
            __state = null;
            try
            {
                __state = KillTracker.CreateSnapshot(__0);
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning("Failed to record killed creature: " + ex.Message);
            }
        }

        private static void Postfix(KillSnapshot __state, float __result)
        {
            try
            {
                KillTracker.TryRecord(__state, __result);
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning("Failed to record killed creature: " + ex.Message);
            }
        }
    }
}
