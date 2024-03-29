using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace ScottBrady.IdentityModel.Tokens.Paseto;

[Obsolete("PASETO support is now deprecated. Please reach out via GitHub if you would like to see this feature maintained.")]
public class PasetoToken
{
    protected PasetoToken() { }
        
    public PasetoToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentNullException(nameof(token));
            
        var tokenParts = token.Split(new[] {'.'}, PasetoConstants.MaxPasetoSegmentCount + 1);
        if (tokenParts.Length != 3 && tokenParts.Length != 4) throw new ArgumentException("Invalid number of token segments");
            
        RawToken = token;

        Version = tokenParts[0];
        Purpose = tokenParts[1];
        EncodedPayload = tokenParts[2];
        if (tokenParts.Length == 4)
        {
            EncodedFooter = tokenParts[3];
            Footer = Base64UrlEncoder.Decode(EncodedFooter);
        }
    }
        
    public string RawToken { get; }
        
    public string Version { get; }
    public string Purpose { get; }
        
    public string EncodedPayload { get; }
    public string Payload { get; protected set; }
        
    public string EncodedFooter { get; }
    public string Footer { get; }

    public void SetPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentNullException(nameof(payload));
            
        try
        {
            JsonSerializer.Deserialize<Dictionary<string, object>>(payload);
            Payload = payload;
        }
        catch (Exception e)
        {
            throw new ArgumentException("Token does contain valid JSON", e);
        }
    }
}