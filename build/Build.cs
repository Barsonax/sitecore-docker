using System;
using System.Linq;
using System.Threading;
using Nuke.Common;
using Nuke.Common.ProjectModel;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Docker.DockerTasks;
using Nuke.Docker;
using Nuke.Common.Tooling;

partial class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.All);

    // Tools
    [PathExecutable(name: "docker-compose")] readonly Tool DockerCompose;

    [PathExecutable] readonly Tool Powershell;

    // Docker options
    [Parameter("Docker image repository prefix, e.g. my.docker-image.repo/")]
    readonly string RepoImagePrefix = "";

    // Get the container created by docker-compose
    private string GetContainerName(string serviceName) {
        var dirName = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory()).Name;
        return $"{dirName}_{serviceName}_1";
    }

    private void AssertCleanDirectory(string dir) {
        if (System.IO.Directory.Exists(dir)) {
            System.IO.Directory.CreateDirectory(dir);
        } else {
            Nuke.Common.ControlFlow.Assert(
                System.IO.Directory.GetFiles(dir).Length == 0,
                $"{dir} is not empty"
            );  
        }
    }

    // Install a Sitecore package using the given script file and the docker-compose.yml file in the current directory
    private void InstallSitecorePackage(string scriptFilename, string sitecoreTargetImageName, string mssqlTargetImageName, string dockerComposeOptions = "") {
        AssertCleanDirectory("./data/mssql");
        AssertCleanDirectory("./data/solr");

        DockerCompose($"{dockerComposeOptions} up -d");

        // Install Commerce Connect package
        var sitecoreContainerName = GetContainerName("sitecore");
        DockerExec(x => x
            .SetContainer(sitecoreContainerName)
            .SetCommand("powershell")
            .SetArgs(scriptFilename)
            .SetInteractive(true)
            .SetTty(true)
        );

        DockerCompose("stop");

        // Give some time to really stop
        Thread.Sleep(10000);

        // Persist changes to DB installation directory
        DockerCompose($"{dockerComposeOptions} up -d mssql");

        var mssqlContainerName = GetContainerName("mssql");
        DockerExec(x => x
            .SetContainer(mssqlContainerName)
            .SetCommand("powershell")
            .SetArgs(@"C:\Persist-Databases.ps1")
            .SetInteractive(true)
            .SetTty(true)
        );

        DockerCompose("stop");

        // Commit changes
        DockerCommit(x => x
            .SetContainer(mssqlContainerName)
            .SetRepository(mssqlTargetImageName));

        DockerCommit(x => x
            .SetContainer(sitecoreContainerName)
            .SetRepository(sitecoreTargetImageName));

        // Remove build artefacts
        DockerCompose("down");
    }

    Target All => _ => _
        .DependsOn(Xp, XpSxa, Xc, XcSxa);

    Target Push => _ => _
        .DependsOn(PushXp, PushXpSxa, PushXc, PushXcSxa);
}
