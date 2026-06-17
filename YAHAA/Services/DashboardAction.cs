namespace YAHAA.Services
{
    /// <summary>
    /// A user-defined dashboard action that fires a Home Assistant webhook (typically the trigger
    /// of a webhook-triggered automation). <see cref="Webhook"/> may be a full URL or just the
    /// webhook id, in which case it is resolved against the configured Home Assistant URL.
    /// </summary>
    public sealed class DashboardAction
    {
        public string Name { get; set; } = string.Empty;
        public string Webhook { get; set; } = string.Empty;
    }
}
