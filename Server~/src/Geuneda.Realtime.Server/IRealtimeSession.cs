using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 클라이언트 연결 하나입니다. RPC 구현은 <see cref="IRpcContext.Session"/> 으로, 다른 곳은 <see cref="IRealtimeSessions"/> 로 얻습니다.
	/// </summary>
	public interface IRealtimeSession
	{
		/// <summary>연결마다 새로 만드는 ID 입니다.</summary>
		string Id { get; }

		/// <summary>로그인한 사용자 ID 입니다. 로그인 전에는 null 입니다(<see cref="IRpcContext.SignIn"/>).</summary>
		string UserId { get; }

		bool IsAuthenticated { get; }

		/// <summary>협상한 서브프로토콜(<c>geuneda.rpc.v1.json</c> 등)입니다.</summary>
		string SubProtocol { get; }

		/// <summary>세션에 붙여 두는 값입니다(스레드 안전).</summary>
		IDictionary<string, object> Items { get; }

		/// <summary>연결이 끝나면 취소됩니다. 건 콜백은 스레드 풀에서 실행되고, 콜백의 예외는 세션 정리를 막지 않습니다(경고 로그).</summary>
		CancellationToken Closed { get; }

		/// <summary>서버 푸시를 보냅니다. 연결이 끝났으면 false 입니다.</summary>
		Task<bool> PushAsync<T>(RpcTopic topic, T message, CancellationToken cancellationToken = default);

		/// <summary>
		/// 종료를 보냅니다. 정책 종료(4000~4999)면 클라이언트가 재연결하지 않습니다. 이미 닫는 중이면 아무것도 하지 않습니다.
		/// 남은 요청은 실행하지 않고, 진행 중인 호출의 <see cref="IRpcContext.CallAborted"/> 를 취소합니다.
		/// </summary>
		/// <exception cref="System.ArgumentOutOfRangeException">종료 메시지에 실을 수 없는 코드(1005, 1006, 1015 등)인 경우</exception>
		Task CloseAsync(int closeStatus);
	}
}
