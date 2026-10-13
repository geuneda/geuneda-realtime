// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 프레임 종류입니다. 프레임의 첫 바이트입니다. 값은 서버와 맞춘 프로토콜이라 바꾸지 않습니다.
	/// </summary>
	public enum RpcFrameKind : byte
	{
		/// <summary>클라이언트 → 서버 요청. <c>[1][flags][requestId u32][methodId u32][payload]</c></summary>
		Request = 1,
		/// <summary>서버 → 클라이언트 응답. <c>[2][flags][requestId u32][status i32][payload]</c></summary>
		Response = 2,
		/// <summary>서버 → 클라이언트 푸시. <c>[3][flags][topicId u32][payload]</c></summary>
		Push = 3,
		/// <summary>핑. <c>[4][flags][sequence u32]</c>. 받은 쪽은 같은 순번으로 퐁을 보냅니다.</summary>
		Ping = 4,
		/// <summary>퐁. <c>[5][flags][sequence u32]</c></summary>
		Pong = 5
	}
}
