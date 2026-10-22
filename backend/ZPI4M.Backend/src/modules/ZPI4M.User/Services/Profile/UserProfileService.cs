using Npgsql;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Profile;
using ZPI4M.User.Repositories.UserRepository;

namespace ZPI4M.User.Services.Profile;

public sealed class UserProfileService(IUserRepository users, UserDbContext dbContext) : IUserProfileService
{
	public async Task<UserProfileResponse?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await users.GetByIdAsync(userId, cancellationToken);
		return user is { IsActive: true, IsEmailConfirmed: true } ? ToResponse(user) : null;
	}

	public async Task<(UpdateUserProfileResult Result, UserProfileResponse? Profile)> UpdateAsync(
		Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(userId, cancellationToken);
		if (user is not { IsActive: true, IsEmailConfirmed: true })
		{
			return (UpdateUserProfileResult.UserUnavailable, null);
		}

		if (user.Login != request.Login && await users.ExistsByLoginAsync(request.Login, cancellationToken))
		{
			return (UpdateUserProfileResult.LoginAlreadyInUse, null);
		}

		user.Login = request.Login;
		user.Name = request.Name;
		user.Surname = request.Surname;
		user.Phone = request.Phone;
		user.BirthDate = request.BirthDate;
		user.Icon = request.Icon;

		try
		{
			await users.UpdateProfileAsync(user, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}
		catch (PostgresException exception) when (exception is
		{
			SqlState: PostgresErrorCodes.UniqueViolation,
			ConstraintName: "c_user_unique_login"
		})
		{
			return (UpdateUserProfileResult.LoginAlreadyInUse, null);
		}

		return (UpdateUserProfileResult.Updated, ToResponse(user));
	}

	private static UserProfileResponse ToResponse(Models.User user)
	{
		return new UserProfileResponse(user.Id, user.Login, user.Name, user.Surname, user.Email,
			user.Phone, user.BirthDate, user.Icon, user.IsEmailConfirmed, user.CreatedAt, user.LastLoginTime);
	}
}
