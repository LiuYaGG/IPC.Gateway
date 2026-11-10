using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Gateway;

internal static class ProjectConfigurationChange
{
    public static bool IsSame(ProjectConfig left, ProjectConfig right) =>
        ProjectProtobufCodec.SerializeProject(left).AsSpan().SequenceEqual(ProjectProtobufCodec.SerializeProject(right));

    public static bool RequiresRuleRebuild(ProjectConfig left, ProjectConfig right)
    {
        // Descriptive tag metadata is not used to compile rule topology. Other
        // changes remain conservative until their runtime dependencies are known.
        static ProjectConfig WithoutDescriptions(ProjectConfig source)
        {
            ProjectConfig copy = ProjectConfigCloner.Clone(source);
            foreach (var device in copy.Devices)
            {
                foreach (var tag in device.Tags) tag.Description = string.Empty;
                foreach (var group in device.Groups)
                    foreach (var tag in group.Tags) tag.Description = string.Empty;
            }
            return copy;
        }
        return !IsSame(WithoutDescriptions(left), WithoutDescriptions(right));
    }
}
