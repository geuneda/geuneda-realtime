using System;
using System.Text;
using Geuneda.Realtime;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime.Tests
{
	[TestFixture]
	public class ProtocolTest
	{
		private sealed class Payload
		{
			public string Name;
			public int Count;
		}

		private static ArraySegment<byte> Bytes(params byte[] bytes) => new ArraySegment<byte>(bytes);

		[Test]
		public void Request_EncodesBigEndianHeaderThenPayload()
		{
			var bytes = RpcFrameCodec.Encode(RpcFrame.Request(0x01020304, 0xA0B0C0D0, Bytes(9, 8)));

			CollectionAssert.AreEqual(new byte[] { 1, 0, 1, 2, 3, 4, 0xA0, 0xB0, 0xC0, 0xD0, 9, 8 }, bytes);
		}

		[Test]
		public void AllKinds_RoundTrip()
		{
			var frames = new[]
			{
				RpcFrame.Request(7, 0xFFFFFFFF, Bytes(1, 2, 3)),
				RpcFrame.Response(7, 100001, Bytes(4)),
				RpcFrame.Response(8, 0, default),
				RpcFrame.Push(0x12345678, Bytes(5, 6)),
				RpcFrame.Ping(42),
				RpcFrame.Pong(42)
			};

			foreach (var frame in frames)
			{
				Assert.IsTrue(RpcFrameCodec.TryDecode(new ArraySegment<byte>(RpcFrameCodec.Encode(frame)), out var decoded), frame.Kind.ToString());
				Assert.AreEqual(frame.Kind, decoded.Kind);
				Assert.AreEqual(frame.Id, decoded.Id);
				Assert.AreEqual(frame.Value, decoded.Value);
				CollectionAssert.AreEqual(frame.Payload.Count == 0 ? new byte[0] : frame.Payload.ToArray(), decoded.Payload.ToArray());
			}

			Assert.AreEqual(0xFFFFFFFF, RpcFrame.Request(1, 0xFFFFFFFF, default).MethodId);
			Assert.AreEqual(100001, RpcFrame.Response(1, 100001, default).Status);
		}

		[Test]
		public void PingPong_IgnoreGivenPayload()
		{
			Assert.AreEqual(6, RpcFrameCodec.Encode(RpcFrame.Ping(1)).Length);
		}

		[Test]
		public void Decode_PayloadPointsIntoSourceWithOffset()
		{
			var source = new byte[] { 0xEE, 3, 0, 0, 0, 0, 9, 0xAB };

			Assert.IsTrue(RpcFrameCodec.TryDecode(new ArraySegment<byte>(source, 1, 7), out var frame));
			Assert.AreEqual(RpcFrameKind.Push, frame.Kind);
			Assert.AreEqual(9u, frame.TopicId);
			CollectionAssert.AreEqual(new byte[] { 0xAB }, frame.Payload.ToArray());
		}

		[TestCase(new byte[0])]
		[TestCase(new byte[] { 1, 0, 0, 0, 0 })]
		[TestCase(new byte[] { 9, 0, 0, 0, 0, 1 })]
		[TestCase(new byte[] { 1, 0, 0, 0, 0, 1, 0, 0, 0 })]
		[TestCase(new byte[] { 2, 0, 0, 0, 0, 1 })]
		[TestCase(new byte[] { 5, 1, 0, 0, 0, 1 })]
		[TestCase(new byte[] { 1, 2, 0, 0, 0, 1, 0, 0, 0, 1 })]
		[TestCase(new byte[] { 4, 0, 0, 0, 0, 1, 7 })]
		[TestCase(new byte[] { 5, 0, 0, 0, 0, 1, 99 })]
		public void Decode_Malformed_ReturnsFalse(byte[] bytes)
		{
			Assert.IsFalse(RpcFrameCodec.TryDecode(new ArraySegment<byte>(bytes), out _));
		}

		[Test]
		public void Decode_DefaultSegment_ReturnsFalse()
		{
			Assert.IsFalse(RpcFrameCodec.TryDecode(default, out _));
		}

		[TestCase("", 0x811C9DC5u)]
		[TestCase("a", 0xE40C292Cu)]
		[TestCase("foobar", 0xBF9CF968u)]
		public void Fnv1a32_KnownVectors(string value, uint expected)
		{
			Assert.AreEqual(expected, Fnv1a32.Hash(value));
		}

		[Test]
		public void RouteIds_AreHashesOfNames()
		{
			var method = RpcMethod.Of("IShopRpc/BuyProduct");

			Assert.AreEqual(Fnv1a32.Hash("IShopRpc/BuyProduct"), method.Id);
			Assert.AreNotEqual(method.Id, RpcMethod.Of("IShopRpc/Refund").Id);
			Assert.AreEqual(method.Id, RpcTopic.Of("IShopRpc/BuyProduct").Id);
			Assert.Throws<ArgumentException>(() => RpcMethod.Of(""));
			Assert.Throws<ArgumentException>(() => RpcTopic.Of(null));
		}

		[Test]
		public void Json_RoundTripsAndEmptyIsDefault()
		{
			var serializer = new JsonRpcSerializer();

			var bytes = serializer.Serialize(new Payload { Name = "geuneda", Count = 3 });
			var payload = serializer.Deserialize<Payload>(new ArraySegment<byte>(bytes));

			Assert.AreEqual("json", serializer.Name);
			Assert.AreEqual("{\"Name\":\"geuneda\",\"Count\":3}", Encoding.UTF8.GetString(bytes));
			Assert.AreEqual(3, payload.Count);
			Assert.IsNull(serializer.Deserialize<Payload>(default));
			Assert.AreEqual(0, serializer.Deserialize<int>(new ArraySegment<byte>(new byte[0])));
		}

		[Test]
		public void Json_InvalidPayload_ThrowsRpcSerializationException()
		{
			var serializer = new JsonRpcSerializer();

			Assert.Throws<RpcSerializationException>(() => serializer.Deserialize<Payload>(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{not json"))));
			Assert.Throws<RpcSerializationException>(() => serializer.Deserialize<int>(new ArraySegment<byte>(Encoding.UTF8.GetBytes("\"text\""))));
		}

		[TestCase(999, false)]
		[TestCase(1000, true)]
		[TestCase(1003, true)]
		[TestCase(1004, false)]
		[TestCase(1005, false)]
		[TestCase(1006, false)]
		[TestCase(1007, true)]
		[TestCase(1012, true)]
		[TestCase(1014, true)]
		[TestCase(1015, false)]
		[TestCase(2999, false)]
		[TestCase(3000, true)]
		[TestCase(4999, true)]
		[TestCase(5000, false)]
		public void CloseCodes_CanSendOnlyRegisteredAndApplicationCodes(int closeStatus, bool expected)
		{
			Assert.AreEqual(expected, RealtimeCloseCodes.CanSend(closeStatus));
		}

		[Test]
		public void CloseCodes_PolicyRangeIsNotReconnectable()
		{
			Assert.IsTrue(RealtimeCloseCodes.IsReconnectable(RealtimeCloseCodes.Normal));
			Assert.IsTrue(RealtimeCloseCodes.IsReconnectable(RealtimeCloseCodes.Abnormal));
			Assert.IsTrue(RealtimeCloseCodes.IsReconnectable(3999));
			Assert.IsFalse(RealtimeCloseCodes.IsReconnectable(4000));
			Assert.IsFalse(RealtimeCloseCodes.IsReconnectable(RealtimeCloseCodes.DuplicateLogin));
			Assert.IsFalse(RealtimeCloseCodes.IsReconnectable(4999));
			Assert.IsTrue(RealtimeCloseCodes.IsReconnectable(5000));
		}
	}
}
