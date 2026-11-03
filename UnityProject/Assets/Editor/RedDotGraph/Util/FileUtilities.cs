using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TEngine;
using Debug = UnityEngine.Debug;

namespace RedDot.Editor.Util
{
    static class FileUtilities
    {
        /// <param name="pretty">true=带缩进(.reddotgraph 源文件,SVN diff 友好);false=紧凑(运行时 JSON,进包体积减半)</param>
        public static string WriteRedDotGraphDataToDisk(string path, object data, bool interactive = true, bool pretty = true)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var text = JsonUtility.ToJson(data, pretty);
            if (WriteToDisk(path, text, interactive))
                return text;
            else
                return null;
        }

        // returns true if successfully written to disk
        // interactive=false 时(资产创建/导入管线等非显式保存路径)不弹模态框,失败直接记日志返回。
        public static bool WriteToDisk(string path, string text, bool interactive = true)
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
                        File.Exists(path) &&
                        (File.GetAttributes(path) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        if (!interactive)
                        {
                            Log.Error("写入失败，文件只读:{0}", path);
                            return false;
                        }
                        if (EditorUtility.DisplayDialog("File is Read-Only", path, "Make Writeable", "Cancel Save"))
                        {
                            // make writeable
                            FileInfo fileInfo = new FileInfo(path);
                            fileInfo.IsReadOnly = false;
                            continue; // retry save
                        }
                        else
                            return false;
                    }

                    Debug.LogException(e);

                    if (!interactive)
                        return false;

                    if (EditorUtility.DisplayDialog("Exception While Saving", e.ToString(), "Retry", "Cancel"))
                        continue; // retry save
                    else
                        return false;
                }
                break; // no exception, file save success!
            }

            return true;
        }
    }
}
