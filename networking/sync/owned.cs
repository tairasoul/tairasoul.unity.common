using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;
using UnityEngine;

namespace tairasoul.unity.common.networking.sync;

abstract class BaseOwnedSyncComponent : MonoBehaviour {
	internal static List<BaseOwnedSyncComponent> ActiveNetworked = [];
	public ulong objectId;
	public abstract void Synchronize();
	public abstract void Synchronize<T>(T packet) where T : IPacket;
	protected static Func<Task<BaseOwnedSyncComponent>> RequestCreation;
	public static async Task<BaseOwnedSyncComponent> RequestCreate() => await RequestCreation();
	public void Start() {
		if (!ActiveNetworked.Contains(this))
			ActiveNetworked.Add(this);
	}

	public void OnDestroy() {
		if (ActiveNetworked.Contains(this))
			ActiveNetworked.Remove(this);
	}

	public void OnEnable() {
		if (!ActiveNetworked.Contains(this))
			ActiveNetworked.Add(this);
	}

	public void OnDisable()
	{
		if (ActiveNetworked.Contains(this))
			ActiveNetworked.Remove(this);
	}
}