using System;
using System.Text;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// Newtonsoft JSON(UTF-8) 직렬화입니다. 서브프로토콜 이름은 <c>json</c> 입니다.
	/// </summary>
	public sealed class JsonRpcSerializer : IRpcSerializer
	{
		private readonly JsonSerializerSettings _settings;

		public string Name => "json";

		/// <param name="settings">Newtonsoft 설정. 없으면 기본값</param>
		public JsonRpcSerializer(JsonSerializerSettings settings = null)
		{
			_settings = settings;
		}

		public byte[] Serialize<T>(T value)
		{
			return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, _settings));
		}

		public T Deserialize<T>(ArraySegment<byte> payload)
		{
			if (payload.Array == null || payload.Count == 0)
			{
				return default;
			}

			try
			{
				return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count), _settings);
			}
			catch (JsonException exception)
			{
				throw new RpcSerializationException($"Invalid JSON payload for {typeof(T).Name}: {exception.Message}", exception);
			}
		}
	}
}
