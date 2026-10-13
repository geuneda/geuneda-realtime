using System;
using System.Buffers.Binary;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// <see cref="RpcFrame"/> 와 바이트 사이를 바꿉니다. 정수는 모두 빅엔디언입니다.
	/// </summary>
	/// <remarks>
	/// 헤더 길이: Request·Response 10바이트(종류 1, flags 1, ID 4, 값 4), Push 6바이트(종류, flags, 토픽 ID), Ping·Pong 6바이트(종류, flags, 순번).
	/// 헤더 뒤는 모두 본문입니다(Ping·Pong 은 본문 없음). 서버는 같은 규칙으로 읽고 씁니다.
	/// </remarks>
	public static class RpcFrameCodec
	{
		internal const int LongHeaderSize = 10;
		internal const int ShortHeaderSize = 6;

		public static byte[] Encode(in RpcFrame frame)
		{
			var payload = frame.Payload;
			var headerSize = HeaderSize(frame.Kind);

			if (headerSize < 0)
			{
				throw new ArgumentException($"Unknown frame kind {frame.Kind}", nameof(frame));
			}

			var hasPayload = frame.Kind != RpcFrameKind.Ping && frame.Kind != RpcFrameKind.Pong;
			var payloadLength = hasPayload ? payload.Count : 0;
			var bytes = new byte[headerSize + payloadLength];

			bytes[0] = (byte)frame.Kind;
			bytes[1] = frame.Flags;
			BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(bytes, 2, 4), frame.Id);

			if (headerSize == LongHeaderSize)
			{
				BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(bytes, 6, 4), frame.Value);
			}

			if (payloadLength > 0)
			{
				Buffer.BlockCopy(payload.Array, payload.Offset, bytes, headerSize, payloadLength);
			}

			return bytes;
		}

		/// <summary>
		/// 바이트를 프레임으로 읽습니다. 종류를 모르거나, 헤더보다 짧거나, flags 가 0 이 아니거나, Ping·Pong 에 본문이 있으면 false 입니다.
		/// 본문은 <paramref name="data"/> 를 복사하지 않고 가리킵니다.
		/// </summary>
		public static bool TryDecode(ArraySegment<byte> data, out RpcFrame frame)
		{
			frame = default;

			if (data.Array == null || data.Count < ShortHeaderSize)
			{
				return false;
			}

			var span = new ReadOnlySpan<byte>(data.Array, data.Offset, data.Count);
			var kind = (RpcFrameKind)span[0];
			var headerSize = HeaderSize(kind);

			if (headerSize < 0 || data.Count < headerSize)
			{
				return false;
			}

			// v1 은 flags 를 쓰지 않고 Ping·Pong 에 본문이 없다. 다른 값은 규약 위반이다
			if (span[1] != 0 || (headerSize == ShortHeaderSize && kind != RpcFrameKind.Push && data.Count != ShortHeaderSize))
			{
				return false;
			}

			var id = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2, 4));
			var value = headerSize == LongHeaderSize ? BinaryPrimitives.ReadInt32BigEndian(span.Slice(6, 4)) : 0;
			var payload = new ArraySegment<byte>(data.Array, data.Offset + headerSize, data.Count - headerSize);

			frame = RpcFrame.Create(kind, span[1], id, value, payload);
			return true;
		}

		private static int HeaderSize(RpcFrameKind kind)
		{
			switch (kind)
			{
				case RpcFrameKind.Request:
				case RpcFrameKind.Response:
					return LongHeaderSize;
				case RpcFrameKind.Push:
				case RpcFrameKind.Ping:
				case RpcFrameKind.Pong:
					return ShortHeaderSize;
				default:
					return -1;
			}
		}
	}
}
