using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 서버 라우트 표(<c>GeneratedRpcRoutes.All</c>)를 만듭니다. 항목마다 타입이 정해진 대리자라 호출마다 리플렉션을 쓰지 않습니다.
	/// 네임스페이스는 컴파일의 어셈블리 이름입니다.
	/// </summary>
	internal static class RouteEmitter
	{
		public const string ClassName = "GeneratedRpcRoutes";

		private const string RpcRoute = "global::Geuneda.Realtime.Server.RpcRoute";
		private const string RpcEmpty = "global::Geuneda.Realtime.Server.RpcEmpty";

		public static string Emit(IReadOnlyList<ServiceModel> services, string assemblyName)
		{
			var writer = new CodeWriter().Header();

			writer.Open("namespace " + NamespaceOf(assemblyName));
			writer.Line("/// <summary>이 어셈블리의 [RpcService] 계약 라우트입니다(생성 코드). AddRealtimeRpc 의 options.AddRoutes 로 등록합니다.</summary>");
			writer.Open($"public static class {ClassName}");
			writer.Line($"public static global::System.Collections.Generic.IReadOnlyList<{RpcRoute}> All {{ get; }} = new {RpcRoute}[]");
			writer.Line("{");

			foreach (var service in services)
			{
				foreach (var method in service.Methods)
				{
					writer.Line("\t" + Route(service, method) + ",");
				}
			}

			writer.Line("};");
			writer.Close();
			writer.Close();
			return writer.ToString();
		}

		private static string Route(ServiceModel service, MethodModel method)
		{
			var requestType = method.RequestType ?? RpcEmpty;
			var typeArguments = method.ResponseType == null
				? $"{service.FullyQualifiedName}, {requestType}"
				: $"{service.FullyQualifiedName}, {requestType}, {method.ResponseType}";

			var arguments = new List<string>();
			if (method.RequestType != null)
			{
				arguments.Add("request");
			}

			if (method.HasCancellationToken)
			{
				arguments.Add("cancellationToken");
			}

			var allowAnonymous = method.AllowAnonymous ? "true" : "false";
			return $"{RpcRoute}.Create<{typeArguments}>(\"{method.RouteName}\", {method.RouteId}u, {allowAnonymous}, " +
			       $"static (service, request, cancellationToken) => service.{method.Identifier}({string.Join(", ", arguments)}))";
		}

		/// <summary>생성할 라우트 표 형식의 메타데이터 이름입니다.</summary>
		public static string MetadataNameOf(string assemblyName)
		{
			return NamespaceOf(assemblyName).Replace("@", string.Empty) + "." + ClassName;
		}

		/// <summary>어셈블리 이름을 네임스페이스로 쓸 수 있게 다듬습니다(<c>Game.Contracts</c> 는 그대로).</summary>
		public static string NamespaceOf(string assemblyName)
		{
			var segments = (assemblyName ?? string.Empty).Split('.').Select(Sanitize);
			return string.Join(".", segments);
		}

		private static string Sanitize(string segment)
		{
			var characters = segment.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
			var identifier = new string(characters);

			if (identifier.Length == 0 || char.IsDigit(identifier[0]))
			{
				identifier = "_" + identifier;
			}

			return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
		}
	}
}
