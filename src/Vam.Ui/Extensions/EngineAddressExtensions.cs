using Vam.Ui.Services;

namespace Vam.Ui.Extensions;

/// <summary>
/// Turns what somebody types into an address gRPC will accept.
/// </summary>
/// <remarks>
/// <para>
/// Somebody told to connect to the machine with the microphones in it types
/// <c>192.168.1.50</c>, or <c>studio-pc</c>, or the whole
/// <c>http://192.168.1.50:5211</c> if they have seen one before. All three mean the same thing and
/// only the third is a URL, so the console completes the other two rather than refusing them.
/// </para>
/// <para>
/// An extension method rather than a helper class: it has a natural receiver and no state, and the
/// project's style guide keeps static utility classes out on the grounds that they cannot be
/// substituted. This one has nothing to substitute.
/// </para>
/// </remarks>
public static class EngineAddressExtensions
{
    /// <summary>Completes a typed address, or returns null when there is nothing to complete.</summary>
    /// <param name="typed">What was typed. Surrounding space is ignored.</param>
    /// <returns>An absolute http or https URL, or null when it cannot be made into one.</returns>
    public static string? ToEngineAddress(this string? typed)
    {
        string trimmed = typed?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return null;
        }

        // Cleartext by default, matching the engine. A local console and a local engine on a
        // self-signed certificate is a worse problem than the one it solves, so https is something
        // an operator opts into by typing it.
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "http://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed))
        {
            return null;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (parsed.Host.Length == 0)
        {
            return null;
        }

        // IsDefaultPort is true both when no port was typed and when the typed port is the scheme's
        // own - :80 on http. Both should become the engine's port: nobody types :80 meaning it, and
        // an engine does not listen there.
        int port = parsed.IsDefaultPort ? VamSessionOptions.DefaultPort : parsed.Port;

        return $"{parsed.Scheme}://{parsed.Host}:{port}";
    }
}
