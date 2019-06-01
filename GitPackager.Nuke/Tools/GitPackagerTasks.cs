using System;
using System.Linq;
using GitPackager.Nuke.Tools.Exceptions;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Nuke.Common;
using Nuke.Common.BuildServers;
using Nuke.Common.IO;

namespace GitPackager.Nuke.Tools
{
    public static class GitPackagerTasks
    {
        /// <summary>
        /// With an existing project
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        public static void DiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, Action<TreeChanges> diffAction)
        {
            DiffFromBaseline(projectPath, baselineName, null, diffAction);
        }

        /// <summary>
        /// With an existing project
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="branchName">Branch name that is taken as diff</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        public static void DiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, string branchName, Action<TreeChanges> diffAction)
        {
            if(string.IsNullOrEmpty(projectPath))
            {
                throw new ArgumentNullException("projectPath needs to refer to the .git directory");
            }

            if(string.IsNullOrEmpty(baselineName))
            {
                throw new ArgumentNullException("baseline needs to be defined to work with");
            }

            // 
            var repository = new Repository(projectPath);
            Logger.Info($"Repository on {projectPath} initialized");
            DiffFromBaselineInternal(repository, diffAction, baselineName, branchName);
        }

        /// <summary>
        /// Workaround for agents in teamcity, since the .git directory is not copied from teamcity to the agent.
        /// This function regenerates the .git directory so that see the diff
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        /// <param name="credentialsHandler">LibGit2Sharp credential handler</param>
        public static void TeamcityDiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler)
        {
            if (TeamCity.Instance == null)
            {
                throw new NoTeamcityInstanceException("No teamcity instance detected");
            }

            var repositoryUrl = TeamCity.Instance.ConfigurationProperties["vcsroot_url"];
            var currentBranch = TeamCity.Instance.ConfigurationProperties.FirstOrDefault(s=>s.Key.StartsWith("teamcity_build_vcs_branch"));
            if (currentBranch.Key == null)
            {
                throw new NoTeamcityInstanceException($"Configuration property teamcity_build_vcs_branch");
            }

            Logger.Info($"Starting rebuilding repository {repositoryUrl} and branch {currentBranch.Value}");
            var destination = Repository.Clone(repositoryUrl, projectPath, GetCloneOptions(credentialsHandler));
            var repository = new Repository(destination);
            Logger.Info($"Finished rebuilding repository {repositoryUrl} and branch {currentBranch.Value}");
            DiffFromBaselineInternal(repository, diffAction, baselineName, currentBranch.Value);
        }

        /// <summary>
        /// Workaround for agents in teamcity, since the .git directory is not copied from teamcity to the agent.
        /// This function regenerates the .git directory so that see the diff
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="branchName">Branch name that is taken as diff</param>
        /// <param name="diffAction">Action that is executed with the changes that are detected</param>
        /// <param name="credentialsHandler">LibGit2Sharp credential handler</param>
        public static void TeamcityDiffFromBaseline(PathConstruction.AbsolutePath projectPath, string baselineName, string branchName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler)
        {
            if (TeamCity.Instance == null)
            {
                throw new NoTeamcityInstanceException("No teamcity instance detected");
            }

            var repositoryUrl = TeamCity.Instance.ConfigurationProperties["vcsroot_url"];

            Logger.Info($"Starting rebuilding repository {repositoryUrl}");
            var destination = Repository.Clone(repositoryUrl, projectPath, GetCloneOptions(credentialsHandler));
            var repository = new Repository(destination);
            Logger.Info($"Finished rebuilding repository {repositoryUrl}");
            DiffFromBaselineInternal(repository, diffAction, baselineName, branchName);
        }

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
                var baselineCommit = FindBaseline(repository, baselineName);
                var branchCommits = FindBranchCommit(repository, branchName);

                if (baselineCommit == null)
                {
                    throw new InvalidOperationException($"Tag and Commit are not found for {baselineName}");
                }

                if (branchCommits == null)
                {
                    throw new InvalidOperationException($"Branch not found {branchName}, following branches found {string.Join(", ", repository.Branches.Select(s=>s.FriendlyName))}");
                }

                var filter = new CommitFilter { ExcludeReachableFrom = baselineCommit, IncludeReachableFrom = branchCommits };
                var allCommits = repository.Commits.QueryBy(filter).ToList();
                var branchCommitSha = allCommits.First().Sha;
                var baselineCommitSha = baselineCommit.Sha;
                Logger.Info($"Processing changes from commit='{baselineCommitSha}' to commit='{branchCommitSha}'");

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

        #region Private methods
        /// <summary>
        /// Build clone options for an empty .git directory with only the used parameter
        /// </summary>
        /// <param name="credentialsHandler"></param>
        /// <returns></returns>
        private static CloneOptions GetCloneOptions(CredentialsHandler credentialsHandler)
        {
            return new CloneOptions
            {
                IsBare = true,
                CredentialsProvider = credentialsHandler
            };
        }

        private static GitObject FindBaseline(Repository gitRepo, string baselineName)
        {
            var tag = gitRepo.Tags[baselineName];
            return tag?.Target;
        }

        private static ICommitLog FindBranchCommit(Repository repository, string branchName = null)
        {
            if(string.IsNullOrEmpty(branchName))
            {
                return repository.Commits;
            }

            // Find latest commits
            var branch = repository.Branches.FirstOrDefault(s => s.FriendlyName.EndsWith(branchName, StringComparison.OrdinalIgnoreCase));
            return branch?.Commits;
        }

        #endregion
    }
}
