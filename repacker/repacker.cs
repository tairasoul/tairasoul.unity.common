using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using tairasoul.unity.common.repacker.extensions;
using tairasoul.unity.common.util.cecil;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace tairasoul.unity.common.repacker;

public enum RepackMode {
	/// <summary>
	/// if one type is referenced by the targets in any way, repacks everything in that type's assembly
	/// </summary>
	Full,
	/// <summary>
	/// if a type is referenced, repacks it and anything it needs to be brought over fully
	/// </summary>
	Referenced,
	/// <summary>
	/// if a type is referenced it's copied over bare and anything on it (excluding attributes) only gets copied if it's directly referenced somewhere <br/>ie. methods are only copied if they're referenced, interfaces are only copied if they're directly referenced anywhere, methods on interface types are only copied if they're ever called directly on the interface type
	/// </summary>
	Strict
}

public record RepackingOptions(
	Action<TypeDefinition, TypeDefinition>? typeCopied = null,
	RepackMode mode = RepackMode.Full,
	bool repackModuleInitializers = true,
	bool repackDebugInformation = true,
	bool treatOutputAsRelease = true,
	FrameworkName? targetFramework = null,
	bool preferFramework = false
);

record RepackingContext(DefinitionMap map, ModuleDefinition output, GenerationalQueue<Repack> repacks, RepackingOptions options) {
	public Dictionary<TypeReference, TypeReference> types = [];
	public Dictionary<MethodReference, MethodReference> methods = [];
	public Dictionary<FieldReference, FieldReference> fields = [];
	public Dictionary<PropertyDefinition, PropertyDefinition> properties = [];
	public Dictionary<EventDefinition, EventDefinition> events = [];
	public Dictionary<CustomAttribute, CustomAttribute> attributes = [];
	public Dictionary<ParameterDefinition, ParameterDefinition> parameters = [];
	public Dictionary<(IGenericParameterProvider owner, int idx), GenericParameter> generics = [];
	public Dictionary<InterfaceImplementation, InterfaceImplementation> impls = [];
	public Dictionary<MethodReturnType, MethodReturnType> mrts = [];
	public Dictionary<CustomAttributeArgument, CustomAttributeArgument> args = [];
	public Dictionary<VariableDefinition, VariableDefinition> variables = [];
	public Dictionary<CustomAttributeNamedArgument, CustomAttributeNamedArgument> named = [];
	public Dictionary<string, TypeDefinition> embedded = [];
	public Dictionary<string, CustomAttribute> moduleAttributes = [];
	public Dictionary<string, CustomAttribute> assemblyAttributes = [];
	public HashSet<ModuleDefinition> modulesChecked = [];
	public HashSet<AssemblyDefinition> assembliesChecked = [];
	public HashSet<ModuleDefinition> repacked = [];
	public Dictionary<Instruction, Instruction> instructions = [];
	public HashSet<string> typeFullNamesAdded = [];
	public HashSet<TypeReference> typesDirectlyReferenced = [];
	public Dictionary<string, Document> documents = [];

	void AddIfMoreSpecific(TypeReference a, TypeReference b) {
		if (types.TryGetValue(a, out var existing)) {
			if (existing is not TypeDefinition && b is TypeDefinition td) {
				types[a] = td;
			}
		}
		else {
			types[a] = b;
		}
	}

	void AddIfMoreSpecific(MethodReference a, MethodReference b) {
		if (methods.TryGetValue(a, out var existing)) {
			if (existing is not MethodDefinition && b is MethodDefinition td) {
				methods[a] = td;
			}
		}
		else {
			methods[a] = b;
		}
	}

	void AddIfMoreSpecific(FieldReference a, FieldReference b) {
		if (fields.TryGetValue(a, out var existing)) {
			if (existing is not FieldDefinition && b is FieldDefinition td) {
				fields[a] = td;
			}
		}
		else {
			fields[a] = b;
		}
	}
	public void Add(TypeReference a, TypeReference b) => AddIfMoreSpecific(a, b);
	public void Add(MethodReference a, MethodReference b) => AddIfMoreSpecific(a, b);
	public void Add(FieldReference a, FieldReference b) => AddIfMoreSpecific(a, b);
	public void Add(PropertyDefinition a, PropertyDefinition b) => properties[a] = b;
	public void Add(EventDefinition a, EventDefinition b) => events[a] = b;
	public void Add(CustomAttribute a, CustomAttribute b) => attributes[a] = b;
	public void Add(ParameterDefinition a, ParameterDefinition b) => parameters[a] = b;
	public void Add(InterfaceImplementation a, InterfaceImplementation b) => impls[a] = b;
	public void Add(MethodReturnType a, MethodReturnType b) => mrts[a] = b;
	public void Add(CustomAttributeArgument a, CustomAttributeArgument b) => args[a] = b;
	public void Add(VariableDefinition a, VariableDefinition b) => variables[a] = b;
	public void Add(CustomAttributeNamedArgument a, CustomAttributeNamedArgument b) => named[a] = b;
	public void Add(string embeddedName, TypeDefinition embedded) => this.embedded[embeddedName] = embedded;
	public void Add(IGenericParameterProvider owner, int genericIdx, GenericParameter copied) => generics[(owner, genericIdx)] = copied;

	public void EnqueueImmediate(Repack repack) {
		repacks.EnqueueImmediate(repack);
	}

	public void Enqueue(Repack repack) {
		repacks.Enqueue(repack);
	}

	public void EnqueueNextGen(Repack repack) {
		repacks.EnqueueNextGen(repack);
	}

	public bool TryDequeue(out Repack? repack) {
		return repacks.TryDequeue(out repack);
	}

	public void CommitImmediate() {
		repacks.CommitImmediate();
	}

	public void Commit() {
		repacks.Commit();
	}
}

static class Common {
	const string PID = "<PrivateImplementationDetails>";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsSpecialName(string name) {
		return name switch
		{
			PID => true,
			_ => false
		};
	}

	static bool IsSpecialType(RepackingContext context, ref TypeDefinition copied) {
		if (IsSpecialName(copied.Name)) {
			foreach (var kv in context.types) {
				if (kv.Value is TypeDefinition def && def.Namespace == copied.Namespace && def.Name == copied.Name) {
					copied = def;
					return true;
				}
			}
		}
		return false;
	}

