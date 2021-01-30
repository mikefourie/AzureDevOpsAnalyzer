namespace AzureDevOpsAnalyzer
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
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
            Repositories allrepositories = new ();
            Repositories repositories = new ();
            DateTime start = DateTime.Now;

            WriteHeader();
            var result = Parser.Default.ParseArguments<Options>(args)
                   .WithParsed(o =>
                   {
                       programOptions = o;
                   });

            if (result.Tag == ParserResultType.NotParsed)
            {
                // Help text requested, or parsing failed. Exit.
                return;
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
                allrepositories = JsonSerializer.Deserialize<Repositories>(InvokeRestCall(projectUrl, "_apis/git/repositories?api-version=6.0"));
                if (!string.IsNullOrEmpty(programOptions.Filter))
                {
                    ConsoleWrite("Applying Filters");
                    string[] repositoryExclusions = programOptions.Filter.Split(",");
                    repositories.value = new List<Value>();

                    foreach (var repo in allrepositories.value.OrderBy(r => r.name))
                    {
                        bool exclude = false;
                        foreach (string exclusion in repositoryExclusions)
                        {
                            Match m = Regex.Match(repo.name, exclusion, RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                ConsoleWrite($"Removing {repo.name} per filter: {exclusion}");
                                exclude = true;
                                break;
                            }
                        }

                        if (!exclude)
                        {
                            repositories.value.Add(repo);
                        }
                    }

                    repositories.count = repositories.value.Count;
                }
                else
                {
                    repositories = allrepositories;
                }

                ConsoleWrite($"Retrieved {repositories.count}");
                if (firstProject)
                {
                    sb.AppendLine("projecturl,defaultBranch,id,name,project,remoteUrl,sshUrl,url,webUrl");
                }

                foreach (var repo in repositories.value)
                {
                    sb.Append(projectUrl + ",");
                    sb.Append(repo.defaultBranch + ",");
                    sb.Append(repo.id + ",");
                    sb.Append(repo.name + ",");
                    sb.Append(repo.project + ",");
                    sb.Append(repo.remoteUrl + ",");
                    sb.Append(repo.sshUrl + ",");
                    sb.Append(repo.url + ",");
                    sb.Append(repo.webUrl + ",");
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-repositories.csv");

                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());

                if (!programOptions.SkipCommits)
                {
                    List<Commit> allCommits = new ();
                    foreach (var repo in repositories.value.Where(r => r.defaultBranch != null).OrderBy(r => r.name))
                    {
                        ConsoleWrite($"Retrieving Commits {repo.name}");
                        CommitHistory commitHistory = JsonSerializer.Deserialize<CommitHistory>(InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/commits?searchCriteria.$top={programOptions.CommitCount}&searchCriteria.itemVersion.version={repo.defaultBranch.Replace("refs/heads/", string.Empty)}&api-version=6.0"));
                        allCommits.AddRange(commitHistory.value);
                    }

                    ConsoleWrite($"Building csv for {allCommits.Count} commits");
                    sb.Clear();
                    if (firstProject)
                    {
                        sb.AppendLine("projecturl,repository, isinternal, authordate,authoremail,authorname,add,delete,edit,commitid,committerdate,year,month,day,dayofweek,weekofyear,hour,committeremail,committername,remoteurl,comment");
                    }

                    foreach (var commit in allCommits)
                    {
                        sb.Append(projectUrl + ",");
                        string[] urlParts = commit.remoteUrl.Split("/");
                        sb.Append(urlParts[6] + ",");
                        if (string.IsNullOrEmpty(programOptions.InternalIdentifier))
                        {
                            sb.Append("true,");
                        }
                        else
                        {
                            sb.Append(currentCulture.CompareInfo.IndexOf(commit.committer.email, programOptions.InternalIdentifier, CompareOptions.IgnoreCase) >= 0 ? true + "," : false + ",");
                        }

                        sb.Append(commit.author.date.ToLocalTime() + ",");
                        sb.Append(commit.author.email + ",");
                        sb.Append(commit.author.name + ",");
                        sb.Append(commit.changeCounts.Add + ",");
                        sb.Append(commit.changeCounts.Delete + ",");
                        sb.Append(commit.changeCounts.Edit + ",");
                        sb.Append(commit.commitId + ",");
                        sb.Append(commit.committer.date.ToLocalTime() + ",");
                        sb.Append(commit.committer.date.ToLocalTime().Year + ",");
                        sb.Append(commit.committer.date.ToLocalTime().Month + ",");
                        sb.Append(commit.committer.date.ToLocalTime().Day + ",");
                        sb.Append(commit.committer.date.ToLocalTime().DayOfWeek + ",");
                        sb.Append(currentCulture.Calendar.GetWeekOfYear(commit.committer.date.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                        sb.Append(commit.committer.date.ToLocalTime().Hour + ",");
                        sb.Append(commit.committer.email + ",");
                        sb.Append(commit.committer.name + ",");
                        sb.Append(commit.remoteUrl + ",");
                        sb.Append($"\"{commit.comment}\"" + ",");
                        sb.AppendLine();
                    }

                    programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-commits.csv");
                    ConsoleWrite($"Writing {programOptions.OutputFile}");
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                }

                if (!programOptions.SkipPushes)
                {
                    List<Push> allPushes = new ();
                    foreach (var repo in repositories.value.Where(r => r.defaultBranch != null).OrderBy(r => r.name))
                    {
                        ConsoleWrite($"Retrieving Pushes {repo.name}");
                        Pushes pushes = JsonSerializer.Deserialize<Pushes>(InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/pushes?$top={programOptions.PushCount}&searchCriteria.refName={repo.defaultBranch}&api-version=6.0"));
                        ConsoleWrite($"\tRetrieved {pushes.value.Count}");
                        allPushes.AddRange(pushes.value);
                    }

                    ConsoleWrite($"Building csv for {allPushes.Count} pushes");
                    sb.Clear();
                    if (firstProject)
                    {
                        sb.AppendLine("projecturl,repository,pushid,pushdate,year,month,day,dayofweek,weekofyear,hour,uniquename,displayname,remoteurl");
                    }

                    foreach (var push in allPushes)
                    {
                        sb.Append(projectUrl + ",");
                        sb.Append(push.repository.name + ",");
                        sb.Append(push.pushId + ",");
                        sb.Append(push.date.ToLocalTime() + ",");
                        sb.Append(push.date.ToLocalTime().Year + ",");
                        sb.Append(push.date.ToLocalTime().Month + ",");
                        sb.Append(push.date.ToLocalTime().Day + ",");
                        sb.Append(push.date.ToLocalTime().DayOfWeek + ",");
                        sb.Append(currentCulture.Calendar.GetWeekOfYear(push.date.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                        sb.Append(push.date.ToLocalTime().Hour + ",");
                        sb.Append(push.pushedBy.uniqueName + ",");
                        sb.Append(push.pushedBy.displayName + ",");
                        sb.Append(push.repository.remoteUrl + ",");
                        sb.AppendLine();
                    }

                    programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-pushes.csv");
                    ConsoleWrite($"Writing {programOptions.OutputFile}");
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                }

                if (!programOptions.SkipBuilds)
                {
                    List<Build> allBuilds = new ();
                    ConsoleWrite($"Retrieving Builds {projectName}");
                    Builds builds = JsonSerializer.Deserialize<Builds>(InvokeRestCall(projectUrl, $"_apis/build/builds/?$top={programOptions.BuildCount}&api-version=6.0"));
                    ConsoleWrite($"\tRetrieved {builds.value.Count}");
                    allBuilds.AddRange(builds.value);

                    ConsoleWrite($"Building csv for {allBuilds.Count} builds");
                    sb.Clear();
                    if (firstProject)
                    {
                        sb.AppendLine("projecturl,id,reason,buildNumber,definition,result,requestedfor,repository,starttime,year,month,day,dayofweek,weekofyear,hour,finishtime,queuetime,totalminutes");
                    }

                    foreach (var build in allBuilds)
                    {
                        TimeSpan buildDuration = build.finishTime - build.startTime;
                        sb.Append(projectUrl + ",");
                        sb.Append(build.id + ",");
                        sb.Append(build.reason + ",");
                        sb.Append(build.buildNumber + ",");
                        sb.Append(build.definition.name + ",");
                        sb.Append(build.result + ",");
                        sb.Append(build.requestedFor.displayName + ",");
                        sb.Append(build.repository.name + ",");
                        sb.Append(build.startTime.ToLocalTime() + ",");
                        sb.Append(build.startTime.ToLocalTime().Year + ",");
                        sb.Append(build.startTime.ToLocalTime().Month + ",");
                        sb.Append(build.startTime.ToLocalTime().Day + ",");
                        sb.Append(build.startTime.ToLocalTime().DayOfWeek + ",");
                        sb.Append(currentCulture.Calendar.GetWeekOfYear(build.startTime.ToLocalTime(), currentCulture.DateTimeFormat.CalendarWeekRule, currentCulture.DateTimeFormat.FirstDayOfWeek) + ",");
                        sb.Append(build.startTime.ToLocalTime().Hour + ",");
                        sb.Append(build.finishTime.ToLocalTime() + ",");
                        sb.Append(build.queueTime.ToLocalTime() + ",");
                        sb.Append(buildDuration.TotalMinutes.ToString("##") + ",");
                        sb.AppendLine();
                    }

                    programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-builds.csv");
                    ConsoleWrite($"Writing {programOptions.OutputFile}");
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                }

                firstProject = false;
            }

            TimeSpan t = DateTime.Now - start;
            ConsoleWrite($"Analysis Completed {t.TotalSeconds}s:{t.Milliseconds}ms");
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
                string creds = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{programOptions.Token}"));
                client.BaseAddress = new Uri($"{baseaddress}/");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
                var response = client.GetAsync(url).Result;
                string content = response.Content.ReadAsStringAsync().Result;
                return response.IsSuccessStatusCode ? content : null;
            }
        }
    }
}