# Nuke.Build.GitExtensions
Extra helper functions to help determine which projects have changed from a baseline in GIT

# Installation
GitPackager.Nuke can be installed using the Nuget package manager. 

```
Install-Package GitPackager.Nuke
```

# Support
* .NET Core 2.0
* .NET Framework 4.6.1 

## Dependency
* Nuke.Build.Common >= 0.20
* GitLib2Sharp >= 0.26

# Integration to build
First include the static using of the `GitPackager` into your `Build.cs`

```
using static Nuke.Git.Utilities.GitPackager.GitPackager;
```

For the current branch you can use the following code
```
DiffFromBaseline(RootDirectory, "baseline", (changes) =>
{
    var added = changes.Added.Select(s => s.Path);
});
```

For a check on a different branch you cna use the following code
```
DiffFromBaseline(RootDirectory, "baseline", "origin/branch", (changes) =>
{
    var added = changes.Added.Select(s => s.Path);
});
```

# Integration in Teamcity
Since buildagents in teamcity don't automatically copy the .git directory, this package automatically creates a copy of the git directory you use an other setting then `Automatically on agent`.