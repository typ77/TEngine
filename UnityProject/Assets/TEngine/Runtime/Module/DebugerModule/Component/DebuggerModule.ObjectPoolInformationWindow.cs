﻿using System.Globalization;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class ObjectPoolInformationWindow : ScrollableDebuggerWindowBase
        {
            private IObjectPoolModule _objectPool = null;

            public override void Initialize(params object[] args)
            {
                _objectPool = ModuleSystem.GetModule<IObjectPoolModule>();
                if (_objectPool == null)
                {
                    Log.Fatal("Object pool component is invalid.");
                    return;
                }
            }

            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Object Pool Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Object Pool Count", _objectPool.Count.ToString());
                }
                GUILayout.EndVertical();
                ObjectPoolBase[] objectPools = _objectPool.GetAllObjectPools(true);
                for (int i = 0; i < objectPools.Length; i++)
                {
                    DrawObjectPool(objectPools[i]);
                }
            }

            private void DrawObjectPool(ObjectPoolBase objectPool)
            {
                GUILayout.Label(Utility.Text.Format("<b>Object Pool: {0}</b>", objectPool.FullName));
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Name", objectPool.Name);
                    DrawItem("Type", objectPool.ObjectType.FullName);
                    DrawItem("Auto Release Interval", objectPool.AutoReleaseInterval.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Capacity", objectPool.Capacity.ToString());
                    DrawItem("Used Count", objectPool.Count.ToString());
                    DrawItem("Can Release Count", objectPool.CanReleaseCount.ToString());
                    DrawItem("Expire Time", objectPool.ExpireTime.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Priority", objectPool.Priority.ToString());
                    ObjectInfo[] objectInfos = objectPool.GetAllObjectInfos();
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Label("<b>Name</b>");
                        GUILayout.Label("<b>Locked</b>", GUILayout.Width(60f));
                        GUILayout.Label(objectPool.AllowMultiSpawn ? "<b>Count</b>" : "<b>In Use</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Flag</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Priority</b>", GUILayout.Width(60f));
                        GUILayout.Label("<b>Last Use Time</b>", GUILayout.Width(120f));
                    }
                    GUILayout.EndHorizontal();

                    if (objectInfos.Length > 0)
                    {
                        for (int i = 0; i < objectInfos.Length; i++)
                        {
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.Label(string.IsNullOrEmpty(objectInfos[i].Name) ? "<None>" : objectInfos[i].Name);
                                GUILayout.Label(objectInfos[i].Locked.ToString(), GUILayout.Width(60f));
                                GUILayout.Label(objectPool.AllowMultiSpawn ? objectInfos[i].SpawnCount.ToString() : objectInfos[i].IsInUse.ToString(), GUILayout.Width(60f));
                                GUILayout.Label(objectInfos[i].CustomCanReleaseFlag.ToString(), GUILayout.Width(60f));
                                GUILayout.Label(objectInfos[i].Priority.ToString(), GUILayout.Width(60f));
                                GUILayout.Label(objectInfos[i].LastUseTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), GUILayout.Width(120f));
                            }
                            GUILayout.EndHorizontal();
                        }
                    }
                    else
                    {
                        GUILayout.Label("<i>Object Pool is Empty ...</i>");
                    }
                }
                GUILayout.EndVertical();
            }
        }
    }
}
