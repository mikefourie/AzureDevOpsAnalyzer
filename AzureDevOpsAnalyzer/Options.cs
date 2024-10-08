﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Options.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace AzureDevOpsAnalyzer;

using CommandLine;

public class Options
{
    [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages", Default = true)]
    public bool Verbose { get; set; }

    [Option('c', "commitcount", Required = false, HelpText = "Set the maximum number of commits to retrieve", Default = 100000)]
    public int CommitCount { get; set; }

    [Option('d', "fromDate", Required = false, HelpText = "Set the minimum date to retrieve data for commits and pushes")]
    public string FromDate { get; set; }

    [Option('p', "pushcount", Required = false, HelpText = "Set the maximum number of commits to retrieve", Default = 100000)]
    public int PushCount { get; set; }

    [Option('b', "buildcount", Required = false, HelpText = "Set the maximum number of builds to retrieve", Default = 5000)]
    public int BuildCount { get; set; }

    [Option('g', "pullrequestcount", Required = false, HelpText = "Set the maximum number of pull requests to retrieve", Default = 5000)]
    public int PullRequestCount { get; set; }

    [Option('u', "project", Required = true, HelpText = "Set the Project Urls to scan. Comma separated")]
    public string ProjectUrl { get; set; }

    [Option('x', "collection", Required = false, HelpText = "Set the Collection Url to scan.")]
    public string CollectionUrl { get; set; }

    [Option('t', "token", Required = false, HelpText = "Set personal access token to use")]
    public string Token { get; set; }

    [Option('o', "outputfile", Required = false, HelpText = "Set name of the CSV file")]
    public string OutputFile { get; set; }

    [Option('i', "identifier", Required = false, HelpText = "Set the internal identifier to match for commits")]
    public string InternalIdentifier { get; set; }

    [Option('f', "filter", Required = false, HelpText = "Set the repository names to filter on. Comma separated regular expression")]
    public string Filter { get; set; }

    [Option('e', "exclusion", Required = false, HelpText = "Set the Filter to exclude rather than include.", Default = false)]
    public bool Exclusion { get; set; }

    [Option('n', "skipcommits", Required = false, HelpText = "Set whether to skip collecting commits", Default = false)]
    public bool SkipCommits { get; set; }

    [Option('m', "skippushes", Required = false, HelpText = "Set whether to skip collecting pushes", Default = false)]
    public bool SkipPushes { get; set; }

    [Option('s', "skipbuilds", Required = false, HelpText = "Set whether to skip collecting builds", Default = "true")]
    public string SkipBuilds { get; set; }

    [Option('w', "skipbuildartifacts", Required = false, HelpText = "Set whether to skip collecting build artifacts", Default = "true")]
    public string SkipBuildArtifacts { get; set; }

    [Option('a', "skipbase", Required = false, HelpText = "Set whether to skip collecting Base info like Teams, Area Paths etc", Default = "true")]
    public string SkipBase { get; set; }

    [Option('k', "skippullrequests", Required = false, HelpText = "Set whether to skip collecting pull requests", Default = false)]
    public bool SkipPullRequests { get; set; }

    [Option('r', "branch", Required = false, HelpText = "Set the branch to scan. Defaults to the default branch per repository for Commits and Pushes.")]
    public string Branch { get; set; }

    [Option('l', "nomessages", Required = false, HelpText = "Set whether to skip saving commit messages", Default = false)]
    public bool NoMessages { get; set; }
}