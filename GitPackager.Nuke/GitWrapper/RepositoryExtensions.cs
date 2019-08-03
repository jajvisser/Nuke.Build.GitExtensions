using System;
using System.Linq;
using LibGit2Sharp;

namespace GitPackager.Nuke.GitWrapper
{
    public static class RepositoryExtensions
    {
        public static GitObject FindBaseline(this Repository gitRepo, string baselineName)
        {
            var tag = gitRepo.Tags[baselineName];
            return tag?.Target;
        }

        public static ICommitLog FindBranchCommit(this Repository repository, string branchName = null)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                return repository.Commits;
            }

            // Find latest commits
            var branch = repository.Branches.FirstOrDefault(s => s.FriendlyName.EndsWith(branchName, StringComparison.OrdinalIgnoreCase));
            return branch?.Commits;
        }
    }
}
