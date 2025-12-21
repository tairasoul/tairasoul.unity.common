using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace tairasoul.unity.common.util;

class ActionQueue : MonoBehaviour
{
	static ActionQueue? _instance;
	public static ActionQueue Instance { 
		get 
		{
			if (_instance == null) {
				GameObject obj = new("tairasoul.unity.common.actionqueue." + Assembly.GetExecutingAssembly().GetName().Name);
				DontDestroyOnLoad(obj);
				_instance = obj.AddComponent<ActionQueue>();
			}
			return _instance;
		}
	}

	public static Action<string> Logger = (_) => { };

	readonly ConcurrentQueue<Action> actions = [];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Enqueue(Action action) {
		Instance.actions.Enqueue(action);
	}

	public void FixedUpdate() {
		while (actions.TryDequeue(out var action))
		{
			try
			{
				action?.Invoke();
			}
			catch (Exception ex) {
				Logger(ex.ToString());
			}
		}
	}
}