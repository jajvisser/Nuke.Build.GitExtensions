using System.Diagnostics;
using System.Linq;
using LibGit2Sharp;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Configuration = Nuke.Common.Configuration;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Git.Extensions.GitPackager.GitPackagerTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tooling;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;

    [Parameter]
    readonly string BuildVersion = "0.1";

    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));

            
        });
    
    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(o => o.SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(BuildVersion)
                .SetFileVersion(BuildVersion)
                .SetInformationalVersion(BuildVersion));
        });

    [Parameter] readonly string Source = "https://api.nuget.org/v3/index.json";
    [Parameter] readonly string ApiKey;

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(Solution)
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .EnableIncludeSymbols()
                .SetVersion(BuildVersion)
                .SetOutputDirectory(OutputDirectory));
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
                    .SetSource(Source)
                    .SetApiKey(ApiKey)
                    .CombineWith(
                        OutputDirectory.GlobFiles("*.nupkg").NotEmpty(), (cs, v) => cs
                            .SetTargetPath(v)),
                degreeOfParallelism: 5,
                completeOnFailure: true);
        })

    #region Test case of git diff

    [Parameter]
    readonly string Repository = "https://github.com/jajvisser/Nuke.Build.GitExtensions.git";
    [Parameter]
    readonly string GitUsername;
    [Parameter]
    readonly string GitPassword;

    Target TestGit => _ => _
        .Requires(() => Repository)
        .Requires(() => GitUsername)
        .Requires(() => GitPassword)
        .Executes(() =>
        {
            var gitMirror = RootDirectory / ".gitmirror";
            EnsureCleanDirectory(gitMirror);

            // Diff from remote baseline
            DiffFromBaseline(gitMirror, Repository, "baseline", "origin/test-branch", (changes) =>
            {
                var added = changes.Added.Select(s=>s.Path);
                Debug.Assert(added.Contains("test-file.txt"));
            }, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });

            // Diff from local baseline
            DiffFromBaseline(RootDirectory, "baseline", (changes) =>
            {
                var added = changes.Added.Select(s => s.Path);
                Debug.Assert(!added.Contains("test-file.txt"));
            });

            DiffFromBaseline(RootDirectory, "baseline", "origin/test-branch", (changes) =>
            {
                var added = changes.Added.Select(s => s.Path);
                Debug.Assert(added.Contains("test-file.txt"));
            });
        });
    #endregion
}
