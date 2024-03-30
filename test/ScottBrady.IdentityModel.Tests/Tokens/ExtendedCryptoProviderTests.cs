using System;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using ScottBrady.IdentityModel.Crypto;
using ScottBrady.IdentityModel.Tokens;
using Xunit;

namespace ScottBrady.IdentityModel.Tests.Tokens;

public class ExtendedCryptoProviderTests
{
    private ExtendedCryptoProvider sut = new ExtendedCryptoProvider();

    [Theory]
    [InlineData("eddsa")]
    [InlineData("RS256")]
    [InlineData("EDDSA")]
    public void IsSupportedAlgorithm_WhenNotSupportedAlgorithm_ExpectFalse(string algorithm)
        => sut.IsSupportedAlgorithm(algorithm);

    [Theory]
    [InlineData(ExtendedSecurityAlgorithms.EdDsa)]
    public void IsSupportedAlgorithm_WhenSupportedAlgorithm_ExpectTrue(string algorithm)
        => sut.IsSupportedAlgorithm(algorithm);
        
    [Fact]
    public void Release_WhenObjectImplementsIDisposable_ExpectObjectDisposed()
    {
            var memoryStream = new MemoryStream();
            sut.Release(memoryStream);
            Assert.Throws<ObjectDisposedException>(() => memoryStream.Read(Span<byte>.Empty));
        }

    [Fact]
    public void Release_WhenObjectDoesNotImplementIDisposable_ExpectNoOp()
    {
            var uri = new Uri("urn:test");
            sut.Release(uri);
        }

    [Fact]
    public void Create_WhenAlgorithmIsNotEdDsaButHasEdDsaSecurityKey_ExpectNotSupportedException()
    {
            var securityKey = new EdDsaSecurityKey(EdDsa.Create(ExtendedSecurityAlgorithms.Curves.Ed25519));

            Assert.Throws<NotSupportedException>(() => sut.Create(SecurityAlgorithms.RsaSha256, securityKey));
        }

    [Fact]
    public void Create_WhenAlgorithmIsEdDsaButIsNotEdDsaSecurityKey_ExpectNotSupportedException()
    {
            var securityKey = new RsaSecurityKey(RSA.Create());

            Assert.Throws<NotSupportedException>(() => sut.Create(ExtendedSecurityAlgorithms.EdDsa, securityKey));
        }

    [Fact]
    public void Create_WhenAlgorithmIsEdDsaWithEdDsaSecurityKey_ExpectEdDsaSignatureProvider()
    {
            var securityKey = new EdDsaSecurityKey(EdDsa.Create(ExtendedSecurityAlgorithms.Curves.Ed25519));

            var signatureProvider = sut.Create(ExtendedSecurityAlgorithms.EdDsa, securityKey);

            var edDsaSignatureProvider = Assert.IsType<EdDsaSignatureProvider>(signatureProvider);
            edDsaSignatureProvider.Algorithm.Should().Be(ExtendedSecurityAlgorithms.EdDsa);
            edDsaSignatureProvider.Key.Should().Be(securityKey);
        }
}