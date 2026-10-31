using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using HarmonyLib;
using RomesteadRadar.Patches;
using RomesteadRadar;

namespace RomesteadRadar
{
    [BepInDependency("IceBoxStudio.Romestead.ModSettingsMenu", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(Guid, "Romestead Radar", PluginVersion.Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.Nohtaehp.Romestead.Radar";

        /// <summary>给其它类使用的日志源（BasePlugin.Log 是实例属性）。</summary>
        internal static ManualLogSource LogSource;

        /// <summary>Mod Settings Menu 中公开的雷达总开关。</summary>
        public static ConfigEntry<bool> Enabled;

        /// <summary>已击杀生物记录（由 KillTracker 维护，请勿手动修改）。</summary>
        public static ConfigEntry<string> KilledCreatures;

        internal static bool RadarEnabled => Enabled == null || Enabled.Value;

        public override void Load()
        {
            LogSource = Log;
            LogSource.LogInfo("=============================================");
            LogSource.LogInfo("Romestead Radar initializing...");
            LogSource.LogInfo("Author: Nohtaehp(https://github.com/Nohtaehp)");

            // 公开配置项：让 Mod Settings Menu 有一项可见设置，而不是只显示内部击杀记录。
            Enabled = Config.Bind("Radar", "Enabled", true,
                new ConfigDescription(
                    Loc.TS(
                        "设置追踪物品后，在小地图上用扇形方向波提示目标方向。",
                        "After selecting a target, show a directional pulse on the minimap."),
                    null,
                    // 分类显示名（ModSettingsMenu 通过 ConfigDescription.Tags 读取）。
                    new
                    {
                        Section = "Radar",
                        DisplayName = Loc.TS("雷达插件", "Radar Plugin"),
                        Order = 0
                    },
                    // 条目显示名。
                    new
                    {
                        DisplayName = Loc.TS("启用雷达", "Enable Radar"),
                        Order = 0
                    }));

            // 这是插件内部数据，不应对玩家显示；用 Mod Settings Menu 能识别的隐藏标签隐藏该条目。
            KilledCreatures = Config.Bind("Radar", "KilledCreatures", string.Empty,
                new ConfigDescription(
                    "已击杀生物记录（按存档 GameId 分开存储，内部数据，请勿手动修改）",
                    null,
                    new { Hidden = true }));
            KillTracker.Initialize(KilledCreatures.Value);
            ModSettingsIntegration.Register(Config);

            // 按补丁类逐个应用：某个补丁失败时不会拖垮整个插件。
            var harmony = new Harmony(Guid);
            Type[] patchTypes =
            {
                typeof(KillTrackingPatch),
                typeof(MinimapRadarPatch),
                typeof(MinimapTrackingLabelPatch),
                typeof(LanguageChangePatch),
                typeof(TopBarItemRadarPatch)
            };

            for (int i = 0; i < patchTypes.Length; i++)
            {
                try
                {
                    harmony.PatchAll(patchTypes[i]);
                }
                catch (Exception ex)
                {
                    LogSource.LogError($"Failed to apply patch {patchTypes[i].Name}: {ex}");
                }
            }

            LogSource.LogInfo("Romestead Radar initialized.");
            LogSource.LogInfo("=============================================");
        }
    }
}
