﻿namespace AzureDevOpsAnalyzer
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
                // Help text requested, or parsing failed.
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
                        foreach (string filter in repositoryExclusions)
                        {
                            Match m = Regex.Match(repo.name, filter, RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                if (programOptions.Exclusion)
                                {
                                    ConsoleWrite($"Excluding {repo.name} per filter: {filter}");
                                    exclude = true;
                                    break;
                                }
                                else
                                {
                                    ConsoleWrite($"Including {repo.name} per filter: {filter}");
                                    exclude = false;
                                    break;
                                }
                            }
                            else
                            {
                                if (programOptions.Exclusion)
                                {
                                    ConsoleWrite($"Including {repo.name} per filter: {filter}");
                                    exclude = false;
                                    break;
                                }
                                else
                                {
                                    ConsoleWrite($"Excluding {repo.name} per filter: {filter}");
                                    exclude = true;
                                    break;
                                }
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
                    sb.Append(projectUrl + "," + repo.defaultBranch + "," + repo.id + "," + repo.name + "," + repo.project + ",");
                    sb.Append(repo.remoteUrl + "," + repo.sshUrl + "," + repo.url + "," + repo.webUrl + ",");
                    sb.AppendLine();
                }

                programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-repositories.csv");
                ConsoleWrite($"Writing {programOptions.OutputFile}");
                File.AppendAllText(programOptions.OutputFile, sb.ToString());
                sb.Clear();
                if (!programOptions.SkipCommits)
                {
                    List<Commit> allCommits = new ();
                    foreach (var repo in repositories.value.Where(r => r.defaultBranch != null).OrderBy(r => r.name))
                    {
                        string branchToScan = string.IsNullOrEmpty(programOptions.Branch) ? repo.defaultBranch.Replace("refs/heads/", string.Empty) : programOptions.Branch;
                        ConsoleWrite($"Retrieving Commits from {repo.name} ({branchToScan})");
                        try
                        {
                            CommitHistory commitHistory = JsonSerializer.Deserialize<CommitHistory>(InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/commits?searchCriteria.$top={programOptions.CommitCount}&searchCriteria.itemVersion.version={branchToScan}&searchCriteria.fromDate={programOptions.FromDate}&api-version=6.0"));
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
                        Pushes pushes = JsonSerializer.Deserialize<Pushes>(InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/pushes?$top={programOptions.PushCount}&searchCriteria.refName={branchToScan}&searchCriteria.fromDate={programOptions.FromDate}&api-version=6.0"));
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
                        sb.Append(push.date.ToLocalTime().Hour + ",\"" + push.pushedBy.uniqueName + "\",\"" + push.pushedBy.displayName + "\"," + push.repository.remoteUrl + ",");
                        sb.AppendLine();
                    }

                    programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", $"{filePrefix}-pushes.csv");
                    ConsoleWrite($"Writing {programOptions.OutputFile}");
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                    sb.Clear();
                }

                if (!programOptions.SkipBuilds)
                {
                    List<Build> allBuilds = new ();
                    ConsoleWrite($"Retrieving Builds from {projectName}");
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
                        sb.Append(projectUrl + "," + build.id + "," + build.reason + "," + build.buildNumber + "," + build.definition.name + "," + build.result + ",");
                        sb.Append(build.requestedFor.displayName + "," + build.repository.name + "," + build.startTime.ToLocalTime() + "," + build.startTime.ToLocalTime().Year + ",");
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
                        PullRequests pullRequests = JsonSerializer.Deserialize<PullRequests>(InvokeRestCall(projectUrl, $"_apis/git/repositories/{repo.name}/pullrequests?searchCriteria.status=completed&searchCriteria.targetRefName={branchToScan}&$top={programOptions.PullRequestCount}&api-version=6.0"));
                        if (pullRequests.value.Count > 0)
                        {
                            ConsoleWrite($"\tRetrieved {pullRequests.value.Count} pull requests from {repo.name}");
                            allPullRequests.AddRange(pullRequests.value);
                        }
                    }

                    ConsoleWrite($"Building csv for {allPullRequests.Count} Pull Requests");
                    sb.Clear();
                    if (firstProject)
                    {
                        sb.AppendLine("projecturl,id,repository,targetrefname,reviewercount,mergestrategy,creationdate,closeddate,createdby,year,month,day,dayofweek,weekofyear,hour,totalhours,totaldays");
                    }

                    foreach (var pullRequest in allPullRequests)
                    {
                        TimeSpan pullRequestDuration = pullRequest.closedDate - pullRequest.creationDate;
                        sb.Append(projectUrl + "," + pullRequest.pullRequestId + "," + pullRequest.repository.name + "," + pullRequest.targetRefName + "," + pullRequest.reviewers.Count + "," + pullRequest.completionOptions?.mergeStrategy + "," ?? string.Empty + ",");
                        sb.Append(pullRequest.creationDate + "," + pullRequest.closedDate + "," + pullRequest.createdBy.displayName + ",");
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