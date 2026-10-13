using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// RPC 를 부르는 쪽입니다. <see cref="IRealtimeClient"/> 와 로그인 단계에 넘기는 호출자가 구현합니다.
	/// </summary>
	public interface IRpcCaller
	{
		/// <summary>
		/// 요청을 보내고 응답을 받습니다. 실패는 예외 대신 코드입니다: 연결 실패·끊김 <c>NetworkError</c>, 시간 초과 <c>Timeout</c>,
		/// 취소 <c>Cancelled</c>, 서버 상태 코드가 100000 이상(게임 범위)이면 그 값, 그 밖의 0 이 아닌 상태나 응답 해석 실패는
		/// <see cref="RealtimeResultCodes.InvalidResponse"/>.
		/// </summary>
		/// <remarks>
		/// 제한 시간과 취소는 보내기 차례를 기다릴 때부터 적용합니다. 보내기 전에 끝난 요청은 보내지 않지만,
		/// 이미 보내는 중이거나 보낸 요청은 서버가 처리할 수 있습니다(결과는 버림).
		/// </remarks>
		UniTask<Result<TResponse>> CallAsync<TRequest, TResponse>(RpcMethod method, TRequest request, CancellationToken cancellationToken = default);

		/// <summary>응답 본문을 쓰지 않는 호출입니다. 결과 규칙은 같습니다.</summary>
		UniTask<Result> CallAsync<TRequest>(RpcMethod method, TRequest request, CancellationToken cancellationToken = default);
	}
}
