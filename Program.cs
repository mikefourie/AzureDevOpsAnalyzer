namespace AzureDevOpsAnalyzer;

using System.Globalization;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommandLine;

public class Program
{
    private static Options programOptions = new ();

    public static void Main(string[] args)
    {
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        StringBuilder sb = new ();
        Repositories repositories = new () { value = new List<Value>() };
        DateTime start = DateTime.Now;
        List<Project> allProjects = new ();
        List<Team> allTeams = new ();

        WriteHeader();
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
            ConsoleWrite($"Retrieving Projects from {programOptions.CollectionUrl}");
            string projectsJson = InvokeRestCall(programOptions.CollectionUrl, $"_apis/projects?api-version=7.0");
            if (!string.IsNullOrEmpty(projectsJson))
            {
                Projects projects = JsonSerializer.Deserialize<Projects>(projectsJson);
                if (projects.value.Count > 0)
                {
                    ConsoleWrite($"\tRetrieved {projects.value.Count} projects from {programOptions.CollectionUrl}");
                    allProjects.AddRange(projects.value);
                }
            }
            else
            {
                ConsoleWrite($"\tWARNING: Unable to retrieve projects from {programOptions.CollectionUrl}");
            }

            ConsoleWrite($"Building csv for {allProjects.Count} Projects");
            sb.Clear();
            sb.AppendLine("collectionurl,id,name");

            foreach (var project in allProjects)
            {
                sb.Append(programOptions.CollectionUrl + "," + project.id + "," + project.name);
                sb.AppendLine();
            }

            programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"projects.csv");
            ConsoleWrite($"Writing {programOptions.OutputFile}");
            File.WriteAllText(programOptions.OutputFile, sb.ToString());
            sb.Clear();

            ConsoleWrite($"Retrieving Teams from {programOptions.CollectionUrl}");
            string teamsJson = InvokeRestCall(programOptions.CollectionUrl, $"_apis/teams?api-version=7.0-preview");
            if (!string.IsNullOrEmpty(teamsJson))
            {
                Teams teams = JsonSerializer.Deserialize<Teams>(teamsJson);
                if (teams.value.Count > 0)
                {
                    ConsoleWrite($"\tRetrieved {teams.value.Count} teams from {programOptions.CollectionUrl}");
                    allTeams.AddRange(teams.value);
                }
            }
            else
            {
                ConsoleWrite($"\tWARNING: Unable to retrieve teams from {programOptions.CollectionUrl}");
            }

            ConsoleWrite($"Building csv for {allTeams.Count} Teams");
            sb.Clear();
            sb.AppendLine("collectionurl,teamid,teamname,projectName");

            foreach (var team in allTeams)
            {
                sb.Append(programOptions.CollectionUrl + "," + team.id + "," + team.name + "," + team.projectName);
                sb.AppendLine();
            }

            programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"teams.csv");
            ConsoleWrite($"Writing {programOptions.OutputFile}");
            File.WriteAllText(programOptions.OutputFile, sb.ToString());
            sb.Clear();

            // Get all team members
            bool firstTeam = true;
            foreach (var team in allTeams)
            {
                List<TeamMember> allTeamMembers = new ();
                ConsoleWrite($"Retrieving Team members for {team.name}");
                string teamMembersJson = InvokeRestCall(programOptions.CollectionUrl, $"_apis/projects/{team.projectName}/teams/{team.name}/members?api-version=7.0-preview");
                if (!string.IsNullOrEmpty(teamMembersJson))
                {
                    TeamMembers teamMembers = JsonSerializer.Deserialize<TeamMembers>(teamMembersJson);
                    if (teamMembers.value.Count > 0)
                    {
                        ConsoleWrite($"\tRetrieved {teamMembers.value.Count} team members from {team.name} in {team.projectName}");
                        allTeamMembers.AddRange(teamMembers.value);
                    }
                }
                else
                {
                    ConsoleWrite($"\tWARNING: Unable to retrieve team members from {team.name} in {team.projectName}");
                }

                ConsoleWrite($"Building csv for {team.name} Team members");
                sb.Clear();
                if (firstTeam)
                {
                    sb.AppendLine("collectionurl,projectName,teamname,isTeamAdmin,displayName,uniqueName");
                }

                foreach (var teammember in allTeamMembers)
                {
                    sb.Append(programOptions.CollectionUrl + "," + team.projectName + "," + team.name + "," + teammember.isTeamAdmin + "," + StringToCSVCell(teammember.identity.displayName) + "," + teammember.identity.uniqueName);
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"teammembers.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
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
            ConsoleWrite($"---------------- Project {projectName} ----------------");
            ConsoleWrite("Retrieving Repositories");
            Repositories allrepositories = JsonSerializer.Deserialize<Repositories>(InvokeRestCall(projectUrl, "_apis/git/repositories?api-version=7.0"));
            if (!string.IsNullOrEmpty(programOptions.Filter))
            {
                ConsoleWrite("Applying Filters");
                string[] repositoryFilters = programOptions.Filter.Split(",");
                foreach (var repo in allrepositories.value.OrderBy(r => r.name))
                {
                    // if we are excluding repos
                    if (programOptions.Exclusion)
                    {
                        bool exclude = false;

                        foreach (string filter in repositoryFilters)
                        {
                            Match m = Regex.Match(repo.name, filter, RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                // we have a match so exclude and break to save cycles
                                ConsoleWrite($"Excluding {repo.name} per filter: {filter}");
                                exclude = true;
                                break;
                            }
                        }

                        if (!exclude)
                        {
                            repositories.value.Add(repo);
                        }
                    }
                    else
                    {
                        bool include = false;

                        // we are including based on filter
                        foreach (string filter in repositoryFilters)
                        {
                            Match m = Regex.Match(repo.name, filter, RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                // we have a match so exclude and break to save cycles
                                ConsoleWrite($"Including {repo.name} per filter: {filter}");
                                include = true;
                                break;
                            }
                        }

                        if (include)
                        {
                            repositories.value.Add(repo);
                        }
                    }
                }

                repositories.count = repositories.value.Count;
            }
            else
            {
                foreach (var repo in allrepositories.value.OrderBy(r => r.name))
                {
                    repositories.value.Add(repo);
                }
            }

            Console.WriteLine("\n-------------------------------------------");
            Console.WriteLine($"{repositories.value.Count} Repositories to report on");
            Console.WriteLine("-------------------------------------------");

            foreach (var repo in repositories.value)
            {
                Console.WriteLine($"\t{repo.name}");
            }

            if (firstProject)
            {
                sb.AppendLine("projecturl,defaultBranch,id,name,project,remoteUrl,sshUrl,url,webUrl,size");
            }

            foreach (var repo in repositories.value)
            {
                sb.Append(projectUrl + "," + repo.defaultBranch + "," + repo.id + "," + repo.name + "," + repo.project.name + ",");
                sb.Append(repo.remoteUrl + "," + repo.sshUrl + "," + repo.url + "," + repo.webUrl + "," + repo.size + ",");
                sb.AppendLine();
            }

            programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-repositories.csv");
            ConsoleWrite($"Writing {programOptions.OutputFile}");
            File.AppendAllText(programOptions.OutputFile, sb.ToString());
            sb.Clear();

            List<AreaPath> allAreaPaths = new ();
            ConsoleWrite($"Retrieving Area Paths from {projectName}");

            string areapathJson = InvokeRestCall(projectUrl, $"_apis/wit/classificationnodes/areas?$depth=100&api-version=7.0");
            if (!string.IsNullOrEmpty(areapathJson))
            {
                AreaPath areaPaths = JsonSerializer.Deserialize<AreaPath>(areapathJson);

                ConsoleWrite($"Building csv for AreaPaths");
                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,areapath,name");
                }

                sb.AppendLine(projectUrl + "," + areaPaths.path.Replace($"\\{projectName}\\Area", projectName) + "," + areaPaths.name);

                if (areaPaths.children.Count > 0)
                {
                    foreach (var path in areaPaths.children)
                    {
                        AppendChildAreaPaths(projectUrl, path, sb, projectName);
                    }
                }
                else
                {
                    sb.AppendLine(projectUrl + "," + areaPaths.path + "," + areaPaths.name);
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-areapaths.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());
                sb.Clear();
            }

            // Get all team area paths
            bool firstTeamAreaPath = true;
            foreach (var team in allTeams)
            {
                List<TeamAreaPathConfig> allTeamAreaPathConfig = new ();
                ConsoleWrite($"Retrieving Area Paths for {team.name}");
                string teamAreaPathsJson = InvokeRestCall(projectUrl, $"{team.name}/_apis/work/teamsettings/teamfieldvalues?api-version=7.0");

                ConsoleWrite($"Building csv for {team.name} Team area paths");
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
                    ConsoleWrite($"\tWARNING: Unable to retrieve team area paths from {team.name} in {team.projectName}");
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-teamareapaths.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                if (firstTeamAreaPath)
                {
                    File.WriteAllText(programOptions.OutputFile, sb.ToString());
                }
                else
                {
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                }

                sb.Clear();
                firstTeamAreaPath = false;
            }

            if (!programOptions.SkipCommits)
            {
                List<Commit> allCommits = new ();
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null).OrderBy(r => r.name))
                {
                    string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch.Replace("refs/heads/", string.Empty) : programOptions.Branch;
                    ConsoleWrite($"Retrieving Commits from {repo.name} ({branchToScan})");
                    try
                    {
                        string commitJson = InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/commits?searchCriteria.$top={programOptions.CommitCount}&searchCriteria.itemVersion.version={branchToScan}&searchCriteria.fromDate={programOptions.FromDate}&api-version=7.0");
                        if (!string.IsNullOrEmpty(commitJson))
                        {
                            CommitHistory commitHistory = JsonSerializer.Deserialize<CommitHistory>(commitJson);
                            if (commitHistory.value.Count > 0)
                            {
                                foreach (Commit item in commitHistory.value)
                                {
                                    item.branch = branchToScan;
                                }

                                allCommits.AddRange(commitHistory.value);
                                ConsoleWrite($"\tRetrieved {commitHistory.value.Count} from {repo.name}");
                            }
                        }
                        else
                        {
                            ConsoleWrite($"\tWARNING: Unable to retrieve commit history from {repo.name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleWrite($"\t{branchToScan} not found {ex}");
                    }
                }

                ConsoleWrite($"Building csv for {allCommits.Count} commits");
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

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-commits.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());
                sb.Clear();
            }

            if (!programOptions.SkipPushes)
            {
                List<Push> allPushes = new ();
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null).OrderBy(r => r.name))
                {
                    string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch : $"refs/heads/{programOptions.Branch}";
                    ConsoleWrite($"Retrieving Pushes from {repo.name} ({branchToScan})");
                    string pushesJson = InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/pushes?$top={programOptions.PushCount}&searchCriteria.refName={branchToScan}&searchCriteria.fromDate={programOptions.FromDate}&api-version=7.0");
                    if (!string.IsNullOrEmpty(pushesJson))
                    {
                        Pushes pushes = JsonSerializer.Deserialize<Pushes>(pushesJson);
                        if (pushes.value.Count > 0)
                        {
                            foreach (Push item in pushes.value)
                            {
                                item.branch = branchToScan;
                            }

                            allPushes.AddRange(pushes.value);
                            ConsoleWrite($"\tRetrieved {pushes.value.Count} pushes from {repo.name}");
                        }
                    }
                    else
                    {
                        ConsoleWrite($"\tWARNING: Unable to retrieve pushes from {repo.name}");
                    }
                }

                ConsoleWrite($"Building csv for {allPushes.Count} pushes");
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
                    sb.Append(push.date.ToLocalTime().Hour + "," + StringToCSVCell(push.pushedBy.uniqueName) + "," + StringToCSVCell(push.pushedBy.displayName) + "\"," + push.repository.remoteUrl + ",");
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-pushes.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());
                sb.Clear();
            }

            if (!Convert.ToBoolean(programOptions.SkipBuilds))
            {
                List<Build> allBuilds = new ();
                ConsoleWrite($"Retrieving top {programOptions.BuildCount} Builds from {projectName}");
                Builds builds = JsonSerializer.Deserialize<Builds>(InvokeRestCall(projectUrl, $"_apis/build/builds/?$top={programOptions.BuildCount}&minTime={programOptions.FromDate}&api-version=7.0"));
                ConsoleWrite($"\tRetrieved {builds.value.Count}");
                allBuilds.AddRange(builds.value);
                ConsoleWrite($"Building csv for {allBuilds.Count} builds");
                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,id,reason,buildNumber,definition,result,requestedfor,uniqueName,repository,starttime,year,month,day,dayofweek,weekofyear,hour,finishtime,queuetime,totalminutes");
                }

                foreach (var build in allBuilds)
                {
                    TimeSpan buildDuration = build.finishTime - build.startTime;
                    sb.Append(projectUrl + "," + build.id + "," + build.reason + "," + build.buildNumber + "," + build.definition.name + "," + build.result + ",");
                    sb.Append(StringToCSVCell(build.requestedFor.displayName) + "," + StringToCSVCell(build.requestedFor.uniqueName) + "," + build.repository.name + "," + build.startTime.ToLocalTime() + "," + build.startTime.ToLocalTime().Year + ",");
                    sb.Append(build.startTime.ToLocalTime().Month + "," + build.startTime.ToLocalTime().Day + "," + build.startTime.ToLocalTime().DayOfWeek + ",");
                    sb.Append(currentCulture.Calendar.GetWeekOfYear(build.startTime.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                    sb.Append(build.startTime.ToLocalTime().Hour + "," + build.finishTime.ToLocalTime() + "," + build.queueTime.ToLocalTime() + "," + buildDuration.TotalMinutes.ToString("##") + ",");
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-builds.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());
                sb.Clear();
            }

            if (!programOptions.SkipPullRequests)
            {
                List<PullRequest> allPullRequests = new ();
                ConsoleWrite($"Retrieving Pull Requests from {projectName}");
                foreach (var repo in repositories.value.Where(r => r.defaultBranch != null).OrderBy(r => r.name))
                {
                    string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch : programOptions.Branch;
                    string pullRequestJson = InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/pullrequests?searchCriteria.status=completed&searchCriteria.targetRefName={branchToScan}&$top={programOptions.PullRequestCount}&api-version=7.0");
                    if (!string.IsNullOrEmpty(pullRequestJson))
                    {
                        PullRequests pullRequests = JsonSerializer.Deserialize<PullRequests>(pullRequestJson);
                        if (pullRequests.value.Count > 0)
                        {
                            ConsoleWrite($"\tRetrieved {pullRequests.value.Count} pull requests from {repo.name}");
                            allPullRequests.AddRange(pullRequests.value);
                        }
                    }
                    else
                    {
                        ConsoleWrite($"\tWARNING: Unable to retrieve pull requests from {repo.name}");
                    }
                }

                ConsoleWrite($"Building csv for {allPullRequests.Count} Pull Requests");
                sb.Clear();
                if (firstProject)
                {
                    sb.AppendLine("projecturl,id,repository,targetrefname,reviewercount,mergestrategy,creationdate,closeddate,createdby,uniqueName,year,month,day,dayofweek,weekofyear,hour,totalhours,totaldays");
                }

                foreach (var pullRequest in allPullRequests)
                {
                    TimeSpan pullRequestDuration = pullRequest.closedDate - pullRequest.creationDate;
                    sb.Append(projectUrl + "," + pullRequest.pullRequestId + "," + pullRequest.repository.name + "," + pullRequest.targetRefName + "," + pullRequest.reviewers.Count + "," + pullRequest.completionOptions?.mergeStrategy + "," ?? string.Empty + ",");
                    sb.Append(pullRequest.creationDate + "," + pullRequest.closedDate + "," + StringToCSVCell(pullRequest.createdBy.displayName) + "," + pullRequest.createdBy.uniqueName + ",");
                    sb.Append(pullRequest.creationDate.ToLocalTime().Year + "," + pullRequest.creationDate.ToLocalTime().Month + "," + pullRequest.creationDate.ToLocalTime().Day + "," + pullRequest.creationDate.ToLocalTime().DayOfWeek + ",");
                    sb.Append(currentCulture.Calendar.GetWeekOfYear(pullRequest.creationDate.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                    sb.Append(pullRequest.creationDate.ToLocalTime().Hour + "," + pullRequestDuration.TotalHours.ToString("#0") + "," + pullRequestDuration.TotalDays.ToString("#0"));
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-pullrequests.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());
                sb.Clear();
            }

            firstProject = false;
        }

        TimeSpan t = DateTime.Now - start;
        ConsoleWrite($"Analysis Completed {t.TotalMinutes}m: {t.Seconds}s");
    }

    /// <summary>
    /// Turn a string into a CSV cell output.
    /// </summary>
    private static string StringToCSVCell(string str)
    {
        bool mustQuote = str.Contains(',') || str.Contains('"') || str.Contains('\r') || str.Contains('\n');
        if (mustQuote)
        {
            StringBuilder sb = new ();
            sb.Append('"');
            foreach (char nextChar in str)
            {
                sb.Append(nextChar);
                if (nextChar == '"')
                {
                    sb.Append('"');
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        return str;
    }

    private static void AppendChildAreaPaths(string projectUrl, Child path, StringBuilder sb, string projectName)
    {
        if (path.children.Count > 0)
        {
            foreach (var child in path.children)
            {
                sb.AppendLine(projectUrl + "," + child.path.Replace($"\\{projectName}\\Area", projectName) + "," + child.name);
                if (child.hasChildren)
                {
                    AppendChildAreaPaths(projectUrl, child, sb, projectName);
                }
            }
        }
        else
        {
            sb.AppendLine(projectUrl + "," + path.path.Replace($"\\{projectName}\\Area", projectName) + "," + path.name);
        }
    }

    private static void HandleParseError(IEnumerable<Error> errs)
    {
        foreach (var error in errs)
        {
            ConsoleWrite($"Error = {error.Tag}");
        }
    }

    private static void WriteHeader()
    {
        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine("    AzureDevOpsAnalyzer");
        Console.WriteLine("----------------------------------------------------------------------\n");
    }

    private static void ConsoleWrite(string message) => Console.WriteLine(programOptions.Verbose ? $"{DateTime.Now} {message}" : $"{message}");

    private static string InvokeRestCall(string baseaddress, string url)
    {
        using (HttpClient client = new ())
        {
            string creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{programOptions.Token}"));
            client.BaseAddress = new Uri($"{baseaddress}/");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
            var response = client.GetAsync(url).Result;
            string content = response.Content.ReadAsStringAsync().Result;
            return response.IsSuccessStatusCode ? content : null;
        }
    }
}