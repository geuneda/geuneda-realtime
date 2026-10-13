using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Geuneda.Realtime.Generator
{
	/// <summary>
	/// 서비스별 진단과 서비스 사이·이미 있는 형식과의 충돌을 보고하고, 오류가 없으면 대상 런타임에 맞는 코드를 만듭니다.
	/// </summary>
	internal static class Emitter
	{
		private const string CallerFieldName = "_caller";

		public static void Emit(SourceProductionContext context, ImmutableArray<ServiceModel> services, Compilation compilation)
		{
			if (services.IsDefaultOrEmpty)
			{
				return;
			}

			var targets = GenerationTargets.From(compilation);
			var ordered = services.OrderBy(service => service.FullyQualifiedName, StringComparer.Ordinal).ToList();
			var diagnostics = new List<DiagnosticInfo>(ordered.SelectMany(service => service.Diagnostics));

			AddServiceNameConflicts(ordered, diagnostics);
			AddRouteIdConflicts(ordered, diagnostics);
			AddGeneratedNameConflicts(ordered, diagnostics);

			if (targets.Client && !targets.ClientDependencies)
			{
				diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.MissingClientDependencies, ordered[0].Location, ordered[0].InterfaceName));
			}

			if (targets.Client)
			{
				AddExistingTypeConflicts(compilation, ordered.SelectMany(ClientTypeNames).ToList(), ordered, diagnostics);
			}

			if (targets.Server)
			{
				AddExistingTypeConflicts(compilation, new[] { RouteEmitter.MetadataNameOf(targets.AssemblyName) }, ordered, diagnostics);
			}

			foreach (var diagnostic in diagnostics)
			{
				context.ReportDiagnostic(diagnostic.ToDiagnostic());
			}

			// 오류가 있으면 빌드가 멈추므로 반쪽 코드를 만들지 않는다
			if (diagnostics.Any(diagnostic => diagnostic.IsError))
			{
				return;
			}

			if (targets.Client)
			{
				foreach (var service in ordered)
				{
					context.AddSource(HintName(service) + ".Client.g.cs", ClientEmitter.Emit(service));
				}
			}

			if (targets.Server)
			{
				context.AddSource(RouteEmitter.ClassName + ".g.cs", RouteEmitter.Emit(ordered, targets.AssemblyName));
			}
		}

		private static void AddServiceNameConflicts(List<ServiceModel> services, List<DiagnosticInfo> diagnostics)
		{
			foreach (var group in services.GroupBy(service => service.InterfaceName).Where(group => group.Count() > 1))
			{
				var names = string.Join(", ", group.Select(service => service.FullyQualifiedName));
				diagnostics.AddRange(group.Skip(1).Select(service =>
					DiagnosticInfo.Create(RpcDiagnostics.DuplicateServiceName, service.Location, service.InterfaceName, names)));
			}
		}

		private static void AddRouteIdConflicts(List<ServiceModel> services, List<DiagnosticInfo> diagnostics)
		{
			var routes = new Dictionary<uint, string>();

			foreach (var method in services.SelectMany(service => service.Methods))
			{
				if (!routes.TryGetValue(method.RouteId, out var other))
				{
					routes.Add(method.RouteId, method.RouteName);
				}
				else if (other != method.RouteName)
				{
					diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.RouteIdCollision, method.Location,
						method.RouteName, other, "0x" + method.RouteId.ToString("X8")));
				}
			}
		}

		/// <summary>생성 멤버와 겹치는 메서드 이름, 같은 네임스페이스에서 같은 생성 형식 이름을 만드는 서비스를 찾습니다.</summary>
		private static void AddGeneratedNameConflicts(List<ServiceModel> services, List<DiagnosticInfo> diagnostics)
		{
			foreach (var service in services)
			{
				var reserved = new[] { service.ClientName, service.ClientInterfaceName, service.MethodsClassName, CallerFieldName };
				foreach (var method in service.Methods.Where(method => reserved.Contains(method.Name)))
				{
					diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.ReservedMemberName, method.Location, service.InterfaceName + "." + method.Name, method.Name));
				}
			}

			// 종류와 상관없이 생성할 형식 이름 전체를 비교한다(IShopRpc 의 인터페이스 IShopRpcClient 와 IIShopRpc 의 클래스 IShopRpcClient 등)
			var owners = new Dictionary<string, ServiceModel>(StringComparer.Ordinal);
			foreach (var service in services)
			{
				foreach (var typeName in ClientTypeNames(service))
				{
					if (owners.TryGetValue(typeName, out var owner))
					{
						diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.GeneratedTypeCollision, service.Location,
							owner.FullyQualifiedName, service.FullyQualifiedName, typeName));
					}
					else
					{
						owners.Add(typeName, service);
					}
				}
			}
		}

		private static void AddExistingTypeConflicts(Compilation compilation, IReadOnlyList<string> metadataNames, List<ServiceModel> services,
			List<DiagnosticInfo> diagnostics)
		{
			foreach (var metadataName in metadataNames)
			{
				var existing = compilation.GetTypeByMetadataName(metadataName);
				var location = existing?.Locations.FirstOrDefault(candidate => candidate.IsInSource);
				if (location != null)
				{
					diagnostics.Add(DiagnosticInfo.Create(RpcDiagnostics.ExistingGeneratedType, LocationInfo.From(location) ?? services[0].Location, metadataName));
				}
			}
		}

		private static IEnumerable<string> ClientTypeNames(ServiceModel service)
		{
			yield return service.MetadataNameOf(service.MethodsClassName);
			yield return service.MetadataNameOf(service.ClientInterfaceName);
			yield return service.MetadataNameOf(service.ClientName);
		}

		private static string HintName(ServiceModel service)
		{
			return service.FullyQualifiedName.Replace("global::", string.Empty).Replace("@", string.Empty);
		}
	}
}
