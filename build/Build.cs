using System;
using System.Diagnostics;
using System.Linq;
using LibGit2Sharp;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Utilities.Collections;
using Configuration = Nuke.Common.Configuration;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;
using static Nuke.Git.Utilities.GitPackager.GitPackager;
using Nuke.Git.Utilities.GitPackager;

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
    [GitRepository] readonly GitRepository GitRepository;

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
            MSBuild(s => s
                .SetTargetPath(Solution)
                .SetTargets("Restore"));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            MSBuild(s => s
                .SetTargetPath(Solution)
                .SetTargets("Rebuild")
                .SetConfiguration(Configuration)
                .SetMaxCpuCount(Environment.ProcessorCount)
                .SetNodeReuse(IsLocalBuild));
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {

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