	const string ROList = "<>z__ReadOnlySingleElementList`1";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsSingleName(string name) {
		return name switch
		{
			ROList => true,
			_ => false,
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsSingleNamespace(string ns) {
		return ns switch
		{
			"System.Runtime.CompilerServices" => true,
			_ => false,
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsSingle(string name, string ns) {
		return IsSingleName(name) || IsSingleNamespace(ns);
	}

	static bool IsSingleType(RepackingContext context, ref TypeDefinition copied) {
		if (IsSingleName(copied.Name) || IsSingleNamespace(copied.Namespace)) {
			foreach (var kv in context.types) {
				if (kv.Value is TypeDefinition def && def.Namespace == copied.Namespace && def.Name == copied.Name) {
					copied = def;
					return true;
				}
			}
		}
		return false;
	}

	public static TypeDefinition? Repack(RepackingContext context, TypeDefinition type, bool wasExplicit = false) {
		if (type.Module == context.output) return type;
		if (context.types.TryGetValue(type, out var tdEx)) {
			if (tdEx is TypeDefinition td) return td;
		}
		if (context.embedded.TryGetValue(type.FullName, out var embeddedEx)) return embeddedEx;
		var copied = NoFixCopyingUtil.copyType(type);
		if (IsSpecialType(context, ref copied)) {
			if (context.options.mode != RepackMode.Strict || wasExplicit) goto memberRepack;
		}
		else if (IsSingleType(context, ref copied)) {
			return copied;
		}
		else if (type.Name == "<Module>") {
			var ctor = GetStatic(type);
			if (ctor != null && context.options.repackModuleInitializers) {
				context.Enqueue(new RepackModuleInitializer(ctor));
			}
			return null;
		}
		else {
			bool isCompilerGenerated = false;
			bool embeddedAlreadyExists = false;
			bool isEmbedded = false;
			for (int i = 0; i < type.CustomAttributes.Count; i++) {
				if (type.CustomAttributes[i].AttributeType.FullName == "Microsoft.CodeAnalysis.EmbeddedAttribute") {
					isEmbedded = true;
					if (!context.embedded.ContainsKey(type.FullName))
						context.Add(type.FullName, copied);
					else
						embeddedAlreadyExists = true;
				}
				else if (type.CustomAttributes[i].AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute") {
					isCompilerGenerated = true;
				}
			}
			if (embeddedAlreadyExists) {
				var td = context.embedded[type.FullName];
				context.Add(type, td!);
				return td;
			}
			if (isCompilerGenerated && !isEmbedded && !IsSpecialName(copied.Name) && !IsSingle(copied.Name, copied.Namespace)) {
				copied.Name = $"<{type.Module.Assembly.Name.Name}>{copied.Name}";
			}
			else if (context.types.Any((v) => v.Value.Name == type.Name && v.Value.Namespace == type.Namespace)) {
				copied.Name = $"<{type.Module.Assembly.Name.Name}>{copied.Name}";
			}
		}
		context.options.typeCopied?.Invoke(type, copied);
		context.Add(type, copied);
		if (type.IsNested)
		{
			if (context.types.TryGetValue(type.DeclaringType, out var decl))
			{
				var td = (TypeDefinition)decl;
				td.NestedTypes.Add(copied);
				copied.DeclaringType = td;
			}
			else
			{
				context.EnqueueImmediate(new RepackType(type.DeclaringType));
				context.EnqueueImmediate(new RepackAssignType(copied, type.DeclaringType));
			}
		}
		else
		{
			context.output.Types.Add(copied);
		}
		for (int i = 0; i < type.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, type.CustomAttributes[i]));
		}
		for (int i = 0; i < type.GenericParameters.Count; i++) {
			context.EnqueueImmediate(new RepackGenericParameter(copied, copied.GenericParameters, i, type.GenericParameters[i]));
		}
		if (type.BaseType != null)
		{
			context.Enqueue(new RepackType(type.BaseType));
			context.Enqueue(new RepackAssignBaseType(copied, type.BaseType));
		}
		if (context.options.mode == RepackMode.Strict && !wasExplicit) {
			context.EnqueueNextGen(new RepackModuleAttributes(type.Module));
			return copied;
		}
		foreach (var intf in type.Interfaces) context.Enqueue(new RepackInterfaceImpl(copied.Interfaces, intf));
		memberRepack:
		foreach (var f in type.Fields) context.Enqueue(new RepackField(copied.Fields, f));
		foreach (var m in type.Methods) context.Enqueue(new RepackMethod(copied.Methods, m));
		foreach (var p in type.Properties) context.Enqueue(new RepackProperty(copied.Properties, p));
		foreach (var e in type.Events) context.Enqueue(new RepackEvent(copied.Events, e));
		context.EnqueueNextGen(new RepackModuleAttributes(type.Module));
		if (context.options.mode == RepackMode.Full && context.repacked.Add(type.Module)) {
			foreach (var t in type.Module.GetTypes()) {
				if (context.types.TryGetValue(t, out var tr)) {
					if (tr is TypeDefinition) continue;
				}
				context.Enqueue(new RepackType(t));
			}
		}
		return copied;
	}

	public static PropertyDefinition Repack(RepackingContext context, PropertyDefinition property) {
		var copied = NoFixCopyingUtil.copyProperty(property);
		context.Add(property, copied);
		for (int i = 0; i < property.CustomAttributes.Count; i++)
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, property.CustomAttributes[i]));
		context.Enqueue(new RepackType(property.PropertyType));
		context.Enqueue(new RepackPropertyType(copied, property.PropertyType));
		context.EnqueueImmediate(new RepackType(property.DeclaringType));
		context.EnqueueImmediate(new RepackAssignProperty(copied, property.DeclaringType));
		if (property.GetMethod != null) context.Enqueue(new RepackPropertyGetter(copied, property.GetMethod));
		if (property.SetMethod != null) context.Enqueue(new RepackPropertySetter(copied, property.SetMethod));
		for (int i = 0; i < property.OtherMethods.Count; i++) context.Enqueue(new RepackMethod(copied.OtherMethods, property.OtherMethods[i]));
		return copied;
	}

	public static EventDefinition Repack(RepackingContext context, EventDefinition ev) {
		var copied = NoFixCopyingUtil.copyEvent(ev);
		context.Add(ev, copied);
		for (int i = 0; i < ev.CustomAttributes.Count; i++)
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, ev.CustomAttributes[i]));
		context.Enqueue(new RepackType(ev.EventType));
		context.Enqueue(new RepackEventType(copied, ev.EventType));
		context.EnqueueImmediate(new RepackType(ev.DeclaringType));
		context.EnqueueImmediate(new RepackAssignEvent(copied, ev.DeclaringType));
		if (ev.AddMethod != null) context.Enqueue(new RepackEventAdd(copied, ev.AddMethod));
		if (ev.InvokeMethod != null) context.Enqueue(new RepackEventInvoke(copied, ev.InvokeMethod));
		if (ev.RemoveMethod != null) context.Enqueue(new RepackEventRemove(copied, ev.RemoveMethod));
		foreach (var m in ev.OtherMethods) context.Enqueue(new RepackMethod(copied.OtherMethods, m));
		return copied;
	}

	public static FieldDefinition Repack(RepackingContext context, FieldDefinition field) {
		var copied = NoFixCopyingUtil.copyField(field);
		context.Add(field, copied);
		for (int i = 0; i < field.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, field.CustomAttributes[i]));
		}
		context.Enqueue(new RepackType(field.FieldType));
		context.Enqueue(new RepackFieldType(copied, field.FieldType));
		context.EnqueueImmediate(new RepackType(field.DeclaringType));
		context.EnqueueImmediate(new RepackAssignField(copied, field.DeclaringType));
		return copied;
	}

	public static MethodDefinition Repack(RepackingContext context, MethodDefinition method, bool doDecl = true) {
		var copied = NoFixCopyingUtil.copyMethod(method);
		context.Add(method, copied);
		for (int i = 0; i < method.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, method.CustomAttributes[i]));
		}
		if (doDecl)
		{
			context.EnqueueImmediate(new RepackType(method.DeclaringType));
			context.EnqueueImmediate(new RepackAssignMethod(copied, method.DeclaringType));
		}
		for (int i = 0; i < method.GenericParameters.Count; i++) {
			context.EnqueueImmediate(new RepackGenericParameter(copied, copied.GenericParameters, i, method.GenericParameters[i]));
		}
		for (int i = 0; i < method.Parameters.Count; i++) {
			context.EnqueueImmediate(new RepackParameter(copied.Parameters, i, method.Parameters[i]));
		}
		for (int i = 0; i < method.Overrides.Count; i++) {
			context.Enqueue(new RepackOverride(copied.Overrides, i, method.Overrides[i]));
		}
		context.EnqueueImmediate(new RepackMethodReturnType(copied, method.MethodReturnType));
		if (method.HasBody) {
			context.Enqueue(new RepackBody(copied, method.Body));
		}
		if (context.options.repackDebugInformation)
		{
			if (method.DebugInformation != null)
			{
				context.EnqueueImmediate(new RepackMethodDebugInformation(copied, method.DebugInformation));
			}
			foreach (var cdi in method.CustomDebugInformations) context.Enqueue(new RepackMethodCustomDebugInformation(copied, cdi));
		}
		return copied;
	}

	public static CustomAttribute Repack(RepackingContext context, CustomAttribute attr) {
		var copied = new CustomAttribute(attr.Constructor);
		for (int i = 0; i < attr.ConstructorArguments.Count; i++) {
			context.Enqueue(new RepackAttributeArgument(copied.ConstructorArguments, i, attr.ConstructorArguments[i]));
		}
		for (int i = 0; i < attr.Fields.Count; i++) {
			context.Enqueue(new RepackAttributeNamedArgument(copied.Fields, i, attr.Fields[i]));
		}
		for (int i = 0; i < attr.Properties.Count; i++) {
			context.Enqueue(new RepackAttributeNamedArgument(copied.Properties, i, attr.Properties[i]));
		}
		return copied;
	}

	public static CustomAttribute Repack(RepackingContext context, CustomAttribute attr, MethodReference constr) {
		var copied = new CustomAttribute(constr);
		for (int i = 0; i < attr.ConstructorArguments.Count; i++) {
			context.Enqueue(new RepackAttributeArgument(copied.ConstructorArguments, i, attr.ConstructorArguments[i]));
		}
		for (int i = 0; i < attr.Fields.Count; i++) {
			context.Enqueue(new RepackAttributeNamedArgument(copied.Fields, i, attr.Fields[i]));
		}
		for (int i = 0; i < attr.Properties.Count; i++) {
			context.Enqueue(new RepackAttributeNamedArgument(copied.Properties, i, attr.Properties[i]));
		}
		return copied;
	}

	public static MethodDefinition? GetStatic(TypeDefinition type) {
		var staticMethod = type.Methods.FirstOrDefault((v) => v.Name == ".cctor");
		return staticMethod;
	}

	public static MethodDefinition GetOrCreateStatic(TypeDefinition type) {
		var staticMethod = type.Methods.FirstOrDefault((v) => v.Name == ".cctor");
		if (staticMethod != null) return staticMethod;
		var md = new MethodDefinition(".cctor", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName, type.Module.TypeSystem.Void);
		var mb = new MethodBody(md);
		md.Body = mb;
		type.Methods.Add(md);
		return md;
	}
}
abstract record Repack {
	public abstract void Run(RepackingContext context);
}
record RepackType(TypeReference type) : Repack {
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var _)) return;
		if (type is TypeDefinition td) {
			var copied = Common.Repack(context, td);
			if (copied == null) return;
			context.Add(type, copied);
			return;
		}
		else if (type is GenericParameter gp) {
			context.EnqueueImmediate(new RepackGeneric(gp));
			return;
		}
		else if (type is GenericInstanceType git) {
			context.EnqueueImmediate(new RepackGit(git));
			return;
		}
		else if (type is RequiredModifierType rmt) {
			context.EnqueueImmediate(new RepackRmt(rmt));
			return;
		}
		else if (type is OptionalModifierType omt) {
			context.EnqueueImmediate(new RepackOmt(omt));
			return;
		}
		else if (type is ArrayType at) {
			context.EnqueueImmediate(new RepackArrayType(at));
			return;
		}
		else if (type is ByReferenceType brt) {
			context.EnqueueImmediate(new RepackByReferenceType(brt));
			return;
		}
		else if (type is PointerType pt) {
			context.EnqueueImmediate(new RepackPointerType(pt));
			return;
		}
		else if (type is PinnedType pin) {
			context.EnqueueImmediate(new RepackPinnedType(pin));
			return;
		}
		else if (type is FunctionPointerType fpt) {
			context.EnqueueImmediate(new RepackFunctionPointerType(fpt));
			return;
		}
		else if (type is SentinelType st) {
			context.EnqueueImmediate(new RepackSentinelType(st));
			return;
		}
		var origIn = context.map.OriginatesIn(type);
		if (type.DeclaringType is GenericInstanceType gt) {
			if (context.types.TryGetValue(gt, out var rep)) {
				var newRef = new TypeReference(type.Namespace, type.Name, context.output, type.Scope, type.IsValueType)
				{
					DeclaringType = rep
				};
				context.Add(type, newRef);
				return;
			}
			else {
				context.EnqueueImmediate(new RepackGit(gt));
				context.EnqueueImmediate(this);
				return;
			}
		}
		if (origIn != null) {
			if (context.types.TryGetValue(origIn, out var origEx)) {
				context.Add(type, origEx);
				return;
			}
			var copied = Common.Repack(context, origIn);
			if (copied == null) return;
			context.Add(type, copied);
			return;
		}
		else {
			var r = type.Resolve();
			if (r != null)
			{
				if (r.Module == context.output)
				{
					context.Add(type, r);
					return;
				}
				if (context.types.TryGetValue(r, out var existing))
				{
					context.Add(type, existing);
					return;
				}
			}
			if (context.types.TryGetValue(type, out var ex))
			{
				context.Add(type, ex);
				return;
			}
			var reference = context.output.ImportReference(type);
			context.Add(type, reference);
			for (int i = 0; i < type.GenericParameters.Count; i++) {
				context.EnqueueImmediate(new RepackGenericParameter(reference, reference.GenericParameters, i, type.GenericParameters[i]));
			}
		}
	}
}
record RepackFunctionPointerType(FunctionPointerType fpt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(fpt, out var _)) return;
		if (context.types.TryGetValue(fpt.ReturnType, out var rt)) {
			var copied = new FunctionPointerType();
			copied.ReturnType = rt;
			copied.CallingConvention = fpt.CallingConvention;
			copied.ExplicitThis = fpt.ExplicitThis;
			copied.HasThis = fpt.HasThis;
			context.Add(fpt, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(fpt.ReturnType));
			context.EnqueueImmediate(this);
		}
	}
}

