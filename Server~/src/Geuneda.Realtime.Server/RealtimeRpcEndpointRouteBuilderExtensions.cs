using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Geuneda.Realtime.Server
{
	public static class RealtimeRpcEndpointRouteBuilderExtensions
	{
		/// <summary>
		/// 실시간 RPC WebSocket 엔드포인트를 연결합니다. <c>app.UseWebSockets()</c> 뒤에 부릅니다.
		/// 설정, 라우트 ID 중복, 계약 구현 등록을 여기서 검사해 잘못되면 시작할 때 예외를 던집니다.
		/// </summary>
		/// <exception cref="InvalidOperationException">AddRealtimeRpc 를 부르지 않았거나 설정·라우트·구현 등록이 잘못된 경우</exception>
		public static IEndpointConventionBuilder MapRealtimeRpc(this IEndpointRouteBuilder endpoints, string pattern)
		{
			ArgumentNullException.ThrowIfNull(endpoints);

			var handler = endpoints.ServiceProvider.GetRequiredService<RealtimeRpcHandler>();
			handler.ValidateServices(endpoints.ServiceProvider);
			return endpoints.Map(pattern, handler.HandleAsync);
		}
	}
}
