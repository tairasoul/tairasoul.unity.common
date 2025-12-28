namespace tairasoul.unity.common.speedrunning.dsl.config;

public static class DslCompilationConfig {
	internal static Type? MethodCallClass;
	internal static IBoundsRegistry BoundsRegistryClass = null!;

	/// <summary>
	/// Set the static class used for call nodes.
	/// </summary>
	/// <typeparam name="T">The static class used. Has to be public.</typeparam>
	public static void SetMethodCallClass<T>() {
		MethodCallClass = typeof(T);
	}

	/// <summary>
	/// Set the bounds registry instance used for bounds.
	/// </summary>
	/// <typeparam name="T">Type of registry.</typeparam>
	/// <param name="registry">Registry instance.</param>
	public static void SetBoundsRegistryClass<T>(T registry) where T : IBoundsRegistry {
		BoundsRegistryClass = registry;
	}
}