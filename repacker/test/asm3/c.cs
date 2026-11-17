public class C {
	public static float Field3 = 0.5F;
	public static float Property2 => A.Field1;
}

public abstract class AbstractA<T> {
	public abstract T AbstractMethod1(T c);
	public abstract T AbstractProp { get; set; }
	public abstract T AbstractMethod2(T c);
}

public interface GenericInterfaceTest<T> where T : AbstractA<T> {
	public T Generic(T c);
	public T InterfaceProp { get; set; }
	public T SecondGenTest(T c);
}

public interface ExplicitOverrideTest {
	public void ExplicitOverride(int c);
}

public interface ExplicitGenericOverrideTest<T> {
	public T prop { get; set; }
	public void Override(bool c);
}