record RepackSentinelType(SentinelType st) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(st, out var _)) return;
		if (context.types.TryGetValue(st.ElementType, out var et)) {
			var copied = new SentinelType(et);
			context.Add(st, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(st.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackPinnedType(PinnedType pt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(pt, out var _)) return;
		if (context.types.TryGetValue(pt.ElementType, out var et)) {
			var copied = new PinnedType(et);
			context.Add(pt, copied);
			return;
		}
		else {
			context.EnqueueImmediate(new RepackType(pt.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackPointerType(PointerType pt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(pt, out var _)) return;
		if (context.types.TryGetValue(pt.ElementType, out var et)) {
			var copied = new PointerType(et);
			context.Add(pt, copied);
			return;
		}
		else {
			context.EnqueueImmediate(new RepackType(pt.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackByReferenceType(ByReferenceType brt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(brt, out var _)) return;
		if (context.types.TryGetValue(brt.ElementType, out var et)) {
			var copied = new ByReferenceType(et);
			context.Add(brt, copied);
			return;
		}
		else {
			context.EnqueueImmediate(new RepackType(brt.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackArrayType(ArrayType at) : Repack 
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(at, out var _)) return;
		if (context.types.TryGetValue(at.ElementType, out var tr)) {
			ArrayType copied = new(tr, at.Rank);
			context.Add(at, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(at.ElementType));
			context.EnqueueImmediate(this);
		}
	}	
}
record RepackInterfaceImpl(Collection<InterfaceImplementation> impls, InterfaceImplementation impl) : Repack
{
	public override void Run(RepackingContext context)
	{
		var copied = NoFixCopyingUtil.copyInterface(impl);
		context.Add(impl, copied);
		for (int i = 0; i < impl.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, impl.CustomAttributes[i]));
		}
		context.EnqueueImmediate(new RepackType(impl.InterfaceType));
		context.EnqueueImmediate(new RepackAssignInterface(copied, impl.InterfaceType));
		impls.Add(copied);
	}
}
record RepackField(Collection<FieldDefinition> fields, FieldDefinition field) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.fields.TryGetValue(field, out var _)) return;
		var copied = NoFixCopyingUtil.copyField(field);
		context.Add(field, copied);
		for (int i = 0; i < field.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, field.CustomAttributes[i]));
		}
		context.EnqueueImmediate(new RepackType(field.FieldType));
		context.EnqueueImmediate(new RepackFieldType(copied, field.FieldType));
		fields.Add(copied);
	}
}
record RepackMethod(Collection<MethodDefinition> methods, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex)) return;
		var copied = Common.Repack(context, method, false);
		if (method.Module.EntryPoint == method) context.output.EntryPoint = copied;
		methods.Add(copied);
	}
}
record RepackProperty(Collection<PropertyDefinition> properties, PropertyDefinition property) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.properties.TryGetValue(property, out var _)) return;
		var copied = NoFixCopyingUtil.copyProperty(property);
		context.Add(property, copied);
		for (int i = 0; i < property.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, property.CustomAttributes[i]));
		}
		context.Enqueue(new RepackType(property.PropertyType));
		context.EnqueueImmediate(new RepackType(property.DeclaringType));
		context.Enqueue(new RepackPropertyType(copied, property.PropertyType));
		context.EnqueueImmediate(new RepackAssignProperty(copied, property.DeclaringType));
		if (property.GetMethod != null) context.Enqueue(new RepackPropertyGetter(copied, property.GetMethod));
		if (property.SetMethod != null) context.Enqueue(new RepackPropertySetter(copied, property.SetMethod));
		foreach (var m in property.OtherMethods) context.Enqueue(new RepackMethod(copied.OtherMethods, m));
	}
}
record RepackPropertyGetter(PropertyDefinition property, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex)) {
			property.GetMethod = (MethodDefinition)ex;
			return;
		}
		var copied = Common.Repack(context, method);
		property.GetMethod = copied;
	}
}
record RepackPropertySetter(PropertyDefinition property, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex)) {
			property.SetMethod = (MethodDefinition)ex;
			return;
		}
		var copied = Common.Repack(context, method);
		property.SetMethod = copied;
	}
}
record RepackEvent(Collection<EventDefinition> events, EventDefinition ev) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.events.TryGetValue(ev, out var _)) return;
		var copied = NoFixCopyingUtil.copyEvent(ev);
		context.Add(ev, copied);
		for (int i = 0; i < ev.CustomAttributes.Count; i++) {
			context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, ev.CustomAttributes[i]));
		}
		context.EnqueueImmediate(new RepackType(ev.DeclaringType));
		context.EnqueueImmediate(new RepackAssignEvent(copied, ev.DeclaringType));
		context.Enqueue(new RepackType(ev.EventType));
		context.Enqueue(new RepackEventType(copied, ev.EventType));
		if (ev.AddMethod != null) context.Enqueue(new RepackEventAdd(copied, ev.AddMethod));
		if (ev.InvokeMethod != null) context.Enqueue(new RepackEventInvoke(copied, ev.InvokeMethod));
		if (ev.RemoveMethod != null) context.Enqueue(new RepackEventRemove(copied, ev.RemoveMethod));
		foreach (var m in ev.OtherMethods) context.Enqueue(new RepackMethod(copied.OtherMethods, m));
	}
}
record RepackEventAdd(EventDefinition ev, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex) && ex is MethodDefinition md) {
			ev.AddMethod = md;
			return;
		}
		var copied = Common.Repack(context, method);
		ev.AddMethod = copied;
	}
}
record RepackEventInvoke(EventDefinition ev, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex) && ex is MethodDefinition md) {
			ev.InvokeMethod = md;
			return;
		}
		var copied = Common.Repack(context, method);
		ev.InvokeMethod = copied;
	}
}
record RepackEventRemove(EventDefinition ev, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex) && ex is MethodDefinition md) {
			ev.RemoveMethod = md;
			return;
		}
		var copied = Common.Repack(context, method);
		ev.RemoveMethod = copied;
	}
}
record RepackEventType(EventDefinition ev, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var tr)) {
			ev.EventType = tr;
		}
		else {
			context.EnqueueImmediate(new RepackType(type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackTargetType(TypeDefinition type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var _)) return;
		Common.Repack(context, type, true);
	}
}
record RepackTargetMethod(MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var _)) return;
		Common.Repack(context, method);
	}
}
record RepackTargetProperty(PropertyDefinition property) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.properties.TryGetValue(property, out var _)) return;
		Common.Repack(context, property);
	}
}
record RepackTargetEvent(EventDefinition ev) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.events.TryGetValue(ev, out var _)) return;
		Common.Repack(context, ev);
	}
}
record RepackTargetField(FieldDefinition field) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.fields.TryGetValue(field, out var _)) return;
		Common.Repack(context, field);
	}
}
record RepackTargetModule(ModuleDefinition module) : Repack
{
	public override void Run(RepackingContext context)
	{
		foreach (var t in module.GetTypes()) {
			context.Enqueue(new RepackType(t));
		}
		context.Enqueue(new RepackModuleAttributes(module));
	}
}
record RepackGenericParameter(IGenericParameterProvider provider, Collection<GenericParameter> generics, int idx, GenericParameter parameter) : Repack
{
	public override void Run(RepackingContext context)
	{
		var copied = NoFixCopyingUtil.copyGeneric(parameter, provider);
		context.Add(parameter, copied);
		context.Add(provider, idx, parameter);
		context.Enqueue(new RepackGenericConstraints(parameter));
		generics.ReplaceSlot(idx, copied);
	}
}
record RepackGenericConstraints(GenericParameter parameter) : Repack {
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(parameter, out var c)) {
			var copied = (GenericParameter)c;
			for (int i = 0; i < parameter.Constraints.Count; i++) {
				context.Enqueue(new RepackConstraint(copied.Constraints, i, parameter.Constraints[i]));
			}
			for (int i = 0; i < parameter.CustomAttributes.Count; i++) {
				context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, parameter.CustomAttributes[i]));
			}
		}
		else {
			throw new Exception("man.");
		}
	}
}
record RepackConstraint(Collection<GenericParameterConstraint> constraints, int idx, GenericParameterConstraint constraint) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(constraint.ConstraintType, out var ct)) {
			var copied = new GenericParameterConstraint(ct);
			for (int i = 0; i < constraint.CustomAttributes.Count; i++) {
				context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, constraint.CustomAttributes[i]));
			}
			constraints.ReplaceSlot(idx, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(constraint.ConstraintType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackParameter(Collection<ParameterDefinition> parameters, int idx, ParameterDefinition parameter) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(parameter.ParameterType, out var pt)) {
			var copied = NoFixCopyingUtil.copyParameter(parameter);
			copied.ParameterType = pt;
			for (int i = 0; i < parameter.CustomAttributes.Count; i++) {
				context.Enqueue(new RepackAttribute(copied.CustomAttributes, i, parameter.CustomAttributes[i]));
			}
			parameters.Add(copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(parameter.ParameterType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackPropertyType(PropertyDefinition on, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var tr)) {
			on.PropertyType = tr;
		}
		else {
			context.Enqueue(new RepackType(type));
			context.Enqueue(this);
		}
	}
}
record RepackFieldType(FieldReference on, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var tr)) {
			on.FieldType = tr;
		}
		else {
			context.Enqueue(new RepackType(type));
			context.Enqueue(this);
		}
	}
}
record RepackMethodReturnType(MethodReference on, MethodReturnType mrt) : Repack
{
	public override void Run(RepackingContext context)
	{
		var copied = new MethodReturnType(on);
		context.Enqueue(new RepackReturnType(copied, mrt.ReturnType));
		context.Enqueue(new RepackReturnTypeAttributes(copied, mrt.CustomAttributes));
		on.MethodReturnType = copied;
		context.Add(mrt, copied);
	}
}
record RepackReturnTypeAttributes(MethodReturnType mrt, Collection<CustomAttribute> attributes) : Repack
{
	public override void Run(RepackingContext context)
	{
		for (int i = 0; i < attributes.Count; i++)
		{
			context.Enqueue(new RepackAttribute(mrt.CustomAttributes, i, attributes[i]));
		}
	}
}
record RepackMethodReference(MethodReference method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var _)) return;
		if (method is GenericInstanceMethod gim) {
			context.EnqueueImmediate(new RepackGim(gim));
			return;
		}
		if (method is MethodDefinition md) {
			var copied = Common.Repack(context, md);
			context.Add(method, copied);
			return;
		}
		var origIn = context.map.OriginatesIn(method);
		if (method.DeclaringType is GenericInstanceType gt) {
			if (context.types.TryGetValue(gt, out var rep)) {
				var newRef = new MethodReference(method.Name, method.ReturnType, rep)
				{
					HasThis = method.HasThis,
					ExplicitThis = method.ExplicitThis,
					CallingConvention = method.CallingConvention
				};
				for (int i = 0; i < method.GenericParameters.Count; i++) {
					context.EnqueueImmediate(new RepackGenericParameter(newRef, newRef.GenericParameters, i, method.GenericParameters[i]));
				}
				for (int i = 0; i < method.Parameters.Count; i++) {
					context.EnqueueImmediate(new RepackParameter(newRef.Parameters, i, method.Parameters[i]));
				}
				context.EnqueueImmediate(new RepackMethodReturnType(newRef, method.MethodReturnType));
				context.Add(method, newRef);
				return;
			}
			else {
				context.EnqueueImmediate(new RepackGit(gt));
				context.EnqueueImmediate(this);
				return;
			}
		}
		if (origIn != null) {
			if (context.methods.TryGetValue(origIn, out var origEx)) {
				context.Add(method, origEx);
				return;
			}
			var repacked = Common.Repack(context, origIn);
			context.Add(method, repacked);
			return;
		}
		else {
			var r = method.Resolve();
			if (r != null) {
				if (r.Module == context.output) {
					context.Add(method, r);
					return;
				}
				if (context.methods.TryGetValue(r, out var ex)) {
					context.Add(method, ex);
					return;
				}
			}
			var reference = context.output.ImportReference(method);
			context.Add(method, reference);
			if (r != null) context.Add(r, reference);
		}
	}
}
record RepackFieldReference(FieldReference field) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.fields.TryGetValue(field, out var _)) return;
		if (field is FieldDefinition fd) {
			Common.Repack(context, fd);
			return;
		}
		var origIn = context.map.OriginatesIn(field);
		if (field.DeclaringType is GenericInstanceType git) {
			if (context.types.TryGetValue(git, out var rep)) {
				var newRef = new FieldReference(field.Name, field.FieldType, rep);
				context.Enqueue(new RepackFieldType(newRef, field.FieldType));
				context.Add(field, newRef);
				return;
			}
			else {
				context.EnqueueImmediate(new RepackGit(git));
				context.EnqueueImmediate(this);
				return;
			}
		}
		if (origIn != null) {
			if (context.fields.TryGetValue(origIn, out var origEx)) {
				context.Add(field, origEx);
				return;
			}
			var copied = Common.Repack(context, origIn);
			context.Add(field, copied);
			return;
		}
		else {
			var r = field.Resolve();
			if (r != null) {
				if (r.Module == context.output) {
					context.Add(field, r);
					return;
				}
				if (context.fields.TryGetValue(r, out var ex)) {
					context.Add(field, ex);
					return;
				}
			}
			var reference = context.output.ImportReference(field);
			context.Add(field, reference);
		}
	}
}
record RepackInstruction(MethodBody to, int idx, MethodBody on, Instruction instruction) : Repack {
	public override void Run(RepackingContext context) {
		var tmpCopy = NoFixCopyingUtil.copyInstruction(instruction);
		to.Instructions.Add(tmpCopy);
		var instr = NoFixCopyingUtil.copyInstruction(instruction);
		// if (on.Method.Name.Contains("scan") && instruction.Operand != null && instruction.OpCode == OpCodes.Newobj) {

		// }
		// Console.WriteLine($"repacking instruction {instruction} {instruction.Offset} {on.Method}");
		if (instr.Operand is MethodReference method) {
			context.Enqueue(new RepackInstructionMethod(to, idx, instruction, instr, method));
		}
		else if (instr.Operand is FieldReference field) {
			context.Enqueue(new RepackInstructionField(to, idx, instruction, instr, field));
		}
		else if (instr.Operand is TypeReference type) {
			context.Enqueue(new RepackInstructionType(to, idx, instruction, instr, type));
		}
		else if (instruction.Operand is Instruction instrRef) {
			context.EnqueueNextGen(new RepackInstructionRef(to, idx, instruction, instr, instrRef));
		}
		else {
			to.Instructions[idx] = instr;
			context.instructions[instruction] = instr;
		}
	}
}
record RepackInstructionField(MethodBody addTo, int idx, Instruction original, Instruction on, FieldReference field) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.fields.TryGetValue(field, out var ex)) {
			on.Operand = ex;
			addTo.Instructions[idx] = on;
			context.instructions[original] = on;
		}
		else {
			context.Enqueue(new RepackFieldReference(field));
			context.Enqueue(this);
		}
	}
}
record RepackInstructionType(MethodBody addTo, int idx, Instruction original, Instruction on, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var ex)) {
			on.Operand = ex;
			addTo.Instructions[idx] = on;
			context.instructions[original] = on;
		}
		else {
			context.Enqueue(new RepackType(type));
			context.Enqueue(this);
		}
	}
}
record RepackInstructionMethod(MethodBody addTo, int idx, Instruction original, Instruction on, MethodReference method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex)) {
			on.Operand = ex;
			addTo.Instructions[idx] = on;
			context.instructions[original] = on;
		}
		else {
			context.Enqueue(new RepackMethodReference(method));
			context.Enqueue(this);
		}
	}
}
record RepackInstructionRef(MethodBody addTo, int idx, Instruction original, Instruction on, Instruction repack) : Repack 
{
	public override void Run(RepackingContext context)
	{
		if (context.instructions.TryGetValue(repack, out var diff)) {
			on.Operand = diff;
			addTo.Instructions[idx] = on;
			context.instructions[original] = on;
			return;
		}
		context.EnqueueNextGen(this);
	}
}
record RepackExceptionHandler(Collection<ExceptionHandler> handlers, MethodBody on, ExceptionHandler handler) : Repack
{
	public override void Run(RepackingContext context)
	{
		ExceptionHandler copied = new(handler.HandlerType);
		if (handler.CatchType != null) context.Enqueue(new RepackCatchType(copied, handler.CatchType));
		if (handler.FilterStart != null) context.EnqueueNextGen(new RepackFilterStart(copied, on, handler.FilterStart));
		context.EnqueueNextGen(new RepackHandlerStart(copied, on, handler.HandlerStart));
		context.EnqueueNextGen(new RepackHandlerTryStart(copied, on, handler.TryStart));
		if (handler.HandlerEnd != null) context.EnqueueNextGen(new RepackHandlerEnd(copied, on, handler.HandlerEnd));
		if (handler.TryEnd != null) context.EnqueueNextGen(new RepackHandlerTryEnd(copied, on, handler.TryEnd));
		handlers.Add(copied);
	}
}
record RepackCatchType(ExceptionHandler handler, TypeReference catchType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(catchType, out var ct)) {
			handler.CatchType = ct;
		}
		else
		{
			context.EnqueueImmediate(new RepackType(catchType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackFilterStart(ExceptionHandler handler, MethodBody on, Instruction start) : Repack
{
	public override void Run(RepackingContext context)
	{
		// Console.WriteLine($"trying to get filter start for {start} {start.Offset} {on.Method}");
		if (context.instructions.TryGetValue(start, out var diff)) {
			handler.FilterStart = diff;
			return;
		}
		context.EnqueueNextGen(this);
	}
}
record RepackHandlerStart(ExceptionHandler handler, MethodBody on, Instruction start) : Repack
{
	public override void Run(RepackingContext context)
	{
		// Console.WriteLine($"trying to get handler start for {start} {start.Offset} {on.Method}");
		if (context.instructions.TryGetValue(start, out var diff)) {
			handler.HandlerStart = diff;
			return;
		}
		context.EnqueueNextGen(this);
	}
}
record RepackHandlerEnd(ExceptionHandler handler, MethodBody on, Instruction end) : Repack
{
	public override void Run(RepackingContext context)
	{
		// Console.WriteLine($"trying to get handler end for {end} {end.Offset} {on.Method}");
		if (context.instructions.TryGetValue(end, out var diff)) {
			handler.HandlerEnd = diff;
			return;
		}
		context.EnqueueNextGen(this);
	}
}
record RepackHandlerTryStart(ExceptionHandler handler, MethodBody on, Instruction start) : Repack
{
	public override void Run(RepackingContext context)
	{
		// Console.WriteLine($"trying to get try start for {start} {start.Offset} {on.Method}");
		if (context.instructions.TryGetValue(start, out var diff)) {
			handler.TryStart = diff;
			return;
		}
		context.EnqueueNextGen(this);
	}
}
record RepackHandlerTryEnd(ExceptionHandler handler, MethodBody on, Instruction end) : Repack
{
	public override void Run(RepackingContext context)
	{
		// Console.WriteLine($"trying to get try end for {end} {end.Offset} {on.Method}");
		if (context.instructions.TryGetValue(end, out var diff)) {
			handler.TryEnd = diff;
			return;
		}
		context.EnqueueNextGen(this);
	}
}
record RepackRmt(RequiredModifierType rmt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(rmt, out var _)) return;
		if (context.types.TryGetValue(rmt.ModifierType, out var mt) && context.types.TryGetValue(rmt.ElementType, out var et)) {
			var copied = NoFixCopyingUtil.copyRmtNew(rmt, mt, et);
			context.Add(rmt, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(rmt.ModifierType));
			context.EnqueueImmediate(new RepackType(rmt.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackOmt(OptionalModifierType omt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(omt, out var _)) return;
		if (context.types.TryGetValue(omt.ModifierType, out var mt) && context.types.TryGetValue(omt.ElementType, out var et)) {
			var copied = new OptionalModifierType(mt, et);
			context.Add(omt, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(omt.ModifierType));
			context.EnqueueImmediate(new RepackType(omt.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackGit(GenericInstanceType git) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(git, out var _)) return;
		if (context.types.TryGetValue(git.ElementType, out var mr)) {
			var copied = NoFixCopyingUtil.copyGitWithNew(git, mr);
			for (int i = 0; i < git.GenericArguments.Count; i++)
			{
				context.EnqueueImmediate(new RepackGenericArgument(copied.GenericArguments, i, git.GenericArguments[i]));
			}
			context.Add(git, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(git.ElementType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackGim(GenericInstanceMethod gim) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(gim, out var _)) return;
		if (context.methods.TryGetValue(gim.ElementMethod, out var mr)) {
			var copied = NoFixCopyingUtil.copyGimWithNew(gim, mr);
			for (int i = 0; i < gim.GenericArguments.Count; i++) {
				context.EnqueueImmediate(new RepackGenericArgument(copied.GenericArguments, i, gim.GenericArguments[i]));
			}
			context.Add(gim, copied);
		}
		else {
			context.EnqueueImmediate(new RepackMethodReference(gim.ElementMethod));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackGenericArgument(Collection<TypeReference> arguments, int idx, TypeReference argument) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(argument, out var tr)) {
			arguments.ReplaceSlot(idx, tr);
		}
		else {
			context.Enqueue(new RepackType(argument));
			context.Enqueue(this);
		}
	}
}
record RepackConstraintRef(GenericParameterConstraint on, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var constr)) {
			on.ConstraintType = constr;
		}
		else {
			context.Enqueue(new RepackType(type));
			context.Enqueue(this);
		}
	}
}
record RepackConstraintRmt(GenericParameterConstraint on, RequiredModifierType rmt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(rmt, out var rep)) {
			on.ConstraintType = rep;
		}
		else {
			context.Enqueue(new RepackRmt(rmt));
			context.Enqueue(this);
		}
	}
}
record RepackConstraintOmt(GenericParameterConstraint on, OptionalModifierType omt) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(omt, out var rep)) {
			on.ConstraintType = rep;
		}
		else {
			context.Enqueue(new RepackOmt(omt));
			context.Enqueue(this);
		}
	}
}
record RepackConstraintGit(GenericParameterConstraint on, GenericInstanceType git) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(git, out var rep)) {
			on.ConstraintType = rep;
		}
		else {
			context.Enqueue(new RepackGit(git));
			context.Enqueue(this);
		}
	}
}
record RepackGeneric(GenericParameter gp) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.generics.TryGetValue((gp.Owner, gp.Position), out var _)) return;
		if (context.types.TryGetValue(gp, out var _)) return;
		var owner = gp.Owner;
		if (owner is TypeReference tr) {
			if (context.types.TryGetValue(tr, out var orig) && context.generics.TryGetValue((orig, gp.Position), out var res)) {
				context.Add(gp.Owner, gp.Position, res);
				context.Add(gp, res);
				return;
			}
			var origIn = context.map.OriginatesIn(tr);
			if (origIn != null && context.types.TryGetValue(origIn, out var origI) && context.generics.TryGetValue((origI, gp.Position), out var origres))
			{
				context.Add(gp.Owner, gp.Position, origres);
				context.Add(gp, origres);
				return;
			}
			if (context.generics.TryGetValue((tr, gp.Position), out var trR)) {
				context.Add(gp.Owner, gp.Position, trR);
				context.Add(gp, trR);
				return;
			}
			context.EnqueueImmediate(new RepackType(tr));
		}
		else if (owner is MethodReference mr) {
			if (context.methods.TryGetValue(mr, out var orig) && context.generics.TryGetValue((orig, gp.Position), out var res)) {
				context.Add(gp.Owner, gp.Position, res);
				context.Add(gp, res);
				return;
			}
			var origIn = context.map.OriginatesIn(mr);
			if (origIn != null && context.methods.TryGetValue(origIn, out var origI) && context.generics.TryGetValue((origI, gp.Position), out var origres))
			{
				context.Add(gp.Owner, gp.Position, origres);
				context.Add(gp, origres);
				return;
			}
			if (context.generics.TryGetValue((mr, gp.Position), out var mrR)) {
				context.Add(gp.Owner, gp.Position, mrR);
				context.Add(gp, mrR);
				return;
			}
			context.EnqueueImmediate(new RepackMethodReference(mr));
		}
	}
}
record RepackMethodGit(MethodReference on, GenericInstanceType git) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(git, out var rep)) {
			on.DeclaringType = rep;
		}
		else {
			context.EnqueueImmediate(new RepackGit(git));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackInterfaceImplGit(InterfaceImplementation on, GenericInstanceType git) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(git, out var rep)) {
			on.InterfaceType = rep;
		}
		else {
			context.EnqueueImmediate(new RepackGit(git));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAttribute(Collection<CustomAttribute> attributes, int idx, CustomAttribute attribute) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(attribute.Constructor, out var constr)) {
			var copied = NoFixCopyingUtil.copyAttributeWithNew(attribute, constr);
			attributes.ReplaceSlot(idx, copied);
			for (int i = 0; i < attribute.ConstructorArguments.Count; i++)
				context.Enqueue(new RepackAttributeArgument(copied.ConstructorArguments, i, attribute.ConstructorArguments[i]));
			for (int i = 0; i < attribute.Fields.Count; i++)
				context.Enqueue(new RepackAttributeNamedArgument(copied.Fields, i, attribute.Fields[i]));
			for (int i = 0; i < attribute.Properties.Count; i++)
				context.Enqueue(new RepackAttributeNamedArgument(copied.Properties, i, attribute.Properties[i]));
		}
		else {
			context.EnqueueImmediate(new RepackMethodReference(attribute.Constructor));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAttributeArgument(Collection<CustomAttributeArgument> args, int idx, CustomAttributeArgument argument) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(argument.Type, out var tr)) {
			var newArg = new CustomAttributeArgument(tr, argument.Value);
			args.ReplaceSlot(idx, newArg);
		}
		else {
			context.EnqueueImmediate(new RepackType(argument.Type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAttributeArgumentValue(CustomAttributeArgument[] args, int idx, CustomAttributeArgument argument) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(argument.Type, out var tr)) {
			var newArg = new CustomAttributeArgument(tr, argument.Value);
			args[idx] = newArg;
		}
		else {
			context.EnqueueImmediate(new RepackType(argument.Type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAttributeNamedArgument(Collection<CustomAttributeNamedArgument> args, int idx, CustomAttributeNamedArgument argument) : Repack
{
	public override void Run(RepackingContext context)
	{
		var a = argument.Argument;
		if (context.types.TryGetValue(a.Type, out var tr)) {
			var newArg = new CustomAttributeNamedArgument(argument.Name, new(tr, a.Value));
			if (newArg.Argument.Value is CustomAttributeArgument[] caa) {
				for (int i = 0; i < caa.Length; i++)
				{
					context.EnqueueImmediate(new RepackAttributeArgumentValue(caa, i, caa[i]));
				}
			}
			args.ReplaceSlot(idx, newArg);
		}
		else {
			context.EnqueueImmediate(new RepackType(a.Type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackOverride(Collection<MethodReference> overrides, int idx, MethodReference ov) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(ov, out var mr)) {
			overrides.ReplaceSlot(idx, mr);
		}
		else {
			context.EnqueueImmediate(new RepackMethodReference(ov));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackVariable(Collection<VariableDefinition> variables, int idx, VariableDefinition variable) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(variable.VariableType, out var tr)) {
			var copied = NoFixCopyingUtil.copyVariable(variable);
			copied.VariableType = tr;
			variables.ReplaceSlot(idx, copied);
		}
		else {
			context.EnqueueImmediate(new RepackType(variable.VariableType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackBody(MethodDefinition on, MethodBody body) : Repack
{
	public override void Run(RepackingContext context)
	{
		var mb = new MethodBody(on);
		mb.InitLocals = body.InitLocals;
		on.Body = mb;
		mb.Instructions.Clear();
		for (int i = 0; i < body.Variables.Count; i++) context.Enqueue(new RepackVariable(mb.Variables, i, body.Variables[i]));
		for (int i = 0; i < body.Instructions.Count; i++) context.EnqueueImmediate(new RepackInstruction(mb, i, body, body.Instructions[i]));
		foreach (var exc in body.ExceptionHandlers) context.Enqueue(new RepackExceptionHandler(mb.ExceptionHandlers, body, exc));
	}
}
record RepackAssignProperty(PropertyDefinition property, TypeDefinition declType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(declType, out var td)) {
			var t = (TypeDefinition)td;
			t.Properties.Add(property);
		}
		else {
			context.EnqueueImmediate(new RepackType(declType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignMethod(MethodDefinition method, TypeDefinition declType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(declType, out var td)) {
			var t = (TypeDefinition)td;
			t.Methods.Add(method);
		}
		else {
			context.EnqueueImmediate(new RepackType(declType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignEvent(EventDefinition ev, TypeDefinition declType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(declType, out var td)) {
			var t = (TypeDefinition)td;
			t.Events.Add(ev);
		}
		else {
			context.EnqueueImmediate(new RepackType(declType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignField(FieldDefinition field, TypeDefinition declType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(declType, out var td)) {
			var t = (TypeDefinition)td;
			t.Fields.Add(field);
		}
		else {
			context.EnqueueImmediate(new RepackType(declType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignType(TypeDefinition type, TypeDefinition declType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(declType, out var td)) {
			var t = (TypeDefinition)td;
			t.NestedTypes.Add(type);
			type.DeclaringType = t;
		}
		else {
			context.EnqueueImmediate(new RepackType(declType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignInterface(InterfaceImplementation impl, TypeReference implType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(implType, out var tr)) {
			impl.InterfaceType = tr;
		}
		else {
			context.EnqueueImmediate(new RepackType(implType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignBaseType(TypeDefinition type, TypeReference baseType) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(baseType, out var tr)) {
			type.BaseType = tr;
		}
		else {
			context.EnqueueImmediate(new RepackType(baseType));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackReturnType(MethodReturnType returnType, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var tr)) {
			returnType.ReturnType = tr;
		}
		else {
			context.EnqueueImmediate(new RepackType(type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackModuleInitializer(MethodDefinition ctor) : Repack
{
	public override void Run(RepackingContext context)
	{
		var cctor = Common.GetOrCreateStatic(context.output.GetType("<Module>"));
		var body = cctor.Body;
		var il = body.GetILProcessor();
		foreach (var instr in ctor.Body.Instructions) {
			if (instr.OpCode == OpCodes.Ret) continue;
			il.Append(NoFixCopyingUtil.copyInstruction(instr));
		}
	}
}
record RepackFinalizeModuleInitializer(MethodDefinition ctor) : Repack
{
	public override void Run(RepackingContext context)
	{
		var body = ctor.Body;
		var il = body.GetILProcessor();
		il.Emit(OpCodes.Ret);
	}
}

static class SpecialAttributes {
	public const string ModuleInitializerAttributeName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";
	public const string CompilationRelaxationName = "System.Runtime.CompilerServices.CompilationRelaxationsAttribute";
	public const string RefSafetyRulesAttributeName = "System.Runtime.CompilerServices.RefSafetyRulesAttribute";
	public const string RuntimeCompatibilityName = "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute";
	public const string UnverifiableCodeName = "System.Security.UnverifiableCodeAttribute";
	public const string DebuggableAttributeName = "System.Diagnostics.DebuggableAttribute";

	public static void CopyModule(Repack repackInstance, RepackingContext context, string specialName, CustomAttribute attribute) {
		if (context.methods.TryGetValue(attribute.Constructor, out var constr)) {
			var newAttr = Common.Repack(context, attribute, constr);
			context.moduleAttributes[specialName] = newAttr;
			context.output.CustomAttributes.Add(newAttr);
			return;
		}
		context.Enqueue(new RepackMethodReference(attribute.Constructor));
		context.Enqueue(repackInstance);
	}

	public static void CopyAssembly(Repack repackInstance, RepackingContext context, string specialName, CustomAttribute attribute) {
		if (context.methods.TryGetValue(attribute.Constructor, out var constr)) {
			var newAttr = Common.Repack(context, attribute, constr);
			context.assemblyAttributes[specialName] = newAttr;
			context.output.Assembly.CustomAttributes.Add(newAttr);
			return;
		}
		context.Enqueue(new RepackMethodReference(attribute.Constructor));
		context.Enqueue(repackInstance);
	}
}

record RepackModuleAttribute(CustomAttribute attribute) : Repack
{
	public override void Run(RepackingContext context)
	{
		switch (attribute.AttributeType.FullName) {
			case SpecialAttributes.RefSafetyRulesAttributeName:
				RunRefSafetyRules(context, attribute);
				break;
			case SpecialAttributes.UnverifiableCodeName:
				RunUnverifiableCode(context, attribute);
				break;
			default:
				RunOther(context, attribute);
				break;
		}
	}

	void RunOther(RepackingContext context, CustomAttribute other) {

	}

	void RunUnverifiableCode(RepackingContext context, CustomAttribute unverifiable) {
		if (context.moduleAttributes.TryGetValue(SpecialAttributes.UnverifiableCodeName, out var _)) return;
		SpecialAttributes.CopyModule(this, context, SpecialAttributes.UnverifiableCodeName, unverifiable);
	}

	void RunRefSafetyRules(RepackingContext context, CustomAttribute safety) {
		if (context.moduleAttributes.TryGetValue(SpecialAttributes.RefSafetyRulesAttributeName, out var existing)) {
			if (existing.ConstructorArguments.Count == 0)
			{
				context.EnqueueNextGen(this);
				return;
			}
			context.output.CustomAttributes.Remove(existing);
			var currentArg = existing.ConstructorArguments.First();
			var currentVal = (int)currentArg.Value;
			var otherVal = (int)safety.ConstructorArguments.First().Value;
			if (otherVal > currentVal) existing.ConstructorArguments[0] = new(currentArg.Type, otherVal);
			var newAttr = Common.Repack(context, existing);
			context.moduleAttributes[SpecialAttributes.RefSafetyRulesAttributeName] = newAttr;
			context.output.CustomAttributes.Add(newAttr);
		}
		else {
			SpecialAttributes.CopyModule(this, context, SpecialAttributes.RefSafetyRulesAttributeName, safety);
		}
	}
}
record RepackAssemblyAttribute(CustomAttribute attribute) : Repack
{
	public override void Run(RepackingContext context)
	{
		switch (attribute.AttributeType.FullName) {
			case SpecialAttributes.DebuggableAttributeName:
				RunDebuggable(context, attribute);
				break;
			case SpecialAttributes.RuntimeCompatibilityName:
				RunRuntimeCompatibility(context, attribute);
				break;
			case SpecialAttributes.CompilationRelaxationName:
				RunCompilationRelaxations(context, attribute);
				break;
			default:
				RunOther(context, attribute);
				break;
		}
	}

	void RunOther(RepackingContext context, CustomAttribute other) {

	}

	void RunDebuggable(RepackingContext context, CustomAttribute debuggable) {
		if (context.assemblyAttributes.TryGetValue(SpecialAttributes.DebuggableAttributeName, out var existing)) {
			if (existing.ConstructorArguments.Count == 0)
			{
				context.EnqueueNextGen(this);
				return;
			}
			context.output.Assembly.CustomAttributes.Remove(existing);
			var currentArg = existing.ConstructorArguments.First();
			var currentVal = (DebuggableAttribute.DebuggingModes)currentArg.Value;
			var otherVal = (DebuggableAttribute.DebuggingModes)debuggable.ConstructorArguments.First().Value;
			var newVal = currentVal | otherVal;
			if (context.options.treatOutputAsRelease) newVal &= ~DebuggableAttribute.DebuggingModes.DisableOptimizations;
			existing.ConstructorArguments[0] = new(currentArg.Type, newVal);
			var newAttr = Common.Repack(context, existing);
			context.output.Assembly.CustomAttributes.Add(newAttr);
			context.assemblyAttributes[SpecialAttributes.DebuggableAttributeName] = newAttr;
		}
		else {
			SpecialAttributes.CopyAssembly(this, context, SpecialAttributes.DebuggableAttributeName, debuggable);
		}
	}

	void RunRuntimeCompatibility(RepackingContext context, CustomAttribute compat) {
		if (context.assemblyAttributes.TryGetValue(SpecialAttributes.RuntimeCompatibilityName, out var existing)) {
			if (existing.Properties.Count == 0)
			{
				context.EnqueueNextGen(this);
				return;
			}
			context.output.Assembly.CustomAttributes.Remove(existing);
			var prop = existing.Properties.First();
			var currentVal = (bool)prop.Argument.Value;
			var otherVal = (bool)compat.Properties.First().Argument.Value;
			if (!currentVal && otherVal) existing.Properties[0] = new(prop.Name, new(prop.Argument.Type, true));
			var newAttr = Common.Repack(context, existing);
			context.output.Assembly.CustomAttributes.Add(newAttr);
			context.assemblyAttributes[SpecialAttributes.RuntimeCompatibilityName] = newAttr;
		}
		else {
			SpecialAttributes.CopyAssembly(this, context, SpecialAttributes.RuntimeCompatibilityName, compat);
		}
	}

	void RunCompilationRelaxations(RepackingContext context, CustomAttribute relaxations) {
		if (context.assemblyAttributes.TryGetValue(SpecialAttributes.CompilationRelaxationName, out var relax)) {
			if (relax.ConstructorArguments.Count == 0)
			{
				context.EnqueueNextGen(this);
				return;
			}
			context.output.Assembly.CustomAttributes.Remove(relax);
			var currentArg = relax.ConstructorArguments.First();
			var currentVal = (int)currentArg.Value;
			var otherVal = (int)relaxations.ConstructorArguments.First().Value;
			relax.ConstructorArguments[0] = new(currentArg.Type, currentVal | otherVal);
			var newAttr = Common.Repack(context, relax);
			context.output.Assembly.CustomAttributes.Add(newAttr);
			context.assemblyAttributes[SpecialAttributes.CompilationRelaxationName] = newAttr;
		}
		else {
			SpecialAttributes.CopyAssembly(this, context, SpecialAttributes.CompilationRelaxationName, relaxations);
		}
	}
}
record RepackModuleAttributes(ModuleDefinition module) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.modulesChecked.Add(module))
		{
			foreach (var attr in module.CustomAttributes)
			{
				context.EnqueueNextGen(new RepackModuleAttribute(attr));
			}
		}
		context.EnqueueNextGen(new RepackAssemblyAttributes(module.Assembly));
	}
}
record RepackAssemblyAttributes(AssemblyDefinition assembly) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (!context.assembliesChecked.Add(assembly)) return;
		foreach (var attr in assembly.CustomAttributes)
		{
			context.EnqueueNextGen(new RepackAssemblyAttribute(attr));
		}
	}
}
static class DebugRepacking {
	public static FieldInfo instrOffsetField = typeof(InstructionOffset).GetField("instruction", BindingFlags.NonPublic | BindingFlags.Instance);

	static BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

	static ConstructorInfo mdi = typeof(MethodDebugInformation).GetConstructor(flags, [typeof(MethodDefinition)]);
	static ConstructorInfo sq = typeof(SequencePoint).GetConstructor(flags, [typeof(int), typeof(Document)]);
	static ConstructorInfo sdi = typeof(ScopeDebugInformation).GetConstructor(flags, []);
	static ConstructorInfo vdi = typeof(VariableDebugInformation).GetConstructor(flags, [typeof(int), typeof(string)]);
	static ConstructorInfo sms = typeof(StateMachineScope).GetConstructor(flags, [typeof(int), typeof(int)]);
	static FieldInfo instrOffsetData = typeof(InstructionOffset).GetField("offset", flags);

	public static CustomDebugInformation Repack(RepackingContext context, CustomDebugInformation debug) {
		if (debug is BinaryCustomDebugInformation bci) return Repack(context, bci);
		else if (debug is AsyncMethodBodyDebugInformation aci) return Repack(context, aci);
		else if (debug is StateMachineScopeDebugInformation scd) return Repack(context, scd);
		else if (debug is EmbeddedSourceDebugInformation esd) return Repack(context, esd);
		else if (debug is SourceLinkDebugInformation sld) return Repack(context, sld);
		throw new InvalidOperationException("unreachable");
	}

	public static SourceLinkDebugInformation Repack(RepackingContext context, SourceLinkDebugInformation debug) {
		SourceLinkDebugInformation info = new(debug.Content);
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}

	public static EmbeddedSourceDebugInformation Repack(RepackingContext context, EmbeddedSourceDebugInformation debug) {
		EmbeddedSourceDebugInformation info = new(debug.Content, debug.Compress);
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}

	public static StateMachineScopeDebugInformation Repack(RepackingContext context, StateMachineScopeDebugInformation debug) {
		StateMachineScopeDebugInformation info = new();
		foreach (var scope in debug.Scopes) info.Scopes.Add(Repack(context, scope));
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}

	public static StateMachineScope Repack(RepackingContext context, StateMachineScope scope) {
		StateMachineScope state;
		if (!scope.End.IsEndOfMethod) {
			state = (StateMachineScope)sms.Invoke([scope.Start.Offset, scope.End.Offset]);
		}
		else {
			state = (StateMachineScope)sms.Invoke([scope.Start.Offset, 0]);
			instrOffsetData.SetValue(state.End, null);
		}
		context.EnqueueNextGen(new RepackAssignOffsetInstruction(state.Start, (Instruction)instrOffsetField.GetValue(scope.Start)));
		context.EnqueueNextGen(new RepackAssignOffsetInstruction(state.End, (Instruction)instrOffsetField.GetValue(scope.End)));
		return state;
	}

	public static BinaryCustomDebugInformation Repack(RepackingContext context, BinaryCustomDebugInformation debug) {
		BinaryCustomDebugInformation info = new(debug.Identifier, debug.Data);
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}

	public static AsyncMethodBodyDebugInformation Repack(RepackingContext context, AsyncMethodBodyDebugInformation debug) {
		AsyncMethodBodyDebugInformation info = new();
		foreach (var yield in debug.Yields.OrderBy(v => v.Offset))
		{
			var off = new InstructionOffset(yield.Offset);
			context.EnqueueNextGen(new RepackAssignOffsetInstruction(off, (Instruction)instrOffsetField.GetValue(yield)));
			info.Yields.Add(off);
		}
		foreach (var res in debug.Resumes.OrderBy(v => v.Offset))
		{
			var off = new InstructionOffset(res.Offset);
			context.EnqueueNextGen(new RepackAssignOffsetInstruction(off, (Instruction)instrOffsetField.GetValue(res)));
			info.Resumes.Add(off);
		}
		{
			var off = new InstructionOffset(debug.CatchHandler.Offset);
			context.EnqueueNextGen(new RepackAssignOffsetInstruction(off, (Instruction)instrOffsetField.GetValue(debug.CatchHandler)));
			info.CatchHandler = off;
		}
		for (int i = 0; i < debug.ResumeMethods.Count; i++) context.Enqueue(new RepackMethodIndexed(info.ResumeMethods, i, debug.ResumeMethods[i]));
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}

	public static ConstantDebugInformation Repack(RepackingContext context, ConstantDebugInformation debug) {
		ConstantDebugInformation info = new(debug.Name, debug.ConstantType, debug.Value);
		context.Enqueue(new RepackAssignConstantType(info, debug.ConstantType));
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}

	public static VariableDebugInformation Repack(RepackingContext context, VariableDebugInformation debug) {
		VariableDebugInformation info = (VariableDebugInformation)vdi.Invoke([debug.Index, debug.Name]);
		info.Attributes = debug.Attributes;
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		return info;
	}
	
	public static ImportTarget Repack(RepackingContext context, ImportTarget target) {
		ImportTarget imp = new(target.Kind)
		{
			Namespace = target.Namespace
		};
		if (target.Type != null)
			context.Enqueue(new RepackImportType(imp, target.Type));
		return imp;
	}

	public static ImportDebugInformation Repack(RepackingContext context, ImportDebugInformation debug) {
		ImportDebugInformation info = new();
		foreach (var t in debug.Targets) info.Targets.Add(Repack(context, t));
		if (debug.Parent != null) info.Parent = Repack(context, debug.Parent);
		return info;
	}

	public static ScopeDebugInformation Repack(RepackingContext context, ScopeDebugInformation debug) {
		ScopeDebugInformation info = (ScopeDebugInformation)sdi.Invoke([]);
		info.Start = new(debug.Start.Offset);
		context.EnqueueNextGen(new RepackAssignOffsetInstruction(info.Start, (Instruction)instrOffsetField.GetValue(debug.Start)));
		if (!debug.End.IsEndOfMethod)
		{
			info.End = new(debug.End.Offset);
			context.EnqueueNextGen(new RepackAssignOffsetInstruction(info.End, (Instruction)instrOffsetField.GetValue(debug.End)));
		}
		else {
			info.End = new(0);
			instrOffsetData.SetValue(info.End, null);
		}
		foreach (var variab in debug.Variables) info.Variables.Add(Repack(context, variab));
		foreach (var constant in debug.Constants) info.Constants.Add(Repack(context, constant));
		if (info.Import != null)
			info.Import = Repack(context, debug.Import);
		foreach (var scope in debug.Scopes) info.Scopes.Add(Repack(context, scope));
		return info;
	}

	public static SequencePoint Repack(SequencePoint point) {
		SequencePoint newSeq = (SequencePoint)sq.Invoke([point.Offset, point.Document]);
		newSeq.StartColumn = point.StartColumn;
		newSeq.StartLine = point.StartLine;
		newSeq.EndColumn = point.EndColumn;
		newSeq.EndLine = point.EndLine;
		return newSeq;
	}

	public static MethodDebugInformation Repack(RepackingContext context, MethodDefinition movedTo, MethodDebugInformation debug) {
		MethodDebugInformation info = (MethodDebugInformation)mdi.Invoke([movedTo]);
		foreach (var sq in debug.SequencePoints.OrderBy(sp => sp.Offset)) info.SequencePoints.Add(Repack(sq));
		if (debug.StateMachineKickOffMethod != null) context.Enqueue(new RepackAssignKickoffMethod(info, debug.StateMachineKickOffMethod));
		foreach (var cdi in debug.CustomDebugInformations) info.CustomDebugInformations.Add(Repack(context, cdi));
		if (debug.Scope != null)
			info.Scope = Repack(context, debug.Scope);
		return info;
	}
}
record RepackImportType(ImportTarget target, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var ex)) {
			target.Type = ex;
		}
		else {
			context.EnqueueImmediate(new RepackType(type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackMethodIndexed(Collection<MethodDefinition> methods, int idx, MethodDefinition method) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(method, out var ex)) {
			methods.ReplaceSlot(idx, (MethodDefinition)ex);
		}
		else {
			context.EnqueueImmediate(new RepackMethodReference(method));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignConstantType(ConstantDebugInformation debug, TypeReference type) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.types.TryGetValue(type, out var ex)) {
			debug.ConstantType = ex;
		}
		else {
			context.EnqueueImmediate(new RepackType(type));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackAssignOffsetInstruction(InstructionOffset offset, Instruction instruction) : Repack
{

	public override void Run(RepackingContext context)
	{
		if (instruction == null) return;
		if (context.instructions.TryGetValue(instruction, out var ex)) {
			DebugRepacking.instrOffsetField.SetValue(offset, ex);
		}
		else {
			context.EnqueueNextGen(this);
		}
	}
}
record RepackAssignKickoffMethod(MethodDebugInformation mdi, MethodDefinition kickoff) : Repack
{
	public override void Run(RepackingContext context)
	{
		if (context.methods.TryGetValue(kickoff, out var ex)) {
			mdi.StateMachineKickOffMethod = (MethodDefinition)ex;
		}
		else {
			context.EnqueueImmediate(new RepackMethodReference(kickoff));
			context.EnqueueImmediate(this);
		}
	}
}
record RepackMethodDebugInformation(MethodDefinition on, MethodDebugInformation debug) : Repack
{
	public override void Run(RepackingContext context)
	{
		MethodDebugInformation cloned = DebugRepacking.Repack(context, on, debug);
		on.DebugInformation = cloned;
	}
}
record RepackMethodCustomDebugInformation(MethodDefinition on, CustomDebugInformation debug) : Repack
{
	public override void Run(RepackingContext context)
	{
		CustomDebugInformation cloned = DebugRepacking.Repack(context, debug);
		on.CustomDebugInformations.Add(cloned);
	}
}

public class Repacker {
	RepackingContext context;
	public Repacker(DefinitionMap defMap, ModuleDefinition output, RepackingOptions options) {
		context = new(defMap, output, new(), options);
	}

	public void Target(TypeDefinition type) {
		context.Enqueue(new RepackTargetType(type));
	}

	public void Target(MethodDefinition method) {
		context.Enqueue(new RepackTargetMethod(method));
	}

	public void Target(EventDefinition ev) {
		context.Enqueue(new RepackTargetEvent(ev));
	}

	public void Target(PropertyDefinition property) {
		context.Enqueue(new RepackTargetProperty(property));
	}
	
	public void Target(FieldDefinition field) {
		context.Enqueue(new RepackTargetField(field));
	}

	public void Target(ModuleDefinition module) {
		foreach (var t in module.GetTypes()) Target(t);
	}

	public void Target(AssemblyDefinition asm) {
		Target(asm.MainModule);
	}

	void RunQueue() {
		while (context.repacks.TotalCount > 0)
		{
			context.Commit();
			while (context.TryDequeue(out Repack? repack))
			{
				// try {
				// 	Console.WriteLine($"running {repack}");
				// }
				// catch (Exception ex) {}
				repack!.Run(context);
				context.CommitImmediate();
			}
		}
	}

	public void Run() {
		RunQueue();
		if (context.options.repackModuleInitializers) {
			context.Enqueue(new RepackFinalizeModuleInitializer(Common.GetOrCreateStatic(context.output.GetType("<Module>"))));
		}
		RunQueue();
	}
}