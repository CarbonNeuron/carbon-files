using CarbonFiles.Core.Models;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Core.Tests.Models;

public class AuthContextTests
{
    [Fact]
    public void Admin_HasCorrectRoleProperties()
    {
        var auth = AuthContext.Admin();

        auth.Role.Should().Be(AuthRole.Admin);
        auth.IsAdmin.Should().BeTrue();
        auth.IsOwner.Should().BeFalse();
        auth.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void Owner_HasCorrectRoleProperties()
    {
        var auth = AuthContext.Owner("testuser", "cf4_abc123");

        auth.Role.Should().Be(AuthRole.Owner);
        auth.IsAdmin.Should().BeFalse();
        auth.IsOwner.Should().BeTrue();
        auth.IsPublic.Should().BeFalse();
        auth.OwnerName.Should().Be("testuser");
        auth.KeyPrefix.Should().Be("cf4_abc123");
    }

    [Fact]
    public void Public_HasCorrectRoleProperties()
    {
        var auth = AuthContext.Public();

        auth.Role.Should().Be(AuthRole.Public);
        auth.IsAdmin.Should().BeFalse();
        auth.IsOwner.Should().BeFalse();
        auth.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void Admin_CanManageAnyBucket()
    {
        var auth = AuthContext.Admin();

        auth.CanManage("alice").Should().BeTrue();
        auth.CanManage("bob").Should().BeTrue();
        auth.CanManage("anyone").Should().BeTrue();
    }

    [Fact]
    public void Owner_CanManageOwnBucketsOnly()
    {
        var auth = AuthContext.Owner("alice", "cf4_abc123");

        auth.CanManage("alice").Should().BeTrue();
        auth.CanManage("bob").Should().BeFalse();
        auth.CanManage("anyone").Should().BeFalse();
    }

    [Fact]
    public void Public_CannotManageAnything()
    {
        var auth = AuthContext.Public();

        auth.CanManage("alice").Should().BeFalse();
        auth.CanManage("bob").Should().BeFalse();
        auth.CanManage("anyone").Should().BeFalse();
    }

    [Fact]
    public void Owner_CanManage_IsCaseSensitive()
    {
        var auth = AuthContext.Owner("Alice", "cf4_abc123");

        auth.CanManage("Alice").Should().BeTrue();
        auth.CanManage("alice").Should().BeFalse();
    }
}
