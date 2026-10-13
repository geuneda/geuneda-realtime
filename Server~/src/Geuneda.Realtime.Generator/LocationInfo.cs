using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 진단 위치입니다. <see cref="Location"/> 은 컴파일을 붙잡고 있어 캐시에 두지 않고 값만 보관합니다.
	/// </summary>
	internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
	{
		public Location ToLocation()
		{
			return Location.Create(FilePath, Span, LineSpan);
		}

		public static LocationInfo From(Location location)
		{
			if (location == null || location.SourceTree == null)
			{
				return null;
			}

			return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
		}
	}
}
