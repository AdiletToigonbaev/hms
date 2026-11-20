using Hms.Modules.Identity.Domain;

namespace Hms.UnitTests.Identity;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_StoresOnlyHash_OfRandomUrlSafeValue()
    {
        var (token, plain) = RefreshToken.Issue(Guid.NewGuid(), null, Now, TimeSpan.FromDays(14), "1.2.3.4", "agent");
        var (_, other) = RefreshToken.Issue(Guid.NewGuid(), null, Now, TimeSpan.FromDays(14), null, null);

        plain.Should().MatchRegex("^[A-Za-z0-9_-]{43}$").And.NotBe(other);
        token.TokenHash.Should().Be(RefreshToken.Hash(plain)).And.NotContain(plain).And.HaveLength(64);
        token.ExpiresAt.Should().Be(Now.AddDays(14));
        token.FamilyId.Should().NotBeEmpty();
    }

    [Fact]
    public void Issue_WithFamily_KeepsSession()
    {
        var family = Guid.NewGuid();
        RefreshToken.Issue(Guid.NewGuid(), family, Now, TimeSpan.FromDays(1), null, null).Token.FamilyId.Should().Be(family);
    }

    [Fact]
    public void IsActive_FalseWhenExpiredOrRevoked()
    {
        var (token, _) = RefreshToken.Issue(Guid.NewGuid(), null, Now, TimeSpan.FromHours(1), null, null);

        token.IsActive(Now.AddMinutes(59)).Should().BeTrue();
        token.IsActive(Now.AddHours(1)).Should().BeFalse();

        token.Revoke(Now.AddMinutes(1), RefreshTokenRevokeReason.Logout);
        token.IsActive(Now.AddMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void WasJustRotated_OnlyWithinGracePeriod_AndOnlyForRotation()
    {
        var (rotated, _) = RefreshToken.Issue(Guid.NewGuid(), null, Now, TimeSpan.FromDays(1), null, null);
        rotated.Revoke(Now, RefreshTokenRevokeReason.Rotated, Guid.NewGuid());

        rotated.WasJustRotated(Now.AddSeconds(29)).Should().BeTrue();
        rotated.WasJustRotated(Now.AddSeconds(31)).Should().BeFalse();

        var (loggedOut, _) = RefreshToken.Issue(Guid.NewGuid(), null, Now, TimeSpan.FromDays(1), null, null);
        loggedOut.Revoke(Now, RefreshTokenRevokeReason.Logout);
        loggedOut.WasJustRotated(Now.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Revoke_IsIdempotent_KeepsFirstReason()
    {
        var (token, _) = RefreshToken.Issue(Guid.NewGuid(), null, Now, TimeSpan.FromDays(1), null, null);
        token.Revoke(Now, RefreshTokenRevokeReason.Rotated);
        token.Revoke(Now.AddMinutes(5), RefreshTokenRevokeReason.ReuseDetected);

        token.RevokeReason.Should().Be(RefreshTokenRevokeReason.Rotated);
        token.RevokedAt.Should().Be(Now);
    }
}
