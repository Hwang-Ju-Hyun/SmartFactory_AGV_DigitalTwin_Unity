// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace realvirtual
{
    //! Editor-side safety net for a smooth first-run experience.

    //! Two jobs, both aimed at "either there is no error, or the user knows exactly what to do":
    //! - Ensures the realvirtual physics layers (rvTransport, rvMU, ...) exist before any component
    //!   needs them. This silently prevents the "A game object can only be in one layer" error in a
    //!   fresh project where the standard settings were never applied.
    //! - Before entering play mode it verifies Active Input Handling is set to "Both". If not, it
    //!   offers a one-click fix and restart instead of letting the user drown in raw input exceptions.
    [InitializeOnLoad]
    public static class RealvirtualSetupGuard
    {
        // Session-only flag so we ask at most once per session if the user chooses to play anyway
        private const string INPUT_PROMPT_DISMISSED_KEY = "realvirtual_InputPromptDismissedThisSession";

        static RealvirtualSetupGuard()
        {
            EditorApplication.delayCall += EnsureLayersOnLoad;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        //! Makes sure the realvirtual layers exist as soon as the editor loads (no-op once present).
        private static void EnsureLayersOnLoad()
        {
            if (BuildPipeline.isBuildingPlayer || Application.isPlaying)
                return;

            ProjectSettingsTools.EnsureRealvirtualLayersConfigured();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            // Guarantee the layers exist before the simulation starts
            ProjectSettingsTools.EnsureRealvirtualLayersConfigured();

            // Never interrupt headless / CI runs with a dialog
            if (Application.isBatchMode)
                return;

            // realvirtual relies on the legacy UnityEngine.Input API. With input handling set to
            // "Input System Package" only, every Input.* access throws at runtime.
            if (ProjectSettingsTools.IsInputHandlingBoth())
                return;

            if (SessionState.GetBool(INPUT_PROMPT_DISMISSED_KEY, false))
                return;

            int choice = EditorUtility.DisplayDialogComplex(
                "realvirtual - Input Handling",
                "realvirtual uses Unity's legacy Input system, but this project has 'Active Input Handling' " +
                "set to 'Input System Package' only.\n\n" +
                "Without 'Both', camera navigation, sensors and the runtime UI will throw errors during play.\n\n" +
                "Fix it now? Unity needs to restart for the change to take effect.",
                "Fix & Restart",   // 0
                "Continue Anyway", // 1
                "Cancel");         // 2

            switch (choice)
            {
                case 0: // Fix & Restart
                    EditorApplication.isPlaying = false; // do not enter play mode in the broken state
                    ProjectSettingsTools.SetInputHandlingBoth();
                    PromptRestart();
                    break;

                case 1: // Continue anyway - let play proceed, but don't nag again this session
                    SessionState.SetBool(INPUT_PROMPT_DISMISSED_KEY, true);
                    break;

                case 2: // Cancel - stay in edit mode, ask again next time
                    EditorApplication.isPlaying = false;
                    break;
            }
        }

        private static void PromptRestart()
        {
            EditorApplication.delayCall += () =>
            {
                bool restart = EditorUtility.DisplayDialog(
                    "Restart Required",
                    "Active Input Handling was set to 'Both'.\n\n" +
                    "Unity needs to restart for this change to take effect.",
                    "Restart Now",
                    "Later");

                if (restart)
                    EditorApplication.OpenProject(Directory.GetCurrentDirectory());
            };
        }
    }
}
