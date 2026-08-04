// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace realvirtual
{
    //! Guided first-run window for a smooth beginner experience.

    //! Shows a live checklist (Unity version, physics layers, input handling, demo scene) where every
    //! item reports its current status and can be fixed with a single click. The goal is simple: the
    //! user should never face a raw error - either everything is configured, or there is an obvious
    //! green button that fixes the next step. Opens automatically on first install.
    public class RealvirtualGetStartedWindow : EditorWindow
    {
        private static readonly Color OkColor = new Color(0.34f, 0.78f, 0.40f);
        private static readonly Color WarnColor = new Color(0.92f, 0.74f, 0.20f);
        private static readonly Color TodoColor = new Color(0.90f, 0.45f, 0.40f);

        private bool _restartNeeded;
        private Vector2 _scroll;

        [MenuItem("Tools/realvirtual/Get Started", false, 900)]
        public static void ShowWindow()
        {
            var window = GetWindow<RealvirtualGetStartedWindow>(true, "realvirtual - Get Started", true);
            window.minSize = new Vector2(470, 520);
            window.Show();
        }

        private void OnFocus()
        {
            Repaint();
        }

        //! Runs a button action after the current IMGUI pass completes. Handlers that show dialogs,
        //! open windows or change the scene must not run inside the layout pass - doing so corrupts the
        //! IMGUI layout stack ("EndLayoutGroup: BeginLayoutGroup must be called first").
        private void Defer(System.Action action)
        {
            EditorApplication.delayCall += () =>
            {
                action?.Invoke();
                if (this != null)
                    Repaint();
            };
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(8);
            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            EditorGUILayout.LabelField("Welcome to realvirtual", title);
            if (!string.IsNullOrEmpty(Global.Version))
                EditorGUILayout.LabelField(Global.Version, EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Let's get your project ready. Each step shows its status - fix anything marked NOTE or TODO with one click.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);

            DrawUnityVersionRow();
            DrawLayersRow();
            DrawInputRow();
            DrawDemoRow();

            EditorGUILayout.Space(12);

            EditorGUILayout.LabelField("In a hurry?", EditorStyles.boldLabel);
            if (GUILayout.Button("Apply All Recommended Settings", GUILayout.Height(30)))
                Defer(() => ProjectSettingsTools.SetStandardSettings(true));

            if (_restartNeeded || !ProjectSettingsTools.IsInputHandlingBoth())
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "A Unity restart is required to activate the input handling change.",
                    MessageType.Warning);
                if (GUILayout.Button("Restart Unity Now", GUILayout.Height(26)))
                    Defer(() => EditorApplication.OpenProject(Directory.GetCurrentDirectory()));
            }

            EditorGUILayout.Space(10);
            DrawOpenDemo();

            EditorGUILayout.Space(14);
            if (GUILayout.Button("Help & Resources (documentation, videos, forum)"))
                Defer(() => { var hello = CreateInstance<HelloWindow>(); hello.Open(); });

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawUnityVersionRow()
        {
            bool ok = Application.unityVersion.StartsWith("6000.3");
            DrawStatusRow(
                ok, warnOnly: true,
                "Unity Version",
                ok
                    ? $"Unity {Application.unityVersion} - supported."
                    : $"Unity {Application.unityVersion} - realvirtual is tested on Unity 6.3 LTS. You can continue, but some features may behave differently.",
                null, null);
        }

        private void DrawLayersRow()
        {
            bool ok = ProjectSettingsTools.AllRealvirtualLayersPresent();
            DrawStatusRow(
                ok, warnOnly: false,
                "Physics Layers",
                ok
                    ? "The realvirtual layers are configured."
                    : "The realvirtual layers (rvTransport, rvMU, ...) are missing. Without them, transport surfaces and sensors throw errors.",
                "Configure",
                () => ProjectSettingsTools.EnsureRealvirtualLayersConfigured());
        }

        private void DrawInputRow()
        {
            bool ok = ProjectSettingsTools.IsInputHandlingBoth();
            DrawStatusRow(
                ok, warnOnly: false,
                "Input Handling",
                ok
                    ? "Active Input Handling is set to 'Both'."
                    : "Active Input Handling must be 'Both'. Otherwise camera navigation, sensors and the runtime UI throw errors. A restart is required after fixing.",
                "Set to Both",
                () =>
                {
                    if (ProjectSettingsTools.SetInputHandlingBoth())
                        _restartNeeded = true;
                });
        }

        private void DrawDemoRow()
        {
            bool ok = SamplesImportChecker.AreSamplesImported();
            DrawStatusRow(
                ok, warnOnly: true,
                "Demo Scene",
                ok
                    ? "The Getting Started demo is imported."
                    : "Import the Getting Started demo to explore a ready-made scene.",
                "Import",
                () => SamplesImportChecker.ShowImportSamplesDialog(false));
        }

        private void DrawOpenDemo()
        {
            string demoPath = ProjectSettingsTools.FindDemoScenePath();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(demoPath)))
            {
                if (GUILayout.Button("Open Demo Scene", GUILayout.Height(30)) && !string.IsNullOrEmpty(demoPath))
                    Defer(() => EditorSceneManager.OpenScene(demoPath));
            }

            if (string.IsNullOrEmpty(demoPath))
                EditorGUILayout.LabelField("Import the demo first to enable this.", EditorStyles.centeredGreyMiniLabel);
        }

        //! Draws one checklist row: a colored status badge, a label with a detail line and an optional fix button.
        private void DrawStatusRow(bool ok, bool warnOnly, string label, string detail, string buttonText, System.Action onFix)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            string badge = ok ? "OK" : (warnOnly ? "NOTE" : "TODO");
            Color color = ok ? OkColor : (warnOnly ? WarnColor : TodoColor);
            var badgeStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = 46
            };
            Color prev = GUI.color;
            GUI.color = color;
            GUILayout.Label(badge, badgeStyle, GUILayout.Height(38));
            GUI.color = prev;

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(detail))
                EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (!ok && onFix != null && !string.IsNullOrEmpty(buttonText))
            {
                if (GUILayout.Button(buttonText, GUILayout.Width(110), GUILayout.Height(30)))
                    Defer(onFix);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
