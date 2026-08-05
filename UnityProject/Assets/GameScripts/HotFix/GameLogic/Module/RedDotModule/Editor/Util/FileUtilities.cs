using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Util
{
    /// <summary>
    /// 文件读写工具（含只读文件处理与重试）。
    /// </summary>
    internal static class FileUtilities
    {
        public static string WriteRedDotGraphDataToDisk(string path, object data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var text = JsonUtility.ToJson(data, true);
            return WriteToDisk(path, text) ? text : null;
        }

        public static bool WriteToDisk(string path, string text)
        {
            while (true)
            {
                try
                {
                    File.WriteAllText(path, text);
                }
                catch (Exception e)
                {
                    if (e.GetBaseException() is UnauthorizedAccessException &&
                        (File.GetAttributes(path) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        if (EditorUtility.DisplayDialog("File is Read-Only", path, "Make Writeable", "Cancel Save"))
                        {
                            new FileInfo(path).IsReadOnly = false;
                            continue;
                        }
                        return false;
                    }

                    Debug.LogException(e);
                    if (EditorUtility.DisplayDialog("Exception While Saving", e.ToString(), "Retry", "Cancel"))
                        continue;
                    return false;
                }
                break;
            }
            return true;
        }
    }
}
