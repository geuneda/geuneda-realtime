namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 클라이언트 코드를 만듭니다: 메서드 ID 표(<c>ShopRpcMethods</c>), 프록시 인터페이스(<c>IShopRpcClient</c>), 프록시(<c>ShopRpcClient</c>).
	/// 프록시는 <c>IRpcCaller.CallAsync</c> 를 부르는 한 줄이고 리플렉션을 쓰지 않습니다.
	/// </summary>
	internal static class ClientEmitter
	{
		private const string UniTask = "global::Cysharp.Threading.Tasks.UniTask";
		private const string Result = "global::Geuneda.Core.Result";
		private const string CancellationToken = "global::System.Threading.CancellationToken";
		private const string RpcMethod = "global::Geuneda.Realtime.RpcMethod";
		private const string RpcCaller = "global::Geuneda.Realtime.IRpcCaller";

		public static string Emit(ServiceModel service)
		{
			var writer = new CodeWriter().Header();

			if (service.Namespace != null)
			{
				writer.Open("namespace " + service.Namespace);
			}

			writer.Line($"/// <summary>{service.InterfaceName} 의 메서드 경로입니다(생성 코드).</summary>");
			writer.Open($"{service.Accessibility} static class {service.MethodsClassName}");
			foreach (var method in service.Methods)
			{
				writer.Line($"public static readonly {RpcMethod} {method.Identifier} = {RpcMethod}.Of(\"{method.RouteName}\");");
			}

			writer.Close().Line();

			writer.Line($"/// <summary>{service.InterfaceName} 를 부르는 클라이언트입니다(생성 코드). 결과 규칙은 IRpcCaller 와 같습니다.</summary>");
			writer.Open($"{service.Accessibility} interface {service.ClientInterfaceName}");
			foreach (var method in service.Methods)
			{
				writer.Line($"{ReturnType(method)} {method.Identifier}({Parameters(method)});");
			}

			writer.Close().Line();

			writer.Line($"/// <summary>{service.InterfaceName} 를 부르는 클라이언트입니다(생성 코드). RealtimeClient 같은 IRpcCaller 를 받습니다.</summary>");
			writer.Open($"{service.Accessibility} sealed class {service.ClientName} : {service.ClientInterfaceName}");
			writer.Line($"private readonly {RpcCaller} _caller;").Line();
			writer.Open($"public {service.ClientName}({RpcCaller} caller)");
			writer.Line("_caller = caller ?? throw new global::System.ArgumentNullException(nameof(caller));");
			writer.Close();

			foreach (var method in service.Methods)
			{
				writer.Line();
				writer.Open($"public {ReturnType(method)} {method.Identifier}({Parameters(method)})");
				writer.Line($"return {Call(service, method)};");
				writer.Close();
			}

			writer.Close();

			if (service.Namespace != null)
			{
				writer.Close();
			}

			return writer.ToString();
		}

		private static string ReturnType(MethodModel method)
		{
			return method.ResponseType == null ? $"{UniTask}<{Result}>" : $"{UniTask}<{Result}<{method.ResponseType}>>";
		}

		private static string Parameters(MethodModel method)
		{
			var cancellation = $"{CancellationToken} cancellationToken = default";
			return method.RequestType == null ? cancellation : $"{method.RequestType} request, {cancellation}";
		}

		private static string Call(ServiceModel service, MethodModel method)
		{
			var route = $"{service.MethodsClassName}.{method.Identifier}";
			var requestType = method.RequestType ?? "object";
			var request = method.RequestType == null ? "null" : "request";
			var typeArguments = method.ResponseType == null ? requestType : $"{requestType}, {method.ResponseType}";

			return $"_caller.CallAsync<{typeArguments}>({route}, {request}, cancellationToken)";
		}
	}
}
