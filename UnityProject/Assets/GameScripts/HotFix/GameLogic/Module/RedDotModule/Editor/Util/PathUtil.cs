namespace GameLogic.RedDot.Editor.Util
{
    /// <summary>
    /// 编辑器资源路径辅助。CurEditorPath 在 RedDotWindow.Initialize 中设置，
    /// 指向 RedDotWindow.cs 所在目录（用于定位同目录下的 Resources/Styles 等）。
    /// </summary>
    public static class PathUtil
    {
        public static string CurEditorPath;

        public static string GetEditorFilePath(string fileName) => $"{CurEditorPath}{fileName}";
    }
}
