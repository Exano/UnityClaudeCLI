using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ClaudeCode.Editor
{
    /// <summary>
    /// Utility window for browsing an external repository and attaching files to the conversation.
    /// </summary>
    public class FileImportWindow : EditorWindow
    {
        private const string k_StylePath = "Packages/com.tonythedev.unity-claude-code-cli/Editor/UI/ClaudeCodeStyles.uss";
        private const string k_PrefKey = "ClaudeCode_ImportPath";
        private static readonly string[] k_Extensions = { ".cs", ".uss", ".uxml", ".shader", ".hlsl", ".cginc", ".json", ".md", ".txt", ".xml", ".yaml", ".yml", ".asmdef" };

        private Action<Attachment> _onAttach;
        private string _repoPath;
        private string _filterText = "";
        private List<FileEntry> _allFiles = new List<FileEntry>();
        private List<FileEntry> _filteredFiles = new List<FileEntry>();
        private HashSet<string> _selected = new HashSet<string>();
        private ScrollView _fileList;
        private Label _countLabel;

        private struct FileEntry
        {
            public string RelativePath;
            public string FullPath;
            public string FileName;
            public string Directory;
        }

        public static void Show(Action<Attachment> onAttach)
        {
            var window = GetWindow<FileImportWindow>(utility: true, title: "Import Files", focus: true);
            window._onAttach = onAttach;
            window._repoPath = EditorPrefs.GetString(k_PrefKey, @"M:\VHornet\Frame Analyzer\Unity_AI_Performance_Analysis");
            window.minSize = new Vector2(420, 450);
            window.maxSize = new Vector2(700, 800);
            window.ScanFiles();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_StylePath);
            if (ss != null) root.styleSheets.Add(ss);
            root.AddToClassList("import-root");

            // Path row
            var pathRow = new VisualElement();
            pathRow.AddToClassList("import-toolbar");
            var pathLabel = new Label("Repo:");
            pathLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            pathLabel.style.fontSize = 11;
            pathLabel.style.marginRight = 4;
            pathRow.Add(pathLabel);
            var pathField = new TextField();
            pathField.AddToClassList("import-path-field");
            pathField.SetValueWithoutNotify(_repoPath ?? "");
            pathField.RegisterValueChangedCallback(e =>
            {
                _repoPath = e.newValue;
                EditorPrefs.SetString(k_PrefKey, _repoPath);
                ScanFiles();
                RebuildFileList();
            });
            pathRow.Add(pathField);
            var browseBtn = new Button(() =>
            {
                var path = EditorUtility.OpenFolderPanel("Select Repository", _repoPath ?? "", "");
                if (!string.IsNullOrEmpty(path))
                {
                    _repoPath = path;
                    EditorPrefs.SetString(k_PrefKey, path);
                    pathField.SetValueWithoutNotify(path);
                    ScanFiles();
                    RebuildFileList();
                }
            }) { text = "..." };
            browseBtn.AddToClassList("import-browse-btn");
            pathRow.Add(browseBtn);
            root.Add(pathRow);

            // Filter row
            var filterRow = new VisualElement();
            filterRow.AddToClassList("import-filter-row");
            var filterLabel = new Label("Filter:");
            filterLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            filterLabel.style.fontSize = 11;
            filterLabel.style.marginRight = 4;
            filterRow.Add(filterLabel);
            var filterField = new TextField();
            filterField.AddToClassList("import-filter-field");
            filterField.RegisterValueChangedCallback(e =>
            {
                _filterText = e.newValue ?? "";
                ApplyFilter();
                RebuildFileList();
            });
            filterRow.Add(filterField);
            _countLabel = new Label("0 files");
            _countLabel.style.color = new Color(0.44f, 0.44f, 0.44f);
            _countLabel.style.fontSize = 10;
            _countLabel.style.marginLeft = 8;
            filterRow.Add(_countLabel);
            root.Add(filterRow);

            // File list
            _fileList = new ScrollView(ScrollViewMode.Vertical);
            _fileList.AddToClassList("import-file-list");
            root.Add(_fileList);

            // Footer
            var footer = new VisualElement();
            footer.AddToClassList("import-footer");
            var selectAllBtn = new Button(() => { SelectAll(true); RebuildFileList(); }) { text = "All" };
            selectAllBtn.AddToClassList("import-select-btn");
            footer.Add(selectAllBtn);
            var selectNoneBtn = new Button(() => { SelectAll(false); RebuildFileList(); }) { text = "None" };
            selectNoneBtn.AddToClassList("import-select-btn");
            footer.Add(selectNoneBtn);
            footer.Add(new VisualElement { style = { flexGrow = 1 } });
            var attachBtn = new Button(OnAttachClicked) { text = "Attach Selected" };
            attachBtn.AddToClassList("import-attach-btn");
            footer.Add(attachBtn);
            root.Add(footer);

            RebuildFileList();
        }

        private void ScanFiles()
        {
            _allFiles.Clear();
            _selected.Clear();
            if (string.IsNullOrEmpty(_repoPath) || !Directory.Exists(_repoPath))
                return;

            var baseDir = _repoPath.Replace('\\', '/').TrimEnd('/');
            foreach (var ext in k_Extensions)
            {
                string[] files;
                try { files = Directory.GetFiles(_repoPath, "*" + ext, SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var file in files)
                {
                    var normalized = file.Replace('\\', '/');
                    var relative = normalized.Substring(baseDir.Length + 1);

                    // Skip hidden folders, bin, obj, Library, Temp
                    if (relative.StartsWith(".") || relative.Contains("/.") ||
                        relative.Contains("/bin/") || relative.Contains("/obj/") ||
                        relative.Contains("/Library/") || relative.Contains("/Temp/"))
                        continue;

                    var dirPart = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
                    _allFiles.Add(new FileEntry
                    {
                        RelativePath = relative,
                        FullPath = normalized,
                        FileName = Path.GetFileName(normalized),
                        Directory = dirPart
                    });
                }
            }

            _allFiles.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _filteredFiles.Clear();
            var filter = _filterText.Trim().ToLowerInvariant();
            foreach (var f in _allFiles)
            {
                if (string.IsNullOrEmpty(filter) || f.RelativePath.ToLowerInvariant().Contains(filter))
                    _filteredFiles.Add(f);
            }
        }

        private void RebuildFileList()
        {
            if (_fileList == null) return;
            _fileList.Clear();

            if (_countLabel != null)
                _countLabel.text = $"{_filteredFiles.Count} files";

            foreach (var file in _filteredFiles)
            {
                var entry = file;
                var row = new VisualElement();
                row.AddToClassList("import-file-item");

                var toggle = new Toggle();
                toggle.AddToClassList("import-file-toggle");
                toggle.SetValueWithoutNotify(_selected.Contains(entry.FullPath));
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _selected.Add(entry.FullPath);
                    else _selected.Remove(entry.FullPath);
                });
                row.Add(toggle);

                var nameLabel = new Label(entry.FileName);
                nameLabel.AddToClassList("import-file-label");
                row.Add(nameLabel);

                if (!string.IsNullOrEmpty(entry.Directory))
                {
                    var dirLabel = new Label(entry.Directory);
                    dirLabel.AddToClassList("import-file-dir");
                    row.Add(dirLabel);
                }

                _fileList.Add(row);
            }
        }

        private void SelectAll(bool select)
        {
            _selected.Clear();
            if (select)
            {
                foreach (var f in _filteredFiles)
                    _selected.Add(f.FullPath);
            }
        }

        private void OnAttachClicked()
        {
            if (_onAttach == null || _selected.Count == 0) return;

            foreach (var path in _selected)
            {
                var fileName = Path.GetFileName(path);
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                string typeLabel = ext switch
                {
                    ".cs" => "Script",
                    ".shader" or ".hlsl" or ".cginc" => "Shader",
                    ".uss" => "Style",
                    ".uxml" => "Layout",
                    ".json" or ".yaml" or ".yml" or ".xml" => "Data",
                    ".md" or ".txt" => "Doc",
                    ".asmdef" => "Assembly",
                    _ => "File"
                };

                _onAttach.Invoke(new Attachment
                {
                    DisplayName = fileName,
                    Path = path,
                    TypeLabel = typeLabel,
                    IsSceneObject = false
                });
            }

            _selected.Clear();
            Close();
        }
    }
}
