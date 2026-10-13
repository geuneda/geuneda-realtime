using Microsoft.CodeAnalysis;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// <c>[RpcService]</c> 계약에서 클라이언트 프록시(Unity, <c>Geuneda.Realtime.IRpcCaller</c> 참조 시)와
	/// 서버 라우트 표(<c>Geuneda.Realtime.Server.RpcRoute</c> 참조 시)를 만드는 증분 소스 생성기입니다.
	/// </summary>
	/// <remarks>
	/// Unity 6000.3 의 Roslyn 4.3 에서 돌도록 Microsoft.CodeAnalysis 4.3 API 만 씁니다(ForAttributeWithMetadataName 대신 CreateSyntaxProvider).
	/// 계약 모델(비싼 부분)은 선언별로 캐시하고, 출력 단계는 대상 런타임과 이미 있는 형식 이름을 보려고 컴파일과 합칩니다.
	/// </remarks>
	[Generator(LanguageNames.CSharp)]
	public sealed class RpcContractGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var services = context.SyntaxProvider
				.CreateSyntaxProvider(
					static (node, _) => ServiceModelBuilder.IsCandidate(node),
					static (syntaxContext, cancellationToken) => ServiceModelBuilder.Build(syntaxContext, cancellationToken))
				.Where(static model => model != null);

			context.RegisterSourceOutput(services.Collect().Combine(context.CompilationProvider),
				static (sourceContext, input) => Emitter.Emit(sourceContext, input.Left, input.Right));
		}
	}
}
