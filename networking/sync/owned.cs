using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;
using UnityEngine;

namespace tairasoul.unity.common.networking.sync;

public abstract class BaseOwnedSyncComponent : MonoBehaviour {
	internal static Dictionary<ulong, BaseOwnedSyncComponent> ActiveNetworked = [];
	public ulong objectId;
	public abstract void Synchronize();
	public abstract void Synchronize<T>(T packet) where T : IPacket;
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
}