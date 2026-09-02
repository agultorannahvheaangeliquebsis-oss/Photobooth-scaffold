using Photobooth.Core;

namespace Photobooth.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTripsThePlainText()
    {
        string protectedText = SecretProtector.Protect("hunter2");

        Assert.Equal("hunter2", SecretProtector.Unprotect(protectedText));
    }

    [Fact]
    public void Protect_DoesNotReturnThePlainTextVerbatim()
    {
        string protectedText = SecretProtector.Protect("hunter2");

        Assert.NotEqual("hunter2", protectedText);
    }

    [Fact]
    public void Protect_EmptyString_RoundTripsToEmptyString()
    {
        string protectedText = SecretProtector.Protect("");

        Assert.Equal("", protectedText);
        Assert.Equal("", SecretProtector.Unprotect(protectedText));
    }

    [Fact]
    public void Unprotect_EmptyString_ReturnsEmptyStringRatherThanThrowing()
    {
        // A location that's never had a password/token saved has "" in the
        // *Protected column, not a valid ciphertext -- Unprotect must treat
        // that as "no secret" rather than trying to Base64-decode it.
        Assert.Equal("", SecretProtector.Unprotect(""));
    }
}
