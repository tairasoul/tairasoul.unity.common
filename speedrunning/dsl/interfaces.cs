using tairasoul.unity.common.speedrunning.dsl.internals;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl;

public interface IBoundsRegistry {
	/// <summary>
	/// The method called when a bound becomes active.
	/// </summary>
	/// <param name="bounds">A wrapper around a pointer to the UnityEngine.Bounds instance on the current SplitFile class.</param>
	public void BoundCreated(BoundsPtrWrapper bounds);
	/// <summary>
	/// The method called when a bound becomes inactive.
	/// </summary>
	/// <param name="bounds">A wrapper around a pointer to the UnityEngine.Bounds instance on the current SplitFile class.</param>
	public void BoundDestroyed(BoundsPtrWrapper bounds);
	/// <summary>
	/// This method is called after updating bounds that are bound to objects.
	/// </summary>
	public void CheckBounds();
}