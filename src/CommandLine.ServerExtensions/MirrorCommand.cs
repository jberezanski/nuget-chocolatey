﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.IO;
using NuGet.Common;
using NuGet.Commands;

namespace NuGet.ServerExtensions
{
    [Command(typeof(NuGetResources), "mirror", "MirrorCommandDescription",
        MinArgs = 3, MaxArgs = 3, UsageDescriptionResourceName = "MirrorCommandUsageDescription",
        UsageSummaryResourceName = "MirrorCommandUsageSummary", UsageExampleResourceName = "MirrorCommandUsageExamples")]
    public class MirrorCommand : Command
    {
        private readonly List<string> _sources = new List<string>();

        [Option(typeof(NuGetResources), "MirrorCommandSourceDescription", AltName = "src")]
        public ICollection<string> Source
        {
            get { return _sources; }
        }

        [Option(typeof(NuGetResources), "MirrorCommandVersionDescription")]
        public string Version { get; set; }

        [Option(typeof(NuGetResources), "MirrorCommandApiKey", AltName = "k")]
        public string ApiKey { get; set; }

        [Option(typeof(NuGetResources), "MirrorCommandPrerelease")]
        public bool Prerelease { get; set; }

        [Option(typeof(NuGetResources), "MirrorCommandTimeoutDescription")]
        public int Timeout { get; set; }

        [Option(typeof(NuGetResources), "MirrorCommandNoCache", AltName = "c")]
        public bool NoCache { get; set; }

        [Option(typeof(NuGetResources), "MirrorCommandNoOp", AltName = "n")]
        public bool NoOp { get; set; }

        private readonly IPackageRepository _cacheRepository = MachineCache.Default;

        public override void ExecuteCommand()
        {
            var srcRepository = GetSourceRepository();
            var dstRepository = GetTargetRepository(Arguments[1], Arguments[2]);
            var mirrorer = GetPackageMirrorer(srcRepository, dstRepository);
            var isPackagesConfig = IsUsingPackagesConfig(Arguments[0]);
            var toMirror = GetPackagesToMirror(Arguments[0], isPackagesConfig);

            if (isPackagesConfig && !String.IsNullOrEmpty(Version))
            {
                throw new ArgumentException(NuGetResources.MirrorCommandNoVersionIfPackagesConfig);
            }

            bool didSomething = false;

            using (mirrorer.SourceRepository.StartOperation(RepositoryOperationNames.Mirror, mainPackageId: null))
            {
                foreach (var package in toMirror)
                {
                    if (mirrorer.MirrorPackage(
                                    package.Id,
                                    package.Version,
                                    ignoreDependencies: false,
                                    allowPrereleaseVersions: AllowPrereleaseVersion(package.Version, isPackagesConfig)))
                    {
                        didSomething = true;
                    }
                }
            }

            if (!didSomething)
            {
                Console.Log(MessageLevel.Warning, NuGetResources.MirrorCommandDidNothing);
            }
        }

        protected virtual IPackageRepository CacheRepository
        {
            get { return _cacheRepository; }
        }

        protected virtual IFileSystem CreateFileSystem()
        {
            return new PhysicalFileSystem(Directory.GetCurrentDirectory());
        }

        private IPackageRepository GetSourceRepository()
        {
            var repository = AggregateRepositoryHelper.CreateAggregateRepositoryFromSources(RepositoryFactory, SourceProvider, Source);
            bool ignoreFailingRepositories = repository.IgnoreFailingRepositories;
            if (!NoCache)
            {
                repository = new AggregateRepository(new[] { CacheRepository, repository })
                             {
                                 IgnoreFailingRepositories = ignoreFailingRepositories
                             };
            }
            repository.Logger = Console;
            return repository;
        }

        private IPackageRepository GetDestinationRepositoryList(string repo)
        {
            return RepositoryFactory.CreateRepository(SourceProvider.ResolveAndValidateSource(repo));
        }

        protected virtual IPackageRepository GetTargetRepository(string pull, string push)
        {
            return new PackageServerRepository(
                sourceRepository: GetDestinationRepositoryList(pull),
                destination: GetDestinationRepositoryPush(push),
                apiKey: GetApiKey(pull),
                timeout: GetTimeout(),
                logger: Console);
        }

        private static PackageServer GetDestinationRepositoryPush(string repo)
        {
            return new PackageServer(repo, userAgent: "NuGet Command Line");
        }

        private PackageMirrorer GetPackageMirrorer(IPackageRepository srcRepository, IPackageRepository dstRepository)
        {
            return new PackageMirrorer(srcRepository, dstRepository)
            {
                Logger = Console,
                NoOp = NoOp
            };
        }

        private string GetApiKey(string source)
        {
            return String.IsNullOrEmpty(ApiKey) ? CommandLineUtility.GetApiKey(Settings, source) : ApiKey;
        }

        private TimeSpan GetTimeout()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(Math.Abs(Timeout));
            return (timeout.Seconds <= 0) ? TimeSpan.FromMinutes(5) : timeout;
        }

        private SemanticVersion GetVersion()
        {
            return null == Version ? null : new SemanticVersion(Version);
        }

        private static PackageReferenceFile GetPackageReferenceFile(IFileSystem fileSystem, string configFilePath)
        {
            // By default the PackageReferenceFile does not throw if the file does not exist at the specified path.
            // We'll try reading from the file so that the file system throws a file not found
            using (fileSystem.OpenFile(configFilePath))
            {
                // Do nothing
            }
            return new PackageReferenceFile(fileSystem, Path.GetFullPath(configFilePath));
        }

        private static bool IsUsingPackagesConfig(string packageId)
        {
            return Path.GetFileName(packageId).Equals(Constants.PackageReferenceFile, StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<PackageReference> GetPackagesToMirror(string packageId, bool isPackagesConfig)
        {
            if (isPackagesConfig)
            {
                IFileSystem fileSystem = CreateFileSystem();
                string configFilePath = Path.GetFullPath(packageId);
                var packageReferenceFile = GetPackageReferenceFile(fileSystem, configFilePath);
                return CommandLineUtility.GetPackageReferences(packageReferenceFile, configFilePath, requireVersion: false);
            }
            else
            {
                return new[] { new PackageReference(packageId, GetVersion(), versionConstraint: null, targetFramework: null) };
            }
        }

        private bool AllowPrereleaseVersion(SemanticVersion version, bool isUsingPackagesConfig)
        {
            if (isUsingPackagesConfig && (null != version))
            {
                return true;
            }
            return Prerelease;
        }
    }
}