using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tairasoul.unity.common.events;

abstract record EventID();
abstract record EventData();
record EventListener(string id, Action<EventData> listener);

static class EventBus {
	static ConcurrentDictionary<EventID, List<EventListener>> listeners = [];

	public static void Listen<T>(T eventId, string listenerId, Action<EventData> listener) 
		where T : EventID {
		if (listeners.TryGetValue(eventId, out var list)) {
			list.Add(new(listenerId, listener));
		}
		else {
			listeners[eventId] = [new(listenerId, listener)];
		}
	}

	public static void Send<T>(T eventId, EventData eventData)
		where T : EventID {
		if (listeners.TryGetValue(eventId, out var list)) {
			foreach (var listener in list) {
				listener.listener(eventData);
			}
		}
	}

	public static void StopListening<T>(T eventId, string id)
		where T : EventID {
		if (listeners.TryGetValue(eventId, out var list))
		{
			var toRemove = list.FirstOrDefault(l => l.id == id);
			if (toRemove != null) list.Remove(toRemove);
		}
	}
	
	unsafe static string Hash(string str) {
		// fnv hash
		// still need to implement t1ha2 so i dont copy this code everywhere i need a hash function		
		ulong hash = 14695981039346656037;
		ReadOnlySpan<byte> bytes = Encoding.Unicode.GetBytes(str);

		fixed (byte* ptr = bytes) {
			int length = bytes.Length;
			for (int i = 0; i < length; i++) {
				hash ^= ptr[i];
				hash *= 1099511628211;
			}
		}
    
		string hashString = hash.ToString("x8");
		return $"hash_{hashString}_";
	}

	public static async Task<H> WaitFor<T, H>(T eventId, Func<H, bool> predicate)
		where T : EventID
		where H : EventData {
		string listenerId = Hash($"{DateTime.Now}_autoListenerWaitFor");
		TaskCompletionSource<H> tcs = new();
		Listen(eventId, listenerId, ed =>
		{
			if (predicate((H)ed)) {
				tcs.SetResult((H)ed);
				StopListening(eventId, listenerId);
			}
		});
		return await tcs.Task;
	}
}