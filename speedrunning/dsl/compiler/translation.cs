using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using tairasoul.unity.common.speedrunning.dsl.internals;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.compiler;

public record Coordinate(float x, float y, float z) {
	public static implicit operator Vector3(Coordinate coord) {
		return new(coord.x, coord.y, coord.z);
	}
}

enum ComparisonType {
	Equals,
	LessOrEqual,
	LessThan,
	GreaterOrEqual,
	GreaterThan,
	NotEquals
}

enum ChainType {
	And
}

enum BinaryOperation {
	Add,
	Subtract,
	Divide,
	Multiply
}

enum GroupedBoundType {
	Once,
	Never,
	Active,
	Inactive
}

abstract record TranslationResult();

record TranslationRootNode(TranslationSplitOrder order, IEnumerable<TranslationResult> results) : TranslationResult;
record TranslationSplitOrder(IEnumerable<string> splits) : TranslationResult;
record TranslationSplit(string splitName, TranslationResult content) : TranslationResult;
record TranslationAny(IEnumerable<TranslationResult> conditions, IEnumerable<TranslationResult> body) : TranslationResult;
record TranslationAll(IEnumerable<TranslationResult> conditions, IEnumerable<TranslationResult> body) : TranslationResult;
record TranslationTimerCall(TimerOperation operation) : TranslationResult;
record TranslationComparison(TranslationResult lh, ComparisonType comparison, TranslationResult rh) : TranslationResult;
record TranslationLogicalChain(TranslationResult first, List<(ChainType op, TranslationResult next)> rest) : TranslationResult;
record TranslationBinary(TranslationResult lh, BinaryOperation operation, TranslationResult rh) : TranslationResult;
record TranslationIf(TranslationResult condition, IEnumerable<TranslationResult> body, TranslationResult? _else) : TranslationResult;
record TranslationElse(IEnumerable<TranslationResult> body) : TranslationResult;
record TranslationElseIf(TranslationResult condition, IEnumerable<TranslationResult> body, TranslationResult? _else) : TranslationResult;
record TranslationLiteral(object value) : TranslationResult;
record TranslationVariableDecl(string propertyAccess, string name) : TranslationResult;
record TranslationVariableReference(string name) : TranslationResult;
record TranslationMethodCall(string name, IEnumerable<TranslationResult> args) : TranslationResult;
record TranslationBoundsGrouped(Coordinate start, Coordinate end, GroupedBoundType grouped) : TranslationResult;
record TranslationBoundsObjectGrouped(string objectPath, GroupedBoundType grouped) : TranslationResult;
record TranslationBoundsObjectSizeGrouped(string objectPath, Coordinate size, GroupedBoundType grouped) : TranslationResult;
record TranslationEventListenGrouped(string ev, bool anypoint) : TranslationResult;
record TranslationEventListen(string ev, IEnumerable<TranslationVariableDecl> args, bool anypoint) : TranslationResult;
record TranslationFulfilled() : TranslationResult;
record TranslationFullConditionNode(TranslationResult condition, IEnumerable<TranslationResult> body) : TranslationResult;
record TranslationObjectCondition(string path, IEnumerable<TranslationVariableDecl> args) : TranslationResult;
record TranslationObjectComponentCondition(string path, string component, IEnumerable<TranslationVariableDecl> args) : TranslationResult;
record TranslationRunImmediate(IEnumerable<TranslationResult> body) : TranslationResult;

class TranslationVisitor : AbstractParseTreeVisitor<TranslationResult>, ISrDslParserVisitor<TranslationResult>
{
	public TranslationResult VisitAll_node([NotNull] SrDslParser.All_nodeContext context)
	{
		List<TranslationResult> conditions = [];
		foreach (var condition in context._conditions)
			conditions.Add(VisitGrouped_condition_node(condition));
		List<TranslationResult> logic = [];
		foreach (var log in context._logic)
			logic.Add(VisitSplit_logic_node(log));
		TranslationAll all = new(conditions, logic);
		return all;
	}

