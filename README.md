# AzureDevOpsAnalyzer

e.g. AzureDevOpsAnalyzer.exe -t YOURPAT -u https://dev.azure.com/YOURORG/YOURPROJECT

  -v, --verbose        (Default: true) Set output to verbose messages. Default is true

  -c, --commitcount    (Default: 100000) Set the maximum number of commits to retrieve

  -p, --pushcount      (Default: 100000) Set the maximum number of commits to retrieve

  -b, --buildcount     (Default: 5000) Set the maximum number of builds to retrieve

  -u, --project        Required. Set the Project Urls to scan. Comma separated

  -t, --token          Set personal access token to use

  -o, --outputfile     Set name of the CSV file

  -i, --identifier     Set the internal identifier to match for commits

  -f, --filter         Set the repository names to filter out. Comma separated regular expression

  -n, --skipcommits    (Default: false) Set whether to skip collecting commits. Default is false

  -m, --skippushes     (Default: false) Set whether to skip collecting pushes. Default is false

  -s, --skipbuilds     (Default: false) Set whether to skip collecting builds. Default is false

  --help               Display this help screen.

  --version            Display version information.