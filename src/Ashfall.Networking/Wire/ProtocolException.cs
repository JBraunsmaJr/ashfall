namespace Ashfall.Networking.Wire;

/// <summary>A message was malformed, truncated or not allowed in the current state.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException()
    {
    }

    public ProtocolException(string message)
        : base(message)
    {
    }

    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
