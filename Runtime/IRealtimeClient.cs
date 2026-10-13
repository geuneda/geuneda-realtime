using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 실시간 RPC 클라이언트입니다. 기본 구현은 <see cref="RealtimeClient"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다.
	/// </summary>
	public interface IRealtimeClient : IRpcCaller, IDisposable
	{
		RealtimeState State { get; }

		/// <summary>연결이 끊겼을 때(재연결 여부 포함). 콜백 디스패처(기본 메인 스레드)에서 불립니다.</summary>
		event Action<RealtimeDisconnectInfo> Disconnected;

		/// <summary>자동 재연결이 끝났을 때(성공 또는 마지막 시도의 실패). 콜백 디스패처에서 불립니다.</summary>
		event Action<Result> Reconnected;

		/// <summary>
		/// 연결하고 로그인합니다. 이미 연결돼 있으면 바로 성공, 연결 중이면 그 결과를 함께 기다립니다(single-flight).
		/// 정책 종료(<see cref="RealtimeResultCodes.NotReconnectable"/>) 상태도 풀고 다시 시도합니다. 로그인 대리자가 던진 예외는 그대로 던집니다.
		/// </summary>
		UniTask<Result> ConnectAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// 푸시 토픽을 구독합니다. 핸들러는 콜백 디스패처에서 불립니다. 돌려준 객체를 Dispose 하면 해제되고, 그 뒤에는 이미 디스패처에 넘긴 푸시도 실행하지 않습니다.
		/// </summary>
		IDisposable Subscribe<TPush>(RpcTopic topic, Action<TPush> handler);
	}
}
