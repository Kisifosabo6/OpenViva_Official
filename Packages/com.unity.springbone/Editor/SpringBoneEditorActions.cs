using Unity.Animations.SpringBones.GameObjectExtensions;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Animations.SpringBones
{
    public static class SpringBoneEditorActions
    {
        public static void ShowSpringBoneWindow()
        {
            SpringBoneWindow.ShowWindow();
        }

        public static void AssignSpringBonesRecursively()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Please exit Play mode.");
                return;
            }

            if (Selection.gameObjects.Length < 1)
            {
                Debug.LogError("Please select one or more objects.");
                return;
            }

            var springManagers = new HashSet<SpringManager>();
            foreach (var gameObject in Selection.gameObjects)
            {
                SpringBoneSetup.AssignSpringBonesRecursively(gameObject.transform);
                var manager = gameObject.GetComponentInParent<SpringManager>();
                if (manager != null)
                {
                    springManagers.Add(manager);
                }
            }

            foreach (var manager in springManagers)
            {
                SpringBoneSetup.FindAndAssignSpringBones(manager, true);
            }

            AssetDatabase.Refresh();
        }

        public static void CreatePivotForSpringBones()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Please exit Play mode.");
                return;
            }

            if (Selection.gameObjects.Length < 1)
            {
                Debug.LogError("Please select one or more objects.");
                return;
            }

            var selectedSpringBones = Selection.gameObjects
                .Select(gameObject => gameObject.GetComponent<SpringBone>())
                .Where(bone => bone != null);
            foreach (var springBone in selectedSpringBones)
            {
                SpringBoneSetup.CreateSpringPivotNode(springBone);
            }
        }

        public static void AddToOrUpdateSpringManagerInSelection()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Please exit Play mode.");
                return;
            }

            if (Selection.gameObjects.Length <= 0)
            {
                Debug.LogError("Please select one or more objects.");
                return;
            }

            foreach (var gameObject in Selection.gameObjects)
            {
                var manager = gameObject.GetComponent<SpringManager>();
                if (manager == null) { manager = gameObject.AddComponent<SpringManager>(); }
                SpringBoneSetup.FindAndAssignSpringBones(manager, true);
            }
        }

        public static void SelectChildSpringBones()
        {
            var springBoneObjects = Selection.gameObjects
                .SelectMany(gameObject => gameObject.GetComponentsInChildren<SpringBone>(true))
                .Select(bone => bone.gameObject)
                .Distinct()
                .ToArray();
            Selection.objects = springBoneObjects;
        }

        public static void DeleteSpringBonesAndManagers()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Please exit Play mode.");
                return;
            }

            if (Selection.gameObjects.Length != 1)
            {
                Debug.LogError("Please select only one root object.");
                return;
            }

            var rootObject = Selection.gameObjects.First();
            var queryMessage = "Are you sure you want to delete all SpringBones and SpringManagers in this object and its children?\n\n"
                + rootObject.name;
            if (EditorUtility.DisplayDialog(
                "Delete SpringBones and Managers", queryMessage, "Delete", "Cancel"))
            {
                SpringBoneSetup.DestroySpringManagersAndBones(rootObject);
                AssetDatabase.Refresh();
            }
        }

        public static void DeleteSelectedBones()
        {
            var springBonesToDelete = GameObjectUtil.FindComponentsOfType<SpringBone>()
                .Where(bone => Selection.gameObjects.Contains(bone.gameObject))
                .ToArray();
            var springManagersToUpdate = GameObjectUtil.FindComponentsOfType<SpringManager>()
                .Where(manager => manager.springBones.Any(bone => springBonesToDelete.Contains(bone)))
                .ToArray();
            Undo.RecordObjects(springManagersToUpdate, "Delete selected bones");
            foreach (var boneToDelete in springBonesToDelete)
            {
                Undo.DestroyObjectImmediate(boneToDelete);
            }
            foreach (var manager in springManagersToUpdate)
            {
                manager.FindSpringBones(true);
            }
        }

        public static void PromptToUpdateSpringBonesFromList()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Cannot update while in Play mode.");
                return;
            }

            var selectedSpringManagers = Selection.gameObjects
                .Select(gameObject => gameObject.GetComponent<SpringManager>())
                .Where(manager => manager != null)
                .ToArray();
            if (!selectedSpringManagers.Any())
            {
                selectedSpringManagers = GameObjectUtil.FindComponentsOfType<SpringManager>().ToArray();
            }

            if (selectedSpringManagers.Count() != 1)
            {
                Debug.LogError("Please select only one SpringManager.");
                return;
            }

            var springManager = selectedSpringManagers.First();
            var queryMessage = "Do you want to update secondary motion bones from the bone list?\n\n"
                + "SpringBone data not in the list will be deleted, and\n"
                + "missing SpringBone data in the model will be added.\n\n"
                + "SpringManager: " + springManager.name;
            if (EditorUtility.DisplayDialog("Update from Bone List", queryMessage, "Update", "Cancel"))
            {
                AutoSpringBoneSetup.UpdateSpringManagerFromBoneList(springManager);
            }
        }
    }
}