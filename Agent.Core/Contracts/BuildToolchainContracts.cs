namespace Agent.Rpa.Contracts
{
    /// <summary>
    /// AiRpa-specific: reports the MSBuild toolchain a build-feedback service is using
    /// and lets the caller pin it (auto / vs2022 / vs2026).
    /// </summary>
    public interface IBuildToolchainAware
    {
        string MsBuildToolchain { get; set; }
        string DescribeToolchain();
    }
}