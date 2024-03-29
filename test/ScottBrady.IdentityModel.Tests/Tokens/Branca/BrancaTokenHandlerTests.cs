using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using ScottBrady.IdentityModel.Crypto;
using ScottBrady.IdentityModel.Tokens.Branca;
using Xunit;

namespace ScottBrady.IdentityModel.Tests.Tokens.Branca;

public class BrancaTokenHandlerTests
{
    private const string ValidToken = "5K6fDIqRhrSuqGE3FbuxAPd19P2toAsbBxOn4bgSame9ti6QZUQJkrggCypBJIEXF6tvhgjeMZTV76UkiqXNSvqHebeplccFrhepHkxU1SlSSFoAMKs5TUomcg6ZgDhiaYDs3IlypSxafP4uvKmu0VD";
    private readonly byte[] validKey = Encoding.UTF8.GetBytes("supersecretkeyyoushouldnotcommit");
    private static readonly byte[] ExpectedPayload = Encoding.UTF8.GetBytes("{\"user\":\"scott@scottbrady91.com\",\"scope\":[\"read\",\"write\",\"delete\"]}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CanReadToken_WhenTokenIsNullOrWhitespace_ExpectFalse(string token)
    {
        var handler = new BrancaTokenHandler();
        var canReadToken = handler.CanReadToken(token);

        canReadToken.Should().BeFalse();
    }

    [Fact]
    public void CanReadToken_WhenTokenIsTooLong_ExpectFalse()
    {
        var tokenBytes = new byte[TokenValidationParameters.DefaultMaximumTokenSizeInBytes + 1];
        new Random().NextBytes(tokenBytes);

        var canReadToken = new BrancaTokenHandler().CanReadToken(Convert.ToBase64String(tokenBytes));

        canReadToken.Should().BeFalse();
    }
        
    [Fact]
    public void CanReadToken_WhenJwtToken_ExpectFalse()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjMiLCJuYW1lIjoiU2NvdHQgQnJhZHkiLCJpYXQiOjE1ODU3Njc0Mjl9.DcGCOpx19JQzVVeZPHgqB73rbLaCUsx-k6PuFdit6IM";
            
        var canReadToken = new BrancaTokenHandler().CanReadToken(jwt);

        canReadToken.Should().BeFalse();
    }
        
    [Fact]
    public void CanReadToken_WhenTokenContainsNonBase64Characters_ExpectFalse()
    {
        const string token = "token==";
            
        var canReadToken = new BrancaTokenHandler().CanReadToken(token);

        canReadToken.Should().BeFalse();
    }

    [Fact]
    public void CanReadToken_WhenBrancaToken_ExpectTrue()
    {
        var canReadToken = new BrancaTokenHandler().CanReadToken(ValidToken);

        canReadToken.Should().BeTrue();
    }

    [Fact]
    public void CanValidateToken_ExpectTrue()
        => new BrancaTokenHandler().CanValidateToken.Should().BeTrue();
        
    [Fact]
    public void CreateToken_WhenPayloadIsNull_ExpectArgumentNullException()
    {
        var handler = new BrancaTokenHandler();
        Assert.Throws<ArgumentNullException>(() => handler.CreateToken(null, validKey));
    }
        
    [Fact]
    public void CreateToken_WhenKeyIsNull_ExpectInvalidOperationException() 
        => Assert.Throws<InvalidOperationException>(() => new BrancaTokenHandler().CreateToken("test", null));

