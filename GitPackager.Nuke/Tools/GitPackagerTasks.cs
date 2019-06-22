using System;
using System.Linq;
using GitPackager.Nuke.GitWrapper;
using GitPackager.Nuke.Tools.Constants;
using GitPackager.Nuke.Tools.Exceptions;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Nuke.Common;
using Nuke.Common.BuildServers;
using Nuke.Common.IO;
using static Nuke.Common.IO.FileSystemTasks;

namespace GitPackager.Nuke.Tools
{
    public static class GitPackagerTasks
    {
        private static PathConstruction.AbsolutePath RootDirectory => (PathConstruction.AbsolutePath)EnvironmentInfo.WorkingDirectory;

        private static bool HasGitDirectory(PathConstruction.AbsolutePath projectPath)
        {
            return DirectoryExists(projectPath / ".git");
        }

        /// <summary>
        /// Find what files have changed between the current commit on the current branch and a baseline tag and return the changes
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        /// <param name="credentialsHandler">LibGit2Sharp credential handler</param>
        public static void DiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler = null)
        {
            if (HasGitDirectory(projectPath))
            {
                LocalDiffFromBaseline(projectPath, baselineName, null, diffAction);
            }
            else
            {
                var newGitProjectPath = projectPath / GitConstants.GitMirrorDirectory;
                TeamcityDiffFromBaseline(newGitProjectPath, baselineName, diffAction, credentialsHandler);
            }
        }

        /// <summary>
        /// Find what files have changed between the current commit on a specific branch and a baseline tag and return the changes
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="branchName">Branch name that is taken as diff</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        /// <param name="credentialsHandler">LibGit2Sharp credential handler</param>
        public static void DiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, string branchName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler)
        {
            if (HasGitDirectory(projectPath))
            {
                LocalDiffFromBaseline(projectPath, baselineName, branchName, diffAction);
            }
            else
            {
                var newGitProjectPath = projectPath / GitConstants.GitMirrorDirectory;
                TeamcityDiffFromBaseline(newGitProjectPath, baselineName, branchName, diffAction, credentialsHandler);
            }
        }

        #region Local Git Repository

        /// <summary>
        /// With an existing project
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="branchName">Branch name that is taken as diff</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        internal static void LocalDiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, string branchName, Action<TreeChanges> diffAction)
        {
            if(string.IsNullOrEmpty(projectPath))
            {
                throw new ArgumentNullException("projectPath needs to refer to the .git directory");
            }

            if(string.IsNullOrEmpty(baselineName))
            {
                throw new ArgumentNullException("baseline needs to be defined to work with");
            }

            // Local repository
            var repository = new Repository(projectPath);
            var currentBranch = branchName;
            if (branchName == null)
            {
                currentBranch = repository.Head.FriendlyName;
            }

            Logger.Info($"Repository on {projectPath / ".git"} initialized");
            DiffFromBaselineInternal(repository, diffAction, baselineName, currentBranch);
        }

        #endregion

        #region Teamcity with .gitmirror

        internal static void TeamcityDiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler)
        {
            if (TeamCity.Instance == null)
            {
                throw new NoTeamcityInstanceException("No teamcity instance detected");
            }

            var repositoryUrl = TeamCity.Instance.ConfigurationProperties[TeamcityConstants.VcsRootUrl];
            var currentBranch = TeamCity.Instance.ConfigurationProperties[TeamcityConstants.VcsBranch];
            if (currentBranch == null)
            {
                throw new NoTeamcityInstanceException($"Configuration property teamcity_build_vcs_branch");
            }

            Logger.Info($"Starting rebuilding repository {repositoryUrl} and branch {currentBranch}");
            var repository = GitRepositoryBuilder.CloneTempRepository(repositoryUrl, projectPath, credentialsHandler);
            Logger.Info($"Finished rebuilding repository {repositoryUrl} and branch {currentBranch}");

            DiffFromBaselineInternal(repository, diffAction, baselineName, currentBranch);
        }

        internal static void TeamcityDiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, string branchName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler)
        {
            if (TeamCity.Instance == null)
            {
                throw new NoTeamcityInstanceException("No teamcity instance detected");
            }

            var repositoryUrl = TeamCity.Instance.ConfigurationProperties[TeamcityConstants.VcsRootUrl];

            Logger.Info($"Starting rebuilding repository {repositoryUrl}");
            var repository = GitRepositoryBuilder.CloneTempRepository(repositoryUrl, projectPath, credentialsHandler);
            Logger.Info($"Finished rebuilding repository {repositoryUrl}");

            DiffFromBaselineInternal(repository, diffAction, baselineName, branchName);
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Internal function to run the baseline diff
        /// </summary>
        /// <param name="repository">Retrieved repository instance</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="branchName">Branch name that is taken as diff</param>
        private static void DiffFromBaselineInternal(Repository repository, Action<TreeChanges> diffAction, string baselineName, string branchName = null)
        {
            using (repository)
            {
                var baselineCommit = repository.FindBaseline(baselineName);
                var branchCommits = repository.FindBranchCommit(branchName);

                if (baselineCommit == null)
                {
                    throw new InvalidOperationException($"Tag and Commit are not found for {baselineName}");
                }

                if (branchCommits == null)
                {
                    throw new InvalidOperationException($"Branch not found {branchName}, following branches found {string.Join(", ", repository.Branches.Select(s => s.FriendlyName))}");
                }

                var filter = new CommitFilter { ExcludeReachableFrom = baselineCommit, IncludeReachableFrom = branchCommits };
                var allCommits = repository.Commits.QueryBy(filter).ToList();
                var branchCommitSha = allCommits.First().Sha;
                var baselineCommitSha = baselineCommit.Sha;
                Logger.Info($"Processing changes from commit='{baselineCommitSha}' to commit='{branchCommitSha}' on branch {branchName}");

                if (allCommits.Any())
                {
                    var newCommit = branchCommits.First();
                    var oldCommit = allCommits.Count == 1 ? newCommit.Parents.First() : allCommits.Last();

                    var changes = repository.Diff.Compare<TreeChanges>(oldCommit.Tree, newCommit.Tree);
                    diffAction(changes);
                }
                else
                {
                    Logger.Info("No commits found on repository");
                }
            }
        }

        #endregion
    }
}
