using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Animations.SpringBones
{
    public class SetObjectParentWindow : EditorWindow
    {
        [MenuItem("UTJ/Set Parent Window")]
        public static void ShowWindow()
        {
            GetWindow<SetObjectParentWindow>("Set Parent");
        }

        // private

        private Transform newParent;

        private void ReparentSelectedObjects()
        {
            var newChildren = Selection.gameObjects
                .Where(item => item.transform != newParent)
                .Select(gameObject => gameObject.transform)
                .ToArray();
            foreach (var child in newChildren)
            {
                Undo.SetTransformParent(child, newParent, "Set Parent");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            newParent = EditorGUILayout.ObjectField("New Parent", newParent, typeof(Transform), true) as Transform;
            EditorGUILayout.Space();
            if (GUILayout.Button("Set Parent"))
            {
                ReparentSelectedObjects();
            }
        }
   }
}