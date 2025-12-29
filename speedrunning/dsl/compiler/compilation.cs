using System.Reflection;
using System.Reflection.Emit;
using tairasoul.unity.common.events;
using tairasoul.unity.common.speedrunning.dsl.config;
using tairasoul.unity.common.speedrunning.dsl.eventbus;
using tairasoul.unity.common.speedrunning.dsl.internals;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.compiler;

class CompilationVisitor : IVisitor {
	static Type InternalDslOperationsType = typeof(InternalDslOperations);
	static Type EventBusType = typeof(EventBus);
	static Type DslIdType = typeof(DslId);
	static Type DslDataType = typeof(DslData);
	static Type SplitCompleted = typeof(DslSplitCompleted);
	static Type SplitCompletedData = typeof(DslSplitCompletionData);
	static Type BoundEntered = typeof(DslPlayerEnteredBounds);
	static Type BoundEnteredData = typeof(DslBoundEntered);
	static Type BoundLeft = typeof(DslPlayerLeftBounds);
	static Type BoundLeftData = typeof(DslBoundLeft);
	static MethodInfo EventBusSend = EventBusType.GetMethod("Send", BindingFlags.Public | BindingFlags.Static);
	static MethodInfo EventBusListen = EventBusType.GetMethod("Listen", BindingFlags.Public | BindingFlags.Static);
	static MethodInfo EventBusStop = EventBusType.GetMethod("StopListening", BindingFlags.Public | BindingFlags.Static);
	static MethodInfo DslOperationsTimerCall = InternalDslOperationsType.GetMethod("Timer");
	static MethodInfo BoundsSetMinMax = typeof(Bounds).GetMethod("SetMinMax");
	static MethodInfo BoundCreated = typeof(IBoundsRegistry).GetMethod("BoundCreated");
	static MethodInfo BoundDestroyed = typeof(IBoundsRegistry).GetMethod("BoundDestroyed");
	static MethodInfo BoundsBindPartial = typeof(BoundsBinder).GetMethod("BindPartial");
	static MethodInfo BoundsBindFull = typeof(BoundsBinder).GetMethod("BindFull");
	static MethodInfo BoundsUnbind = typeof(BoundsBinder).GetMethod("Unbind");
	static MethodInfo DslDataArgs = DslDataType.GetProperty("args").GetGetMethod();
	static MethodInfo AccessorUtilGet = typeof(AccessorUtil).GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
	static MethodInfo AccessorUtilFindGameObject = typeof(AccessorUtil).GetMethod("FindGameObject", BindingFlags.Public | BindingFlags.Static);
	static MethodInfo GetComponent = typeof(GameObject).GetMethods().FirstOrDefault(m => m.Name == "GetComponent" && m.GetParameters().Length == 0 && m.GetGenericArguments().Length == 1);

	TypeBuilder currentClass;
	MethodBuilder currentMethod;
	ILGenerator currentGenerator;
	MethodBuilder previousMethod;
	ILGenerator previousGenerator;
	FieldBuilder dslOperationsField;
	FieldBuilder boundRegistryField;
	FieldBuilder splitIndexField;
	Dictionary<string, MethodBuilder> methods = [];
	Dictionary<string, LocalBuilder> locals = [];
	List<MethodBuilder> activeInactiveBounds = [];
	List<FieldBuilder> falseInits = [];
	List<FieldBuilder> boundResets = [];
	int events = 0;
	int bounds = 0;
	ConstructorBuilder constructor;

