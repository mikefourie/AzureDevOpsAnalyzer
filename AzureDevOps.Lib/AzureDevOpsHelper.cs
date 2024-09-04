namespace AzureDevOps.Lib
{
    using System.Text;

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
    }
}
