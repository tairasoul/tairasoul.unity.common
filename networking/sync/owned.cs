using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.util;
using UnityEngine;

namespace tairasoul.unity.common.networking.sync;

/// <summary>
/// a component representing any synced object in the game world
/// </summary>
public abstract class BaseOwnedSyncComponent
#if UNITY_ENVIRONMENT
	: MonoBehaviour
#endif
{
	public static Dictionary<ulong, BaseOwnedSyncComponent> ActiveNetworked = [];
	/// <summary>
	/// object id for this component
	/// </summary>
	public ulong objectId;

	ulong idCachedFor = ulong.MaxValue;
	ushort cachedPlayer;
	/// <summary>
	/// extracted player id from the object id
	/// </summary>
	public ushort playerId {
		get {
			if (idCachedFor != objectId) {
				cachedPlayer = ObjectIdUtils.ExtractPlayerID(objectId);
				idCachedFor = objectId;
			}
			return cachedPlayer;
		}
	}
	/// <summary>
	/// sends state over the network
	/// </summary>
	public abstract void Synchronize();
	/// <summary>
	/// receives state over the network
	/// </summary>
	/// <typeparam name="T">the packet type</typeparam>
	/// <param name="packet">the state to check against</param>
	public abstract void Synchronize<T>(T packet) where T : IPacket;
#if UNITY_ENVIRONMENT
	public virtual void Start() {
		if (!ActiveNetworked.ContainsKey(objectId))
			ActiveNetworked.Add(objectId, this);
	}

	public virtual void OnDestroy() {
		if (ActiveNetworked.ContainsKey(objectId))
			ActiveNetworked.Remove(objectId);
	}

	public virtual void OnEnable() {
		if (!ActiveNetworked.ContainsKey(objectId))
			ActiveNetworked.Add(objectId, this);
	}

	public virtual void OnDisable()
	{
		if (ActiveNetworked.ContainsKey(objectId))
			ActiveNetworked.Remove(objectId);
	}
#else
	public virtual void Enable() {
		if (!ActiveNetworked.ContainsKey(objectId))
			ActiveNetworked.Add(objectId, this);
	}

	public virtual void Disable() {
		if (ActiveNetworked.ContainsKey(objectId))
			ActiveNetworked.Remove(objectId);
	}
#endif
}