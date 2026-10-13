extern alias ServerLib;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Geuneda.Realtime.Generator.Tests
{
	/// <summary>
	/// 계약 소스를 컴파일하고 생성기를 돌립니다. 클라이언트 모드는 Unity 런타임 형식의 최소 대역을, 서버 모드는 실제 서버 라이브러리를 참조합니다.
	/// </summary>
	internal static class GeneratorHarness
	{
		/// <summary>Unity 쪽 형식의 대역입니다. 이름과 시그니처는 실제(Geuneda.Realtime, UniTask, Geuneda.Core)와 같습니다.</summary>
		public const string ClientRuntimeStub = @"
namespace Cysharp.Threading.Tasks { public readonly struct UniTask { } public readonly struct UniTask<T> { } }
namespace Geuneda.Core { public readonly struct Result { } public readonly struct Result<T> { } }
namespace Geuneda.Realtime
{
	public readonly struct RpcMethod { public static RpcMethod Of(string name) => default; }
	public interface IRpcCaller
	{
		Cysharp.Threading.Tasks.UniTask<Geuneda.Core.Result<TResponse>> CallAsync<TRequest, TResponse>(RpcMethod method, TRequest request, System.Threading.CancellationToken cancellationToken = default);
		Cysharp.Threading.Tasks.UniTask<Geuneda.Core.Result> CallAsync<TRequest>(RpcMethod method, TRequest request, System.Threading.CancellationToken cancellationToken = default);
	}
	[System.AttributeUsage(System.AttributeTargets.Interface)] public sealed class RpcServiceAttribute : System.Attribute { }
	[System.AttributeUsage(System.AttributeTargets.Method)] public sealed class RpcAllowAnonymousAttribute : System.Attribute { }
}";

		/// <summary>클라이언트 런타임은 있지만 UniTask·Result 가 보이지 않는 어셈블리의 대역입니다.</summary>
		public const string ClientRuntimeWithoutDependenciesStub = @"
namespace Geuneda.Realtime
{
	public interface IRpcCaller { }
	[System.AttributeUsage(System.AttributeTargets.Interface)] public sealed class RpcServiceAttribute : System.Attribute { }
	[System.AttributeUsage(System.AttributeTargets.Method)] public sealed class RpcAllowAnonymousAttribute : System.Attribute { }
}";

		public const string AttributesOnlyStub = @"
namespace Geuneda.Realtime
{
	[System.AttributeUsage(System.AttributeTargets.Interface)] public sealed class RpcServiceAttribute : System.Attribute { }
	[System.AttributeUsage(System.AttributeTargets.Method)] public sealed class RpcAllowAnonymousAttribute : System.Attribute { }
}";

		// .NET 공유 프레임워크 어셈블리만 참조한다(테스트 프로세스의 TPA 에는 서버 라이브러리도 있어 서버 모드로 잘못 잡힘)
		private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new Lazy<ImmutableArray<MetadataReference>>(() =>
		{
			var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
			return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
				.Split(Path.PathSeparator)
				.Where(path => !string.IsNullOrEmpty(path) && Path.GetDirectoryName(path) == frameworkDirectory)
				.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
				.ToImmutableArray();
		});

		public static GeneratorRun Run(string assemblyName, bool serverRuntime, params string[] sources)
		{
			var references = PlatformReferences.Value;
			if (serverRuntime)
			{
				references = references.Add(MetadataReference.CreateFromFile(typeof(ServerLib::Geuneda.Realtime.Server.RpcRoute).Assembly.Location));
			}

			var syntaxTrees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"Source{index}.cs")).ToList();
			var compilation = CSharpCompilation.Create(assemblyName, syntaxTrees, references,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

			GeneratorDriver driver = CSharpGeneratorDriver.Create(new RpcContractGenerator());
			driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

			var generated = driver.GetRunResult().GeneratedTrees.ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString());
			var compileErrors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
			return new GeneratorRun(generatorDiagnostics, generated, compileErrors);
		}
	}
}
