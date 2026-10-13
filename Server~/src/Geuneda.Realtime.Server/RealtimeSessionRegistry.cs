using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Geuneda.Realtime.Server
{
	/// <summary>
	/// 열린 세션과 사용자별 세션입니다. 사용자당 세션은 하나이고, 새 로그인이 이전 세션을 밀어냅니다.
	/// </summary>
	/// <remarks>
	/// 사용자 표는 <c>_usersGate</c> 하나로 지킵니다. 로그인은 그 안에서 세션이 아직 RPC 를 받는지 확인하고, 세션 제거는
	/// RPC 수명을 끝낸 뒤에 부르므로, 닫힌 세션의 늦은 로그인이 정상 세션을 밀어내거나 사용자 칸에 남지 않습니다.
	/// </remarks>
	internal sealed class RealtimeSessionRegistry : IRealtimeSessions
	{
		private readonly ConcurrentDictionary<string, RealtimeServerSession> _sessions = new ConcurrentDictionary<string, RealtimeServerSession>();
		private readonly Dictionary<string, RealtimeServerSession> _users = new Dictionary<string, RealtimeServerSession>(StringComparer.Ordinal);
		private readonly object _usersGate = new object();

		public IReadOnlyCollection<IRealtimeSession> All => _sessions.Values.ToArray();

		public IRealtimeSession FindByUser(string userId)
		{
			if (userId == null)
			{
				return null;
			}

			lock (_usersGate)
			{
				return _users.TryGetValue(userId, out var session) ? session : null;
			}
		}

		public void Add(RealtimeServerSession session)
		{
			_sessions[session.Id] = session;
		}

		/// <summary>세션을 뺍니다. 세션의 RPC 수명을 끝낸 뒤 부릅니다.</summary>
		public void Remove(RealtimeServerSession session)
		{
			_sessions.TryRemove(session.Id, out _);

			var userId = session.UserId;
			if (userId == null)
			{
				return;
			}

			lock (_usersGate)
			{
				// 새 세션이 이미 차지했으면 지우지 않는다
				if (_users.TryGetValue(userId, out var current) && current == session)
				{
					_users.Remove(userId);
				}
			}
		}

		/// <summary>사용자를 이 세션에 묶고, 밀려난 이전 세션을 돌려줍니다(없으면 null).</summary>
		/// <exception cref="OperationCanceledException">세션이 닫히기 시작해 RPC 를 더 받지 않는 경우</exception>
		public RealtimeServerSession SignIn(RealtimeServerSession session, string userId)
		{
			lock (_usersGate)
			{
				session.CallsAborted.ThrowIfCancellationRequested();

				_users.TryGetValue(userId, out var previous);
				_users[userId] = session;
				return previous == session ? null : previous;
			}
		}
	}
}
