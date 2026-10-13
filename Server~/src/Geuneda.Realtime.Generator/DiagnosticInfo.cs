using System.Linq;
using Microsoft.CodeAnalysis;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 나중에 보고할 진단입니다. 모델과 함께 캐시되도록 값만 담습니다.
	/// </summary>
	internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo Location, EquatableArray<string> Arguments)
	{
		public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;

		public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo location, params string[] arguments)
		{
			return new DiagnosticInfo(descriptor, location, new EquatableArray<string>(arguments));
		}

		public Diagnostic ToDiagnostic()
		{
			return Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.Cast<object>().ToArray());
		}
	}
}
