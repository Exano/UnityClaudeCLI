using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ClaudeCode.Editor
{
    /// <summary>
    /// Represents a dragged asset/object as a compact reference.
    /// </summary>
    [Serializable]
    public struct Attachment
    {
        public string DisplayName; // e.g. "PlayerController.cs"
        public string Path;        // e.g. "Assets/Scripts/PlayerController.cs"
        public string TypeLabel;   // e.g. "Script", "Prefab", "GameObject", "Material"
        public string Detail;      // optional extra info (components, etc.)
        public bool IsSceneObject; // true = hierarchy object, not a file on disk

        /// <summary>
        /// Compact reference for the prompt.
        /// File assets → Claude can Read/Grep them.
        /// Scene objects → Claude should use MCP to inspect them.
        /// </summary>
        public string ToPromptReference()
        {
            var sb = new StringBuilder();
            if (IsSceneObject)
            {
                sb.Append($"[Scene {TypeLabel} name=\"{DisplayName}\" path=\"{Path}\"]");
                if (!string.IsNullOrEmpty(Detail))
                    sb.Append($" ({Detail})");
                sb.Append(" — live scene object, not a file. Use MCP find_gameobjects by_name or by_path to get its instance ID.");
            }
            else
            {
                sb.Append($"[{TypeLabel}: {Path}]");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Handles drag-and-drop of GameObjects, prefabs, scripts, and other assets
    /// into the Claude Code input area, producing compact Attachment references.
    /// </summary>
    public static class DragDropHandler
    {
        /// <summary>
        /// Register drag-and-drop callbacks on a target element.
        /// Calls onAttach for each dropped object instead of modifying the input field.
        /// </summary>
        public static void Register(VisualElement dropTarget, Label statusLabel, Action<Attachment> onAttach)
        {
            dropTarget.RegisterCallback<DragEnterEvent>(_ =>
            {
                dropTarget.AddToClassList("drop-hover");
                if (statusLabel != null)
                    statusLabel.text = "Drop to attach\u2026";
            });

            dropTarget.RegisterCallback<DragLeaveEvent>(_ =>
            {
                dropTarget.RemoveFromClassList("drop-hover");
                if (statusLabel != null)
                    statusLabel.text = "Ready";
            });

            dropTarget.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            });

            dropTarget.RegisterCallback<DragPerformEvent>(evt =>
            {
                dropTarget.RemoveFromClassList("drop-hover");
                if (statusLabel != null)
                    statusLabel.text = "Ready";

                DragAndDrop.AcceptDrag();

                var objects = DragAndDrop.objectReferences;
                if (objects == null || objects.Length == 0) return;

                foreach (var obj in objects)
                {
                    if (obj == null) continue;
                    var attachment = CreateAttachment(obj);
                    onAttach?.Invoke(attachment);
                }
            });
        }

        static Attachment CreateAttachment(UnityEngine.Object obj)
        {
            var assetPath = AssetDatabase.GetAssetPath(obj);

            if (obj is MonoScript script)
            {
                return new Attachment
                {
                    DisplayName = script.name + ".cs",
                    Path = assetPath,
                    TypeLabel = "Script"
                };
            }

            if (obj is GameObject go)
            {
                bool isPrefab = !string.IsNullOrEmpty(assetPath);
                if (isPrefab)
                {
                    return new Attachment
                    {
                        DisplayName = go.name,
                        Path = assetPath,
                        TypeLabel = "Prefab"
                    };
                }

                // Scene GameObject — not a file, give Claude component summary
                var components = go.GetComponents<Component>();
                var names = new List<string>();
                foreach (var c in components)
                {
                    if (c == null) continue;
                    var typeName = c.GetType().Name;
                    if (typeName != "Transform") names.Add(typeName);
                }
                var detail = names.Count > 0 ? "Components: " + string.Join(", ", names) : null;

                return new Attachment
                {
                    DisplayName = go.name,
                    Path = GetHierarchyPath(go.transform),
                    TypeLabel = "GameObject",
                    Detail = detail,
                    IsSceneObject = true
                };
            }

            // Generic asset
            return new Attachment
            {
                DisplayName = obj.name,
                Path = assetPath,
                TypeLabel = obj.GetType().Name
            };
        }

        static string GetHierarchyPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            // No leading slash — matches MCP's GameObjectLookup.GetGameObjectPath() format
            return string.Join("/", parts);
        }
    }
}
