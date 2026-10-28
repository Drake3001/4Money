using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Notifications.ApiControllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(INotificationService notifications, ICurrentUser currentUser) : ControllerBase
{
	[HttpPut("read-all")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		await notifications.MarkAllAsReadAsync(userId, cancellationToken);
		return NoContent();
	}

	[HttpPut("{notificationId:guid}/read")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public async Task<IActionResult> MarkAsRead(Guid notificationId, CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		var found = await notifications.MarkAsReadAsync(userId, notificationId, cancellationToken);
		return found ? NoContent() : Problem(
			statusCode: StatusCodes.Status404NotFound, title: "Notification not found.");
	}

	[HttpGet]
	[ProducesResponseType(typeof(PagedResult<NotificationResponse>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	public async Task<IActionResult> GetByUser(
		CancellationToken cancellationToken,
		[FromQuery, Range(1, int.MaxValue)] int pageNumber = 1,
		[FromQuery, Range(1, 100)] int pageSize = 20,
		[FromQuery] bool? isRead = null)
	{
		if (((long)pageNumber - 1) * pageSize > int.MaxValue)
		{
			ModelState.AddModelError(nameof(pageNumber), "The requested page offset is too large.");
			return ValidationProblem(ModelState);
		}

		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		var page = await notifications.GetByUserAsync(userId, pageNumber, pageSize, isRead, cancellationToken);
		return Ok(page);
	}
}
