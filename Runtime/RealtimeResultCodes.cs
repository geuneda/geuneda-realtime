// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// realtime 패키지의 결과 코드입니다(core 결과 코드 범위 표의 2000 블록).
	/// </summary>
	/// <remarks>
	/// 연결 실패·연결 끊김은 <c>ResultCodes.NetworkError</c>, 시간 초과는 <c>Timeout</c>, 호출자 취소는 <c>Cancelled</c> 입니다.
	/// 서버 응답의 상태 코드가 100000 이상(게임 범위)이면 그 값 그대로 실패 코드가 되고, 0 도 게임 범위도 아니면 <see cref="InvalidResponse"/> 입니다.
	/// </remarks>
	public static class RealtimeResultCodes
	{
		/// <summary>이 패키지 코드 블록의 시작입니다. 코드로는 쓰지 않습니다.</summary>
		public const int RangeStart = 2000;
		/// <summary>연결이 없고 <see cref="RealtimeClientOptions.ConnectOnCall"/> 이 꺼져 있습니다.</summary>
		public const int NotConnected = RangeStart + 1;
		/// <summary>서버가 정책 종료(4000~4999)로 닫았습니다. <see cref="IRealtimeClient.ConnectAsync"/> 로 다시 연결해야 합니다.</summary>
		public const int NotReconnectable = RangeStart + 2;
		/// <summary>클라이언트를 닫아(Dispose) 진행 중이던 연결·호출이 끝났습니다.</summary>
		public const int Closed = RangeStart + 3;
		/// <summary>응답 본문을 읽지 못했거나 응답 상태 코드가 0 도 게임 범위(100000 이상)도 아닙니다(서버와 형식 불일치).</summary>
		public const int InvalidResponse = RangeStart + 4;
		/// <summary>서버가 요청한 서브프로토콜(프로토콜 버전·직렬화 형식)을 받지 않았습니다. 재연결하지 않습니다.</summary>
		public const int ProtocolMismatch = RangeStart + 5;
	}
}
