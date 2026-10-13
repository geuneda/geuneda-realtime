using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// 서버 푸시 토픽 이름과 ID 입니다. ID 규칙은 <see cref="RpcMethod"/> 와 같습니다.
	/// </summary>
	public readonly struct RpcTopic
	{
		public string Name { get; }
		public uint Id { get; }

		private RpcTopic(string name)
		{
			Name = name;
			Id = Fnv1a32.Hash(name);
		}

		/// <exception cref="ArgumentException"><paramref name="name"/> 이 비어 있는 경우</exception>
		public static RpcTopic Of(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Topic name must not be empty", nameof(name));
			}

			return new RpcTopic(name);
		}

		public override string ToString() => Name;
	}
}
