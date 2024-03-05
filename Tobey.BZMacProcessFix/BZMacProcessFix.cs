using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tobey.BZMacProcessFix
{
    public static class BZMacProcessFix
    {
        // Without the contents of this region, the patcher will not be loaded by BepInEx - do not remove!
        #region BepInEx Patcher Contract
        public static IEnumerable<string> TargetDLLs { get; } = Enumerable.Empty<string>();
        public static void Patch(AssemblyDefinition _) { }
        #endregion

        public static readonly ManualLogSource logger = Logger.CreateLogSource("BZ macOS process fix");

        // unfortunately, BepInEx does not search inside the plugins directory for assemblies so we must permanently patch plugin assemblies
        // which include a process filter for "SubnauticaZero" so that they will work on macOS, where the process name is "Subnautica Below Zero"
        public static void Initialize()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {   // only run the patcher on macOS
                return;
            }

            var dlls = Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories).Distinct();

            var bepinProcessType = typeof(BepInProcess);
            var bepinProcessConstructor = bepinProcessType.GetConstructors()[0];

            foreach (var dll in dlls)
            {
                var relativePath = GetRelativePath(dll, Paths.GameRootPath);

                try
                {
                    using (var assembly = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters() { ReadWrite = true }))
                    {
                        // gather types with a BepInProcess attribute that have a process filter for "SubnauticaZero"
                        // but DO NOT have a process filter for "Subnautica Below Zero"
                        var plugins = assembly.Modules
                            .SelectMany(module => module.GetAllTypes())
                            .Where(type =>
                                type.CustomAttributes.Any(a => a.AttributeType.FullName == typeof(BepInPlugin).FullName) &&

                                type.CustomAttributes.Any(a =>
                                    a.AttributeType.FullName == typeof(BepInProcess).FullName &&
                                    string.Equals((a.ConstructorArguments.SingleOrDefault(arg => arg.Value is string).Value as string).Replace(".exe", string.Empty), "SubnauticaZero", StringComparison.InvariantCultureIgnoreCase)) &&

                                !type.CustomAttributes.Any(a =>
                                    a.AttributeType.FullName == typeof(BepInProcess).FullName &&
                                    string.Equals((a.ConstructorArguments.SingleOrDefault(arg => arg.Value is string).Value as string).Replace(".exe", string.Empty), "Subnautica Below Zero", StringComparison.InvariantCultureIgnoreCase)));

                        if (plugins.Any())
                        {
                            foreach (var plugin in plugins)
                            {
                                // add a process filter for "Subnautica Below Zero"
                                var newAttribute = new CustomAttribute(plugin.Module.ImportReference(bepinProcessConstructor));
                                newAttribute.ConstructorArguments.Add(new CustomAttributeArgument(plugin.Module.ImportReference(typeof(string)), "Subnautica Below Zero"));
                                plugin.CustomAttributes.Add(newAttribute);
                            }

                            // save the patched assembly
                            try
                            {
                                assembly.Write();
                                logger.LogInfo($"Fix applied to [{relativePath}]");
                            }
                            catch (IOException)
                            {
                                logger.LogWarning($"Could not apply fix to [{relativePath}] as it is already in use by another process.");

                                var workaroundPath = $"{dll}.PATCHED";
                                assembly.Write(workaroundPath);
                                logger.LogWarning($"Saved fix to [{GetRelativePath(workaroundPath, Paths.GameRootPath)}] instead. You will need to quit the game, manually replace the original file with the patched one and then relaunch the game.");
                            }
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    logger.LogDebug($"Skipping [{relativePath}] as it is not a managed .NET assembly.");
                }
                catch (Exception e)
                {
                    logger.LogWarning($"There was an unhandled exception while working with the assembly [{relativePath}]:");
                    logger.LogError(e);
                }
            }
        }

        public static string GetRelativePath(string file, string folder)
        {
            var pathUri = new Uri(file);
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }
            var folderUri = new Uri(folder);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
