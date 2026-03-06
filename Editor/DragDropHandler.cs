using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ClaudeCode.Editor
{
    /// <summary>
    /// Represents a dragged asset/object as a compact reference.
    /// </summary>
    public struct Attachment
    {
        public string DisplayName; // e.g. "PlayerController.cs"
        public string Path;        // e.g. "Assets/Scripts/PlayerController.cs"
        public string TypeLabel;   // e.g. "Script", "Prefab", "GameObject", "Material"

        /// <summary>
        /// Compact one-line reference for the prompt. Claude can Read/Grep the file.
        /// </summary>
        public string ToPromptReference()
        {
            if (!string.IsNullOrEmpty(Path))
                return $"[{TypeLabel}: {Path}]";
            return $"[{TypeLabel}: {DisplayName}]";
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

                // Scene GameObject — no file path, include hierarchy
                return new Attachment
                {
                    DisplayName = go.name,
                    Path = GetHierarchyPath(go.transform),
                    TypeLabel = "GameObject"
                };
            }

            // Generic asset
            var typeName = obj.GetType().Name;
            return new Attachment
            {
                DisplayName = obj.name,
                Path = assetPath,
                TypeLabel = typeName
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
            return "/" + string.Join("/", parts);
        }
    }
}
