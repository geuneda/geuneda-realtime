using System.Collections.Concurrent;

namespace Geuneda.Realtime.SampleServer
{
	/// <summary>사용자별로 기억한 메모입니다(샘플이라 메모리에만 둠).</summary>
	public sealed class SampleMemory
	{
		public ConcurrentDictionary<string, string> Notes { get; } = new ConcurrentDictionary<string, string>();
	}
}
