namespace YAHAA.Services
{
    /// <summary>
    /// Outcome of a Home Assistant connection attempt.
    /// </summary>
    public sealed class ConnectionResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }

        /// <summary>Friendly name of the instance (from /api/config), if available.</summary>
        public string? LocationName { get; init; }

        /// <summary>Home Assistant version reported by the instance, if available.</summary>
        public string? Version { get; init; }

        public static ConnectionResult Ok(string? locationName, string? version) =>
            new() { Success = true, LocationName = locationName, Version = version };

        public static ConnectionResult Fail(string message) =>
            new() { Success = false, ErrorMessage = message };
    }
}
