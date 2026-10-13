using Microsoft.CodeAnalysis.CSharp;

namespace Geuneda.Realtime.Generator
{
	/// <summary>C# 식별자 출력 도우미입니다. 경로 문자열과 해시에는 쓰지 않습니다.</summary>
	internal static class Identifiers
	{
		/// <summary>예약어면 <c>@</c> 를 붙입니다(<c>event</c> → <c>@event</c>).</summary>
		public static string Escape(string name)
		{
			return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
		}
	}
}
