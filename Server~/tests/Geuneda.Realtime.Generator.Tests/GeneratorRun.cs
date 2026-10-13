using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Geuneda.Realtime.Generator.Tests
{
	/// <summary>생성기 실행 결과입니다: 생성기 진단, 생성 파일(이름 → 내용), 생성 코드를 합친 컴파일의 오류.</summary>
	internal sealed class GeneratorRun
	{
		public GeneratorRun(ImmutableArray<Diagnostic> diagnostics, IReadOnlyDictionary<string, string> generated, IReadOnlyList<Diagnostic> compileErrors)
		{
			Diagnostics = diagnostics;
			Generated = generated;
			CompileErrors = compileErrors;
		}

		public ImmutableArray<Diagnostic> Diagnostics { get; }
		public IReadOnlyDictionary<string, string> Generated { get; }
		public IReadOnlyList<Diagnostic> CompileErrors { get; }

		public IEnumerable<string> DiagnosticIds => Diagnostics.Select(diagnostic => diagnostic.Id);
	}
}
