using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tairasoul.unity.common.hashing;

namespace tairasoul.unity.common.events;

record EventListener(string id, Action<EventData> listener);
#if PUBLIC_EVENTBUS
public abstract record EventID();
public abstract record EventData();

public static class EventBus
{
#else
abstract record EventID();
abstract record EventData();

static class EventBus {
#endif
	static ConcurrentDictionary<EventID, List<EventListener>> listeners = [];

	public static void Listen<T>(T eventId, string listenerId, Action<EventData> listener)
		where T : EventID
	{
		if (listeners.TryGetValue(eventId, out var list))
		{
			list.Add(new(listenerId, listener));
		}
		else
		{
			listeners[eventId] = [new(listenerId, listener)];
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
		string listenerId = Murmur3.Hash128($"{DateTime.Now}_autoListenerWaitFor");
		TaskCompletionSource<H> tcs = new();
		Listen(eventId, listenerId, ed =>
		{
			if (predicate((H)ed))
			{
				tcs.SetResult((H)ed);
				StopListening(eventId, listenerId);
			}
		});
		return await tcs.Task;
	}
}