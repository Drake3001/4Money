namespace ZPI4M.User.DTOs.Authentication;

public sealed record GoogleChallengeResponse(string ClientId, string Nonce, DateTimeOffset ExpiresAt);
