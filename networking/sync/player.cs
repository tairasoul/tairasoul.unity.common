using System.Collections.Generic;
using tairasoul.unity.common.networking.interfaces;
using UnityEngine;

namespace tairasoul.unity.common.networking.sync;

public abstract class PlayerSyncComponent : MonoBehaviour {
	internal static Dictionary<ushort, PlayerSyncComponent> ActiveNetworked = [];
	public static List<PlayerSyncComponent> ours = [];
	public ushort player;
	public abstract void Synchronize();
	public abstract void Synchronize<T>(T packet) where T : IPacket;
	public virtual void Start() {
		if (!ActiveNetworked.ContainsKey(player))
			ActiveNetworked.Add(player, this);
	}

	public virtual void OnDestroy() {
		if (ActiveNetworked.ContainsKey(player))
			ActiveNetworked.Remove(player);
	}

	public virtual void OnEnable() {
		if (!ActiveNetworked.ContainsKey(player))
			ActiveNetworked.Add(player, this);
	}

	public virtual void OnDisable()
	{
		if (ActiveNetworked.ContainsKey(player))
			ActiveNetworked.Remove(player);
	}
}