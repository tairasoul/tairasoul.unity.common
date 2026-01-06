using System.Linq.Expressions;

namespace tairasoul.unity.common.speedrunning.runtime;

// proxy purely because older Mono versions have a Reflection.Emit implementation issue that causes interface methods
// to not be bound properly and thus cause a VTable error when you attempt to create that class
class SplitFileProxy {
	object file;
	Func<bool> isCompletedAction;
	Action callCurrentSplitAction;
	Action resetAction;
	Action startListenersAction;

	public SplitFileProxy(object file) {
		this.file = file;
		Expression instanceExpr = Expression.Constant(file);
		Expression isCompletedExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("IsCompleted"));
		Expression callCurrentSplitExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("CallCurrentSplit"));
		Expression resetExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("Reset"));
		Expression startListenersExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("StartListeners"));
		isCompletedAction = Expression.Lambda<Func<bool>>(isCompletedExpr).Compile();
		callCurrentSplitAction = Expression.Lambda<Action>(callCurrentSplitExpr).Compile();
		resetAction = Expression.Lambda<Action>(resetExpr).Compile();
		startListenersAction = Expression.Lambda<Action>(startListenersExpr).Compile();
	}

	public bool IsCompleted() => isCompletedAction();
	public void CallCurrentSplit() => callCurrentSplitAction();
	public void Reset() => resetAction();
	public void StartListeners() => startListenersAction();
}