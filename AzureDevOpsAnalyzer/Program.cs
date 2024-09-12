namespace AzureDevOpsAnalyzer;

using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AzureDevOps.Lib;
using CommandLine;
using Helper.Lib;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    private static Options programOptions = new ();

    public static async Task Main(string[] args)
    {
        ServiceCollection services = new ();
        services.AddHttpClient();
        ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        StringBuilder sb = new ();
        DateTime start = DateTime.Now;
        List<Project> allProjects = new ();
        List<Team> allTeams = new ();
        string currentDirectory = Directory.GetCurrentDirectory();
        ConsoleHelper.WriteHeader("    AzureDevOpsAnalyzer");
        var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    programOptions = o;
                })
                .WithNotParsed(HandleParseError);

        if (result.Tag == ParserResultType.NotParsed)
        {
            // Help text requested, or parsing failed.
            return;
        }

        if (!Convert.ToBoolean(programOptions.SkipBase))
        {
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieving Projects from {programOptions.CollectionUrl}");
            string projectsJson = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), programOptions.CollectionUrl, $"_apis/projects?api-version=7.0", programOptions.Token);
            if (!string.IsNullOrEmpty(projectsJson))
            {
                Projects projects = JsonSerializer.Deserialize<Projects>(projectsJson);
                if (projects.value.Count > 0)
                {
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tRetrieved {projects.value.Count} projects from {programOptions.CollectionUrl}");
                    allProjects.AddRange(projects.value);
                }
            }
            else
            {
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tWARNING: Unable to retrieve projects from {programOptions.CollectionUrl}");
            }

            sb.Clear();
            sb.AppendLine("collectionurl,id,name");

            foreach (var project in allProjects)
            {
                sb.Append(programOptions.CollectionUrl + "," + project.id + "," + project.name);
                sb.AppendLine();
            }

            programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"projects.csv");
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {allProjects.Count} Projects to {programOptions.OutputFile}", ConsoleColor.Green);
            File.WriteAllText(programOptions.OutputFile, sb.ToString());
            sb.Clear();

            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieving Teams from {programOptions.CollectionUrl}");
            string teamsJson = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), programOptions.CollectionUrl, $"_apis/teams?api-version=7.0-preview", programOptions.Token);
            if (!string.IsNullOrEmpty(teamsJson))
            {
                Teams teams = JsonSerializer.Deserialize<Teams>(teamsJson);
                if (teams.value.Count > 0)
                {
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tRetrieved {teams.value.Count} teams from {programOptions.CollectionUrl}");
                    allTeams.AddRange(teams.value);
                }
            }
            else
            {
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tWARNING: Unable to retrieve teams from {programOptions.CollectionUrl}");
            }

            sb.Clear();
            sb.AppendLine("collectionurl,teamid,teamname,projectName");

            foreach (var team in allTeams)
            {
                sb.Append(programOptions.CollectionUrl + "," + team.id + "," + team.name + "," + team.projectName);
                sb.AppendLine();
            }

            programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"teams.csv");
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {allTeams.Count} Teams to {programOptions.OutputFile}", ConsoleColor.Green);
            File.WriteAllText(programOptions.OutputFile, sb.ToString());
            sb.Clear();

            // Get all team members
            bool firstTeam = true;
            foreach (var team in allTeams)
            {
                List<TeamMember> allTeamMembers = new ();
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieving Team members for {team.name}");
                string teamMembersJson = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), programOptions.CollectionUrl, $"_apis/projects/{team.projectName}/teams/{team.name}/members?api-version=7.0-preview", programOptions.Token);
                if (!string.IsNullOrEmpty(teamMembersJson))
                {
                    TeamMembers teamMembers = JsonSerializer.Deserialize<TeamMembers>(teamMembersJson);
                    if (teamMembers.value.Count > 0)
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tRetrieved {teamMembers.value.Count} team members from {team.name} in {team.projectName}");
                        allTeamMembers.AddRange(teamMembers.value);
                    }
                }
                else
                {
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tWARNING: Unable to retrieve team members from {team.name} in {team.projectName}");
                }

                sb.Clear();
                if (firstTeam)
                {
                    sb.AppendLine("collectionurl,projectName,teamname,isTeamAdmin,displayName,uniqueName");
                }

                foreach (var teammember in allTeamMembers)
                {
                    sb.Append(programOptions.CollectionUrl + "," + team.projectName + "," + team.name + "," + teammember.isTeamAdmin + "," + StringHelper.StringToCSVCell(teammember.identity.displayName) + "," + teammember.identity.uniqueName);
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"teammembers.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {team.name} Team members to {programOptions.OutputFile}", ConsoleColor.Green);
                if (firstTeam)
                {
                    File.WriteAllText(programOptions.OutputFile, sb.ToString());
                }
                else
                {
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                }

                sb.Clear();
                firstTeam = false;
            }
        }

        string[] projectUrls = programOptions.ProjectUrl.Split(",");
        bool firstProject = true;
        foreach (string projectUrl in projectUrls)
        {
            string[] projectParts = projectUrl.Split("/");
            string projectName = projectParts[^1];
            string filePrefix = projectUrls.Length > 1 ? "multi" : projectName;
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Analyzing {projectName}, retrieving Repositories");
            Repositories repositories = AzureDevOpsHelper.GetRepositoriesAsync(httpClientFactory, projectUrl, "_apis/git/repositories?api-version=7.0", programOptions.Filter, programOptions.Exclusion, programOptions.Token).Result;
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Repositories to Analyze: {repositories.count}");

            if (firstProject)
            {
                sb.AppendLine("projecturl,defaultBranch,id,name,project,remoteUrl,sshUrl,url,webUrl,size,isDisabled");
            }

            int repoCount = 0;
            foreach (var repo in repositories.value)
            {
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"{++repoCount}. {repo.name}");
                sb.Append(projectUrl + "," + repo.defaultBranch + "," + repo.id + "," + repo.name + "," + repo.project.name + ",");
                sb.Append(repo.remoteUrl + "," + repo.sshUrl + "," + repo.url + "," + repo.webUrl + "," + repo.size + "," + repo.isDisabled);
                sb.AppendLine();
            }

            programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-repositories.csv");
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {programOptions.OutputFile}", ConsoleColor.Green);
            WriteToFile(sb, firstProject);

            if (!Convert.ToBoolean(programOptions.SkipBase))
            {
                List<AreaPath> allAreaPaths = new ();
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieving Area Paths from {projectName}");

                string areapathJson = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/wit/classificationnodes/areas?$depth=100&api-version=7.0", programOptions.Token);
                if (!string.IsNullOrEmpty(areapathJson))
                {
                    AreaPath areaPaths = JsonSerializer.Deserialize<AreaPath>(areapathJson);

                    sb.Clear();
                    if (firstProject)
                    {
                        sb.AppendLine("projecturl,areapath,name");
                    }

                    sb.AppendLine(projectUrl + "," + areaPaths.path.Replace($"\\{projectName}\\Area", projectName) + "," + areaPaths.name);

                    if (areaPaths.children != null && areaPaths.children.Count > 0)
                    {
                        foreach (var path in areaPaths.children)
                        {
                            AzureDevOpsHelper.AppendChildAreaPaths(projectUrl, path, sb, projectName);
                        }
                    }
                    else
                    {
                        sb.AppendLine(projectUrl + "," + areaPaths.path + "," + areaPaths.name);
                    }

                    programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-areapaths.csv");
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {programOptions.OutputFile}", ConsoleColor.Green);
                    WriteToFile(sb, firstProject);
                }

                // Get all team area paths
                bool firstTeamAreaPath = true;
                foreach (var team in allTeams)
                {
                    List<TeamAreaPathConfig> allTeamAreaPathConfig = new ();
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieving Area Paths for {team.name}");
                    string teamAreaPathsJson = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"{team.name}/_apis/work/teamsettings/teamfieldvalues?api-version=7.0", programOptions.Token);

                    sb.Clear();
                    if (firstTeamAreaPath)
                    {
                        sb.AppendLine("projecturl,teamname,areapath,includechildren");
                    }

                    if (!string.IsNullOrEmpty(teamAreaPathsJson))
                    {
                        TeamAreaPathConfig teamAreaPaths = JsonSerializer.Deserialize<TeamAreaPathConfig>(teamAreaPathsJson);
                        if (teamAreaPaths.values.Count > 0)
                        {
                            foreach (var areapath in teamAreaPaths.values)
                            {
                                sb.Append(projectUrl + "," + team.name + "," + areapath.value + "," + areapath.includeChildren);
                                sb.AppendLine();
                            }
                        }
                    }
                    else
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tWARNING: Unable to retrieve team area paths from {team.name} in {team.projectName}");
                    }

                    programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-teamareapaths.csv");
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {team.name} Team area paths for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                    WriteToFile(sb, firstTeamAreaPath);
                    firstTeamAreaPath = false;
                }
            }

            if (!programOptions.SkipCommits)
            {
                List<Commit> allCommits = new ();
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null && r.isDisabled != true))
                {
                    string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch.Replace("refs/heads/", string.Empty) : programOptions.Branch;
                    try
                    {
                        string commitJson = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/git/repositories/{repo.name}/commits?searchCriteria.$top={programOptions.CommitCount}&searchCriteria.itemVersion.version={branchToScan}&searchCriteria.fromDate={programOptions.FromDate}&api-version=7.0", programOptions.Token);
                        if (!string.IsNullOrEmpty(commitJson))
                        {
                            CommitHistory commitHistory = JsonSerializer.Deserialize<CommitHistory>(commitJson);
                            if (commitHistory.value.Count > 0)
                            {
                                if (commitHistory.value.Count > 5000)
                                {
                                    Parallel.ForEach(commitHistory.value, item =>
                                    {
                                        item.branch = branchToScan;
                                    });
                                }
                                else
                                {
                                    foreach (Commit item in commitHistory.value)
                                    {
                                        item.branch = branchToScan;
                                    }
                                }

                                allCommits.AddRange(commitHistory.value);
                                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved {commitHistory.value.Count} commits from {repo.name} ({branchToScan})");
                            }
                            else
                            {
                                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved 0 commits from {repo.name} ({branchToScan})");
                            }
                        }
                        else
                        {
                            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"WARNING: Unable to retrieve commit history from {repo.name} ({branchToScan})");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"{branchToScan} not found {ex}");
                    }
                }

                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,repository,branch,isinternal,authordate,authoremail,authorname,add,delete,edit,commitid,committerdate,year,month,day,dayofweek,weekofyear,hour,committeremail,committername,remoteurl,comment");
                }

                foreach (var commit in allCommits)
                {
                    sb.Append(projectUrl + ",");
                    string[] urlParts = commit.remoteUrl.Split("/");
                    sb.Append(urlParts[6] + ",");
                    sb.Append(commit.branch + ",");
                    if (string.IsNullOrEmpty(programOptions.InternalIdentifier))
                    {
                        sb.Append("true,");
                    }
                    else
                    {
                        sb.Append(currentCulture.CompareInfo.IndexOf(commit.committer.email, programOptions.InternalIdentifier, CompareOptions.IgnoreCase) >= 0 ? true + "," : false + ",");
                    }

                    sb.Append(commit.author.date.ToLocalTime() + ",\"" + commit.author.email + "\",\"" + commit.author.name + "\"," + commit.changeCounts.Add + ",");
                    sb.Append(commit.changeCounts.Delete + "," + commit.changeCounts.Edit + "," + commit.commitId + "," + commit.committer.date.ToLocalTime() + ",");
                    sb.Append(commit.committer.date.ToLocalTime().Year + "," + commit.committer.date.ToLocalTime().Month + "," + commit.committer.date.ToLocalTime().Day + "," + commit.committer.date.ToLocalTime().DayOfWeek + ",");
                    sb.Append(currentCulture.Calendar.GetWeekOfYear(commit.committer.date.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                    sb.Append(commit.committer.date.ToLocalTime().Hour + "," + commit.committer.email + ",\"" + commit.committer.name + "\"," + commit.remoteUrl + ",");
                    if (!programOptions.NoMessages)
                    {
                        sb.Append($"\"{commit.comment}\"" + ",");
                    }

                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-commits.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {allCommits.Count} commits for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                WriteToFile(sb, firstProject);

                allCommits = new ();
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null && r.isDisabled != true))
                {
                    try
                    {
                        CommitHistory commitHistory = JsonSerializer.Deserialize<CommitHistory>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/git/repositories/{repo.name}/commits?searchCriteria.$top={programOptions.CommitCount}&searchCriteria.fromDate={programOptions.FromDate}&api-version=7.0", programOptions.Token));
                        if (commitHistory.value.Count > 0)
                        {
                            allCommits.AddRange(commitHistory.value);
                            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved {commitHistory.value.Count} commits from {repo.name} (nobranches)");
                        }
                        else
                        {
                            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved 0 commits from {repo.name} (nobranches)");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Error {ex}");
                    }
                }

                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,repository,branch,isinternal,authordate,authoremail,authorname,add,delete,edit,commitid,committerdate,year,month,day,dayofweek,weekofyear,hour,committeremail,committername,remoteurl,comment");
                }

                foreach (var commit in allCommits)
                {
                    sb.Append(projectUrl + ",");
                    string[] urlParts = commit.remoteUrl.Split("/");
                    sb.Append(urlParts[6] + ",");
                    sb.Append(commit.branch + ",");
                    if (string.IsNullOrEmpty(programOptions.InternalIdentifier))
                    {
                        sb.Append("true,");
                    }
                    else
                    {
                        sb.Append(currentCulture.CompareInfo.IndexOf(commit.committer.email, programOptions.InternalIdentifier, CompareOptions.IgnoreCase) >= 0 ? true + "," : false + ",");
                    }

                    sb.Append(commit.author.date.ToLocalTime() + ",\"" + commit.author.email + "\",\"" + commit.author.name + "\"," + commit.changeCounts.Add + ",");
                    sb.Append(commit.changeCounts.Delete + "," + commit.changeCounts.Edit + "," + commit.commitId + "," + commit.committer.date.ToLocalTime() + ",");
                    sb.Append(commit.committer.date.ToLocalTime().Year + "," + commit.committer.date.ToLocalTime().Month + "," + commit.committer.date.ToLocalTime().Day + "," + commit.committer.date.ToLocalTime().DayOfWeek + ",");
                    sb.Append(currentCulture.Calendar.GetWeekOfYear(commit.committer.date.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                    sb.Append(commit.committer.date.ToLocalTime().Hour + "," + commit.committer.email + ",\"" + commit.committer.name + "\"," + commit.remoteUrl + ",");
                    if (!programOptions.NoMessages)
                    {
                        sb.Append($"\"{commit.comment}\"" + ",");
                    }

                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-ALLcommits.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {allCommits.Count} commits for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                WriteToFile(sb, firstProject);
            }

            if (!programOptions.SkipPushes)
            {
                List<Push> allPushes = new ();
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null && r.isDisabled != true))
                {
                    string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch : $"refs/heads/{programOptions.Branch}";
                    Pushes pushes = JsonSerializer.Deserialize<Pushes>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/git/repositories/{repo.name}/pushes?$top={programOptions.PushCount}&searchCriteria.refName={branchToScan}&searchCriteria.fromDate={programOptions.FromDate}&api-version=7.0", programOptions.Token));
                    if (pushes.value.Count > 0)
                    {
                        foreach (Push item in pushes.value)
                        {
                            item.branch = branchToScan;
                        }

                        allPushes.AddRange(pushes.value);
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved {pushes.value.Count} pushes from {repo.name} ({branchToScan})");
                    }
                    else
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved 0 pushes from {repo.name} ({branchToScan})");
                    }
                }

                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,repository,branch,pushid,pushdate,year,month,day,dayofweek,weekofyear,hour,uniquename,displayname,remoteurl");
                }

                foreach (var push in allPushes)
                {
                    sb.Append(projectUrl + "," + push.repository.name + "," + push.branch + "," + push.pushId + "," + push.date.ToLocalTime() + ",");
                    sb.Append(push.date.ToLocalTime().Year + "," + push.date.ToLocalTime().Month + "," + push.date.ToLocalTime().Day + "," + push.date.ToLocalTime().DayOfWeek + ",");
                    sb.Append(currentCulture.Calendar.GetWeekOfYear(push.date.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                    sb.Append(push.date.ToLocalTime().Hour + "," + StringHelper.StringToCSVCell(push.pushedBy.uniqueName) + "," + StringHelper.StringToCSVCell(push.pushedBy.displayName) + "\"," + push.repository.remoteUrl + ",");
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-pushes.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {allPushes.Count} pushes for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                WriteToFile(sb, firstProject);
            }

            if (!Convert.ToBoolean(programOptions.SkipBuilds))
            {
                List<Build> allBuildsToIterate = new ();
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieving {programOptions.BuildCount} most recent Builds from {projectName}");
                Builds buildsToIterate = JsonSerializer.Deserialize<Builds>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/build/builds/?$top=5000&maxBuildsPerDefinition=1&minTime={programOptions.FromDate}&api-version=7.0", programOptions.Token));
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"\tRetrieved {buildsToIterate.value.Count} distinct build definition runs");
                allBuildsToIterate.AddRange(buildsToIterate.value);

                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Building csv for {buildsToIterate.value.Count} build definitions to iterate");
                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,id,reason,buildNumber,definition,result,requestedfor,uniqueName,repository,starttime,year,month,day,dayofweek,weekofyear,hour,finishtime,queuetime,totalminutes");
                }

                int defcounter = 1;
                int buildcounter = 0;
                foreach (var buildtoIterate in allBuildsToIterate)
                {
                    Builds builds = JsonSerializer.Deserialize<Builds>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/build/builds/?definitions={buildtoIterate.definition.id}&$top={programOptions.BuildCount}&minTime={programOptions.FromDate}&api-version=7.0", programOptions.Token));
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Building csv for {builds.value.Count} builds. Build Definition {buildtoIterate.definition.name} - {defcounter++} of {buildsToIterate.value.Count}");
                    foreach (var build in builds.value)
                    {
                        TimeSpan buildDuration = build.finishTime - build.startTime;
                        sb.Append(projectUrl + "," + build.id + "," + build.reason + "," + build.buildNumber + "," + build.definition.name + "," + build.result + ",");
                        sb.Append(StringHelper.StringToCSVCell(build.requestedFor.displayName) + "," + StringHelper.StringToCSVCell(build.requestedFor.uniqueName) + "," + build.repository.name + "," + build.startTime.ToLocalTime() + "," + build.startTime.ToLocalTime().Year + ",");
                        sb.Append(build.startTime.ToLocalTime().Month + "," + build.startTime.ToLocalTime().Day + "," + build.startTime.ToLocalTime().DayOfWeek + ",");
                        sb.Append(currentCulture.Calendar.GetWeekOfYear(build.startTime.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                        sb.Append(build.startTime.ToLocalTime().Hour + "," + build.finishTime.ToLocalTime() + "," + build.queueTime.ToLocalTime() + "," + buildDuration.TotalMinutes.ToString("##") + ",");
                        sb.AppendLine();
                        buildcounter++;
                    }
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-builds.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {buildcounter} builds for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                WriteToFile(sb, firstProject);

                if (!Convert.ToBoolean(programOptions.SkipBuildArtifacts))
                {
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Iterating Build artifacts");
                    sb.Clear();
                    if (firstProject)
                    {
                        sb.AppendLine("projecturl,buildid,buildNumber,definition,artifactid,artifactname,artifactsize");
                    }

                    defcounter = 1;
                    buildcounter = 0;
                    foreach (var buildtoIterate in allBuildsToIterate)
                    {
                        Builds builds = JsonSerializer.Deserialize<Builds>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/build/builds/?definitions={buildtoIterate.definition.id}&$top={programOptions.BuildCount}&minTime={programOptions.FromDate}&api-version=7.0", programOptions.Token));
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Building csv for {builds.value.Count} builds. Build Definition {buildtoIterate.definition.name} - {defcounter++} of {buildsToIterate.value.Count}");
                        foreach (var build in builds.value)
                        {
                            BuildArtifacts buildartifacts = JsonSerializer.Deserialize<BuildArtifacts>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/build/builds/{build.id}/artifacts?api-version=7.0", programOptions.Token));
                            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Building csv for {build.buildNumber} artifacts. Build Definition {build.definition.name}");
                            foreach (var artifact in buildartifacts.value)
                            {
                                sb.Append(projectUrl + "," + build.id + "," + build.buildNumber + "," + build.definition.name + ",");
                                sb.Append(artifact.id + "," + artifact.name + "," + artifact.resource.properties.artifactsize + ",");
                                sb.AppendLine();
                                buildcounter++;
                            }
                        }
                    }

                    programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-buildartifacts.csv");
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {buildcounter} build artifacts for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                    WriteToFile(sb, firstProject);
                }
            }

            if (!programOptions.SkipPullRequests)
            {
                List<PullRequest> allPullRequests = new ();
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null && r.isDisabled != true))
                {
                    string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch : programOptions.Branch;
                    PullRequests pullRequests = JsonSerializer.Deserialize<PullRequests>(await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, $"_apis/git/repositories/{repo.name}/pullrequests?searchCriteria.status=completed&searchCriteria.targetRefName={branchToScan}&$top={programOptions.PullRequestCount}&api-version=7.0", programOptions.Token));
                    if (pullRequests.value.Count > 0)
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved {pullRequests.value.Count} pull requests from {repo.name}");
                        allPullRequests.AddRange(pullRequests.value);
                    }
                    else
                    {
                        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Retrieved 0 pull requests from {repo.name}");
                    }
                }

                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,id,repository,targetrefname,reviewercount,mergestrategy,creationdate,closeddate,createdby,uniqueName,year,month,day,dayofweek,weekofyear,hour,totalhours,totaldays");
                }

                foreach (var pullRequest in allPullRequests)
                {
                    TimeSpan pullRequestDuration = pullRequest.closedDate - pullRequest.creationDate;
                    sb.Append(projectUrl + "," + pullRequest.pullRequestId + "," + pullRequest.repository.name + "," + pullRequest.targetRefName + "," + pullRequest.reviewers.Count + "," + pullRequest.completionOptions?.mergeStrategy + "," ?? string.Empty + ",");
                    sb.Append(pullRequest.creationDate + "," + pullRequest.closedDate + "," + StringHelper.StringToCSVCell(pullRequest.createdBy.displayName) + "," + pullRequest.createdBy.uniqueName + ",");
                    sb.Append(pullRequest.creationDate.ToLocalTime().Year + "," + pullRequest.creationDate.ToLocalTime().Month + "," + pullRequest.creationDate.ToLocalTime().Day + "," + pullRequest.creationDate.ToLocalTime().DayOfWeek + ",");
                    sb.Append(currentCulture.Calendar.GetWeekOfYear(pullRequest.creationDate.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                    sb.Append(pullRequest.creationDate.ToLocalTime().Hour + "," + pullRequestDuration.TotalHours.ToString("#0") + "," + pullRequestDuration.TotalDays.ToString("#0"));
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"{filePrefix}-pullrequests.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {allPullRequests.Count} Pull Requests for {projectName} to {programOptions.OutputFile}", ConsoleColor.Green);
                WriteToFile(sb, firstProject);
            }

            firstProject = false;
        }

        TimeSpan t = DateTime.Now - start;
        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Analysis Completed in {t.Minutes}m: {t.Seconds}s");
    }

    private static void WriteToFile(StringBuilder sb, bool firstProject)
    {
        if (firstProject)
        {
            File.WriteAllText(programOptions.OutputFile, sb.ToString());
        }
        else
        {
            File.AppendAllText(programOptions.OutputFile, sb.ToString());
        }

        sb.Clear();
    }

    private static void HandleParseError(IEnumerable<Error> errs)
    {
        foreach (var error in errs)
        {
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Error = {error.Tag}");
        }
    }
}