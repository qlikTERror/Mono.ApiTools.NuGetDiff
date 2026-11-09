using System.Runtime.CompilerServices;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit.Abstractions;

namespace Mono.ApiTools.Tests;

public class GetMissingSymbolsTests : BaseUnitTest
{
	public GetMissingSymbolsTests(ITestOutputHelper output)
	{
		Output = output;
	}

	public ITestOutputHelper Output { get; }

	[Fact]
	public void ClassLibraryV1IsCompatibleWithClassLibraryV1()
	{
		var result = ApiCompat.GetMissingSymbols(
			new FileInputAssembly(LibraryBuiltAgainstV1)
			{
				SearchPaths = [Path.GetDirectoryName(LibraryBuiltAgainstV1)!]
			},
			new FileInputAssembly(ClassLibraryV1)
			{
				SearchPaths = [Path.GetDirectoryName(ClassLibraryV1)!]
			});

		AssertMatchesExpectedResults(result);
	}

	[Fact]
	public void ClassLibraryV2IsPartiallyCompatibleWithClassLibraryV1()
	{
		var result = ApiCompat.GetMissingSymbols(
			new FileInputAssembly(LibraryBuiltAgainstV1)
			{
				SearchPaths = [Path.GetDirectoryName(LibraryBuiltAgainstV1)!]
			},
			new FileInputAssembly(ClassLibraryV2)
			{
				SearchPaths = [Path.GetDirectoryName(ClassLibraryV2)!]
			});

		AssertMatchesExpectedResults(result);
	}

	[Fact]
	public void SvgSkiaIsCompatibleWithSkiaSharpV2()
	{
		var v2 = Path.GetDirectoryName(SkiaSharpV2Downloader)!;

		var result = ApiCompat.GetMissingSymbols(
			new FileInputAssembly(Path.Combine(v2, "Svg.Skia.dll"))
			{
				SearchPaths = [v2]
			},
			new FileInputAssembly(Path.Combine(v2, "SkiaSharp.dll"))
			{
				SearchPaths = [v2]
			});

		AssertMatchesExpectedResults(result);
	}

	[Fact]
	public void SvgSkiaIsPartiallyCompatibleWithSkiaSharpV3()
	{
		var v2 = Path.GetDirectoryName(SkiaSharpV2Downloader)!;
		var v3 = Path.GetDirectoryName(SkiaSharpV3Downloader)!;

		var result = ApiCompat.GetMissingSymbols(
			new FileInputAssembly(Path.Combine(v2, "Svg.Skia.dll"))
			{
				SearchPaths = [v2]
			},
			new FileInputAssembly(Path.Combine(v3, "SkiaSharp.dll"))
			{
				SearchPaths = [v3]
			});

		AssertMatchesExpectedResults(result);
	}

	[Fact]
	public void MethodReturningArrayOfDependencyTypeIsCompatible()
	{
		byte[] dependencyBytes;
		using (var dependency = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("Dependency", new Version(2, 0)), "Dependency", ModuleKind.Dll))
		{
			var module = dependency.MainModule;
			var point = new TypeDefinition("Example", "Point", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
			var api = new TypeDefinition("Example", "Api", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
			module.Types.Add(point);
			module.Types.Add(api);

			var getPoints = new MethodDefinition("GetPoints", MethodAttributes.Public | MethodAttributes.Static, new ArrayType(point));
			getPoints.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
			getPoints.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
			api.Methods.Add(getPoints);

			using var output = new MemoryStream();
			dependency.Write(output);
			dependencyBytes = output.ToArray();
		}

		byte[] consumerBytes;
		using (var consumer = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("Consumer", new Version(1, 0)), "Consumer", ModuleKind.Dll))
		{
			var module = consumer.MainModule;
			var dependencyReference = new AssemblyNameReference("Dependency", new Version(1, 0));
			module.AssemblyReferences.Add(dependencyReference);
			var point = new TypeReference("Example", "Point", module, dependencyReference);
			var api = new TypeReference("Example", "Api", module, dependencyReference);
			var getPoints = new MethodReference("GetPoints", new ArrayType(point), api) { HasThis = false };

			var consumerType = new TypeDefinition("Example", "Consumer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
			module.Types.Add(consumerType);
			var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
			method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, getPoints));
			method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
			method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
			consumerType.Methods.Add(method);

			using var output = new MemoryStream();
			consumer.Write(output);
			consumerBytes = output.ToArray();
		}

		var result = ApiCompat.GetMissingSymbols(
			new StreamInputAssembly(() => new MemoryStream(consumerBytes)),
			new StreamInputAssembly(() => new MemoryStream(dependencyBytes)));

		Assert.Empty(result.Types);
		Assert.Empty(result.Members);
	}

	private static void WriteExpectedResults(MissingSymbols result, [CallerMemberName] string? methodName = null)
	{
		var actual = GetActualContents(result);

		File.WriteAllText($"../../../ExpectedResults/{methodName}.txt", actual);
	}

	private void AssertMatchesExpectedResults(MissingSymbols result, [CallerMemberName] string? methodName = null)
	{
		var actual = GetActualContents(result);

		Output.WriteLine(actual);

		var expected = File.ReadAllText($"ExpectedResults/{methodName}.txt");

		Assert.Equal(expected, actual);
	}

	private static string GetActualContents(MissingSymbols result)
	{
		var sb = new StringBuilder();

		sb.AppendLine("Mising Types:");
		if (result.Types.Count == 0)
		{
			sb.AppendLine("/*None*/");
		}
		else
		{
			var types = result.Types.ToList();
			types.Sort();
			foreach (var type in types)
			{
				sb.AppendLine(type);
			}
		}

		sb.AppendLine("");
		sb.AppendLine("Missing Members:");
		if (result.Members.Count == 0)
		{
			sb.AppendLine("/*None*/");
		}
		else
		{
			var members = result.Members.ToList();
			members.Sort();
			foreach (var member in members)
			{
				sb.AppendLine(member);
			}
		}

		var actual = sb.ToString();

		return actual;
	}
}
