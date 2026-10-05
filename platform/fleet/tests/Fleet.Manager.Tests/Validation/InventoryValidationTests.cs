using System.Net;
using Fleet.Core.Validation;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Validation;

/// <summary>RW-135: the inventory validator refuses the primary, private ranges, the manager's own address and injection strings.</summary>
public sealed class InventoryValidationTests
{
    private static AddressGuard Guard(FakeHostResolver? resolver = null, string[]? own = null) =>
        new(resolver ?? new FakeHostResolver(), own ?? new[] { "203.0.113.99" });

    [Theory]
    [InlineData("185.252.233.186")]
    [InlineData("::ffff:185.252.233.186")]
    [InlineData("64:ff9b::b9fc:e9ba")]
    [InlineData("10.0.0.5")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("203.0.113.99")]
    public async Task Forbidden_addresses_are_refused(string address)
    {
        var check = await Guard().CheckAsync(address, CancellationToken.None);
        Assert.False(check.Allowed, address + " must be refused");
        Assert.Equal(AddressGuard.ForbiddenCode, check.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 203.0.113.10")]
    [InlineData("203.0.113.10 ")]
    [InlineData("203.0.113.10\n")]
    [InlineData("3113781690")]
    [InlineData("0xb9.0xfc.0xe9.0xba")]
    [InlineData("0xb9fce9ba")]
    [InlineData("185.252.233")]
    [InlineData("185.252.233.186.5")]
    [InlineData("010.1.1.1")]
    [InlineData("1.2.3.256")]
    [InlineData("203.0.113.10; rm -rf /")]
    [InlineData("203.0.113.10 && id")]
    [InlineData("$(id).example.com")]
    [InlineData("`id`.example.com")]
    [InlineData("a b.example.com")]
    [InlineData("evil.example.com\n")]
    [InlineData("-oProxyCommand=id")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("host..example.com")]
    [InlineData("helper")]
    [InlineData("localhost")]
    [InlineData("exa_mple.com")]
    [InlineData("example.com/path")]
    [InlineData("user@example.com")]
    [InlineData("[2001:db8::1]")]
    [InlineData("2001:db8::1%eth0")]
    public async Task Malformed_or_hostile_input_is_rejected_before_anything_else(string address)
    {
        var check = await Guard().CheckAsync(address, CancellationToken.None);
        Assert.False(check.Allowed, "'" + address + "' must be rejected");
        Assert.Equal("address_invalid", check.Code);
    }

    [Theory]
    [InlineData("203.0.113.10")]
    [InlineData("198.51.100.7")]
    [InlineData("8.8.8.8")]
    [InlineData("2001:db8::1")]
    public async Task Ordinary_public_addresses_pass(string address)
    {
        var check = await Guard().CheckAsync(address, CancellationToken.None);
        Assert.True(check.Allowed);
        Assert.Equal(IPAddress.Parse(address).ToString(), check.Normalized);
    }

    [Fact]
    public async Task A_host_name_is_resolved_and_every_record_must_be_allowed()
    {
        var resolver = new FakeHostResolver();
        resolver.Add("good.example.com", "203.0.113.5", "2001:db8::5");
        resolver.Add("mixed.example.com", "203.0.113.6", "10.0.0.1");
        resolver.Add("primary.example.com", "185.252.233.186");
        resolver.Add("self.example.com", "203.0.113.99");
        var guard = Guard(resolver);

        var good = await guard.CheckAsync("Good.Example.Com", CancellationToken.None);
        Assert.True(good.Allowed);
        Assert.Equal("good.example.com", good.Normalized);
        Assert.Equal(2, good.Resolved.Count);

        foreach (var name in new[] { "mixed.example.com", "primary.example.com", "self.example.com" })
        {
            var check = await guard.CheckAsync(name, CancellationToken.None);
            Assert.False(check.Allowed, name);
            Assert.Equal(AddressGuard.ForbiddenCode, check.Code);
        }

        var missing = await guard.CheckAsync("nowhere.example.com", CancellationToken.None);
        Assert.False(missing.Allowed);
        Assert.Equal("address_unresolvable", missing.Code);
    }

    [Theory]
    [InlineData("api.oetwithdrhesham.co.uk")]
    [InlineData("app.oetwithdrhesham.co.uk")]
    [InlineData("oetwithdrhesham.co.uk")]
    [InlineData("oetwebsite-postgres.example.com")]
    [InlineData("db.oetwebsite.example.net")]
    public async Task Production_names_are_never_helpers_even_if_they_resolve_somewhere_harmless(string name)
    {
        var resolver = new FakeHostResolver();
        resolver.Add(name, "203.0.113.50");
        var check = await Guard(resolver).CheckAsync(name, CancellationToken.None);
        Assert.False(check.Allowed);
        Assert.Equal(AddressGuard.ForbiddenCode, check.Code);
    }

    [Fact]
    public async Task Extra_forbidden_addresses_from_configuration_are_honoured()
    {
        var guard = new AddressGuard(new FakeHostResolver(), null, new[] { "203.0.113.77" });
        Assert.False((await guard.CheckAsync("203.0.113.77", CancellationToken.None)).Allowed);
        Assert.True((await guard.CheckAsync("203.0.113.78", CancellationToken.None)).Allowed);
    }

    [Theory]
    [InlineData("helper-eu-01", true)]
    [InlineData("abc", true)]
    [InlineData("a-b", true)]
    [InlineData("ab", false)]
    [InlineData("-abc", false)]
    [InlineData("Abc", false)]
    [InlineData("abc_def", false)]
    [InlineData("abc\n", false)]
    [InlineData("abc def", false)]
    [InlineData("abc;id", false)]
    public void Node_references_follow_the_api_pattern(string value, bool valid)
    {
        Assert.Equal(valid, InputValidator.IsValidNodeRef(value));
        Assert.Equal(valid, InputValidator.ValidateNodeRef(value) is null);
    }

    [Theory]
    [InlineData("root", true)]
    [InlineData("ubuntu", true)]
    [InlineData("_svc-1", true)]
    [InlineData("Root", false)]
    [InlineData("root;id", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData("-x", false)]
    public void Ssh_user_names_are_allow_listed(string value, bool valid)
    {
        Assert.Equal(valid, InputValidator.IsValidSshUser(value));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(22, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(65536, false)]
    [InlineData(-1, false)]
    public void Ports_are_one_to_65535(int port, bool valid)
    {
        Assert.Equal(valid, InputValidator.ValidatePort(port) is null);
    }

    [Fact]
    public void Display_names_regions_and_providers_are_restricted_text()
    {
        Assert.Null(InputValidator.ValidateDisplayName("Helper EU 01"));
        Assert.NotNull(InputValidator.ValidateDisplayName("<script>"));
        Assert.NotNull(InputValidator.ValidateDisplayName(string.Empty));
        Assert.NotNull(InputValidator.ValidateDisplayName(new string('a', 81)));
        Assert.Null(InputValidator.ValidateOptionalLabel("region", null));
        Assert.Null(InputValidator.ValidateOptionalLabel("region", "eu-central"));
        Assert.NotNull(InputValidator.ValidateOptionalLabel("region", "eu;central"));
    }

    [Fact]
    public void Image_digests_and_sha256_hex_have_one_exact_shape()
    {
        Assert.True(InputValidator.IsValidImageDigest("sha256:" + new string('a', 64)));
        Assert.False(InputValidator.IsValidImageDigest("sha256:" + new string('a', 63)));
        Assert.False(InputValidator.IsValidImageDigest("sha256:" + new string('A', 64)));
        Assert.False(InputValidator.IsValidImageDigest("latest"));
        Assert.False(InputValidator.IsValidImageDigest("sha256:" + new string('a', 64) + "\n"));
        Assert.True(InputValidator.IsValidSha256Hex(new string('0', 64)));
    }
}
