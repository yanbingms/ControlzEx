///////////////////////////////////////////////////////////////////////////////
// TOOLS / ADDINS
///////////////////////////////////////////////////////////////////////////////

#tool "GitVersion.CommandLine"
#tool "gitreleasemanager"
#addin "Cake.Figlet"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
if (string.IsNullOrWhiteSpace(target))
{
    target = "Default";
}

var configuration = Argument("configuration", "Release");
if (string.IsNullOrWhiteSpace(configuration))
{
    configuration = "Release";
}

var verbosity = Argument("verbosity", Verbosity.Normal);
if (string.IsNullOrWhiteSpace(configuration))
{
    verbosity = Verbosity.Normal;
}

///////////////////////////////////////////////////////////////////////////////
// PREPARATION
///////////////////////////////////////////////////////////////////////////////

var repoName = "ControlzEx";
var local = BuildSystem.IsLocalBuild;

// Set build version
if (local == false
    || verbosity == Verbosity.Verbose)
{
    GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.BuildServer });
}
GitVersion gitVersion = GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.Json });

var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var branchName = gitVersion.BranchName;
var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("develop", branchName);
var isReleaseBranch = StringComparer.OrdinalIgnoreCase.Equals("master", branchName);
var isTagged = AppVeyor.Environment.Repository.Tag.IsTag;

// Directories and Paths
var solution = "src/ControlzEx.sln";

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    if (!IsRunningOnWindows())
    {
        throw new NotImplementedException($"{repoName} will only build on Windows because it's not possible to target WPF and Windows Forms from UNIX.");
    }

    Information(Figlet(repoName));

    Information("Informational   Version: {0}", gitVersion.InformationalVersion);
    Information("SemVer          Version: {0}", gitVersion.SemVer);
    Information("AssemblySemVer  Version: {0}", gitVersion.AssemblySemVer);
    Information("MajorMinorPatch Version: {0}", gitVersion.MajorMinorPatch);
    Information("NuGet           Version: {0}", gitVersion.NuGetVersion);
    Information("IsLocalBuild           : {0}", local);
    Information("Branch                 : {0}", branchName);
    Information("Configuration          : {0}", configuration);
});

Teardown(ctx =>
{
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .ContinueOnError()
    .Does(() =>
{
    var directoriesToDelete = GetDirectories("src/**/obj").Concat(GetDirectories("src/**/bin"));
    DeleteDirectories(directoriesToDelete, new DeleteDirectorySettings { Recursive = true, Force = true });
});

Task("Restore")
    .Does(() =>
{
    var msBuildSettings = new MSBuildSettings {
        Verbosity = Verbosity.Minimal,
        Configuration = configuration,
        // Restore = true, // only with cake 0.28.x
        ArgumentCustomization = args => args.Append("/m")
    };

    MSBuild(solution, msBuildSettings.WithTarget("restore"));
});

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
{
    var msBuildSettings = new MSBuildSettings {
        Verbosity = Verbosity.Normal,
        Configuration = configuration,
        // Restore = true, // only with cake 0.28.x     
        ArgumentCustomization = args => args.Append("/m")
    };
    MSBuild(solution, msBuildSettings
            .SetMaxCpuCount(0)            
            .WithProperty("Version", isReleaseBranch ? gitVersion.MajorMinorPatch : gitVersion.NuGetVersion)
            .WithProperty("AssemblyVersion", gitVersion.AssemblySemVer)
            .WithProperty("FileVersion", gitVersion.AssemblySemFileVer)
            .WithProperty("InformationalVersion", gitVersion.InformationalVersion)
            );
});

Task("Pack")
    .Does(() =>
{
    var msBuildSettings = new MSBuildSettings {
        Verbosity = Verbosity.Normal,
        Configuration = configuration
    };
    var project = "./src/ControlzEx/ControlzEx.csproj";

    MSBuild(project, msBuildSettings
      .WithTarget("pack")
      .WithProperty("PackageOutputPath", "../bin")
      .WithProperty("RepositoryBranch", branchName)
      .WithProperty("RepositoryCommit", gitVersion.Sha)
      .WithProperty("Description", "ControlzEx is a library with some shared Controls for WPF.")
      .WithProperty("Version", isReleaseBranch ? gitVersion.MajorMinorPatch : gitVersion.NuGetVersion)
      .WithProperty("AssemblyVersion", gitVersion.AssemblySemVer)
      .WithProperty("FileVersion", gitVersion.AssemblySemFileVer)
      .WithProperty("InformationalVersion", gitVersion.InformationalVersion)
    );
});

Task("Zip")
    .Does(() =>
{
    var zipDir = "src/bin/" + configuration + "/ControlzEx.Showcase";
    if (!DirectoryExists(zipDir))
    {
        Information("Could not zip any artifact! Folder doesn't exist: " + zipDir);
    }
    else
    {
        Zip(zipDir, "src/bin/ControlzEx.Showcase.v" + gitVersion.NuGetVersion + ".zip");
    }
});

Task("CreateRelease")
    .WithCriteria(() => !isTagged)
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    GitReleaseManagerCreate(username, token, repoName, repoName, new GitReleaseManagerCreateSettings {
        Milestone         = gitVersion.MajorMinorPatch,
        Name              = gitVersion.AssemblySemFileVer,
        Prerelease        = isDevelopBranch,
        TargetCommitish   = branchName,
        WorkingDirectory  = "."
    });
});

Task("ExportReleaseNotes")
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    GitReleaseManagerExport(username, token, repoName, repoName, "releasenotes.md", new GitReleaseManagerExportSettings {
        TagName         = gitVersion.SemVer
    });
});

Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .IsDependentOn("Build");

Task("appveyor")
    .IsDependentOn("Default")
    .IsDependentOn("Pack")
    .IsDependentOn("Zip");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);