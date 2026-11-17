using System.Text;
using ImageMagick;

namespace ZPI4M.Shared.Images;

public static class IconImageProcessor
{
	private const int MaxFileBytes = 5 * 1024 * 1024;
	public const string ContentType = "image/webp";

	public static async Task<byte[]> NormalizeAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		await using var buffer = new FileStream(
			Path.Combine(Path.GetTempPath(), $"icon-image-{Guid.NewGuid():N}"),
			FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
			FileOptions.Asynchronous | FileOptions.DeleteOnClose);
		var bytes = new byte[81920];
		int read;
		while ((read = await content.ReadAsync(bytes, cancellationToken)) > 0)
		{
			if (buffer.Length + read > MaxFileBytes)
				throw new IconImageException(413);
			await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
		}
		if (buffer.Length == 0)
			throw new IconImageException(400);

		buffer.Position = 0;
		var header = new byte[128];
		var count = await buffer.ReadAsync(header, cancellationToken);
		var png = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
		var jpeg = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
		var webp = count >= 12 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
		var heic = count >= 16 && Encoding.ASCII.GetString(header, 4, 4) == "ftyp" && HasHeicBrand(header.AsSpan(8, count - 8));
		var format = contentType.ToLowerInvariant() switch
		{
			"image/png" when png => MagickFormat.Png,
			"image/jpeg" when jpeg => MagickFormat.Jpeg,
			"image/webp" when webp => MagickFormat.WebP,
			"image/heic" or "image/heif" when heic => MagickFormat.Heic,
			_ => MagickFormat.Unknown
		};
		if (format == MagickFormat.Unknown)
			throw new IconImageException(415);

		buffer.Position = 0;
		using var normalized = new MemoryStream();
		try
		{
			var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };
			using var image = new MagickImage();
			await image.PingAsync(buffer, settings, cancellationToken);
			if (image.Width == 0 || image.Height == 0 || image.Width > 16384 || image.Height > 16384 || (ulong)image.Width * image.Height > 64_000_000)
				throw new IconImageException(415);
			buffer.Position = 0;
			await image.ReadAsync(buffer, settings, cancellationToken);
			image.AutoOrient();
			image.ColorSpace = ColorSpace.sRGB;
			image.Resize(new MagickGeometry(512, 512) { Greater = true });
			image.Strip();
			image.Quality = 85;
			await image.WriteAsync(normalized, MagickFormat.WebP, cancellationToken);
		}
		catch (MagickException)
		{
			throw new IconImageException(415);
		}
		cancellationToken.ThrowIfCancellationRequested();
		return normalized.ToArray();
	}

	private static bool HasHeicBrand(ReadOnlySpan<byte> brands)
	{
		for (var i = 0; i + 4 <= brands.Length; i += 4)
		{
			if (i == 4) continue;
			var brand = Encoding.ASCII.GetString(brands.Slice(i, 4));
			if (brand is "heic" or "heix" or "hevc" or "hevx") return true;
		}
		return false;
	}
}
