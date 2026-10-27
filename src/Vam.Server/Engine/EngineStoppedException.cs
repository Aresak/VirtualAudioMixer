using Vam.Core;

namespace Vam.Server.Engine;

/// <summary>
/// Thrown to whoever was waiting on the control loop when there is no control loop to run their work.
/// </summary>
public sealed class EngineStoppedException : VamException
{
    /// <summary>Says the engine is not running.</summary>
    public EngineStoppedException()
        : base("The engine is not running.")
    {
    }
}
