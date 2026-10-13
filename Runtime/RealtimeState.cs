// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	public enum RealtimeState
	{
		/// <summary>연결이 없습니다. 닫힌(Dispose) 클라이언트도 이 상태입니다.</summary>
		Disconnected,
		/// <summary>연결하거나 로그인하는 중입니다.</summary>
		Connecting,
		/// <summary>로그인까지 끝나 호출할 수 있습니다.</summary>
		Connected
	}
}
