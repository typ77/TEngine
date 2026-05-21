// Assets/TEngine/Runtime/Module/DebugerModule/Component/DebuggerModule.TimerInformationWindow.cs
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class TimerInformationWindow : ScrollableDebuggerWindowBase
        {
            private ITimerModule _timer;
            private readonly List<TimerDiagnosticInfo> _diagnostics = new List<TimerDiagnosticInfo>();

            public override void Initialize(params object[] args)
            {
                _timer = ModuleSystem.GetModule<ITimerModule>();
                if (_timer == null)
                {
                    Log.Fatal("TimerModule not found.");
                    return;
                }
            }

            protected override void OnDrawScrollableWindow()
            {
                if (_timer == null) { GUILayout.Label("TimerModule not found."); return; }

                GUILayout.Label("<b>Timer System</b>");
                GUILayout.BeginVertical("box");
                DrawItem("Active Timers", _timer.ActiveTimerCount.ToString());
                DrawItem("Zombie Timers", _timer.ZombieCount.ToString());
                DrawItem("Pool Capacity", _timer.PoolCapacity.ToString());
                DrawItem("Pool Used", _timer.PoolUsed.ToString());
                GUILayout.EndVertical();

                _timer.GetDiagnostics(_diagnostics);
                if (_diagnostics.Count == 0) return;

                GUILayout.Label("<b>Active Timer List</b>");
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>Id</b>", GUILayout.Width(40));
                GUILayout.Label("<b>Remaining</b>", GUILayout.Width(80));
                GUILayout.Label("<b>Tick</b>", GUILayout.Width(60));
                GUILayout.Label("<b>Total</b>", GUILayout.Width(60));
                GUILayout.Label("<b>Mode</b>", GUILayout.Width(70));
                GUILayout.Label("<b>Paused</b>", GUILayout.Width(60));
                GUILayout.EndHorizontal();

                foreach (TimerDiagnosticInfo info in _diagnostics)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(info.Id.ToString(), GUILayout.Width(40));
                    GUILayout.Label($"{info.RemainingSeconds:F2}s", GUILayout.Width(80));
                    GUILayout.Label(info.TickIndex.ToString(), GUILayout.Width(60));
                    GUILayout.Label(info.TotalTicks < 0 ? "∞" : info.TotalTicks.ToString(), GUILayout.Width(60));
                    GUILayout.Label(info.TimeMode.ToString(), GUILayout.Width(70));
                    GUILayout.Label(info.IsPaused ? "Yes" : "No", GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                }
            }
        }
    }
}
