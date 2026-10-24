using System;
using System.Collections.Generic;

namespace ZPI4M.Notifications.Models;

public partial class Notification
{
	public Guid Id { get; set; }

	public Guid? FkGroupId { get; set; }

	public string Text { get; set; } = null!;

	public DateTime SentAt { get; set; }
}
