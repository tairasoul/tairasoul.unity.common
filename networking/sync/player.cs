using System.Collections.Generic;
using tairasoul.unity.common.networking.interfaces;
using UnityEngine;

namespace tairasoul.unity.common.networking.sync;

/// <summary>
/// a sync component representing the player. there should only be one of these per player
/// </summary>
public abstract class PlayerSyncComponent
#if UNITY_ENVIRONMENT
	: MonoBehaviour
#endif
{
	public static Dictionary<ushort, PlayerSyncComponent> ActiveNetworked = [];
	public static PlayerSyncComponent ours = null;
	/// <summary>
	/// the player this sync component belongs to
	/// </summary>
	public ushort player;
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
	public virtual void Start()
	{
		ours = this;
	}

	public virtual void OnDestroy()
	{
		ours = null;
	}

	public virtual void OnEnable()
	{
		ours = this;
	}

	public virtual void OnDisable()
	{
		ours = null;
	}
#else
	public virtual void Enable() {
		ours = this;
	}

	public virtual void Disable() {
		ours = null;
	}
#endif
}