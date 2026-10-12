#if UNITY_EDITOR
namespace LiveLink.Editor.Connection
{
    public enum LiveLinkCompatibilityState
    {
        NotVerified,
        Checking,
        Compatible,
        Mismatch,
        Incompatible,
        VerificationFailed,
        Disconnected
    }
}
#endif
