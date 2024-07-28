using System;
using Jnk.PolyStruct;

namespace Jnk.PolyStructGeneration.Sample;

[PolyStruct]
public interface ITest
{
    bool IsComplete { get; }
    void Execute();
    TimeSpan GetDuration(bool isPrecise, ref TimeSpan previous);
}

public struct TestBase
{
    private int _test;

    public bool IsComplete => _test++ > 0;
    public readonly void Execute() => throw new Exception();
    public readonly TimeSpan GetDuration(bool isPrecise, ref TimeSpan previous) => throw new NotImplementedException();
}

public partial struct TestA : ITest
{
    [GenerateInterfaceDelegates]
    private TestBase _base;

    private int _value;

    // public bool IsComplete => false;

    public void Execute()
    {
        _value++;
    }

    // public TimeSpan GetDuration(bool isPrecise, ref TimeSpan previous)
    // {
    //     throw new NotImplementedException();
    // }
}

public struct TestB : ITest
{
    private float _value;

    public bool IsComplete => true;

    public void Execute()
    {
        _value++;
    }

    public TimeSpan GetDuration(bool isPrecise, ref TimeSpan previous)
    {
        throw new NotImplementedException();
    }
}

public struct TestC : ITest
{
    private long _value;

    public bool IsComplete { get; }

    public void Execute()
    {
        throw new NotImplementedException();
    }

    public TimeSpan GetDuration(bool isPrecise, ref TimeSpan previous)
    {
        throw new NotImplementedException();
    }
}

public class TestExample
{
    public void Execute()
    {
        Test sample = new TestB();
        sample.Execute();
        TimeSpan previous = default;
        var time = sample.GetDuration(false, ref previous);

        var testA = new TestA();
        testA.Execute();
    }
}

// [StructLayout(LayoutKind.Explicit)]
// public struct Sample : ISample
// {
//     private enum Type
//     {
//         Uninitialized = 0,
//         Empty = 1,
//         SampleA,
//         SampleB
//     }
//
//     [FieldOffset(0)] private Type _type;
//
//     [FieldOffset(sizeof(Type))] private SampleA _sampleA;
//
//     [FieldOffset(sizeof(Type))] private SampleB _sampleB;
//
//     public static readonly Sample Empty = new() { _type = Type.Empty };
//
//     public readonly ISample UnwrappedValue => _type switch
//     {
//         Type.Uninitialized => throw new InvalidOperationException("Struct has not been initialized."),
//         Type.Empty => null!,
//         Type.SampleA => _sampleA,
//         Type.SampleB => _sampleB,
//         _ => throw new ArgumentOutOfRangeException()
//     };
//
//     public Sample(SampleA sample)
//     {
//         _type = Type.SampleA;
//         _sampleA = sample;
//     }
//
//     public Sample(SampleB sample)
//     {
//         _type = Type.SampleB;
//         _sampleB = sample;
//     }
//
//     public bool IsComplete
//     {
//         get
//         {
//             switch (_type)
//             {
//                 case Type.Uninitialized:
//                     throw new InvalidOperationException("Struct has not been initialized.");
//                 case Type.Empty:
//                     return default;
//                 case Type.SampleA:
//                     return _sampleA.IsComplete;
//                 case Type.SampleB:
//                     return _sampleB.IsComplete;
//                 default:
//                     throw new ArgumentOutOfRangeException();
//             }
//         }
//     }
//
//     public void Execute()
//     {
//         switch (_type)
//         {
//             case Type.Uninitialized:
//                 throw new InvalidOperationException("Struct has not been initialized.");
//             case Type.Empty:
//                 break;
//             case Type.SampleA:
//                 _sampleA.Execute();
//                 break;
//             case Type.SampleB:
//                 _sampleB.Execute();
//                 break;
//             default:
//                 throw new ArgumentOutOfRangeException();
//         }
//     }
//
//     public static implicit operator Sample(SampleA sampleA) => new(sampleA);
//
//     public static explicit operator SampleA(Sample sample) => sample._type == Type.SampleA ? sample._sampleA : throw new InvalidCastException();
// }
