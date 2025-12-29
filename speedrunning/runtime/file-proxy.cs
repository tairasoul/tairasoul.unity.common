using System.Linq.Expressions;

namespace tairasoul.unity.common.speedrunning.runtime;

// proxy purely because older Mono versions have a Reflection.Emit implementation issue that causes interface methods
// to not be bound properly and thus cause a VTable error when you attempt to create that class
class SplitFileProxy {
	object file;
	Expression isCompletedExpr;
	Expression callCurrentSplitExpr;
	Expression resetExpr;
	Func<bool> isCompletedAction;
	Action callCurrentSplitAction;
	Action resetAction;

	public SplitFileProxy(object file) {
		this.file = file;
		Expression instanceExpr = Expression.Constant(file);
		isCompletedExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("IsCompleted"));
		callCurrentSplitExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("CallCurrentSplit"));
		resetExpr = Expression.Call(instanceExpr, file.GetType().GetMethod("Reset"));
		isCompletedAction = Expression.Lambda<Func<bool>>(isCompletedExpr).Compile();
		callCurrentSplitAction = Expression.Lambda<Action>(callCurrentSplitExpr).Compile();
		resetAction = Expression.Lambda<Action>(resetExpr).Compile();
	}

	public bool IsCompleted() => isCompletedAction();
	public void CallCurrentSplit() => callCurrentSplitAction();
	public void Reset() => resetAction();
}