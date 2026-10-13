using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// WebSocket 바이너리 메시지 하나에 담기는 프레임입니다. 종류에 따라 <see cref="Id"/> 와 <see cref="Value"/> 의 뜻이 다릅니다.
	/// </summary>
	/// <remarks>
	/// <list type="table">
	/// <item><term>Request</term><description><see cref="Id"/> 요청 ID(1 이상), <see cref="Value"/> 메서드 ID</description></item>
	/// <item><term>Response</term><description><see cref="Id"/> 요청 ID, <see cref="Value"/> 상태 코드(0 성공)</description></item>
	/// <item><term>Push</term><description><see cref="Id"/> 토픽 ID</description></item>
	/// <item><term>Ping, Pong</term><description><see cref="Id"/> 순번</description></item>
	/// </list>
	/// <see cref="Flags"/> 는 예약(지금은 0)입니다.
	/// </remarks>
	public readonly struct RpcFrame
	{
		public RpcFrameKind Kind { get; }
		public byte Flags { get; }
		public uint Id { get; }
		public int Value { get; }
		public ArraySegment<byte> Payload { get; }

		private RpcFrame(RpcFrameKind kind, byte flags, uint id, int value, ArraySegment<byte> payload)
		{
			Kind = kind;
			Flags = flags;
			Id = id;
			Value = value;
			Payload = payload;
		}

		/// <summary>요청 ID 입니다(Request, Response).</summary>
		public uint RequestId => Id;

		/// <summary>메서드 ID 입니다(Request).</summary>
		public uint MethodId => unchecked((uint)Value);

		/// <summary>응답 상태 코드입니다(Response). 0 이면 성공입니다.</summary>
		public int Status => Value;

		/// <summary>토픽 ID 입니다(Push).</summary>
		public uint TopicId => Id;

		/// <summary>핑퐁 순번입니다(Ping, Pong).</summary>
		public uint Sequence => Id;

		public static RpcFrame Request(uint requestId, uint methodId, ArraySegment<byte> payload)
		{
			return new RpcFrame(RpcFrameKind.Request, 0, requestId, unchecked((int)methodId), payload);
		}

		public static RpcFrame Response(uint requestId, int status, ArraySegment<byte> payload)
		{
			return new RpcFrame(RpcFrameKind.Response, 0, requestId, status, payload);
		}

		public static RpcFrame Push(uint topicId, ArraySegment<byte> payload)
		{
			return new RpcFrame(RpcFrameKind.Push, 0, topicId, 0, payload);
		}

		public static RpcFrame Ping(uint sequence)
		{
			return new RpcFrame(RpcFrameKind.Ping, 0, sequence, 0, default);
		}

		public static RpcFrame Pong(uint sequence)
		{
			return new RpcFrame(RpcFrameKind.Pong, 0, sequence, 0, default);
		}

		internal static RpcFrame Create(RpcFrameKind kind, byte flags, uint id, int value, ArraySegment<byte> payload)
		{
			return new RpcFrame(kind, flags, id, value, payload);
		}
	}
}
