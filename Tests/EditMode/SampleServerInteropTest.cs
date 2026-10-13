using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
	/// 실제 .NET 샘플 서버(<c>Server~/samples/Geuneda.Realtime.SampleServer</c>)와의 상호 운용을 확인합니다.
	/// 서버를 띄우고 <c>GEUNEDA_REALTIME_INTEROP_URL</c>(예: <c>ws://127.0.0.1:5199/rpc</c>)을 설정한 뒤 이름으로 골라 실행합니다.
	/// </summary>
	[TestFixture]
	[Explicit("샘플 서버가 필요합니다")]
	[Category("Interop")]
	public class SampleServerInteropTest
	{
		private const string UrlVariable = "GEUNEDA_REALTIME_INTEROP_URL";

		private static async UniTask WaitUntil(Func<bool> condition)
		{
			var clock = Stopwatch.StartNew();
			while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5))
			{
				await UniTask.Yield();
			}

			Assert.IsTrue(condition(), "condition was not met in time");
		}

		[UnityTest]
		public IEnumerator SampleServer_LoginCallsStatusAndPush() => UniTask.ToCoroutine(async () =>
		{
			var url = Environment.GetEnvironmentVariable(UrlVariable);
			if (string.IsNullOrEmpty(url))
			{
				Assert.Ignore($"{UrlVariable} is not set");
			}

			var pushes = new List<string>();
			var disconnects = new List<RealtimeDisconnectInfo>();
			using (var client = new RealtimeClient(new RealtimeClientOptions
			{
				ServerUri = new Uri(url),
				LoginAsync = async (caller, cancellationToken) =>
					await new SampleRpcClient(caller).Login(new ISampleRpc.LoginRequest { UserId = "unity-interop" }, cancellationToken)
			}))
			{
				client.Disconnected += info => disconnects.Add(info);
				var sample = new SampleRpcClient(client);
				var subscription = client.Subscribe<ISampleRpc.EchoResponse>(RpcTopic.Of(ISampleRpc.PushTopic), push => pushes.Add(push.Text));

				var connected = await client.ConnectAsync();
				var echo = await sample.Echo(new ISampleRpc.EchoRequest { Text = "hello" });
				var failed = await sample.Fail(new ISampleRpc.FailRequest { Status = 101007 });
				var remembered = await sample.Remember(new ISampleRpc.RememberRequest { Note = "from unity" });
				var recalled = await sample.Recall();
				var pushed = await sample.PushMe(new ISampleRpc.EchoRequest { Text = "pushed" });
				var unknown = await client.CallAsync<object, object>(RpcMethod.Of("ISampleRpc/Missing"), null);
				await WaitUntil(() => pushes.Count == 1);
				subscription.Dispose();

				Assert.IsTrue(connected.IsSuccess, connected.ToString());
				Assert.AreEqual("hello", echo.Value.Text);
				Assert.AreEqual("unity-interop", echo.Value.UserId);
				Assert.AreEqual(101007, failed.Code);
				Assert.IsTrue(remembered.IsSuccess, remembered.ToString());
				Assert.AreEqual("from unity", recalled.Value.Note);
				Assert.IsTrue(pushed.IsSuccess, pushed.ToString());
				Assert.AreEqual("pushed", pushes[0]);
				Assert.AreEqual(RpcStatusCodes.MethodNotFound, unknown.Code);
				CollectionAssert.IsEmpty(disconnects);
			}
		});
	}
}
