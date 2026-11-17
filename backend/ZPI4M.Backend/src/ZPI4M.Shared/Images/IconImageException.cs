namespace ZPI4M.Shared.Images;

public sealed class IconImageException(int status) : Exception
{
	public int Status { get; } = status;
}
