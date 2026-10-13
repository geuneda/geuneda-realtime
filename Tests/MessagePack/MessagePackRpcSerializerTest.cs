using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Realtime;
using MessagePack;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	[TestFixture]
	public class MessagePackRpcSerializerTest
	{
		[MessagePackObject]
		public class Payload
		{
			[Key(0)] public string Name;
			[Key(1)] public int Count;
		}

		private sealed class RecordingTransport : IRealtimeTransport
		{
			public string SubProtocol;

			public UniTask<Result<IRealtimeConnection>> ConnectAsync(Uri uri, string subProtocol, CancellationToken cancellationToken)
			{
				SubProtocol = subProtocol;
				return UniTask.FromResult(Result<IRealtimeConnection>.Fail(ResultCodes.NetworkError));
			}
		}

		[Test]
		public void RoundTripsCompactArrayAndEmptyIsDefault()
		{
			var serializer = new MessagePackRpcSerializer();

			var bytes = serializer.Serialize(new Payload { Name = "geuneda", Count = 3 });
			var payload = serializer.Deserialize<Payload>(new ArraySegment<byte>(bytes));

			Assert.AreEqual("msgpack", serializer.Name);
			Assert.AreEqual(0x92, bytes[0], "two-element array");
			Assert.AreEqual("geuneda", payload.Name);
			Assert.AreEqual(3, payload.Count);
			Assert.IsNull(serializer.Deserialize<Payload>(default));
			Assert.AreEqual(0, serializer.Deserialize<int>(new ArraySegment<byte>(new byte[0])));
		}

		[Test]
		public void DecodesFromSegmentOffset()
		{
			var serializer = new MessagePackRpcSerializer();
			var bytes = serializer.Serialize(42);
			var padded = new byte[bytes.Length + 2];
			Buffer.BlockCopy(bytes, 0, padded, 1, bytes.Length);

			Assert.AreEqual(42, serializer.Deserialize<int>(new ArraySegment<byte>(padded, 1, bytes.Length)));
		}

		[Test]
		public void InvalidPayload_ThrowsRpcSerializationException()
		{
			var serializer = new MessagePackRpcSerializer();

			Assert.Throws<RpcSerializationException>(() => serializer.Deserialize<Payload>(new ArraySegment<byte>(new byte[] { 0xC1 })));
			Assert.Throws<RpcSerializationException>(() => serializer.Deserialize<int>(new ArraySegment<byte>(serializer.Serialize("text"))));
		}

		[Test]
		public void Client_AdvertisesMessagePackSubProtocol()
		{
			var transport = new RecordingTransport();
			using (var client = new RealtimeClient(new RealtimeClientOptions
			{
				ServerUri = new Uri("wss://game.example.com/rpc"),
				Serializer = new MessagePackRpcSerializer(),
				Transport = transport
			}))
			{
				client.ConnectAsync().Forget();
			}

			Assert.AreEqual("geuneda.rpc.v1.msgpack", transport.SubProtocol);
		}
	}
}
