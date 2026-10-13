using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Geuneda.Realtime.Server
{
	/// <summary>메서드 ID 로 라우트를 찾는 표입니다. 같은 ID 가 둘이면 시작할 때 예외입니다.</summary>
	internal sealed class RpcRouteTable
	{
		private readonly FrozenDictionary<uint, RpcRoute> _routes;

		public RpcRouteTable(IEnumerable<RpcRoute> routes)
		{
			var map = new Dictionary<uint, RpcRoute>();

			foreach (var route in routes)
			{
				if (route == null)
				{
					throw new InvalidOperationException("RealtimeServerOptions.Routes contains null");
				}

				if (map.TryGetValue(route.Id, out var existing))
				{
					throw new InvalidOperationException(existing.Name == route.Name
						? $"RPC route '{route.Name}' is registered twice"
						: $"RPC routes '{existing.Name}' and '{route.Name}' share id {route.Id}");
				}

				map.Add(route.Id, route);
			}

			_routes = map.ToFrozenDictionary();
		}

		public IEnumerable<RpcRoute> All => _routes.Values;

		public bool TryGet(uint id, out RpcRoute route)
		{
			return _routes.TryGetValue(id, out route);
		}
	}
}
