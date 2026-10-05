using System;
using System.Collections.Generic;

namespace AnalyticsService.Data;

public partial class ClickEvent
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public DateTime ClickedAtUtc { get; set; }

    public string? Referrer { get; set; }

    public string? UserAgent { get; set; }
}
