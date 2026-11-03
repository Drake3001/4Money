namespace ZPI4M.Grouping.Services.Groups;

public sealed class GroupIconException(int status) : Exception { public int Status { get; } = status; }
