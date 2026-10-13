using System;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.Realtime
{
	/// <summary>
	/// FNV-1a 32비트 해시(offset 2166136261, prime 16777619)입니다. 서버와 코드 생성기도 같은 함수를 씁니다.
	/// </summary>
	public static class Fnv1a32
	{
		private const uint OffsetBasis = 2166136261;
		private const uint Prime = 16777619;

		public static uint Hash(string value)
		{
			var hash = OffsetBasis;

			foreach (var b in Encoding.UTF8.GetBytes(value ?? throw new ArgumentNullException(nameof(value))))
			{
				hash = unchecked((hash ^ b) * Prime);
			}

			return hash;
		}
	}
}
