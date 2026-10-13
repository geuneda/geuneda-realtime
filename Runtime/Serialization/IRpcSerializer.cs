using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 요청·응답·푸시 본문의 직렬화입니다. 기본은 <see cref="JsonRpcSerializer"/> 이고, MessagePack 은 <c>Geuneda.Realtime.MessagePack</c> 어셈블리에 있습니다.
	/// </summary>
	/// <remarks>
	/// <see cref="Name"/> 은 WebSocket 서브프로토콜(<c>geuneda.rpc.v1.&lt;이름&gt;</c>)로 서버에 알려, 서버가 같은 형식을 쓰게 합니다.
	/// 여러 스레드에서 동시에 불릴 수 있습니다.
	/// </remarks>
	public interface IRpcSerializer
	{
		/// <summary>형식 이름(소문자, 예: <c>json</c>, <c>msgpack</c>)입니다.</summary>
		string Name { get; }

		byte[] Serialize<T>(T value);

		/// <summary>
		/// 본문을 읽습니다. 본문이 비었으면 <c>default</c> 입니다. 읽지 못하면 <see cref="RpcSerializationException"/> 을 던집니다(다른 예외로 바꾸지 않음).
		/// </summary>
		T Deserialize<T>(ArraySegment<byte> payload);
	}
}
