using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Threading;
using System.Threading.Tasks;

namespace TEngine.Editor.ExcelTool
{
    public class ExcelDataWindow : EditorWindow
    {
        // ─── 数据 ───
        private List<FileMetadata> allFiles = new();
        private HashSet<string> selectedKeys = new();
        private Dictionary<string, bool> expandedStates = new();
        private List<FileMetadata> displayFiles = new();
        private HashSet<string> visibleSheetKeys = new();
        private string searchText = "";
        private Vector2 scrollPosition;

        // ─── 异步扫描状态 ───
        private HashSet<string> _pendingPaths = new();
        private Dictionary<string, int> _fileIndex = new();
        private volatile int _scanDone;
        private volatile int _scanTotal;
        private bool _isScanning;
        private readonly object _scanLock = new();
        private List<FileMetadata> _bgBuffer = new();
        private CancellationTokenSource _scanCts;

        // ─── OnGUI 缓存 ───
        private List<string> _cachedDupNames = new();
        private bool _cachedAllSelected;
        private bool _selectionDirty = true;

        [MenuItem("Tools/配置表工具/数据生成窗口")]
        public static void ShowWindow()
        {
            GetWindow<ExcelDataWindow>("Excel Data Tool");
        }

        private void OnEnable()
        {
            QuickRefresh();
            StartBackgroundScan();
        }

        private void OnDisable()
        {
            _scanCts?.Cancel();
            _isScanning = false;
            EditorApplication.update -= PollScanProgress;
        }

        // ─── 刷新逻辑 ───

        /// <summary>
        /// 快速刷新：只读文件列表 + 缓存，不扫描 Excel 内容（< 50ms）
        /// </summary>
        private void QuickRefresh()
        {
            var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
            var cachedFiles = SheetMetadataCache.Load();

            var cacheMap = new Dictionary<string, FileMetadata>();
            foreach (var f in cachedFiles)
                cacheMap[f.FilePath] = f;

            var filePaths = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                .Where(s => (s.EndsWith(".xls") || s.EndsWith(".xlsx")) && !Path.GetFileName(s).StartsWith("~"))
                .ToArray();

            allFiles.Clear();
            _pendingPaths.Clear();
            _fileIndex.Clear();

            for (int i = 0; i < filePaths.Length; i++)
            {
                var path = filePaths[i];
                var lw = File.GetLastWriteTimeUtc(path).Ticks;

                if (cacheMap.TryGetValue(path, out var cached) && cached.LastWriteTime == lw)
                {
                    allFiles.Add(cached);
                }
                else
                {
                    allFiles.Add(new FileMetadata
                    {
                        FileName = Path.GetFileName(path),
                        FilePath = path,
                        LastWriteTime = lw
                    });
                    _pendingPaths.Add(path);
                }
                _fileIndex[path] = i;
            }

            SheetMetadataCache.DetectDuplicates(allFiles);
            CleanupSelection();
            ApplySearchFilter();
        }

