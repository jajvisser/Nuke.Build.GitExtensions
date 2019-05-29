using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Nuke.Common;
using System;
using System.Linq;
using static Nuke.Common.IO.PathConstruction;

namespace Nuke.Git.Utilities.GitPackager
{
    public static class GitPackager
    {
        /// <summary>
        /// With an existing project
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath">Git repository path.</param>
        /// <param name="baselineName">Baseline name of the base commit to check against</param>
        /// <param name="diffAction">Action that is executed</param>
        /// <param name="branchName">Branchname </param>
        public static void DiffFromBaseline(AbsolutePath projectPath, string baselineName, Action<TreeChanges> diffAction, string branchName = null)
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
            DiffFromBaseline(repository, diffAction, baselineName, branchName);
        }

        /// <summary>
        /// Workaround for agents in teamcity, since the .git directory
        /// Find what files have changed between the current commit and a baseline tag and return the 
        /// </summary>
        /// <param name="projectPath"></param>
        /// <param name="repositoryUrl"></param>
        /// <param name="baselineName"></param>
        /// <param name="branchName"></param>
        /// <param name="diffAction"></param>
        /// <param name="credentialsHandler"></param>
        public static void DiffFromBaseline(AbsolutePath projectPath, string repositoryUrl, string baselineName, string branchName, Action<TreeChanges> diffAction, CredentialsHandler credentialsHandler)
        {
            Logger.Info($"Starting rebuilding repository");
            var destination = Repository.Clone(repositoryUrl, projectPath, GetCloneOptions(credentialsHandler));
            var repository = new Repository(destination);
            Logger.Info($"Finished rebuilding repository");
            DiffFromBaseline(repository, diffAction, baselineName);
        }

        private static CloneOptions GetCloneOptions(CredentialsHandler credentialsHandler)
        {
            return new CloneOptions
            {
                IsBare = true,
                CredentialsProvider = credentialsHandler
            };
        }

        private static void DiffFromBaseline(Repository repository, Action<TreeChanges> diffAction, string baselineName, string branchName = null)
        {
            using (repository)
            {
                var baselineCommit = FindBaseline(repository, baselineName);
                var branchCommits = FindBranchCommit(repository, branchName);
                var latestBranchCommit = branchCommits.First().Sha;

                if (string.IsNullOrWhiteSpace(baselineCommit))
                {
                    throw new InvalidOperationException($"Tag and Commit are not found for {baselineName}");
                }

                Logger.Info($"Processing changes from commit='{baselineCommit}' to commit='{latestBranchCommit}'");

                var filter = new CommitFilter { ExcludeReachableFrom = baselineCommit };
                var commits = repository.Commits.QueryBy(filter).ToList();

                if (commits.Any())
                {
                    var newCommit = branchCommits.First();
                    var oldCommit = commits.Count == 1 ? newCommit.Parents.First() : commits.Last();

                    var changes = repository.Diff.Compare<TreeChanges>(oldCommit.Tree, newCommit.Tree);
                    diffAction(changes);
                }
                else
                {
                    Logger.Info($"No commits found on repository");
                }
            }
        }

        private static string FindBaseline(Repository gitRepo, string baselineName)
        {
            var tag = gitRepo.Tags[baselineName];
            return tag?.Target?.Sha;
        }

        private static ICommitLog FindBranchCommit(Repository repository, string branchName = null)
        {
            if(string.IsNullOrEmpty(branchName))
            {
                return repository.Commits;
            }

            // Find latest commits
            var branch = repository.Branches[branchName];
            return branch?.Commits;
        }
    }
}
