using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using GitPackager.Nuke.Tools.Constants;
using LibGit2Sharp;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Configuration = Nuke.Common.Configuration;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static GitPackager.Nuke.Tools.GitPackagerTasks;
using static GitPackager.Nuke.Tools.GitTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tooling;
using Nuke.Common.BuildServers;

[CheckBuildProjectConfigurations]
[DotNetVerbosityMapping]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    // Default build action
    public static int Main() => Execute<Build>(x => x.Compile);

    // Description fields
    const string Description = "Nuke Build to help filter projects based on a baseline tag. This also clone its own .git directory. Based on the Cake GitPackager.";
    const string Author = "Joris Visser";

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [Parameter] string BuildVersion;

    [Parameter] readonly string Source = "https://api.nuget.org/v3/index.json";
    [Parameter] readonly string ApiKey;

    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";

    #region Default build tasks

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
            if (TeamCity.Instance != null)
            {
                BuildVersion = TeamCity.Instance.BuildNumber;
                Logger.Info($"{BuildVersion} is used as a buildserver");
            }

            DotNetBuild(o => o.SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(BuildVersion)
                .SetFileVersion(BuildVersion)
                .SetInformationalVersion(BuildVersion));
        });
    
    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            if (TeamCity.Instance != null)
            {
                BuildVersion = TeamCity.Instance.BuildNumber;
                Logger.Info($"{BuildVersion} is used as a buildserver");
            }

            DotNetPack(s => s
                .SetProject(Solution)
                .EnableNoBuild()
                .SetDescription(Description)
                .SetAuthors(Author)
                .SetPackageReleaseNotes(GetReleaseNotes())
                .SetPackageLicenseUrl("https://licenses.nuget.org/MIT")
                .SetPackageRequireLicenseAcceptance(false)
                .SetConfiguration(Configuration)
                .SetVersion(BuildVersion)
                .SetOutputDirectory(OutputDirectory));
        });

    static string GetReleaseNotes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "ReleaseNotes.txt";

        var notes = assembly.GetManifestResourceNames();
        
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        using (var reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }

    Target Push => _ => _
        .DependsOn(Pack)
        .Requires(() => Configuration == Configuration.Release)
        .Requires(() => ApiKey)
        .Requires(() => Source)
        .Executes(() =>
        {
            if (TeamCity.Instance != null)
            {
                BuildVersion = TeamCity.Instance.BuildNumber;
                Logger.Info($"{BuildVersion} is used as a buildserver");
            }

            DotNetNuGetPush(s => s
                    .SetSource(Source)
                    .SetApiKey(ApiKey)
                    .CombineWith(
                        OutputDirectory.GlobFiles( $"*{BuildVersion}.nupkg").NotEmpty(),
                        (cs, v) => cs.SetTargetPath(v)), degreeOfParallelism: 5, completeOnFailure: true);
        });

    Target Tag => _ => _
        .Requires(() => GitUsername)
        .Requires(() => GitPassword)
        .Executes(() =>
        {
            var tempTag = Guid.NewGuid().ToString();

            // Create tag
            CreateTag(tempTag, RootDirectory, Repository, (url, fromUrl, types) => new UsernamePasswordCredentials { Password = GitPassword, Username = GitUsername});

            // Delete tag
            DeleteTag(tempTag, RootDirectory, Repository, (url, fromUrl, types) => new UsernamePasswordCredentials { Password = GitPassword, Username = GitUsername });
        });

#endregion

    #region Test case of git diff

    [Parameter]
    readonly string Repository = "https://github.com/jajvisser/Nuke.Build.GitExtensions.git";
    [Parameter]
    readonly string GitUsername;
    [Parameter]
    readonly string GitPassword;

    Target TestGitBaseline => _ => _
        .Requires(() => Repository)
        .Requires(() => GitUsername)
        .Requires(() => GitPassword)
        .Executes(() =>
        {
            Logger.Info("Testing baseline with branch test-branch");
            if (TeamCity.Instance != null)
            {
                EnsureCleanDirectory(RootDirectory / GitConstants.GitDirectory);
            }

            // Diff from remote baseline
            DiffFromBaseline(RootDirectory, "baseline", "test-branch", (changes) =>
            {
                var added = changes.Added.Select(s => s.Path);
                Debug.Assert(added.Contains("test-file.txt"));
            }, (url, x, y) => new UsernamePasswordCredentials() {Username = GitUsername, Password = GitPassword});

            Logger.Info("Testing baseline with current branch");
            if (TeamCity.Instance != null)
            {
                EnsureCleanDirectory(RootDirectory / GitConstants.GitDirectory);
            }

            // Diff from remote baseline
            DiffFromBaseline(RootDirectory, "baseline", (changes) =>
            {
                var added = changes.Added.Select(s => s.Path);
                Debug.Assert(added.Contains("test-file.txt"));
            }, (url, x, y) => new UsernamePasswordCredentials() {Username = GitUsername, Password = GitPassword});
        });

    Target TestGitTasks => _ => _
        .DependsOn(TestGitBaseline)
        .Requires(() => Repository)
        .Requires(() => GitUsername)
        .Requires(() => GitPassword)
        .Executes(() =>
        {
            // Reset test scenario
            DeleteTag("test-tag", RootDirectory, Repository, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });

            // Create tag
            CreateTag("test-tag", RootDirectory, Repository, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });

            // Delete tag
            DeleteTag("test-tag", RootDirectory, Repository, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });

            // RecreateTag
            ResetTag("test-tag", RootDirectory, Repository, (url, x, y) => new UsernamePasswordCredentials() { Username = GitUsername, Password = GitPassword });
        });

    Target TestGit => _ => _
        .DependsOn(TestGitTasks)
        .Executes(() => { });

    #endregion
}
