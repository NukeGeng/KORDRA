using System.Globalization;
using System.Reflection;
using System.Text;
using Kordra.ArtifactManagement.Domain;
using Xunit;

namespace Kordra.ArtifactManagement.Domain.Tests;

public sealed class OCIImageDigestTests
{
    private const string EmptySha256 = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string AbcSha256 = "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string AbcSha512 = "sha512:ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";

    [Theory]
    [InlineData(EmptySha256, "sha256", 64)]
    [InlineData(AbcSha256, "sha256", 64)]
    [InlineData(AbcSha512, "sha512", 128)]
    public void ParsePreservesValidatedIdentity(string input, string algorithm, int hexLength)
    {
        var value = OCIImageDigest.Parse(input);
        Assert.Equal(input, value.Value);
        Assert.Equal(input, value.ToString());
        Assert.Equal(algorithm, value.Algorithm);
        Assert.Equal(hexLength, value.EncodedHash.Length);
        Assert.True(OCIImageDigest.TryParse(input, out var parsed));
        Assert.Equal(value, parsed);
    }

    public static IEnumerable<object?[]> InvalidDigests()
    {
        foreach (var value in new string?[]
        {
            null, "", "latest", "registry.example.invalid/app:latest", "sha256:",
            "SHA256:" + new string('a', 64), "sha256:" + new string('A', 64),
            "sha256:" + new string('a', 63), "sha256:" + new string('a', 65),
            "sha512:" + new string('a', 127), "sha512:" + new string('a', 129),
            "sha256:" + new string('g', 64), "sha256:" + new string('é', 64),
            "sha256:" + new string('０', 64), "md5:" + new string('a', 32),
            "blake3:" + new string('a', 64), "sha256+b64u:" + new string('a', 64),
            " " + AbcSha256, AbcSha256 + " ", AbcSha256 + "\n", AbcSha256 + "\r\n",
            "sha256::" + new string('a', 64), AbcSha256 + "@tag", AbcSha256 + "\0",
            "sha256:" + new string('a', 31) + "\n" + new string('a', 32),
            new string('a', 100_000)
        })
        {
            yield return [value];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidDigests))]
    public void TryParseRejectsInvalidInputAndClearsResult(string? input)
    {
        OCIImageDigest? result = OCIImageDigest.Parse(AbcSha256);
        Assert.False(OCIImageDigest.TryParse(input, out result));
        Assert.Null(result);
    }

    [Fact]
    public void ParseRejectsNullWithoutEchoingInput() =>
        Assert.Throws<ArgumentNullException>(() => OCIImageDigest.Parse(null!));

    [Theory]
    [InlineData("credential-like-untrusted-input")]
    [InlineData("sha256:invalid\nlog-injection")]
    public void ParseErrorsDoNotEchoUntrustedInput(string input)
    {
        var error = Assert.Throws<FormatException>(() => OCIImageDigest.Parse(input));
        Assert.Equal("Unsupported or malformed OCI digest.", error.Message);
        Assert.DoesNotContain(input, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EmptySha256, "")]
    [InlineData(AbcSha256, "abc")]
    [InlineData(AbcSha512, "abc")]
    public void VerifyKnownRawByteVectors(string encoded, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        Assert.True(OCIImageDigest.Parse(encoded).MatchesContent(bytes, bytes.LongLength));
    }

    [Theory]
    [InlineData(AbcSha256)]
    [InlineData(AbcSha512)]
    public void ChangedByteCannotMatchAnExistingIdentity(string encoded)
    {
        var value = OCIImageDigest.Parse(encoded);
        Assert.False(value.MatchesContent("abd"u8, 3));
        Assert.False(value.MatchesContent("abc\n"u8, 4));
        Assert.True(value.MatchesContent("abc"u8, 3));
        Assert.Equal(encoded, value.Value);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(4L)]
    [InlineData(long.MaxValue)]
    public void WrongDeclaredSizeFailsEvenForCorrectHash(long size) =>
        Assert.False(OCIImageDigest.Parse(AbcSha256).MatchesContent("abc"u8, size));

    [Fact]
    public void EqualityHashingAndNullOperatorsUseExactValidatedValue()
    {
        var left = OCIImageDigest.Parse(AbcSha256);
        var same = OCIImageDigest.Parse(AbcSha256);
        var different = OCIImageDigest.Parse(EmptySha256);
        Assert.True(left == same);
        Assert.False(left != same);
        Assert.Equal(left.GetHashCode(), same.GetHashCode());
        Assert.Single(new HashSet<OCIImageDigest> { left, same });
        Assert.True(left != different);
        Assert.False(left.Equals((object)AbcSha256));
        Assert.False(left.Equals((object?)null));
        Assert.False(left == null);
        Assert.False(null == left);
        OCIImageDigest? firstNull = null;
        OCIImageDigest? secondNull = null;
        Assert.True(firstNull == secondNull);
    }

    [Fact]
    public void ValuesCannotBeConstructedOrModifiedThroughPublicApi()
    {
        var type = typeof(OCIImageDigest);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task DuplicateAndConcurrentVerificationDoesNotMutateIdentity()
    {
        var value = OCIImageDigest.Parse(AbcSha512);
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(
            () => value.MatchesContent("abc"u8, 3))));
        Assert.All(results, result => Assert.True(result));
        Assert.Equal(AbcSha512, value.Value);
    }

    [Fact]
    public void DomainAssemblyDoesNotDependOnOtherModulesOrAdapters()
    {
        Assert.All(typeof(OCIImageDigest).Assembly.GetReferencedAssemblies(), reference =>
            Assert.StartsWith("System.", reference.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void ParsingAndIdentityAreCultureIndependent(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var value = OCIImageDigest.Parse(AbcSha256);
            Assert.True(value.MatchesContent("abc"u8, 3));
            Assert.Equal(OCIImageDigest.Parse(AbcSha256), value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
