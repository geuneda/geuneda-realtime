using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 증분 생성기 캐시가 값으로 비교할 수 있게 감싼 읽기 전용 배열입니다.
	/// </summary>
	internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
	{
		private readonly T[] _items;

		public EquatableArray(IEnumerable<T> items)
		{
			_items = items.ToArray();
		}

		public int Count => _items?.Length ?? 0;

		public bool Equals(EquatableArray<T> other)
		{
			return (_items ?? Array.Empty<T>()).SequenceEqual(other._items ?? Array.Empty<T>());
		}

		public override bool Equals(object obj)
		{
			return obj is EquatableArray<T> other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				var hash = 17;
				foreach (var item in this)
				{
					hash = hash * 31 + (item?.GetHashCode() ?? 0);
				}

				return hash;
			}
		}

		public IEnumerator<T> GetEnumerator()
		{
			return ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
