using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// WebSocket 실시간 RPC 클라이언트입니다. 연결 관리(single-flight 연결, 로그인, 자동 재연결)와 세션(<see cref="RealtimeSession"/>)을 나눕니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 연결은 동시에 몇 곳에서 요청해도 한 번만 시도하고 결과를 함께 받습니다. 연결할 때마다 <see cref="RealtimeClientOptions.LoginAsync"/> 로 다시 로그인합니다.
	/// </para>
	/// <para>
	/// 끊기면 대기 중인 호출은 <c>NetworkError</c> 로 끝납니다. 서버 정책 종료(4000~4999)가 아니면 지수 백오프와 지터로 자동 재연결합니다.
	/// 정책 종료(로그인 중 받은 것 포함) 뒤의 호출은 <see cref="RealtimeResultCodes.NotReconnectable"/> 이고, <see cref="ConnectAsync"/> 로 다시 연결합니다.
	/// </para>
	/// <para>
	/// 로그인 대리자가 던진 예외(취소 제외)는 결과로 바꾸지 않고 그 연결을 기다리던 호출 모두에 그대로 던집니다. 자동 재연결 중이면 오류 로그를 남기고 재연결을 멈춥니다.
	/// </para>
	/// <para>
	/// 호출과 연결의 결과(로그인 대리자 안의 호출 포함)는 메인 스레드에서 돌려줍니다. 이벤트와 푸시 핸들러는 콜백 디스패처(기본 메인 스레드)에서 불리고, 예외는 로그만 남깁니다.
	/// 수신 처리(프레임 해석, 응답·푸시 역직렬화)는 전송에 따라 백그라운드 스레드에서 할 수 있으므로 직렬화는 스레드 안전해야 합니다.
	/// 구독을 해제했거나 클라이언트를 Dispose 했으면 이미 디스패처에 넘긴 푸시도 실행하지 않습니다.
	/// </para>
	/// </remarks>
	public sealed class RealtimeClient : IRealtimeClient
	{
		private sealed class Subscription
		{
			public uint TopicId;
			public Action<ArraySegment<byte>> Deliver;
			/// <summary>구독 해제 뒤에는 이미 디스패처에 넘긴 푸시도 실행하지 않도록 실행 시점에 확인합니다.</summary>
			public volatile bool Active = true;
		}

		private sealed class SubscriptionHandle : IDisposable
		{
			private RealtimeClient _owner;
			private readonly Subscription _subscription;

			public SubscriptionHandle(RealtimeClient owner, Subscription subscription)
			{
				_owner = owner;
				_subscription = subscription;
			}

			public void Dispose()
			{
				Interlocked.Exchange(ref _owner, null)?.RemoveSubscription(_subscription);
			}
		}

		/// <summary>로그인 단계에서 아직 공개하지 않은 세션으로 호출합니다. 공개 호출과 같이 결과는 메인 스레드에서 돌려줍니다.</summary>
		private sealed class SessionCaller : IRpcCaller
		{
			private readonly RealtimeClient _owner;
			private readonly RealtimeSession _session;

			public SessionCaller(RealtimeClient owner, RealtimeSession session)
			{
				_owner = owner;
				_session = session;
			}

			public async UniTask<Result<TResponse>> CallAsync<TRequest, TResponse>(RpcMethod method, TRequest request, CancellationToken cancellationToken = default)
			{
				try
				{
					return await _owner.CallOnSessionAsync<TRequest, TResponse>(_session, method, request, cancellationToken);
				}
				finally
				{
					await UniTask.SwitchToMainThread();
				}
			}

			public async UniTask<Result> CallAsync<TRequest>(RpcMethod method, TRequest request, CancellationToken cancellationToken = default)
			{
				try
				{
					return await _owner.CallOnSessionAsync<TRequest, object>(_session, method, request, cancellationToken, false);
				}
				finally
				{
					await UniTask.SwitchToMainThread();
				}
			}
		}

		/// <summary>서버 오류 상태의 최솟값입니다(core 결과 코드 범위 표의 게임 범위).</summary>
		private const int MinServerErrorCode = 100000;

		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Realtime");
		private static readonly Subscription[] NoSubscriptions = new Subscription[0];

		private readonly Uri _serverUri;
		private readonly IRpcSerializer _serializer;
		private readonly IRealtimeTransport _transport;
		private readonly Func<IRpcCaller, CancellationToken, UniTask<Result>> _loginAsync;
		private readonly bool _connectOnCall;
		private readonly TimeSpan _connectTimeout;
		private readonly TimeSpan _requestTimeout;
		private readonly TimeSpan _pingInterval;
		private readonly TimeSpan _pingTimeout;
		private readonly int _maxReconnectAttempts;
		private readonly TimeSpan _reconnectBaseDelay;
		private readonly TimeSpan _reconnectMaxDelay;
		private readonly Action<Action> _dispatcher;
		private readonly string _subProtocol;

		private readonly object _gate = new object();
		private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
		private readonly Random _random = new Random();

		private RealtimeSession _session;
		private UniTaskCompletionSource<Result> _connecting;
		private string _policyCloseReason;
		private bool _reconnecting;
		private bool _disposed;
		private Subscription[] _subscriptions = NoSubscriptions;

		public event Action<RealtimeDisconnectInfo> Disconnected;
		public event Action<Result> Reconnected;

		/// <exception cref="ArgumentNullException"><paramref name="options"/> 나 필수 값이 null 인 경우</exception>
		/// <exception cref="ArgumentException">주소가 ws/wss 가 아니거나 시간·횟수 값이 범위 밖인 경우</exception>
		public RealtimeClient(RealtimeClientOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			_serverUri = options.ServerUri ?? throw new ArgumentNullException(nameof(options.ServerUri));
			if (!_serverUri.IsAbsoluteUri || (_serverUri.Scheme != "ws" && _serverUri.Scheme != "wss"))
			{
				throw new ArgumentException($"Server uri must be ws:// or wss://: {_serverUri}", nameof(options));
			}

			_serializer = options.Serializer ?? throw new ArgumentNullException(nameof(options.Serializer));
			_transport = options.Transport ?? throw new ArgumentNullException(nameof(options.Transport));
			_loginAsync = options.LoginAsync;
			_connectOnCall = options.ConnectOnCall;
			_connectTimeout = RequirePositive(options.ConnectTimeout, nameof(options.ConnectTimeout));
			_requestTimeout = RequirePositive(options.RequestTimeout, nameof(options.RequestTimeout));
			_pingInterval = options.PingInterval >= TimeSpan.Zero ? options.PingInterval
				: throw new ArgumentException("PingInterval must not be negative", nameof(options));
			_pingTimeout = RequirePositive(options.PingTimeout, nameof(options.PingTimeout));
			_maxReconnectAttempts = options.MaxReconnectAttempts >= 0 ? options.MaxReconnectAttempts
				: throw new ArgumentException("MaxReconnectAttempts must not be negative", nameof(options));
			_reconnectBaseDelay = RequirePositive(options.ReconnectBaseDelay, nameof(options.ReconnectBaseDelay));
			_reconnectMaxDelay = options.ReconnectMaxDelay >= _reconnectBaseDelay ? options.ReconnectMaxDelay
				: throw new ArgumentException("ReconnectMaxDelay must be >= ReconnectBaseDelay", nameof(options));
			_dispatcher = options.CallbackDispatcher ?? (action => UniTask.Post(action));
			_subProtocol = "geuneda.rpc.v1." + _serializer.Name;
		}

		public RealtimeState State
		{
			get
			{
				lock (_gate)
				{
					if (_disposed || (_session == null && _connecting == null))
					{
						return RealtimeState.Disconnected;
					}

					return _session != null ? RealtimeState.Connected : RealtimeState.Connecting;
				}
			}
		}

		public async UniTask<Result> ConnectAsync(CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();

			lock (_gate)
			{
				_policyCloseReason = null;
			}

			// 결과뿐 아니라 예외(로그인 대리자 버그)도 메인 스레드에서 받게 한다
			try
			{
				return await EnsureConnectedAsync(cancellationToken);
			}
			finally
			{
				await UniTask.SwitchToMainThread();
			}
		}

		public async UniTask<Result<TResponse>> CallAsync<TRequest, TResponse>(RpcMethod method, TRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();

			try
			{
				var session = await GetSessionForCallAsync(cancellationToken);
				return session.TryGetValue(out var connected)
					? await CallOnSessionAsync<TRequest, TResponse>(connected, method, request, cancellationToken)
					: Result<TResponse>.Fail(session.Code, session.Message);
			}
			finally
			{
				await UniTask.SwitchToMainThread();
			}
		}

		public async UniTask<Result> CallAsync<TRequest>(RpcMethod method, TRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();

			try
			{
				var session = await GetSessionForCallAsync(cancellationToken);
				return session.TryGetValue(out var connected)
					? await CallOnSessionAsync<TRequest, object>(connected, method, request, cancellationToken, false)
					: Result.Fail(session.Code, session.Message);
			}
			finally
			{
				await UniTask.SwitchToMainThread();
			}
		}

		/// <exception cref="ArgumentNullException"><paramref name="handler"/> 가 null 인 경우</exception>
		public IDisposable Subscribe<TPush>(RpcTopic topic, Action<TPush> handler)
		{
			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}

			ThrowIfDisposed();

			var subscription = new Subscription { TopicId = topic.Id };
			subscription.Deliver = payload =>
			{
				TPush message;
				try
				{
					message = _serializer.Deserialize<TPush>(payload);
				}
				catch (RpcSerializationException exception)
				{
					Logger.Warning($"Dropped push for {topic}: {exception.Message}");
					return;
				}

				Dispatch(() =>
				{
					if (subscription.Active)
					{
						handler(message);
					}
				});
			};

			lock (_gate)
			{
				var updated = new Subscription[_subscriptions.Length + 1];
				Array.Copy(_subscriptions, updated, _subscriptions.Length);
				updated[_subscriptions.Length] = subscription;
				_subscriptions = updated;
			}

			return new SubscriptionHandle(this, subscription);
		}

		/// <summary>
		/// 연결을 닫고 재연결을 멈춥니다. 대기 중인 호출과 연결은 <see cref="RealtimeResultCodes.Closed"/> 로 끝납니다. 이후 호출은 <see cref="ObjectDisposedException"/> 입니다.
		/// </summary>
		public void Dispose()
		{
			RealtimeSession session;

			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				session = _session;
				_session = null;
			}

			_lifetime.Cancel();
			session?.Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.Normal, "Client disposed", false),
				RealtimeCloseCodes.Normal, RealtimeResultCodes.Closed, false);
		}

		internal async UniTask<Result<TResponse>> CallOnSessionAsync<TRequest, TResponse>(RealtimeSession session, RpcMethod method,
			TRequest request, CancellationToken cancellationToken, bool readBody = true)
		{
			var response = await session.CallAsync(method.Id, _serializer.Serialize(request), _requestTimeout, cancellationToken);

			if (!response.TryGetValue(out var frame))
			{
				return Result<TResponse>.Fail(MapClosedCode(response.Code), response.Message);
			}

			if (frame.Status >= MinServerErrorCode)
			{
				return Result<TResponse>.Fail(frame.Status, $"{method} failed with status {frame.Status}");
			}

			if (frame.Status != 0)
			{
				// 공용·패키지 코드와 겹치는 값(예: 4 NetworkError)을 그대로 쓰면 재시도 판단이 틀어진다
				return Result<TResponse>.Fail(RealtimeResultCodes.InvalidResponse, $"{method} returned status {frame.Status} outside the game code range");
			}

			if (!readBody)
			{
				return Result<TResponse>.Ok(default);
			}

			try
			{
				return Result<TResponse>.Ok(_serializer.Deserialize<TResponse>(frame.Payload));
			}
			catch (RpcSerializationException exception)
			{
				return Result<TResponse>.Fail(RealtimeResultCodes.InvalidResponse, $"{method}: {exception.Message}");
			}
		}

		private async UniTask<Result<RealtimeSession>> GetSessionForCallAsync(CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (_session != null)
				{
					return Result<RealtimeSession>.Ok(_session);
				}

				if (_policyCloseReason != null)
				{
					return Result<RealtimeSession>.Fail(RealtimeResultCodes.NotReconnectable, _policyCloseReason);
				}

				if (!_connectOnCall)
				{
					return Result<RealtimeSession>.Fail(RealtimeResultCodes.NotConnected);
				}
			}

			var connected = await EnsureConnectedAsync(cancellationToken);
			if (!connected.IsSuccess)
			{
				return Result<RealtimeSession>.Fail(connected.Code, connected.Message);
			}

			lock (_gate)
			{
				return _session != null
					? Result<RealtimeSession>.Ok(_session)
					: Result<RealtimeSession>.Fail(ResultCodes.NetworkError, "Connection lost right after connecting");
			}
		}

		private async UniTask<Result> EnsureConnectedAsync(CancellationToken cancellationToken)
		{
			UniTaskCompletionSource<Result> connecting;
			var start = false;

			lock (_gate)
			{
				if (_disposed)
				{
					return Result.Fail(RealtimeResultCodes.Closed);
				}

				if (_session != null)
				{
					return Result.Ok();
				}

				if (_policyCloseReason != null)
				{
					return Result.Fail(RealtimeResultCodes.NotReconnectable, _policyCloseReason);
				}

				if (_connecting == null)
				{
					_connecting = new UniTaskCompletionSource<Result>();
					start = true;
				}

				connecting = _connecting;
			}

			if (start)
			{
				RunConnectAsync(connecting).Forget();
			}

			try
			{
				return await connecting.Task.AttachExternalCancellation(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// 연결 시도는 다른 대기자를 위해 계속된다
				return Result.Fail(ResultCodes.Cancelled);
			}
		}

		private async UniTaskVoid RunConnectAsync(UniTaskCompletionSource<Result> connecting)
		{
			Result result;

			try
			{
				result = await ConnectOnceAsync();
			}
			catch (Exception exception)
			{
				// 로그인 대리자 같은 바깥 코드의 버그는 결과로 바꾸지 않고 기다리는 쪽 모두에게 던진다
				lock (_gate)
				{
					_connecting = null;
				}

				connecting.TrySetException(exception);
				return;
			}

			lock (_gate)
			{
				_connecting = null;
			}

			connecting.TrySetResult(result);
		}

		private async UniTask<Result> ConnectOnceAsync()
		{
			using (var timeout = new CancellationTokenSource(_connectTimeout))
			using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _lifetime.Token))
			{
				var token = linked.Token;
				var connected = await _transport.ConnectAsync(_serverUri, _subProtocol, token);

				if (!connected.TryGetValue(out var connection))
				{
					return MapConnectFailure(connected, timeout);
				}

				var session = new RealtimeSession(connection, _pingInterval, _pingTimeout, OnPush, OnSessionClosed);
				var published = false;

				try
				{
					session.Start();
					var login = await LoginAsync(session, token);

					lock (_gate)
					{
						if (_disposed)
						{
							return Result.Fail(RealtimeResultCodes.Closed);
						}

						var closeInfo = session.CloseInfo;
						if (closeInfo.HasValue)
						{
							return FailClosedDuringLogin(closeInfo.Value);
						}

						// 제한 시간이 지난 뒤 끝난 로그인은 공개하지 않는다
						if (token.IsCancellationRequested)
						{
							return MapConnectFailure(Result.Fail(ResultCodes.Cancelled), timeout);
						}

						if (login.IsSuccess)
						{
							_session = session;
							published = true;
						}
					}

					return login.IsSuccess ? Result.Ok() : MapConnectFailure(login, timeout);
				}
				finally
				{
					// 공개하지 못한 세션(로그인 실패, 로그인 대리자 예외, 종료, 취소)은 여기서 닫는다
					if (!published)
					{
						session.Close(new RealtimeDisconnectInfo(RealtimeCloseCodes.Normal, "Connect attempt ended", false),
							RealtimeCloseCodes.Normal, _lifetime.IsCancellationRequested ? RealtimeResultCodes.Closed : ResultCodes.NetworkError, false);
					}
				}
			}
		}

		private async UniTask<Result> LoginAsync(RealtimeSession session, CancellationToken cancellationToken)
		{
			if (_loginAsync == null)
			{
				return Result.Ok();
			}

			try
			{
				// 연결을 백그라운드 스레드에서 시작했어도 로그인 대리자는 메인 스레드에서 부른다(토큰 읽기 등 Unity API 사용).
				// 대리자가 토큰을 무시해도 연결 제한 시간과 Dispose 가 듣도록 기다림 자체를 토큰에 묶는다(늦은 결과는 버림)
				await UniTask.SwitchToMainThread(cancellationToken);
				return await _loginAsync(new SessionCaller(this, session), cancellationToken).AttachExternalCancellation(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return Result.Fail(ResultCodes.Cancelled);
			}
		}

		/// <summary>로그인 중 서버가 닫은 경우입니다. 정책 종료면 이후 자동 연결·재연결을 막습니다. <c>_gate</c> 안에서 부릅니다.</summary>
		private Result FailClosedDuringLogin(RealtimeDisconnectInfo closeInfo)
		{
			var reason = $"Server closed the session during login ({closeInfo.CloseStatus} {closeInfo.Reason})";

			if (RealtimeCloseCodes.IsReconnectable(closeInfo.CloseStatus))
			{
				return Result.Fail(ResultCodes.NetworkError, reason);
			}

			_policyCloseReason = reason;
			return Result.Fail(RealtimeResultCodes.NotReconnectable, reason);
		}

		private Result MapConnectFailure(Result failure, CancellationTokenSource timeout)
		{
			if (failure.Code != ResultCodes.Cancelled)
			{
				return failure;
			}

			if (_lifetime.IsCancellationRequested)
			{
				return Result.Fail(RealtimeResultCodes.Closed);
			}

			return timeout.IsCancellationRequested
				? Result.Fail(ResultCodes.Timeout, $"Connect and login did not finish in {_connectTimeout.TotalSeconds:0.###}s")
				: failure;
		}

		private int MapClosedCode(int code)
		{
			// 클라이언트를 닫아서 끝난 호출은 Closed 로 알린다
			return code == ResultCodes.NetworkError && _lifetime.IsCancellationRequested ? RealtimeResultCodes.Closed : code;
		}

		private void OnPush(RealtimeSession session, RpcFrame frame)
		{
			Subscription[] subscriptions;

			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				subscriptions = _subscriptions;
			}

			foreach (var subscription in subscriptions)
			{
				if (subscription.TopicId == frame.TopicId)
				{
					subscription.Deliver(frame.Payload);
				}
			}
		}

		private void OnSessionClosed(RealtimeSession session, RealtimeDisconnectInfo info)
		{
			bool willReconnect;
			bool startLoop;

			lock (_gate)
			{
				// 로그인 중이던 세션이나 이미 바뀐 세션의 종료는 무시한다
				if (session != _session || _disposed)
				{
					return;
				}

				_session = null;

				var reconnectable = RealtimeCloseCodes.IsReconnectable(info.CloseStatus);
				if (!reconnectable)
				{
					_policyCloseReason = $"Server closed the session ({info.CloseStatus} {info.Reason})";
				}

				// 재연결 루프가 이미 돌고 있으면 그 루프가 이 종료도 맡는다(성공 확정 전에 다시 끊긴 것을 루프가 확인함)
				willReconnect = reconnectable && _maxReconnectAttempts > 0;
				startLoop = willReconnect && !_reconnecting;
				if (startLoop)
				{
					_reconnecting = true;
				}
			}

			var disconnected = new RealtimeDisconnectInfo(info.CloseStatus, info.Reason, willReconnect);
			Logger.Info($"Disconnected: {disconnected}");
			Dispatch(() => Disconnected?.Invoke(disconnected));

			if (startLoop)
			{
				ReconnectLoopAsync().Forget();
			}
		}

		/// <summary>
		/// 자동 재연결 루프입니다. 한 번에 하나만 돌고(<c>_reconnecting</c>), 루프가 도는 동안 생긴 종료도 이 루프가 맡습니다.
		/// 끝날 때 결과를 <see cref="Reconnected"/> 로 한 번 알립니다.
		/// </summary>
		private async UniTaskVoid ReconnectLoopAsync()
		{
			while (true)
			{
				var result = await ReconnectAttemptsAsync();
				bool again;
				bool disposed;

				lock (_gate)
				{
					disposed = _disposed;

					// 성공을 확정하기 전에 다시 끊겼으면 OnSessionClosed 는 새 루프를 만들지 않았으므로 이 루프가 처음부터 다시 시도한다
					var lostAgain = !disposed && result.IsSuccess && _session == null;
					again = lostAgain && _policyCloseReason == null;
					if (lostAgain && !again)
					{
						result = Result.Fail(RealtimeResultCodes.NotReconnectable, _policyCloseReason);
					}

					if (!again)
					{
						_reconnecting = false;
					}
				}

				if (again)
				{
					continue;
				}

				if (!disposed)
				{
					Dispatch(() => Reconnected?.Invoke(result));
				}

				return;
			}
		}

		private async UniTask<Result> ReconnectAttemptsAsync()
		{
			var result = Result.Fail(ResultCodes.NetworkError, "Reconnect did not run");

			for (var attempt = 0; attempt < _maxReconnectAttempts; attempt++)
			{
				try
				{
					await UniTask.Delay(GetReconnectDelay(attempt), true, PlayerLoopTiming.Update, _lifetime.Token);
					result = await EnsureConnectedAsync(_lifetime.Token);
				}
				catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
				{
					return Result.Fail(RealtimeResultCodes.Closed);
				}
				catch (Exception exception)
				{
					// 로그인 대리자 같은 바깥 코드의 버그다. 받을 호출자가 없으므로 오류 로그로 알리고 멈춘다
					Logger.Error(exception, "Realtime reconnect failed unexpectedly");
					return Result.Fail(ResultCodes.Unknown, exception.Message);
				}

				if (result.IsSuccess || !IsRetryable(result.Code))
				{
					return result;
				}

				Logger.Info($"Reconnect attempt {attempt + 1} failed: {result}");
			}

			return result;
		}

		internal TimeSpan GetReconnectDelay(int attempt)
		{
			double random;
			lock (_random)
			{
				random = _random.NextDouble();
			}

			var exponential = _reconnectBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 30));
			var cap = Math.Min(_reconnectMaxDelay.TotalMilliseconds, exponential);
			return TimeSpan.FromMilliseconds(cap * (0.5 + 0.5 * random));
		}

		private static bool IsRetryable(int code)
		{
			return code == ResultCodes.NetworkError || code == ResultCodes.Timeout;
		}

		private void RemoveSubscription(Subscription subscription)
		{
			subscription.Active = false;

			lock (_gate)
			{
				var index = Array.IndexOf(_subscriptions, subscription);
				if (index < 0)
				{
					return;
				}

				var updated = new Subscription[_subscriptions.Length - 1];
				Array.Copy(_subscriptions, 0, updated, 0, index);
				Array.Copy(_subscriptions, index + 1, updated, index, _subscriptions.Length - index - 1);
				_subscriptions = updated;
			}
		}

		private void Dispatch(Action action)
		{
			try
			{
				_dispatcher(() =>
				{
					// 디스패처가 나중에 실행해도 Dispose 뒤의 이벤트·푸시는 버린다
					if (IsDisposed)
					{
						return;
					}

					try
					{
						action();
					}
					catch (Exception exception)
					{
						Logger.Error(exception, "Realtime callback threw");
					}
				});
			}
			catch (Exception exception)
			{
				// 디스패처 자체의 실패(닫힌 큐 등)가 세션 정리·재연결을 끊지 않게 한다
				Logger.Error(exception, "Realtime callback dispatcher failed");
			}
		}

		private bool IsDisposed
		{
			get
			{
				lock (_gate)
				{
					return _disposed;
				}
			}
		}

		private void ThrowIfDisposed()
		{
			if (IsDisposed)
			{
				throw new ObjectDisposedException(nameof(RealtimeClient));
			}
		}

		private static TimeSpan RequirePositive(TimeSpan value, string name)
		{
			if (value <= TimeSpan.Zero)
			{
				throw new ArgumentException($"{name} must be positive", name);
			}

			return value;
		}
	}
}
