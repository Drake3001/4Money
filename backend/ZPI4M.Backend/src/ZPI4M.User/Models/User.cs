using System;
using System.Collections.Generic;

namespace ZPI4M.User.Models;

public partial class User
{
    public Guid Id { get; set; }

    public string Login { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Surname { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public DateOnly BirthDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginTime { get; set; }

    public bool IsActive { get; set; }

    public string? Icon { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiryTime { get; set; }

    public string? TinkUserId { get; set; }
}
