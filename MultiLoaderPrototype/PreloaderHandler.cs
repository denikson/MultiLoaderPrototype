using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MonoMod.Utils;

namespace MultiLoaderPrototype
{
    public static class PreloaderHandler
    {
        private static readonly Type AssemblyPatcherType =
            AccessTools.TypeByName("BepInEx.Preloader.Patching.AssemblyPatcher");

        private static readonly MethodInfo GetPatcherPluginsProp =
            AccessTools.PropertyGetter(AssemblyPatcherType, "PatcherPlugins");

        private static readonly Action<string> AddPatchesFromDir =
            (Action<string>) AccessTools.Method(AssemblyPatcherType, "AddPatchersFromDirectory")
                .CreateDelegate<Action<string>>();

        private static readonly Func<IList> GetPatcherPlugins =
            (Func<IList>) GetPatcherPluginsProp.CreateDelegate<Func<IList>>();

        private static Harmony instance;

        public static void Init()
        {
            // Add custom preloader patchers by doing a one-time PatcherPlugins prop hook
            instance = new Harmony("org.bepinex.multimodloader.preloaderhandler");
            instance.Patch(GetPatcherPluginsProp,
                new HarmonyMethod(AccessTools.Method(typeof(PreloaderHandler), nameof(OnGetPatcherPlugins))));
        }

        private static void OnGetPatcherPlugins()
        {
            // Unpatch right away and add the custom preloader patchers
            instance.UnpatchAll(instance.Id);
            AddModPatchers();
        }

        private static void AddModPatchers()
        {
            try
            {
                var patcherPlugins = GetPatcherPlugins();
                // Get original patchers added by bepin (should be only this one and bepin's own)
                var origPatcherPlugins = patcherPlugins?.Cast<object>().ToList();

                Util.Logger.LogInfo("Loading preloader patchers from mod");
                foreach (var preloaderPatchesDir in ModManager.GetPreloaderPatchesDirs())
                    AddPatchesFromDir(preloaderPatchesDir);

                // Poll newly added patchers and remove the two by bepin
                var newPatcherPlugins = patcherPlugins?.Cast<object>().Except(origPatcherPlugins).ToList();

                // Initialize patchers manually because this method runs after real Initialize
                Util.Logger.LogInfo($"Loading {newPatcherPlugins?.Count} preloader patchers from mods...");
                InitializeModPatchers(newPatcherPlugins);
            }
            catch (Exception e)
            {
                Util.Logger.LogError($"Failed to add custom preloader patchers. Error message: {e.Message}");
                Util.Logger.LogDebug(e);
            }
        }

        private static void InitializeModPatchers(List<object> patchers)
        {
            Func<object, Action> initializerProp = null;

            Func<object, Action> GetInitializerProp(Type t)
            {
                return AccessTools.Property(t, "Initializer")
                    .GetGetMethod()
                    .CreateDelegate<Func<object, Action>>() as Func<object, Action>;
            }

            foreach (var patcher in patchers)
            {
                // PatcherInfo is internal in BepInEx 5 (fix coming in 6.0), so we have to pull it with some reflection
                initializerProp = initializerProp ?? GetInitializerProp(patcher.GetType());
                var initializer = initializerProp?.Invoke(patcher);
                initializer?.Invoke();
            }
        }
    }
}