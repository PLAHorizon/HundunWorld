using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Prints the real member types of Flax's editor scripting API. See FlaxApiDump.csproj for why the
// anchor type matters: LoadFrom-based reflection does not resolve this assembly.

internal static class Program
{
    private static readonly BindingFlags Flags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static int Main(string[] args)
    {
        Assembly assembly = typeof(FlaxEngine.Actor).Assembly;

        var types = new List<string>
        {
            "FlaxEditor.Surface.VisjectSurface",
            "FlaxEditor.Surface.NodeArchetype",
            "FlaxEditor.Surface.GroupArchetype",
            "FlaxEditor.Surface.SurfaceNode",
            "FlaxEditor.Surface.ISurfaceContext",
            "FlaxEditor.Surface.IVisjectSurfaceOwner",
            "FlaxEditor.Surface.NodeElementArchetype",
            "FlaxEditor.Surface.NodeElementType",
            "FlaxEditor.Surface.NodeFlags",
            "FlaxEditor.Scripting.ScriptType",
            "FlaxEditor.Windows.Assets.AssetEditorWindow",
        };

        string methodFilter = null;
        var explicitTypes = new List<string>();

        foreach (var arg in args)
        {
            if (arg.StartsWith("/filter:", StringComparison.OrdinalIgnoreCase))
                methodFilter = arg.Substring(8);
            else
                explicitTypes.Add(arg);
        }

        if (explicitTypes.Count > 0)
            types = explicitTypes;

        foreach (var name in types)
        {
            var type = assembly.GetType(name);
            if (type == null)
            {
                Console.WriteLine($"### {name}: NOT FOUND");
                continue;
            }

            Console.WriteLine($"### {name}  (base: {type.BaseType?.Name}, abstract: {type.IsAbstract}, interface: {type.IsInterface})");

            foreach (var ctor in type.GetConstructors(Flags))
                Console.WriteLine($"  ctor ({Join(ctor.GetParameters().Select(p => $"{Short(p.ParameterType)} {p.Name}"))})");

            foreach (var field in type.GetFields(Flags).OrderBy(f => f.Name))
                Console.WriteLine($"  field {Accessibility(field.Attributes)} {Short(field.FieldType)} {field.Name}{(field.IsLiteral ? " = " + field.GetRawConstantValue() : string.Empty)}");

            foreach (var property in type.GetProperties(Flags).OrderBy(p => p.Name))
            {
                var get = property.GetGetMethod(true);
                var set = property.GetSetMethod(true);
                Console.WriteLine($"  prop  {Short(property.PropertyType)} {property.Name} {{{(get != null ? " get;" : " get: none;")}{(set != null ? " set;" : string.Empty)}}}");
            }

            if (methodFilter != null)
            {
                foreach (var method in type.GetMethods(Flags)
                             .Where(m => m.Name.IndexOf(methodFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderBy(m => m.Name))
                {
                    if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_") || method.Name == "ToString")
                        continue;

                    Console.WriteLine($"  meth  {MethodAccessibility(method.Attributes)}{(method.IsAbstract ? " abstract" : string.Empty)}{(method.IsVirtual ? " virtual" : string.Empty)} " +
                                      $"{Short(method.ReturnType)} {method.Name}({Join(method.GetParameters().Select(p => $"{Short(p.ParameterType)} {p.Name}"))})");
                }
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static string Accessibility(FieldAttributes attributes) =>
        (attributes & FieldAttributes.FieldAccessMask).ToString();

    private static string MethodAccessibility(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask).ToString();

    private static string Short(Type type)
    {
        if (type == null)
            return "?";

        if (type.IsGenericType)
        {
            var name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return $"{name}<{string.Join(",", type.GetGenericArguments().Select(Short))}>";
        }

        if (type.IsArray)
            return $"{Short(type.GetElementType())}[]";

        if (type.IsByRef)
            return $"ref {Short(type.GetElementType())}";

        return type.Name;
    }

    private static string Join(IEnumerable<string> parts) => string.Join(", ", parts);
}
