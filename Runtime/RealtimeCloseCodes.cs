// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// WebSocket 종료 상태 코드와 재연결 규칙입니다.
	/// </summary>
	/// <remarks>
	/// 서버가 4000~4999 로 닫으면 서버 정책 종료(중복 로그인, 점검, 강제 퇴장 등)로 보고 자동 재연결하지 않습니다.
	/// 그 밖의 종료(정상 종료, 서버 재시작, 네트워크 끊김, 이유 없는 종료)는 재연결합니다.
	/// </remarks>
	public static class RealtimeCloseCodes
	{
		public const int Normal = 1000;
		public const int GoingAway = 1001;
		public const int ProtocolError = 1002;
		public const int UnsupportedData = 1003;
		/// <summary>종료 메시지 없이 끊김, 전송 오류, 핑퐁 무응답. 클라이언트가 붙이는 값입니다.</summary>
		public const int Abnormal = 1006;
		public const int MessageTooBig = 1009;

		public const int PolicyRangeStart = 4000;
		public const int PolicyRangeEnd = 4999;
		/// <summary>같은 계정이 다른 곳에서 로그인했습니다.</summary>
		public const int DuplicateLogin = 4001;
		/// <summary>서버 점검입니다.</summary>
		public const int Maintenance = 4002;
		/// <summary>서버가 내보냈습니다(밴, 운영 조치).</summary>
		public const int Kicked = 4003;

		/// <summary>4000~4999(서버 정책 종료)가 아니면 true 입니다.</summary>
		public static bool IsReconnectable(int closeStatus)
		{
			return closeStatus < PolicyRangeStart || closeStatus > PolicyRangeEnd;
		}

		/// <summary>종료 메시지에 실을 수 있는 코드면 true 입니다(1005, 1006, 1015 같은 예약 코드는 보낼 수 없음).</summary>
		internal static bool CanSend(int closeStatus)
		{
			return (closeStatus >= Normal && closeStatus <= UnsupportedData)
				|| (closeStatus >= 1007 && closeStatus <= 1014)
				|| (closeStatus >= 3000 && closeStatus <= PolicyRangeEnd);
		}
	}
}
