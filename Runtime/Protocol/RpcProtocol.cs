// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 프로토콜 이름 규칙입니다. 클라이언트와 서버(<c>Geuneda.Realtime.Server</c>)가 같은 파일을 씁니다.
	/// </summary>
	public static class RpcProtocol
	{
		/// <summary>WebSocket 서브프로토콜 앞부분입니다. 프레임 규격을 바꾸면 버전을 올립니다.</summary>
		public const string SubProtocolPrefix = "geuneda.rpc.v1.";

		/// <summary>직렬화 이름(<c>json</c>, <c>msgpack</c>)의 서브프로토콜입니다.</summary>
		public static string SubProtocolOf(string serializerName)
		{
			return SubProtocolPrefix + serializerName;
		}
	}
}