        /// <summary>
        /// 启动后台线程扫描未缓存的 Excel 文件
        /// </summary>
        private void StartBackgroundScan()
        {
            if (_pendingPaths.Count == 0) return;

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();

            _isScanning = true;
            _scanDone = 0;
            _scanTotal = _pendingPaths.Count;
            _bgBuffer = new List<FileMetadata>();

            var paths = _pendingPaths.ToList();
            var token = _scanCts.Token;

            Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (token.IsCancellationRequested) return;
                    var meta = SheetMetadataScanner.ScanFile(path);
                    lock (_scanLock)
                    {
                        _bgBuffer.Add(meta);
                    }
                    Interlocked.Increment(ref _scanDone);
                }
            }, token);

            EditorApplication.update += PollScanProgress;
        }

        /// <summary>
        /// 主线程轮询：合并后台扫描结果并刷新 UI
        /// </summary>
        private void PollScanProgress()
        {
            if (!_isScanning) return;

            List<FileMetadata> batch;
            lock (_scanLock)
            {
                batch = new List<FileMetadata>(_bgBuffer);
                _bgBuffer.Clear();
            }

            foreach (var scanned in batch)
            {
                if (_fileIndex.TryGetValue(scanned.FilePath, out int idx))
                {
                    allFiles[idx] = scanned;
                    _pendingPaths.Remove(scanned.FilePath);
                }
            }

            if (batch.Count > 0)
            {
                SheetMetadataCache.DetectDuplicates(allFiles);
                ApplySearchFilter();
            }

            Repaint();

            if (_scanDone >= _scanTotal)
            {
                EditorApplication.update -= PollScanProgress;
                _isScanning = false;
                SheetMetadataCache.Save(allFiles);
                CleanupSelection();
                ApplySearchFilter();
                Repaint();
            }
        }

        /// <summary>
        /// 手动刷新按钮回调
        /// </summary>
        private void RefreshSheetList()
        {
            QuickRefresh();
            StartBackgroundScan();
        }

        private void CleanupSelection()
        {
            var validKeys = new HashSet<string>();
            foreach (var f in allFiles)
                foreach (var s in f.Sheets)
                    if (s.IsValid)
                        validKeys.Add(MakeKey(f.FileName, s.Name));
            selectedKeys.RemoveWhere(k => !validKeys.Contains(k));
        }

        private static string MakeKey(string fileName, string sheetName) => $"{fileName}|{sheetName}";

        private ExcelExportContext CreateExportContext()
        {
            var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
            var outputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath);
            var languagePath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath, "Localization.xlsx");
            // 共享缓存优化始终启用——SharedCacheAnalyzer 内部按五道闸门自动判断每个字段是否值得优化。
            return new ExcelExportContext(inputPath, outputPath, languagePath, useSharedCache: true);
        }

        // ─── 搜索过滤 ───
        private void ApplySearchFilter()
        {
            visibleSheetKeys.Clear();
            displayFiles.Clear();
            _cachedDupNames = SheetMetadataCache.GetDuplicateNames(allFiles);
            _selectionDirty = true;

            if (string.IsNullOrEmpty(searchText))
            {
                displayFiles = allFiles
                    .Where(f => _pendingPaths.Contains(f.FilePath)
                             || f.Sheets.Any(s => s.IsValid)
                             || !string.IsNullOrEmpty(f.ScanError))
                    .ToList();
                foreach (var f in displayFiles)
                    foreach (var s in f.Sheets.Where(s => s.IsValid))
                        visibleSheetKeys.Add(MakeKey(f.FileName, s.Name));
                return;
            }

            foreach (var f in allFiles)
            {
                bool fileNameMatch = f.FileName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;

                // Loading 中的文件只按文件名匹配
                if (_pendingPaths.Contains(f.FilePath))
                {
                    if (fileNameMatch)
                    {
                        displayFiles.Add(f);
                        if (!expandedStates.ContainsKey(f.FilePath))
                            expandedStates[f.FilePath] = true;
                    }
                    continue;
                }

                var matchingSheets = f.Sheets
                    .Where(s => s.IsValid && (fileNameMatch || s.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                if (matchingSheets.Count > 0)
                {
                    displayFiles.Add(f);
                    foreach (var s in matchingSheets)
                        visibleSheetKeys.Add(MakeKey(f.FileName, s.Name));
                    if (!expandedStates.ContainsKey(f.FilePath))
                        expandedStates[f.FilePath] = true;
                }
            }
        }

        private bool IsSheetVisible(string fileName, string sheetName)
            => visibleSheetKeys.Contains(MakeKey(fileName, sheetName));

        // ─── 选择逻辑 ───
        private bool IsFileAllSelected(FileMetadata file)
            => file.Sheets.Where(s => s.IsValid).All(s => selectedKeys.Contains(MakeKey(file.FileName, s.Name)));

        private bool IsFilePartiallySelected(FileMetadata file)
        {
            var validSheets = file.Sheets.Where(s => s.IsValid).ToList();
            int selected = validSheets.Count(s => selectedKeys.Contains(MakeKey(file.FileName, s.Name)));
            return selected > 0 && selected < validSheets.Count;
        }

        private void SetFileSelection(FileMetadata file, bool selected)
        {
            foreach (var s in file.Sheets.Where(s => s.IsValid))
            {
                var key = MakeKey(file.FileName, s.Name);
                if (selected) selectedKeys.Add(key);
                else selectedKeys.Remove(key);
            }
            _selectionDirty = true;
        }

        private void ToggleAllSelection()
        {
            if (_selectionDirty)
            {
                _cachedAllSelected = displayFiles.Count > 0 && displayFiles.All(f => IsFileAllSelected(f));
                _selectionDirty = false;
            }
            bool allSelected = _cachedAllSelected;

            foreach (var f in displayFiles)
                SetFileSelection(f, !allSelected);
        }

        // ─── 导出操作 ───
        private void ExportSingleSheet(FileMetadata file, SheetMetadata sheet)
        {
            var ctx = CreateExportContext();
            ExcelDataTool.GenerateSingleSheet(ctx, file.FileName, sheet.Name, false);
            AssetDatabase.Refresh();
        }

        private void ExportAllSheets(FileMetadata file)
        {
            var ctx = CreateExportContext();
            ExcelDataTool.GenerateSelectedSheets(ctx, new List<string> { file.FileName }, false);
            AssetDatabase.Refresh();
        }

        private void GenerateSelected()
        {
            var fileToSheets = new Dictionary<string, HashSet<string>>();
            foreach (var key in selectedKeys)
            {
                var idx = key.IndexOf('|');
                if (idx < 0) continue;
                var fileName = key.Substring(0, idx);
                var sheetName = key.Substring(idx + 1);
                if (!fileToSheets.ContainsKey(fileName))
                    fileToSheets[fileName] = new HashSet<string>();
                fileToSheets[fileName].Add(sheetName);
            }

            if (fileToSheets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请至少选择一个数据表", "确定");
                return;
            }

            var fileNamesToExport = new List<string>();
            var singleSheetExports = new List<(string fileName, string sheetName)>();

            foreach (var kvp in fileToSheets)
            {
                var file = allFiles.FirstOrDefault(f => f.FileName == kvp.Key);
                if (file == null) continue;
                var allValidNames = new HashSet<string>(file.Sheets.Where(s => s.IsValid).Select(s => s.Name));
                if (kvp.Value.SetEquals(allValidNames))
                    fileNamesToExport.Add(kvp.Key);
                else
                    foreach (var sheetName in kvp.Value)
                        singleSheetExports.Add((kvp.Key, sheetName));
            }

            if (fileNamesToExport.Count > 0)
            {
                var ctx = CreateExportContext();
                ExcelDataTool.GenerateSelectedSheets(ctx, fileNamesToExport, false);
            }

            foreach (var (fileName, sheetName) in singleSheetExports)
            {
                var singleCtx = CreateExportContext();
                ExcelDataTool.GenerateSingleSheet(singleCtx, fileName, sheetName, false);
            }

            AssetDatabase.Refresh();
        }

        private void OpenBinaryConfigDir()
        {
            var dir = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath);
            if (Directory.Exists(dir))
                Application.OpenURL($"file://{dir}");
            else
                EditorUtility.DisplayDialog("错误", "二进制配置目录不存在", "确定");
        }

        private void OpenCodeConfigDir()
        {
            var dir = Path.Combine(Application.dataPath, ExcelExportContext.ScriptFolder);
            if (Directory.Exists(dir))
                Application.OpenURL($"file://{dir}");
            else
                EditorUtility.DisplayDialog("错误", "配置代码目录不存在", "确定");
        }

        // ─── OnGUI ───
        private void OnGUI()
        {
            // ── 搜索栏 ──
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("SearchField");

            var newSearch = GUILayout.TextField(searchText, EditorStyles.toolbarTextField, GUILayout.Width(500));
            searchText = newSearch;

            // Enter 键触发搜索
            var ev = Event.current;
            if (ev.isKey && ev.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "SearchField")
            {
                ApplySearchFilter();
                ev.Use();
            }

            // 搜索按钮触发搜索
            if (GUILayout.Button("搜索", EditorStyles.toolbarButton))
            {
                ApplySearchFilter();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── 重名警告（使用缓存）──
            if (_cachedDupNames.Count > 0)
            {
                var dupText = $"⚠ 检测到 {_cachedDupNames.Count} 个重名 sheet: {string.Join(", ", _cachedDupNames)}";
                EditorGUILayout.HelpBox(dupText, MessageType.Warning);
            }

            GUILayout.Space(4);

            // ── 工具栏 ──
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新列表", EditorStyles.toolbarButton))
                RefreshSheetList();

            // 全选 checkbox（使用缓存）
            EditorGUI.BeginChangeCheck();
            if (_selectionDirty)
            {
                _cachedAllSelected = displayFiles.Count > 0 && displayFiles.All(f => IsFileAllSelected(f));
                _selectionDirty = false;
            }
            var topLevelAllSelected = _cachedAllSelected;
            var topNewState = EditorGUILayout.Toggle(topLevelAllSelected, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck() && topLevelAllSelected != topNewState)
                ToggleAllSelection();
            GUILayout.Label("全选", GUILayout.Width(36));

            if (GUILayout.Button("生成选中项", EditorStyles.toolbarButton))
                GenerateSelected();

            if (GUILayout.Button("打开配置导出目录", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                OpenBinaryConfigDir();
                OpenCodeConfigDir();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── 表头 ──
            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("选择", GUILayout.Width(50));
            GUILayout.Label("名称");
            GUILayout.Label("", GUILayout.Width(140));
            GUILayout.EndHorizontal();

            // ── 树形列表 ──
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            foreach (var file in displayFiles)
            {
                // Loading 中的文件
                if (_pendingPaths.Contains(file.FilePath))
                {
                    EditorGUI.BeginDisabledGroup(true);
                    GUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.Toggle(false, GUILayout.Width(30));
                    GUILayout.Label($"⏳ {file.FileName}", EditorStyles.label);
                    GUILayout.Label("扫描中...", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                    EditorGUI.EndDisabledGroup();
                    continue;
                }

                // 错误文件
                if (!string.IsNullOrEmpty(file.ScanError) && !file.Sheets.Any(s => s.IsValid))
                {
                    var prevColor = GUI.contentColor;
                    GUI.contentColor = Color.yellow;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"⚠ {file.FileName}");
                    GUILayout.Label($"({file.ScanError})", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                    GUI.contentColor = prevColor;
                    continue;
                }

                var validSheets = file.Sheets.Where(s => s.IsValid).ToList();

                // 父节点
                bool wasExpanded = expandedStates.GetValueOrDefault(file.FilePath, false);

                GUILayout.BeginHorizontal(EditorStyles.helpBox);

                bool allSelected = IsFileAllSelected(file);
                EditorGUI.showMixedValue = !allSelected && IsFilePartiallySelected(file);
                EditorGUI.BeginChangeCheck();
                bool newState = EditorGUILayout.Toggle(allSelected, GUILayout.Width(30));
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                    SetFileSelection(file, newState);

                var newExpanded = EditorGUILayout.Foldout(wasExpanded, $"{file.FileName} ({validSheets.Count})", true);
                expandedStates[file.FilePath] = newExpanded;

                if (GUILayout.Button("导出全部", GUILayout.Width(60)))
                    ExportAllSheets(file);
                if (GUILayout.Button("打开xlsx", GUILayout.Width(60)))
                {
                    if (File.Exists(file.FilePath))
                        System.Diagnostics.Process.Start(file.FilePath);
                }
                if (GUILayout.Button("打开目录", GUILayout.Width(60)))
                {
                    var dir = Path.GetDirectoryName(file.FilePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        EditorUtility.RevealInFinder(file.FilePath);
                }

                GUILayout.EndHorizontal();

                // 子节点
                if (newExpanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var sheet in validSheets)
                    {
                        if (!IsSheetVisible(file.FileName, sheet.Name))
                            continue;

                        var key = MakeKey(file.FileName, sheet.Name);
                        bool isSelected = selectedKeys.Contains(key);

                        GUILayout.BeginHorizontal();

                        if (sheet.IsDuplicate)
                            GUI.contentColor = Color.red;

                        EditorGUI.BeginChangeCheck();
                        isSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(30));
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (isSelected) selectedKeys.Add(key);
                            else selectedKeys.Remove(key);
                            _selectionDirty = true;
                        }

                        GUILayout.Label(sheet.Name);

                        if (GUILayout.Button("导出", GUILayout.Width(60)))
                            ExportSingleSheet(file, sheet);

                        if (sheet.IsDuplicate)
                            GUI.contentColor = Color.white;

                        GUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }
            }

            GUILayout.EndScrollView();

            // ── 扫描进度条 ──
            if (_isScanning)
            {
                var progress = _scanTotal > 0 ? (float)_scanDone / _scanTotal : 0f;
                EditorGUI.ProgressBar(
                    GUILayoutUtility.GetRect(0, 20),
                    progress,
                    $"正在扫描配置表... {_scanDone}/{_scanTotal}");
            }
        }
    }
}