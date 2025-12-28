using System.Reflection.Emit;

namespace tairasoul.unity.common.speedrunning.dsl.compiler;

interface IVisitor {
	public void Visit(TranslationSplit split);
	public void Visit(TranslationAny any);
	public void Visit(TranslationAll all);
	public void Visit(TranslationTimerCall timer);
	public void Visit(TranslationRunImmediate immediate);
	public void Visit(TranslationComparison comparison, Label shortCircuit);
	public void Visit(TranslationBinary binary);
	public void Visit(TranslationIf _if);
	public void Visit(TranslationElse _else);
	public void Visit(TranslationElseIf _elseif);
	public void Visit(TranslationLiteral literal);
	public void Visit(TranslationVariableReference varRef);
	public void Visit(TranslationMethodCall method);
	public void Visit(TranslationBoundsGrouped grouped);
	public void Visit(TranslationBoundsObjectGrouped grouped);
	public void Visit(TranslationBoundsObjectSizeGrouped grouped);
	public void Visit(TranslationEventListen evnetListen, IEnumerable<TranslationResult> body);
	public void Visit(TranslationEventListenGrouped grouped);
	public void Visit(TranslationFulfilled ct);
	public void Visit(TranslationFullConditionNode fullCond);
	public void Visit(TranslationObjectCondition objectCond, IEnumerable<TranslationResult> body);
	public void Visit(TranslationObjectComponentCondition objectCond, IEnumerable<TranslationResult> body);
}