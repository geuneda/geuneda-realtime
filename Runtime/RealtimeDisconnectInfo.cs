// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 연결이 끊긴 이유입니다. <see cref="IRealtimeClient.Disconnected"/> 로 전달합니다.
	/// </summary>
	public readonly struct RealtimeDisconnectInfo
	{
		/// <summary>종료 상태 코드(<see cref="RealtimeCloseCodes"/>)입니다.</summary>
		public int CloseStatus { get; }

		/// <summary>종료 설명입니다. 없으면 null 입니다.</summary>
		public string Reason { get; }

		/// <summary>자동 재연결을 시작했으면 true 입니다. 정책 종료이거나 재연결이 꺼져 있으면 false 입니다.</summary>
		public bool WillReconnect { get; }

		public RealtimeDisconnectInfo(int closeStatus, string reason, bool willReconnect)
		{
			CloseStatus = closeStatus;
			Reason = reason;
			WillReconnect = willReconnect;
		}

		public override string ToString()
		{
			return $"{CloseStatus} {Reason} (reconnect: {WillReconnect})";
		}
	}
}
