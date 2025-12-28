using tairasoul.unity.common.events;

namespace tairasoul.unity.common.speedrunning.dsl.eventbus;

public static class EventTypeRegistry {
	static Dictionary<DslId, Type[]> types = [];

	internal static Type[] GetRegistered(string id) {
		return types[new(id)];
	}

	public static void Register(string id, Type[] types) {
		EventTypeRegistry.types[new(id)] = types;
	}
}