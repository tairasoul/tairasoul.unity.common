using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace tairasoul.unity.common.util;

public class ActionQueue 
#if UNITY_ENVIRONMENT
: MonoBehaviour
#endif
{
	public static ActionQueue? _instance;
#if UNITY_ENVIRONMENT
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
#endif

	public static Action<string> Logger = (_) => { };

	readonly ConcurrentQueue<Action> actions = [];

#if !ACTION_QUEUE_RUN_IMMEDIATELY
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Enqueue(Action action) {
		Instance.actions.Enqueue(action);
	}
#else
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Enqueue(Action action) {
		action();
	}
#endif


	public void RunQueue() {
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

#if UNITY_ENVIRONMENT && ACTION_QUEUE_USE_FIXED_UPDATE
	public void FixedUpdate() {
		RunQueue();
	}
#endif
}