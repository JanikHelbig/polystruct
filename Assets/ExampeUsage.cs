using UnityEngine;

public class ExampleUsage : MonoBehaviour
{
    private void Start()
    {
        Example a = new ExampleA();
        a.Print(); // Prints "ExampleA!"

        Example b = new ExampleB();
        b.Print(); // Prints "ExampleB!"

        Example c = new ExampleC();
        c.Print(); // Prints "ExampleA!"
    }
}
