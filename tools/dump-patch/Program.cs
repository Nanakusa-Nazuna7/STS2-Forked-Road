using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main(string[] args)
    {
        string dllPath = args[0];
        var resolver = new PathAssemblyResolver(
            Directory.GetFiles(Environment.GetEnvironmentVariable("STS2_GAME_DIR") ?? @"D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64", "*.dll")
                .Concat(new[] { dllPath }));
        using var ctx = new MetadataLoadContext(resolver);
        var asm = ctx.LoadFromAssemblyPath(dllPath);
        foreach (var t in asm.GetTypes())
        {
            var attrs = t.GetCustomAttributesData()
                .Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToList();
            if (attrs.Count == 0) continue;
            foreach (var attr in attrs)
            {
                Type? targetType = null;
                string? methodName = null;
                foreach (var c in attr.ConstructorArguments)
                {
                    if (c.Value is Type tv && targetType == null) targetType = tv;
                    else if (c.Value is string s) methodName = s;
                }
                foreach (var n in attr.NamedArguments)
                {
                    if (n.MemberName == "type" && n.TypedValue.Value is Type ntv) targetType = ntv;
                    if (n.MemberName == "methodName" && n.TypedValue.Value is string ns) methodName = ns;
                }
                if (targetType == null || methodName == null) continue;
                var pm = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Prefix" || m.Name == "Postfix" || m.Name == "Finalizer");
                if (pm == null) continue;
                string ps = string.Join(", ", pm.GetParameters().Select(p =>
                    (p.ParameterType.IsByRef ? "ref " : "") + p.ParameterType.Name + " " + p.Name));
                Console.WriteLine($"{t.Name} -> {targetType.Name}.{methodName}: {ps}");
            }
        }
    }
}
