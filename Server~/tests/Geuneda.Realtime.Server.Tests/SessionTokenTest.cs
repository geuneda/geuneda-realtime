using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>연결 취소가 받기 루프 정리를 기다리지 않고 공개 호출 토큰에 바로 전해지는지 확인합니다.</summary>
	public class SessionTokenTest
	{
		[Fact]
		public async Task ConnectionAbort_StopsCallsBeforeReceiveLoopEnds()
		{
			var socket = new StuckWebSocket();
			var registry = new RealtimeSessionRegistry();
			var services = new ServiceCollection().AddScoped<RpcCallContext>().BuildServiceProvider();
			using var requestAborted = new CancellationTokenSource();
			var session = new RealtimeServerSession(socket, new JsonRpcSerializer(), socket.SubProtocol, new RealtimeServerOptions(),
				new RpcRouteTable(Array.Empty<RpcRoute>()), services.GetRequiredService<IServiceScopeFactory>(), registry,
				NullLogger.Instance, requestAborted.Token);
			registry.Add(session);
			var run = session.RunAsync();

			// 받기는 취소를 무시하고 붙잡혀 있어 RunAsync 의 정리는 아직 돌지 않는다
			requestAborted.Cancel();

			Assert.False(run.IsCompleted);
			Assert.True(session.CallsAborted.IsCancellationRequested);
			Assert.Throws<OperationCanceledException>(() => session.SignIn("user-1"));
			Assert.Null(registry.FindByUser("user-1"));
			Assert.Null(session.UserId);

			socket.Release();
			await run.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.True(session.Closed.IsCancellationRequested);
		}
	}
}
