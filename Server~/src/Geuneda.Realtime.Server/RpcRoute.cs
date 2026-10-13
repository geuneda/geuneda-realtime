using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 계약 메서드 하나의 라우트입니다. 소스 생성기가 <c>GeneratedRpcRoutes.All</c> 로 만들고, 항목마다 타입이 정해진 대리자라 호출마다 리플렉션을 쓰지 않습니다.
	/// </summary>
	public sealed class RpcRoute
	{
		private readonly Func<IServiceProvider, ArraySegment<byte>, IRpcSerializer, CancellationToken, Task<byte[]>> _invoke;

		private RpcRoute(string name, uint id, Type serviceType, bool allowAnonymous,
			Func<IServiceProvider, ArraySegment<byte>, IRpcSerializer, CancellationToken, Task<byte[]>> invoke)
		{
			Name = name;
			Id = id;
			ServiceType = serviceType;
			AllowAnonymous = allowAnonymous;
			_invoke = invoke;
		}

		/// <summary>경로 이름(<c>"IShopRpc/BuyProduct"</c>)입니다.</summary>
		public string Name { get; }

		/// <summary>경로 이름의 FNV-1a 해시입니다. 프레임의 메서드 ID 입니다.</summary>
		public uint Id { get; }

		/// <summary>구현을 DI 에서 꺼낼 계약 인터페이스입니다.</summary>
		public Type ServiceType { get; }

		/// <summary>로그인 전에도 부를 수 있으면 true 입니다(<see cref="RpcAllowAnonymousAttribute"/>).</summary>
		public bool AllowAnonymous { get; }

		/// <summary>응답 본문이 있는 메서드(<c>Task&lt;TResponse&gt;</c>)의 라우트를 만듭니다.</summary>
		/// <exception cref="ArgumentException">이름이 비었거나 <paramref name="id"/> 가 이름의 해시와 다른 경우</exception>
		public static RpcRoute Create<TService, TRequest, TResponse>(string name, uint id, bool allowAnonymous,
			Func<TService, TRequest, CancellationToken, Task<TResponse>> handler)
			where TService : class
		{
			Validate(name, id, handler);

			return new RpcRoute(name, id, typeof(TService), allowAnonymous, async (services, payload, serializer, cancellationToken) =>
			{
				var request = ReadRequest<TRequest>(payload, serializer);
				var response = await handler(services.GetRequiredService<TService>(), request, cancellationToken).ConfigureAwait(false);
				return serializer.Serialize(response);
			});
		}

		/// <summary>응답 본문이 없는 메서드(<c>Task</c>)의 라우트를 만듭니다.</summary>
		/// <exception cref="ArgumentException">이름이 비었거나 <paramref name="id"/> 가 이름의 해시와 다른 경우</exception>
		public static RpcRoute Create<TService, TRequest>(string name, uint id, bool allowAnonymous,
			Func<TService, TRequest, CancellationToken, Task> handler)
			where TService : class
		{
			Validate(name, id, handler);

			return new RpcRoute(name, id, typeof(TService), allowAnonymous, async (services, payload, serializer, cancellationToken) =>
			{
				var request = ReadRequest<TRequest>(payload, serializer);
				await handler(services.GetRequiredService<TService>(), request, cancellationToken).ConfigureAwait(false);
				return Array.Empty<byte>();
			});
		}

		internal Task<byte[]> InvokeAsync(IServiceProvider services, ArraySegment<byte> payload, IRpcSerializer serializer,
			CancellationToken cancellationToken)
		{
			return _invoke(services, payload, serializer, cancellationToken);
		}

		public override string ToString() => Name;

		private static TRequest ReadRequest<TRequest>(ArraySegment<byte> payload, IRpcSerializer serializer)
		{
			if (typeof(TRequest) == typeof(RpcEmpty))
			{
				return default;
			}

			try
			{
				return serializer.Deserialize<TRequest>(payload);
			}
			catch (RpcSerializationException exception)
			{
				throw new RpcBadRequestException(exception.Message, exception);
			}
		}

		private static void Validate(string name, uint id, Delegate handler)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Route name must not be empty", nameof(name));
			}

			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}

			if (Fnv1a32.Hash(name) != id)
			{
				throw new ArgumentException($"Route id {id} is not the FNV-1a hash of '{name}'", nameof(id));
			}
		}
	}
}
