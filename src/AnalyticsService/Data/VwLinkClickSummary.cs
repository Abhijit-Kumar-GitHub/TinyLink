using System;
using System.Collections.Generic;

namespace AnalyticsService.Data;

public partial class VwLinkClickSummary
{
    public string Code { get; set; } = null!;

    public long TotalClicks { get; set; }

    public DateTime? LastClickedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public long? ClicksLast24h { get; set; }
}