	public TranslationResult VisitAny_node([NotNull] SrDslParser.Any_nodeContext context)
	{
		List<TranslationResult> conditions = [];
		foreach (var condition in context._conditions)
			conditions.Add(VisitGrouped_condition_node(condition));
		List<TranslationResult> logic = [];
		foreach (var log in context._logic)
			logic.Add(VisitSplit_logic_node(log));
		TranslationAny all = new(conditions, logic);
		return all;
	}

	public TranslationResult VisitBinary([NotNull] SrDslParser.BinaryContext context)
	{
		var lh = context.GetChild(0);
		var op = context.GetChild(1);
		var rh = context.GetChild(2);
		TranslationResult lhr = null!;
		BinaryOperation opr = op.GetText() switch
		{
			"+" => BinaryOperation.Add,
			"-" => BinaryOperation.Subtract,
			"/" => BinaryOperation.Divide,
			"*" => BinaryOperation.Multiply,
			_ => throw new NotImplementedException("brother how")
		};
		TranslationResult rhr = null!;
		if (lh is TerminalNodeImpl)
			lhr = new TranslationVariableReference(lh.GetText());
		else if (lh is SrDslParser.DatatypeContext datatype) {
			lhr = VisitDatatype(datatype);
		}
		if (rh is TerminalNodeImpl)
			rhr = new TranslationVariableReference(rh.GetText());
		else if (rh is SrDslParser.DatatypeContext datatype) {
			rhr = VisitDatatype(datatype);
		}
		else if (rh is SrDslParser.BinaryContext binary) {
			rhr = VisitBinary(binary);
		}
		return new TranslationBinary(lhr, opr, rhr);
	}

	public TranslationResult VisitBinary_lh_node([NotNull] SrDslParser.Binary_lh_nodeContext context)
	{
		var dt = context.datatype();
		if (dt != null)
			return VisitDatatype(context.datatype());
		else
			return new TranslationVariableReference(context.IDENTIFIER().GetText());
	}

	public TranslationResult VisitBool_dt([NotNull] SrDslParser.Bool_dtContext context)
	{
		if (context.TRUE() != null) {
			return new TranslationLiteral(true);
		}
		else {
			return new TranslationLiteral(false);
		}
	}

	public TranslationResult VisitBounds_condition([NotNull] SrDslParser.Bounds_conditionContext context)
	{
		throw new NotImplementedException();
	}

	public TranslationResult VisitBounds_grouped([NotNull] SrDslParser.Bounds_groupedContext context)
	{
		GroupedBoundType bound = context.GROUPED_BOUND_TYPE().GetText() switch
		{
			"never" => GroupedBoundType.Never,
			"once" => GroupedBoundType.Once,
			"active" => GroupedBoundType.Active,
			"inactive" => GroupedBoundType.Inactive,
			_ => throw new NotImplementedException("how")
		};
		SrDslParser.Bounds_conditionContext ctx = context.bounds_condition();
		TranslationLiteral x1 = (TranslationLiteral)VisitNumber(ctx._numbers[0]);
		TranslationLiteral y1 = (TranslationLiteral)VisitNumber(ctx._numbers[1]);
		TranslationLiteral z1 = (TranslationLiteral)VisitNumber(ctx._numbers[2]);
		TranslationLiteral x2 = (TranslationLiteral)VisitNumber(ctx._numbers[3]);
		TranslationLiteral y2 = (TranslationLiteral)VisitNumber(ctx._numbers[4]);
		TranslationLiteral z2 = (TranslationLiteral)VisitNumber(ctx._numbers[5]);
		Coordinate start = new(Convert.ToSingle(x1.value), Convert.ToSingle(y1.value), Convert.ToSingle(z1.value));
		Coordinate end = new(Convert.ToSingle(x2.value), Convert.ToSingle(y2.value), Convert.ToSingle(z2.value));
		return new TranslationBoundsGrouped(start, end, bound);
	}

	public TranslationResult VisitBounds_object([NotNull] SrDslParser.Bounds_objectContext context)
	{
		throw new NotImplementedException();
	}

	public TranslationResult VisitBounds_object_grouped([NotNull] SrDslParser.Bounds_object_groupedContext context)
	{
		GroupedBoundType bound = context.GROUPED_BOUND_TYPE().GetText() switch
		{
			"never" => GroupedBoundType.Never,
			"once" => GroupedBoundType.Never,
			"active" => GroupedBoundType.Active,
			"inactive" => GroupedBoundType.Inactive,
			_ => throw new NotImplementedException("how")
		};
		return new TranslationBoundsObjectGrouped(context.bounds_object().path.Text, bound);
	}

