using System;
using System.Collections.Generic;

namespace YAHAA.Services
{
    /// <summary>A reportable sensor: its HA identity, icons, and how to read its current value.</summary>
    public sealed record SensorInfo(string Id, string DisplayName, string OnIcon, string OffIcon, Func<bool> Read);

    /// <summary>The fixed set of status sensors YAHAA can report to Home Assistant.</summary>
    public static class SensorCatalog
    {
        public static IReadOnlyList<SensorInfo> All { get; } = new List<SensorInfo>
        {
            new("active", "Active", "mdi:monitor", "mdi:monitor-off",
                () => Activity.IsActive(AppSettings.IdleThresholdSeconds)),
            new("camera", "Camera", "mdi:webcam", "mdi:webcam-off",
                CapabilityUsage.IsCameraInUse),
            new("microphone", "Microphone", "mdi:microphone", "mdi:microphone-off",
                CapabilityUsage.IsMicrophoneInUse),
            new("camera_or_microphone", "Camera or Microphone", "mdi:video", "mdi:video-off",
                () => CapabilityUsage.IsCameraInUse() || CapabilityUsage.IsMicrophoneInUse()),
        };
    }
}
