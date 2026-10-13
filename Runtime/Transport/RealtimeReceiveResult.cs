using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// <see cref="IRealtimeConnection.ReceiveAsync"/> 의 결과입니다. 메시지 또는 종료 중 하나입니다.
	/// </summary>
	public readonly struct RealtimeReceiveResult
	{
		public bool IsClosed { get; }
		public ArraySegment<byte> Message { get; }
		public int CloseStatus { get; }
		public string CloseReason { get; }

		private RealtimeReceiveResult(bool isClosed, ArraySegment<byte> message, int closeStatus, string closeReason)
		{
			IsClosed = isClosed;
			Message = message;
			CloseStatus = closeStatus;
			CloseReason = closeReason;
		}

		public static RealtimeReceiveResult FromMessage(ArraySegment<byte> message)
		{
			return new RealtimeReceiveResult(false, message, 0, null);
		}

		public static RealtimeReceiveResult Closed(int closeStatus, string closeReason)
		{
			return new RealtimeReceiveResult(true, default, closeStatus, closeReason);
		}
	}
}
