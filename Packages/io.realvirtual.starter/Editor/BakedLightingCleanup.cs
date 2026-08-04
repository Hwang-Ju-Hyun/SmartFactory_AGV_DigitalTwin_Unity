// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace realvirtual
{
    //! Removes the per-scene baked-lighting folders (LightingData + ReflectionProbe + Lightmaps) that
    //! Unity stores next to a scene, and disables Baked Global Illumination on each scene.
    //!
    //! realvirtual computes the skybox ambient and reflection live at runtime (EnvironmentController ->
    //! SkyboxSetup -> DynamicGI.UpdateEnvironment), so for skybox-lit scenes the baked data is redundant
    //! and its deletion is visually identical. Scenes that rely on REAL baked lightmaps (baked shadows /
    //! ambient occlusion) will look different after cleanup - this is intended and clearly warned about.
    //!
    //! Removing these folders slims down the project and removes the binary lighting files that otherwise
    //! show up as changes in version control after every bake.
    public static class BakedLightingCleanup
    {
        //! Shared LightingSettings asset shipped with realvirtual that has Baked GI disabled.
        private const string SharedLightingSettingsPath =
            "Packages/io.realvirtual.starter/Assets/Settings/Realvirtual Lighting Settings.lighting";

        // ---- Customer-facing menu (with safety warning) ----

        [MenuItem("Tools/realvirtual/Settings/Remove Baked Lighting Folders...", false, 916)]
        public static void RemoveBakedLightingMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Remove Baked Lighting Folders",
                    "Please leave Play mode before running this.", "OK");
                return;
            }

            var allScenes = ProjectScenePaths("Assets/");
            var activeScene = SceneManager.GetActiveScene();
            bool activeSaved = !string.IsNullOrEmpty(activeScene.path);

            var warning =
                "This DELETES the baked lighting data (LightingData, ReflectionProbe and Lightmaps) and " +
                "disables Baked Global Illumination.\n\n" +
                "realvirtual then lights these scenes with its runtime skybox ambient - visually identical " +
                "for skybox-lit scenes. Scenes that rely on REAL baked lightmaps (baked shadows / ambient " +
                "occlusion) WILL look different.\n\n" +
                "This cannot be undone except through version control. Commit or back up your project first.\n\n" +
                $"Found {allScenes.Count} scene(s) under Assets/.\n\n" +
                "What do you want to clean?";

            // 0 = "All scenes", 1 = "Cancel", 2 = "Active scene only"
            int choice = EditorUtility.DisplayDialogComplex("Remove Baked Lighting Folders",
                warning,
                $"All scenes ({allScenes.Count})",
                "Cancel",
                activeSaved ? "Active scene only" : "Active scene (none open)");

            if (choice == 1) return;
            if (choice == 2)
            {
                if (!activeSaved)
                {
                    EditorUtility.DisplayDialog("Remove Baked Lighting Folders",
                        "No saved scene is open.", "OK");
                    return;
                }
                RunActiveScene();
                return;
            }
            RunScenes("Assets/", dryRun: false);
        }

        // ---- Prompt-free entry points (automation / scripting / MCP) ----

        //! Disables baked GI on the active scene and deletes its baked-lighting folder. No dialog.
        public static void RunActiveScene()
        {
            if (!GuardNotPlaying()) return;
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[BakedLightingCleanup] No saved scene is open.");
                return;
            }

            var shared = LoadSharedSettings();
            var folders = new HashSet<string>();
            int processed = 0;
            ProcessOpenScene(scene, shared, folders, ref processed);
            int deleted = DeleteFolders(folders);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BakedLightingCleanup] Active scene done: {scene.path}  processed={processed} foldersDeleted={deleted}");
        }

        //! Cleans every scene under Assets/ (no dialog). Intended for scripted/automated runs.
        public static void CleanAllProjectScenesNoPrompt() => RunScenes("Assets/", dryRun: false);

        //! Cleans only the bundled demo scenes under Assets/Samples (no dialog). For maintainer use.
        public static void CleanSamplesNoPrompt() => RunScenes("Assets/Samples/", dryRun: false);

        //! Dry run over Assets/Samples (lists what would change, deletes nothing). For maintainer use.
        public static void DryRunSamplesNoPrompt() => RunScenes("Assets/Samples/", dryRun: true);

        // ---- Core ----

        //! Disables baked GI and deletes the baked-lighting folder of every scene whose path starts with
        //! pathPrefix. References are nulled first; folders are deleted afterwards (so shared folders are
        //! only removed once no scene references them anymore). Returns the number of folders deleted.
        public static int RunScenes(string pathPrefix, bool dryRun)
        {
            if (!GuardNotPlaying()) return 0;

            var originalScenePath = SceneManager.GetActiveScene().path;
            var shared = LoadSharedSettings();
            if (shared == null && !dryRun)
                Debug.LogWarning($"[BakedLightingCleanup] Shared settings not found at {SharedLightingSettingsPath}; " +
                                 "falling back to LightingSettings.bakedGI = false per scene.");

            var scenePaths = ProjectScenePaths(pathPrefix);
            var folders = new HashSet<string>();
            int processed = 0, deleted = 0, errors = 0;

            try
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    var path = scenePaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Remove Baked Lighting",
                            $"{i + 1}/{scenePaths.Count}  {path}", (float)(i + 1) / scenePaths.Count))
                        break;
                    try
                    {
                        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        if (dryRun)
                        {
                            var own = SameNameBakedFolder(scene.path);
                            if (own != null) folders.Add(own);
                            processed++;
                        }
                        else
                        {
                            ProcessOpenScene(scene, shared, folders, ref processed);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        errors++;
                        Debug.LogError($"[BakedLightingCleanup] FAILED on {path}: {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!dryRun)
            {
                deleted = DeleteFolders(folders);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (!string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath))
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);

            var sb = new StringBuilder();
            sb.AppendLine($"[BakedLightingCleanup] {(dryRun ? "DRY RUN" : "DONE")} over {scenePaths.Count} scene(s) under '{pathPrefix}'.");
            sb.AppendLine($"  processed (baked GI disabled) = {processed}");
            sb.AppendLine($"  folders {(dryRun ? "to delete" : "deleted")}     = {(dryRun ? folders.Count : deleted)}");
            sb.AppendLine($"  errors                        = {errors}");
            if (folders.Count > 0)
            {
                sb.AppendLine(dryRun ? "  Folders that WOULD be deleted:" : "  Folders deleted:");
                foreach (var f in folders.OrderBy(x => x)) sb.AppendLine("    - " + f);
            }
            Debug.Log(sb.ToString());
            return deleted;
        }

        //! Disables baked GI on the currently open scene, clears + nulls its LightingData reference,
        //! saves the scene, and records the scene's own (same-name) baked-lighting folder for deletion.
        private static void ProcessOpenScene(Scene scene, LightingSettings shared,
            HashSet<string> folders, ref int processed)
        {
            // The scene's OWN baked-lighting folder (the same-name folder Unity creates next to the
            // scene). Do NOT resolve the LightingData GUID - duplicated GUIDs (e.g. a Samples~ source and
            // its imported Assets/Samples copy) can resolve to a folder outside this scene's directory.
            var own = SameNameBakedFolder(scene.path);
            bool hasReference = Lightmapping.lightingDataAsset != null;

            // Nothing baked here - leave the scene untouched so it is not needlessly re-serialized.
            if (own == null && !hasReference)
                return;

            if (own != null) folders.Add(own);

            // Disable baked GI without creating a per-scene LightingSettings object.
            if (shared != null)
                Lightmapping.lightingSettings = shared;                       // m_EnableBakedLightmaps:0 in shared asset
            else if (Lightmapping.TryGetLightingSettings(out var ls) && ls != null)
                ls.bakedGI = false;

            // Drop the LightingData asset of the loaded scene and null the serialized reference.
            Lightmapping.ClearLightingDataAsset();
            Lightmapping.lightingDataAsset = null;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            processed++;
        }

        //! Deletes the collected baked-lighting folders (and their .meta). Each is re-checked to be a
        //! valid, baked-lighting folder so nothing unexpected is removed. Returns the number deleted.
        private static int DeleteFolders(HashSet<string> folders)
        {
            int deleted = 0;
            foreach (var folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder) && LooksLikeBakedFolder(folder))
                {
                    if (AssetDatabase.DeleteAsset(folder)) deleted++;
                    else Debug.LogWarning($"[BakedLightingCleanup] Could not delete folder {folder}");
                }
            }
            return deleted;
        }

        //! Returns the same-name folder next to a scene (the folder Unity bakes into) if it exists and
        //! actually contains baked-lighting artifacts; otherwise null.
        private static string SameNameBakedFolder(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return null;
            var dir = (Path.GetDirectoryName(scenePath) ?? "").Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(scenePath);
            var folder = string.IsNullOrEmpty(dir) ? name : dir + "/" + name;
            return LooksLikeBakedFolder(folder) ? folder : null;
        }

        //! True if the folder is a valid Unity folder that contains baked-lighting output.
        private static bool LooksLikeBakedFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return false;
            var abs = Path.GetFullPath(folder);
            if (!Directory.Exists(abs)) return false;
            return Directory.EnumerateFiles(abs, "LightingData.asset").Any()
                   || Directory.EnumerateFiles(abs, "ReflectionProbe-*.exr").Any()
                   || Directory.EnumerateFiles(abs, "Lightmap-*").Any();
        }

        //! All scene paths under the given prefix that live in the project (excludes Library/PackageCache).
        private static List<string> ProjectScenePaths(string pathPrefix)
            => AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(pathPrefix) && !p.Contains("/Library/"))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

        private static LightingSettings LoadSharedSettings()
            => AssetDatabase.LoadAssetAtPath<LightingSettings>(SharedLightingSettingsPath);

        private static bool GuardNotPlaying()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BakedLightingCleanup] Stop Play mode before running (scene edits in Play are discarded).");
                return false;
            }
            return true;
        }
    }
}
