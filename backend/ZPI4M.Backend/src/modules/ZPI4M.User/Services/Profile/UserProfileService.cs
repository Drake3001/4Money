using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Profile;
using ZPI4M.User.Repositories.UserRepository;

namespace ZPI4M.User.Services.Profile;

public sealed class UserProfileService(
	IUserRepository users,
	UserDbContext dbContext,
	IUserIconService icons,
	ILogger<UserProfileService> logger) : IUserProfileService
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
		if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true))
			return (UpdateUserProfileResult.InvalidRequest, null);
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

		if (request.Icon is not null && !await icons.IsPresetAsync(request.Icon, cancellationToken))
			return (UpdateUserProfileResult.InvalidRequest, null);

		var oldKey = user.IconStorageKey;
		string? uploadedKey = null;
		var commitStarted = false;
		try
		{
			if (request.IconContent is not null)
				uploadedKey = await icons.UploadAsync(userId, request.IconContent, request.IconContentType ?? string.Empty, cancellationToken);
			user.Login = request.Login;
			user.Name = request.Name;
			user.Surname = request.Surname;
			user.Phone = request.Phone;
			user.BirthDate = request.BirthDate;
			if (request.RemoveIcon || request.Icon is not null || uploadedKey is not null)
			{
				user.Icon = request.Icon;
				user.IconStorageKey = uploadedKey;
			}
			await users.UpdateProfileAsync(user, cancellationToken);
			commitStarted = true;
			await transaction.CommitAsync(cancellationToken);
		}
		catch (PostgresException exception) when (!commitStarted && exception is
		{
			SqlState: PostgresErrorCodes.UniqueViolation,
			ConstraintName: "c_user_unique_login"
		})
		{
			await RollbackAsync(transaction, uploadedKey);
			return (UpdateUserProfileResult.LoginAlreadyInUse, null);
		}
		catch
		{
			if (!commitStarted)
				await RollbackAsync(transaction, uploadedKey);
			else if (uploadedKey is not null)
				logger.LogWarning("Profile image commit failed; uploaded file retained.");
			throw;
		}

		if (oldKey is not null && oldKey != user.IconStorageKey && oldKey.StartsWith($"users/{userId}/icons/", StringComparison.Ordinal))
			await icons.CleanupAsync(oldKey);

		return (UpdateUserProfileResult.Updated, ToResponse(user));
	}

	private async Task RollbackAsync(IDbContextTransaction transaction, string? uploadedKey)
	{
		await transaction.RollbackAsync(CancellationToken.None);
		if (uploadedKey is not null)
			await icons.CleanupAsync(uploadedKey);
	}

	private static UserProfileResponse ToResponse(Models.User user)
	{
		return new UserProfileResponse(user.Id, user.Login, user.Name, user.Surname, user.Email,
			user.Phone, user.BirthDate, user.Icon, user.IsEmailConfirmed, user.CreatedAt, user.LastLoginTime,
			user.PasswordHash is not null, user.GoogleSubject is not null, user.IconStorageKey);
	}
}
