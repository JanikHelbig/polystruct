using Jnk.PolyStruct;
using UnityEngine;

[PolyStruct]
public interface IExample
{
    public void Print();
}

public struct ExampleA : IExample
{
    public void Print()
    {
        Debug.Log("ExampleA!");
    }
}

public struct ExampleB : IExample
{
    public void Print()
    {
        Debug.Log("ExampleB!");
    }
}

public partial struct ExampleC : IExample
{
    [GenerateInterfaceDelegates]
    private ExampleA _base;
}