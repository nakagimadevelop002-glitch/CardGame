#if UNITY_EDITOR && UNITY_SWITCH
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;


namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet.Editor
{
    [InitializeOnLoad]
    public static class NintendoSdkPluginImporter
    {
        const string k_ScriptingDefineWhenPackageInstalled = "NINTENDO_SDK_PLUGIN_IMPORTED";
        static string dialogKey => $"ShownSDKImportDialog_{PlayerSettings.productGUID}";
        const string k_PluginName = "NintendoSDKPlugin.a";

#if !NINTENDO_SDK_PLUGIN_IMPORTED
        static NintendoSdkPluginImporter()
        {
            if (IsSDKPluginInProject())
            {
                AddScriptingDefine();
                return;
            }

            if (EditorPrefs.GetBool(dialogKey))
            {
                Debug.LogWarning("Nintendo SDK Plugin was not imported, the sample will not work correctly until is is imported manually");
                return;
            }

            bool foundSDKPlugin = TryFindNintendoSDKPlugin(out string sdkPluginPath);

            if (!foundSDKPlugin)
            {
                EditorUtility.DisplayDialog("Nintendo SDK Plugin Not Found", "This sample requires the Nintendo SDK Plugin to be imported in to the project.\n" +
                    "This was not found in your SDK Install folder.\nPlease import it in to the project manually", "Ok");
                EditorPrefs.SetBool(dialogKey, true);
                return;
            }

            bool shouldImport = EditorUtility.DisplayDialog("Import Nintendo SDK Plugin", "This sample requires the Nintendo SDK Plugin to be imported in to the project.\n" +
                $"This plugin was found at: \n{sdkPluginPath}.\nWould you like to import it?\nYou can import it manually later", "Import Now", "Don't Import");
            EditorPrefs.SetBool(dialogKey, true);

            if (shouldImport)
            {
                ImportSDKPlugin(sdkPluginPath);
            }
        }
#endif

        [MenuItem("Samples/Switch Controls/Import Nintendo SDK Plugin")]
        static void TryImportSDKPlugin()
        {
            bool foundSDKPlugin = TryFindNintendoSDKPlugin(out string sdkPluginPath);
            if (!foundSDKPlugin)
            {
                Debug.LogError("Couldn't find SDK Plugin");
                return;
            }

            ImportSDKPlugin(sdkPluginPath);
        }

        static void ImportSDKPlugin(string path)
        {
            AssetDatabase.ImportPackage(path, false);
            AddScriptingDefine();
        }


        static bool IsSDKPluginInProject()
        {
            foreach (var switchImporter in PluginImporter.GetImporters(BuildTarget.Switch))
            {
                string name = Path.GetFileName(switchImporter.assetPath);
                if (name == k_PluginName)
                {
                    return true;
                }
            }

            return false;
        }

        static bool TryFindNintendoSDKPlugin(out string path)
        {
            string nintendoSDKPath = Environment.GetEnvironmentVariable("NINTENDO_SDK_ROOT");

            //"C:\Nintendo\SDK175\UnityForNintendoSwitch\Plugins\NintendoSDKPlugin\Libraries\NintendoSDKPlugin.unitypackage"
            string expectedPackagePath = Path.Combine(nintendoSDKPath, "..\\", "UnityForNintendoSwitch", "Plugins", "NintendoSDKPlugin",
                "Libraries", "NintendoSDKPlugin.unitypackage");
            expectedPackagePath = Path.GetFullPath(expectedPackagePath);

            if (File.Exists(expectedPackagePath))
            {
                path = expectedPackagePath;
                return true;
            }

            path = string.Empty;
            return false;
        }

        static void AddScriptingDefine()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.NintendoSwitch) ?? string.Empty;

            if (defines.Contains(k_ScriptingDefineWhenPackageInstalled))
            {
                return;
            }

            defines += $";{k_ScriptingDefineWhenPackageInstalled}";
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.NintendoSwitch, defines);
            Debug.Log($"{k_ScriptingDefineWhenPackageInstalled} was added to the Scripting Defines for Nintendo Switch");
        }
    }
}
#endif