using System.Collections.Generic;
using tairasoul.unity.common.networking.interfaces;
using UnityEngine;

namespace tairasoul.unity.common.networking.sync;

abstract class PlayerSyncComponent : MonoBehaviour {
	internal static List<PlayerSyncComponent> ActiveNetworked = [];
	public static PlayerSyncComponent ours { get; internal set; }
	public ushort player;
	public abstract void Synchronize();
	public abstract void Synchronize<T>(T packet) where T : IPacket;
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