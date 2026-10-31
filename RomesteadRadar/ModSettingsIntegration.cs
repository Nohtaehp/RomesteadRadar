using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;

namespace RomesteadRadar
{
    /// <summary>
    /// 软集成 Mod Settings Menu：通过反射注册版本、说明和图标，未安装该 Mod 时不影响插件。
    /// </summary>
    internal static class ModSettingsIntegration
    {
        private const string ModSettingsAssemblyName = "ModSettingsMenu";
        private const string RegistryTypeName = "ModSettingsMenu.Api.ModSettingsRegistry";
        private const string OptionsTypeName = "ModSettingsMenu.Api.ModSettingsModOptions";

        internal static void Register(ConfigFile config)
        {
            try
            {
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == ModSettingsAssemblyName);
                if (assembly == null)
                    return;

                Type registryType = assembly.GetType(RegistryTypeName, false);
                Type optionsType = assembly.GetType(OptionsTypeName, false);
                if (registryType == null || optionsType == null)
                    return;

                // ModSettingsRegistry.Register(guid, name, config, options)
                MethodInfo registerMethod = registryType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        ParameterInfo[] parameters = m.GetParameters();
                        return m.Name == "Register"
                            && parameters.Length == 4
                            && parameters[0].ParameterType == typeof(string)
                            && parameters[1].ParameterType == typeof(string)
                            && parameters[2].ParameterType == typeof(ConfigFile)
                            && parameters[3].ParameterType == optionsType;
                    });
                if (registerMethod == null)
                    return;

                object options = Activator.CreateInstance(optionsType);
                SetProperty(options, "Description", Loc.TS(
                    "物品、生物和宝箱方向雷达。",
                    "Directional radar for items, creatures, and chests."));
                SetProperty(options, "Version", PluginVersion.DisplayVersion);
                SetProperty(options, "Icon", "magnifying_glass");
                SetProperty(options, "Order", 100);
                SetProperty(options, "HideWhenEmpty", false);

                registerMethod.Invoke(
                    null,
                    new object[] { Plugin.Guid, "Romestead Radar", config, options });
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning("Mod Settings Menu registration failed: " + ex.Message);
            }
        }

        private static void SetProperty(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
                property.SetValue(target, value);
        }
    }
}
