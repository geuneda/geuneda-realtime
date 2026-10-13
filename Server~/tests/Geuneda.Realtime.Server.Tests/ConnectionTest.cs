using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Geuneda.Realtime.Server.Tests
{
	/// <summary>서브프로토콜 협상, 종료 코드, 무응답 종료를 확인합니다.</summary>
	public class ConnectionTest
	{
		[Fact]
		public async Task UnsupportedSubProtocol_IsAcceptedWithoutProtocolAndClosedWith1002()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await RawRpcClient.ConnectAsync(server.Uri, subProtocol: "geuneda.rpc.v1.xml");

			var closeStatus = await client.WaitForCloseAsync();

			Assert.Null(client.SubProtocol);
			Assert.Equal(WebSocketCloseStatus.ProtocolError, closeStatus);
		}

		[Fact]
		public async Task TextMessage_ClosesWith1003()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			await client.SendRawAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text);

			Assert.Equal(WebSocketCloseStatus.InvalidMessageType, await client.WaitForCloseAsync());
		}

		[Fact]
		public async Task OversizedMessage_ClosesWith1009()
		{
			await using var server = await RealtimeTestServer.StartAsync(options => options.MaxMessageSize = 1024);
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			await client.SendFrameAsync(RpcFrame.Request(1, Fnv1a32.Hash("ITestRpc/Login"), new ArraySegment<byte>(new byte[2000])));

			Assert.Equal(WebSocketCloseStatus.MessageTooBig, await client.WaitForCloseAsync());
		}

		[Fact]
		public async Task InvalidFrame_ClosesWith1002()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			await client.SendRawAsync(new byte[] { 99, 0, 0, 0, 0, 0 });

			Assert.Equal(WebSocketCloseStatus.ProtocolError, await client.WaitForCloseAsync());
		}

		[Fact]
		public async Task ClientOnlyFrameKindFromClient_ClosesWith1002()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			await client.SendFrameAsync(RpcFrame.Push(1, new ArraySegment<byte>(new byte[0])));

			Assert.Equal(WebSocketCloseStatus.ProtocolError, await client.WaitForCloseAsync());
		}

		[Fact]
		public async Task ClientClose_IsEchoedWithSameCode()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			await client.CloseAsync((WebSocketCloseStatus)4005);

			Assert.Equal(4005, (int)await client.WaitForCloseAsync());
		}

		[Fact]
		public async Task IdleSession_ClosesWith1001()
		{
			await using var server = await RealtimeTestServer.StartAsync(options => options.IdleTimeout = TimeSpan.FromMilliseconds(300));
			await using var client = await RawRpcClient.ConnectAsync(server.Uri);

			Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await client.WaitForCloseAsync());
		}

		[Fact]
		public async Task ClosedSession_LeavesRegistry()
		{
			await using var server = await RealtimeTestServer.StartAsync();
			var client = await RawRpcClient.ConnectAsync(server.Uri);
			await client.CallAsync<ITestRpc.LoginReq, ITestRpc.LoginRes>("ITestRpc/Login", new ITestRpc.LoginReq { UserId = "user-1" });

			await client.DisposeAsync();
			for (var i = 0; i < 100 && server.Sessions.All.Count > 0; i++)
			{
				await Task.Delay(20);
			}

			Assert.Empty(server.Sessions.All);
			Assert.Null(server.Sessions.FindByUser("user-1"));
		}
	}
}