	public TranslationResult VisitBounds_object_size([NotNull] SrDslParser.Bounds_object_sizeContext context)
	{
		throw new NotImplementedException();
	}

	public TranslationResult VisitBounds_object_size_grouped([NotNull] SrDslParser.Bounds_object_size_groupedContext context)
	{
		GroupedBoundType bound = context.GROUPED_BOUND_TYPE().GetText() switch
		{
			"never" => GroupedBoundType.Never,
			"once" => GroupedBoundType.Never,
			"active" => GroupedBoundType.Active,
			"inactive" => GroupedBoundType.Inactive,
			_ => throw new NotImplementedException("how")
		};
		SrDslParser.Bounds_object_sizeContext ctx = context.bounds_object_size();
		TranslationLiteral x = (TranslationLiteral)VisitNumber(ctx._numbers[0]);
		TranslationLiteral y = (TranslationLiteral)VisitNumber(ctx._numbers[1]);
		TranslationLiteral z = (TranslationLiteral)VisitNumber(ctx._numbers[2]);
		return new TranslationBoundsObjectSizeGrouped(ctx.path.Text, new Coordinate(Convert.ToSingle(x.value), Convert.ToSingle(y.value), Convert.ToSingle(z.value)), bound);
	}

	public TranslationResult VisitCall_arg([NotNull] SrDslParser.Call_argContext context)
	{
		var dt = context.datatype();
		if (dt != null)
			return VisitDatatype(dt);
		else
			return new TranslationVariableReference(context.GetText());
	}

	public TranslationResult VisitCall_node([NotNull] SrDslParser.Call_nodeContext context)
	{
		string methodName = context.GetChild(1).GetText();
		List<TranslationResult> arguments = [];
		foreach (var callArg in context._call_args)
			arguments.Add(VisitCall_arg(callArg));
		return new TranslationMethodCall(methodName, arguments);
	}

	public TranslationResult VisitCall_shorthand([NotNull] SrDslParser.Call_shorthandContext context)
	{
		string methodName = context.GetChild(0).GetText();
		List<TranslationResult> arguments = [];
		foreach (var callArg in context._call_args)
			arguments.Add(VisitCall_arg(callArg));
		return new TranslationMethodCall(methodName, arguments);
	}

	public TranslationResult VisitComparison([NotNull] SrDslParser.ComparisonContext context)
	{
		var cf = context.main_comparison;
		var cs = context._extra_comparisons;
		List<(ChainType op, TranslationResult next)> rest = [];
		foreach (var extra in cs) {
			ChainType comp = ChainType.And;
			TranslationResult c = VisitComparison_l(extra.comparison_l());
			rest.Add((comp, c));
		}
		TranslationLogicalChain chain = new(VisitComparison_l(cf), rest);
		return chain;
	}

	public TranslationResult VisitComparison_andor([NotNull] SrDslParser.Comparison_andorContext context)
	{
		throw new NotImplementedException();
	}

	public TranslationResult VisitComparison_binaryrh_node([NotNull] SrDslParser.Comparison_binaryrh_nodeContext context)
	{
		var dt = context.datatype();
		var b = context.binary();
		if (dt != null)
			return VisitDatatype(dt);
		else if (b != null)
			return VisitBinary(b);
		else
			return new TranslationVariableReference(context.GetText());
	}

	public TranslationResult VisitComparison_full([NotNull] SrDslParser.Comparison_fullContext context)
	{
		TranslationResult lh = VisitComparison_binaryrh_node(context.lh);
		ComparisonType op = context.op.Text switch
		{
			"==" => ComparisonType.Equals,
			"<=" => ComparisonType.LessOrEqual,
			"<" => ComparisonType.LessThan,
			">=" => ComparisonType.GreaterOrEqual,
			">" => ComparisonType.GreaterThan,
			"!=" => ComparisonType.NotEquals,
			_ => throw new NotImplementedException("how")
		};
		TranslationResult rh = VisitComparison_binaryrh_node(context.rh);
		return new TranslationComparison(lh, op, rh);
	}

