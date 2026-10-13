using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// RPC 메서드 이름(<c>"IShopRpc/BuyProduct"</c>)과 프레임에 싣는 정수 ID 입니다. ID 는 이름의 UTF-8 FNV-1a 32비트 해시입니다.
	/// </summary>
	/// <remarks>
	/// 서버는 같은 해시로 라우팅합니다. 호출할 때마다 계산하지 않도록 <c>static readonly</c> 필드로 둡니다.
	/// </remarks>
	public readonly struct RpcMethod
	{
		public string Name { get; }
		public uint Id { get; }

		private RpcMethod(string name)
		{
			Name = name;
			Id = Fnv1a32.Hash(name);
		}

		/// <exception cref="ArgumentException"><paramref name="name"/> 이 비어 있는 경우</exception>
		public static RpcMethod Of(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Method name must not be empty", nameof(name));
			}

			return new RpcMethod(name);
		}

		public override string ToString() => Name;
	}
}
