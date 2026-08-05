using System;
using System.Runtime;

internal static class PerformanceRuntimeTuning
{
    public static void Apply()
    {
#if PERFORMANCE_RELEASE
        // Background GC remains enabled by the runtime configuration. SustainedLowLatency
        // prevents foreground Gen2 collections during normal play at the cost of retaining
        // more managed memory, which is the intended trade-off for the performance build.
        try
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        catch (InvalidOperationException)
        {
            // Keep startup robust if a host/runtime does not allow changing the latency mode.
        }
#endif
    }
}
