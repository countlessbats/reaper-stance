using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Patcher <Assembly-CSharp.dll> <LoomReapingKnivesModal.dll>");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
string sidecarPath = Path.GetFullPath(args[1]);
string patchedPath = assemblyPath + ".patched";

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine("Missing game assembly: " + assemblyPath);
    return 2;
}

if (!File.Exists(sidecarPath))
{
    Console.Error.WriteLine("Missing sidecar assembly: " + sidecarPath);
    return 2;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(sidecarPath)!);

var parameters = new ReaderParameters
{
    ReadWrite = false,
    InMemory = true,
    AssemblyResolver = resolver
};

using var module = ModuleDefinition.ReadModule(assemblyPath, parameters);

if (module.AssemblyReferences.Any(r => r.Name == "LoomReapingKnivesModal"))
{
    Console.WriteLine("Already patched: sidecar reference exists.");
    return 0;
}

var gameState = module.Types.FirstOrDefault(t => t.Name == "GameState");
var update = gameState?.Methods.FirstOrDefault(m => m.Name == "Update" && !m.IsStatic && !m.HasParameters);
if (gameState == null || update == null || !update.HasBody)
{
    Console.Error.WriteLine("Could not find GameState.Update.");
    return 3;
}

var sidecar = AssemblyDefinition.ReadAssembly(sidecarPath);
var bootstrap = sidecar.MainModule.Types.First(t => t.FullName == "LoomReapingKnivesModal.Bootstrap");
var tick = bootstrap.Methods.First(m => m.Name == "Tick" && m.IsStatic && !m.HasParameters);
var importedTick = module.ImportReference(tick);

var il = update.Body.GetILProcessor();
var first = update.Body.Instructions.First();
il.InsertBefore(first, il.Create(OpCodes.Call, importedTick));

module.AssemblyReferences.Add(new AssemblyNameReference("LoomReapingKnivesModal", sidecar.Name.Version));
if (File.Exists(patchedPath))
{
    File.Delete(patchedPath);
}

module.Write(patchedPath);
File.Copy(patchedPath, assemblyPath, overwrite: true);
File.Delete(patchedPath);

Console.WriteLine("Patched GameState.Update -> LoomReapingKnivesModal.Bootstrap.Tick().");
return 0;
