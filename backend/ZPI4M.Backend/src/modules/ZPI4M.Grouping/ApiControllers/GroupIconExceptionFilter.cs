using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ZPI4M.Grouping.Services.Groups;
namespace ZPI4M.Grouping.ApiControllers;

public sealed class GroupIconExceptionFilter : IExceptionFilter
{
	public void OnException(ExceptionContext context)
	{
		var status = context.Exception switch { GroupIconException e => e.Status, AmazonS3Exception => 503, HttpRequestException => 503, _ => 0 };
		if (status == 0) return;
		context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = status == 503 ? "Storage unavailable." : "Invalid group image." }) { StatusCode = status };
		context.ExceptionHandled = true;
	}
}
