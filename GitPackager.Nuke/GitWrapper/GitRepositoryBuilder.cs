using GitPackager.Nuke.Tools.Constants;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Nuke.Common.IO;
using static Nuke.Common.IO.FileSystemTasks;

namespace GitPackager.Nuke.GitWrapper
{
    public class GitRepositoryBuilder
    {
        public static Repository GetRepository(string repositoryUrl, PathConstruction.AbsolutePath projectPath, CredentialsHandler credentialsHandler)
        {
            if (FileExists(projectPath / GitConstants.GitDirectory / GitConstants.IndexFile))
            {
                return new Repository(projectPath);
            }

            return CloneTempRepository(repositoryUrl, projectPath, credentialsHandler);
        }

        private static Repository CloneTempRepository(string repositoryUrl, PathConstruction.AbsolutePath projectPath, CredentialsHandler credentialsHandler)
        {
            var destination = Repository.Clone(repositoryUrl, projectPath, GetCloneOptions(credentialsHandler));
            return new Repository(destination);
        }

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
    }
}
