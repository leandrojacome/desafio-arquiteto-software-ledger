namespace Ledger.Architecture.Tests.Ci;

internal sealed record PipelineStep(
    string? Script,
    string? Task,
    string? Condition,
    bool IsCheckout,
    string? CheckoutTarget,
    string? FetchDepth,
    string? DisplayName,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Env,
    string? ScriptKind = null);

internal sealed record PipelineJob(
    string Name,
    IReadOnlyList<string> DependsOn,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<PipelineStep> Steps,
    int? TimeoutInMinutes = null,
    bool IsDeployment = false,
    string? Environment = null);

internal sealed record PipelineStage(
    string Name,
    IReadOnlyList<string> DependsOn,
    string? Condition,
    IReadOnlyList<PipelineJob> Jobs);

internal sealed record TemplateCall(string From, string To, IReadOnlyList<string> SuppliedParameters);

internal sealed record TemplateParameter(string Name, string Type, bool HasDefault, string? Default);
