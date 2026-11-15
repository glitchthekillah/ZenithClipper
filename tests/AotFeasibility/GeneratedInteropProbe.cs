using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json.Serialization;

// Compile-only proof of AOT-safe COM generation. Not wired to native activation.
[GeneratedComInterface]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
internal partial interface IActivateOperationProbe
{
    void GetActivateResult(out int activationResult, out nint client);
}
[GeneratedComInterface]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
internal partial interface ICompletionProbe
{
    [PreserveSig] int ActivateCompleted(nint operation);
}
[GeneratedComInterface]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface IAgileProbe { }
[GeneratedComClass]
internal partial class CompletionProbe : ICompletionProbe, IAgileProbe
{
    // Real implementation must convert operation via StrategyBasedComWrappers and own client release.
    public int ActivateCompleted(nint operation) => 0;
}
[JsonSerializable(typeof(ProbeStatus))]
internal partial class ProbeJson : JsonSerializerContext { }
internal sealed record ProbeStatus(int Pid, string State);