	public TranslationResult VisitComparison_l([NotNull] SrDslParser.Comparison_lContext context)
	{
		var cf = context.comparison_full();
		var cs = context.comparison_shorthand();
		if (cf != null)
			return VisitComparison_full(cf);
		else
			return VisitComparison_shorthand(cs);
	}

	public TranslationResult VisitComparison_shorthand([NotNull] SrDslParser.Comparison_shorthandContext context)
	{
		TranslationResult lh = VisitComparison_binaryrh_node(context.comparison_binaryrh_node());
		return new TranslationComparison(lh, ComparisonType.Equals, new TranslationLiteral(true));
	}

	public TranslationResult VisitCondition_node_nonopt([NotNull] SrDslParser.Condition_node_nonoptContext context)
	{
		var oc = context.object_condition();
		var occ = context.object_condition_component();
		var el = context.event_listen();
		if (oc != null)
			return VisitObject_condition(oc);
		else if (occ != null)
			return VisitObject_condition_component(occ);
		else
			return VisitEvent_listen(el);
	}

	public TranslationResult VisitCondition_node_opt([NotNull] SrDslParser.Condition_node_optContext context)
	{
		var bc = context.bounds_grouped();
		var el = context.event_listen_group();
		var bo = context.bounds_object_grouped();
		var bos = context.bounds_object_size_grouped();
		if (bc != null)
			return VisitBounds_grouped(bc);
		else if (el != null)
			return VisitEvent_listen_group(el);
		else if (bo != null)
			return VisitBounds_object_grouped(bo);
		else
			return VisitBounds_object_size_grouped(bos);
	}

	public TranslationResult VisitDatatype([NotNull] SrDslParser.DatatypeContext context)
	{
		var dtc = context.GetChild(0);
		if (dtc is SrDslParser.NumberContext numberCtx) {
			return VisitNumber(numberCtx);
		}
		else if (dtc is SrDslParser.Bool_dtContext boolCtx) {
			return VisitBool_dt(boolCtx);
		}
		else if (context.STRING() != null) {
			return new TranslationLiteral(context.GetText());
		}
		else {
			return new TranslationVariableReference(dtc.GetText());
		}
	}

	public TranslationResult VisitElse_if_statement([NotNull] SrDslParser.Else_if_statementContext context)
	{
		TranslationResult comp = VisitComparison(context.comp);
		List<TranslationResult> body = [];
		foreach (var log in context._logic)
			body.Add(VisitSplit_logic_node(log));
		if (context.branch != null) {
			return new TranslationElseIf(comp, body, VisitIf_branch(context.branch));
		}
		return new TranslationElseIf(comp, body, null);
	}

	public TranslationResult VisitElse_statement([NotNull] SrDslParser.Else_statementContext context)
	{
		List<TranslationResult> body = [];
		foreach (var log in context._logic)
			body.Add(VisitSplit_logic_node(log));
		return new TranslationElse(body);
	}

	public TranslationResult VisitEvent_listen([NotNull] SrDslParser.Event_listenContext context)
	{
		return new TranslationEventListen(context.event_name.Text, context._args.Select((v) => new TranslationVariableDecl(v.Text, v.Text)), context.ANYPOINT() != null);
	}

	public TranslationResult VisitEvent_listen_group([NotNull] SrDslParser.Event_listen_groupContext context)
	{
		return new TranslationEventListenGrouped(context.event_name.Text, context.ANYPOINT() != null);
	}

	public TranslationResult VisitGcn_nonopt([NotNull] SrDslParser.Gcn_nonoptContext context)
	{
		var oc = context.object_condition();
		var occ = context.object_condition_component();
		var el = context.event_listen();
		if (oc != null)
			return VisitObject_condition(oc);
		else if (occ != null)
			return VisitObject_condition_component(occ);
		else
			return VisitEvent_listen(el);
	}

	public TranslationResult VisitGcn_opt([NotNull] SrDslParser.Gcn_optContext context)
	{
		var bg = context.bounds_grouped();
		var elg = context.event_listen_group();
		var bog = context.bounds_object_grouped();
		var bosg = context.bounds_object_size_grouped();
		if (bg != null)
			return VisitBounds_grouped(bg);
		else if (elg != null)
			return VisitEvent_listen_group(elg);
		else if (bog != null)
			return VisitBounds_object_grouped(bog);
		else
			return VisitBounds_object_size_grouped(bosg);
	}

