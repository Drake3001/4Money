using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ZPI4M.User.Services.Profile;

namespace ZPI4M.User.ApiControllers;

public sealed class UserIconExceptionFilter : IExceptionFilter
{
	public void OnException(ExceptionContext context)
	{
		var status = context.Exception switch
		{
			UserIconException exception => exception.Status,
			AmazonS3Exception or HttpRequestException => 503,
			OperationCanceledException when !context.HttpContext.RequestAborted.IsCancellationRequested => 503,
			_ => 0
		};
		if (status == 0)
			return;
		context.Result = new ObjectResult(new ProblemDetails
		{
			Status = status,
			Title = status == 503 ? "Storage unavailable." : "Invalid profile image."
		})
		{ StatusCode = status };
		context.ExceptionHandled = true;
	}
}
