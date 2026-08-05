using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Loadstar.Core.Tests;

/// <summary>
/// Reads P/Invoke declarations straight out of compiled IL.
///
/// <para>Metadata rather than reflection, for two reasons that matter to what this is for. It sees
/// <em>declarations</em>, so a forbidden import is caught even if nothing ever calls it — which is
/// the case that would otherwise slip through, since dead code is exactly where something like this
/// gets parked. And it never loads the assembly, so a test running on plain net8.0 can inspect the
/// Windows-targeted capture assembly, and a hostile or broken binary cannot execute anything by
/// being examined.</para>
/// </summary>
internal static class AssemblyScanner
{
    /// <summary>
    /// Locates every Loadstar assembly produced by a build, by walking up to the directory holding
    /// the solution and then looking under <c>src</c>.
    ///
    /// <para>Discovery is by path rather than by project reference on purpose: referencing the
    /// assemblies would mean this test only sees what it was told about, and the whole point is to
    /// catch a <em>new</em> project that someone forgot to wire in.</para>
    /// </summary>
    public static IReadOnlyList<string> FindLoadstarAssemblies()
    {
        var root = FindRepositoryRoot();
        var src = Path.Combine(root, "src");

        if (!Directory.Exists(src))
        {
            return [];
        }

        // "Loadstar*.dll", not "Loadstar.*.dll". The shell projects set AssemblyName to `Loadstar`
        // and `loadstar`, so the dotted pattern silently skipped the two assemblies most likely to
        // grow native calls — the guard passed by scanning everything except the application.
        return Directory
            .EnumerateFiles(src, "Loadstar*.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AssemblyScanner).Assembly.Location) ?? Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Loadstar.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find Loadstar.sln above the test assembly, so the posture scan has no idea " +
            "what to scan. This test must not be allowed to pass in that state.");
    }

    /// <summary>Every P/Invoke declared in the assembly at <paramref name="assemblyPath"/>.</summary>
    public static IReadOnlyList<NativeImport> ReadNativeImports(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            return [];
        }

        var reader = peReader.GetMetadataReader();
        var imports = new List<NativeImport>();

        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);

            if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
            {
                continue;
            }

            var import = method.GetImport();

            if (import.Module.IsNil)
            {
                continue;
            }

            var module = reader.GetString(reader.GetModuleReference(import.Module).Name);

            // A P/Invoke may rename the managed method, so the entry point is authoritative. It is
            // only absent when the two are the same.
            var entryPoint = import.Name.IsNil
                ? reader.GetString(method.Name)
                : reader.GetString(import.Name);

            imports.Add(new NativeImport(
                Path.GetFileName(assemblyPath),
                DeclaringTypeName(reader, handle),
                module,
                entryPoint));
        }

        return imports;
    }

    private static string DeclaringTypeName(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var typeHandle = reader.GetMethodDefinition(handle).GetDeclaringType();

        if (typeHandle.IsNil)
        {
            return "<unknown>";
        }

        var type = reader.GetTypeDefinition(typeHandle);
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);

        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
}

internal sealed record NativeImport(string Assembly, string DeclaringType, string Module, string EntryPoint)
{
    /// <summary>Module name without the extension, lowercased, for comparison.</summary>
    public string NormalizedModule =>
        Path.GetFileNameWithoutExtension(Module).ToLowerInvariant();

    /// <summary>
    /// The entry point with any ANSI/Unicode suffix removed, so a denylist entry of
    /// <c>PostMessage</c> also catches <c>PostMessageW</c>.
    /// </summary>
    public string NormalizedEntryPoint
    {
        get
        {
            var name = EntryPoint;

            if (name.Length > 1 && (name[^1] == 'A' || name[^1] == 'W') && char.IsLower(name[^2]))
            {
                name = name[..^1];
            }

            return name.ToLowerInvariant();
        }
    }

    public override string ToString() => $"{Assembly}: {DeclaringType} -> {Module}!{EntryPoint}";
}
