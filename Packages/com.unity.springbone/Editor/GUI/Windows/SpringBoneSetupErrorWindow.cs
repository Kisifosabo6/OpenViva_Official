using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Animations.SpringBones
{
    public class SpringBoneSetupErrorWindow : EditorWindow
    {
        public interface IConfirmAction
        {
            void Perform();
        }

        public static void ShowWindow
        (
            GameObject springBoneRoot,
            GameObject colliderRoot,
            string path, 
            IEnumerable<DynamicsSetup.ParseMessage> errors, 
            IConfirmAction onConfirm
        )
        {
            var window = GetWindow<SpringBoneSetupErrorWindow>("Dynamics Setup");
            window.springBoneRoot = springBoneRoot;
            window.colliderRoot = colliderRoot;
            window.filePath = path;
            window.onConfirmAction = onConfirm;
            window.errors = errors.ToArray();
        }

        // private

        private GameObject springBoneRoot;
        private GameObject colliderRoot;
        private string filePath;
        private IConfirmAction onConfirmAction;
        private DynamicsSetup.ParseMessage[] errors;
        private Vector2 scrollPosition;

        private void OnGUI()
        {
            EditorGUILayout.Space();
            GUILayout.Label("Some errors were encountered in the dynamics setup. Would you like to create only the valid items?");
            EditorGUILayout.Space();
            EditorGUILayout.ObjectField("Spring Bone Root", springBoneRoot, typeof(GameObject), true);
            EditorGUILayout.ObjectField("Collider Root", colliderRoot, typeof(GameObject), true);
            EditorGUILayout.TextField("Path", filePath);
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Valid"))
            {
                onConfirmAction.Perform();
                Close();
            }
            if (GUILayout.Button("Cancel")) { Close(); }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();

            GUILayout.Label("Errors");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
            foreach (var error in errors)
            {
                var errorString = error.Message;
                if (!string.IsNullOrEmpty(error.SourceLine))
                {
                    errorString += "\n" + error.SourceLine;
                }
                GUILayout.Label(errorString);
            }
            GUILayout.EndScrollView();
        }
    }
}