using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ZPI4M.Banking.ApiControllers;

internal static class BankingBrowserResponse
{
	private static readonly Lazy<string> LogoDataUrl = new(() =>
	{
		using var stream = typeof(BankingBrowserResponse).Assembly.GetManifestResourceStream("ZPI4M.Banking.Logo.png")
			?? throw new InvalidOperationException("Banking logo resource is missing.");
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
	});

	public static ContentResult? Create(HttpRequest request, bool success, int statusCode = StatusCodes.Status200OK)
	{
		if (request.GetTypedHeaders().Accept?.Any(value => value.MediaType.Value == "text/html"
				&& value.Quality.GetValueOrDefault(1) > 0) != true)
			return null;

		var response = request.HttpContext.Response;
		response.Headers["Referrer-Policy"] = "no-referrer";
		response.Headers["X-Content-Type-Options"] = "nosniff";
		response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'";
		var message = success ? "Połączenie z bankiem zakończone sukcesem." : "Połączenie z bankiem zakończone niepowodzeniem.";
		// A standalone result stays on the callback URL and never opens another frontend session.
		return new ContentResult
		{
			ContentType = "text/html; charset=utf-8",
			StatusCode = statusCode,
			Content = $$"""
				<!doctype html>
				<html lang="pl">
				<head>
				<meta charset="utf-8">
				<meta name="viewport" content="width=device-width, initial-scale=1">
				<title>4Money - Połączenie z bankiem</title>
				<link rel="icon" type="image/png" href="{{LogoDataUrl.Value}}">
				<style>
				* { box-sizing: border-box; }
				body { margin: 0; min-height: 100vh; padding: 40px 16px; background: #f5f8fa; color: #07183a; font-family: system-ui, sans-serif; }
				main { width: 100%; max-width: 448px; margin: 0 auto; }
				.brand { display: block; width: 80px; height: 80px; object-fit: contain; margin: 0 auto 24px; }
				.card { background: white; border: 1px solid #e2e6eb; border-radius: 16px; padding: 24px; }
				h1 { margin: 0 0 20px; font-size: 24px; }
				.result { display: flex; align-items: flex-start; gap: 12px; border-radius: 12px; padding: 16px; font-size: 14px; line-height: 1.5; }
				.success { background: #e6f7f0; color: #0a7f5d; }
				.failure { background: #fdecee; color: #c0343f; }
				.icon { flex: none; font-size: 20px; line-height: 1; }
				p { margin: 20px 0 0; color: #52607d; font-size: 14px; line-height: 1.6; }
				</style>
				</head>
				<body><main>
				<img class="brand" src="{{LogoDataUrl.Value}}" alt="4Money" width="80" height="80">
				<section class="card" aria-labelledby="title">
				<h1 id="title">Połączenie z bankiem</h1>
				<div class="result {{(success ? "success" : "failure")}}" role="{{(success ? "status" : "alert")}}">
				<span class="icon" aria-hidden="true">{{(success ? "✓" : "✕")}}</span>
				<span>{{message}}</span>
				</div>
				<p>Możesz zamknąć tę kartę i wrócić do karty 4Money, w której rozpoczęto łączenie z bankiem.</p>
				</section></main></body>
				</html>
				"""
		};
	}
}
