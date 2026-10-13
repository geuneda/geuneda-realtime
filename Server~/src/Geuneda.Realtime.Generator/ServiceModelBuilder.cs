using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// <c>[RpcService]</c> 인터페이스 선언을 <see cref="ServiceModel"/> 로 바꾸고 계약 규칙을 검사합니다.
	/// </summary>
	internal static class ServiceModelBuilder
	{
		public const string ServiceAttribute = "Geuneda.Realtime.RpcServiceAttribute";
		public const string AllowAnonymousAttribute = "Geuneda.Realtime.RpcAllowAnonymousAttribute";

		private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

		public static bool IsCandidate(SyntaxNode node)
		{
			return node is InterfaceDeclarationSyntax declaration && declaration.AttributeLists.Count > 0;
		}

		public static ServiceModel Build(GeneratorSyntaxContext context, CancellationToken cancellationToken)
		{
			var declaration = (InterfaceDeclarationSyntax)context.Node;
			if (!(context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol service)
			    || !HasAttribute(service, ServiceAttribute))
			{
				return null;
			}

			// partial 인터페이스는 특성 목록이 있는 선언(후보) 중 첫 번째에서 한 번만 만든다. [RpcService] 가 뒤쪽 선언에 있어도 찾는다
			var representative = service.DeclaringSyntaxReferences.First(reference =>
				reference.GetSyntax(cancellationToken) is InterfaceDeclarationSyntax candidate && candidate.AttributeLists.Count > 0);
			if (representative.SyntaxTree != declaration.SyntaxTree || representative.Span != declaration.Span)
			{
				return null;
			}

			var diagnostics = new List<DiagnosticInfo>();
			var location = LocationInfo.From(declaration.Identifier.GetLocation());

			if (service.IsGenericType)
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.GenericService, location, service.Name));
			}

			if (service.ContainingType != null)
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.NestedService, location, service.Name));
			}

			if (service.Interfaces.Length > 0)
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.InheritedService, location, service.Name));
			}

			var methods = new List<MethodModel>();
			var names = new HashSet<string>();

			foreach (var member in service.GetMembers())
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary && !method.IsStatic && method.IsAbstract)
				{
					if (!names.Add(method.Name))
					{
						diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.DuplicateMethodName, LocationOf(method), service.Name, method.Name));
						continue;
					}

					var model = BuildMethod(service, method, diagnostics);
					if (model != null)
					{
						methods.Add(model);
					}
				}
				else if (member is IMethodSymbol accessor && accessor.AssociatedSymbol != null)
				{
					// 속성·이벤트 접근자는 그 속성·이벤트로 한 번만 보고한다
				}
				else if (member is INamedTypeSymbol || member is IFieldSymbol { IsConst: true })
				{
					// 요청·응답 형식(IShopRpc.BuyProductReq)과 상수(푸시 토픽 이름 등)를 인터페이스 안에 두는 것은 허용한다
				}
				else
				{
					diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.InvalidMember, LocationOf(member), service.Name, member.Name));
				}
			}

			return new ServiceModel(
				service.ContainingNamespace.IsGlobalNamespace ? null : service.ContainingNamespace.ToDisplayString(TypeFormat).Replace("global::", string.Empty),
				MetadataNamespaceOf(service.ContainingNamespace),
				service.Name,
				service.ToDisplayString(TypeFormat),
				service.DeclaredAccessibility == Accessibility.Public,
				location,
				new EquatableArray<MethodModel>(methods),
				new EquatableArray<DiagnosticInfo>(diagnostics));
		}

		private static MethodModel BuildMethod(INamedTypeSymbol service, IMethodSymbol method, List<DiagnosticInfo> diagnostics)
		{
			var location = LocationOf(method);
			var display = service.Name + "." + method.Name;
			var valid = true;

			if (method.IsGenericMethod)
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.GenericMethod, location, display));
				valid = false;
			}

			string responseType = null;
			if (!(method.ReturnType is INamedTypeSymbol returnType) || !TryGetTaskResult(returnType, out responseType))
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.InvalidReturnType, location, display));
				valid = false;
			}

			var parameters = method.Parameters;
			var hasCancellationToken = parameters.Length > 0 && IsCancellationToken(parameters[parameters.Length - 1].Type);
			var requestCount = parameters.Length - (hasCancellationToken ? 1 : 0);
			string requestType = null;

			if (requestCount > 1
			    || parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams)
			    || (requestCount == 1 && IsCancellationToken(parameters[0].Type)))
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.InvalidParameters, location, display));
				valid = false;
			}
			else if (requestCount == 1)
			{
				requestType = parameters[0].Type.ToDisplayString(TypeFormat);
			}

			if (!valid)
			{
				return null;
			}

			var routeName = service.Name + "/" + method.Name;
			return new MethodModel(method.Name, routeName, Fnv1a32.Hash(routeName), requestType, responseType,
				hasCancellationToken, HasAttribute(method, AllowAnonymousAttribute), location);
		}

		private static bool TryGetTaskResult(INamedTypeSymbol type, out string responseType)
		{
			responseType = null;

			if (type.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
			{
				return false;
			}

			if (type.MetadataName == "Task")
			{
				return true;
			}

			if (type.MetadataName == "Task`1")
			{
				responseType = type.TypeArguments[0].ToDisplayString(TypeFormat);
				return true;
			}

			return false;
		}

		private static bool IsCancellationToken(ITypeSymbol type)
		{
			return type.ToDisplayString() == "System.Threading.CancellationToken";
		}

		private static bool HasAttribute(ISymbol symbol, string attributeName)
		{
			return symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName);
		}

		private static string MetadataNamespaceOf(INamespaceSymbol symbol)
		{
			var names = new List<string>();
			for (var current = symbol; current != null && !current.IsGlobalNamespace; current = current.ContainingNamespace)
			{
				names.Insert(0, current.Name);
			}

			return string.Join(".", names);
		}

		private static LocationInfo LocationOf(ISymbol symbol)
		{
			return LocationInfo.From(symbol.Locations.FirstOrDefault());
		}
	}
}
