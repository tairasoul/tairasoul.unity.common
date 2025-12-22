using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.sync;

namespace tairasoul.unity.common.networking.components;

abstract record CreationData();

static class ComponentCreatorRegistry {
	static Dictionary<Type, IComponentCreator> registeredCreators = [];

	public static void Register<T>() where T : IComponentCreator, new() {
		T instance = new();
		if (registeredCreators.Any((v) => v.Key == typeof(T))) return;
		registeredCreators.Add(typeof(T), instance);
	}

	public static async Task<BaseOwnedSyncComponent> Create<T>() where T : IComponentCreator, new() {
		return await Create<T>((_) => true);
	}

	public static async Task<BaseOwnedSyncComponent> Create<T>(Func<CreationData, bool> predicate) where T : IComponentCreator, new() {
		if (registeredCreators.TryGetValue(typeof(T), out IComponentCreator creator)) {
			return await creator.RequestCreation(predicate);
		}
		return null;
	}
}