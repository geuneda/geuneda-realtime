using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 클라이언트 연결 하나입니다. 받기 루프, 요청 처리 루프, 무응답 감시가 함께 돌고, 받기 루프가 끝나면 세션이 끝납니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 내부 신호와 공개 토큰을 나눕니다. 내부 <c>_connection</c>(받기·보내기·무응답 감시)과 <c>_dispatch</c>(요청 처리)는 이 클래스만 기다리고,
	/// 공개 <c>_callsAborted</c>(<see cref="IRpcContext.CallAborted"/>)와 <c>_closed</c>(<see cref="Closed"/>)는 사용자 코드가 콜백을 걸 수 있으므로
	/// <c>CancelAsync</c> 로 스레드 풀에서 알리고 실패만 관찰합니다. 그래서 콜백이 던지거나 막혀도 종료·정리가 멈추지 않습니다.
	/// 종료가 시작되면(어느 쪽이 닫든) 공개 호출 토큰을 먼저 취소 상태로 만들고 처리를 멈춰, 큐에 남은 요청은 실행하지 않습니다.
	/// 호출은 스레드 풀에서 시작하므로(<c>Task.Run</c>) 구현이 Task 를 돌려주기 전에 동기로 막혀도 처리 루프는 멈추지 않고,
	/// 연결 정리는 진행 중인 호출이 끝나기를 기다리지 않습니다(그 호출은 자기 DI 범위를 들고 끝까지 돌고, 결과는 버림).
	/// 세션이 끝나면 대기열에 남은 요청을 비워, 놓아 준 호출이 세션을 붙잡아도 요청 본문은 남지 않게 합니다.
	/// </para>
	/// <para>
	/// 요청은 받은 순서대로 하나씩 처리합니다(같은 사용자의 구매·사용 순서가 섞이지 않게). 대기 요청이
	/// <see cref="RealtimeServerOptions.MaxQueuedRequests"/> 를 넘으면 <see cref="RpcStatusCodes.Overloaded"/> 로 바로 거절하므로
	/// 받기 루프는 멈추지 않고 핑에 계속 답합니다.
	/// </para>
	/// <para>
	/// 보내기(응답, 퐁, 푸시, 종료)는 <c>_sendLock</c> 으로 하나씩입니다. 종료는 한 번만 보내고, 그 뒤 보내기는 하지 않습니다.
	/// 종료를 보낸 뒤에는 클라이언트의 종료 답을 기다리며 받은 메시지를 버리고, <see cref="RealtimeServerOptions.CloseTimeout"/> 이 지나면 끝냅니다
	/// (바로 끊으면 클라이언트가 종료 프레임을 읽기 전에 연결 리셋을 받을 수 있음).
	/// </para>
	/// </remarks>
	internal sealed class RealtimeServerSession : IRealtimeSession
	{
		private const int ReceiveBufferSize = 16 * 1024;
		private static readonly byte[] NoBody = Array.Empty<byte>();

		private readonly WebSocket _socket;
		private readonly IRpcSerializer _serializer;
		private readonly RealtimeServerOptions _options;
		private readonly RpcRouteTable _routes;
		private readonly IServiceScopeFactory _scopes;
		private readonly RealtimeSessionRegistry _registry;
		private readonly ILogger _logger;
		private readonly Channel<RpcFrame> _requests;
		private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
		private readonly CancellationTokenSource _connection;
		private readonly CancellationTokenSource _dispatch = new CancellationTokenSource();
		private readonly CancellationTokenSource _callsAborted = new CancellationTokenSource();
		private readonly CancellationTokenSource _closed = new CancellationTokenSource();
		private readonly object _gate = new object();

		private long _lastReceived;
		private string _userId;
		private int _closing;

		public RealtimeServerSession(WebSocket socket, IRpcSerializer serializer, string subProtocol, RealtimeServerOptions options,
			RpcRouteTable routes, IServiceScopeFactory scopes, RealtimeSessionRegistry registry, ILogger logger, CancellationToken requestAborted)
		{
			_socket = socket;
			_serializer = serializer;
			_options = options;
			_routes = routes;
			_scopes = scopes;
			_registry = registry;
			_logger = logger;
			_connection = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);

			// 연결이 어떻게 끝나든(요청 중단, 보내기 실패, 종료 시간 초과) 받기 루프 정리를 기다리지 않고 바로
			// 공개 호출 토큰을 취소 상태로 만든 뒤 처리를 멈춘다(그 사이 늦은 로그인이 끼지 않게)
			_connection.Token.Register(StopCalls);
			_requests = Channel.CreateBounded<RpcFrame>(new BoundedChannelOptions(options.MaxQueuedRequests)
			{
				SingleReader = true,
				SingleWriter = true,
				FullMode = BoundedChannelFullMode.Wait
			});
			_lastReceived = Environment.TickCount64;

			Id = Guid.NewGuid().ToString("N");
			SubProtocol = subProtocol;
		}

		public string Id { get; }

		public string UserId
		{
			get
			{
				lock (_gate)
				{
					return _userId;
				}
			}
		}

		public bool IsAuthenticated => UserId != null;

		public string SubProtocol { get; }

		public IDictionary<string, object> Items { get; } = new ConcurrentDictionary<string, object>();

		public CancellationToken Closed => _closed.Token;

		/// <summary>RPC 를 더 실행하지 않을 때(종료 시작, 연결 끝) 취소됩니다. 호출의 <see cref="IRpcContext.CallAborted"/> 입니다.</summary>
		internal CancellationToken CallsAborted => _callsAborted.Token;

		/// <summary>처리를 기다리는 요청 수입니다(테스트용).</summary>
		internal int QueuedRequests => _requests.Reader.Count;

		public async Task RunAsync()
		{
			var dispatch = DispatchLoopAsync();
			var idle = IdleLoopAsync();

			try
			{
				await ReceiveLoopAsync().ConfigureAwait(false);
			}
			finally
			{
				_requests.Writer.TryComplete();
				StopCalls();
				_connection.Cancel();
				SignalExternally(_closed);

				// 처리 루프는 진행 중인 호출을 기다리지 않고 곧 끝난다
				await Task.WhenAll(dispatch, idle).ConfigureAwait(false);

				// 읽는 쪽(처리 루프)이 끝났으니 남은 요청을 비운다. 놓아 준 호출이 세션을 붙잡아도 요청 본문은 남지 않는다
				while (_requests.Reader.TryRead(out _))
				{
				}
			}
		}

		public async Task<bool> PushAsync<T>(RpcTopic topic, T message, CancellationToken cancellationToken = default)
		{
			var payload = _serializer.Serialize(message);
			return await SendFrameAsync(RpcFrame.Push(topic.Id, new ArraySegment<byte>(payload)), cancellationToken).ConfigureAwait(false);
		}

		/// <exception cref="ArgumentOutOfRangeException">종료 메시지에 실을 수 없는 코드(1005, 1006, 1015 등)인 경우</exception>
		public async Task CloseAsync(int closeStatus)
		{
			if (!RealtimeCloseCodes.CanSend(closeStatus))
			{
				throw new ArgumentOutOfRangeException(nameof(closeStatus), closeStatus, "Close status cannot be sent in a close frame");
			}

			if (Interlocked.Exchange(ref _closing, 1) == 1)
			{
				return;
			}

			// 종료가 시작되면 남은 요청을 실행하지 않고 진행 중인 호출에 취소를 알린다
			StopCalls();

			try
			{
				using (var timeout = new CancellationTokenSource(_options.CloseTimeout))
				{
					await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
					try
					{
						if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
						{
							await _socket.CloseOutputAsync((WebSocketCloseStatus)closeStatus, null, timeout.Token).ConfigureAwait(false);
						}
					}
					finally
					{
						_sendLock.Release();
					}
				}
			}
			catch (Exception exception) when (IsConnectionException(exception))
			{
				// 닫기는 최선 노력이다
			}
			finally
			{
				// 클라이언트가 종료에 답하지 않아도 일정 시간 뒤 세션을 끝낸다
				_connection.CancelAfter(_options.CloseTimeout);
			}
		}

		/// <exception cref="OperationCanceledException">세션이 닫히기 시작한 경우(늦게 끝난 로그인이 사용자 표를 바꾸지 않게)</exception>
		internal void SignIn(string userId)
		{
			if (string.IsNullOrEmpty(userId))
			{
				throw new ArgumentException("User id must not be empty", nameof(userId));
			}

			RealtimeServerSession previous;

			lock (_gate)
			{
				if (_userId == userId)
				{
					return;
				}

				if (_userId != null)
				{
					throw new InvalidOperationException($"Realtime session {Id} is already signed in as another user");
				}

				// 사용자 ID 를 먼저 남겨야 레지스트리 제거가 이 세션의 사용자 칸을 찾는다. 거절되면 되돌린다
				_userId = userId;
				try
				{
					previous = _registry.SignIn(this, userId);
				}
				catch
				{
					_userId = null;
					throw;
				}
			}

			if (previous != null)
			{
				_logger.LogInformation("User {UserId} signed in on session {SessionId}; closing previous session {PreviousSessionId}",
					userId, Id, previous.Id);
				_ = previous.CloseAsync(RealtimeCloseCodes.DuplicateLogin);
			}
		}

		private async Task ReceiveLoopAsync()
		{
			var buffer = new byte[ReceiveBufferSize];
			var token = _connection.Token;

			try
			{
				while (true)
				{
					var (closed, message) = await ReceiveMessageAsync(buffer, token).ConfigureAwait(false);
					if (closed)
					{
						return;
					}

					// 거부한 메시지이거나 닫는 중이면 클라이언트의 종료 답을 기다리며 버린다
					if (message == null || Volatile.Read(ref _closing) == 1)
					{
						continue;
					}

					Interlocked.Exchange(ref _lastReceived, Environment.TickCount64);

					if (!RpcFrameCodec.TryDecode(new ArraySegment<byte>(message), out var frame))
					{
						await CloseAsync(RealtimeCloseCodes.ProtocolError).ConfigureAwait(false);
						continue;
					}

					switch (frame.Kind)
					{
						case RpcFrameKind.Request:
							if (!_requests.Writer.TryWrite(frame))
							{
								// 대기열이 가득 찼다. 읽기를 멈추면 핑도 못 받으므로 이 요청만 바로 거절한다
								await SendFrameAsync(RpcFrame.Response(frame.RequestId, RpcStatusCodes.Overloaded, new ArraySegment<byte>(NoBody)))
									.ConfigureAwait(false);
							}

							break;
						case RpcFrameKind.Ping:
							await SendFrameAsync(RpcFrame.Pong(frame.Sequence)).ConfigureAwait(false);
							break;
						case RpcFrameKind.Pong:
							break;
						default:
							await CloseAsync(RealtimeCloseCodes.ProtocolError).ConfigureAwait(false);
							break;
					}
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				// 세션을 끝냈다
			}
			catch (Exception exception) when (IsConnectionException(exception))
			{
				_logger.LogDebug(exception, "Realtime session {SessionId} connection ended", Id);
			}
		}

		/// <summary>
		/// 메시지 하나를 받습니다. 종료 프레임이면 같은 코드로 답하고 <c>Closed</c> 입니다. 텍스트·크기 초과는 그 코드로 닫고 메시지 없이 돌아옵니다.
		/// </summary>
		private async Task<(bool Closed, byte[] Message)> ReceiveMessageAsync(byte[] buffer, CancellationToken token)
		{
			using (var message = new MemoryStream())
			{
				while (true)
				{
					var received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

					if (received.MessageType == WebSocketMessageType.Close)
					{
						// 클라이언트가 닫았거나 서버 종료에 답했다. 받은 코드를 그대로 돌려준다
						var status = (int?)received.CloseStatus ?? RealtimeCloseCodes.Normal;
						await CloseAsync(RealtimeCloseCodes.CanSend(status) ? status : RealtimeCloseCodes.Normal).ConfigureAwait(false);
						return (true, null);
					}

					if (received.MessageType != WebSocketMessageType.Binary)
					{
						await CloseAsync(RealtimeCloseCodes.UnsupportedData).ConfigureAwait(false);
						return (false, null);
					}

					if (message.Length + received.Count > _options.MaxMessageSize)
					{
						await CloseAsync(RealtimeCloseCodes.MessageTooBig).ConfigureAwait(false);
						return (false, null);
					}

					message.Write(buffer, 0, received.Count);

					if (received.EndOfMessage)
					{
						return (false, message.ToArray());
					}
				}
			}
		}

		private async Task DispatchLoopAsync()
		{
			// 이 루프는 내부 신호(_dispatch)만 기다린다. 호출에 넘기는 공개 토큰에는 사용자 콜백이 걸릴 수 있다
			var stop = _dispatch.Token;
			var callAborted = _callsAborted.Token;

			try
			{
				while (await _requests.Reader.WaitToReadAsync(stop).ConfigureAwait(false))
				{
					while (!stop.IsCancellationRequested && !callAborted.IsCancellationRequested && _requests.Reader.TryRead(out var frame))
					{
						// 스레드 풀에서 시작해, 구현이 Task 를 돌려주기 전에 동기로 막혀도 이 루프와 연결 정리가 멈추지 않게 한다
						var invocation = Task.Run(() => InvokeAsync(frame, callAborted), CancellationToken.None);
						(int Status, byte[] Body) result;

						try
						{
							result = await invocation.WaitAsync(stop).ConfigureAwait(false);
						}
						catch (OperationCanceledException) when (stop.IsCancellationRequested || callAborted.IsCancellationRequested)
						{
							// 진행 중인 호출은 자기 DI 범위를 들고 끝까지 돈다. 연결 정리는 그 호출을 기다리지 않는다
							Abandon(invocation);
							return;
						}

						await SendFrameAsync(RpcFrame.Response(frame.RequestId, result.Status, new ArraySegment<byte>(result.Body))).ConfigureAwait(false);
					}
				}
			}
			catch (OperationCanceledException) when (stop.IsCancellationRequested)
			{
				// 세션이 닫히기 시작했다
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Realtime session {SessionId} dispatch loop failed", Id);
				await CloseAsync((int)WebSocketCloseStatus.InternalServerError).ConfigureAwait(false);
			}
		}

		private async Task<(int Status, byte[] Body)> InvokeAsync(RpcFrame frame, CancellationToken token)
		{
			// 스레드 풀에서 시작하기 전에 세션이 닫히기 시작했으면 실행하지 않는다
			token.ThrowIfCancellationRequested();

			if (!_routes.TryGet(frame.MethodId, out var route))
			{
				_logger.LogDebug("Realtime session {SessionId} called unknown method {MethodId}", Id, frame.MethodId);
				return (RpcStatusCodes.MethodNotFound, NoBody);
			}

			if (!route.AllowAnonymous && !IsAuthenticated)
			{
				return (RpcStatusCodes.Unauthenticated, NoBody);
			}

			var scope = _scopes.CreateAsyncScope();
			await using (scope.ConfigureAwait(false))
			{
				scope.ServiceProvider.GetRequiredService<RpcCallContext>().Initialize(this, route, frame.RequestId, token);

				try
				{
					var body = await route.InvokeAsync(scope.ServiceProvider, frame.Payload, _serializer, token).ConfigureAwait(false);
					return (RpcStatusCodes.Success, body);
				}
				catch (RpcStatusException exception)
				{
					return (exception.Status, NoBody);
				}
				catch (RpcBadRequestException exception)
				{
					_logger.LogDebug(exception, "Realtime RPC {Route} received an unreadable request", route.Name);
					return (RpcStatusCodes.BadRequest, NoBody);
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Realtime RPC {Route} failed", route.Name);
					return (RpcStatusCodes.InternalError, NoBody);
				}
			}
		}

		private void Abandon(Task invocation)
		{
			// 늦게 끝난 호출의 실패도 관찰해 둔다(InvokeAsync 는 취소 말고는 예외를 밖으로 내지 않음)
			invocation.ContinueWith(task => _logger.LogDebug(task.Exception, "Abandoned realtime RPC on session {SessionId} faulted", Id),
				CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
		}

		private async Task IdleLoopAsync()
		{
			var token = _connection.Token;
			var limit = (long)_options.IdleTimeout.TotalMilliseconds;
			var interval = TimeSpan.FromMilliseconds(Math.Clamp(limit / 4, 10, 5000));

			try
			{
				while (true)
				{
					await Task.Delay(interval, token).ConfigureAwait(false);

					if (Environment.TickCount64 - Interlocked.Read(ref _lastReceived) > limit)
					{
						_logger.LogInformation("Closing idle realtime session {SessionId}", Id);
						await CloseAsync(RealtimeCloseCodes.GoingAway).ConfigureAwait(false);
						return;
					}
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				// 세션을 끝냈다
			}
		}

		private async Task<bool> SendFrameAsync(RpcFrame frame, CancellationToken cancellationToken = default)
		{
			var bytes = RpcFrameCodec.Encode(frame);

			try
			{
				using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_connection.Token, cancellationToken))
				{
					await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
					try
					{
						if (_socket.State != WebSocketState.Open || Volatile.Read(ref _closing) == 1)
						{
							return false;
						}

						await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, linked.Token).ConfigureAwait(false);
						return true;
					}
					finally
					{
						_sendLock.Release();
					}
				}
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			catch (Exception exception) when (IsConnectionException(exception))
			{
				_logger.LogDebug(exception, "Realtime session {SessionId} send failed", Id);
				_connection.Cancel();
				return false;
			}
		}

		/// <summary>RPC 실행을 멈춥니다. 공개 호출 토큰을 먼저 취소 상태로 만들어 로그인 검사·시작 전 검사가 바로 보게 하고, 그다음 처리 루프를 멈춥니다.</summary>
		private void StopCalls()
		{
			SignalExternally(_callsAborted);
			_dispatch.Cancel();
		}

		/// <summary>
		/// 공개 토큰을 취소합니다. 토큰은 바로 취소 상태가 되고, 사용자가 건 콜백은 스레드 풀에서 돌며 실패는 로그로만 남깁니다
		/// (콜백이 던지거나 막혀도 종료·정리가 멈추지 않게).
		/// </summary>
		private void SignalExternally(CancellationTokenSource source)
		{
			if (source.IsCancellationRequested)
			{
				return;
			}

			source.CancelAsync().ContinueWith(
				task => _logger.LogWarning(task.Exception, "A cancellation callback on realtime session {SessionId} threw", Id),
				CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
		}

		private static bool IsConnectionException(Exception exception)
		{
			return exception is WebSocketException || exception is IOException || exception is ObjectDisposedException
			       || exception is OperationCanceledException;
		}
	}
}
