using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 配置表 bytes 反向查看器：加载导出的二进制配置（Configs/Bytes 与 Configs/LanguageBytes），
    /// 复用运行时生成类解码，以表格形式展示与 Excel 源一致的数据。支持值搜索、行详情、JSON/CSV 导出与拖拽查看。
    /// </summary>
    public class ConfigBytesViewerWindow : EditorWindow
    {
        private const string MenuPath = "Tools/配置表工具/Bytes数据查看器";
        private const string LanguagePrefKey = "ConfigBytesViewer.Language";
        private const string LegendPrefKey = "ConfigBytesViewer.ShowLegend";
        private const string DefaultLanguageName = "ChineseSimplified";
        private const float RowHeight = 18f;
        private const float HeaderHeight = 22f;
        private const float ListWidth = 280f;
        private const float DetailHeight = 180f;

        [MenuItem(MenuPath, false, 310)]
        private static void Open()
        {
            var window = GetWindow<ConfigBytesViewerWindow>(false, "Bytes数据查看器");
            window.minSize = new Vector2(960f, 540f);
        }

        /// <summary>左侧列表条目。</summary>
        private sealed class TableEntry
        {
            public string Path;
            public string Name;
            public bool IsLanguage;
        }

        private readonly List<TableEntry> _tables = new List<TableEntry>();
        private readonly List<int> _filteredIndices = new List<int>();
        private string _tableFilter = string.Empty;
        private int _selectedIndex = -1;

        private string[] _languageFiles = Array.Empty<string>();
        private string[] _languageNames = Array.Empty<string>();
        private int _languageIndex = -1;
        private string _injectedLanguageFile;

        private DecodedTable _decoded;
        private string _decodedPath;
        private string _decodedError;
        private string _decodedHex;

        private string _valueFilter = string.Empty;
        private List<int> _visibleRows;
        private int _selectedRow = -1;
        private string _detailJson = string.Empty;

        private Vector2 _listScroll;
        private Vector2 _gridScroll;
        private Vector2 _detailScroll;
        private float[] _colWidths;
        private int _resizeCol = -1;
        private float _resizeStartX;
        private float _resizeStartWidth;
        private GUIStyle _cellStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _diffEntryStyle;
        private bool _stylesDirty = true;

        // Excel 比对状态
        private int _activeTab;
        private TableDiffResult _diff;
        private bool _diffActive;
        private Dictionary<int, Dictionary<int, CellDiff>> _cellDiffsByRow = new Dictionary<int, Dictionary<int, CellDiff>>();
        /// <summary>bytes 独有行（Excel 已删除该 key）在当前表中的行下标集，数据网格整行红底用。</summary>
        private readonly HashSet<int> _bytesOnlyRowIndices = new HashSet<int>();
        private bool _onlyChangedRows;
        private bool _onlyChangedTables;
        private readonly Dictionary<string, TableDiffResult> _allDiffs = new Dictionary<string, TableDiffResult>();
        private readonly Dictionary<string, Color> _dotColorCache = new Dictionary<string, Color>();
        private Vector2 _diffTabScroll;
        private float _diffTabContentHeight = 60f;
        private int _diffSectionSelected = -1;
        private int _diffEntrySelected = -1;

        // 键盘导航与图例
        private int _activePane;
        private bool _showLegend;
        private float _listViewHeight = 200f;
        private float _gridViewHeight = 200f;

        private static readonly Color HeaderBgColor = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color KeyColumnColor = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color SelectRowColor = new Color(0.24f, 0.48f, 0.9f, 0.4f);
        private static readonly Color SelectRowTextColor = new Color(0.7f, 0.85f, 1f);
        // 差异着色（暗 skin 低饱和、半透明叠加，文字保持默认前景色）
        private static readonly Color DiffCellColor = new Color(1f, 0.72f, 0.1f, 0.30f);
        private static readonly Color DiffDeletedRowColor = new Color(0.9f, 0.35f, 0.3f, 0.22f);
        private static readonly Color DiffDotChanged = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color DiffDotAdded = new Color(0.4f, 0.8f, 0.4f);
        private static readonly Color DiffDotDeleted = new Color(0.9f, 0.35f, 0.3f);
        private static readonly Color DiffDotMixed = new Color(0.95f, 0.55f, 0.2f);
        private static readonly Color DiffDotStale = new Color(0.5f, 0.5f, 0.5f);

        private void OnEnable()
        {
            _showLegend = EditorPrefs.GetBool(LegendPrefKey, false);
            RefreshFileList();
            InitLanguages();
            EnsureLanguageInjected();
        }

        private void OnGUI()
        {
            // 样式为空也要重建：域重载后 _stylesDirty 可能被序列化残留为 false，新加样式会漏初始化
            if (_stylesDirty || _cellStyle == null || _headerStyle == null || _detailStyle == null || _diffEntryStyle == null)
            {
                BuildStyles();
                _stylesDirty = false;
            }
            HandleDragAndDrop();
            HandleKeyboardNavigation();
            DrawToolbar();
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("当前处于 Play 模式：语言表注入会覆盖游戏运行时已加载的语言配置，请留意。", MessageType.Warning);
            }
            if (_showLegend)
            {
                DrawLegend();
            }

            // 主体区在根级布局占满剩余空间，再手动切分左右两栏（绝对矩形，窗口缩放自适应）
            var bodyRect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_tables.Count == 0)
            {
                EditorGUI.HelpBox(bodyRect,
                    $"未找到任何 .bytes 文件。\n配置表目录: {ConfigBytesDecoder.GetTableBytesDir()}\n请确认已执行 Tools/配置表工具/生成全部数据。",
                    MessageType.Warning);
                return;
            }
            var listRect = new Rect(bodyRect.x, bodyRect.y, ListWidth, bodyRect.height);
            var gridRect = new Rect(bodyRect.x + ListWidth, bodyRect.y, Mathf.Max(0f, bodyRect.width - ListWidth), bodyRect.height);
            DrawTableList(listRect);
            DrawGrid(gridRect);
            DrawDetail();
        }

        #region 数据准备

        private void RefreshFileList()
        {
            _tables.Clear();
            foreach (var path in ConfigBytesDecoder.ScanTableFiles())
            {
                var name = Path.GetFileNameWithoutExtension(path);
                _tables.Add(new TableEntry
                {
                    Path = path,
                    Name = name,
                    IsLanguage = name.StartsWith("Language_", StringComparison.Ordinal),
                });
            }
            ApplyTableFilter();
            ClearDecoded();
            _selectedIndex = -1;
        }

        private void InitLanguages()
        {
            _languageFiles = ConfigBytesDecoder.ScanLanguageFiles().ToArray();
            _languageNames = _languageFiles.Select(ConfigBytesDecoder.GetLanguageDisplayName).ToArray();
            var saved = EditorPrefs.GetString(LanguagePrefKey, string.Empty);
            _languageIndex = Array.IndexOf(_languageNames, saved);
            if (_languageIndex < 0 && _languageNames.Length > 0)
            {
                var fallback = Array.IndexOf(_languageNames, DefaultLanguageName);
                _languageIndex = fallback >= 0 ? fallback : 0;
            }
            _injectedLanguageFile = null;
        }

        private void ApplyTableFilter()
        {
            _filteredIndices.Clear();
            for (var i = 0; i < _tables.Count; i++)
            {
                if (!string.IsNullOrEmpty(_tableFilter) &&
                    _tables[i].Name.IndexOf(_tableFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (_onlyChangedTables && _allDiffs.Count > 0)
                {
                    if (!_allDiffs.TryGetValue(_tables[i].Name, out var r) || !r.HasChange)
                    {
                        continue;
                    }
                }
                _filteredIndices.Add(i);
            }
        }

        private void SelectTable(int index)
        {
            if (_selectedIndex == index && _decoded != null)
            {
                return;
            }
            _selectedIndex = index;
            EnsureLanguageInjected();
            DecodeSelected();
        }

        private void ClearDecoded()
        {
            _decoded = null;
            _decodedPath = null;
            _decodedError = null;
            _decodedHex = null;
            _visibleRows = null;
            _selectedRow = -1;
            _detailJson = string.Empty;
            _colWidths = null;
            _gridScroll = Vector2.zero;
            ClearDiffState();
        }

        /// <summary>清除当前表的比对状态（切换表/切语言/刷新时）。</summary>
        private void ClearDiffState()
        {
            _diff = null;
            _diffActive = false;
            _activeTab = 0;
            _cellDiffsByRow.Clear();
            _bytesOnlyRowIndices.Clear();
            _onlyChangedRows = false;
            _diffTabScroll = Vector2.zero;
            _diffTabContentHeight = 60f;
            _diffSectionSelected = -1;
            _diffEntrySelected = -1;
        }

        private void DecodeSelected()
        {
            ClearDecoded();
            if (_selectedIndex < 0 || _selectedIndex >= _tables.Count)
            {
                return;
            }
            var entry = _tables[_selectedIndex];
            ApplyDecoded(ConfigBytesDecoder.Decode(entry.Path));
            // 已有比对缓存且未失效时自动恢复着色
            if (_decoded != null && _allDiffs.TryGetValue(entry.Name, out var cached) && !IsDiffStale(cached))
            {
                ApplyDiff(cached);
            }
        }

        private void ApplyDecoded(DecodedTable table)
        {
            _decoded = table.Ok ? table : null;
            _decodedPath = table.FilePath;
            _decodedError = table.Error;
            _decodedHex = table.HexPreview;
            if (table.Ok)
            {
                ComputeColumnWidths(table);
                _visibleRows = null;
                _gridScroll = Vector2.zero;
            }
            Repaint();
        }

        private void InvalidateDecoded()
        {
            // 语言切换后 desc 类属性文本会变化，当前表需要重解码
            if (_selectedIndex >= 0 && _selectedIndex < _tables.Count)
            {
                DecodeSelected();
            }
        }

        private void EnsureLanguageInjected()
        {
            if (_languageFiles.Length == 0 || _languageIndex < 0 || _languageIndex >= _languageFiles.Length)
            {
                return;
            }
            var file = _languageFiles[_languageIndex];
            if (_injectedLanguageFile == file)
            {
                return;
            }
            if (ConfigBytesDecoder.InjectLanguage(file, out var error))
            {
                _injectedLanguageFile = file;
            }
            else
            {
                Debug.LogError($"[BytesViewer] 注入语言表失败: {error}");
            }
        }

        #endregion

        #region UI 绘制

        private void BuildStyles()
        {
            _cellStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0),
            };
            _headerStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0),
            };
            _detailStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            _diffEntryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(2, 2, 0, 0),
            };
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (_languageNames.Length > 0)
            {
                EditorGUILayout.LabelField("多语言:", EditorStyles.miniLabel, GUILayout.Width(48));
                var newIndex = EditorGUILayout.Popup(_languageIndex, _languageNames, EditorStyles.toolbarPopup, GUILayout.Width(150));
                if (newIndex != _languageIndex)
                {
                    _languageIndex = newIndex;
                    EditorPrefs.SetString(LanguagePrefKey, _languageNames[_languageIndex]);
                    _injectedLanguageFile = null;
                    EnsureLanguageInjected();
                    InvalidateDecoded();
                }
            }
            else
            {
                EditorGUILayout.LabelField("多语言: 未找到 LanguageBytes 目录", EditorStyles.miniLabel);
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44)))
            {
                RefreshFileList();
                InitLanguages();
                EnsureLanguageInjected();
            }
            if (GUILayout.Button("校验全部", EditorStyles.toolbarButton, GUILayout.Width(64)))
            {
                ValidateAll();
            }
            if (GUILayout.Button("Excel比对·全表", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                CompareAllTables();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("打开目录", EditorStyles.toolbarButton, GUILayout.Width(64)))
            {
                EditorUtility.RevealInFinder(ConfigBytesDecoder.GetTableBytesDir());
            }
            var showLegend = GUILayout.Toggle(_showLegend, "?", EditorStyles.toolbarButton, GUILayout.Width(24));
            if (showLegend != _showLegend)
            {
                _showLegend = showLegend;
                EditorPrefs.SetBool(LegendPrefKey, _showLegend);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>颜色与操作说明面板（工具栏 ? 开关）。</summary>
        private void DrawLegend()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("颜色说明", EditorStyles.boldLabel);
            DrawLegendSwatchRow(DiffCellColor, "网格黄底 —— 该单元格的值与 Excel 不同（悬停看对照，点击看详情，双击定位源表）", true);
            DrawLegendSwatchRow(DiffDeletedRowColor, "网格红底 —— 该行 bytes 有、Excel 无（Excel 已删除该 key；改 key 会同时报增行+删行）", true);
            DrawLegendDotRow(DiffDotChanged, "黄点 —— 该表存在值修改");
            DrawLegendDotRow(DiffDotAdded, "绿点 —— 该表存在新增行/列（Excel 有、bytes 无）");
            DrawLegendDotRow(DiffDotDeleted, "红点 —— 该表存在删除行/列（bytes 有、Excel 无）");
            DrawLegendDotRow(DiffDotMixed, "橙点 —— 该表既有新增又有删除");
            DrawLegendDotRow(DiffDotStale, "灰点 —— 比对结果已过期（源表在比对后又被修改，建议重新比对）");
            EditorGUILayout.LabelField("↑/↓ 方向键：在表列表或数据网格中移动选中项（点击哪一侧即锁定哪一侧）", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("拖住表头分隔线可调整列宽；比对后可用「仅看差异行/仅看有改动」快速过滤", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private static void DrawLegendDotRow(Color color, string text)
        {
            EditorGUILayout.BeginHorizontal();
            var rect = GUILayoutUtility.GetRect(18f, 14f, GUILayout.Width(18f));
            EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y + 3f, 9f, 9f), color);
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLegendSwatchRow(Color overlay, string text, bool onDark)
        {
            EditorGUILayout.BeginHorizontal();
            var rect = GUILayoutUtility.GetRect(18f, 14f, GUILayout.Width(18f));
            var swatch = new Rect(rect.x + 2f, rect.y + 2f, 14f, 10f);
            if (onDark)
            {
                EditorGUI.DrawRect(swatch, HeaderBgColor);
            }
            EditorGUI.DrawRect(swatch, overlay);
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableList(Rect rect)
        {
            // 过滤行
            var labelWidth = 60f;
            GUI.Label(new Rect(rect.x, rect.y + 1f, labelWidth, 16f), $"表({_filteredIndices.Count}):", EditorStyles.miniLabel);
            GUI.SetNextControlName("TableFilter");
            var newFilter = GUI.TextField(new Rect(rect.x + labelWidth, rect.y, Mathf.Max(0f, rect.width - labelWidth - 76f), 16f), _tableFilter);
            if (newFilter != _tableFilter)
            {
                _tableFilter = newFilter;
                ApplyTableFilter();
            }
            if (_allDiffs.Count > 0)
            {
                var onlyChanged = GUI.Toggle(new Rect(rect.xMax - 74f, rect.y, 74f, 16f), _onlyChangedTables, "仅看有改动", EditorStyles.miniButton);
                if (onlyChanged != _onlyChangedTables)
                {
                    _onlyChangedTables = onlyChanged;
                    ApplyTableFilter();
                }
            }

            var listTop = rect.y + 20f;
            var listRect = new Rect(rect.x, listTop, rect.width, Mathf.Max(0f, rect.height - (listTop - rect.y)));
            _listViewHeight = listRect.height;
            var contentWidth = Mathf.Max(0f, listRect.width - 16f);
            _listScroll = GUI.BeginScrollView(listRect, _listScroll, new Rect(0f, 0f, contentWidth, _filteredIndices.Count * RowHeight));
            var first = Mathf.Max(0, (int)(_listScroll.y / RowHeight) - 1);
            var last = Mathf.Min(_filteredIndices.Count, first + Mathf.CeilToInt(listRect.height / RowHeight) + 2);
            for (var vi = first; vi < last; vi++)
            {
                var index = _filteredIndices[vi];
                var entry = _tables[index];
                var rowRect = new Rect(0f, vi * RowHeight, contentWidth, RowHeight);
                if (index == _selectedIndex)
                {
                    EditorGUI.DrawRect(rowRect, SelectRowColor);
                }
                // 比对结果色点：黄=有值修改 绿=有新增 红=有删除 橙=混合 灰=结果已过期
                var hasDot = _allDiffs.TryGetValue(entry.Name, out var diffResult);
                var labelX = 4f;
                if (hasDot)
                {
                    var dotColor = DotColorFor(entry.Name, diffResult);
                    EditorGUI.DrawRect(new Rect(rowRect.x + 4f, rowRect.y + 5f, 8f, 8f), dotColor);
                    labelX = 16f;
                }
                var label = entry.IsLanguage ? entry.Name + "  (多语言)" : entry.Name;
                GUI.Label(new Rect(rowRect.x + labelX, rowRect.y, rowRect.width - labelX, RowHeight), label, _cellStyle);
            }
            GUI.EndScrollView();

            // 焦点指示：最近点击的一侧绘亮色边条
            if (_activePane == 0)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), SelectRowColor);
            }

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && listRect.Contains(evt.mousePosition))
            {
                _activePane = 0;
                if (evt.button == 0)
                {
                    // 纵向滚动条热区不选表
                    if (_filteredIndices.Count * RowHeight > listRect.height - 2f && evt.mousePosition.y > listRect.yMax - 16f)
                    {
                        return;
                    }
                    var localY = evt.mousePosition.y - listRect.y + _listScroll.y;
                    var vi = (int)(localY / RowHeight);
                    if (vi >= 0 && vi < _filteredIndices.Count)
                    {
                        SelectTable(_filteredIndices[vi]);
                        evt.Use();
                    }
                }
                else if (evt.button == 1)
                {
                    var localY = evt.mousePosition.y - listRect.y + _listScroll.y;
                    var vi = (int)(localY / RowHeight);
                    if (vi >= 0 && vi < _filteredIndices.Count)
                    {
                        SelectTable(_filteredIndices[vi]);
                        evt.Use();
                        HandleListContextMenu();
                    }
                }
            }
        }

        private void DrawGrid(Rect rect)
        {
            var statsY = rect.y;
            // 右侧控件从右往左排：导出CSV / 导出JSON / 搜索框 / 与Excel比对，其余空间给统计文案
            var csvRect = new Rect(rect.xMax - 64f, statsY + 1f, 64f, 16f);
            var jsonRect = new Rect(csvRect.x - 74f, statsY + 1f, 70f, 16f);
            var searchRect = new Rect(jsonRect.x - 184f, statsY + 1f, 180f, 16f);
            var compareRect = new Rect(searchRect.x - 96f, statsY + 1f, 92f, 16f);
            var statsLabelRect = new Rect(rect.x, statsY + 2f, Mathf.Max(0f, compareRect.x - rect.x - 8f), 16f);

            string statsText;
            if (_decoded != null)
            {
                statsText = $"{Path.GetFileName(_decodedPath)}   行数: {_decoded.TotalCount}   解码: {_decoded.DecodeMs:F1}ms   拖住表头分隔线可调列宽";
                if (_diff != null && IsDiffStale(_diff))
                {
                    statsText += "   ⚠ 源表已再修改，建议重新比对";
                }
            }
            else if (_decodedError != null)
            {
                statsText = $"{Path.GetFileName(_decodedPath)}   解码失败";
            }
            else
            {
                statsText = "请选择左侧表，或拖入 .bytes 文件";
            }
            GUI.Label(statsLabelRect, statsText, EditorStyles.miniLabel);

            GUI.SetNextControlName("ValueFilter");
            var newValueFilter = GUI.TextField(searchRect, _valueFilter, "ToolbarSeachTextField");
            if (newValueFilter != _valueFilter)
            {
                _valueFilter = newValueFilter;
                ApplyValueFilter();
            }
            using (new EditorGUI.DisabledScope(_decoded == null))
            {
                if (GUI.Button(compareRect, "与Excel比对"))
                {
                    CompareCurrentTable();
                }
                if (GUI.Button(jsonRect, "导出JSON"))
                {
                    ExportJson();
                }
                if (GUI.Button(csvRect, "导出CSV"))
                {
                    ExportCsv();
                }
            }

            // 页签行：数据 / Excel差异 + 右侧差异汇总与操作
            var hasTabs = _decoded != null || _diff != null;
            var tabY = statsY + 20f;
            var contentTop = tabY + 20f;
            if (hasTabs)
            {
                var dataTabRect = new Rect(rect.x, tabY, 100f, 18f);
                var excelTabRect = new Rect(dataTabRect.xMax + 4f, tabY, 150f, 18f);
                var dataTabStyle = _activeTab == 0 ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                var excelTabStyle = _activeTab == 1 ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                if (GUI.Button(dataTabRect, "数据 (bytes)", dataTabStyle))
                {
                    _activeTab = 0;
                }
                using (new EditorGUI.DisabledScope(_diff == null))
                {
                    var excelTabText = ExcelTabText();
                    if (GUI.Button(excelTabRect, excelTabText, excelTabStyle))
                    {
                        _activeTab = 1;
                    }
                }

                if (_diffActive && _diff != null)
                {
                    var langName = string.IsNullOrEmpty(_injectedLanguageFile)
                        ? "未注入"
                        : ConfigBytesDecoder.GetLanguageDisplayName(_injectedLanguageFile);
                    var summary = $"[比对语言:{langName}] 差异: 改{_diff.CellChanges.Count} · 增行{_diff.ExcelOnlyRows.Count} · 删行{_diff.BytesOnlyRows.Count} · 列{_diff.ExcelOnlyColumns.Count + _diff.BytesOnlyColumns.Count}";
                    var summaryRect = new Rect(excelTabRect.xMax + 12f, tabY + 2f, 260f, 16f);
                    GUI.Label(summaryRect, summary, EditorStyles.miniLabel);
                    var clearRect = new Rect(rect.xMax - 64f, tabY, 64f, 16f);
                    var onlyRect = new Rect(clearRect.x - 78f, tabY, 74f, 16f);
                    var onlyChanged = GUI.Toggle(onlyRect, _onlyChangedRows, "仅看差异行", EditorStyles.miniButton);
                    if (onlyChanged != _onlyChangedRows)
                    {
                        _onlyChangedRows = onlyChanged;
                        ApplyValueFilter();
                    }
                    if (GUI.Button(clearRect, "清除着色"))
                    {
                        _diffActive = false;
                        _onlyChangedRows = false;
                        ApplyValueFilter();
                    }
                }

                if (_activeTab == 0)
                {
                    DrawTabContent(new Rect(rect.x, contentTop, rect.width, Mathf.Max(0f, rect.height - (contentTop - rect.y))));
                }
                else
                {
                    // 差异页签：内容坐标（原点=内容左上角）+ 按实际内容高度滚动
                    var diffViewRect = new Rect(rect.x, contentTop, rect.width, Mathf.Max(0f, rect.height - (contentTop - rect.y)));
                    var contentHeight = Mathf.Max(60f, _diffTabContentHeight);
                    _diffTabScroll = GUI.BeginScrollView(diffViewRect, _diffTabScroll, new Rect(0f, 0f, rect.width - 20f, contentHeight));
                    DrawDiffTab(new Rect(0f, 0f, rect.width - 20f, contentHeight));
                    GUI.EndScrollView();
                }
                return;
            }

            var contentRect = new Rect(rect.x, contentTop, rect.width, Mathf.Max(0f, rect.height - (contentTop - rect.y)));
            if (_decodedError != null)
            {
                var message = _decodedError;
                if (!string.IsNullOrEmpty(_decodedHex))
                {
                    message += "\n\n" + _decodedHex;
                }
                EditorGUI.HelpBox(contentRect, message, MessageType.Error);
            }
            else if (_decoded != null)
            {
                DrawGridContent(contentRect);
            }
        }

        /// <summary>Excel 差异页签文案：无结果/未找到源表/差异计数。</summary>
        private string ExcelTabText()
        {
            if (_diff == null)
            {
                return "Excel差异 (未比对)";
            }
            if (!_diff.ExcelFound)
            {
                return "Excel差异 (无源表)";
            }
            if (!string.IsNullOrEmpty(_diff.Error))
            {
                return "Excel差异 (出错)";
            }
            var count = _diff.CellChanges.Count + _diff.ExcelOnlyRows.Count + _diff.BytesOnlyRows.Count +
                        _diff.ExcelOnlyColumns.Count + _diff.BytesOnlyColumns.Count;
            return count > 0 ? $"Excel差异 ●{count}" : "Excel差异 (一致)";
        }

        /// <summary>页签内容分发。</summary>
        private void DrawTabContent(Rect rect)
        {
            if (_decodedError != null)
            {
                var message = _decodedError;
                if (!string.IsNullOrEmpty(_decodedHex))
                {
                    message += "\n\n" + _decodedHex;
                }
                EditorGUI.HelpBox(rect, message, MessageType.Error);
            }
            else if (_decoded != null)
            {
                DrawGridContent(rect);
            }
        }

        private void DrawGridContent(Rect rect)
        {
            var table = _decoded;
            if (_activePane == 1)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), SelectRowColor);
            }
            var rows = _visibleRows;
            var rowsCount = rows != null ? rows.Count : table.RowTexts.Count;
            var totalWidth = 0f;
            foreach (var w in _colWidths)
            {
                totalWidth += w;
            }

            // 表头：横向跟随主体滚动（锁纵向，隐藏滚动条）
            var headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            GUI.BeginScrollView(headerRect, new Vector2(_gridScroll.x, 0f),
                new Rect(0f, 0f, totalWidth, HeaderHeight), GUIStyle.none, GUIStyle.none);
            var hx = 0f;
            for (var c = 0; c < table.Columns.Length; c++)
            {
                var cellRect = new Rect(hx, 0f, _colWidths[c], HeaderHeight);
                EditorGUI.DrawRect(cellRect, HeaderBgColor);
                GUI.Label(cellRect, table.Columns[c], _headerStyle);
                hx += _colWidths[c];
            }
            GUI.EndScrollView();

            // 列宽拖拽调整（命中表头分隔线）
            HandleColumnResize(headerRect);

            // 主体网格：固定行高 + 可见行裁剪
            var bodyRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, Mathf.Max(0f, rect.height - HeaderHeight));
            _gridViewHeight = bodyRect.height;
            _gridScroll = GUI.BeginScrollView(bodyRect, _gridScroll, new Rect(0f, 0f, totalWidth, rowsCount * RowHeight));
            var first = Mathf.Max(0, (int)(_gridScroll.y / RowHeight) - 1);
            var last = Mathf.Min(rowsCount, first + Mathf.CeilToInt(bodyRect.height / RowHeight) + 2);
            for (var vi = first; vi < last; vi++)
            {
                var rowIndex = rows != null ? rows[vi] : vi;
                var y = vi * RowHeight;
                if (rowIndex == _selectedRow)
                {
                    EditorGUI.DrawRect(new Rect(0f, y, totalWidth, RowHeight), SelectRowColor);
                }
                // bytes 独有行（Excel 已删除/改 key）整行红底，叠加在选中色之上依然可辨
                var isBytesOnlyRow = _diffActive && _bytesOnlyRowIndices.Contains(rowIndex);
                if (isBytesOnlyRow)
                {
                    EditorGUI.DrawRect(new Rect(0f, y, totalWidth, RowHeight), DiffDeletedRowColor);
                }
                var texts = table.RowTexts[rowIndex];
                var x = 0f;
                Dictionary<int, CellDiff> rowDiffs = null;
                if (_diffActive)
                {
                    _cellDiffsByRow.TryGetValue(rowIndex, out rowDiffs);
                }
                for (var c = 0; c < texts.Length; c++)
                {
                    var cellRect = new Rect(x, y, _colWidths[c], RowHeight);
                    if (c == 0)
                    {
                        EditorGUI.DrawRect(cellRect, KeyColumnColor);
                    }
                    CellDiff cellDiff = null;
                    if (rowDiffs != null)
                    {
                        rowDiffs.TryGetValue(c, out cellDiff);
                    }
                    if (cellDiff != null)
                    {
                        // 差异底色叠在选中色之上，选中行内的差异格依然可辨；悬停显示前后值与源表位置
                        EditorGUI.DrawRect(cellRect, DiffCellColor);
                        var tooltip = $"Excel: {Truncate(cellDiff.ExcelText, 80)}  [{cellDiff.ExcelCellRef}]\nbytes: {Truncate(cellDiff.BytesText, 80)}\n(点击看对照，双击定位源表)";
                        GUI.Label(cellRect, new GUIContent(texts[c], tooltip), rowIndex == _selectedRow ? _headerStyle : _cellStyle);
                    }
                    else if (isBytesOnlyRow)
                    {
                        GUI.Label(cellRect, new GUIContent(texts[c], "bytes 独有行：Excel 已删除该 key（改 key 会同时产生 Excel 新增行+删除行）"),
                            rowIndex == _selectedRow ? _headerStyle : _cellStyle);
                    }
                    else
                    {
                        GUI.Label(cellRect, texts[c], rowIndex == _selectedRow ? _headerStyle : _cellStyle);
                    }
                    x += _colWidths[c];
                }
            }
            GUI.EndScrollView();
            // 点击命中必须放在滚动视图外：scope 内 mousePosition 已被裁剪换算成内容坐标，再按窗口坐标换算会错位
            HandleGridClick(bodyRect, totalWidth, rowsCount, rows);
        }

        /// <summary>
        /// 表头列宽拖拽调整：按住列分隔线（±3px）左右拖动改变列宽。
        /// </summary>
        private void HandleColumnResize(Rect headerRect)
        {
            if (_colWidths == null || _colWidths.Length == 0)
            {
                return;
            }
            var evt = Event.current;

            if (_resizeCol >= 0)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    _colWidths[_resizeCol] = Mathf.Max(32f, _resizeStartWidth + evt.mousePosition.x - _resizeStartX);
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp)
                {
                    _resizeCol = -1;
                    evt.Use();
                }
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && headerRect.Contains(evt.mousePosition))
            {
                // 分隔线的窗口坐标 = 表头x + 前缀列宽和 - 横向滚动偏移
                var acc = 0f;
                for (var c = 0; c < _colWidths.Length - 1; c++)
                {
                    acc += _colWidths[c];
                    var sepX = headerRect.x + acc - _gridScroll.x;
                    if (Mathf.Abs(evt.mousePosition.x - sepX) <= 3f)
                    {
                        _resizeCol = c;
                        _resizeStartX = evt.mousePosition.x;
                        _resizeStartWidth = _colWidths[c];
                        evt.Use();
                        return;
                    }
                }
            }

            if (evt.type == EventType.Repaint && headerRect.Contains(evt.mousePosition))
            {
                var acc = 0f;
                for (var c = 0; c < _colWidths.Length - 1; c++)
                {
                    acc += _colWidths[c];
                    var sepX = headerRect.x + acc - _gridScroll.x;
                    if (Mathf.Abs(evt.mousePosition.x - sepX) <= 3f)
                    {
                        EditorGUIUtility.AddCursorRect(new Rect(sepX - 3f, headerRect.y, 6f, HeaderHeight), MouseCursor.SplitResizeLeftRight);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 网格行点击命中（滚动视图外部调用，窗口坐标）。
        /// 点击滚动条热区不选行；命中行由视图坐标加滚动偏移换算回内容行号，过滤器激活时映射回原始行。
        /// </summary>
        private void HandleGridClick(Rect bodyRect, float totalWidth, int rowsCount, List<int> rows)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || !bodyRect.Contains(evt.mousePosition))
            {
                return;
            }
            _activePane = 1;
            // 排除滚动条热区（滚动条自身通常会消费事件，这里按几何再兜底一次）
            if (totalWidth > bodyRect.width - 2f && evt.mousePosition.x > bodyRect.xMax - 16f)
            {
                return;
            }
            if (rowsCount * RowHeight > bodyRect.height - 2f && evt.mousePosition.y > bodyRect.yMax - 16f)
            {
                return;
            }
            var contentY = evt.mousePosition.y - bodyRect.y + _gridScroll.y;
            var vi = (int)(contentY / RowHeight);
            if (vi >= 0 && vi < rowsCount)
            {
                _selectedRow = rows != null ? rows[vi] : vi;
                // 列命中（差异单元格联动）
                var clickedCol = -1;
                var contentX = evt.mousePosition.x - bodyRect.x + _gridScroll.x;
                var accX = 0f;
                for (var c = 0; c < _colWidths.Length; c++)
                {
                    accX += _colWidths[c];
                    if (contentX < accX)
                    {
                        clickedCol = c;
                        break;
                    }
                }
                CellDiff cellDiff = null;
                if (clickedCol > 0 && _cellDiffsByRow.TryGetValue(_selectedRow, out var rowDiffs))
                {
                    rowDiffs.TryGetValue(clickedCol, out cellDiff);
                }
                if (cellDiff != null)
                {
                    _detailJson = BuildCellDiffDetail(cellDiff);
                    if (evt.clickCount == 2 && !string.IsNullOrEmpty(_diff.ExcelPath) && File.Exists(_diff.ExcelPath))
                    {
                        EditorUtility.RevealInFinder(_diff.ExcelPath);
                        Debug.Log($"[BytesViewer] 差异单元格: {_diff.ExcelPath} [{cellDiff.ExcelCellRef}] 字段 {cellDiff.Field}");
                    }
                }
                else
                {
                    UpdateDetail();
                }
                evt.Use();
                Repaint();
            }
        }

        private void DrawDetail()
        {
            EditorGUILayout.LabelField("选中行详情:", EditorStyles.miniLabel);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(DetailHeight));
            if (!string.IsNullOrEmpty(_detailJson))
            {
                var width = EditorGUIUtility.currentViewWidth - 40f;
                var height = _detailStyle.CalcHeight(new GUIContent(_detailJson), width);
                EditorGUILayout.SelectableLabel(_detailJson, _detailStyle, GUILayout.Height(Mathf.Max(height, 16f)));
            }
            else
            {
                EditorGUILayout.LabelField("（点击表格行查看完整 JSON）", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region 交互逻辑

        private void ApplyValueFilter()
        {
            _visibleRows = null;
            _selectedRow = -1;
            _detailJson = string.Empty;
            if (_decoded == null)
            {
                return;
            }
            var hasValueFilter = !string.IsNullOrEmpty(_valueFilter);
            if (!hasValueFilter && !_onlyChangedRows)
            {
                return;
            }
            _visibleRows = new List<int>();
            for (var i = 0; i < _decoded.RowTexts.Count; i++)
            {
                if (_onlyChangedRows && !_cellDiffsByRow.ContainsKey(i) && !_bytesOnlyRowIndices.Contains(i))
                {
                    continue;
                }
                if (hasValueFilter)
                {
                    var cells = _decoded.RowTexts[i];
                    var matched = false;
                    foreach (var cell in cells)
                    {
                        if (cell != null && cell.IndexOf(_valueFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        continue;
                    }
                }
                _visibleRows.Add(i);
            }
        }

        private void UpdateDetail()
        {
            _detailJson = string.Empty;
            if (_decoded == null || _selectedRow < 0 || _selectedRow >= _decoded.Rows.Count)
            {
                return;
            }
            var row = _decoded.Rows[_selectedRow];
            var obj = new JObject();
            for (var i = 0; i < _decoded.Columns.Length; i++)
            {
                var v = row[i];
                try
                {
                    obj[_decoded.Columns[i]] = v == null ? JValue.CreateNull() : JToken.FromObject(v);
                }
                catch (Exception e)
                {
                    obj[_decoded.Columns[i]] = $"<err: {e.Message}>";
                }
            }
            _detailJson = obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private void ComputeColumnWidths(DecodedTable table)
        {
            _colWidths = new float[table.Columns.Length];
            for (var c = 0; c < table.Columns.Length; c++)
            {
                var units = TextUnits(table.Columns[c]);
                var sample = Math.Min(table.RowTexts.Count, 60);
                for (var r = 0; r < sample; r++)
                {
                    units = Mathf.Max(units, TextUnits(table.RowTexts[r][c]));
                }
                _colWidths[c] = Mathf.Clamp(units * 7f + 18f, 56f, 320f);
            }
        }

        private static int TextUnits(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }
            var units = 0;
            foreach (var ch in s)
            {
                units += ch > 127 ? 2 : 1;
            }
            return Mathf.Min(units, 40);
        }

        private void HandleDragAndDrop()
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            {
                return;
            }
            if (!position.Contains(evt.mousePosition))
            {
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var path in DragAndDrop.paths)
                {
                    if (!path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    _selectedIndex = -1;
                    ApplyDecoded(ConfigBytesDecoder.Decode(path));
                    break;
                }
            }
            evt.Use();
        }

        private void ExportJson()
        {
            var path = EditorUtility.SaveFilePanel("导出JSON", string.Empty, _decoded.TableName + ".json", "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            File.WriteAllText(path, ConfigBytesDecoder.ToJson(_decoded), new UTF8Encoding(false));
            Debug.Log($"[BytesViewer] 已导出 JSON: {path}");
        }

        private void ExportCsv()
        {
            var path = EditorUtility.SaveFilePanel("导出CSV", string.Empty, _decoded.TableName + ".csv", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            // UTF-8 带 BOM，便于 Excel 直接打开识别中文
            File.WriteAllText(path, ConfigBytesDecoder.ToCsv(_decoded), new UTF8Encoding(true));
            Debug.Log($"[BytesViewer] 已导出 CSV: {path}");
        }

        private void ValidateAll()
        {
            EnsureLanguageInjected();
            var files = ConfigBytesDecoder.ScanTableFiles();
            var failures = new List<string>();
            var okCount = 0;
            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    if (i % 10 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Bytes校验", $"{Path.GetFileName(file)} ({i + 1}/{files.Count})", (float)i / files.Count);
                    }
                    var table = ConfigBytesDecoder.Decode(file);
                    if (table.Ok)
                    {
                        try
                        {
                            ConfigBytesDecoder.ToJson(table);
                            okCount++;
                        }
                        catch (Exception e)
                        {
                            failures.Add($"{Path.GetFileName(file)}: JSON序列化失败 {e.Message}");
                        }
                    }
                    else
                    {
                        failures.Add($"{Path.GetFileName(file)}: {table.Error}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var summary = $"校验完成: {okCount}/{files.Count} 成功";
            if (failures.Count > 0)
            {
                Debug.LogError($"[BytesViewer] {summary}\n{string.Join("\n", failures.Take(20))}");
                EditorUtility.DisplayDialog("Bytes校验", $"{summary}\n失败明细见 Console（前 20 条）", "确定");
            }
            else
            {
                Debug.Log($"[BytesViewer] {summary}");
                EditorUtility.DisplayDialog("Bytes校验", summary, "确定");
            }
        }

        #endregion

        #region 键盘导航

        /// <summary>
        /// ↑/↓ 方向键导航：作用于最近点击的一侧（表列表/数据网格）。
        /// 搜索/过滤文本框聚焦时方向键交给光标，不劫持。
        /// </summary>
        private void HandleKeyboardNavigation()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown)
            {
                return;
            }
            if (evt.keyCode != KeyCode.UpArrow && evt.keyCode != KeyCode.DownArrow)
            {
                return;
            }
            var focusedControl = GUI.GetNameOfFocusedControl();
            if (focusedControl == "TableFilter" || focusedControl == "ValueFilter")
            {
                return;
            }
            evt.Use();
            var delta = evt.keyCode == KeyCode.DownArrow ? 1 : -1;
            if (_activePane == 0)
            {
                NavigateTableList(delta);
            }
            else
            {
                NavigateGrid(delta);
            }
        }

        /// <summary>表列表选中项随方向键移动，并保证滚动可见。</summary>
        private void NavigateTableList(int delta)
        {
            if (_filteredIndices.Count == 0)
            {
                return;
            }
            var pos = _filteredIndices.IndexOf(_selectedIndex);
            var next = pos < 0
                ? (delta > 0 ? 0 : _filteredIndices.Count - 1)
                : Mathf.Clamp(pos + delta, 0, _filteredIndices.Count - 1);
            if (next == pos)
            {
                return;
            }
            SelectTable(_filteredIndices[next]);
            var rowY = next * RowHeight;
            if (rowY < _listScroll.y)
            {
                _listScroll.y = rowY;
            }
            else if (rowY + RowHeight > _listScroll.y + _listViewHeight)
            {
                _listScroll.y = rowY + RowHeight - _listViewHeight;
            }
            Repaint();
        }

        /// <summary>数据网格选中行随方向键移动（遵循值过滤/仅看差异的可见行序列），并保证滚动可见。</summary>
        private void NavigateGrid(int delta)
        {
            if (_decoded == null || _activeTab != 0)
            {
                return;
            }
            var rows = _visibleRows;
            var count = rows != null ? rows.Count : _decoded.RowTexts.Count;
            if (count == 0)
            {
                return;
            }
            var pos = rows != null ? rows.IndexOf(_selectedRow) : _selectedRow;
            var next = pos < 0
                ? (delta > 0 ? 0 : count - 1)
                : Mathf.Clamp(pos + delta, 0, count - 1);
            if (next == pos)
            {
                return;
            }
            _selectedRow = rows != null ? rows[next] : next;
            UpdateDetail();
            var rowY = next * RowHeight;
            if (rowY < _gridScroll.y)
            {
                _gridScroll.y = rowY;
            }
            else if (rowY + RowHeight > _gridScroll.y + _gridViewHeight)
            {
                _gridScroll.y = rowY + RowHeight - _gridViewHeight;
            }
            Repaint();
        }

        #endregion

        #region Excel比对与逆向导出

        /// <summary>比对当前表并应用着色。</summary>
        private void CompareCurrentTable()
        {
            if (_decoded == null || _selectedIndex < 0 || _selectedIndex >= _tables.Count)
            {
                return;
            }
            EnsureLanguageInjected();
            EditorUtility.DisplayProgressBar("Excel比对", $"正在比对 {_tables[_selectedIndex].Name} ...", 0.5f);
            TableDiffResult result;
            try
            {
                result = ConfigExcelDiff.DiffTable(_decoded);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            var tableName = _tables[_selectedIndex].Name;
            _allDiffs[tableName] = result;
            _dotColorCache.Remove(tableName);
            ApplyDiff(result);
            ApplyTableFilter();
        }

        /// <summary>全表比对：结果落左列表色点，可取消。</summary>
        private void CompareAllTables()
        {
            EnsureLanguageInjected();
            var files = ConfigBytesDecoder.ScanTableFiles();
            var done = 0;
            var changed = 0;
            var identical = 0;
            var noSource = 0;
            var failed = 0;
            try
            {
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith("Language_", StringComparison.Ordinal))
                    {
                        continue; // 语言表无独立源 sheet（由配置表多语言列生成）
                    }
                    done++;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Excel比对·全表", $"正在比对 {name} ({done})", (float)done / files.Count))
                    {
                        break;
                    }
                    var decoded = ConfigBytesDecoder.Decode(file);
                    TableDiffResult result;
                    if (!decoded.Ok)
                    {
                        result = new TableDiffResult { TableName = name, Error = "bytes 解码失败: " + decoded.Error };
                    }
                    else
                    {
                        result = ConfigExcelDiff.DiffTable(decoded);
                    }
                    _allDiffs[name] = result;
                    if (!result.ExcelFound) noSource++;
                    else if (!string.IsNullOrEmpty(result.Error)) failed++;
                    else if (result.HasChange) changed++;
                    else identical++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            _dotColorCache.Clear();
            ApplyTableFilter();
            Repaint();
            Debug.Log($"[BytesViewer] 全表比对完成: 有差异 {changed} / 一致 {identical} / 无源表 {noSource} / 出错 {failed}（左列表已标注色点）");
        }

        /// <summary>应用比对结果：重建 行→列 差异映射并开启着色；有差异时自动切到差异页签。</summary>
        private void ApplyDiff(TableDiffResult result)
        {
            _diff = result;
            _diffActive = true;
            _cellDiffsByRow.Clear();
            _onlyChangedRows = false;
            _diffSectionSelected = -1;
            _diffEntrySelected = -1;
            if (result == null)
            {
                return;
            }
            _activeTab = result.ExcelFound && string.IsNullOrEmpty(result.Error) && result.HasChange ? 1 : 0;
            var rowCount = _decoded?.RowTexts.Count ?? 0;
            var colCount = _decoded?.Columns.Length ?? 0;
            foreach (var change in result.CellChanges)
            {
                if (change.RowIndex < 0 || change.RowIndex >= rowCount)
                {
                    continue;
                }
                var colIndex = -1;
                for (var c = 0; c < colCount; c++)
                {
                    if (_decoded.Columns[c] == change.Field)
                    {
                        colIndex = c;
                        break;
                    }
                }
                if (colIndex < 0)
                {
                    continue;
                }
                if (!_cellDiffsByRow.TryGetValue(change.RowIndex, out var rowMap))
                {
                    rowMap = new Dictionary<int, CellDiff>();
                    _cellDiffsByRow[change.RowIndex] = rowMap;
                }
                rowMap[colIndex] = change;
            }
            // bytes 独有行（Excel 已删除该 key，含改 key 场景）→ 数据网格整行红底
            _bytesOnlyRowIndices.Clear();
            if (_decoded != null && result.BytesOnlyRows.Count > 0)
            {
                var onlyKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in result.BytesOnlyRows)
                {
                    onlyKeys.Add(row.Key);
                }
                for (var i = 0; i < _decoded.RowTexts.Count; i++)
                {
                    if (onlyKeys.Contains(_decoded.RowTexts[i][0]))
                    {
                        _bytesOnlyRowIndices.Add(i);
                    }
                }
            }
            ApplyValueFilter();
            Repaint();
        }

        /// <summary>比对结果是否已过期（源 xlsx 修改时间晚于比对时间）。</summary>
        private bool IsDiffStale(TableDiffResult result)
        {
            if (result == null || !result.ExcelFound || string.IsNullOrEmpty(result.ExcelPath) || !File.Exists(result.ExcelPath))
            {
                return false;
            }
            try
            {
                return File.GetLastWriteTimeUtc(result.ExcelPath) != result.ExcelLastWriteUtc;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>左列表色点颜色：红(有删除) > 橙(增删混合) > 绿(有新增) > 黄(有值修改)，结果过期灰。</summary>
        private Color DotColorFor(string tableName, TableDiffResult result)
        {
            if (IsDiffStale(result))
            {
                return DiffDotStale;
            }
            if (_dotColorCache.TryGetValue(tableName, out var cached))
            {
                return cached;
            }
            var hasAdd = result.ExcelOnlyRows.Count > 0 || result.ExcelOnlyColumns.Count > 0;
            var hasDelete = result.BytesOnlyRows.Count > 0 || result.BytesOnlyColumns.Count > 0;
            Color color;
            if (hasAdd && hasDelete) color = DiffDotMixed;
            else if (hasDelete) color = DiffDotDeleted;
            else if (hasAdd) color = DiffDotAdded;
            else if (result.CellChanges.Count > 0) color = DiffDotChanged;
            else color = DiffDotStale;
            _dotColorCache[tableName] = color;
            return color;
        }

        /// <summary>差异单元格对照文本（详情面板展示）。</summary>
        private string BuildCellDiffDetail(CellDiff diff)
        {
            var rowJson = string.Empty;
            if (_decoded != null && diff.RowIndex >= 0 && diff.RowIndex < _decoded.Rows.Count)
            {
                var saveSelected = _selectedRow;
                _selectedRow = diff.RowIndex;
                UpdateDetail();
                rowJson = _detailJson;
                _selectedRow = saveSelected;
            }
            return $"【差异对照】{diff.Field}  (行 {diff.Key})\nExcel: {diff.ExcelText}   [{diff.ExcelCellRef}]\nbytes:  {diff.BytesText}\n\n—— 整行 ——\n{rowJson}";
        }

        /// <summary>
        /// Excel 差异页签内容（滚动内容坐标系，原点=内容左上角，绘制完回写实际内容高度）。
        /// 条目单击=详情面板看对照；双击=切到数据页签定位。
        /// </summary>
        private void DrawDiffTab(Rect rect)
        {
            var y = 4f;
            if (_diff == null)
            {
                GUI.Label(new Rect(8f, y, rect.width - 16f, 32f), "尚未比对：点击统计栏 [与Excel比对]。", EditorStyles.miniLabel);
                _diffTabContentHeight = y + 40f;
                return;
            }
            if (!_diff.ExcelFound)
            {
                GUI.Label(new Rect(8f, y, rect.width - 16f, 32f),
                    $"未找到源 sheet（{ConfigExcelDiff.GetSheetName(_diff.TableName)}），请确认 Tables 目录配置。", EditorStyles.miniLabel);
                _diffTabContentHeight = y + 40f;
                return;
            }
            if (!string.IsNullOrEmpty(_diff.Error))
            {
                EditorGUI.HelpBox(new Rect(4f, y, rect.width - 8f, 50f), _diff.Error, MessageType.Error);
                _diffTabContentHeight = y + 60f;
                return;
            }
            if (!_diff.HasChange)
            {
                GUI.Label(new Rect(8f, y, rect.width - 16f, 32f),
                    $"与 Excel 一致（比对 {_diff.MatchedRows} 行）。源表: {_diff.ExcelPath}", EditorStyles.miniLabel);
                _diffTabContentHeight = y + 40f;
                return;
            }

            y = DrawDiffSectionHeader(rect, y, $"➕ Excel新增行 ({_diff.ExcelOnlyRows.Count})");
            for (var i = 0; i < _diff.ExcelOnlyRows.Count; i++)
            {
                var row = _diff.ExcelOnlyRows[i];
                y = DrawDiffRowEntry(new Rect(8f, y, rect.width - 16f, RowHeight), 0, i,
                    $"{row.Key}   (Excel 第{row.ExcelRowNumber}行)", BuildRowSnapshotText(row.Fields));
            }
            y += 10f;
            y = DrawDiffSectionHeader(rect, y, $"➖ Excel删除行 ({_diff.BytesOnlyRows.Count})");
            for (var i = 0; i < _diff.BytesOnlyRows.Count; i++)
            {
                var row = _diff.BytesOnlyRows[i];
                y = DrawDiffRowEntry(new Rect(8f, y, rect.width - 16f, RowHeight), 1, i, row.Key, BuildRowSnapshotText(row.Fields));
            }
            y += 10f;
            y = DrawDiffSectionHeader(rect, y, "列差异");
            GUI.Label(new Rect(8f, y, rect.width - 16f, 16f),
                $"➕ Excel新增列 ({_diff.ExcelOnlyColumns.Count}): {(_diff.ExcelOnlyColumns.Count > 0 ? string.Join(", ", _diff.ExcelOnlyColumns) : "无")}", EditorStyles.miniLabel);
            y += 16f;
            GUI.Label(new Rect(8f, y, rect.width - 16f, 16f),
                $"➖ Excel删除列 ({_diff.BytesOnlyColumns.Count}): {(_diff.BytesOnlyColumns.Count > 0 ? string.Join(", ", _diff.BytesOnlyColumns) : "无")}", EditorStyles.miniLabel);
            y += 26f;
            y = DrawDiffSectionHeader(rect, y, $"✎ 值修改 ({_diff.CellChanges.Count})");
            for (var i = 0; i < _diff.CellChanges.Count; i++)
            {
                y = DrawValueChangeEntry(new Rect(8f, y, rect.width - 16f, RowHeight), i, _diff.CellChanges[i]);
            }
            _diffTabContentHeight = y + 20f;
        }

        /// <summary>行快照 → 详情面板多行文本。</summary>
        private static string BuildRowSnapshotText(List<KeyValuePair<string, string>> fields)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var pair in fields)
            {
                sb.AppendLine($"{pair.Key} = {Truncate(pair.Value, 200)}");
            }
            return sb.ToString();
        }

        private float DrawDiffSectionHeader(Rect rect, float y, string title)
        {
            GUI.Label(new Rect(rect.x + 4f, y, rect.width - 8f, 18f), title, EditorStyles.miniBoldLabel);
            return y + 20f;
        }

        /// <summary>单列差异条目：整行文本裁剪显示，悬停看全文。</summary>
        private float DrawDiffRowEntry(Rect rect, int section, int entryIndex, string text, string tooltip = null)
        {
            var isSelected = _diffSectionSelected == section && _diffEntrySelected == entryIndex;
            if (isSelected)
            {
                EditorGUI.DrawRect(rect, SelectRowColor);
            }
            GUI.Label(rect, new GUIContent(text, tooltip), _diffEntryStyle);
            HandleDiffEntryMouse(rect, section, entryIndex);
            return rect.yMax;
        }

        /// <summary>值修改条目：左段定位信息、右段前后对照（裁剪），悬停看全文；单击详情/双击定位。</summary>
        private float DrawValueChangeEntry(Rect rect, int entryIndex, CellDiff change)
        {
            var isSelected = _diffSectionSelected == 2 && _diffEntrySelected == entryIndex;
            if (isSelected)
            {
                EditorGUI.DrawRect(rect, SelectRowColor);
            }
            var tooltip = $"Excel: {Truncate(change.ExcelText, 400)}\nbytes: {Truncate(change.BytesText, 400)}\n[{change.ExcelCellRef}]\n(单击看详情，双击定位到行)";
            var leftWidth = Mathf.Min(230f, rect.width * 0.45f);
            GUI.Label(new Rect(rect.x, rect.y, leftWidth, rect.height),
                new GUIContent($"[{Truncate(change.Key, 18)}] {change.Field}", tooltip), _diffEntryStyle);
            GUI.Label(new Rect(rect.x + leftWidth + 8f, rect.y, Mathf.Max(0f, rect.width - leftWidth - 72f), rect.height),
                new GUIContent($"\"{Truncate(change.ExcelText, 120)}\" ⇄ \"{Truncate(change.BytesText, 120)}\"", tooltip), _diffEntryStyle);
            GUI.Label(new Rect(rect.xMax - 60f, rect.y, 60f, rect.height),
                new GUIContent(change.ExcelCellRef, tooltip), _diffEntryStyle);
            HandleDiffEntryMouse(rect, 2, entryIndex);
            return rect.yMax;
        }

        /// <summary>差异条目命中：单击=详情面板看对照（停留本页签）；双击=切到数据页签定位。</summary>
        private void HandleDiffEntryMouse(Rect rect, int section, int entryIndex)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || !rect.Contains(evt.mousePosition))
            {
                return;
            }
            _diffSectionSelected = section;
            _diffEntrySelected = entryIndex;
            OnDiffEntryClicked(section, entryIndex, evt.clickCount >= 2);
            evt.Use();
            Repaint();
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? string.Empty;
            }
            return text.Substring(0, max) + "…";
        }

        /// <summary>差异条目动作：新增行/删除行→详情面板看全字段；值修改→详情面板看对照；jumpToGrid 时定位到数据页签。</summary>
        private void OnDiffEntryClicked(int section, int entryIndex, bool jumpToGrid)
        {
            if (_diff == null)
            {
                return;
            }
            if (section == 0 && entryIndex < _diff.ExcelOnlyRows.Count)
            {
                var row = _diff.ExcelOnlyRows[entryIndex];
                _detailJson = $"【Excel新增行】{row.Key} (Excel 第{row.ExcelRowNumber}行)\n" + BuildRowSnapshotText(row.Fields);
            }
            else if (section == 1 && entryIndex < _diff.BytesOnlyRows.Count)
            {
                var row = _diff.BytesOnlyRows[entryIndex];
                _detailJson = $"【Excel删除行】{row.Key}（bytes 侧仍存在）\n" + BuildRowSnapshotText(row.Fields);
                if (jumpToGrid)
                {
                    JumpToBytesRow(row.Key);
                }
            }
            else if (section == 2 && entryIndex < _diff.CellChanges.Count)
            {
                var change = _diff.CellChanges[entryIndex];
                _detailJson = BuildCellDiffDetail(change);
                if (jumpToGrid)
                {
                    _selectedRow = change.RowIndex;
                    _activeTab = 0;
                    ScrollGridToSelectedRow();
                }
            }
            Repaint();
        }

        /// <summary>按主键在数据网格中定位行并保证滚动可见。</summary>
        private void JumpToBytesRow(string key)
        {
            if (_decoded == null)
            {
                return;
            }
            for (var i = 0; i < _decoded.RowTexts.Count; i++)
            {
                if (_decoded.RowTexts[i][0] == key)
                {
                    _selectedRow = i;
                    UpdateDetail();
                    _activeTab = 0;
                    ScrollGridToSelectedRow();
                    break;
                }
            }
        }

        /// <summary>把当前选中行滚动到网格视口中部。</summary>
        private void ScrollGridToSelectedRow()
        {
            var visible = _visibleRows;
            var vi = visible != null ? visible.IndexOf(_selectedRow) : _selectedRow;
            if (vi < 0)
            {
                return;
            }
            var rowY = vi * RowHeight;
            if (rowY < _gridScroll.y || rowY + RowHeight > _gridScroll.y + _gridViewHeight)
            {
                _gridScroll.y = Mathf.Max(0f, rowY - _gridViewHeight / 2f);
            }
        }

        /// <summary>左列表右键菜单：逆向导出可回导 Excel。</summary>
        private void HandleListContextMenu()
        {
            var menu = new GenericMenu();
            if (_decoded != null && !_decoded.IsLanguageFile)
            {
                menu.AddItem(new GUIContent("导出为Excel(可回导)"), false, ExportCurrentTable);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("导出为Excel(可回导)（语言表不支持）"));
            }
            menu.ShowAsContext();
        }

        /// <summary>导出当前表为可回导 Excel；完成后按源表核对结果给出服务器列警告。</summary>
        private void ExportCurrentTable()
        {
            if (_decoded == null)
            {
                return;
            }
            var defaultName = ConfigExcelDiff.GetSheetName(_decoded.TableName) + "_recovered.xlsx";
            var path = EditorUtility.SaveFilePanel("导出可回导Excel", string.Empty, defaultName, "xlsx");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            var error = ConfigBytesExporter.ExportToExcel(_decoded, path, out var sourceOnlyColumns);
            if (error != null)
            {
                EditorUtility.DisplayDialog("导出失败", error, "确定");
                return;
            }
            string extra;
            if (sourceOnlyColumns == null)
            {
                extra = "\n\n⚠ 未找到源 sheet，无法核对服务器列清单。";
            }
            else if (sourceOnlyColumns.Count > 0)
            {
                extra = "\n\n⚠ 以下源表列未包含在导出结果中（服务器列等），作为新源表使用前需与服务器核对:\n" + string.Join(", ", sourceOnlyColumns);
            }
            else
            {
                extra = "\n\n已核对：源表无未导出列。";
            }
            Debug.Log($"[BytesViewer] 已导出可回导Excel: {path}{extra}");
            EditorUtility.DisplayDialog("导出成功", $"已导出: {path}{extra}", "确定");
        }

        #endregion
    }
}
