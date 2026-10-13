using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// WebSocket 요청을 받아 서브프로토콜(직렬화 형식)을 고르고 세션을 돌립니다. 만들 때 설정·라우트를 검사합니다(잘못되면 시작할 때 예외).
	/// </summary>
	internal sealed class RealtimeRpcHandler
	{
		private readonly RealtimeServerOptions _options;
		private readonly RpcRouteTable _routes;
		private readonly IReadOnlyDictionary<string, IRpcSerializer> _serializers;
		private readonly IServiceScopeFactory _scopes;
		private readonly RealtimeSessionRegistry _registry;
		private readonly ILogger<RealtimeServerSession> _sessionLogger;

		public RealtimeRpcHandler(IOptions<RealtimeServerOptions> options, IServiceScopeFactory scopes, RealtimeSessionRegistry registry,
			ILogger<RealtimeServerSession> sessionLogger)
		{
			_options = Validate(options.Value);
			_routes = new RpcRouteTable(_options.Routes);
			_serializers = BuildSerializers(_options.Serializers);
			_scopes = scopes;
			_registry = registry;
			_sessionLogger = sessionLogger;
		}

		/// <summary>라우트의 계약 인터페이스가 DI 에 등록됐는지 검사합니다.</summary>
		/// <exception cref="InvalidOperationException">등록되지 않은 계약이 있는 경우</exception>
		public void ValidateServices(IServiceProvider services)
		{
			var isService = services.GetService<IServiceProviderIsService>();
			if (isService == null)
			{
				return;
			}

			var missing = _routes.All.Select(route => route.ServiceType).Distinct().Where(type => !isService.IsService(type)).ToList();
			if (missing.Count > 0)
			{
				throw new InvalidOperationException(
					$"RPC service implementations are not registered: {string.Join(", ", missing.Select(type => type.FullName))}. " +
					"Register them, for example services.AddScoped<IShopRpc, ShopRpc>().");
			}
		}

		public async Task HandleAsync(HttpContext context)
		{
			if (!context.WebSockets.IsWebSocketRequest)
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			var subProtocol = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault(_serializers.ContainsKey);
			if (subProtocol == null)
			{
				// 서브프로토콜 없이 받고 닫으면 클라이언트가 형식 불일치(ProtocolMismatch)로 알고 재연결하지 않는다
				using (var rejected = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false))
				{
					await CloseQuietlyAsync(rejected, RealtimeCloseCodes.ProtocolError).ConfigureAwait(false);
				}

				return;
			}

			using (var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { SubProtocol = subProtocol }).ConfigureAwait(false))
			{
				var session = new RealtimeServerSession(socket, _serializers[subProtocol], subProtocol, _options, _routes, _scopes, _registry,
					_sessionLogger, context.RequestAborted);

				_registry.Add(session);
				try
				{
					await session.RunAsync().ConfigureAwait(false);
				}
				finally
				{
					_registry.Remove(session);
				}
			}
		}

		private async Task CloseQuietlyAsync(WebSocket socket, int closeStatus)
		{
			try
			{
				using (var timeout = new CancellationTokenSource(_options.CloseTimeout))
				{
					await socket.CloseOutputAsync((WebSocketCloseStatus)closeStatus, "Unsupported subprotocol", timeout.Token).ConfigureAwait(false);
				}
			}
			catch (Exception exception) when (exception is WebSocketException || exception is OperationCanceledException)
			{
				// 닫기는 최선 노력이다
			}
		}

		private static RealtimeServerOptions Validate(RealtimeServerOptions options)
		{
			if (options.MaxMessageSize <= 0 || options.MaxQueuedRequests <= 0
			    || options.IdleTimeout <= TimeSpan.Zero || options.CloseTimeout <= TimeSpan.Zero)
			{
				throw new InvalidOperationException("RealtimeServerOptions: MaxMessageSize, MaxQueuedRequests, IdleTimeout and CloseTimeout must be positive");
			}

			return options;
		}

		private static IReadOnlyDictionary<string, IRpcSerializer> BuildSerializers(IEnumerable<IRpcSerializer> serializers)
		{
			var map = new Dictionary<string, IRpcSerializer>(StringComparer.Ordinal);

			foreach (var serializer in serializers)
			{
				if (serializer == null || string.IsNullOrEmpty(serializer.Name))
				{
					throw new InvalidOperationException("RealtimeServerOptions.Serializers contains null or an unnamed serializer");
				}

				if (!map.TryAdd(RpcProtocol.SubProtocolOf(serializer.Name), serializer))
				{
					throw new InvalidOperationException($"RealtimeServerOptions.Serializers has two serializers named '{serializer.Name}'");
				}
			}

			if (map.Count == 0)
			{
				throw new InvalidOperationException("RealtimeServerOptions.Serializers is empty");
			}

			return map;
		}
	}
}
