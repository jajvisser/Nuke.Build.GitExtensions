using System;
using System.Linq;
using GitPackager.Nuke.GitWrapper;
using GitPackager.Nuke.Tools.Constants;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Nuke.Common;
using Nuke.Common.IO;

namespace GitPackager.Nuke.Tools
{
    public static class GitTasks
    {
        public static void DeleteTag(string tag, PathConstruction.AbsolutePath projectPath, string repositoryUrl, CredentialsHandler credentialsHandler = null)
        {
            var repository = GitRepositoryBuilder.GetRepository(repositoryUrl, projectPath, credentialsHandler);
            var origin = repository.Network.Remotes[GitConstants.Origin];

            Logger.Info($"Removing tag {tag}");
            repository.Tags.Remove(tag);
            repository.Network.Push(origin, GitConstants.DeleteRef + GitConstants.RefsTags + tag, new PushOptions {  CredentialsProvider = credentialsHandler });
            Logger.Info($"Removed tag {tag} from remote");
        }

        public static void CreateTag(string tag, PathConstruction.AbsolutePath projectPath, string repositoryUrl, CredentialsHandler credentialsHandler = null)
        {
            var repository = GitRepositoryBuilder.GetRepository(repositoryUrl, projectPath, credentialsHandler);
            var origin = repository.Network.Remotes[GitConstants.Origin];
            var lastestCommit = repository.Head.Commits.First();

            Logger.Info($"Creating tag {tag} with commit {lastestCommit.Sha}");
            repository.ApplyTag(tag);
            repository.Network.Push(origin, GitConstants.RefsTags + tag, new PushOptions { CredentialsProvider = credentialsHandler });
            Logger.Info($"Created tag {tag} with commit {lastestCommit.Sha} on remote");
        }

        public static void ResetTagToLatestCommit(string tag, PathConstruction.AbsolutePath projectPath, string repositoryUrl,
            CredentialsHandler credentialsHandler = null)
        {
            var repository = GitRepositoryBuilder.GetRepository(repositoryUrl, projectPath, credentialsHandler);
            if (repository.Tags.Any(s => string.Equals(s.FriendlyName, tag, StringComparison.OrdinalIgnoreCase)))
            {
                DeleteTag(tag, projectPath, repositoryUrl, credentialsHandler);
            }

            CreateTag(tag, projectPath, repositoryUrl, credentialsHandler);
        }
    }
}
