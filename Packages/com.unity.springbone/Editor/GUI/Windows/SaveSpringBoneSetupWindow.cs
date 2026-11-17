using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Animations.SpringBones
{
    public class SaveSpringBoneSetupWindow : EditorWindow
    {
        public static void ShowWindow()
        {
            var editorWindow = GetWindow<SaveSpringBoneSetupWindow>(
                "Save Spring Bone Setup");
            if (editorWindow != null)
            {
                editorWindow.SelectObjectsFromSelection();
            }
        }

        // private

        private GameObject springBoneRoot;
        private SpringBoneSerialization.ExportSettings exportSettings;

        private void SelectObjectsFromSelection()
        {
            springBoneRoot = null;

            if (Selection.objects.Length > 0)
            {
                springBoneRoot = Selection.objects[0] as GameObject;
            }

            if (springBoneRoot == null)
            {
                var characterRootComponentTypes = new System.Type[] {
                    typeof(SpringManager),
                    typeof(Animation),
                    typeof(Animator)
                };
                springBoneRoot = characterRootComponentTypes
                    .Select(type => FindObjectOfType(type) as Component)
                    .Where(component => component != null)
                    .Select(component => component.gameObject)
                    .FirstOrDefault();
            }
        }

        private void ShowExportSettingsUI(ref Rect uiRect)
        {
            if (exportSettings == null)
            {
                exportSettings = new SpringBoneSerialization.ExportSettings();
            }

            GUI.Label(uiRect, "Export Settings", SpringBoneGUIStyles.HeaderLabelStyle);
            uiRect.y += uiRect.height;
            exportSettings.ExportSpringBones = GUI.Toggle(uiRect, exportSettings.ExportSpringBones, "Spring Bones", SpringBoneGUIStyles.ToggleStyle);
            uiRect.y += uiRect.height;
            exportSettings.ExportCollision = GUI.Toggle(uiRect, exportSettings.ExportCollision, "Colliders", SpringBoneGUIStyles.ToggleStyle);
            uiRect.y += uiRect.height;
        }

        private void OnGUI()
        {
            SpringBoneGUIStyles.ReacquireStyles();

            const int ButtonHeight = 30;
            const int UISpacing = 8;
            const int UIRowHeight = 24;

            var uiWidth = (int)position.width - UISpacing * 2;
            var yPos = UISpacing;

            springBoneRoot = LoadSpringBoneSetupWindow.DoObjectPicker(
                "Spring Bone Root", springBoneRoot, uiWidth, UIRowHeight, ref yPos);
            var buttonRect = new Rect(UISpacing, yPos, uiWidth, ButtonHeight);
            if (GUI.Button(buttonRect, "Get Root from Selection", SpringBoneGUIStyles.ButtonStyle))
            {
                SelectObjectsFromSelection();
            }
            yPos += ButtonHeight + UISpacing;
            buttonRect.y = yPos;

            ShowExportSettingsUI(ref buttonRect);
            if (springBoneRoot != null)
            {
                if (GUI.Button(buttonRect, "Save CSV", SpringBoneGUIStyles.ButtonStyle))
                {
                    BrowseAndSaveSpringSetup();
                }
            }
        }

        private void BrowseAndSaveSpringSetup()
        {
            if (springBoneRoot == null) { return; }

            var initialFileName = springBoneRoot.name + "_Dynamics.csv";

            var path = EditorUtility.SaveFilePanel(
                "Save Spring Bone Setup", "", initialFileName, "csv");
            if (path.Length == 0) { return; }

            if (System.IO.File.Exists(path))
            {
                var overwriteMessage = "File already exists. Overwrite?\n\n" + path;
                if (!EditorUtility.DisplayDialog("Save Spring Bones", overwriteMessage, "Overwrite", "Cancel"))
                {
                    return;
                }
            }

            var sourceText = SpringBoneSerialization.BuildDynamicsSetupString(springBoneRoot, exportSettings);
            if (FileUtil.WriteAllText(path, sourceText))
            {
                AssetDatabase.Refresh();
                Debug.Log("Saved successfully: " + path);
            }
        }
    }
}