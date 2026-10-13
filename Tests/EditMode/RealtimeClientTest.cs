using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Realtime;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	[TestFixture]
	public class RealtimeClientTest
	{
		private sealed class LoginRequest
		{
			public string Token;
		}

		private sealed class EchoRequest
		{
			public string Text;
		}

		private sealed class EchoResponse
		{
			public string Text;
			public int Length;
		}

		private static readonly RpcMethod LoginMethod = RpcMethod.Of("ISessionRpc/Login");
		private static readonly RpcMethod EchoMethod = RpcMethod.Of("IEchoRpc/Echo");
		private static readonly RpcTopic NoticeTopic = RpcTopic.Of("Notice");

		private FakeRealtimeServer _server;
		private List<RealtimeDisconnectInfo> _disconnects;
		private List<Result> _reconnects;
		private int _logins;
		private bool _loginThrows;
		private int _mainThreadId;
		private RealtimeClient _client;

		[SetUp]
		public void SetUp()
		{
			_server = new FakeRealtimeServer();
			_disconnects = new List<RealtimeDisconnectInfo>();
			_reconnects = new List<Result>();
			_logins = 0;
			_loginThrows = false;
			_mainThreadId = Thread.CurrentThread.ManagedThreadId;

			_server.Respond(LoginMethod, frame => (0, null));
			_server.Respond(EchoMethod, frame =>
			{
				var request = new JsonRpcSerializer().Deserialize<EchoRequest>(frame.Payload);
				return (0, Encoding.UTF8.GetBytes($"{{\"Text\":\"{request.Text}\",\"Length\":{request.Text.Length}}}"));
			});
		}

		[TearDown]
		public void TearDown()
		{
			_client?.Dispose();
		}

		private RealtimeClient CreateClient(Action<RealtimeClientOptions> configure = null)
		{
			var options = new RealtimeClientOptions
			{
				ServerUri = new Uri("wss://game.example.com/rpc"),
				Transport = _server,
				CallbackDispatcher = action => action(),
				PingInterval = TimeSpan.Zero,
				ReconnectBaseDelay = TimeSpan.FromMilliseconds(10),
				ReconnectMaxDelay = TimeSpan.FromMilliseconds(20),
				LoginAsync = async (caller, cancellationToken) =>
				{
					_logins++;
					if (_loginThrows)
					{
						// 백그라운드 스레드에서 던져도 호출자는 메인 스레드에서 받아야 한다
						await UniTask.SwitchToThreadPool();
						throw new InvalidOperationException("login delegate bug");
					}

					return await caller.CallAsync(LoginMethod, new LoginRequest { Token = "token-" + _logins }, cancellationToken);
				}
			};

			configure?.Invoke(options);

			_client = new RealtimeClient(options);
			_client.Disconnected += info => _disconnects.Add(info);
			_client.Reconnected += result => _reconnects.Add(result);
			return _client;
		}

		private static async UniTask WaitUntil(Func<bool> condition)
		{
			// 배치 모드 EditMode 는 프레임이 매우 빨라 프레임 수가 아니라 시간으로 기다린다
			var clock = Stopwatch.StartNew();
			while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5))
			{
				await UniTask.Yield();
			}

			Assert.IsTrue(condition(), "condition was not met in time");
		}

		/// <summary>작업의 예외와, 그 예외를 받은 스레드를 기록합니다. 대기를 각각 따로 관찰하려면 작업을 시작한 직후 부릅니다.</summary>
		private static async UniTask<(Exception Error, int ThreadId)> CatchAsync<T>(UniTask<T> task)
		{
			try
			{
				await task;
				return (null, Thread.CurrentThread.ManagedThreadId);
			}
			catch (Exception exception)
			{
				return (exception, Thread.CurrentThread.ManagedThreadId);
			}
		}

		private int EchoRequestsReceived => _server.ReceivedRequests.Count(frame => frame.MethodId == EchoMethod.Id);

		[UnityTest]
		public IEnumerator Connect_UsesSubProtocolAndLogsInWithFirstMessage() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();

			var result = await client.ConnectAsync();

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(RealtimeState.Connected, client.State);
			Assert.AreEqual("geuneda.rpc.v1.json", _server.LastSubProtocol);
			Assert.AreEqual(1, _server.ReceivedRequests.Count);
			Assert.AreEqual(LoginMethod.Id, _server.ReceivedRequests[0].MethodId);
			StringAssert.Contains("token-1", Encoding.UTF8.GetString(_server.ReceivedRequests[0].Payload.ToArray()));
		});

		[UnityTest]
		public IEnumerator ConcurrentConnects_ShareOneAttempt() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_server.ConnectGate = new UniTaskCompletionSource();

			var first = client.ConnectAsync();
			var second = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "hi" });
			Assert.AreEqual(RealtimeState.Connecting, client.State);
			_server.ConnectGate.TrySetResult();

			var (connected, echoed) = await UniTask.WhenAll(first, second);

			Assert.IsTrue(connected.IsSuccess);
			Assert.AreEqual("hi", echoed.Value.Text);
			Assert.AreEqual(1, _server.ConnectCalls);
			Assert.AreEqual(1, _logins);
		});

		[UnityTest]
		public IEnumerator Call_ReturnsDeserializedResponse() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();

			var result = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "geuneda" });

			Assert.AreEqual("geuneda", result.Value.Text);
			Assert.AreEqual(7, result.Value.Length);
			Assert.AreEqual(EchoMethod.Id, _server.ReceivedRequests[1].MethodId);
		});

		[UnityTest]
		public IEnumerator Call_ServerStatus_BecomesFailureCode() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_server.Respond(EchoMethod, frame => (100042, null));

			var failed = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			_server.Respond(EchoMethod, frame => (-5, null));
			var negative = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			_server.Respond(EchoMethod, frame => (ResultCodes.NetworkError, null));
			var reserved = await client.CallAsync<EchoRequest>(EchoMethod, new EchoRequest { Text = "x" });

			Assert.AreEqual(100042, failed.Code);
			Assert.AreEqual(RealtimeResultCodes.InvalidResponse, negative.Code);
			Assert.AreEqual(RealtimeResultCodes.InvalidResponse, reserved.Code);
		});

		[UnityTest]
		public IEnumerator Call_InvalidBody_IsInvalidResponse() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_server.Respond(EchoMethod, frame => (0, Encoding.UTF8.GetBytes("{broken")));

			var result = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });

			Assert.AreEqual(RealtimeResultCodes.InvalidResponse, result.Code);
		});

		[UnityTest]
		public IEnumerator Call_NoResponse_TimesOutAndDropsLateResponse() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options => options.RequestTimeout = TimeSpan.FromMilliseconds(100));
			RpcFrame? pending = null;
			_server.Respond(EchoMethod, frame =>
			{
				pending = frame;
				return null;
			});

			var result = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			_server.Current.ServerSend(RpcFrame.Response(pending.Value.RequestId, 0, new ArraySegment<byte>(Encoding.UTF8.GetBytes("{}"))));
			_server.Respond(EchoMethod, frame => (0, Encoding.UTF8.GetBytes("{\"Text\":\"next\"}")));
			var next = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "y" });

			Assert.AreEqual(ResultCodes.Timeout, result.Code);
			Assert.AreEqual("next", next.Value.Text);
			Assert.AreEqual(1, _server.Connections.Count);
		});

		[UnityTest]
		public IEnumerator Call_CallerCancel_IsCancelled() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();
			_server.Respond(EchoMethod, frame => null);

			using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
			{
				var result = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" }, cancellation.Token);

				Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			}
		});

		[UnityTest]
		public IEnumerator Push_DeliveredToTopicSubscribersUntilDisposed() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();
			var received = new List<string>();
			var observed = new List<string>();
			var subscription = client.Subscribe<EchoResponse>(NoticeTopic, notice => received.Add(notice.Text));
			client.Subscribe<EchoResponse>(NoticeTopic, notice => observed.Add(notice.Text));
			client.Subscribe<EchoResponse>(RpcTopic.Of("Other"), notice => received.Add("other"));

			_server.Current.ServerSend(RpcFrame.Push(NoticeTopic.Id, new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"Text\":\"one\"}"))));
			_server.Current.ServerSend(RpcFrame.Push(NoticeTopic.Id, new ArraySegment<byte>(Encoding.UTF8.GetBytes("{bad"))));
			await WaitUntil(() => received.Count == 1);
			subscription.Dispose();
			subscription.Dispose();
			_server.Current.ServerSend(RpcFrame.Push(NoticeTopic.Id, new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"Text\":\"two\"}"))));
			await WaitUntil(() => observed.Count == 2);

			CollectionAssert.AreEqual(new[] { "one" }, received);
			CollectionAssert.AreEqual(new[] { "one", "two" }, observed);
			Assert.AreEqual(RealtimeState.Connected, client.State);
		});

		[UnityTest]
		public IEnumerator ServerClose_Reconnectable_FailsPendingAndReconnectsWithNewLogin() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();
			_server.Respond(EchoMethod, frame => null);

			var pending = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart");
			var result = await pending;
			await WaitUntil(() => _reconnects.Count == 1);

			Assert.AreEqual(ResultCodes.NetworkError, result.Code);
			Assert.AreEqual(1, _disconnects.Count);
			Assert.IsTrue(_disconnects[0].WillReconnect);
			Assert.AreEqual(RealtimeCloseCodes.GoingAway, _disconnects[0].CloseStatus);
			Assert.IsTrue(_reconnects[0].IsSuccess);
			Assert.AreEqual(2, _logins);
			Assert.AreEqual(2, _server.Connections.Count);
			Assert.AreEqual(RealtimeState.Connected, client.State);
		});

		[UnityTest]
		public IEnumerator ServerClose_Policy_StopsUntilConnectAsync() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();

			_server.Current.ServerClose(RealtimeCloseCodes.DuplicateLogin, "other device");
			await WaitUntil(() => _disconnects.Count == 1);
			var blocked = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			var reconnected = await client.ConnectAsync();

			Assert.IsFalse(_disconnects[0].WillReconnect);
			Assert.AreEqual(RealtimeResultCodes.NotReconnectable, blocked.Code);
			Assert.AreEqual(2, _server.ConnectCalls);
			Assert.IsTrue(reconnected.IsSuccess);
			CollectionAssert.IsEmpty(_reconnects);
		});

		[UnityTest]
		public IEnumerator LoginRejected_FailsConnectWithServerStatusAndClosesConnection() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_server.Respond(LoginMethod, frame => (100500, null));

			var result = await client.ConnectAsync();

			Assert.AreEqual(100500, result.Code);
			Assert.AreEqual(RealtimeState.Disconnected, client.State);
			Assert.IsTrue(_server.Current.Disposed);
			Assert.AreEqual(RealtimeCloseCodes.Normal, _server.Current.ClientCloseStatus);
		});

		[UnityTest]
		public IEnumerator ConnectFailure_IsNetworkErrorAndReconnectGivesUpAfterMaxAttempts() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options => options.MaxReconnectAttempts = 2);
			await client.ConnectAsync();

			_server.FailNextConnects = 10;
			_server.Current.ServerClose(RealtimeCloseCodes.Abnormal, null);
			await WaitUntil(() => _reconnects.Count == 1);
			var direct = await client.ConnectAsync();

			Assert.AreEqual(ResultCodes.NetworkError, _reconnects[0].Code);
			Assert.AreEqual(1 + 2 + 1, _server.ConnectCalls);
			Assert.AreEqual(ResultCodes.NetworkError, direct.Code);
		});

		[UnityTest]
		public IEnumerator PingTimeout_DisconnectsAndReconnects() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options =>
			{
				options.PingInterval = TimeSpan.FromMilliseconds(30);
				options.PingTimeout = TimeSpan.FromMilliseconds(30);
			});
			await client.ConnectAsync();

			_server.AutoPong = false;
			await WaitUntil(() => _disconnects.Count == 1);
			_server.AutoPong = true;
			await WaitUntil(() => _reconnects.Count == 1);

			Assert.AreEqual(RealtimeCloseCodes.Abnormal, _disconnects[0].CloseStatus);
			StringAssert.Contains("Ping", _disconnects[0].Reason);
			Assert.IsTrue(_reconnects[0].IsSuccess);
			Assert.AreEqual(RealtimeCloseCodes.GoingAway, _server.Connections[0].ClientCloseStatus);
		});

		[UnityTest]
		public IEnumerator HealthyPingPong_StaysConnected() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options =>
			{
				options.PingInterval = TimeSpan.FromMilliseconds(20);
				options.PingTimeout = TimeSpan.FromMilliseconds(200);
			});
			await client.ConnectAsync();

			await UniTask.Delay(300, true);

			CollectionAssert.IsEmpty(_disconnects);
			Assert.AreEqual(RealtimeState.Connected, client.State);
		});

		[UnityTest]
		public IEnumerator Dispose_EndsPendingCallsAsClosedAndRejectsNewCalls() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();
			_server.Respond(EchoMethod, frame => null);

			var pending = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			client.Dispose();
			var result = await pending;

			Assert.AreEqual(RealtimeResultCodes.Closed, result.Code);
			Assert.AreEqual(RealtimeCloseCodes.Normal, _server.Current.ClientCloseStatus);
			Assert.Throws<ObjectDisposedException>(() => client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest()).GetAwaiter().GetResult());
			Assert.Throws<ObjectDisposedException>(() => client.Subscribe<EchoResponse>(NoticeTopic, notice => { }));
			CollectionAssert.IsEmpty(_disconnects);
		});

		[UnityTest]
		public IEnumerator Dispose_WhileConnecting_EndsConnectAsClosed() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_server.ConnectGate = new UniTaskCompletionSource();

			var connecting = client.ConnectAsync();
			client.Dispose();
			var result = await connecting;

			Assert.AreEqual(RealtimeResultCodes.Closed, result.Code);
		});

		[UnityTest]
		public IEnumerator ConnectOnCallOff_IsNotConnected() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options => options.ConnectOnCall = false);

			var result = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });

			Assert.AreEqual(RealtimeResultCodes.NotConnected, result.Code);
			Assert.AreEqual(0, _server.ConnectCalls);
		});

		[UnityTest]
		public IEnumerator InvalidFrameFromServer_DisconnectsAsProtocolError() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();

			_server.Current.ServerSendRaw(new byte[] { 99, 0, 0, 0, 0, 0 });
			await WaitUntil(() => _disconnects.Count == 1);

			Assert.AreEqual(RealtimeCloseCodes.ProtocolError, _disconnects[0].CloseStatus);
			Assert.AreEqual(RealtimeCloseCodes.ProtocolError, _server.Connections[0].ClientCloseStatus);
		});

		[UnityTest]
		public IEnumerator Call_CancelledWhileWaitingToSend_IsNeverSent() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();
			var gate = new UniTaskCompletionSource();
			_server.Current.SendGate = gate;

			var first = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "first" });
			using (var cancellation = new CancellationTokenSource())
			{
				var second = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "second" }, cancellation.Token);
				cancellation.Cancel();
				var cancelled = await second;
				_server.Current.SendGate = null;
				gate.TrySetResult();
				var sent = await first;

				Assert.AreEqual(ResultCodes.Cancelled, cancelled.Code);
				Assert.AreEqual("first", sent.Value.Text);
				Assert.AreEqual(1, EchoRequestsReceived);
			}
		});

		[UnityTest]
		public IEnumerator Call_BlockedSend_TimesOutAndDisposeEndsQueuedCallAsClosed() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options => options.RequestTimeout = TimeSpan.FromMilliseconds(100));
			await client.ConnectAsync();
			_server.Current.SendGate = new UniTaskCompletionSource();

			var timedOut = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "a" });
			var queued = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "b" });
			client.Dispose();
			var closed = await queued;

			Assert.AreEqual(ResultCodes.Timeout, timedOut.Code);
			Assert.AreEqual(RealtimeResultCodes.Closed, closed.Code);
			Assert.AreEqual(0, EchoRequestsReceived);
		});

		[UnityTest]
		public IEnumerator LoginThrows_PropagatesToAllWaitersAndClosesConnection() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_loginThrows = true;
			_server.ConnectGate = new UniTaskCompletionSource();

			var connecting = CatchAsync(client.ConnectAsync());
			var calling = CatchAsync(client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" }));
			_server.ConnectGate.TrySetResult();
			var (connectError, connectErrorThreadId) = await connecting;
			var (callError, callErrorThreadId) = await calling;
			_server.ConnectGate = null;
			_loginThrows = false;
			var recovered = await client.ConnectAsync();

			Assert.IsInstanceOf<InvalidOperationException>(connectError);
			Assert.IsInstanceOf<InvalidOperationException>(callError);
			Assert.AreEqual(_mainThreadId, connectErrorThreadId);
			Assert.AreEqual(_mainThreadId, callErrorThreadId);
			Assert.IsTrue(_server.Connections[0].Disposed);
			Assert.AreEqual(RealtimeCloseCodes.Normal, _server.Connections[0].ClientCloseStatus);
			Assert.IsTrue(recovered.IsSuccess);
			Assert.AreEqual(2, _server.Connections.Count);
		});

		[UnityTest]
		public IEnumerator PolicyCloseDuringLogin_IsNotReconnectableAndBlocksAutoConnect() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			_server.Respond(LoginMethod, frame =>
			{
				_server.Current.ServerClose(RealtimeCloseCodes.Maintenance, "maintenance");
				return null;
			});

			var result = await client.ConnectAsync();
			var call = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });

			Assert.AreEqual(RealtimeResultCodes.NotReconnectable, result.Code);
			Assert.AreEqual(RealtimeResultCodes.NotReconnectable, call.Code);
			Assert.AreEqual(1, _server.ConnectCalls);
			Assert.AreEqual(RealtimeCloseCodes.Maintenance, _server.Current.ClientCloseStatus);
			CollectionAssert.IsEmpty(_disconnects);
		});

		[UnityTest]
		public IEnumerator PolicyCloseDuringReconnectLogin_StopsReconnecting() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient();
			await client.ConnectAsync();
			_server.Respond(LoginMethod, frame =>
			{
				_server.Current.ServerClose(RealtimeCloseCodes.DuplicateLogin, "other device");
				return null;
			});

			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart");
			await WaitUntil(() => _reconnects.Count == 1);
			var call = await client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });

			Assert.IsTrue(_disconnects.Single().WillReconnect);
			Assert.AreEqual(RealtimeResultCodes.NotReconnectable, _reconnects[0].Code);
			Assert.AreEqual(RealtimeResultCodes.NotReconnectable, call.Code);
			Assert.AreEqual(2, _server.ConnectCalls);
		});

		[UnityTest]
		public IEnumerator ConnectRightAfterFailedCall_OpensNewConnection() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options =>
			{
				options.ReconnectBaseDelay = TimeSpan.FromSeconds(2);
				options.ReconnectMaxDelay = TimeSpan.FromSeconds(2);
			});
			await client.ConnectAsync();
			_server.Respond(EchoMethod, frame => null);

			var pending = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart");
			var failed = await pending;
			var reconnected = await client.ConnectAsync();

			Assert.AreEqual(ResultCodes.NetworkError, failed.Code);
			Assert.IsTrue(reconnected.IsSuccess);
			Assert.AreEqual(2, _server.ConnectCalls);
			Assert.AreEqual(2, _logins);
			Assert.AreEqual(RealtimeState.Connected, client.State);
		});

		[UnityTest]
		public IEnumerator CloseWhileReconnecting_ReportsWillReconnectAndRecovers() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options =>
			{
				options.ReconnectBaseDelay = TimeSpan.FromMilliseconds(300);
				options.ReconnectMaxDelay = TimeSpan.FromMilliseconds(300);
			});
			await client.ConnectAsync();

			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart");
			await WaitUntil(() => _disconnects.Count == 1);
			await client.ConnectAsync();
			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart again");
			await WaitUntil(() => _disconnects.Count == 2);
			await WaitUntil(() => client.State == RealtimeState.Connected && _reconnects.Count >= 1);

			Assert.IsTrue(_disconnects[1].WillReconnect);
			Assert.AreEqual(3, _server.ConnectCalls);
			Assert.IsTrue(_reconnects.All(result => result.IsSuccess));
		});

		[UnityTest]
		public IEnumerator QueuedPush_IsDroppedAfterUnsubscribeOrDispose() => UniTask.ToCoroutine(async () =>
		{
			var queued = new List<Action>();
			var client = CreateClient(options => options.CallbackDispatcher = queued.Add);
			await client.ConnectAsync();
			var received = new List<string>();
			var subscription = client.Subscribe<EchoResponse>(NoticeTopic, notice => received.Add("unsubscribed"));
			client.Subscribe<EchoResponse>(NoticeTopic, notice => received.Add("kept"));
			var push = RpcFrame.Push(NoticeTopic.Id, new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"Text\":\"one\"}")));

			_server.Current.ServerSend(push);
			await WaitUntil(() => queued.Count == 2);
			subscription.Dispose();
			queued.ForEach(action => action());
			_server.Current.ServerSend(push);
			await WaitUntil(() => queued.Count == 3);
			client.Dispose();
			queued[2]();

			CollectionAssert.AreEqual(new[] { "kept" }, received);
		});

		[UnityTest]
		public IEnumerator LoginIgnoringToken_TimesOutBeforeLoginEndsAndIsNotPublishedLater() => UniTask.ToCoroutine(async () =>
		{
			var login = new UniTaskCompletionSource<Result>();
			var client = CreateClient(options =>
			{
				options.ConnectTimeout = TimeSpan.FromMilliseconds(50);
				options.LoginAsync = (caller, cancellationToken) => login.Task;
			});
			var clock = Stopwatch.StartNew();

			var result = await client.ConnectAsync();
			var elapsed = clock.Elapsed;
			login.TrySetResult(Result.Ok());
			await UniTask.Yield();
			await UniTask.Yield();

			Assert.AreEqual(ResultCodes.Timeout, result.Code);
			Assert.Less(elapsed, TimeSpan.FromSeconds(2), "connect ends while the login is still pending");
			Assert.AreEqual(RealtimeState.Disconnected, client.State);
			Assert.IsTrue(_server.Current.Disposed);
		});

		[UnityTest]
		public IEnumerator Dispose_WhileLoginHangs_EndsConnectAsClosed() => UniTask.ToCoroutine(async () =>
		{
			var client = CreateClient(options => options.LoginAsync = (caller, cancellationToken) => new UniTaskCompletionSource<Result>().Task);

			var connecting = client.ConnectAsync();
			client.Dispose();
			var result = await connecting;

			Assert.AreEqual(RealtimeResultCodes.Closed, result.Code);
			Assert.IsTrue(_server.Current.Disposed);
		});

		[UnityTest]
		public IEnumerator QueuedConnectionEvents_AreDroppedAfterDispose() => UniTask.ToCoroutine(async () =>
		{
			var queued = new List<Action>();
			var client = CreateClient(options => options.CallbackDispatcher = queued.Add);
			await client.ConnectAsync();

			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart");
			await WaitUntil(() => queued.Count == 2);
			client.Dispose();
			queued.ForEach(action => action());

			CollectionAssert.IsEmpty(_disconnects);
			CollectionAssert.IsEmpty(_reconnects);
		});

		[UnityTest]
		public IEnumerator DispatcherFailure_DoesNotBreakCleanupOrReconnect() => UniTask.ToCoroutine(async () =>
		{
			// 디스패처 실패는 오류 로그로 남는다
			LogAssert.ignoreFailingMessages = true;
			var client = CreateClient(options => options.CallbackDispatcher = action => throw new ObjectDisposedException("queue"));
			await client.ConnectAsync();
			_server.Respond(EchoMethod, frame => null);

			var pending = client.CallAsync<EchoRequest, EchoResponse>(EchoMethod, new EchoRequest { Text = "x" });
			_server.Current.ServerClose(RealtimeCloseCodes.GoingAway, "restart");
			var failed = await pending;
			await WaitUntil(() => client.State == RealtimeState.Connected && _server.ConnectCalls == 2);

			Assert.AreEqual(ResultCodes.NetworkError, failed.Code);
			Assert.IsTrue(_server.Connections[0].Disposed);
		});

		[Test]
		public void Options_AreValidated()
		{
			Assert.Throws<ArgumentNullException>(() => new RealtimeClient(null));
			Assert.Throws<ArgumentNullException>(() => new RealtimeClient(new RealtimeClientOptions()));
			Assert.Throws<ArgumentException>(() => new RealtimeClient(new RealtimeClientOptions { ServerUri = new Uri("https://example.com") }));
			Assert.Throws<ArgumentException>(() => new RealtimeClient(new RealtimeClientOptions
			{
				ServerUri = new Uri("wss://example.com"), RequestTimeout = TimeSpan.Zero
			}));
			Assert.Throws<ArgumentException>(() => new RealtimeClient(new RealtimeClientOptions
			{
				ServerUri = new Uri("wss://example.com"), ReconnectBaseDelay = TimeSpan.FromSeconds(10), ReconnectMaxDelay = TimeSpan.FromSeconds(1)
			}));
		}

		[Test]
		public void ReconnectDelay_GrowsExponentiallyWithJitterUpToMax()
		{
			var client = CreateClient(options =>
			{
				options.ReconnectBaseDelay = TimeSpan.FromSeconds(1);
				options.ReconnectMaxDelay = TimeSpan.FromSeconds(30);
			});

			for (var attempt = 0; attempt < 10; attempt++)
			{
				var cap = Math.Min(30, Math.Pow(2, attempt));
				var delay = client.GetReconnectDelay(attempt).TotalSeconds;

				Assert.GreaterOrEqual(delay, cap * 0.5, $"attempt {attempt}");
				Assert.LessOrEqual(delay, cap, $"attempt {attempt}");
			}
		}
	}
}
