using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: AssemblyVersion("0.0.0.0")]
namespace SoloDayDebug
{
	[BepInPlugin("com.local.solodaydebug", "Solo Day Debug", "1.0.0")]
	public class DayDebugPlugin : BaseUnityPlugin
	{
		private bool _show;

		private Rect _win = new Rect(24f, 590f, 440f, 220f);

		private GUIStyle _lbl;

		private GUIStyle _btn;

		private void Awake()
		{
			Logger.LogInfo("Solo Day Debug loaded. Host F8 = day +1 / -1.");
		}

		private static Type SoloType()
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				Type type = assemblies[i].GetType("SoloBank.SoloPlugin");
				if (type != null)
				{
					return type;
				}
			}
			return null;
		}

		private static int GetDay()
		{
			Type type = SoloType();
			if (type == null)
			{
				return -1;
			}
			FieldInfo field = type.GetField("SharedDay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return -1;
			}
			return (int)field.GetValue(null);
		}

		private static bool ShiftDay(int delta)
		{
			Type type = SoloType();
			if (type == null)
			{
				return false;
			}
			FieldInfo field = type.GetField("SharedDay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return false;
			}
			int num = (int)field.GetValue(null) + delta;
			if (num < 1)
			{
				num = 1;
			}
			field.SetValue(null, num);
			MethodInfo method = type.GetMethod("Broadcast", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				method.Invoke(null, null);
			}
			MethodInfo method2 = type.GetMethod("ApplyAllElevatorLocks", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method2 != null)
			{
				method2.Invoke(null, null);
			}
			return true;
		}

		private void Update()
		{
			if (!NetworkServer.active)
			{
				_show = false;
				return;
			}
			bool flag = false;
			try
			{
				Keyboard current = Keyboard.current;
				if (current != null && current.f8Key.wasPressedThisFrame)
				{
					flag = true;
				}
			}
			catch
			{
			}
			try
			{
				if (Input.GetKeyDown(KeyCode.F8))
				{
					flag = true;
				}
			}
			catch
			{
			}
			if (flag)
			{
				_show = !_show;
			}
		}

		private void OnGUI()
		{
			if (_show && NetworkServer.active)
			{
				if (_lbl == null)
				{
					_lbl = new GUIStyle(GUI.skin.label)
					{
						fontSize = 20,
						fontStyle = FontStyle.Bold,
						wordWrap = true,
						normal = 
						{
							textColor = Color.white
						}
					};
					_btn = new GUIStyle(GUI.skin.button)
					{
						fontSize = 22,
						fontStyle = FontStyle.Bold
					};
				}
				_win = GUI.Window(1146700337, _win, Draw, "DAY DEBUG (F8)  host only");
			}
		}

		private void Draw(int id)
		{
			GUILayout.Space(8f);
			int day = GetDay();
			if (day < 0)
			{
				GUILayout.Label("Solo Bank is not loaded.", _lbl);
			}
			else
			{
				GUILayout.Label("Solo Bank day: " + day + " / 10", _lbl);
			}
			GUILayout.Space(8f);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Day -1", _btn, GUILayout.Height(44f), GUILayout.MinWidth(180f)))
			{
				ShiftDay(-1);
			}
			if (GUILayout.Button("Day +1", _btn, GUILayout.Height(44f), GUILayout.MinWidth(180f)))
			{
				ShiftDay(1);
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(8f);
			GUILayout.Label("Does not change money or tickets. Ranking live on day 5 and settlement. Other days hide other players.", _lbl);
			GUI.DragWindow();
		}
	}
}
