using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tairasoul.unity.common.hashing;

namespace tairasoul.unity.common.events;

record EventListener(string id, Action<EventData> listener);
public abstract record EventID();
public abstract record EventData();

public static class EventBus
{
	static ConcurrentDictionary<EventID, List<EventListener>> listeners = [];

	public static void Listen<T, C>(T eventId, string listenerId, Action<C> listener)
		where T : EventID
		where C : EventData
	{
		if (listeners.TryGetValue(eventId, out var list))
		{
			list.Add(new(listenerId, (v) => listener((C)v)));
		}
		else
		{
			listeners[eventId] = [new(listenerId, (v) => listener((C)v))];
		}
	}

	public static void Send<T>(T eventId, EventData eventData)
		where T : EventID
	{
		if (listeners.TryGetValue(eventId, out var list))
		{
			foreach (var listener in list.ToList())
				listener.listener(eventData);
		}
	}

	public static void StopListening<T>(T eventId, string id)
		where T : EventID
	{
		if (listeners.TryGetValue(eventId, out var list))
		{
			var toRemove = list.FirstOrDefault(l => l.id == id);
			if (toRemove != null) list.Remove(toRemove);
		}
	}

	public static async Task<H> WaitFor<T, H>(T eventId, Func<H, bool> predicate)
		where T : EventID
		where H : EventData
	{
		string listenerId = Murmur3.Hash128String($"{DateTime.Now}_autoListenerWaitFor");
		TaskCompletionSource<H> tcs = new();
		Listen<T, H>(eventId, listenerId, ed =>
		{
			if (predicate(ed))
			{
				tcs.SetResult(ed);
				StopListening(eventId, listenerId);
			}
		});
		return await tcs.Task;
	}
}