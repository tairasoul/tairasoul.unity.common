using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl;

public interface IBoundsRegistry {
	/// <summary>
	/// The method called when a bound becomes active.
	/// </summary>
	/// <param name="bounds">The UnityEngine.Bounds instance.</param>
	public void BoundCreated(Bounds bounds);
	/// <summary>
	/// The method called when a bound becomes inactive.
	/// </summary>
	/// <param name="bounds">The UnityEngine.Bounds instance.</param>
	public void BoundDestroyed(Bounds bounds);
}