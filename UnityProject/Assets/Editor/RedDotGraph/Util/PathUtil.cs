using UnityEditor;

namespace RedDot.Editor.Util
{
    public static class PathUtil
    {
        private static string _curEditorPath;

        /// <summary>编辑器资源根路径(以 / 结尾)。正常流程由 RedDotWindow.Initialize 赋值;
        /// 兜底:窗口经布局恢复(未经 Initialize)时按 RedDotWindow.cs 资产位置推导,
        /// 否则 uss/uxml 全部加载失败,节点视图构建直接抛 styleSheet 空指针(2026-09-16 反馈批修复)。</summary>
        public static string CurEditorPath
        {
            get
            {
                if (string.IsNullOrEmpty(_curEditorPath))
                {
                    var guids = AssetDatabase.FindAssets("t:MonoScript RedDotWindow");
                    if (guids != null && guids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        _curEditorPath = path.Replace("RedDotWindow.cs", "");
                    }
                }
                return _curEditorPath;
            }
            set { _curEditorPath = value; }
        }

        public static string GetEditorFilePath(string fileName)
        {
            return $"{CurEditorPath}{fileName}";
        }
    }
}