    [Fact]
    public void CreateToken_WhenKeyIsNot32Bytes_ExpectInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() =>
            new BrancaTokenHandler().CreateToken("test", Encoding.UTF8.GetBytes("iamonly14bytes")));

    [Fact]
    public void CreateToken_WhenTokenGenerated_ExpectBas62EncodedTokenWithCorrectLength()
    {
        var payload = CreateTestPayload();
        var handler = new BrancaTokenHandler();

        var token = handler.CreateToken(payload, 0, validKey);

        token.Any(x => !Base62.CharacterSet.Contains(x)).Should().BeFalse();
        Base62.Decode(token).Length.Should().Be(payload.Length + 29 + 16);
    }

    [Fact]
    public void CreateToken_WhenSecurityTokenDescriptorIsNull_ExpectArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new BrancaTokenHandler().CreateToken(null));

        
    [Fact]
    public void CreateAndDecryptToken_WithSecurityTokenDescriptor_ExpectCorrectBrancaTimestampAndNoIatClaim()
    {
        var handler = new BrancaTokenHandler();
            
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            EncryptingCredentials = new EncryptingCredentials(new SymmetricSecurityKey(validKey), ExtendedSecurityAlgorithms.XChaCha20Poly1305)
        });

        var parsedToken = handler.DecryptToken(token, validKey);
        var jObject = JObject.Parse(Encoding.UTF8.GetString(parsedToken.Payload));
        jObject["iat"].Should().BeNull();
            
        parsedToken.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMilliseconds(1500));
    }
        
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DecryptToken_WhenTokenIsNullOrWhitespace_ExpectArgumentNullException(string token)
    {
        var handler = new BrancaTokenHandler();
        Assert.Throws<ArgumentNullException>(() => handler.DecryptToken(token, validKey));
    }

    [Fact]
    public void DecryptToken_WhenKeyIsNull_ExpectInvalidOperationException() 
        => Assert.Throws<InvalidOperationException>(() => new BrancaTokenHandler().DecryptToken(ValidToken, null));

    [Fact]
    public void DecryptToken_WhenKeyIsNot32Bytes_ExpectInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() =>
            new BrancaTokenHandler().DecryptToken(ValidToken, Encoding.UTF8.GetBytes("iamonly14bytes")));
        
    [Fact]
    public void DecryptToken_WhenTokenHasInvalidLength_ExpectSecurityTokenException()
    {
        var bytes = new byte[20];
        new Random().NextBytes(bytes);

        Assert.Throws<SecurityTokenException>(() =>
            new BrancaTokenHandler().DecryptToken(Base62.Encode(bytes), validKey));
    }
        
    [Fact]
    public void DecryptToken_WhenTokenHasIncorrectVersion_ExpectSecurityTokenException()
    {
        var bytes = new byte[120];
        new Random().NextBytes(bytes);
        bytes[0] = 0x00;

        Assert.Throws<SecurityTokenException>(() =>
            new BrancaTokenHandler().DecryptToken(Base62.Encode(bytes), validKey));
    }
        
    [Fact]
    public void DecryptToken_WhenValidToken_ExpectCorrectPayload()
    {
        var parsedToken = new BrancaTokenHandler().DecryptToken(ValidToken, validKey);
        parsedToken.Payload.Should().BeEquivalentTo(ExpectedPayload);
    }

    [Fact]
    public void EncryptAndDecryptToken_ExpectCorrectPayloadAndTimestamp()
    {
        var payload = Guid.NewGuid().ToString();
        var handler = new BrancaTokenHandler();

        var token = handler.CreateToken(payload, validKey);
        var decryptedPayload = handler.DecryptToken(token, validKey);

        decryptedPayload.Payload.Should().BeEquivalentTo(Encoding.UTF8.GetBytes(payload));
        decryptedPayload.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void EncryptAndDecryptToken_WithExplicitTimestamp_ExpectCorrectPayloadAndTimestamp()
    {
        var payload = Guid.NewGuid().ToString();
        var timestamp = new DateTime(2020, 08, 22).ToUniversalTime();
        var handler = new BrancaTokenHandler();

        var token = handler.CreateToken(payload, timestamp, validKey);
        var decryptedPayload = handler.DecryptToken(token, validKey);

        decryptedPayload.Payload.Should().BeEquivalentTo(Encoding.UTF8.GetBytes(payload));
        decryptedPayload.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void EncryptAndDecryptToken_WithExplicitBrancaTimestamp_ExpectCorrectPayloadAndTimestamp()
    {
        var payload = CreateTestPayload();
        var timestamp = uint.MinValue;
        var handler = new BrancaTokenHandler();

        var token = handler.CreateToken(payload, timestamp, validKey);
        var decryptedPayload = handler.DecryptToken(token, validKey);

        decryptedPayload.Payload.Should().BeEquivalentTo(payload);
        decryptedPayload.Timestamp.Should().Be(new DateTime(1970, 01, 01, 0, 0, 0, DateTimeKind.Utc));
        decryptedPayload.BrancaFormatTimestamp.Should().Be(timestamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateToken_WhenTokenIsNullOrWhitespace_ExpectFailureWithArgumentNullException(string token)
    {
        var result = new BrancaTokenHandler().ValidateToken(token, new TokenValidationParameters());

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<ArgumentNullException>();
    }

    [Fact]
    public void ValidateToken_WhenTokenValidationParametersAreNull_ExpectFailureWithArgumentNullException()
    {
        var result = new BrancaTokenHandler().ValidateToken(ValidToken, null);
            
        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<ArgumentNullException>();
    }

    [Fact]
    public void ValidateToken_WhenTokenCannotBeRead_ExpectFailureWithSecurityTokenException()
    {
        var result = new BrancaTokenHandler().ValidateToken("=====", new TokenValidationParameters());
            
        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenException>();
    }

    [Fact]
    public void ValidateToken_WhenIncorrectDecryptionKey_ExpectFailureWithSecurityTokenDecryptionFailedException()
    {
        var key = new byte[32];
        new Random().NextBytes(key);

        var result = new BrancaTokenHandler().ValidateToken(
            ValidToken,
            new TokenValidationParameters {TokenDecryptionKey = new SymmetricSecurityKey(key)});

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenDecryptionFailedException>();
    }

    [Fact]
    public void ValidateToken_WhenTokenPayloadIsNotJson_ExpectFailureWithArgumentException()
    {
        const string tokenWithInvalidPayload = "Mvm6wbsyZMgClkmtiBf0lW3rEkvnCK5RgytoerJJex40b9yqh6GbSlfkFJHgFX9ocF";

        var result = new BrancaTokenHandler().ValidateToken(
            tokenWithInvalidPayload,
            new TokenValidationParameters {TokenDecryptionKey = new SymmetricSecurityKey(validKey)});

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public void ValidateToken_WhenValidToken_ExpectSuccessResultWithSecurityTokenAndClaimsIdentity()
    {
        var expectedIdentity = new ClaimsIdentity("test");
            
        var mockHandler = new Mock<BrancaTokenHandler> {CallBase = true};
        mockHandler.Protected()
            .Setup<TokenValidationResult>("ValidateTokenPayload",
                ItExpr.IsAny<BrancaSecurityToken>(),
                ItExpr.IsAny<TokenValidationParameters>())
            .Returns(new TokenValidationResult
            {
                ClaimsIdentity = expectedIdentity,
                IsValid = true
            });

        var result = mockHandler.Object.ValidateToken(
            ValidToken,
            new TokenValidationParameters {TokenDecryptionKey = new SymmetricSecurityKey(validKey)});

        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.Should().Be(expectedIdentity);
        result.SecurityToken.Should().NotBeNull();
    }

    [Fact]
    public void ValidateToken_WhenSaveSignInTokenIsTrue_ExpectIdentityBootstrapContext()
    {
        const string expectedToken = ValidToken;
        var expectedIdentity = new ClaimsIdentity("test");
            
        var mockHandler = new Mock<BrancaTokenHandler> {CallBase = true};
        mockHandler.Protected()
            .Setup<TokenValidationResult>("ValidateTokenPayload",
                ItExpr.IsAny<BrancaSecurityToken>(),
                ItExpr.IsAny<TokenValidationParameters>())
            .Returns(new TokenValidationResult
            {
                ClaimsIdentity = expectedIdentity,
                IsValid = true
            });

        var result = mockHandler.Object.ValidateToken(
            expectedToken,
            new TokenValidationParameters
            {
                TokenDecryptionKey = new SymmetricSecurityKey(validKey),
                SaveSigninToken = true
            });

        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.BootstrapContext.Should().Be(expectedToken);
    }
        
    [Fact]
    public void CreateAndValidateToken_WithSecurityTokenDescriptor_ExpectCorrectBrancaTimestampAndNoIatClaim()
    {
        const string issuer = "me";
        const string audience = "you";
        const string subject = "123";
        var expires = DateTime.UtcNow.AddDays(1);
        var notBefore = DateTime.UtcNow;
            
        var handler = new BrancaTokenHandler();

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires,
            NotBefore = notBefore,
            Claims = new Dictionary<string, object> {{"sub", subject}},
            EncryptingCredentials = new EncryptingCredentials(new SymmetricSecurityKey(validKey), ExtendedSecurityAlgorithms.XChaCha20Poly1305)
        });

        var validatedToken = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            TokenDecryptionKey = new SymmetricSecurityKey(validKey)
        });

        validatedToken.IsValid.Should().BeTrue();
        validatedToken.ClaimsIdentity.Claims.Should().Contain(
            x => x.Type == "sub" && x.Value == subject);

        var brancaToken = (BrancaSecurityToken) validatedToken.SecurityToken;
        brancaToken.Issuer.Should().Be(issuer);
        brancaToken.Audiences.Should().Contain(audience);
        brancaToken.Subject.Should().Be(subject);
        brancaToken.IssuedAt.Should().BeCloseTo(notBefore, TimeSpan.FromSeconds(1));
        brancaToken.ValidFrom.Should().BeCloseTo(notBefore, TimeSpan.FromSeconds(1));
        brancaToken.ValidTo.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetBrancaDecryptionKeys_WheInvalidKeysInParameters_ExpectInvalidKeysRemoved()
    {
        var expectedKey = new byte[32];
        new Random().NextBytes(expectedKey);
            
        var handler = new TestBrancaTokenHandler();
        var keys = handler.GetBrancaDecryptionKeys("test", new TokenValidationParameters
        {
            TokenDecryptionKeyResolver = (token, securityToken, kid, parameters) => new List<SecurityKey>(),
            TokenDecryptionKey = new SymmetricSecurityKey(expectedKey),
            TokenDecryptionKeys = new[] {new RsaSecurityKey(RSA.Create())}
        }).ToList();

        keys.Count.Should().Be(1);
        keys.Should().Contain(x => x.Key.SequenceEqual(expectedKey));
    }

    [Fact]
    public void IsValidKey_WhenKeyIsNot32Bytes_ExpectFalse()
    {
        var key = new byte[16];
        new Random().NextBytes(key);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(key);

        isValidKey.Should().BeFalse();
    }

    [Fact]
    public void IsValidKey_WhenKeyIsValid_ExpectTrue()
    {
        var key = new byte[32];
        new Random().NextBytes(key);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(key);

        isValidKey.Should().BeTrue();
    }

    [Fact]
    public void IsValidKey_WhenSecurityKeyIsNot32Bytes_ExpectFalse()
    {
        var keyBytes = new byte[16];
        new Random().NextBytes(keyBytes);
        var key = new SymmetricSecurityKey(keyBytes);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(key);

        isValidKey.Should().BeFalse();
    }

    [Fact]
    public void IsValidKey_WhenSecurityKeyIsNotSymmetricSecurityKey_ExpectFalse()
    {
        var key = new RsaSecurityKey(RSA.Create());

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(key);

        isValidKey.Should().BeFalse();
    }

    [Fact]
    public void IsValidKey_WhenSecurityKeyIsValid_ExpectTrue()
    {
        var keyBytes = new byte[32];
        new Random().NextBytes(keyBytes);
        var key = new SymmetricSecurityKey(keyBytes);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(key);

        isValidKey.Should().BeTrue();
    }

    [Fact]
    public void IsValidKey_WhenEcryptingCredentialsKeyIsNot32Bytes_ExpectFalse()
    {
        var keyBytes = new byte[16];
        new Random().NextBytes(keyBytes);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new EncryptingCredentials(key, ExtendedSecurityAlgorithms.XChaCha20Poly1305);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(credentials);

        isValidKey.Should().BeFalse();
    }

    [Fact]
    public void IsValidKey_WhenEcryptingCredentialsHasKeyWrappingSet_ExpectFalse()
    {
        var keyBytes = new byte[32];
        new Random().NextBytes(keyBytes);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new EncryptingCredentials(
            key, 
            SecurityAlgorithms.Aes256KeyWrap,
            ExtendedSecurityAlgorithms.XChaCha20Poly1305);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(credentials);

        isValidKey.Should().BeFalse();
    }

    [Fact]
    public void IsValidKey_WhenEcryptingCredentialsHasIncorrectEncryptionAlgorithm_ExpectFalse()
    {
        var keyBytes = new byte[32];
        new Random().NextBytes(keyBytes);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new EncryptingCredentials(key, SecurityAlgorithms.Aes128Encryption);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(credentials);

        isValidKey.Should().BeFalse();
    }

    [Fact]
    public void IsValidKey_WhenEcryptingCredentialsIsValid_ExpectTrue()
    {
        var keyBytes = new byte[32];
        new Random().NextBytes(keyBytes);
        var key = new SymmetricSecurityKey(keyBytes);
        var credentials = new EncryptingCredentials(key, ExtendedSecurityAlgorithms.XChaCha20Poly1305);

        var isValidKey = new TestBrancaTokenHandler().IsValidKey(credentials);

        isValidKey.Should().BeTrue();
    }

    private static byte[] CreateTestPayload()
    {
        var payload = new byte[32];
        RandomNumberGenerator.Fill(payload);
        return payload;
    }
}

public class TestBrancaTokenHandler : BrancaTokenHandler
{
    public new IEnumerable<SymmetricSecurityKey> GetBrancaDecryptionKeys(string token, TokenValidationParameters validationParameters) 
        => base.GetBrancaDecryptionKeys(token, validationParameters);

    public new bool IsValidKey(byte[] key) => base.IsValidKey(key);
    public new bool IsValidKey(SecurityKey key) => base.IsValidKey(key);
    public new bool IsValidKey(EncryptingCredentials credentials) => base.IsValidKey(credentials);
}