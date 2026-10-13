using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geuneda.Realtime.Server
{
	public static class RealtimeRpcServiceCollectionExtensions
	{
		/// <summary>
		/// 실시간 RPC 서버를 등록합니다. 계약 구현은 따로 등록합니다(<c>services.AddScoped&lt;IShopRpc, ShopRpc&gt;()</c>).
		/// </summary>
		/// <example>
		/// <code>
		/// builder.Services.AddScoped&lt;IShopRpc, ShopRpc&gt;();
		/// builder.Services.AddRealtimeRpc(options => options.AddRoutes(Game.Contracts.GeneratedRpcRoutes.All));
		/// app.UseWebSockets();
		/// app.MapRealtimeRpc("/rpc");
		/// </code>
		/// </example>
		public static IServiceCollection AddRealtimeRpc(this IServiceCollection services, Action<RealtimeServerOptions> configure)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(configure);

			services.Configure(configure);
			services.TryAddSingleton<RealtimeSessionRegistry>();
			services.TryAddSingleton<IRealtimeSessions>(provider => provider.GetRequiredService<RealtimeSessionRegistry>());
			services.TryAddSingleton<RealtimeRpcHandler>();
			services.TryAddScoped<RpcCallContext>();
			services.TryAddScoped<IRpcContext>(provider => provider.GetRequiredService<RpcCallContext>());
			return services;
		}
	}
}
