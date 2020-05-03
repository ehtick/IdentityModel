using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Signers;

namespace ScottBrady.IdentityModel.Tokens
{
    public class PasetoVersion2 : PasetoVersionStrategy
    {
        public override PasetoSecurityToken Decrypt(PasetoToken token, IEnumerable<SecurityKey> decryptionKeys)
        {
            throw new NotImplementedException();
        }

        public override PasetoSecurityToken Verify(PasetoToken token, IEnumerable<SecurityKey> signingKeys)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (signingKeys == null || !signingKeys.Any()) throw new ArgumentNullException(nameof(signingKeys));

            var keys = signingKeys.OfType<EdDsaSecurityKey>().ToList();
            if (!keys.Any()) throw new SecurityTokenInvalidSigningKeyException($"PASETO v2 requires key of type {typeof(EdDsaSecurityKey)}");
            
            if (token.Version != "v2") throw new ArgumentException("Invalid PASETO version");
            if (token.Purpose != "public") throw new ArgumentException("Invalid PASETO purpose");
            
            // decode payload
            var payload = Base64UrlEncoder.DecodeBytes(token.EncodedPayload);
            if (payload.Length < 64) throw new SecurityTokenInvalidSignatureException("Payload does not contain signature");

            // extract signature from payload (rightmost 64 bytes)
            var signature = new byte[64];
            Buffer.BlockCopy(payload, payload.Length - 64, signature, 0, 64);

            // decode payload JSON
            var message = new byte[payload.Length - 64];
            Buffer.BlockCopy(payload, 0, message, 0, payload.Length - 64);
            token.SetPayload(Encoding.UTF8.GetString(message));
            
            // pack
            var signedMessage = PreAuthEncode(new[]
            {
                Encoding.UTF8.GetBytes("v2.public."), 
                message,
                Base64UrlEncoder.DecodeBytes(string.Empty)
            });

            // verify signature using valid keys
            foreach (var publicKey in keys)
            {
                var signer = new Ed25519Signer();
                signer.Init(false, publicKey.KeyParameters);
                signer.BlockUpdate(signedMessage, 0, signedMessage.Length);
            
                var isValidSignature = signer.VerifySignature(signature);
                if (isValidSignature) return new PasetoSecurityToken(token);
            }

            throw new SecurityTokenInvalidSignatureException("Invalid PASETO signature");
        }
    }
}