	public CompilationVisitor(ModuleBuilder module, IEnumerable<string> conditionOrder) {
		currentClass = module.DefineType("SplitFile", TypeAttributes.Class | TypeAttributes.Public);
		dslOperationsField = currentClass.DefineField("dslOperations", InternalDslOperationsType, FieldAttributes.Private);
		boundRegistryField = currentClass.DefineField("boundRegistry", typeof(IBoundsRegistry), FieldAttributes.Private);
		splitIndexField = currentClass.DefineField("splitIndex", typeof(int), FieldAttributes.Private);
		MethodBuilder fulfilled = currentClass.DefineMethod("IsCompleted", MethodAttributes.Public, CallingConventions.HasThis, typeof(bool), []);
		ILGenerator fg = fulfilled.GetILGenerator();
		fg.Emit(OpCodes.Ldarg_0);
		fg.Emit(OpCodes.Ldfld, splitIndexField);
		fg.Emit(OpCodes.Ldc_I4, conditionOrder.Count());
		fg.Emit(OpCodes.Ceq);
		fg.Emit(OpCodes.Ret);
		MethodBuilder ccs = currentClass.DefineMethod("CallCurrentSplit", MethodAttributes.Public, typeof(void), []);
		ILGenerator cgen = ccs.GetILGenerator();
		Label[] switchLabels = new Label[conditionOrder.Count()];
		for (int i = 0; i < conditionOrder.Count(); i++)
			switchLabels[i] = cgen.DefineLabel();
		cgen.Emit(OpCodes.Ldarg_0);
		cgen.Emit(OpCodes.Ldfld, splitIndexField);
		cgen.Emit(OpCodes.Switch, switchLabels);
		cgen.Emit(OpCodes.Ret);
		for (int i = 0; i < conditionOrder.Count(); i++) {
			cgen.MarkLabel(switchLabels[i]);
			cgen.Emit(OpCodes.Ldarg_0);
			MethodBuilder method = currentClass.DefineMethod(conditionOrder.ElementAt(i) + "condition", MethodAttributes.Public, CallingConventions.HasThis, typeof(bool), []);
			methods[conditionOrder.ElementAt(i)] = method;
			Label doTrue = cgen.DefineLabel();
			cgen.Emit(OpCodes.Call, method);
			cgen.Emit(OpCodes.Brtrue_S, doTrue);
			cgen.Emit(OpCodes.Ret);
			cgen.MarkLabel(doTrue);
			cgen.Emit(OpCodes.Ldarg_0);
			EmitInt(cgen, i + 1);
			cgen.Emit(OpCodes.Stfld, splitIndexField);
			cgen.Emit(OpCodes.Newobj, SplitCompleted.GetConstructors().First());
			cgen.Emit(OpCodes.Ldstr, conditionOrder.ElementAt(i));
			cgen.Emit(OpCodes.Newobj, SplitCompletedData.GetConstructor([typeof(string)]));
			cgen.Emit(OpCodes.Call, EventBusSend.MakeGenericMethod(SplitCompleted));
			cgen.Emit(OpCodes.Ret);
		}
		constructor = currentClass.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, [InternalDslOperationsType, typeof(IBoundsRegistry)]);
		ILGenerator gen = constructor.GetILGenerator();
		gen.Emit(OpCodes.Ldarg_0);
		gen.Emit(OpCodes.Ldarg_0);
		gen.Emit(OpCodes.Ldarg_1);
		gen.Emit(OpCodes.Stfld, dslOperationsField);
		gen.Emit(OpCodes.Ldarg_2);
		gen.Emit(OpCodes.Stfld, boundRegistryField);
	}

	public void Finish() {
		ILGenerator gen = constructor.GetILGenerator();
		foreach (var fieldInit in falseInits) {
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldc_I4_0);
			gen.Emit(OpCodes.Stfld, fieldInit);
		}
		gen.Emit(OpCodes.Ret);
		MethodBuilder reset = currentClass.DefineMethod("Reset", MethodAttributes.Public, CallingConventions.HasThis, typeof(void), []);
		ILGenerator rg = reset.GetILGenerator();
		rg.Emit(OpCodes.Ldarg_0);
		rg.Emit(OpCodes.Ldc_I4_0);
		rg.Emit(OpCodes.Stfld, splitIndexField);
		foreach (var fieldInit in falseInits) {
			rg.Emit(OpCodes.Ldarg_0);
			rg.Emit(OpCodes.Ldc_I4_0);
			rg.Emit(OpCodes.Stfld, fieldInit);
		}
		LocalBuilder boundLocal = rg.DeclareLocal(typeof(Bounds));
		foreach (var bound in boundResets) {
			LoadLocalA(rg, boundLocal);
			rg.Emit(OpCodes.Initobj, typeof(Bounds));
			rg.Emit(OpCodes.Ldarg_0);
			LoadLocal(rg, boundLocal);
			rg.Emit(OpCodes.Stfld, bound);
		}
		rg.Emit(OpCodes.Ret);
		currentClass.CreateType();
	}

	public void Visit(TranslationSplit split) {
		currentMethod = methods[split.splitName];
		currentGenerator = currentMethod.GetILGenerator();
		if (split.content is TranslationAny any) {
			Visit(any);
		}
		else if (split.content is TranslationAll all) {
			Visit(all);
		}
		else if (split.content is TranslationFullConditionNode fc) {
			Visit(fc);
			foreach (var method in activeInactiveBounds) {
				currentGenerator.Emit(OpCodes.Ldarg_0);
				currentGenerator.Emit(OpCodes.Call, method);
			}
			currentGenerator.Emit(OpCodes.Ret);
			activeInactiveBounds.Clear();
		}
		else if (split.content is TranslationRunImmediate ri) {
			Visit(ri);
		}
	}

	void VisitLogicNode(TranslationResult res) {
		if (res is TranslationTimerCall timerCall)
			Visit(timerCall);
		else if (res is TranslationFulfilled fulfilled)
			Visit(fulfilled);
		else if (res is TranslationMethodCall method)
			Visit(method);
		else if (res is TranslationIf _subif)
			Visit(_subif);
	}

	public void Visit(TranslationAny any) {
		previousMethod = currentMethod;
		previousGenerator = currentGenerator;
		List<MethodInfo> ConditionMethods = [];
		for (int i = 0; i < any.conditions.Count(); i++) {
			currentMethod = currentClass.DefineMethod(previousMethod.Name + $"_cond{i}", MethodAttributes.Public, CallingConventions.HasThis, typeof(bool), []);
			ConditionMethods.Add(currentMethod);
			currentGenerator = currentMethod.GetILGenerator();
			var condition = any.conditions.ElementAt(i);
			if (condition is TranslationFullConditionNode fc)
				Visit(fc);
			currentGenerator.Emit(OpCodes.Ret);
		}
		currentMethod = previousMethod;
		currentGenerator = previousGenerator;
		Label splitNotCompleted = currentGenerator.DefineLabel();
		Label RunLogic = currentGenerator.DefineLabel();
		for (int i = 0; i < any.conditions.Count(); i++)
		{
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Call, ConditionMethods[i]);
			currentGenerator.Emit(OpCodes.Brtrue, RunLogic);
		}
		currentGenerator.Emit(OpCodes.Br, splitNotCompleted);
		currentGenerator.MarkLabel(RunLogic);
		foreach (var method in activeInactiveBounds) {
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Call, method);
		}
		activeInactiveBounds.Clear();
		foreach (var logic in any.body) {
			VisitLogicNode(logic);
		}
		currentGenerator.MarkLabel(splitNotCompleted);
		currentGenerator.Emit(OpCodes.Ldc_I4_0);
		currentGenerator.Emit(OpCodes.Ret);
	}

	public void Visit(TranslationAll all) {
		previousMethod = currentMethod;
		previousGenerator = currentGenerator;
		List<MethodInfo> ConditionMethods = [];
		for (int i = 0; i < all.conditions.Count(); i++) {
			currentMethod = currentClass.DefineMethod(previousMethod.Name + $"_cond{i}", MethodAttributes.Public, CallingConventions.HasThis, typeof(bool), []);
			ConditionMethods.Add(currentMethod);
			currentGenerator = currentMethod.GetILGenerator();
			var condition = all.conditions.ElementAt(i);
			if (condition is TranslationFullConditionNode fc)
				Visit(fc);
			currentGenerator.Emit(OpCodes.Ret);
		}
		currentMethod = previousMethod;
		currentGenerator = previousGenerator;
		Label splitNotCompleted = currentGenerator.DefineLabel();
		for (int i = 0; i < all.conditions.Count(); i++)
		{
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Call, ConditionMethods[i]);
			currentGenerator.Emit(OpCodes.Brfalse, splitNotCompleted);
		}
		foreach (var method in activeInactiveBounds) {
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Call, method);
		}
		activeInactiveBounds.Clear();
		foreach (var logic in all.body) {
			VisitLogicNode(logic);
		}
		currentGenerator.MarkLabel(splitNotCompleted);
		currentGenerator.Emit(OpCodes.Ldc_I4_0);
		currentGenerator.Emit(OpCodes.Ret);
	}

	void EmitInt(ILGenerator gen, int i) {
		switch (i) {
			case 0:
				gen.Emit(OpCodes.Ldc_I4_0);
				break;
			case 1:
				gen.Emit(OpCodes.Ldc_I4_1);
				break;
			case 2:
				gen.Emit(OpCodes.Ldc_I4_2);
				break;
			case 3:
				gen.Emit(OpCodes.Ldc_I4_3);
				break;
			case 4:
				gen.Emit(OpCodes.Ldc_I4_4);
				break;
			case 5:
				gen.Emit(OpCodes.Ldc_I4_5);
				break;
			case 6:
				gen.Emit(OpCodes.Ldc_I4_6);
				break;
			case 7:
				gen.Emit(OpCodes.Ldc_I4_7);
				break;
			case 8:
				gen.Emit(OpCodes.Ldc_I4_8);
				break;
			case -1:
				gen.Emit(OpCodes.Ldc_I4_M1);
				break;
			default:
				if (i >= -128 && i <= 127)
					gen.Emit(OpCodes.Ldc_I4_S, (sbyte)i);
				else 
					gen.Emit(OpCodes.Ldc_I4, i);
				break;
		}
	}

	void StoreLocal(ILGenerator gen, int idx) {
		switch (idx) {
			case 0:
				gen.Emit(OpCodes.Stloc_0);
				break;
			case 1:
				gen.Emit(OpCodes.Stloc_1);
				break;
			case 2:
				gen.Emit(OpCodes.Stloc_2);
				break;
			case 3:
				gen.Emit(OpCodes.Stloc_3);
				break;
			default:
				if (idx <= 127 && idx >= -128)
					gen.Emit(OpCodes.Stloc_S, (sbyte)idx);
				else
					gen.Emit(OpCodes.Stloc, idx);
				break;
		}
	}

	void LoadLocalA(ILGenerator gen, LocalBuilder localIdx) {
		LoadLocalA(gen, localIdx.LocalIndex);
	}

	void LoadLocal(ILGenerator gen, LocalBuilder localIdx) {
		LoadLocal(gen, localIdx.LocalIndex);
	}

	void StoreLocal(ILGenerator gen, LocalBuilder idx) {
		StoreLocal(gen, idx.LocalIndex);
	}

	void LoadLocalA(ILGenerator gen, int localIdx) {
		if (localIdx >= -128 && localIdx <= 127)
			gen.Emit(OpCodes.Ldloca_S, (sbyte)localIdx);
		else
			gen.Emit(OpCodes.Ldloca, localIdx);
	}

	void LoadLocal(ILGenerator gen, int localIdx) {
		switch (localIdx) {
			case 0:
				gen.Emit(OpCodes.Ldloc_0);
				break;
			case 1:
				gen.Emit(OpCodes.Ldloc_1);
				break;
			case 2:
				gen.Emit(OpCodes.Ldloc_2);
				break;
			case 3:
				gen.Emit(OpCodes.Ldloc_3);
				break;
			default:
				if (localIdx >= -128 && localIdx <= 127)
					gen.Emit(OpCodes.Ldloc_S, (sbyte)localIdx);
				else
					gen.Emit(OpCodes.Ldloc, localIdx);
				break;
		}
	}

	public void Visit(TranslationTimerCall timer) {
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldfld, dslOperationsField);
		int timerI = (int)timer.operation;
		EmitInt(currentGenerator, timerI);
		currentGenerator.Emit(OpCodes.Call, DslOperationsTimerCall);
	}

	public void Visit(TranslationComparison comparison, Label shortCircuit) {
		TranslationResult lh = comparison.lh;
		ComparisonType comparisonType = comparison.comparison;
		TranslationResult rh = comparison.rh;
		if (lh is TranslationVariableReference lref) {
			Visit(lref);
		}
		else if (lh is TranslationLiteral lliteral)
			Visit(lliteral);
		else if (lh is TranslationBinary lbinary)
			Visit(lbinary);
		if (rh is TranslationVariableReference rref) {
			Visit(rref);
		}
		else if (rh is TranslationLiteral rliteral)
			Visit(rliteral);
		else if (rh is TranslationBinary rbinary)
			Visit(rbinary);
		switch (comparisonType) {
			case ComparisonType.Equals:
				currentGenerator.Emit(OpCodes.Ceq);
				currentGenerator.Emit(OpCodes.Brfalse, shortCircuit);
				break;
			case ComparisonType.LessOrEqual:
				currentGenerator.Emit(OpCodes.Bgt, shortCircuit);
				break;
			case ComparisonType.LessThan:
				currentGenerator.Emit(OpCodes.Bge, shortCircuit);
				break;
			case ComparisonType.GreaterOrEqual:
				currentGenerator.Emit(OpCodes.Blt, shortCircuit);
				break;
			case ComparisonType.GreaterThan:
				currentGenerator.Emit(OpCodes.Ble, shortCircuit);
				break;
		}
	}

	void EmitAdd(TranslationLiteral ll, TranslationLiteral rl) {
		if (ll.value is float lf1 && rl.value is float rf1)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf1 + rf1);
		}
		else if (ll.value is int li1 && rl.value is int ri1)
		{
			EmitInt(currentGenerator, li1 + ri1);
		}
		else if (ll.value is float lf2 && rl.value is int ri2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf2 + ri2);
		}
		else if (ll.value is int li2 && rl.value is float rf2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, li2 + rf2);
		}
	}

	void EmitSubtract(TranslationLiteral ll, TranslationLiteral rl) {
		if (ll.value is float lf1 && rl.value is float rf1)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf1 - rf1);
		}
		else if (ll.value is int li1 && rl.value is int ri1)
		{
			EmitInt(currentGenerator, li1 - ri1);
		}
		else if (ll.value is float lf2 && rl.value is int ri2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf2 - ri2);
		}
		else if (ll.value is int li2 && rl.value is float rf2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, li2 - rf2);
		}
	}

	void EmitMultiply(TranslationLiteral ll, TranslationLiteral rl) {
		if (ll.value is float lf1 && rl.value is float rf1)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf1 * rf1);
		}
		else if (ll.value is int li1 && rl.value is int ri1)
		{
			EmitInt(currentGenerator, li1 * ri1);
		}
		else if (ll.value is float lf2 && rl.value is int ri2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf2 * ri2);
		}
		else if (ll.value is int li2 && rl.value is float rf2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, li2 * rf2);
		}
	}

	void EmitDivide(TranslationLiteral ll, TranslationLiteral rl) {
		if (ll.value is float lf1 && rl.value is float rf1)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf1 / rf1);
		}
		else if (ll.value is int li1 && rl.value is int ri1)
		{
			EmitInt(currentGenerator, li1 / ri1);
		}
		else if (ll.value is float lf2 && rl.value is int ri2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, lf2 / ri2);
		}
		else if (ll.value is int li2 && rl.value is float rf2)
		{
			currentGenerator.Emit(OpCodes.Ldc_R4, li2 / rf2);
		}
	}

	public void Visit(TranslationBinary binary) {
		if (binary.lh is TranslationLiteral ll && binary.rh is TranslationLiteral rl)
		{
			switch (binary.operation)
			{
				case BinaryOperation.Add:
					EmitAdd(ll, rl);
					break;
				case BinaryOperation.Subtract:
					EmitSubtract(ll, rl);
					break;
				case BinaryOperation.Multiply:
					EmitMultiply(ll, rl);
					break;
				case BinaryOperation.Divide:
					EmitDivide(ll, rl);
					break;
			}
		}
		else if (binary.rh is TranslationBinary bin)
		{
			Visit(bin);
			if (binary.lh is TranslationLiteral lliteral)
				Visit(lliteral);
			else if (binary.lh is TranslationVariableReference lref) {
				Visit(lref);
			}
		}
		else {
			if (binary.lh is TranslationLiteral lliteral)
				Visit(lliteral);
			else if (binary.lh is TranslationVariableReference lref) {
				Visit(lref);
			}
			if (binary.rh is TranslationLiteral rliteral)
				Visit(rliteral);
			else if (binary.rh is TranslationVariableReference rref) {
				Visit(rref);
			}
		}
		switch (binary.operation) {
			case BinaryOperation.Add:
				currentGenerator.Emit(OpCodes.Add);
				break;
			case BinaryOperation.Subtract:
				currentGenerator.Emit(OpCodes.Sub);
				break;
			case BinaryOperation.Divide:
				currentGenerator.Emit(OpCodes.Div);
				break;
			case BinaryOperation.Multiply:
				currentGenerator.Emit(OpCodes.Mul);
				break;
		}
	}

	public void Visit(TranslationIf _if) {
		Label Short = currentGenerator.DefineLabel();
		TranslationLogicalChain logical = (TranslationLogicalChain)_if.condition;
		Visit((TranslationComparison)logical.first, Short);
		foreach (var (_, next) in logical.rest) {
			Visit((TranslationComparison)next, Short);
		}
		foreach (var logic in _if.body) {
			VisitLogicNode(logic);
		}
		currentGenerator.MarkLabel(Short);
		if (_if._else is TranslationElse _else)
			Visit(_else);
		else if (_if._else is TranslationElseIf _elseif)
			Visit(_elseif);
	}

	public void Visit(TranslationElse _else) {
		foreach (var logic in _else.body) {
			VisitLogicNode(logic);
		}
	}

	public void Visit(TranslationElseIf _elseif) {
		Label Short = currentGenerator.DefineLabel();
		TranslationLogicalChain logical = (TranslationLogicalChain)_elseif.condition;
		Visit((TranslationComparison)logical.first, Short);
		foreach (var (_, next) in logical.rest) {
			Visit((TranslationComparison)next, Short);
		}
		foreach (var logic in _elseif.body) {
			VisitLogicNode(logic);
		}
		currentGenerator.MarkLabel(Short);
		if (_elseif._else is TranslationElse _else)
			Visit(_else);
		else if (_elseif._else is TranslationElseIf _elseif2)
			Visit(_elseif2);
	}

	public void Visit(TranslationLiteral literal) {
		if (literal.value.GetType() == typeof(int))
			EmitInt(currentGenerator, (int)literal.value);
		else if (literal.value.GetType() == typeof(float))
			currentGenerator.Emit(OpCodes.Ldc_R4, (float)literal.value);
		else if (literal.value.GetType() == typeof(string))
			currentGenerator.Emit(OpCodes.Ldstr, (string)literal.value);
		else if (literal.value.GetType() == typeof(bool))
			if ((bool)literal.value)
				currentGenerator.Emit(OpCodes.Ldc_I4_1);
			else
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
	}

	public void Visit(TranslationMethodCall method) {
		if (DslCompilationConfig.MethodCallClass != null) {
			MethodInfo methodinf = DslCompilationConfig.MethodCallClass.GetMethod(method.name, BindingFlags.Static | BindingFlags.Public);
			foreach (var arg in method.args) {
				if (arg is TranslationLiteral literal)
					Visit(literal);
				else if (arg is TranslationVariableReference refer)
					Visit(refer);
			}
			currentGenerator.Emit(OpCodes.Call, methodinf);
		}
	}

	public void Visit(TranslationBoundsGrouped grouped) {
		int b = bounds++;
		FieldBuilder boundField = currentClass.DefineField($"bound{b}_instance", typeof(Bounds), FieldAttributes.Private);
		boundResets.Add(boundField);
		if (grouped.grouped is GroupedBoundType.Once or GroupedBoundType.Never) {
			FieldBuilder everVisited = currentClass.DefineField($"boundEverVisited_{b}", typeof(bool), FieldAttributes.Private);
			falseInits.Add(everVisited);
			MethodBuilder listener = currentClass.DefineMethod($"boundEverVisited_{b}listener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			ILGenerator gen = listener.GetILGenerator();
			gen.Emit(OpCodes.Ldarg_1);
			ConvertIL(gen, BoundEnteredData);
			gen.Emit(OpCodes.Call, BoundEnteredData.GetProperty("bounds").GetGetMethod());
			Label label = gen.DefineLabel();
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Ceq);
			gen.Emit(OpCodes.Ldc_I4_0);
			gen.Emit(OpCodes.Beq, label);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldc_I4_1);
			gen.Emit(OpCodes.Stfld, everVisited);
			gen.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			gen.Emit(OpCodes.Ldstr, $"boundEverVisited_{b}bus");
			gen.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundEntered));
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundRegistryField);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Callvirt, BoundDestroyed);
			gen.MarkLabel(label);
			gen.Emit(OpCodes.Ret);
			FieldBuilder listenStarted = currentClass.DefineField($"boundListening{b}", typeof(bool), FieldAttributes.Private);
			falseInits.Add(listenStarted);
			Label l = currentGenerator.DefineLabel();
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, listenStarted);
			currentGenerator.Emit(OpCodes.Brtrue, l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldc_I4_1);
			currentGenerator.Emit(OpCodes.Stfld, listenStarted);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			LocalBuilder boundLocal = currentGenerator.DeclareLocal(typeof(Bounds));
			LoadLocalA(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Initobj, typeof(Bounds));
			LoadLocal(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Stfld, boundField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldflda, boundField);
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Min(grouped.start.x, grouped.end.x));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Min(grouped.start.y, grouped.end.y));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Min(grouped.start.z, grouped.end.z));
			currentGenerator.Emit(OpCodes.Newobj, typeof(Vector3).GetConstructor([typeof(float), typeof(float), typeof(float)]));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Max(grouped.start.x, grouped.end.x));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Max(grouped.start.y, grouped.end.y));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Max(grouped.start.z, grouped.end.z));
			currentGenerator.Emit(OpCodes.Newobj, typeof(Vector3).GetConstructor([typeof(float), typeof(float), typeof(float)]));
			currentGenerator.Emit(OpCodes.Call, BoundsSetMinMax);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundRegistryField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Callvirt, BoundCreated);
			currentGenerator.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"boundEverVisited_{b}bus");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, listener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundEntered));
			currentGenerator.MarkLabel(l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, everVisited);
			if (grouped.grouped is GroupedBoundType.Never) {
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
				currentGenerator.Emit(OpCodes.Ceq);
			}
		}
		else {
			FieldBuilder isActive = currentClass.DefineField($"bound_{b}active", typeof(bool), FieldAttributes.Private);
			FieldBuilder listening = currentClass.DefineField($"bound_{b}listening", typeof(bool), FieldAttributes.Private);
			falseInits.Add(isActive);
			falseInits.Add(listening);
			MethodBuilder stopListening = currentClass.DefineMethod($"bound_{b}stopListening", MethodAttributes.Private, typeof(void), []);
			activeInactiveBounds.Add(stopListening);
			MethodBuilder enterlistener = currentClass.DefineMethod($"bound_{b}enterlistener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			MethodBuilder leavelistener = currentClass.DefineMethod($"bound_{b}leavelistener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			ILGenerator sl = stopListening.GetILGenerator();
			ILGenerator elist = enterlistener.GetILGenerator();
			ILGenerator llist = leavelistener.GetILGenerator();
			sl.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			sl.Emit(OpCodes.Ldstr, $"bound_{b}enterlistener");
			sl.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundEntered));
			sl.Emit(OpCodes.Newobj, BoundLeft.GetConstructors().First());
			sl.Emit(OpCodes.Ldstr, $"bound_{b}leavelistener");
			sl.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundLeft));
			sl.Emit(OpCodes.Ret);
			elist.Emit(OpCodes.Ldarg_1);
			ConvertIL(elist, BoundEnteredData);
			elist.Emit(OpCodes.Call, BoundEnteredData.GetProperty("bounds").GetGetMethod());
			Label elabel = elist.DefineLabel();
			elist.Emit(OpCodes.Ldarg_0);
			elist.Emit(OpCodes.Ldfld, boundField);
			elist.Emit(OpCodes.Ceq);
			elist.Emit(OpCodes.Ldc_I4_0);
			elist.Emit(OpCodes.Beq, elabel);
			elist.Emit(OpCodes.Ldarg_0);
			elist.Emit(OpCodes.Ldc_I4_1);
			elist.Emit(OpCodes.Stfld, isActive);
			elist.MarkLabel(elabel);
			elist.Emit(OpCodes.Ret);
			llist.Emit(OpCodes.Ldarg_1);
			ConvertIL(llist, BoundLeftData);
			llist.Emit(OpCodes.Call, BoundLeftData.GetProperty("bounds").GetGetMethod());
			Label llabel = llist.DefineLabel();
			llist.Emit(OpCodes.Ldarg_0);
			llist.Emit(OpCodes.Ldfld, boundField);
			llist.Emit(OpCodes.Ceq);
			llist.Emit(OpCodes.Ldc_I4_0);
			llist.Emit(OpCodes.Beq, llabel);
			llist.Emit(OpCodes.Ldarg_0);
			llist.Emit(OpCodes.Ldc_I4_0);
			llist.Emit(OpCodes.Stfld, isActive);
			llist.MarkLabel(llabel);
			llist.Emit(OpCodes.Ret);
			Label l = currentGenerator.DefineLabel();
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, listening);
			currentGenerator.Emit(OpCodes.Brtrue, l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldc_I4_1);
			currentGenerator.Emit(OpCodes.Stfld, listening);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			LocalBuilder boundLocal = currentGenerator.DeclareLocal(typeof(Bounds));
			LoadLocalA(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Initobj, typeof(Bounds));
			LoadLocal(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Stfld, boundField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldflda, boundField);
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Min(grouped.start.x, grouped.end.x));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Min(grouped.start.y, grouped.end.y));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Min(grouped.start.z, grouped.end.z));
			currentGenerator.Emit(OpCodes.Newobj, typeof(Vector3).GetConstructor([typeof(float), typeof(float), typeof(float)]));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Max(grouped.start.x, grouped.end.x));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Max(grouped.start.y, grouped.end.y));
			currentGenerator.Emit(OpCodes.Ldc_R4, Math.Max(grouped.start.z, grouped.end.z));
			currentGenerator.Emit(OpCodes.Newobj, typeof(Vector3).GetConstructor([typeof(float), typeof(float), typeof(float)]));
			currentGenerator.Emit(OpCodes.Call, BoundsSetMinMax);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundRegistryField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Callvirt, BoundCreated);
			currentGenerator.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"bound_{b}enterlistener");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, enterlistener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundEntered));
			currentGenerator.Emit(OpCodes.Newobj, BoundLeft.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"bound_{b}leavelistener");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, leavelistener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundLeft));
			currentGenerator.MarkLabel(l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, isActive);
			if (grouped.grouped is GroupedBoundType.Inactive) {
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
				currentGenerator.Emit(OpCodes.Ceq);
			}
		}
	}

	string RemovePathDelimiters(string path) {
		string result = path;
		char target = '\\';
		int first = path.IndexOf(target);
		if (first >= 0)
			result = result.Remove(first, 1);
		int last = path.LastIndexOf(target);
		if (last >= 0 && last != first)
			result = result.Remove(last - (first < last ? 1 : 0), 1);
		return result;
	}

	public void Visit(TranslationBoundsObjectGrouped grouped) {
		int b = bounds++;
		FieldBuilder boundField = currentClass.DefineField($"bound{b}_instance", typeof(Bounds), FieldAttributes.Private);
		boundResets.Add(boundField);
		if (grouped.grouped is GroupedBoundType.Once or GroupedBoundType.Never) {
			FieldBuilder everVisited = currentClass.DefineField($"boundEverVisited_{b}", typeof(bool), FieldAttributes.Private);
			falseInits.Add(everVisited);
			MethodBuilder listener = currentClass.DefineMethod($"boundEverVisited_{b}listener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			ILGenerator gen = listener.GetILGenerator();
			gen.Emit(OpCodes.Ldarg_1);
			ConvertIL(gen, BoundEnteredData);
			gen.Emit(OpCodes.Call, BoundEnteredData.GetProperty("bounds").GetGetMethod());
			Label label = gen.DefineLabel();
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Ceq);
			gen.Emit(OpCodes.Ldc_I4_0);
			gen.Emit(OpCodes.Beq, label);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldc_I4_1);
			gen.Emit(OpCodes.Stfld, everVisited);
			gen.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			gen.Emit(OpCodes.Ldstr, $"boundEverVisited_{b}bus");
			gen.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundEntered));
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundRegistryField);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Callvirt, BoundDestroyed);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Call, BoundsUnbind);
			gen.MarkLabel(label);
			gen.Emit(OpCodes.Ret);
			FieldBuilder listenStarted = currentClass.DefineField($"boundListening{b}", typeof(bool), FieldAttributes.Private);
			falseInits.Add(listenStarted);
			Label l = currentGenerator.DefineLabel();
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, listenStarted);
			currentGenerator.Emit(OpCodes.Brtrue, l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldc_I4_1);
			currentGenerator.Emit(OpCodes.Stfld, listenStarted);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			LocalBuilder boundLocal = currentGenerator.DeclareLocal(typeof(Bounds));
			LoadLocalA(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Initobj, typeof(Bounds));
			LoadLocal(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Stfld, boundField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Ldstr, RemovePathDelimiters(grouped.objectPath));
			currentGenerator.Emit(OpCodes.Call, AccessorUtilFindGameObject);
			currentGenerator.Emit(OpCodes.Call, BoundsBindFull);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundRegistryField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Callvirt, BoundCreated);
			currentGenerator.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"boundEverVisited_{b}bus");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, listener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundEntered));
			currentGenerator.MarkLabel(l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, everVisited);
			if (grouped.grouped is GroupedBoundType.Never) {
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
				currentGenerator.Emit(OpCodes.Ceq);
			}
		}
		else {
			FieldBuilder isActive = currentClass.DefineField($"bound_{b}active", typeof(bool), FieldAttributes.Private);
			FieldBuilder listening = currentClass.DefineField($"bound_{b}listening", typeof(bool), FieldAttributes.Private);
			falseInits.Add(isActive);
			falseInits.Add(listening);
			MethodBuilder stopListening = currentClass.DefineMethod($"bound_{b}stopListening", MethodAttributes.Private, typeof(void), []);
			activeInactiveBounds.Add(stopListening);
			MethodBuilder enterlistener = currentClass.DefineMethod($"bound_{b}enterlistener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			MethodBuilder leavelistener = currentClass.DefineMethod($"bound_{b}leavelistener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			ILGenerator sl = stopListening.GetILGenerator();
			ILGenerator elist = enterlistener.GetILGenerator();
			ILGenerator llist = leavelistener.GetILGenerator();
			sl.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			sl.Emit(OpCodes.Ldstr, $"bound_{b}enterlistener");
			sl.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundEntered));
			sl.Emit(OpCodes.Newobj, BoundLeft.GetConstructors().First());
			sl.Emit(OpCodes.Ldstr, $"bound_{b}leavelistener");
			sl.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundLeft));
			sl.Emit(OpCodes.Ldarg_0);
			sl.Emit(OpCodes.Ldfld, boundField);
			sl.Emit(OpCodes.Call, BoundsUnbind);
			sl.Emit(OpCodes.Ret);
			elist.Emit(OpCodes.Ldarg_1);
			ConvertIL(elist, BoundEnteredData);
			elist.Emit(OpCodes.Call, BoundEnteredData.GetProperty("bounds").GetGetMethod());
			Label elabel = elist.DefineLabel();
			elist.Emit(OpCodes.Ldarg_0);
			elist.Emit(OpCodes.Ldfld, boundField);
			elist.Emit(OpCodes.Ceq);
			elist.Emit(OpCodes.Ldc_I4_0);
			elist.Emit(OpCodes.Beq, elabel);
			elist.Emit(OpCodes.Ldarg_0);
			elist.Emit(OpCodes.Ldc_I4_1);
			elist.Emit(OpCodes.Stfld, isActive);
			elist.MarkLabel(elabel);
			elist.Emit(OpCodes.Ret);
			llist.Emit(OpCodes.Ldarg_1);
			ConvertIL(llist, BoundLeftData);
			llist.Emit(OpCodes.Call, BoundLeftData.GetProperty("bounds").GetGetMethod());
			Label llabel = llist.DefineLabel();
			llist.Emit(OpCodes.Ldarg_0);
			llist.Emit(OpCodes.Ldfld, boundField);
			llist.Emit(OpCodes.Ceq);
			llist.Emit(OpCodes.Ldc_I4_0);
			llist.Emit(OpCodes.Beq, llabel);
			llist.Emit(OpCodes.Ldarg_0);
			llist.Emit(OpCodes.Ldc_I4_0);
			llist.Emit(OpCodes.Stfld, isActive);
			llist.MarkLabel(llabel);
			llist.Emit(OpCodes.Ret);
			Label l = currentGenerator.DefineLabel();
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, listening);
			currentGenerator.Emit(OpCodes.Brtrue, l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldc_I4_1);
			currentGenerator.Emit(OpCodes.Stfld, listening);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			LocalBuilder boundLocal = currentGenerator.DeclareLocal(typeof(Bounds));
			LoadLocalA(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Initobj, typeof(Bounds));
			LoadLocal(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Stfld, boundField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Ldstr, RemovePathDelimiters(grouped.objectPath));
			currentGenerator.Emit(OpCodes.Call, AccessorUtilFindGameObject);
			currentGenerator.Emit(OpCodes.Call, BoundsBindFull);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundRegistryField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Callvirt, BoundCreated);
			currentGenerator.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"bound_{b}enterlistener");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, enterlistener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundEntered));
			currentGenerator.Emit(OpCodes.Newobj, BoundLeft.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"bound_{b}leavelistener");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, leavelistener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundLeft));
			currentGenerator.MarkLabel(l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, isActive);
			if (grouped.grouped is GroupedBoundType.Inactive) {
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
				currentGenerator.Emit(OpCodes.Ceq);
			}
		}
	}

	public void Visit(TranslationBoundsObjectSizeGrouped grouped) {
		int b = bounds++;
		FieldBuilder boundField = currentClass.DefineField($"bound{b}_instance", typeof(Bounds), FieldAttributes.Private);
		boundResets.Add(boundField);
		if (grouped.grouped is GroupedBoundType.Once or GroupedBoundType.Never) {
			FieldBuilder everVisited = currentClass.DefineField($"boundEverVisited_{b}", typeof(bool), FieldAttributes.Private);
			falseInits.Add(everVisited);
			MethodBuilder listener = currentClass.DefineMethod($"boundEverVisited_{b}listener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			ILGenerator gen = listener.GetILGenerator();
			gen.Emit(OpCodes.Ldarg_1);
			ConvertIL(gen, BoundEnteredData);
			gen.Emit(OpCodes.Call, BoundEnteredData.GetProperty("bounds").GetGetMethod());
			Label label = gen.DefineLabel();
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Ceq);
			gen.Emit(OpCodes.Ldc_I4_0);
			gen.Emit(OpCodes.Beq, label);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldc_I4_1);
			gen.Emit(OpCodes.Stfld, everVisited);
			gen.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			gen.Emit(OpCodes.Ldstr, $"boundEverVisited_{b}bus");
			gen.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundEntered));
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundRegistryField);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Callvirt, BoundDestroyed);
			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, boundField);
			gen.Emit(OpCodes.Call, BoundsUnbind);
			gen.MarkLabel(label);
			gen.Emit(OpCodes.Ret);
			FieldBuilder listenStarted = currentClass.DefineField($"boundListening{b}", typeof(bool), FieldAttributes.Private);
			falseInits.Add(listenStarted);
			Label l = currentGenerator.DefineLabel();
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, listenStarted);
			currentGenerator.Emit(OpCodes.Brtrue, l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldc_I4_1);
			currentGenerator.Emit(OpCodes.Stfld, listenStarted);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			LocalBuilder boundLocal = currentGenerator.DeclareLocal(typeof(Bounds));
			LoadLocalA(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Initobj, typeof(Bounds));
			LoadLocal(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Stfld, boundField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Ldstr, RemovePathDelimiters(grouped.objectPath));
			currentGenerator.Emit(OpCodes.Call, AccessorUtilFindGameObject);
			currentGenerator.Emit(OpCodes.Ldc_R4, grouped.size.x);
			currentGenerator.Emit(OpCodes.Ldc_R4, grouped.size.y);
			currentGenerator.Emit(OpCodes.Ldc_R4, grouped.size.z);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Coordinate).GetConstructor([typeof(float), typeof(float), typeof(float)]));
			currentGenerator.Emit(OpCodes.Call, BoundsBindPartial);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundRegistryField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Callvirt, BoundCreated);
			currentGenerator.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"boundEverVisited_{b}bus");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, listener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundEntered));
			currentGenerator.MarkLabel(l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, everVisited);
			if (grouped.grouped is GroupedBoundType.Never) {
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
				currentGenerator.Emit(OpCodes.Ceq);
			}
		}
		else {
			FieldBuilder isActive = currentClass.DefineField($"bound_{b}active", typeof(bool), FieldAttributes.Private);
			FieldBuilder listening = currentClass.DefineField($"bound_{b}listening", typeof(bool), FieldAttributes.Private);
			falseInits.Add(isActive);
			falseInits.Add(listening);
			MethodBuilder stopListening = currentClass.DefineMethod($"bound_{b}stopListening", MethodAttributes.Private, typeof(void), []);
			activeInactiveBounds.Add(stopListening);
			MethodBuilder enterlistener = currentClass.DefineMethod($"bound_{b}enterlistener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			MethodBuilder leavelistener = currentClass.DefineMethod($"bound_{b}leavelistener", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
			ILGenerator sl = stopListening.GetILGenerator();
			ILGenerator elist = enterlistener.GetILGenerator();
			ILGenerator llist = leavelistener.GetILGenerator();
			sl.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			sl.Emit(OpCodes.Ldstr, $"bound_{b}enterlistener");
			sl.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundEntered));
			sl.Emit(OpCodes.Newobj, BoundLeft.GetConstructors().First());
			sl.Emit(OpCodes.Ldstr, $"bound_{b}leavelistener");
			sl.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(BoundLeft));
			sl.Emit(OpCodes.Ldarg_0);
			sl.Emit(OpCodes.Ldfld, boundField);
			sl.Emit(OpCodes.Call, BoundsUnbind);
			sl.Emit(OpCodes.Ret);
			elist.Emit(OpCodes.Ldarg_1);
			ConvertIL(elist, BoundEnteredData);
			elist.Emit(OpCodes.Call, BoundEnteredData.GetProperty("bounds").GetGetMethod());
			Label elabel = elist.DefineLabel();
			elist.Emit(OpCodes.Ldarg_0);
			elist.Emit(OpCodes.Ldfld, boundField);
			elist.Emit(OpCodes.Ceq);
			elist.Emit(OpCodes.Ldc_I4_0);
			elist.Emit(OpCodes.Beq, elabel);
			elist.Emit(OpCodes.Ldarg_0);
			elist.Emit(OpCodes.Ldc_I4_1);
			elist.Emit(OpCodes.Stfld, isActive);
			elist.MarkLabel(elabel);
			elist.Emit(OpCodes.Ret);
			llist.Emit(OpCodes.Ldarg_1);
			ConvertIL(llist, BoundLeftData);
			llist.Emit(OpCodes.Call, BoundLeftData.GetProperty("bounds").GetGetMethod());
			Label llabel = llist.DefineLabel();
			llist.Emit(OpCodes.Ldarg_0);
			llist.Emit(OpCodes.Ldfld, boundField);
			llist.Emit(OpCodes.Ceq);
			llist.Emit(OpCodes.Ldc_I4_0);
			llist.Emit(OpCodes.Beq, llabel);
			llist.Emit(OpCodes.Ldarg_0);
			llist.Emit(OpCodes.Ldc_I4_0);
			llist.Emit(OpCodes.Stfld, isActive);
			llist.MarkLabel(llabel);
			llist.Emit(OpCodes.Ret);
			Label l = currentGenerator.DefineLabel();
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, listening);
			currentGenerator.Emit(OpCodes.Brtrue, l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldc_I4_1);
			currentGenerator.Emit(OpCodes.Stfld, listening);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			LocalBuilder boundLocal = currentGenerator.DeclareLocal(typeof(Bounds));
			LoadLocalA(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Initobj, typeof(Bounds));
			LoadLocal(currentGenerator, boundLocal);
			currentGenerator.Emit(OpCodes.Stfld, boundField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Ldstr, RemovePathDelimiters(grouped.objectPath));
			currentGenerator.Emit(OpCodes.Call, AccessorUtilFindGameObject);
			currentGenerator.Emit(OpCodes.Ldc_R4, grouped.size.x);
			currentGenerator.Emit(OpCodes.Ldc_R4, grouped.size.y);
			currentGenerator.Emit(OpCodes.Ldc_R4, grouped.size.z);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Coordinate).GetConstructor([typeof(float), typeof(float), typeof(float)]));
			currentGenerator.Emit(OpCodes.Call, BoundsBindPartial);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundRegistryField);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, boundField);
			currentGenerator.Emit(OpCodes.Callvirt, BoundCreated);
			currentGenerator.Emit(OpCodes.Newobj, BoundEntered.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"bound_{b}enterlistener");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, enterlistener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundEntered));
			currentGenerator.Emit(OpCodes.Newobj, BoundLeft.GetConstructors().First());
			currentGenerator.Emit(OpCodes.Ldstr, $"bound_{b}leavelistener");
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldvirtftn, leavelistener);
			currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
			currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(BoundLeft));
			currentGenerator.MarkLabel(l);
			currentGenerator.Emit(OpCodes.Ldarg_0);
			currentGenerator.Emit(OpCodes.Ldfld, isActive);
			if (grouped.grouped is GroupedBoundType.Inactive) {
				currentGenerator.Emit(OpCodes.Ldc_I4_0);
				currentGenerator.Emit(OpCodes.Ceq);
			}
		}
	}

	void ConvertIL(ILGenerator gen, Type type) {
		if (type == typeof(int))
			gen.Emit(OpCodes.Conv_I4);
		else if (type == typeof(float))
			gen.Emit(OpCodes.Conv_R4);
		else {
			gen.Emit(OpCodes.Castclass, type);
		}
	}

	public void Visit(TranslationEventListen eventListen, IEnumerable<TranslationResult> body) {
		locals.Clear();
		int ev = events++;
		FieldBuilder listenStarted = currentClass.DefineField($"events_listening{ev}", typeof(bool), FieldAttributes.Private);
		FieldBuilder fulfilled = currentClass.DefineField($"event_fulfilled{ev}", typeof(bool), FieldAttributes.Private);
		falseInits.Add(listenStarted);
		falseInits.Add(fulfilled);
		Label l = currentGenerator.DefineLabel();
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldfld, listenStarted);
		currentGenerator.Emit(OpCodes.Brtrue, l);
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldc_I4_0);
		currentGenerator.Emit(OpCodes.Stfld, fulfilled);
		MethodBuilder listenMethod = currentClass.DefineMethod($"event_listen{ev}", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
		ILGenerator gen = listenMethod.GetILGenerator();
		LocalBuilder evdLoc = gen.DeclareLocal(typeof(object[]));
		gen.Emit(OpCodes.Ldarg_0);
		gen.Emit(OpCodes.Castclass, DslDataType);
		gen.Emit(OpCodes.Call, DslDataArgs);
		StoreLocal(gen, evdLoc);
		Type[] evTypes = EventTypeRegistry.GetRegistered(eventListen.ev);
		for (int i = 0; i < eventListen.args.Count(); i++) {
			var e = eventListen.args.ElementAt(i);
			locals[e.name] = gen.DeclareLocal(evTypes[i]);
			LoadLocal(gen, evdLoc);
			gen.Emit(OpCodes.Ldelem_Ref, i);
			ConvertIL(gen, evTypes[i]);
			StoreLocal(gen, locals[e.name]);
		}
		ILGenerator _b = currentGenerator;
		currentGenerator = gen;
		foreach (var b_node in body) {
			if (b_node is TranslationFulfilled) {
				gen.Emit(OpCodes.Ldarg_0);
				gen.Emit(OpCodes.Ldc_I4_1);
				gen.Emit(OpCodes.Stfld, fulfilled);
				gen.Emit(OpCodes.Ldstr, eventListen.ev);
				gen.Emit(OpCodes.Newobj, DslIdType.GetConstructor([typeof(string)]));
				gen.Emit(OpCodes.Ldstr, $"event_listen{ev}");
				gen.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(DslIdType));
			}
			else
				VisitLogicNode(b_node);
		}
		gen.Emit(OpCodes.Ret);
		currentGenerator = _b;
		currentGenerator.Emit(OpCodes.Ldstr, eventListen.ev);
		currentGenerator.Emit(OpCodes.Newobj, DslIdType.GetConstructor([typeof(string)]));
		currentGenerator.Emit(OpCodes.Ldstr, $"event_listen{ev}");
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldvirtftn, listenMethod);
		currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
		currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(DslIdType));
		currentGenerator.MarkLabel(l);
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldfld, fulfilled);
	}

	public void Visit(TranslationEventListenGrouped grouped) {
		locals.Clear();
		int ev = events++;
		FieldBuilder listenStarted = currentClass.DefineField($"events_listening{ev}", typeof(bool), FieldAttributes.Private);
		FieldBuilder fulfilled = currentClass.DefineField($"event_fulfilled{ev}", typeof(bool), FieldAttributes.Private);
		falseInits.Add(listenStarted);
		falseInits.Add(fulfilled);
		Label l = currentGenerator.DefineLabel();
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldfld, listenStarted);
		currentGenerator.Emit(OpCodes.Brtrue, l);
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldc_I4_0);
		currentGenerator.Emit(OpCodes.Stfld, fulfilled);
		MethodBuilder listenMethod = currentClass.DefineMethod($"event_listen{ev}", MethodAttributes.Private, typeof(void), [typeof(EventData)]);
		ILGenerator gen = listenMethod.GetILGenerator();
		gen.Emit(OpCodes.Ldarg_0);
		gen.Emit(OpCodes.Ldc_I4_1);
		gen.Emit(OpCodes.Stfld, fulfilled);
		gen.Emit(OpCodes.Ldstr, grouped.ev);
		gen.Emit(OpCodes.Newobj, DslIdType.GetConstructor([typeof(string)]));
		gen.Emit(OpCodes.Ldstr, $"event_listen{ev}");
		gen.Emit(OpCodes.Call, EventBusStop.MakeGenericMethod(DslIdType));
		gen.Emit(OpCodes.Ret);
		currentGenerator.Emit(OpCodes.Ldstr, grouped.ev);
		currentGenerator.Emit(OpCodes.Newobj, DslIdType.GetConstructor([typeof(string)]));
		currentGenerator.Emit(OpCodes.Ldstr, $"event_listen{ev}");
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldvirtftn, listenMethod);
		currentGenerator.Emit(OpCodes.Newobj, typeof(Action<EventData>).GetConstructors().First());
		currentGenerator.Emit(OpCodes.Call, EventBusListen.MakeGenericMethod(DslIdType));
		currentGenerator.MarkLabel(l);
		currentGenerator.Emit(OpCodes.Ldarg_0);
		currentGenerator.Emit(OpCodes.Ldfld, fulfilled);
	}

	public void Visit(TranslationFulfilled ct) {
		currentGenerator.Emit(OpCodes.Ldc_I4_1);
		currentGenerator.Emit(OpCodes.Ret);
	}

	public void Visit(TranslationFullConditionNode fullCond) {
		switch (fullCond.condition) {
			case TranslationBoundsGrouped tbg:
				Visit(tbg);
				break;
			case TranslationEventListenGrouped elg:
				Visit(elg);
				break;
			case TranslationBoundsObjectGrouped bog:
				Visit(bog);
				break;
			case TranslationBoundsObjectSizeGrouped bosg:
				Visit(bosg);
				break;
			case TranslationObjectCondition oc:
				Visit(oc, fullCond.body);
				break;
			case TranslationObjectComponentCondition occ:
				Visit(occ, fullCond.body);
				break;
			case TranslationEventListen el:
				Visit(el, fullCond.body);
				break;
		}
	}

	Type? EmitAccessor(ILGenerator gen, Type type, int idx, string fieldName) {
		string[] accesses = fieldName.Trim('.').Split('.');
		Type prevType = type;
		Type? memberType = null;
		foreach (var access in accesses)
		{
			var member = prevType.GetMember(access, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where((v) => v is not MethodInfo).FirstOrDefault();
			if (member == null || member == default) return null;
			if (member is FieldInfo field)
			{
				memberType = field.FieldType;
			}
			else
			{
				memberType = ((PropertyInfo)member).PropertyType;
			}
			prevType = memberType;
			LoadLocal(gen, idx);
			gen.Emit(OpCodes.Ldstr, access);
			gen.Emit(OpCodes.Call, AccessorUtilGet.MakeGenericMethod(memberType));
		}
		return memberType;
	}

	public void Visit(TranslationObjectCondition objectCond, IEnumerable<TranslationResult> body) {
		locals.Clear();
		LocalBuilder goLocal = currentGenerator.DeclareLocal(typeof(GameObject));
		Label isNull = currentGenerator.DefineLabel();
		currentGenerator.Emit(OpCodes.Ldstr, RemovePathDelimiters(objectCond.path));
		currentGenerator.Emit(OpCodes.Call, AccessorUtilFindGameObject);
		currentGenerator.Emit(OpCodes.Dup);
		currentGenerator.Emit(OpCodes.Ldnull);
		currentGenerator.Emit(OpCodes.Beq, isNull);
		StoreLocal(currentGenerator, goLocal);
		foreach (var arg in objectCond.args) {
			Type? argType = EmitAccessor(currentGenerator, typeof(GameObject), goLocal.LocalIndex, arg.propertyAccess);
			if (argType == null) continue;
			var local = currentGenerator.DeclareLocal(argType);
			locals[arg.name] = local;
			StoreLocal(currentGenerator, local);
		}
		foreach (var node in body)
			VisitLogicNode(node);
		currentGenerator.MarkLabel(isNull);
		currentGenerator.Emit(OpCodes.Ldc_I4_0);
	}

	public void Visit(TranslationObjectComponentCondition objectCond, IEnumerable<TranslationResult> body) {
		locals.Clear();
		Type type = Typefinder.FindType(objectCond.component);
		if (type == null) return;
		LocalBuilder compLocal = currentGenerator.DeclareLocal(type);
		Label isNull = currentGenerator.DefineLabel();
		currentGenerator.Emit(OpCodes.Ldstr, RemovePathDelimiters(objectCond.path));
		currentGenerator.Emit(OpCodes.Call, AccessorUtilFindGameObject);
		currentGenerator.Emit(OpCodes.Dup);
		currentGenerator.Emit(OpCodes.Ldnull);
		currentGenerator.Emit(OpCodes.Beq, isNull);
		currentGenerator.Emit(OpCodes.Call, GetComponent.MakeGenericMethod(type));
		currentGenerator.Emit(OpCodes.Dup);
		currentGenerator.Emit(OpCodes.Ldnull);
		currentGenerator.Emit(OpCodes.Beq, isNull);
		StoreLocal(currentGenerator, compLocal);
		foreach (var arg in objectCond.args) {
			Type? argType = EmitAccessor(currentGenerator, type, compLocal.LocalIndex, arg.propertyAccess);
			if (argType == null) continue;
			var local = currentGenerator.DeclareLocal(argType);
			locals[arg.name] = local;
			StoreLocal(currentGenerator, local);
		}
		foreach (var node in body)
			VisitLogicNode(node);
		currentGenerator.MarkLabel(isNull);
		currentGenerator.Emit(OpCodes.Ldc_I4_0);
	}

	public void Visit(TranslationVariableReference varRef)
	{
		LocalBuilder local = locals[varRef.name];
		LoadLocal(currentGenerator, local);
	}

	public void Visit(TranslationRunImmediate immediate)
	{
		foreach (var logic in immediate.body)
			VisitLogicNode(logic);
		currentGenerator.Emit(OpCodes.Ldc_I4_1);
		currentGenerator.Emit(OpCodes.Ret);
	}
}