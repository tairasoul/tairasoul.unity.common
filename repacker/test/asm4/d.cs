using System;

public interface GenIntfTest<C> {
	public void Gen2(C b);
}

public class E<F> where F : IntfA {

}

public class F<A, H> where A : AbstractA<H> {

}

public class D<F> : AbstractA<F>, IntfA, GenericInterfaceTest<F>, GenIntfTest<F>, ExplicitOverrideTest, ExplicitGenericOverrideTest<F>
	where F : AbstractA<F>
 {
	public class NestedWithGeneric<T> {
		public static T Nested(float float1, float float2) {
			return default;
		}
	}

	public class DNested {
		public static float Method2(float float1, float float2) {
			return float1 * float2 + float1;
		}
	}

	F Field1;

	public static float Property3 => B.Field2;
	public static float Property4 => C.Field3;

	public F InterfaceProp { get; set; }
	public override F AbstractProp { get; set; }
	public F prop { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

	public static float Method1() {
		return B.Property1 + B.Field2 + C.Property2 + C.Field3 + DNested.Method2(Property3, C.Field3);
	}

	public void ClassGenRet(F[] p1) {
		return;
	}

	public void IntfMethod()
	{
		NestedWithGeneric<F>.Nested(1, 2);
		Console.WriteLine("itnfmethod 1");
	}

	public override F AbstractMethod1(F c)
	{
		Console.WriteLine("abstract m1");
		return default;
	}

	public void Generic2<H>(H b, F c) where H : unmanaged
	{
		Console.WriteLine("gen2");
	}

	public F Generic(F c)
	{
		Console.WriteLine("gen1");
		return default;
	}

	// public F SecondGenTest(F c)
	// {
	// 	Console.WriteLine("gen3");
	// 	return default;
	// }

	F GenericInterfaceTest<F>.SecondGenTest(F c)
	{
		throw new NotImplementedException();
	}

	void GenIntfTest<F>.Gen2(F b)
	{
		throw new NotImplementedException();
	}

	public override F AbstractMethod2(F c)
	{
		throw new NotImplementedException();
	}

	void ExplicitOverrideTest.ExplicitOverride(int c)
	{
		throw new NotImplementedException();
	}

	void ExplicitGenericOverrideTest<F>.Override(bool c)
	{
		throw new NotImplementedException();
	}
}