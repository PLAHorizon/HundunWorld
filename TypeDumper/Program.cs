using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        string path = @"C:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\HundunWorld\Plugins\UnrealSharp\Managed\Binaries\Managed\net10.0\UnrealSharp.dll";
        var asm = Assembly.LoadFrom(path);
        foreach (var type in asm.GetTypes())
        {
            if (type.Namespace != null && type.Namespace.StartsWith("UnrealSharp.ModularGameplay"))
            {
                Console.WriteLine(type.FullName);
            }
        }
    }
}
