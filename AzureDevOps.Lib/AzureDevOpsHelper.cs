namespace AzureDevOps.Lib;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Helper.Lib;

public class AzureDevOpsHelper
{
    public static void AppendChildAreaPaths(string projectUrl, Child path, StringBuilder sb, string projectName)
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

    public static async Task<Repositories> GetRepositoriesAsync(IHttpClientFactory httpClientFactory, string projectUrl, string apiUrl, string filter, bool excludeFilter, string token)
    {
        Repositories repositories = new () { value = new List<Value>() };
        string json = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, apiUrl, token);
        Repositories allrepositories = JsonSerializer.Deserialize<Repositories>(json);
        if (!string.IsNullOrEmpty(filter))
        {
            string[] repositoryFilters = filter.Split(",");
            foreach (var repo in allrepositories.value.OrderBy(r => r.name))
            {
                // if we are excluding repos
                if (excludeFilter)
                {
                    bool exclude = false;
                    foreach (string f in repositoryFilters)
                    {
                        Match m = Regex.Match(repo.name, f, RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            // we have a match so exclude and break to save cycles
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
                    // we are including based on filter
                    foreach (string f in repositoryFilters)
                    {
                        Match m = Regex.Match(repo.name, filter, RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            // we have a match so include and break to save cycles
                            //  ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Including {repo.name} per filter: {f}");
                            repositories.value.Add(repo);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            repositories.value.AddRange(allrepositories.value.OrderBy(r => r.name));
        }

        repositories.count = repositories.value.Count;
        return repositories;
    }
}