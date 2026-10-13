using System.Threading;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 처리 중인 RPC 호출 정보입니다. 호출마다 만드는 DI 범위(scoped)에서 주입받습니다. 호출 밖에서 쓰면 <see cref="System.InvalidOperationException"/> 입니다.
	/// </summary>
	public interface IRpcContext
	{
		IRealtimeSession Session { get; }

		RpcRoute Route { get; }

		uint RequestId { get; }

		/// <summary>
		/// 세션이 닫히기 시작하면(종료를 보내거나 받음, 연결 끊김) 취소됩니다. 계약 메서드의 CancellationToken 인자와 같습니다.
		/// 건 콜백은 스레드 풀에서 실행되고, 던지거나 오래 걸려도 세션 종료·정리를 막지 않습니다.
		/// </summary>
		CancellationToken CallAborted { get; }

		/// <summary>
		/// 세션을 로그인 상태로 만듭니다. 같은 사용자의 다른 세션이 있으면 <see cref="RealtimeCloseCodes.DuplicateLogin"/>(4001)으로 닫습니다.
		/// </summary>
		/// <exception cref="System.ArgumentException"><paramref name="userId"/> 가 비어 있는 경우</exception>
		/// <exception cref="System.InvalidOperationException">이미 다른 사용자로 로그인한 세션인 경우</exception>
		/// <exception cref="System.OperationCanceledException">세션이 닫히기 시작한 경우(늦게 끝난 로그인이 다른 세션을 밀어내지 않음)</exception>
		void SignIn(string userId);
	}
}
