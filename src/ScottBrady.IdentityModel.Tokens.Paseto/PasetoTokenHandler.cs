using System;
using System.Collections.Generic;
using Microsoft.IdentityModel.Tokens;

namespace ScottBrady.IdentityModel.Tokens.Paseto
{
    [Obsolete("PASETO support is now deprecated. Please reach out via GitHub if you would like to see this feature maintained.")]
    public class PasetoTokenHandler : JwtPayloadTokenHandler
    {
        private readonly Dictionary<string, PasetoVersionStrategy> supportedVersions;

        public PasetoTokenHandler(Dictionary<string, PasetoVersionStrategy> supportedVersions = null)
        {
            this.supportedVersions = supportedVersions ?? new Dictionary<string, PasetoVersionStrategy>
            {
                {PasetoConstants.Versions.V1, new PasetoVersion1()},
                {PasetoConstants.Versions.V2, new PasetoVersion2()}
            };
        }

        public override bool CanReadToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (token.Length > MaximumTokenSizeInBytes) return false;

            var tokenParts = token.Split(new[] {'.'}, PasetoConstants.MaxPasetoSegmentCount + 1);
            if (tokenParts.Length != 3 && tokenParts.Length != 4) return false;

            return true;
        }

        public virtual string CreateToken(PasetoSecurityTokenDescriptor tokenDescriptor)
        {
            if (tokenDescriptor == null) throw new ArgumentNullException(nameof(tokenDescriptor));
            if (!(tokenDescriptor is PasetoSecurityTokenDescriptor pasetoSecurityTokenDescriptor))
                throw new ArgumentException($"Token descriptor must be of type '{typeof(PasetoSecurityTokenDescriptor)}'", nameof(tokenDescriptor));

            // get strategy for version + purpose
            if (!supportedVersions.TryGetValue(pasetoSecurityTokenDescriptor.Version, out var strategy))
            {
                throw new SecurityTokenException("Unsupported PASETO version");
            }
            
            // create payload
            var payload = tokenDescriptor.ToJwtPayload(JwtDateTimeFormat.Iso);

            // generate token
            string token;
            if (pasetoSecurityTokenDescriptor.Purpose == "local")
            {
                token = strategy.Encrypt(payload, null, pasetoSecurityTokenDescriptor.EncryptingCredentials);
            }
            else if (pasetoSecurityTokenDescriptor.Purpose == "public")
            {
                token = strategy.Sign(payload, null, pasetoSecurityTokenDescriptor.SigningCredentials);
            }
            else
            {
                throw new SecurityTokenException("Unsupported PASETO purpose");
            }

            return token;
        }

        public override TokenValidationResult ValidateToken(string token, TokenValidationParameters validationParameters)
        {
            if (string.IsNullOrWhiteSpace(token)) return new TokenValidationResult {Exception = new ArgumentNullException(nameof(token))};
            if (validationParameters == null) return new TokenValidationResult {Exception = new ArgumentNullException(nameof(validationParameters))};
            if (!CanReadToken(token)) return new TokenValidationResult {Exception = new SecurityTokenException("Unable to read token")};

            var pasetoToken = new PasetoToken(token);

            // get strategy for version + purpose
            if (!supportedVersions.TryGetValue(pasetoToken.Version, out var strategy))
            {
                return new TokenValidationResult {Exception = new SecurityTokenException("Unsupported PASETO version")};
            }

            PasetoSecurityToken pasetoSecurityToken;
            try
            {
                if (pasetoToken.Purpose == "local")
                {
                    var keys = GetDecryptionKeys(token, validationParameters);
                    pasetoSecurityToken = strategy.Decrypt(pasetoToken, keys);
                }
                else if (pasetoToken.Purpose == "public")
                {
                    var keys = GetSigningKeys(token, validationParameters);

                    // TODO: kid handling (footer?)

                    pasetoSecurityToken = strategy.Verify(pasetoToken, keys);
                }
                else
                {
                    return new TokenValidationResult {Exception = new SecurityTokenException("Unsupported PASETO purpose")};
                }
            }
            catch (Exception e)
            {
                return new TokenValidationResult {Exception = e};
            }

            var innerValidationResult = ValidateTokenPayload(pasetoSecurityToken, validationParameters);
            if (!innerValidationResult.IsValid) return innerValidationResult;

            var identity = innerValidationResult.ClaimsIdentity;
            if (validationParameters.SaveSigninToken) identity.BootstrapContext = token;

            return new TokenValidationResult
            {
                SecurityToken = pasetoSecurityToken,
                ClaimsIdentity = identity,
                IsValid = true
            };
        }
    }
}