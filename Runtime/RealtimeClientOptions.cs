using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// <see cref="RealtimeClient"/> 설정입니다. 클라이언트를 만들 때 값을 복사하므로, 그 뒤에 바꿔도 반영되지 않습니다.
	/// </summary>
	public sealed class RealtimeClientOptions
	{
		/// <summary>서버 주소(<c>wss://</c> 권장, <c>ws://</c>)입니다. 필수입니다.</summary>
		public Uri ServerUri { get; set; }

		/// <summary>본문 직렬화입니다. 기본 <see cref="JsonRpcSerializer"/> 입니다.</summary>
		public IRpcSerializer Serializer { get; set; } = new JsonRpcSerializer();

		/// <summary>전송입니다. 기본 <see cref="ClientWebSocketTransport"/> 입니다.</summary>
		public IRealtimeTransport Transport { get; set; } = new ClientWebSocketTransport();

		/// <summary>
		/// 연결할 때마다(재연결 포함) 메인 스레드에서 부르는 로그인입니다. 넘겨받은 호출자로 로그인 RPC 를 보내고 결과를 돌려줍니다.
		/// 실패하면 연결을 닫고 그 코드로 연결이 실패합니다. 예외(취소 제외)는 연결을 기다리던 호출로 그대로 던집니다.
		/// 토큰은 여기서 얻어 첫 메시지로 보냅니다(URL 에 싣지 않음). 없으면 로그인 없이 연결합니다.
		/// </summary>
		public Func<IRpcCaller, CancellationToken, UniTask<Result>> LoginAsync { get; set; }

		/// <summary>연결이 없을 때 호출이 연결을 시작하면 true(기본), 아니면 <see cref="RealtimeResultCodes.NotConnected"/> 입니다.</summary>
		public bool ConnectOnCall { get; set; } = true;

		/// <summary>연결과 로그인을 합친 제한 시간입니다. 기본 10초입니다.</summary>
		public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>호출 하나의 제한 시간입니다. 기본 10초입니다.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>핑 간격입니다. 기본 15초, <see cref="TimeSpan.Zero"/> 면 핑을 보내지 않습니다.</summary>
		public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(15);

		/// <summary>퐁이 <c>PingInterval + PingTimeout</c> 동안 없으면 끊긴 것으로 봅니다. 기본 10초입니다.</summary>
		public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>끊긴 뒤 자동 재연결 최대 시도 횟수입니다. 기본 5, 0 이면 자동 재연결하지 않습니다(다음 호출이 연결).</summary>
		public int MaxReconnectAttempts { get; set; } = 5;

		/// <summary>재연결 대기의 기준입니다. n 번째 시도(0부터) 전 대기는 <c>min(Max, Base × 2^n)</c> 의 50~100% 입니다. 기본 1초입니다.</summary>
		public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

		/// <summary>재연결 대기의 상한입니다. 기본 30초입니다.</summary>
		public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

		/// <summary>
		/// 이벤트와 푸시 핸들러를 실행할 곳입니다. 없으면 UniTask 로 메인 스레드 PlayerLoop 에 넘깁니다.
		/// 백그라운드 스레드에서 불릴 수 있으므로 직접 넣는다면 스레드 안전해야 합니다.
		/// </summary>
		public Action<Action> CallbackDispatcher { get; set; }
	}
}