	public TranslationResult VisitGrouped_condition_node([NotNull] SrDslParser.Grouped_condition_nodeContext context)
	{
		var gcn = context.grouped_condition_node_nonopt();
		var gc = context.grouped_condition_node_opt();
		if (gcn != null)
			return VisitGrouped_condition_node_nonopt(gcn);
		else
			return VisitGrouped_condition_node_opt(gc);
	}

	public TranslationResult VisitGrouped_condition_node_nonopt([NotNull] SrDslParser.Grouped_condition_node_nonoptContext context)
	{
		var gc = context.gcn_nonopt();
		List<TranslationResult> logicResult = [];
		foreach (var log in context._logic)
			logicResult.Add(VisitSplit_logic_node(log));
		return new TranslationFullConditionNode(VisitGcn_nonopt(gc), logicResult);
	}

	public TranslationResult VisitGrouped_condition_node_opt([NotNull] SrDslParser.Grouped_condition_node_optContext context)
	{
		var gc = context.gcn_opt();
		List<TranslationResult> logicResult = [];
		if (context._logic.Count > 0)
		{
			foreach (var log in context._logic)
				logicResult.Add(VisitSplit_logic_node(log));
		}
		else {
			logicResult.Add(new TranslationFulfilled());
		}
		return new TranslationFullConditionNode(VisitGcn_opt(gc), logicResult);
	}

	public TranslationResult VisitIf_branch([NotNull] SrDslParser.If_branchContext context)
	{
		var es = context.else_statement();
		var eifs = context.else_if_statement();
		if (es != null)
			return VisitElse_statement(es);
		else
			return VisitElse_if_statement(eifs);
	}

	public TranslationResult VisitIf_statement([NotNull] SrDslParser.If_statementContext context)
	{
		TranslationResult comp = VisitComparison(context.comp);
		List<TranslationResult> body = [];
		foreach (var log in context._logic)
			body.Add(VisitSplit_logic_node(log));
		if (context.branch != null) {
			return new TranslationIf(comp, body, VisitIf_branch(context.branch));
		}
		return new TranslationIf(comp, body, null);
	}

	public TranslationResult VisitLogic_node([NotNull] SrDslParser.Logic_nodeContext context)
	{
		var tn = context.timer_node();
		var ifs = context.if_statement();
		var cn = context.call_node();
		var cs = context.call_shorthand();
		if (tn != null)
			return VisitTimer_node(tn);
		else if (ifs != null)
			return VisitIf_statement(ifs);
		else if (cn != null)
			return VisitCall_node(cn);
		else
			return VisitCall_shorthand(cs);
	}

	public TranslationResult VisitNongrouped_condition_node([NotNull] SrDslParser.Nongrouped_condition_nodeContext context)
	{
		var ncn = context.nongrouped_condition_node_nonopt();
		var nco = context.nongrouped_condition_node_opt();
		if (ncn != null)
			return VisitNongrouped_condition_node_nonopt(ncn);
		else
			return VisitNongrouped_condition_node_opt(nco);
	}

	public TranslationResult VisitNongrouped_condition_node_nonopt([NotNull] SrDslParser.Nongrouped_condition_node_nonoptContext context)
	{
		var nc = context.condition_node_nonopt();
		List<TranslationResult> logic = [];
		foreach (var log in context._logic)
			logic.Add(VisitSplit_logic_node(log));
		return new TranslationFullConditionNode(VisitCondition_node_nonopt(nc), logic);
	}

	public TranslationResult VisitNongrouped_condition_node_opt([NotNull] SrDslParser.Nongrouped_condition_node_optContext context)
	{
		var nc = context.condition_node_opt();
		List<TranslationResult> logic = [];
		if (context._logic.Count > 0) {
			foreach (var log in context._logic)
				logic.Add(VisitSplit_logic_node(log));
		}
		else {
			logic.Add(new TranslationFulfilled());
		}
		return new TranslationFullConditionNode(VisitCondition_node_opt(nc), logic);
	}

