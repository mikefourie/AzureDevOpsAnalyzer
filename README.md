# AzureDevOpsAnalyzer

[![Build Status](https://github.com/mikefourie/AzureDevOpsAnalyzer/workflows/.NET/badge.svg)](https://github.com/mikefourie/AzureDevOpsAnalyzer/actions)

e.g. AzureDevOpsAnalyzer.exe -t YOURPAT -u https://dev.azure.com/YOURORG/YOURPROJECT

  -v, --verbose             (Default: true) Set output to verbose messages

  -c, --commitcount         (Default: 100000) Set the maximum number of commits to retrieve

  -d, --fromDate            Set the minimum date to retrieve data for commits and pushes

  -p, --pushcount           (Default: 100000) Set the maximum number of commits to retrieve

  -b, --buildcount          (Default: 5000) Set the maximum number of builds to retrieve

  -g, --pullrequestcount    (Default: 5000) Set the maximum number of pull requests to retrieve

  -u, --project             Required. Set the Project Urls to scan. Comma separated

  -t, --token               Set personal access token to use

  -o, --outputfile          Set name of the CSV file

  -i, --identifier          Set the internal identifier to match for commits

  -f, --filter              Set the repository names to filter on. Comma separated regular expression

  -e, --exclusion           (Default: false) Set the Filter to exclude rather than include.

  -n, --skipcommits         (Default: false) Set whether to skip collecting commits

  -m, --skippushes          (Default: false) Set whether to skip collecting pushes

  -s, --skipbuilds          (Default: true) Set whether to skip collecting builds

  -k, --skippullrequests    (Default: false) Set whether to skip collecting pull requests

  -r, --branch              Set the branch to scan. Defaults to the default branch per repository for Commits and
                            Pushes.

  -l, --nomessages          (Default: false) Set whether to skip saving commit messages

  --help                    Display this help screen.

  --version                 Display version information.
