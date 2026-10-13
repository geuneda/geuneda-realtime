using Microsoft.CodeAnalysis;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 컴파일이 참조하는 런타임입니다. 클라이언트(<c>IRpcCaller</c>)가 있으면 프록시를, 서버(<c>RpcRoute</c>)가 있으면 라우트 표를 만듭니다.
	/// 프록시는 UniTask 와 <c>Geuneda.Core.Result</c> 를 쓰므로 둘 다 보여야 합니다(<see cref="ClientDependencies"/>).
	/// </summary>
	internal sealed record GenerationTargets(bool Client, bool ClientDependencies, bool Server, string AssemblyName)
	{
		private const string ClientRuntimeType = "Geuneda.Realtime.IRpcCaller";
		private const string ServerRuntimeType = "Geuneda.Realtime.Server.RpcRoute";
		private const string UniTaskType = "Cysharp.Threading.Tasks.UniTask";
		private const string ResultType = "Geuneda.Core.Result";

		public static GenerationTargets From(Compilation compilation)
		{
			return new GenerationTargets(
				compilation.GetTypeByMetadataName(ClientRuntimeType) != null,
				compilation.GetTypeByMetadataName(UniTaskType) != null && compilation.GetTypeByMetadataName(ResultType) != null,
				compilation.GetTypeByMetadataName(ServerRuntimeType) != null,
				compilation.AssemblyName);
		}
	}
}
