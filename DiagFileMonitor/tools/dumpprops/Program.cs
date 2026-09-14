using System.Reflection;
using System.Text.Json;

// Dumps public instance property names per type from an assembly, without executing it.
var target = args[0];
var searchDirs = args.Skip(1).ToArray();

var assemblies = new List<string>();
foreach (var dir in searchDirs)
    assemblies.AddRange(Directory.GetFiles(dir, "*.dll"));
assemblies.Add(target);

var resolver = new PathAssemblyResolver(assemblies.Distinct());
using var mlc = new MetadataLoadContext(resolver);
var asm = mlc.LoadFromAssemblyPath(target);

var result = new Dictionary<string, List<string>>();
foreach (var type in asm.GetTypes())
{
    if (!type.IsPublic && !type.IsNestedPublic) continue;
    var props = new List<string>();
    var t = type;
    while (t != null && t.FullName != "System.Object")
    {
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (!props.Contains(p.Name)) props.Add(p.Name);
        try { t = t.BaseType; } catch { break; }
    }
    result[type.FullName] = props;
}

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
