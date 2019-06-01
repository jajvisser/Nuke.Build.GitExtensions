using System.Diagnostics;
using System.Linq;
using GitPackager.Nuke.Tools;
using LibGit2Sharp;
using Microsoft.Build.Tasks;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Configuration = Nuke.Common.Configuration;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static GitPackager.Nuke.Tools.GitPackagerTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tooling;
using GitPackager.Nuke.Tools;
using Nuke.Common.BuildServers;

[CheckBuildProjectConfigurations]
[DotNetVerbosityMapping]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    const string Description = "Nuke Build to help filter projects based on a baseline tag. This also clone its own .git directory. Based on the Cake GitPackager";
    const string Author = "Joris Visser";
    const string ReleaseNotes = "1.0 - Initial release";

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;

    [Parameter] readonly string BuildVersion;

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
                .SetDescription(Description)
                .SetAuthors(Author)
                .SetPackageReleaseNotes(ReleaseNotes)
                .SetPackageLicenseUrl("https://licenses.nuget.org/MIT")
                .SetPackageRequireLicenseAcceptance(false)
                .SetConfiguration(Configuration)
                .SetVersion(BuildVersion)
                .SetOutputDirectory(OutputDirectory));
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Requires(() => Configuration == Configuration.Release)
        .Requires(() => ApiKey)
        .Requires(() => Source)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
                    .SetSource(Source)
                    .SetApiKey(ApiKey)
                    .CombineWith(
                        OutputDirectory.GlobFiles("*.nupkg").NotEmpty(),
                        (cs, v) => cs.SetTargetPath(v)), degreeOfParallelism: 5, completeOnFailure: true);
        });

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
            TeamcityDiffFromBaseline(gitMirror, "baseline", "origin/test-branch", (changes) =>
            {
                var added = changes.Added.Select(s=>s.Path);
                Debug.Assert(added.Contains("test-file.txt"));
            }, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });

            EnsureCleanDirectory(gitMirror);
            // Diff from remote baseline
            TeamcityDiffFromBaseline(gitMirror, "baseline", (changes) =>
            {
                var added = changes.Added.Select(s => s.Path);
                Debug.Assert(added.Contains("test-file.txt"));
            }, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });

            if (TeamCity.Instance == null)
            {
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
            }
        });
    #endregion
}
