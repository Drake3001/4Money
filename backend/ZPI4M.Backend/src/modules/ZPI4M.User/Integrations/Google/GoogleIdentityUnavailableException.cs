namespace ZPI4M.User.Integrations.Google;

public sealed class GoogleIdentityUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);
