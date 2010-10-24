﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using EnvDTE;
using NuPack.VisualStudio.Resources;

namespace NuPack.VisualStudio.Cmdlets {

    /// <summary>
    /// This class acts as the base class for InstallPackage, UninstallPackage and UpdatePackage commands.
    /// </summary>
    public abstract class ProcessPackageBaseCmdlet : NuPackBaseCmdlet {
        private IProjectManager _projectManager;

        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, Position = 0)]
        public string Id { get; set; }

        [Parameter(Position = 1)]
        public string Project { get; set; }

        protected override IVsPackageManager PackageManager {
            get {
                return base.PackageManager;
            }
            set {
                base.PackageManager = value;
                _projectManager = null;
            }
        }

        protected IProjectManager ProjectManager {
            get {
                if (_projectManager == null) {
                    _projectManager = GetProjectManager(Project);
                }

                return _projectManager;
            }
        }

        protected override void BeginProcessing() {
            base.BeginProcessing();

            if (PackageManager != null) {
                PackageManager.PackageInstalling += OnPackageInstalling;
                PackageManager.PackageInstalled += OnPackageInstalled;
            }
        }

        protected override void EndProcessing() {
            base.EndProcessing();

            if (PackageManager != null) {
                PackageManager.PackageInstalling -= OnPackageInstalling;
                PackageManager.PackageInstalled -= OnPackageInstalled;
            }

            if (_projectManager != null) {
                _projectManager.PackageReferenceAdded -= OnPackageReferenceAdded;
                _projectManager.PackageReferenceRemoving -= OnPackageReferenceRemoving;
            }

            WriteLine();
        }

        private IProjectManager GetProjectManager(string projectName) {
            if (PackageManager == null) {
                return null;
            }

            Project project = null;

            // If the user specified a project then use it
            if (!String.IsNullOrEmpty(projectName)) {
                project = GetProjectFromName(projectName);

                // If that project was invalid then throw
                if (project == null) {
                    throw new InvalidOperationException(VsResources.Cmdlet_MissingProjectParameter);
                }
            }
            else if (!String.IsNullOrEmpty(DefaultProjectName)) {
                // If there is a default project then use it
                project = GetProjectFromName(DefaultProjectName);

                Debug.Assert(project != null, "default project should never be invalid");
            }

            if (project == null) {
                // No project specified and default project was null
                return null;
            }

            IProjectManager projectManager = PackageManager.GetProjectManager(project);
            projectManager.PackageReferenceAdded += OnPackageReferenceAdded;
            projectManager.PackageReferenceRemoving += OnPackageReferenceRemoving;

            return projectManager;
        }

        private void OnPackageInstalling(object sender, PackageOperationEventArgs e) {
            // write disclaimer text before a package is installed
            WriteDisclaimerText(e.Package);
        }

        private void OnPackageInstalled(object sender, PackageOperationEventArgs e) {
            AddToolsFolderToEnvironmentPath(e.InstallPath);
            ExecuteScript(e.InstallPath, "init.ps1", e.Package, null);
        }

        private void AddToolsFolderToEnvironmentPath(string installPath) {
            string toolsPath = Path.Combine(installPath, "tools");
            if (Directory.Exists(toolsPath)) {
                var envPath = (string)GetVariableValue("env:path");
                if (!envPath.EndsWith(";", StringComparison.OrdinalIgnoreCase)) {
                    envPath = envPath + ";";
                }
                envPath += toolsPath;

                SessionState.PSVariable.Set("env:path", envPath);
            }
        }

        private void OnPackageReferenceAdded(object sender, PackageOperationEventArgs e) {
            var projectManager = (ProjectManager)sender;
            EnvDTE.Project project = GetProjectFromName(projectManager.Project.ProjectName);

            ExecuteScript(e.InstallPath, "install.ps1", e.Package, project);
        }

        private void OnPackageReferenceRemoving(object sender, PackageOperationEventArgs e) {
            var projectManager = (ProjectManager)sender;
            EnvDTE.Project project = GetProjectFromName(projectManager.Project.ProjectName);

            ExecuteScript(e.InstallPath, "uninstall.ps1", e.Package, project);
        }

        protected void ExecuteScript(string rootPath, string scriptFileName, IPackage package, Project project) {
            string toolsPath = Path.Combine(rootPath, "tools");
            string fullPath = Path.Combine(toolsPath, scriptFileName);
            if (File.Exists(fullPath)) {
                var psVariable = SessionState.PSVariable;

                // set temp variables to pass to the script
                psVariable.Set("__rootPath", rootPath);
                psVariable.Set("__toolsPath", toolsPath);
                psVariable.Set("__package", package);
                psVariable.Set("__project", project);

                string command = "& '" + fullPath + "' $__rootPath $__toolsPath $__package $__project";
                WriteVerbose(VsResources.Cmdlet_ExecutingScript);
                InvokeCommand.InvokeScript(command);

                // clear temp variables
                psVariable.Remove("__rootPath");
                psVariable.Remove("__toolsPath");
                psVariable.Remove("__package");
                psVariable.Remove("__project");
            }
        }

        protected void WriteDisclaimerText(IPackageMetadata package) {
            if (package.RequireLicenseAcceptance) {
                string message = String.Format(
                    CultureInfo.CurrentCulture,
                    VsResources.InstallSuccessDisclaimerText,
                    package.Id,
                    String.Join(", ", package.Authors),
                    package.LicenseUrl);

                WriteLine(message);
            }
        }
    }
}