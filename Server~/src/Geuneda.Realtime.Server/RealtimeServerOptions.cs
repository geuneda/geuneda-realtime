using System;
using System.Collections.Generic;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 실시간 RPC 서버 설정입니다(<c>AddRealtimeRpc</c>). 서버가 시작한 뒤에는 바꾸지 않습니다.
	/// </summary>
	public sealed class RealtimeServerOptions
	{
		/// <summary>라우트입니다. 계약 어셈블리의 <c>GeneratedRpcRoutes.All</c> 을 <see cref="AddRoutes"/> 로 넣습니다.</summary>
		public List<RpcRoute> Routes { get; } = new List<RpcRoute>();

		/// <summary>
		/// 직렬화입니다. 클라이언트가 요청한 서브프로토콜(<c>geuneda.rpc.v1.&lt;Name&gt;</c>) 중 처음 맞는 것을 씁니다. 기본은 JSON 하나이고,
		/// MessagePack 은 <c>Serializers.Add(new MessagePackRpcSerializer(options))</c> 로 더합니다.
		/// </summary>
		public List<IRpcSerializer> Serializers { get; } = new List<IRpcSerializer> { new JsonRpcSerializer() };

		/// <summary>받는 메시지 하나의 최대 크기입니다. 넘으면 1009 로 닫습니다. 기본 16MB(클라이언트와 같음)입니다.</summary>
		public int MaxMessageSize { get; set; } = 16 * 1024 * 1024;

		/// <summary>아무 메시지(핑 포함)도 받지 않으면 1001 로 닫는 시간입니다. 기본 60초(클라이언트 핑 15초 + 무응답 10초보다 길게)입니다.</summary>
		public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

		/// <summary>세션당 처리 대기 요청 수입니다. 넘치는 요청은 <see cref="RpcStatusCodes.Overloaded"/> 로 바로 거절합니다(받기·핑은 계속). 기본 256 입니다.</summary>
		public int MaxQueuedRequests { get; set; } = 256;

		/// <summary>종료를 보낸 뒤 클라이언트 답을 기다리는 시간입니다. 기본 5초입니다.</summary>
		public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(5);

		/// <exception cref="ArgumentNullException"><paramref name="routes"/> 가 null 인 경우</exception>
		public RealtimeServerOptions AddRoutes(IEnumerable<RpcRoute> routes)
		{
			Routes.AddRange(routes ?? throw new ArgumentNullException(nameof(routes)));
			return this;
		}
	}
}
