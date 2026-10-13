using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 서버와 메시지 연결을 엽니다. 기본은 <see cref="ClientWebSocketTransport"/> 이고, 테스트에서는 가짜로 바꿉니다.
	/// </summary>
	public interface IRealtimeTransport
	{
		/// <summary>
		/// 연결을 엽니다. 연결하지 못하면 <c>ResultCodes.NetworkError</c>, <paramref name="cancellationToken"/> 이 취소되면 <c>ResultCodes.Cancelled</c>,
		/// 서버가 서브프로토콜을 받지 않으면 <see cref="RealtimeResultCodes.ProtocolMismatch"/> 입니다.
		/// </summary>
		/// <param name="subProtocol">서버와 맞출 서브프로토콜(<c>geuneda.rpc.v1.json</c> 등)</param>
		UniTask<Result<IRealtimeConnection>> ConnectAsync(Uri uri, string subProtocol, CancellationToken cancellationToken);
	}
}
