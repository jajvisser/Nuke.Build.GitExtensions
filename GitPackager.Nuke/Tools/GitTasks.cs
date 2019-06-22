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
            repository.Network.Push(origin, "+:" + GitConstants.RefsTags + tag, new PushOptions {  CredentialsProvider = credentialsHandler });
            Logger.Info($"Removed tag {tag} from remote");
        }

        public static void CreateTag(string tag, PathConstruction.AbsolutePath projectPath, string repositoryUrl, CredentialsHandler credentialsHandler = null)
        {
            var repository = GitRepositoryBuilder.GetRepository(repositoryUrl, projectPath, credentialsHandler);
            var origin = repository.Network.Remotes[GitConstants.Origin];

            Logger.Info($"Creating tag {tag}");
            repository.ApplyTag(tag);
            repository.Network.Push(origin, GitConstants.RefsTags + tag, new PushOptions { CredentialsProvider = credentialsHandler });
            Logger.Info($"Removed tag {tag} from remote");
        }
    }
}