	public TranslationResult VisitNumber([NotNull] SrDslParser.NumberContext context)
	{
		if (context.FLOAT() != null) {
			return new TranslationLiteral(float.Parse(context.FLOAT().GetText()));
		}
		else {
			return new TranslationLiteral(int.Parse(context.INTEGER().GetText()));
		}
	}

	public TranslationResult VisitObject_condition([NotNull] SrDslParser.Object_conditionContext context)
	{
		string path = context.path.Text;
		List<TranslationVariableDecl> decls = [];
		foreach (var vard in context._args)
			decls.Add(new TranslationVariableDecl(vard.DOT_ACCESSED().GetText(), vard.IDENTIFIER().GetText()));
		return new TranslationObjectCondition(path, decls);
	}

	public TranslationResult VisitObject_condition_component([NotNull] SrDslParser.Object_condition_componentContext context)
	{
		string path = context.path.Text;
		string comp = context.component.Text;
		List<TranslationVariableDecl> decls = [];
		foreach (var vard in context._args)
			decls.Add(new TranslationVariableDecl(vard.DOT_ACCESSED().GetText(), vard.IDENTIFIER().GetText()));
		return new TranslationObjectComponentCondition(path, comp, decls);
	}

	public TranslationResult VisitProperty_access([NotNull] SrDslParser.Property_accessContext context)
	{
		var da = context.DOT_ACCESSED().GetText();
		var id = context.IDENTIFIER().GetText();
		return new TranslationVariableDecl(da, id);
	}

	public TranslationResult VisitRoot([NotNull] SrDslParser.RootContext context)
	{
		List<TranslationResult> results = [];
		foreach (var node in context._splits)
			results.Add(VisitSplitdef_node(node));
		return new TranslationRootNode((TranslationSplitOrder)VisitSplit_order(context.split_order()), results);
	}

	public TranslationResult VisitRunimmediate_node([NotNull] SrDslParser.Runimmediate_nodeContext context)
	{
		List<TranslationResult> results = [];
		foreach (var logic in context._logic)
			results.Add(VisitSplit_logic_node(logic));
		return new TranslationRunImmediate(results);
	}

	public TranslationResult VisitSplitdef_node([NotNull] SrDslParser.Splitdef_nodeContext context)
	{
		string splitName = context.IDENTIFIER().GetText();
		var any = context.any_node();
		var all = context.all_node();
		var nongrouped = context.nongrouped_condition_node();
		var immediate = context.runimmediate_node();
		if (any != null) {
			return new TranslationSplit(splitName, VisitAny_node(any));
		}
		else if (all != null) {
			return new TranslationSplit(splitName, VisitAll_node(all));
		}
		else if (nongrouped != null) {
			return new TranslationSplit(splitName, VisitNongrouped_condition_node(nongrouped));
		}
		else {
			return new TranslationSplit(splitName, VisitRunimmediate_node(immediate));
		}
	}

	public TranslationResult VisitSplit_logic_node([NotNull] SrDslParser.Split_logic_nodeContext context)
	{
		var ln = context.logic_node();
		if (ln != null)
			return VisitLogic_node(ln);
		else
			return new TranslationFulfilled();
	}

	public TranslationResult VisitSplit_order([NotNull] SrDslParser.Split_orderContext context)
	{
		return new TranslationSplitOrder(context._order_def.Select(v => v.Text));
	}

	public TranslationResult VisitTimer_node([NotNull] SrDslParser.Timer_nodeContext context)
	{
		TimerOperation operation = context.GetChild(1).GetText() switch {
			"pause" => TimerOperation.Pause,
			"resume" => TimerOperation.Resume,
			"start" => TimerOperation.Start,
			"split" => TimerOperation.Split,
			"startorsplit" => TimerOperation.StartOrSplit,
			"unsplit" => TimerOperation.Unsplit,
			"skipsplit" => TimerOperation.SkipSplit,
			"pausegametime" => TimerOperation.PauseGameTime,
			"unpausegametime" => TimerOperation.UnpauseGameTime,
			_ => throw new NotImplementedException("this should not occur")
		};

		return new TranslationTimerCall(operation);
	}
}