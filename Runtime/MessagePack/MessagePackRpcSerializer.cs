using System;
using MessagePack;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// MessagePack-CSharp(v3) 직렬화입니다. 서브프로토콜 이름은 <c>msgpack</c> 입니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 이 어셈블리는 <c>org.nuget.messagepack</c>(UnityNuGet) 3.0 이상이 있을 때만 컴파일됩니다.
	/// 다른 방법(NuGetForUnity 등)으로 MessagePack 을 넣었다면 Scripting Define Symbols 에 <c>GEUNEDA_MESSAGEPACK</c> 을 추가합니다.
	/// </para>
	/// <para>
	/// 본문 타입은 <c>[MessagePackObject]</c> 와 <c>[Key]</c> 로 표시합니다. IL2CPP 빌드는 리플렉션 생성(동적 코드)을 쓸 수 없으므로
	/// 소스 생성기가 만든 포매터가 들어 있는 리졸버를 <c>options</c> 로 넘깁니다.
	/// </para>
	/// <para>
	/// 옵션을 넘기지 않으면 <c>MessagePackSecurity.UntrustedData</c>(해시 충돌 방어, 깊이 제한)를 씁니다. 서버는 네트워크 입력을 받으므로
	/// 옵션을 직접 넘길 때도 <c>.WithSecurity(MessagePackSecurity.UntrustedData)</c> 를 붙입니다.
	/// </para>
	/// </remarks>
	public sealed class MessagePackRpcSerializer : IRpcSerializer
	{
		private readonly MessagePackSerializerOptions _options;

		public string Name => "msgpack";

		/// <param name="options">MessagePack 설정. 없으면 표준 리졸버에 <c>UntrustedData</c> 보안</param>
		public MessagePackRpcSerializer(MessagePackSerializerOptions options = null)
		{
			_options = options ?? MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
		}

		public byte[] Serialize<T>(T value)
		{
			return MessagePackSerializer.Serialize(value, _options);
		}

		public T Deserialize<T>(ArraySegment<byte> payload)
		{
			if (payload.Array == null || payload.Count == 0)
			{
				return default;
			}

			try
			{
				return MessagePackSerializer.Deserialize<T>(new ReadOnlyMemory<byte>(payload.Array, payload.Offset, payload.Count), _options);
			}
			catch (MessagePackSerializationException exception)
			{
				throw new RpcSerializationException($"Invalid MessagePack payload for {typeof(T).Name}: {exception.Message}", exception);
			}
		}
	}
}
