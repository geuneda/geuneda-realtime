using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Realtime;
using Geuneda.Realtime.Tests.Interop;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	/// <summary>
	/// 소스 생성기가 Unity 컴파일(Roslyn 4.3)에서 만든 <see cref="SampleRpcClient"/> 를 가짜 서버로 확인합니다.
	/// </summary>
	[TestFixture]
	public class GeneratedClientTest
	{
		private FakeRealtimeServer _server;
		private RealtimeClient _client;

		[SetUp]
		public void SetUp()
		{
			_server = new FakeRealtimeServer();
			_client = new RealtimeClient(new RealtimeClientOptions
			{
				ServerUri = new Uri("wss://game.example.com/rpc"),
				Transport = _server,
				CallbackDispatcher = action => action(),
				PingInterval = TimeSpan.Zero
			});
		}

		[TearDown]
		public void TearDown()
		{
			_client.Dispose();
		}

		[Test]
		public void GeneratedMethodIds_AreHashesOfInterfaceAndMethodNames()
		{
			Assert.AreEqual("ISampleRpc/Echo", SampleRpcMethods.Echo.Name);
			Assert.AreEqual(Fnv1a32.Hash("ISampleRpc/Echo"), SampleRpcMethods.Echo.Id);
			Assert.AreEqual(Fnv1a32.Hash("ISampleRpc/Recall"), SampleRpcMethods.Recall.Id);
			Assert.Throws<ArgumentNullException>(() => new SampleRpcClient(null));
		}

		[UnityTest]
		public IEnumerator GeneratedClient_CallsEveryMethodShape() => UniTask.ToCoroutine(async () =>
		{
			ISampleRpcClient sample = new SampleRpcClient(_client);
			_server.Respond(SampleRpcMethods.Echo, frame => (0, Encoding.UTF8.GetBytes("{\"Text\":\"hi\",\"UserId\":\"u1\"}")));
			_server.Respond(SampleRpcMethods.Remember, frame => (0, null));
			_server.Respond(SampleRpcMethods.Recall, frame => (0, Encoding.UTF8.GetBytes("{\"Note\":\"kept\"}")));
			_server.Respond(SampleRpcMethods.Fail, frame => (101007, null));

			Result<ISampleRpc.EchoResponse> echo = await sample.Echo(new ISampleRpc.EchoRequest { Text = "hi" });
			Result remember = await sample.Remember(new ISampleRpc.RememberRequest { Note = "kept" });
			Result<ISampleRpc.RememberedResponse> recall = await sample.Recall();
			Result<ISampleRpc.EchoResponse> fail = await sample.Fail(new ISampleRpc.FailRequest { Status = 101007 });

			Assert.AreEqual("hi", echo.Value.Text);
			Assert.AreEqual("u1", echo.Value.UserId);
			Assert.IsTrue(remember.IsSuccess);
			Assert.AreEqual("kept", recall.Value.Note);
			Assert.AreEqual(101007, fail.Code);
			Assert.AreEqual(SampleRpcMethods.Echo.Id, _server.ReceivedRequests[0].MethodId);
			StringAssert.Contains("\"Note\":\"kept\"", Encoding.UTF8.GetString(_server.ReceivedRequests[1].Payload.ToArray()));
			Assert.AreEqual(SampleRpcMethods.Recall.Id, _server.ReceivedRequests[2].MethodId);
		});
	}
}
